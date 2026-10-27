[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string] $Version = '0.0.1',

    [switch] $SkipFullVerifier,

    [switch] $KeepArtifacts
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$diagnosticParent = [IO.Path]::GetFullPath(
    (Join-Path $projectRoot 'output\release-pipeline-test'))
$runRoot = Join-Path $diagnosticParent ([Guid]::NewGuid().ToString('N'))
$bundleRoot = Join-Path $runRoot 'bundle'
$notesPath = Join-Path $runRoot 'release-notes.md'
$extractRoot = Join-Path $runRoot 'portable'
$certificate = $null
$myStore = $null

function Assert-ContainedPath {
    param(
        [Parameter(Mandatory)]
        [string] $Parent,

        [Parameter(Mandatory)]
        [string] $Child
    )

    $normalizedParent = ([IO.Path]::GetFullPath($Parent)).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $normalizedChild = [IO.Path]::GetFullPath($Child)
    if (-not $normalizedChild.StartsWith(
            $normalizedParent,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "The diagnostic release path escaped its approved root: $normalizedChild"
    }
}

function Get-MsiProperty {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $installer = $null
    $database = $null
    $view = $null
    $record = $null
    try {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = $installer.OpenDatabase(
            [IO.Path]::GetFullPath($Path),
            0)
        $view = $database.OpenView(
            "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$Name'")
        $view.Execute()
        $record = $view.Fetch()
        if ($null -eq $record) {
            throw "The MSI property '$Name' is missing."
        }

        return [string] $record.StringData(1)
    }
    finally {
        foreach ($comObject in @($record, $view, $database, $installer)) {
            if ($null -ne $comObject -and
                [Runtime.InteropServices.Marshal]::IsComObject($comObject)) {
                [void] [Runtime.InteropServices.Marshal]::FinalReleaseComObject(
                    $comObject)
            }
        }
    }
}

function Assert-PublishRejected {
    param(
        [Parameter(Mandatory)]
        [hashtable] $Arguments,

        [Parameter(Mandatory)]
        [string] $ExpectedMessage
    )

    try {
        & (Join-Path $PSScriptRoot 'Publish-HypershellRelease.ps1') `
            @Arguments | Out-Null
        throw 'The unsafe diagnostic release invocation was unexpectedly accepted.'
    }
    catch {
        if (-not $_.Exception.Message.Contains(
                $ExpectedMessage,
                [StringComparison]::Ordinal)) {
            throw
        }
    }
}

try {
    Assert-ContainedPath -Parent $diagnosticParent -Child $runRoot
    [IO.Directory]::CreateDirectory($runRoot) | Out-Null
    [IO.File]::WriteAllText(
        $notesPath,
        "Diagnostic release-pipeline validation only.`n",
        [Text.UTF8Encoding]::new($false))

    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=Hypershell Diagnostic Release $([Guid]::NewGuid().ToString('N'))" `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -NotAfter ([DateTimeOffset]::Now.AddDays(1).DateTime)
    if ($null -eq $certificate -or -not $certificate.HasPrivateKey) {
        throw 'The diagnostic code-signing certificate was not created.'
    }

    $guardArguments = @{
        Version = $Version
        ReleaseNotesFile = $notesPath
        CertificateThumbprint = $certificate.Thumbprint
        Channel = 'stable'
        OutputDirectory = Join-Path $runRoot 'rejected-stable-bundle'
        AllowDirty = $true
        AllowUntrustedDiagnosticCertificate = $true
    }
    Assert-PublishRejected `
        -Arguments $guardArguments `
        -ExpectedMessage 'allowed only on the diagnostic channel'

    $guardArguments['Channel'] = 'diagnostic'
    $guardArguments['OutputDirectory'] = Join-Path $projectRoot `
        "output\release-pipeline-escape\$([Guid]::NewGuid().ToString('N'))"
    Assert-PublishRejected `
        -Arguments $guardArguments `
        -ExpectedMessage 'must remain beneath output\release-pipeline-test'

    $publishArguments = @{
        Version = $Version
        ReleaseNotesFile = $notesPath
        CertificateThumbprint = $certificate.Thumbprint
        Channel = 'diagnostic'
        OutputDirectory = $bundleRoot
        AllowDirty = $true
        AllowUntrustedDiagnosticCertificate = $true
    }
    if ($SkipFullVerifier) {
        $publishArguments['SkipVerification'] = $true
    }
    & (Join-Path $PSScriptRoot 'Publish-HypershellRelease.ps1') `
        @publishArguments

    $installerPath = Join-Path $bundleRoot `
        "Hypershell-$Version-win-x64.msi"
    $portablePath = Join-Path $bundleRoot `
        "Hypershell-$Version-win-x64-portable.zip"
    $manifestPath = Join-Path $bundleRoot 'release-manifest.json'
    foreach ($artifact in @(
            $installerPath,
            $portablePath,
            $manifestPath)) {
        if (-not [IO.File]::Exists($artifact) -or
            (Get-Item -LiteralPath $artifact).Length -le 0) {
            throw "The diagnostic release artifact is missing or empty: $artifact"
        }
    }

    $installerSignature = Get-AuthenticodeSignature `
        -LiteralPath $installerPath
    if ($installerSignature.Status.ToString() -notin
            @('Valid', 'NotTrusted', 'UnknownError') -or
        $null -eq $installerSignature.SignerCertificate -or
        $installerSignature.SignerCertificate.Thumbprint -ne
            $certificate.Thumbprint) {
        throw 'The diagnostic MSI did not retain its expected Authenticode signer.'
    }
    if ((Get-MsiProperty -Path $installerPath -Name 'ProductVersion') -ne
        $Version) {
        throw 'The diagnostic MSI product version does not match the requested release.'
    }
    $allUsers = Get-MsiProperty -Path $installerPath -Name 'ALLUSERS'
    if ($allUsers -ne '1') {
        throw "The diagnostic MSI is not configured for a per-machine installation (ALLUSERS='$allUsers')."
    }

    [IO.Compression.ZipFile]::ExtractToDirectory(
        $portablePath,
        $extractRoot)
    foreach ($binaryName in @(
            'Hypershell.exe',
            'Hypershell.dll',
            'Hypershell.ProfileProtocol.dll')) {
        $binaryPath = Join-Path $extractRoot $binaryName
        $signature = Get-AuthenticodeSignature -LiteralPath $binaryPath
        if ($signature.Status.ToString() -notin
                @('Valid', 'NotTrusted', 'UnknownError') -or
            $null -eq $signature.SignerCertificate -or
            $signature.SignerCertificate.Thumbprint -ne
                $certificate.Thumbprint) {
            throw "The portable binary was not signed by the diagnostic signer: $binaryName"
        }
    }

    $bundleBytes = (Get-ChildItem -LiteralPath $bundleRoot -File |
        Measure-Object -Property Length -Sum).Sum
    Write-Output (
        "HYPERSHELL_RELEASE_PIPELINE_OK|version:$Version|" +
        "artifacts:3|bytes:$bundleBytes|signer:$($certificate.Thumbprint)")
}
finally {
    if ($null -ne $certificate) {
        $myStore = [Security.Cryptography.X509Certificates.X509Store]::new(
            [Security.Cryptography.X509Certificates.StoreName]::My,
            [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        $myStore.Open(
            [Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        foreach ($match in @($myStore.Certificates.Find(
                [Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $certificate.Thumbprint,
                $false))) {
            $myStore.Remove($match)
        }
        $myStore.Close()
        $myStore = $null
        $certificate.Dispose()
    }

    if (-not $KeepArtifacts -and
        [IO.Directory]::Exists($runRoot)) {
        Assert-ContainedPath -Parent $diagnosticParent -Child $runRoot
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
