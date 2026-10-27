#Requires -Version 7.0
[CmdletBinding()]
param([switch]$Remove, [switch]$Enable)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'secure-tunnel-common.ps1')
$identity = Get-SecureTunnelTaskIdentity
$existing = Get-ScheduledTask -TaskName $identity.name -TaskPath $identity.path -ErrorAction SilentlyContinue
if ($existing) { Assert-OwnedSecureTunnelTask $existing | Out-Null }
if ($Remove) {
    if ($existing) { Disable-ScheduledTask -TaskName $identity.name -TaskPath $identity.path | Out-Null; Unregister-ScheduledTask -TaskName $identity.name -TaskPath $identity.path -Confirm:$false }
    Write-Output 'Removed the owned Secure Tunnel scheduled task. The legacy task was not changed.'
    exit 0
}
$config = Get-SecureTunnelConfig
Assert-SecureTunnelBinary $config
$key = Get-SecureTunnelKey
$key.Dispose()
$launcher = Assert-TaskLauncherInstallation
$action = New-ScheduledTaskAction -Execute $launcher -Argument $identity.arguments -WorkingDirectory $PSScriptRoot
$principal = New-ScheduledTaskPrincipal -UserId $identity.userSid -LogonType Interactive -RunLevel Limited
$logon = New-ScheduledTaskTrigger -AtLogOn -User $identity.userSid
$watchdog = New-ScheduledTaskTrigger -Once -At ([DateTime]::Now.AddMinutes(1)) -RepetitionInterval ([TimeSpan]::FromMinutes(1))
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable -RestartCount 3 -RestartInterval ([TimeSpan]::FromMinutes(1)) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$settings.Enabled = [bool]$Enable
$task = New-ScheduledTask -Action $action -Principal $principal -Trigger @($logon,$watchdog) -Settings $settings -Description $identity.description
Register-ScheduledTask -TaskName $identity.name -TaskPath $identity.path -InputObject $task -Force | Out-Null
$verified = Get-ScheduledTask -TaskName $identity.name -TaskPath $identity.path
Assert-OwnedSecureTunnelTask $verified | Out-Null
if ($verified.Principal.RunLevel -ne 'Limited' -or $verified.Principal.LogonType -ne 'Interactive' -or $verified.Settings.MultipleInstances -ne 'IgnoreNew' -or $verified.Settings.ExecutionTimeLimit -ne 'PT0S') { throw 'The task did not retain limited-user recovery settings.' }
[pscustomobject]@{ taskName=$identity.name; enabled=$verified.Settings.Enabled; startup='Current-user logon and minute watchdog'; secretInAction=$false; legacyTaskChanged=$false } | ConvertTo-Json
