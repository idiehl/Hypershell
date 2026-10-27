#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stable-common.ps1')
$root = Get-StableStateRoot
$config = Get-StableConfig
$runPath = Join-Path $root 'run.json'
$stopPath = Join-Path $root 'stop.request'
$lockPath = Join-Path $root 'supervisor.lock'
$lock = $null; $companion = $null; $tunnel = $null
$receipt = @{ supervisor = (Get-ProcessIdentity (Get-Process -Id $PID)); companion = $null; tunnel = $null; endpoint = "$($config.publicOrigin)/mcp"; provider = $config.launcher.provider; status = 'starting'; startedAt = [DateTime]::UtcNow.ToString('o'); restartCount = 0 }
function Save-Receipt([string]$Status) {
    $receipt.status = $Status
    $receipt.updatedAt = [DateTime]::UtcNow.ToString('o')
    $receipt.companion = if ($companion -and -not $companion.HasExited) { Get-ProcessIdentity $companion } else { $null }
    $receipt.tunnel = if ($tunnel -and -not $tunnel.HasExited) { Get-ProcessIdentity $tunnel } else { $null }
    Write-StableJson $runPath $receipt
}
function Stop-OwnHelper($Process) {
    if ($Process -and -not $Process.HasExited) {
        $checked = Get-OwnedProcess (Get-ProcessIdentity $Process)
        if ($checked) { Stop-Process -Id $checked.Id -ErrorAction SilentlyContinue }
    }
}
function Start-Tunnel {
    $launch = $config.launcher
    if ($launch.provider -eq 'external') { return $null }
    Assert-PinnedTunnelBinary $launch.tunnelPath $launch.tunnelSha256
    if ($launch.provider -eq 'ngrok') {
        $arguments = @('http',"http://127.0.0.1:$($config.port)",'--url',$config.publicOrigin,'--inspect=false','--log','stdout','--log-format','json','--config',('"' + $launch.tunnelConfigPath + '"'))
    } elseif ($launch.provider -eq 'cloudflare') {
        $arguments = @('tunnel','--no-autoupdate','--config',('"' + $launch.tunnelConfigPath + '"'),'run',$launch.tunnelId)
    } else { throw 'Unsupported stable tunnel provider.' }
    return Start-Process -FilePath $launch.tunnelPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'tunnel.stdout.log') -RedirectStandardError (Join-Path $root 'tunnel.stderr.log')
}
try {
    # A file lock prevents two startup invocations from supervising this profile.
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    Save-Receipt 'waiting-for-meridian'
    $consecutiveFailures = 0
    $unreadyCount = 0
    while (-not (Test-Path -LiteralPath $stopPath)) {
        if ($companion -and $companion.HasExited) { $companion = $null; $receipt.restartCount++; $consecutiveFailures++ }
        if ($tunnel -and $tunnel.HasExited) { $tunnel = $null; $receipt.restartCount++; $consecutiveFailures++ }
        $backendReady = $false
        try {
            $health = Invoke-RestMethod "$($config.meridianOrigin)/__meridian_status" -TimeoutSec 3
            $backendReady = $health.application -eq 'atlas-meridian' -and $health.status -eq 'ready'
        } catch { }
        if ($backendReady -and -not $companion) {
            if (Get-NetTCPConnection -State Listen -LocalPort $config.port -ErrorAction SilentlyContinue) { throw 'The stable companion port became occupied. The supervisor will not adopt or stop that process.' }
            $companion = Start-Process -FilePath $config.launcher.nodePath -ArgumentList @(('"' + $config.launcher.serverPath + '"'),'--config',('"' + (Join-Path $root 'config.json') + '"')) -WorkingDirectory (Split-Path $config.launcher.serverPath -Parent) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'companion.stdout.log') -RedirectStandardError (Join-Path $root 'companion.stderr.log')
            Save-Receipt 'starting-companion'
        }
        $localReady = $false
        if ($companion -and -not $companion.HasExited) {
            try { $metadata = Invoke-RestMethod "http://127.0.0.1:$($config.port)/.well-known/oauth-authorization-server" -TimeoutSec 3; $localReady = $metadata.issuer -eq $config.publicOrigin } catch { }
            if ($localReady) { $unreadyCount = 0 }
            else {
                $unreadyCount++
                if ($unreadyCount -ge 6) { Stop-OwnHelper $companion; $companion = $null; $unreadyCount = 0; $receipt.restartCount++; $consecutiveFailures++ }
            }
        }
        if ($localReady -and -not $tunnel -and $config.launcher.provider -ne 'external') { $tunnel = Start-Tunnel; Save-Receipt 'starting-tunnel' }
        if ($localReady) {
            if (($config.launcher.provider -eq 'external') -or ($tunnel -and -not $tunnel.HasExited -and ([DateTime]::Now - $tunnel.StartTime).TotalSeconds -ge 30)) { $consecutiveFailures = 0 }
            Save-Receipt 'local-ready'
        }
        elseif (-not $backendReady) { Save-Receipt 'waiting-for-meridian' }
        # Small bounded pauses keep the supervisor responsive to its stop file.
        $delaySeconds = [Math]::Min(30, 2 + (2 * $consecutiveFailures))
        for ($i = 0; $i -lt $delaySeconds -and -not (Test-Path -LiteralPath $stopPath); $i++) { Start-Sleep -Seconds 1 }
    }
} catch {
    # Do not echo exception text: provider errors can include sensitive details.
    [Console]::Error.WriteLine('The stable Meridian supervisor stopped after a validation or launch failure. Inspect its private local logs.')
    $receipt.status = 'failed'
} finally {
    Stop-OwnHelper $tunnel
    Stop-OwnHelper $companion
    if ($lock) { Save-Receipt $(if ($receipt.status -eq 'failed') { 'failed' } else { 'stopped' }); $lock.Dispose() }
}
