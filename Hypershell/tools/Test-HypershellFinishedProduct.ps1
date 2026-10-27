[CmdletBinding()]
param(
    [string]$ServerAlias = 'pinglab',
    [string]$ExpectedServerCommit = '',
    [string]$ExpectedServerImage = '',
    [string]$AcceptanceEvidencePath = '',
    [switch]$RunFullVerifier
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot
$checks = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [bool]$Passed,
        [Parameter(Mandatory)] [string]$Evidence,
        [string]$Remediation = ''
    )

    $checks.Add([pscustomobject]@{
        Name = $Name
        Passed = $Passed
        Evidence = $Evidence
        Remediation = $Remediation
    })
}

function Invoke-GitText {
    param([Parameter(Mandatory)] [string[]]$Arguments)
    $value = & git @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $value"
    }

    return ($value | Out-String).Trim()
}

function Normalize-IpAddress {
    param([Parameter(Mandatory)] [string]$Address)
    try {
        return [System.Net.IPAddress]::Parse($Address).ToString()
    }
    catch {
        return $Address
    }
}

$head = Invoke-GitText @('rev-parse', 'HEAD')
$origin = Invoke-GitText @('rev-parse', 'origin/main')
$status = Invoke-GitText @('status', '--porcelain')
Add-Check 'Repository is clean' ($status.Length -eq 0) `
    $(if ($status.Length -eq 0) { $head } else { $status }) `
    'Commit or deliberately remove every local change.'
Add-Check 'Origin contains audited commit' ($head -eq $origin) `
    "HEAD=$head origin/main=$origin" `
    'Push the audited commit to the private origin.'

if ($RunFullVerifier) {
    $verifierOutput = & dotnet run --project `
        tools\Hypershell.CSharpVerifier\Hypershell.CSharpVerifier.csproj `
        -c Release -- 2>&1
    $verifierPassed = $LASTEXITCODE -eq 0 -and
        ($verifierOutput -match 'verification passed')
    Add-Check 'Full Hypershell verifier' $verifierPassed `
        $(if ($verifierPassed) { 'All verifier stages passed.' } else { ($verifierOutput | Select-Object -Last 20 | Out-String).Trim() }) `
        'Resolve every failing verifier stage before release.'
}
else {
    Add-Check 'Full Hypershell verifier requested' $false `
        'Skipped in this run.' `
        'Re-run with -RunFullVerifier for finished-release evidence.'
}

$trustPath = Join-Path $repositoryRoot `
    'src\Hypershell\Assets\Release\trusted-signers.json'
$trust = Get-Content -LiteralPath $trustPath -Raw | ConvertFrom-Json
$trustedFingerprints = @($trust.signerCertificateSha256)
$signingCertificates = @(Get-ChildItem Cert:\CurrentUser\My `
    -CodeSigningCert -ErrorAction SilentlyContinue | Where-Object {
        $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date)
    })
$certificateFingerprints = @($signingCertificates | ForEach-Object {
    $_.GetCertHashString([System.Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
})
$matchingSigner = @($certificateFingerprints | Where-Object {
    $trustedFingerprints -contains $_
})
Add-Check 'Trusted code-signing certificate' `
    ($signingCertificates.Count -gt 0 -and $matchingSigner.Count -gt 0) `
    "usable-certificates=$($signingCertificates.Count) trusted-pins=$($trustedFingerprints.Count) matches=$($matchingSigner.Count)" `
    'Acquire the trusted certificate, run Set-HypershellReleaseSigner.ps1, commit the pin, promote the server with --release-signer-sha256, and publish the signed build.'

$serverScript = @'
set -euo pipefail
printf 'SERVER_COMMIT=%s\n' "$(git --git-dir=/opt/hypershell/git/hypershell.git rev-parse refs/heads/main)"
printf 'LIVE_IMAGE=%s\n' "$(docker inspect -f '{{.Config.Image}}' hypershell-profile-server)"
printf 'LIVE_RUNNING=%s\n' "$(docker inspect -f '{{.State.Running}}' hypershell-profile-server)"
printf 'LIVE_RESTARTS=%s\n' "$(docker inspect -f '{{.RestartCount}}' hypershell-profile-server)"
printf 'LIVE_PORT=%s\n' "$(docker port hypershell-profile-server 8080/tcp | head -n1)"
printf 'LOCAL_HEALTH=%s\n' "$(curl --fail --silent --show-error http://127.0.0.1:$(docker port hypershell-profile-server 8080/tcp | head -n1 | cut -d: -f2)/healthz | tr -d '\n')"
if findmnt -rn /srv/hypershell >/dev/null 2>&1; then
  printf 'VOLUME_MOUNTED=true\n'
  printf 'VOLUME_BYTES=%s\n' "$(df -B1 --output=size /srv/hypershell | tail -n1 | tr -d ' ')"
else
  printf 'VOLUME_MOUNTED=false\n'
  printf 'VOLUME_BYTES=0\n'
fi
printf 'UPSTREAM_INSTALLED=%s\n' "$(test -f /etc/nginx/conf.d/hypershell-profile-upstream.conf && echo true || echo false)"
printf 'ADMIN_SITE_INSTALLED=%s\n' "$(test -e /etc/nginx/sites-enabled/admin.atlasuniversalis.com && echo true || echo false)"
printf 'SNAPSHOT_TIMER_ENABLED=%s\n' "$(systemctl is-enabled hypershell-volume-snapshot.timer 2>/dev/null || true)"
printf 'RESTORE_EVIDENCE_COUNT=%s\n' "$(find /srv/hypershell/backups /opt/atlas/backups -type f -name 'restore-evidence-*.json' 2>/dev/null | wc -l)"
'@

$serverLines = @($serverScript | & ssh -o BatchMode=yes `
    -o ConnectTimeout=10 $ServerAlias 'bash -s' 2>&1)
$serverConnected = $LASTEXITCODE -eq 0
Add-Check 'Server SSH preflight' $serverConnected `
    $(if ($serverConnected) { $ServerAlias } else { ($serverLines | Out-String).Trim() }) `
    'Restore noninteractive SSH access before deployment.'

$server = @{}
if ($serverConnected) {
    foreach ($line in $serverLines) {
        $text = [string]$line
        $separator = $text.IndexOf('=')
        if ($separator -gt 0) {
            $server[$text.Substring(0, $separator)] =
                $text.Substring($separator + 1)
        }
    }
}

if ([string]::IsNullOrWhiteSpace($ExpectedServerCommit)) {
    $ExpectedServerCommit = $head
}
$serverCommit = [string]($server['SERVER_COMMIT'] ?? '')
Add-Check 'Server repository is current' `
    ($serverConnected -and $serverCommit -eq $ExpectedServerCommit) `
    "expected=$ExpectedServerCommit actual=$serverCommit" `
    'Push the intended commit to the private bare repository.'

$liveImage = [string]($server['LIVE_IMAGE'] ?? '')
$imagePassed = $serverConnected -and
    ([string]::IsNullOrWhiteSpace($ExpectedServerImage) -or
     $liveImage -eq $ExpectedServerImage)
Add-Check 'Production profile-server image' $imagePassed `
    "live=$liveImage expected=$ExpectedServerImage" `
    'Promote the validated candidate through the named cutover script.'
Add-Check 'Production profile-server health' `
    ($serverConnected -and $server['LIVE_RUNNING'] -eq 'true' -and
     $server['LIVE_RESTARTS'] -eq '0' -and
     $server['LOCAL_HEALTH'] -match '"status":"ready"') `
    "running=$($server['LIVE_RUNNING']) restarts=$($server['LIVE_RESTARTS']) port=$($server['LIVE_PORT'])" `
    'Inspect the container and roll back using the cutover evidence.'

$minimumVolumeBytes = 45L * 1024 * 1024 * 1024
$volumeBytes = 0L
[void][long]::TryParse([string]($server['VOLUME_BYTES'] ?? '0'), [ref]$volumeBytes)
Add-Check 'Dedicated Hypershell block volume' `
    ($serverConnected -and $server['VOLUME_MOUNTED'] -eq 'true' -and
     $volumeBytes -ge $minimumVolumeBytes) `
    "mounted=$($server['VOLUME_MOUNTED']) bytes=$volumeBytes" `
    'Attach the 50 GB volume and mount it at /srv/hypershell with prepare-storage.sh.'
Add-Check 'Nginx blue-green upstream' `
    ($serverConnected -and $server['UPSTREAM_INSTALLED'] -eq 'true') `
    "installed=$($server['UPSTREAM_INSTALLED'])" `
    'Install nginx-upstream.conf and the upstream-based API/admin sites.'
Add-Check 'Scheduled volume snapshots' `
    ($serverConnected -and $server['SNAPSHOT_TIMER_ENABLED'] -eq 'enabled') `
    "timer=$($server['SNAPSHOT_TIMER_ENABLED'])" `
    'Configure the scoped DigitalOcean token/volume UUID and enable the verified timer.'
Add-Check 'Isolated restore evidence' `
    ($serverConnected -and [int]($server['RESTORE_EVIDENCE_COUNT'] ?? 0) -gt 0) `
    "count=$($server['RESTORE_EVIDENCE_COUNT'])" `
    'Run restore-profile-server.sh against the newest consistent backup.'

$expectedA = '167.71.179.90'
$expectedAaaa = Normalize-IpAddress '2604:a880:800:14:0:1:aa80:1000'
$resolvedA = @()
$resolvedAaaa = @()
try {
    $resolvedA = @(Resolve-DnsName admin.atlasuniversalis.com -Type A `
        -DnsOnly -ErrorAction Stop | ForEach-Object { $_.IPAddress })
    $resolvedAaaa = @(Resolve-DnsName admin.atlasuniversalis.com -Type AAAA `
        -DnsOnly -ErrorAction Stop | ForEach-Object {
            Normalize-IpAddress $_.IPAddress
        })
}
catch {
}
Add-Check 'Admin DNS A record' ($resolvedA -contains $expectedA) `
    "expected=$expectedA actual=$($resolvedA -join ',')" `
    'Create admin.atlasuniversalis.com A -> 167.71.179.90.'
Add-Check 'Admin DNS AAAA record' ($resolvedAaaa -contains $expectedAaaa) `
    "expected=$expectedAaaa actual=$($resolvedAaaa -join ',')" `
    'Create admin.atlasuniversalis.com AAAA -> 2604:a880:800:14:0:1:aa80:1000.'

$adminTlsPassed = $false
$adminTlsEvidence = 'DNS or HTTPS request unavailable.'
try {
    $response = Invoke-WebRequest `
        'https://admin.atlasuniversalis.com/Account/Login' `
        -MaximumRedirection 0 -TimeoutSec 15
    $adminTlsPassed = $response.StatusCode -eq 200 -and
        $response.Content -match 'Hypershell Admin'
    $adminTlsEvidence = "HTTP $($response.StatusCode)"
}
catch {
    $adminTlsEvidence = $_.Exception.Message
}
Add-Check 'Private admin portal TLS' $adminTlsPassed $adminTlsEvidence `
    'Issue the dedicated certificate, enable the admin Nginx site, and verify login over HTTPS.'

$acceptancePassed = $false
$acceptanceEvidence = 'No manual two-PC evidence supplied.'
if (-not [string]::IsNullOrWhiteSpace($AcceptanceEvidencePath) -and
    (Test-Path -LiteralPath $AcceptanceEvidencePath -PathType Leaf)) {
    try {
        $acceptance = Get-Content -LiteralPath $AcceptanceEvidencePath -Raw |
            ConvertFrom-Json
        $required = @(
            'cleanInstall', 'deviceAuthorization', 'profileRestore',
            'credentialUnlock', 'remoteLinux', 'remoteWindows',
            'aiRemoteTask', 'gitWorkflow', 'signedUpdate',
            'rollback', 'isolatedRecoveryRestore')
        $missing = @($required | Where-Object {
            -not ($acceptance.PSObject.Properties.Name -contains $_) -or
            $acceptance.$_ -ne $true
        })
        $acceptancePassed = $missing.Count -eq 0
        $acceptanceEvidence = if ($acceptancePassed) {
            (Resolve-Path -LiteralPath $AcceptanceEvidencePath).Path
        }
        else {
            "Missing or false: $($missing -join ', ')"
        }
    }
    catch {
        $acceptanceEvidence = $_.Exception.Message
    }
}
Add-Check 'Two-PC end-to-end acceptance' $acceptancePassed `
    $acceptanceEvidence `
    'Complete doc/OPERATIONS.md acceptance steps and supply the resulting JSON evidence file.'

foreach ($check in $checks) {
    $prefix = if ($check.Passed) { 'PASS' } else { 'FAIL' }
    Write-Host "[$prefix] $($check.Name) — $($check.Evidence)"
    if (-not $check.Passed -and $check.Remediation) {
        Write-Host "       $($check.Remediation)"
    }
}

$failed = @($checks | Where-Object { -not $_.Passed })
Write-Host "HYPERSHELL_FINISHED_PRODUCT_AUDIT|passed:$($checks.Count - $failed.Count)|failed:$($failed.Count)|commit:$head"
if ($failed.Count -gt 0) {
    exit 1
}
