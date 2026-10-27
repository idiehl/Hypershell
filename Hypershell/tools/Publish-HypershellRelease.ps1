[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $ReleaseNotesFile,

    [Parameter(Mandatory)]
    [string] $CertificateThumbprint,

    [ValidatePattern('^[a-z][a-z0-9-]{1,31}$')]
    [string] $Channel = 'stable',

    [string] $MinimumWindowsVersion = '10.0.17763',

    [uri] $TimestampServer = 'http://timestamp.digicert.com',

    [string] $OutputDirectory,

    [ValidateSet('Core', 'Complete')]
    [string] $Edition = 'Core',

    [string] $SuitePayloadDirectory,
    [string] $WebView2InstallerPath,
    [string] $WebView2InstallerSha256,
    [string] $DotNetSdkInstallerPath,
    [string] $DotNetSdkInstallerSha256,
    [ValidatePattern('^10\.[0-9]+\.[0-9]+$')]
    [string] $DotNetSdkVersion = '10.0.100',

    [switch] $AllowDirty,

    [switch] $SkipVerification,

    [switch] $AllowUntrustedDiagnosticCertificate
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$notesPath = [IO.Path]::GetFullPath($ReleaseNotesFile)
if (-not [IO.File]::Exists($notesPath)) {
    throw "The release-notes file does not exist: $notesPath"
}
if ((Get-Item -LiteralPath $notesPath).Length -gt 32768) {
    throw 'Release notes exceed the 32 KiB manifest limit.'
}

$normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').Replace(':', '').ToUpperInvariant()
if ($normalizedThumbprint.Length -lt 40 -or $normalizedThumbprint -notmatch '^[0-9A-F]+$') {
    throw 'The code-signing certificate thumbprint is malformed.'
}
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$normalizedThumbprint" -ErrorAction SilentlyContinue
if ($null -eq $certificate -or -not $certificate.HasPrivateKey) {
    throw 'The requested CurrentUser code-signing certificate with its private key is not installed.'
}
if (($AllowDirty -or $SkipVerification) -and
    $Channel -ne 'diagnostic') {
    throw 'Dirty or unverified builds are allowed only on the diagnostic channel.'
}
if ($AllowUntrustedDiagnosticCertificate) {
    if (-not $AllowDirty -or $Channel -ne 'diagnostic') {
        throw 'An untrusted diagnostic certificate requires -AllowDirty and the diagnostic channel.'
    }
    if (-not $certificate.Subject.StartsWith(
            'CN=Hypershell Diagnostic Release ',
            [StringComparison]::Ordinal) -or
        $certificate.Subject -ne $certificate.Issuer -or
        $certificate.NotAfter.ToUniversalTime() -gt
            [DateTime]::UtcNow.AddHours(48)) {
        throw 'The untrusted certificate is not an eligible short-lived Hypershell diagnostic signer.'
    }
}
$timestampServerUri = $TimestampServer.AbsoluteUri

if ($Edition -eq 'Complete') {
    if ($Channel -ne 'stable') { throw 'Complete release publication requires the stable channel.' }
    & (Join-Path $PSScriptRoot 'Test-HypershellSuitePayload.ps1') -PayloadDirectory $SuitePayloadDirectory -ExpectedVersion $Version
    foreach ($prerequisite in @(
        @{ Path = $WebView2InstallerPath; Hash = $WebView2InstallerSha256 },
        @{ Path = $DotNetSdkInstallerPath; Hash = $DotNetSdkInstallerSha256 }
    )) {
        if ($prerequisite.Hash -notmatch '^[a-fA-F0-9]{64}$' -or -not (Test-Path -LiteralPath $prerequisite.Path -PathType Leaf)) {
            throw 'Complete requires offline prerequisite installers and their explicitly pinned SHA-256 hashes.'
        }
        if ((Get-FileHash -LiteralPath $prerequisite.Path -Algorithm SHA256).Hash -ine $prerequisite.Hash) { throw 'Prerequisite SHA-256 mismatch.' }
        $prerequisiteSignature = Get-AuthenticodeSignature -LiteralPath $prerequisite.Path
        if ($prerequisiteSignature.Status -ne 'Valid' -or $prerequisiteSignature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
            throw 'Prerequisite installers must have a valid Microsoft Authenticode signature.'
        }
    }
    $sdkInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo([IO.Path]::GetFullPath($DotNetSdkInstallerPath))
    $sdkDescription = $sdkInfo.ProductName + ' ' + $sdkInfo.FileDescription
    if ($sdkDescription -notmatch '(?i)\.NET.*SDK' -or $sdkDescription -notmatch '(?i)x64' -or
        $sdkInfo.ProductVersion -notmatch ('^' + [Regex]::Escape($DotNetSdkVersion) + '(?:[. +\-]|$)')) {
        throw 'The SDK installer signed product metadata must identify the requested .NET SDK version and x64 architecture.'
    }
    $webViewInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo([IO.Path]::GetFullPath($WebView2InstallerPath))
    if (($webViewInfo.ProductName + ' ' + $webViewInfo.FileDescription) -notmatch '(?i)WebView2' -or
        [IO.Path]::GetFileName($WebView2InstallerPath) -ine 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe' -or
        (Get-Item -LiteralPath $WebView2InstallerPath).Length -lt 10MB) {
        throw 'Use the Microsoft WebView2 standalone x64 runtime installer, not the small online bootstrapper.'
    }
}

function Find-SignTool {
    $available = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $available) {
        return $available.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if ([IO.Directory]::Exists($kitsRoot)) {
        $candidate = Get-ChildItem -LiteralPath $kitsRoot -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($null -ne $candidate) {
            return $candidate
        }
    }

    throw 'SignTool was not found. Install the Windows SDK signing tools before producing a stable release.'
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath"
    }
}

function Invoke-ArtifactSigning {
    param([Parameter(Mandatory)][string] $Path)

    Invoke-Checked -FilePath $signTool -Arguments @(
        'sign',
        '/fd', 'SHA256',
        '/td', 'SHA256',
        '/tr', $timestampServerUri,
        '/sha1', $normalizedThumbprint,
        $Path)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $acceptedStatuses = if ($AllowUntrustedDiagnosticCertificate) {
        @('Valid', 'NotTrusted', 'UnknownError')
    }
    else {
        @('Valid')
    }
    if ($signature.Status.ToString() -notin $acceptedStatuses -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint -ne $normalizedThumbprint) {
        throw "Authenticode verification failed after signing: $Path ($($signature.Status))"
    }
}

$releaseRoot = $null
$scratchRoot = $null
Push-Location $projectRoot
try {
    $gitCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $gitCommit -notmatch '^[0-9a-fA-F]{40,64}$') {
        throw 'The release must be built from a valid Git commit.'
    }
    $worktreeChanges = @(& git status --porcelain --untracked-files=all)
    if (-not $AllowDirty -and $worktreeChanges.Count -gt 0) {
        throw 'The release worktree is not clean. Commit the intended source or use -AllowDirty for a non-production diagnostic build.'
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $projectRoot "release\$Version"
    }
    $releaseRoot = [IO.Path]::GetFullPath($OutputDirectory)
    if ($AllowUntrustedDiagnosticCertificate) {
        $diagnosticParent = ([IO.Path]::GetFullPath(
                (Join-Path $projectRoot 'output\release-pipeline-test'))).TrimEnd(
                [IO.Path]::DirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar
        if (-not $releaseRoot.StartsWith(
                $diagnosticParent,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'An untrusted diagnostic build must remain beneath output\release-pipeline-test.'
        }
    }
    if ([IO.Directory]::Exists($releaseRoot) -or [IO.File]::Exists($releaseRoot)) {
        throw "The release output already exists and will not be overwritten: $releaseRoot"
    }

    $scratchRoot = Join-Path $projectRoot "output\release-build\$([Guid]::NewGuid().ToString('N'))"
    $publishRoot = Join-Path $scratchRoot 'publish\win-x64'
    $installerRoot = Join-Path $scratchRoot 'installer'
    [IO.Directory]::CreateDirectory($publishRoot) | Out-Null
    [IO.Directory]::CreateDirectory($installerRoot) | Out-Null
    $signTool = Find-SignTool

    if (-not $SkipVerification) {
        Invoke-Checked -FilePath 'dotnet' -Arguments @(
            'run',
            '--project', '.\tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj',
            '-c', 'Release',
            '--no-launch-profile')
        Invoke-Checked -FilePath 'dotnet' -Arguments @('run', '--project', './tools/Hypershell.DistributionVerifier/Hypershell.DistributionVerifier.csproj', '-c', 'Release', '--artifacts-path', (Join-Path $scratchRoot 'distribution-tests'))
        Invoke-Checked -FilePath 'dotnet' -Arguments @('run', '--project', './tools/Hypershell.SuiteVerifier/Hypershell.SuiteVerifier.csproj', '-c', 'Release', '--artifacts-path', (Join-Path $scratchRoot 'suite-tests'))
        & (Join-Path $PSScriptRoot 'Test-HypershellGitHubRelease.ps1')
        & (Join-Path $PSScriptRoot 'Test-HypershellSuitePayloadValidation.ps1')
    }

    Invoke-Checked -FilePath 'dotnet' -Arguments @(
        'publish',
        '.\src\Hypershell\Hypershell.csproj',
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        ('-p:Version=' + $Version),
        '-o', $publishRoot,
        '--nologo')

    foreach ($applicationBinary in @(
        'Hypershell.exe',
        'Hypershell.dll',
        'Hypershell.ProfileProtocol.dll')) {
        $binaryPath = Join-Path $publishRoot $applicationBinary
        if (-not [IO.File]::Exists($binaryPath)) {
            throw "The published application binary is missing: $binaryPath"
        }
        Invoke-ArtifactSigning -Path $binaryPath
    }

    $generatedFilesPath = Join-Path $installerRoot 'PublishedFiles.wxs'
    & .\installer\Generate-WixFiles.ps1 `
        -PublishDirectory $publishRoot `
        -OutputPath $generatedFilesPath
    Invoke-Checked -FilePath 'dotnet' -Arguments @(
        'build',
        '.\installer\Hypershell.Installer.wixproj',
        '-c', 'Release',
        ('-p:ProductVersion=' + $Version),
        ('-p:PublishDir=' + $publishRoot),
        ('-p:GeneratedFilesPath=' + $generatedFilesPath),
        '-o', $installerRoot,
        '--nologo')

    $installerPath = Join-Path $installerRoot "Hypershell-$Version-win-x64.msi"
    if (-not [IO.File]::Exists($installerPath)) {
        throw "WiX did not produce the expected installer: $installerPath"
    }
    Invoke-ArtifactSigning -Path $installerPath

    $completePath = $null
    if ($Edition -eq 'Complete') {
        $stagedComponents = Join-Path $scratchRoot 'components'
        [IO.Directory]::CreateDirectory($stagedComponents) | Out-Null
        foreach ($component in @('Authoring', 'Meridian')) {
            Copy-Item -LiteralPath (Join-Path $SuitePayloadDirectory $component) -Destination $stagedComponents -Recurse
        }
        Copy-Item -LiteralPath (Join-Path $SuitePayloadDirectory 'suite-payload.json') -Destination $stagedComponents
        & (Join-Path $PSScriptRoot 'Test-HypershellSuitePayload.ps1') -PayloadDirectory $stagedComponents -ExpectedVersion $Version
        & (Join-Path $PSScriptRoot 'Test-HyperlibSuiteSeed.ps1') -HyperlibPayloadDirectory (Join-Path $stagedComponents 'Authoring/Hyperlib') -OutputDirectory (Join-Path $scratchRoot 'seed-smoke')
        foreach ($component in @('Authoring', 'Meridian')) {
            $payload = Join-Path $scratchRoot "components/$component"
            # Sign application code; framework assemblies retain their original Microsoft signatures.
            foreach ($binary in @(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object {
                $_.Name -match '^(Hypershell|HyperLib|Meridian)([.].*)?[.](exe|dll)$'
            })) { Invoke-ArtifactSigning -Path $binary.FullName }
            $fragment = Join-Path $installerRoot "$component.wxs"
            & (Join-Path $projectRoot 'installer/Suite/Generate-ComponentFiles.ps1') -ComponentName $component -PublishDirectory $payload -OutputPath $fragment
            Invoke-Checked -FilePath 'dotnet' -Arguments @('build', (Join-Path $projectRoot 'installer/Suite/Hypershell.Component.wixproj'), '-c', 'Release',
                "-p:ProductVersion=$Version", "-p:ComponentName=$component", "-p:GeneratedFilesPath=$fragment",
                "-p:BaseIntermediateOutputPath=$installerRoot/obj-$component/", '-o', $installerRoot, '--nologo')
            Invoke-ArtifactSigning -Path (Join-Path $installerRoot "Hypershell-$component-$Version-win-x64.msi")
        }
        Invoke-Checked -FilePath 'dotnet' -Arguments @('build', (Join-Path $projectRoot 'installer/Suite/Hypershell.Complete.wixproj'), '-c', 'Release',
            "-p:ProductVersion=$Version", "-p:CoreInstallerPath=$installerPath",
            "-p:AuthoringInstallerPath=$installerRoot/Hypershell-Authoring-$Version-win-x64.msi",
            "-p:MeridianInstallerPath=$installerRoot/Hypershell-Meridian-$Version-win-x64.msi",
            "-p:WebView2InstallerPath=$WebView2InstallerPath", "-p:DotNetSdkInstallerPath=$DotNetSdkInstallerPath",
            "-p:DotNetSdkMinimumVersion=$DotNetSdkVersion", "-p:BaseIntermediateOutputPath=$installerRoot/obj-complete/", '-o', $installerRoot, '--nologo')
        $unsignedBundle = Join-Path $installerRoot "Hypershell-Complete-$Version-win-x64.exe"
        # Burn caches a detached engine for repair/uninstall. Sign that engine
        # before reattaching and signing the complete downloadable executable.
        $wixCliRoot = Join-Path $scratchRoot 'wix-cli'
        Invoke-Checked -FilePath 'dotnet' -Arguments @('tool', 'install', 'wix', '--version', '4.0.6', '--tool-path', $wixCliRoot)
        $wixCli = Join-Path $wixCliRoot 'wix.exe'
        $enginePath = Join-Path $installerRoot 'complete-engine.exe'
        Invoke-Checked -FilePath $wixCli -Arguments @('burn', 'detach', $unsignedBundle, '-engine', $enginePath)
        Invoke-ArtifactSigning -Path $enginePath
        $signedBundleDirectory = Join-Path $installerRoot 'signed'
        [IO.Directory]::CreateDirectory($signedBundleDirectory) | Out-Null
        $completePath = Join-Path $signedBundleDirectory ([IO.Path]::GetFileName($unsignedBundle))
        Invoke-Checked -FilePath $wixCli -Arguments @('burn', 'reattach', $unsignedBundle, '-engine', $enginePath, '-o', $completePath)
        Invoke-ArtifactSigning -Path $completePath
    }

    [IO.Directory]::CreateDirectory($releaseRoot) | Out-Null
    $finalInstallerPath = Join-Path $releaseRoot ([IO.Path]::GetFileName($installerPath))
    [IO.File]::Copy($installerPath, $finalInstallerPath, $false)
    if ($null -ne $completePath) {
        [IO.File]::Copy($completePath, (Join-Path $releaseRoot ([IO.Path]::GetFileName($completePath))), $false)
    }
    $portablePath = Join-Path $releaseRoot "Hypershell-$Version-win-x64-portable.zip"
    Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $portablePath -CompressionLevel Optimal

    $publishedAt = [DateTimeOffset]::UtcNow.ToString('O')
    $manifestPath = Join-Path $releaseRoot 'release-manifest.json'
    Invoke-Checked -FilePath 'dotnet' -Arguments @(
        'run',
        '--project', '.\tools\Hypershell.ReleaseTool\Hypershell.ReleaseTool.csproj',
        '-c', 'Release',
        '--',
        'sign-manifest',
        '--version', $Version,
        '--channel', $Channel,
        '--git-commit', $gitCommit,
        '--published-at', $publishedAt,
        '--minimum-windows', $MinimumWindowsVersion,
        '--notes-file', $notesPath,
        '--installer', $finalInstallerPath,
        '--portable', $portablePath,
        '--certificate-thumbprint', $normalizedThumbprint,
        '--output', $manifestPath)

    $signerInfo = & dotnet run `
        --project .\tools\Hypershell.ReleaseTool\Hypershell.ReleaseTool.csproj `
        -c Release -- signer-info --certificate-thumbprint $normalizedThumbprint
    if ($LASTEXITCODE -ne 0) {
        throw 'The release signer fingerprint could not be read.'
    }
    $signerLine = $signerInfo |
        Where-Object { $_ -like 'RELEASE_SIGNER|*' } |
        Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($signerLine)) {
        throw 'The release signer did not return its SHA-256 fingerprint.'
    }
    $signerSha256 = $signerLine.Split('|')[1]
    $verifyArguments = @(
        'run',
        '--project', '.\tools\Hypershell.ReleaseTool\Hypershell.ReleaseTool.csproj',
        '-c', 'Release',
        '--',
        'verify-bundle',
        '--manifest', $manifestPath,
        '--trusted-signer', $signerSha256)
    if (-not $AllowUntrustedDiagnosticCertificate) {
        $verifyArguments += '--require-chain'
    }
    Invoke-Checked -FilePath 'dotnet' -Arguments $verifyArguments

    if ($null -ne $completePath) {
        $completeManifestPath = Join-Path $releaseRoot 'complete-manifest.json'
        Invoke-Checked -FilePath 'dotnet' -Arguments @('run', '--project', './tools/Hypershell.ReleaseTool/Hypershell.ReleaseTool.csproj', '-c', 'Release', '--',
            'sign-manifest', '--version', $Version, '--channel', 'stable-complete', '--git-commit', $gitCommit,
            '--published-at', $publishedAt, '--minimum-windows', $MinimumWindowsVersion, '--notes-file', $notesPath,
            '--installer', (Join-Path $releaseRoot ([IO.Path]::GetFileName($completePath))), '--certificate-thumbprint', $normalizedThumbprint,
            '--output', $completeManifestPath)
        Invoke-Checked -FilePath 'dotnet' -Arguments @('run', '--project', './tools/Hypershell.ReleaseTool/Hypershell.ReleaseTool.csproj', '-c', 'Release', '--',
            'verify-bundle', '--manifest', $completeManifestPath, '--trusted-signer', $signerSha256, '--require-chain')
    }

    if (-not $AllowDirty) {
        $finalCommit = (& git rev-parse HEAD).Trim()
        $finalChanges = @(& git status --porcelain --untracked-files=all)
        if ($LASTEXITCODE -ne 0 -or $finalCommit -ne $gitCommit -or $finalChanges.Count -gt 0) {
            throw 'Source changed during release creation. The preserved output is not eligible for publication.'
        }
    }
    Write-Output "HYPERSHELL_RELEASE_OK|$Version|$Channel|$gitCommit|$signerSha256|$releaseRoot"
}
catch {
    if ($null -ne $releaseRoot -and [IO.Directory]::Exists($releaseRoot)) {
        Write-Warning "The incomplete release output was preserved for diagnosis: $releaseRoot"
    }
    throw
}
finally {
    try {
        if ($null -ne $scratchRoot -and
            [IO.Directory]::Exists($scratchRoot)) {
            $scratchParent = ([IO.Path]::GetFullPath(
                    (Join-Path $projectRoot 'output\release-build'))).TrimEnd(
                    [IO.Path]::DirectorySeparatorChar) +
                [IO.Path]::DirectorySeparatorChar
            $resolvedScratch = [IO.Path]::GetFullPath($scratchRoot)
            if (-not $resolvedScratch.StartsWith(
                    $scratchParent,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw 'The generated release scratch path escaped its approved output directory.'
            }
            Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
        }
    }
    finally {
        Pop-Location
    }
}
