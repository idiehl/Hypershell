#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stable-common.ps1')
$root = Get-StableStateRoot
$config = Get-StableConfig
$runPath = Join-Path $root 'run.json'
$orphanCompanion = $null
if (Test-Path -LiteralPath $runPath) {
    $run = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json -AsHashtable
    if (Get-OwnedProcess $run.supervisor) { Write-Output 'The stable Meridian supervisor is already running.'; exit 0 }
    $orphanCompanion = Get-OwnedProcess $run.companion
    Get-OwnedProcess $run.tunnel | Out-Null
}
$listeners = @(Get-NetTCPConnection -State Listen -LocalPort $config.port -ErrorAction SilentlyContinue)
if ($listeners.Count -and (-not $orphanCompanion -or @($listeners | Where-Object { $_.OwningProcess -ne $orphanCompanion.Id }).Count)) { throw 'The stable companion port is occupied by an unrecognized process. No process was stopped.' }
$stopPath = Join-Path $root 'stop.request'
if (Test-Path -LiteralPath $stopPath) { Remove-Item -LiteralPath $stopPath }
$identity = Get-StableTaskIdentity
$task = Get-ScheduledTask -TaskName $identity.name -TaskPath $identity.path -ErrorAction SilentlyContinue
if (-not $task) { throw 'Register the current-user task with install-startup.ps1 -RegisterOnly first. Direct launch from a coding-agent process is deliberately disabled.' }
Assert-OwnedStableTask $task | Out-Null
Enable-ScheduledTask -TaskName $identity.name -TaskPath $identity.path | Out-Null
Start-ScheduledTask -TaskName $identity.name -TaskPath $identity.path
$deadline = [DateTime]::UtcNow.AddSeconds(20)
do {
    Start-Sleep -Milliseconds 200
    if (Test-Path -LiteralPath $runPath) {
        $receipt = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json -AsHashtable
        $process = Get-OwnedProcess $receipt.supervisor
        if ($process -and ([DateTime]::UtcNow - ([DateTimeOffset]$receipt.updatedAt).UtcDateTime).TotalSeconds -lt 90) {
            [pscustomobject]@{ supervisorPid = $process.Id; endpoint = "$($config.publicOrigin)/mcp"; provider = $config.launcher.provider; launcher = 'windows-task-scheduler'; currentPrototypeUnchanged = $true } | ConvertTo-Json
            exit 0
        }
    }
} until ([DateTime]::UtcNow -ge $deadline)
throw 'The scheduled supervisor did not report a live process and fresh heartbeat. Check Task Scheduler and the private lifecycle log.'
