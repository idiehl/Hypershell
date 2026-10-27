#Requires -Version 7.4
<#
.SYNOPSIS
Creates or resumes a GitHub draft containing the exact signed Complete/Core bundle.
.DESCRIPTION
The destination must already exist. TargetCommit belongs to the destination
repository and may differ from the private source commit in the signed manifests.
Set GH_TOKEN with Contents write permission for that repository. No token is
accepted on the command line. This command never publishes a draft or pushes code.
WhatIf validates the local inventory and hashes without contacting GitHub or
claiming cryptographic verification. The real run requires trusted signatures
and Authenticode before it makes any GitHub request.
.EXAMPLE
./tools/Publish-HypershellGitHubRelease.ps1 -BundleDirectory ./release/1.2.3 `
  -Owner idiehl -Repository hypershell-releases -TargetCommit DESTINATION_COMMIT `
  -TrustedSignerSha256 CERTIFICATE_SHA256 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string] $BundleDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9-]{0,38}$')][string] $Owner,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$')][string] $Repository,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string] $TargetCommit,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string] $TrustedSignerSha256,
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]*$')][string] $TokenEnvironmentVariable = 'GH_TOKEN'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'GitHubRelease.psm1') -Force
$plan = Get-HypershellGitHubReleasePlan -BundleDirectory $BundleDirectory -Owner $Owner -Repository $Repository -TargetCommit $TargetCommit
if ($plan.SignerSha256 -cne $TrustedSignerSha256.ToLowerInvariant()) {
    throw 'The expected release signer differs from the signer named in the manifests.'
}
if ($WhatIfPreference) {
    [void] $PSCmdlet.ShouldProcess("$Owner/$Repository $($plan.Tag)", 'Create or resume the exact GitHub draft and upload the verified bundle')
    [pscustomobject]@{
        Mode = 'OfflinePlan'; SignatureVerified = $false; Draft = $true
        Repository = "$Owner/$Repository"; Tag = $plan.Tag; TargetCommit = $plan.TargetCommit
        Files = @($plan.Files | Select-Object Name, Size, Sha256)
        Note = 'Local inventory and hashes checked. Cryptographic trust, Authenticode and GitHub checks run only during execution.'
    }
    return
}
if (-not $IsWindows) { throw 'Production publication requires Windows Authenticode verification.' }
$project = Join-Path $PSScriptRoot 'Hypershell.ReleaseTool/Hypershell.ReleaseTool.csproj'
foreach ($manifest in $plan.Manifests) {
    $verification = @(& dotnet run --project $project -c Release -- verify-bundle --manifest $manifest.Path --trusted-signer $TrustedSignerSha256 --require-chain 2>&1)
    if ($LASTEXITCODE -ne 0 -or -not ($verification -match '^RELEASE_BUNDLE_VALID\|')) {
        throw "Trusted signature verification failed for $($manifest.Name). No GitHub requests were made."
    }
}
foreach ($artifact in $plan.Files | Where-Object RequiresAuthenticode) {
    $signature = Get-AuthenticodeSignature -LiteralPath $artifact.Path
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate) {
        throw "Authenticode validation failed: $($artifact.Name). No GitHub requests were made."
    }
    $signerHash = $signature.SignerCertificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
    if ($signerHash -cne $TrustedSignerSha256.ToLowerInvariant()) { throw "Authenticode signer differs from the signed manifest: $($artifact.Name)" }
}
# Rebuild the byte inventory after verification to detect changes during validation.
$fresh = Get-HypershellGitHubReleasePlan -BundleDirectory $BundleDirectory -Owner $Owner -Repository $Repository -TargetCommit $TargetCommit
if ($fresh.Body -cne $plan.Body) { throw 'The local release changed during verification.' }
$plan.SignatureVerified = $true
if (-not $PSCmdlet.ShouldProcess("$Owner/$Repository $($plan.Tag)", 'Create or resume the exact GitHub draft and upload the verified bundle')) { return }
$token = [Environment]::GetEnvironmentVariable($TokenEnvironmentVariable)
if ([string]::IsNullOrWhiteSpace($token)) { throw "Set $TokenEnvironmentVariable to a token with Contents write permission for this repository." }
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromHours(1)
try {
    $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $token)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('Hypershell-Release/1.0')
    $client.DefaultRequestHeaders.Add('X-GitHub-Api-Version', '2022-11-28')
    $client.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
    $request = {
        param($Method, $Url, $Body, $File)
        $uri = [Uri] $Url
        if ($uri.Scheme -ne 'https' -or $uri.Host -notin @('api.github.com', 'uploads.github.com') -or
            $uri.Port -ne 443 -or $uri.UserInfo.Length -gt 0) { throw 'The request escaped the GitHub API allowlist.' }
        $optional = $Method -eq 'GET-OPTIONAL'
        $httpMethod = if ($optional) { 'GET' } else { $Method }
        $message = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($httpMethod), $uri)
        $response = $null
        try {
            if ($null -ne $File) {
                $stream = [IO.File]::Open($File.Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
                $message.Content = [Net.Http.StreamContent]::new($stream)
                if ($stream.Length -ne $File.Size -or
                    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() -cne $File.Sha256) {
                    throw "The release file changed before upload: $($File.Name)"
                }
                $stream.Position = 0
                $message.Content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
            }
            elseif ($null -ne $Body) {
                $message.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 10 -Compress), [Text.Encoding]::UTF8, 'application/json')
            }
            $response = $client.SendAsync($message).GetAwaiter().GetResult()
            if ($optional -and [int] $response.StatusCode -eq 404) { return $null }
            if (-not $response.IsSuccessStatusCode) {
                # Do not echo request headers, credential material, or remote error bodies.
                throw "GitHub $httpMethod request failed with HTTP $([int] $response.StatusCode). The draft and uploaded assets are retained for a safe retry."
            }
            $json = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ($json.Length -gt 4MB) { throw 'GitHub returned an oversized JSON response.' }
            return ($json | ConvertFrom-Json -Depth 30)
        }
        finally {
            if ($null -ne $response) { $response.Dispose() }
            $message.Dispose()
        }
    }.GetNewClosure()
    $result = Invoke-HypershellGitHubDraft -Plan $plan -Request $request
    Write-Output $result
    Write-Output "HYPERSHELL_GITHUB_DRAFT_READY|$Owner/$Repository|$($plan.Tag)|assets:$($result.ArtifactCount)|sha256-verified|draft-only"
}
finally {
    $client.Dispose()
    $handler.Dispose()
    $token = $null
}
