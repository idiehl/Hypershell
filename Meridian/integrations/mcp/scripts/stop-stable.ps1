#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stable-common.ps1')
$root = Get-StableStateRoot
$runPath = Join-Path $root 'run.json'
if (-not (Test-Path -LiteralPath $runPath)) { throw 'No stable supervisor receipt exists.' }
$run = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json -AsHashtable
$supervisor = Get-OwnedProcess $run.supervisor
Set-Content -LiteralPath (Join-Path $root 'stop.request') -Value ([DateTime]::UtcNow.ToString('o')) -Encoding ascii
if ($supervisor) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (-not $supervisor.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if (-not $supervisor.HasExited) { $checked = Get-OwnedProcess $run.supervisor; if ($checked) { Stop-Process -Id $checked.Id } }
}
# Recover only helpers whose exact process identities were recorded by this profile.
$run = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json -AsHashtable
foreach ($identity in @($run.tunnel, $run.companion)) { $process = Get-OwnedProcess $identity; if ($process) { Stop-Process -Id $process.Id } }
$run.stoppedAt = [DateTime]::UtcNow.ToString('o'); $run.status = 'stopped'
Write-StableJson $runPath $run
Write-Output 'Stopped only the stable profile helpers. The existing Quick Tunnel, prototype companion, Meridian, and saved grants remain unchanged.'
