[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $ReleaseNotesFile,

    [Parameter(Mandatory)]
    [string] $CertificateThumbprint,

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$')]
    [string] $ServerAlias = 'pinglab',

    [switch] $Pin,
    [ValidateSet('Core', 'Complete')][string] $Edition = 'Core',
    [string] $SuitePayloadDirectory,
    [string] $WebView2InstallerPath,
    [string] $WebView2InstallerSha256,
    [string] $DotNetSdkInstallerPath,
    [string] $DotNetSdkInstallerSha256,
    [ValidatePattern('^10\.[0-9]+\.[0-9]+$')][string] $DotNetSdkVersion = '10.0.100'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$remoteStage = $null
$remoteStageCreated = $false
$remoteStageCommitted = $false
$notesPath = [IO.Path]::GetFullPath($ReleaseNotesFile)
if (-not [IO.File]::Exists($notesPath)) {
    throw "The release-notes file does not exist: $notesPath"
}

function Invoke-NativeText {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = @(& $FilePath @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE`: $($output -join [Environment]::NewLine)"
    }
    return ($output | Out-String).Trim()
}

Push-Location $projectRoot
try {
    $head = Invoke-NativeText -FilePath 'git' -Arguments @(
        'rev-parse', 'HEAD')
    $origin = Invoke-NativeText -FilePath 'git' -Arguments @(
        'rev-parse', 'origin/main')
    $status = Invoke-NativeText -FilePath 'git' -Arguments @(
        'status', '--porcelain', '--untracked-files=all')
    if ($head -notmatch '^[0-9a-f]{40,64}$' -or $head -ne $origin) {
        throw 'The stable release must use the exact commit already pushed to origin/main.'
    }
    if (-not [string]::IsNullOrWhiteSpace($status)) {
        throw 'The stable release worktree must be clean.'
    }

    $signerResult = @(& (Join-Path $PSScriptRoot `
                'Set-HypershellReleaseSigner.ps1') `
            -CertificateThumbprint $CertificateThumbprint `
            -CheckOnly)
    $signerLine = $signerResult |
        Where-Object { $_ -like 'HYPERSHELL_RELEASE_SIGNER_READY|*' } |
        Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($signerLine) -or
        $signerLine -notmatch '\|sha256:(?<sha>[0-9a-f]{64})\|') {
        throw 'The trusted release-signer fingerprint could not be validated.'
    }
    $signerSha256 = $Matches.sha
    $trust = Get-Content -LiteralPath `
        'src\Hypershell\Assets\Release\trusted-signers.json' `
        -Raw | ConvertFrom-Json
    if (@($trust.signerCertificateSha256) -notcontains $signerSha256) {
        throw 'The validated signer is not bundled in the desktop release trust policy.'
    }

    $serverCommit = Invoke-NativeText -FilePath 'ssh' -Arguments @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        'git', '--git-dir=/opt/hypershell/git/hypershell.git',
        'rev-parse', 'refs/heads/main')
    if ($serverCommit -ne $head) {
        throw 'The private server repository does not contain the release commit.'
    }
    $mountTarget = Invoke-NativeText -FilePath 'ssh' -Arguments @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        'findmnt', '-nro', 'TARGET', '-T', '/srv/hypershell')
    if ($mountTarget -ne '/srv/hypershell') {
        throw 'The dedicated Hypershell block volume is not mounted.'
    }
    $activeSigner = Invoke-NativeText -FilePath 'ssh' -Arguments @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        'docker', 'inspect',
        '-f', '{{range .Config.Env}}{{println .}}{{end}}',
        'hypershell-profile-server')
    $activeSigner = @($activeSigner -split "`r?`n" |
        Where-Object { $_ -like 'HYPERSHELL_RELEASE_SIGNER_SHA256=*' } |
        ForEach-Object { $_.Substring($_.IndexOf('=') + 1) }) |
        Select-Object -First 1
    if ($activeSigner -ne $signerSha256) {
        throw 'Production has not been promoted with the validated release signer.'
    }
    $remotePublisher =
        '/opt/hypershell/profile-server/deploy/hypershell-server/publish-profile-release.sh'
    $remotePublisherHash = Invoke-NativeText -FilePath 'ssh' -Arguments @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        'sha256sum', $remotePublisher)
    $remotePublisherHash = ($remotePublisherHash -split '\s+')[0]
    $localPublisherHash = (Get-FileHash -LiteralPath `
            'deploy\hypershell-server\publish-profile-release.sh' `
            -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($remotePublisherHash -ne $localPublisherHash) {
        throw 'The server release publisher does not match the audited release commit.'
    }

    if (-not $PSCmdlet.ShouldProcess(
            "$ServerAlias stable channel",
            "Build, upload, and publish immutable Hypershell $Version")) {
        return
    }

    & (Join-Path $PSScriptRoot 'Publish-HypershellRelease.ps1') `
        -Version $Version `
        -ReleaseNotesFile $notesPath `
        -CertificateThumbprint $CertificateThumbprint `
        -Edition $Edition -SuitePayloadDirectory $SuitePayloadDirectory `
        -WebView2InstallerPath $WebView2InstallerPath -WebView2InstallerSha256 $WebView2InstallerSha256 `
        -DotNetSdkInstallerPath $DotNetSdkInstallerPath -DotNetSdkInstallerSha256 $DotNetSdkInstallerSha256 `
        -DotNetSdkVersion $DotNetSdkVersion
    $bundleRoot = Join-Path $projectRoot "release\$Version"
    $artifactNames = @(
        "Hypershell-$Version-win-x64.msi",
        "Hypershell-$Version-win-x64-portable.zip",
        'release-manifest.json')
    if ($Edition -eq 'Complete') {
        $artifactNames += @("Hypershell-Complete-$Version-win-x64.exe", 'complete-manifest.json')
    }
    foreach ($artifactName in $artifactNames) {
        $artifactPath = Join-Path $bundleRoot $artifactName
        if (-not [IO.File]::Exists($artifactPath) -or
            (Get-Item -LiteralPath $artifactPath).Length -le 0) {
            throw "The local stable release artifact is missing: $artifactPath"
        }
    }

    $currentHead = Invoke-NativeText -FilePath 'git' -Arguments @(
        'rev-parse', 'HEAD')
    $currentOrigin = Invoke-NativeText -FilePath 'git' -Arguments @(
        'rev-parse', 'origin/main')
    $currentStatus = Invoke-NativeText -FilePath 'git' -Arguments @(
        'status', '--porcelain', '--untracked-files=all')
    if ($currentHead -ne $head -or $currentOrigin -ne $head -or
        -not [string]::IsNullOrWhiteSpace($currentStatus)) {
        throw 'The release source changed while the bundle was being built.'
    }
    $currentServerCommit = Invoke-NativeText -FilePath 'ssh' -Arguments @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        'git', '--git-dir=/opt/hypershell/git/hypershell.git',
        'rev-parse', 'refs/heads/main')
    if ($currentServerCommit -ne $head) {
        throw 'The private server repository changed while the bundle was being built.'
    }

    $uploadId = [Guid]::NewGuid().ToString('N')
    $remoteStage = "/srv/hypershell/releases/.upload-$Version-$uploadId"
    $remoteIncoming = "/srv/hypershell/releases/incoming/$Version"
    Invoke-NativeText -FilePath 'ssh' -Arguments @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        'install', '-d', '-m', '0750', '-o', 'root', '-g', '1654',
        $remoteStage) | Out-Null
    $remoteStageCreated = $true
    foreach ($artifactName in $artifactNames) {
        Invoke-NativeText -FilePath 'scp' -Arguments @(
            '-q', '--',
            (Join-Path $bundleRoot $artifactName),
            "${ServerAlias}:$remoteStage/$artifactName") | Out-Null
    }

    $localHashes = @{}
    foreach ($artifactName in $artifactNames) {
        $localHashes[$artifactName] = (Get-FileHash -LiteralPath `
                (Join-Path $bundleRoot $artifactName) `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        $remoteHash = Invoke-NativeText -FilePath 'ssh' -Arguments @(
            '-o', 'BatchMode=yes',
            '-o', 'ConnectTimeout=10',
            $ServerAlias,
            'sha256sum', "$remoteStage/$artifactName")
        if (($remoteHash -split '\s+')[0] -ne $localHashes[$artifactName]) {
            throw "The uploaded release artifact failed SHA-256 comparison: $artifactName"
        }
    }
    Invoke-NativeText -FilePath 'ssh' -Arguments @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        'chown', '-R', 'root:1654', $remoteStage) | Out-Null
    Invoke-NativeText -FilePath 'ssh' -Arguments @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        'chmod', '0750', $remoteStage) | Out-Null
    foreach ($artifactName in $artifactNames) {
        Invoke-NativeText -FilePath 'ssh' -Arguments @(
            '-o', 'BatchMode=yes',
            '-o', 'ConnectTimeout=10',
            $ServerAlias,
            'chmod', '0640', "$remoteStage/$artifactName") | Out-Null
    }
    Invoke-NativeText -FilePath 'ssh' -Arguments @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        'test', '!', '-e', $remoteIncoming) | Out-Null
    Invoke-NativeText -FilePath 'ssh' -Arguments @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        'install', '-d', '-m', '0750', '-o', 'root', '-g', '1654',
        '/srv/hypershell/releases/incoming') | Out-Null
    Invoke-NativeText -FilePath 'ssh' -Arguments @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        'mv', '-T', '--no-clobber', '--',
        $remoteStage, $remoteIncoming) | Out-Null
    $remoteStageCommitted = $true

    $publishArguments = @(
        '-o', 'BatchMode=yes',
        '-o', 'ConnectTimeout=10',
        $ServerAlias,
        $remotePublisher,
        '--bundle', $remoteIncoming,
        '--signer-sha256', $signerSha256)
    if ($Pin) {
        $publishArguments += '--pin'
    }
    $publishOutput = Invoke-NativeText -FilePath 'ssh' `
        -Arguments $publishArguments
    if ($publishOutput -notmatch
        "HYPERSHELL_RELEASE_PUBLISHED\|version:$([Regex]::Escape($Version))\|") {
        throw 'The server did not return immutable release-publication evidence.'
    }
    Write-Output $publishOutput
    Write-Output (
        "HYPERSHELL_STABLE_RELEASE_PUBLISHED|version:$Version|" +
        "commit:$head|signer:$signerSha256|server:$ServerAlias")
}
finally {
    if ($remoteStageCreated -and -not $remoteStageCommitted -and
        $remoteStage -match '^/srv/hypershell/releases/\.upload-[0-9A-Za-z.-]+-[0-9a-f]{32}$') {
        try {
            Invoke-NativeText -FilePath 'ssh' -Arguments @(
                '-o', 'BatchMode=yes',
                '-o', 'ConnectTimeout=10',
                $ServerAlias,
                'rm', '-rf', '--', $remoteStage) | Out-Null
        }
        catch {
            Write-Warning (
                "The failed root-owned upload staging directory could not " +
                "be removed: $remoteStage")
        }
    }
    Pop-Location
}
