#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stable-common.ps1')
. (Join-Path $PSScriptRoot 'task-launcher-common.ps1')
$stateRoot = Get-StableStateRoot
$config = Get-StableConfig
if ($config.meridianOrigin -ne 'http://127.0.0.1:5240') { throw 'This installed-backend task requires the verified local Meridian origin.' }
$identity = Get-BackendTaskIdentity
$taskName = $identity.name
$description = $identity.description
$existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existing) { Assert-OwnedBackendTask $existing -AllowLegacyUpgrade | Out-Null }
$launcher = Assert-TaskLauncherInstallation
$sid = $identity.userSid
$principal = New-ScheduledTaskPrincipal -UserId $sid -LogonType Interactive -RunLevel Limited
$action = New-ScheduledTaskAction -Execute $launcher -Argument $identity.arguments -WorkingDirectory $PSScriptRoot
$triggers = @(
    (New-ScheduledTaskTrigger -AtLogOn -User $sid),
    (New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(1)) -RepetitionInterval (New-TimeSpan -Minutes 1))
)
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$definition = New-ScheduledTask -Action $action -Trigger $triggers -Principal $principal -Settings $settings -Description $description
Register-ScheduledTask -TaskName $taskName -InputObject $definition -Force | Out-Null
$verified = Get-ScheduledTask -TaskName $taskName
Assert-OwnedBackendTask $verified | Out-Null
$stopPath = Join-Path $stateRoot 'backend.stop.request'
if (Test-Path -LiteralPath $stopPath) { Remove-Item -LiteralPath $stopPath }
Start-ScheduledTask -TaskName $taskName
[pscustomobject]@{ taskName = $taskName; user = $sid; logonType = 'Interactive'; runLevel = 'Limited'; executable = $launcher; mode = $identity.arguments; noConsole = $true; watchdogMinutes = 1 } | ConvertTo-Json
