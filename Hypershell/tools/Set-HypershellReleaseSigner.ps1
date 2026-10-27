[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $CertificateThumbprint,

    [switch] $CheckOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$maximumTrustDocumentBytes = 64 * 1024
$maximumSignerCount = 8
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$trustPath = Join-Path $projectRoot `
    'src\Hypershell\Assets\Release\trusted-signers.json'
$normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').Replace(
        ':', '').ToUpperInvariant()
if ($normalizedThumbprint.Length -lt 40 -or
    $normalizedThumbprint -notmatch '^[0-9A-F]+$') {
    throw 'The code-signing certificate thumbprint is malformed.'
}

$certificate = Get-Item -LiteralPath `
    "Cert:\CurrentUser\My\$normalizedThumbprint" `
    -ErrorAction SilentlyContinue
if ($null -eq $certificate -or -not $certificate.HasPrivateKey) {
    throw 'The requested CurrentUser code-signing certificate with its private key is not installed.'
}

$temporaryPath = $null
try {
    $now = [DateTime]::UtcNow
    if ($certificate.NotBefore.ToUniversalTime() -gt $now -or
        $certificate.NotAfter.ToUniversalTime() -le $now) {
        throw 'The code-signing certificate is outside its validity period.'
    }

    $ekuExtension = $certificate.Extensions |
        Where-Object { $_.Oid.Value -eq '2.5.29.37' } |
        Select-Object -First 1
    if ($null -eq $ekuExtension) {
        throw 'The certificate does not declare an Enhanced Key Usage extension.'
    }
    $enhancedKeyUsage = [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new(
        $ekuExtension,
        $ekuExtension.Critical)
    if (-not @($enhancedKeyUsage.EnhancedKeyUsages).Where({
                $_.Value -eq $codeSigningOid
            }).Count) {
        throw 'The certificate does not permit Code Signing.'
    }

    $keyUsageExtension = $certificate.Extensions |
        Where-Object { $_.Oid.Value -eq '2.5.29.15' } |
        Select-Object -First 1
    if ($null -ne $keyUsageExtension) {
        $keyUsage = [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            $keyUsageExtension,
            $keyUsageExtension.Critical)
        if (($keyUsage.KeyUsages -band
                [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature) -eq 0) {
            throw 'The certificate key usage does not permit digital signatures.'
        }
    }

    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.ApplicationPolicy.Add(
            [Security.Cryptography.Oid]::new($codeSigningOid))
        $chain.ChainPolicy.RevocationMode =
            [Security.Cryptography.X509Certificates.X509RevocationMode]::Online
        $chain.ChainPolicy.RevocationFlag =
            [Security.Cryptography.X509Certificates.X509RevocationFlag]::ExcludeRoot
        $chain.ChainPolicy.VerificationFlags =
            [Security.Cryptography.X509Certificates.X509VerificationFlags]::NoFlag
        $chain.ChainPolicy.UrlRetrievalTimeout = [TimeSpan]::FromSeconds(15)
        if (-not $chain.Build($certificate)) {
            $statuses = @($chain.ChainStatus | ForEach-Object {
                    $_.Status.ToString()
                } | Sort-Object -Unique) -join ','
            throw "Windows does not trust the code-signing certificate chain ($statuses)."
        }
    }
    finally {
        $chain.Dispose()
    }

    $signerSha256 = ($certificate.GetCertHashString(
            [Security.Cryptography.HashAlgorithmName]::SHA256)).ToLowerInvariant()
    if ($signerSha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'The certificate SHA-256 fingerprint could not be calculated.'
    }

    $trustInfo = Get-Item -LiteralPath $trustPath
    if ($trustInfo.Length -le 0 -or
        $trustInfo.Length -gt $maximumTrustDocumentBytes) {
        throw 'The bundled release trust document has an invalid size.'
    }
    if (($trustInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'The bundled release trust document cannot be a reparse point.'
    }
    $originalBytes = [IO.File]::ReadAllBytes($trustPath)
    $originalHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($originalBytes))
    $parsed = Get-Content -LiteralPath $trustPath -Raw | ConvertFrom-Json
    $propertyNames = @($parsed.PSObject.Properties.Name)
    if ($propertyNames.Count -ne 2 -or
        $propertyNames -notcontains 'schemaVersion' -or
        $propertyNames -notcontains 'signerCertificateSha256' -or
        $parsed.schemaVersion -ne 1) {
        throw 'The bundled release trust document is malformed.'
    }
    $existing = @($parsed.signerCertificateSha256 | ForEach-Object {
            ([string] $_).Replace(':', '').Trim().ToLowerInvariant()
        })
    if ($existing.Count -gt $maximumSignerCount -or
        @($existing | Where-Object { $_ -notmatch '^[0-9a-f]{64}$' }).Count -gt 0 -or
        @($existing | Select-Object -Unique).Count -ne $existing.Count) {
        throw 'The bundled release trust document contains invalid signer fingerprints.'
    }

    $alreadyPinned = $existing -contains $signerSha256
    $changed = $false
    if (-not $alreadyPinned -and -not $CheckOnly) {
        if ($existing.Count -ge $maximumSignerCount) {
            throw 'The bundled release trust document has no room for another transition signer.'
        }
        if ($PSCmdlet.ShouldProcess(
                $trustPath,
                "Add trusted release signer $signerSha256")) {
            $updated = [ordered]@{
                schemaVersion = 1
                signerCertificateSha256 = @($existing) + $signerSha256
            } | ConvertTo-Json -Depth 3
            $updated += "`n"
            $currentBytes = [IO.File]::ReadAllBytes($trustPath)
            $currentHash = [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($currentBytes))
            if ($currentHash -ne $originalHash) {
                throw 'The release trust document changed during signer enrollment.'
            }
            $temporaryPath = Join-Path (
                Split-Path -Parent $trustPath) `
                ".trusted-signers.$([Guid]::NewGuid().ToString('N')).tmp"
            [IO.File]::WriteAllText(
                $temporaryPath,
                $updated,
                [Text.UTF8Encoding]::new($false))
            [IO.File]::Replace($temporaryPath, $trustPath, $null, $true)
            $temporaryPath = $null
            $changed = $true
        }
    }

    $daysRemaining = [Math]::Floor(
        ($certificate.NotAfter.ToUniversalTime() - $now).TotalDays)
    Write-Output (
        "HYPERSHELL_RELEASE_SIGNER_READY|sha256:$signerSha256|" +
        "thumbprint:$normalizedThumbprint|days:$daysRemaining|" +
        "already-pinned:$($alreadyPinned.ToString().ToLowerInvariant())|" +
        "changed:$($changed.ToString().ToLowerInvariant())|" +
        "check-only:$($CheckOnly.ToString().ToLowerInvariant())")
}
finally {
    if ($null -ne $temporaryPath -and
        [IO.File]::Exists($temporaryPath)) {
        [IO.File]::Delete($temporaryPath)
    }
    $certificate.Dispose()
}
