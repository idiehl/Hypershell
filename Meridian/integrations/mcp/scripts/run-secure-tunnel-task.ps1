#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'secure-tunnel-common.ps1')
$root = Get-SecureTunnelRoot
$stopPath = Join-Path $root 'stop.request'
if (Test-Path -LiteralPath $stopPath) { exit 0 }
$lock = $null
try { $lock = [IO.File]::Open((Join-Path $root 'supervisor.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch { exit 0 } # Another owned supervisor is active.
$receipt = @{ provider='openai-secure-mcp-tunnel'; status='starting'; startedAt=[DateTime]::UtcNow.ToString('o'); restartCount=0; supervisorPid=$PID }
function Save-SecureReceipt([string]$State, $Summary = $null) {
    $receipt.status = $State
    $receipt.updatedAt = [DateTime]::UtcNow.ToString('o')
    if ($Summary) { $receipt.runtime = $Summary }
    Write-SecureTunnelJson (Join-Path $root 'run.json') $receipt
}
try {
    $config = Get-SecureTunnelConfig
    Assert-SecureTunnelBinary $config
    $backoff = 2
    $unhealthySince = $null
    while (-not (Test-Path -LiteralPath $stopPath)) {
        $backendReady = $false
        try {
            $backend = Invoke-RestMethod "$($config.meridianOrigin)/__meridian_status" -TimeoutSec 3
            $backendReady = $backend.application -eq 'atlas-meridian' -and $backend.status -eq 'ready'
        } catch { }
        if (-not $backendReady) {
            # The native app/backend has its own lifecycle. Never start, stop, or replace it here.
            Save-SecureReceipt 'waiting-for-meridian'
        } else {
            $summary = Get-SecureTunnelSummary (Invoke-SecureTunnelClient $config @('runtimes','status','meridian-authoring','--json') 45)
            if (-not $summary.processRunning) {
                $command = Get-SecureTunnelCommand $config
                $connected = Invoke-SecureTunnelClient $config @('runtimes','connect','--alias','meridian-authoring','--tunnel-id',$config.tunnelId,'--name','Meridian Authoring','--mcp-command',$command,'--runtime-api-key','env:CONTROL_PLANE_API_KEY','--json') 75
                $summary = Get-SecureTunnelSummary $connected
                $receipt.restartCount++
                Save-SecureReceipt 'starting-runtime' $summary
            }
            if ($summary.processRunning -and $summary.healthy -and $summary.ready) {
                Save-SecureReceipt 'ready' $summary
                $unhealthySince = $null
                $backoff = 2
            } else {
                Save-SecureReceipt 'recovering' $summary
                if (-not $unhealthySince) { $unhealthySince = [DateTime]::UtcNow }
                # A temporary network outage uses the client's own retries. Restart only
                # a persistently non-live process; readiness alone never triggers a kill.
                if ($summary.processRunning -and -not $summary.healthy -and ([DateTime]::UtcNow - $unhealthySince).TotalSeconds -ge 120) {
                    $null = Invoke-SecureTunnelClient $config @('runtimes','stop','meridian-authoring','--json') 45
                    $unhealthySince = $null
                }
                $backoff = [Math]::Min(30, $backoff * 2)
            }
        }
        $delay = if ($backendReady -and $receipt.status -eq 'ready') { 15 } elseif ($backendReady) { $backoff } else { 5 }
        for ($i=0; $i -lt $delay -and -not (Test-Path -LiteralPath $stopPath); $i++) { Start-Sleep -Seconds 1 }
    }
    $null = Invoke-SecureTunnelClient $config @('runtimes','stop','meridian-authoring','--json') 45
    Save-SecureReceipt 'stopped'
} catch {
    Save-SecureReceipt 'failed'
    [Console]::Error.WriteLine('The Secure Tunnel supervisor stopped after validation or runtime failure. Inspect the private state and status command; no secret values were logged.')
    exit 1
} finally { if ($lock) { $lock.Dispose() } }
