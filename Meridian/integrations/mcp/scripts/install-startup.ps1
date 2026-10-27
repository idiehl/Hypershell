#Requires -Version 7.0
[CmdletBinding()]
param([switch]$Remove, [switch]$RegisterOnly)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stable-common.ps1')
$identity = Get-StableTaskIdentity
$existing = Get-ScheduledTask -TaskName $identity.name -TaskPath $identity.path -ErrorAction SilentlyContinue
if ($existing) { Assert-OwnedStableTask $existing | Out-Null }
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Meridian MCP.lnk'
if ($Remove) {
    if ($existing) { Disable-ScheduledTask -TaskName $identity.name -TaskPath $identity.path | Out-Null; Unregister-ScheduledTask -TaskName $identity.name -TaskPath $identity.path -Confirm:$false }
    Write-Output 'Removed the owned current-user Meridian MCP scheduled task. Use stop-stable.ps1 to stop running helpers.'
    exit 0
}
$config = Get-StableConfig
$root = Get-StableStateRoot
$fixedServer = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'src/server.mjs'))
if ([IO.Path]::GetFullPath($config.launcher.serverPath) -ne $fixedServer -or [IO.Path]::GetFullPath($config.stateRoot) -ne $root -or -not (Test-Path -LiteralPath $fixedServer -PathType Leaf)) { throw 'The stable profile does not reference this fixed companion and private state directory.' }
if (-not $RegisterOnly) {
    $headers = @{ Accept = 'application/json' }
    $metadata = Invoke-WebRequest "$($config.publicOrigin)/.well-known/oauth-protected-resource/mcp" -TimeoutSec 10 -Headers $headers -UserAgent 'Meridian-MCP-Health/0.2'
    if ($metadata.Headers['Content-Type'] -notmatch '^application/json(?:\s*;|$)' -or ($metadata.Content | ConvertFrom-Json).resource -ne "$($config.publicOrigin)/mcp") { throw 'The stable endpoint did not pass its public JSON metadata check.' }
    $challenge = Invoke-WebRequest "$($config.publicOrigin)/mcp" -TimeoutSec 10 -SkipHttpErrorCheck -Headers $headers -UserAgent 'Meridian-MCP-Health/0.2'
    if ($challenge.StatusCode -ne 401 -or $challenge.Headers['WWW-Authenticate'] -notmatch 'Bearer' -or $challenge.Headers['Content-Type'] -notmatch '^application/json(?:\s*;|$)') { throw 'The stable endpoint did not pass its OAuth challenge check.' }
}
$pwsh = (Get-Process -Id $PID).Path
$arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -File "' + $identity.scriptPath + '"'
$action = New-ScheduledTaskAction -Execute $pwsh -Argument $arguments -WorkingDirectory $PSScriptRoot
$principal = New-ScheduledTaskPrincipal -UserId $identity.userSid -LogonType Interactive -RunLevel Limited
$logon = New-ScheduledTaskTrigger -AtLogOn -User $identity.userSid
$watchdog = New-ScheduledTaskTrigger -Once -At ([DateTime]::Now.AddMinutes(1)) -RepetitionInterval ([TimeSpan]::FromMinutes(1))
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable -RestartCount 3 -RestartInterval ([TimeSpan]::FromMinutes(1)) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
if ($RegisterOnly) { $settings.Enabled = if ($existing) { [bool]$existing.Settings.Enabled } else { $false } }
$task = New-ScheduledTask -Action $action -Principal $principal -Trigger @($logon, $watchdog) -Settings $settings -Description $identity.description
Register-ScheduledTask -TaskName $identity.name -TaskPath $identity.path -InputObject $task -Force | Out-Null
$verified = Get-ScheduledTask -TaskName $identity.name -TaskPath $identity.path
Assert-OwnedStableTask $verified | Out-Null
if ($verified.Principal.RunLevel -ne 'Limited' -or $verified.Principal.LogonType -ne 'Interactive' -or $verified.Settings.MultipleInstances -ne 'IgnoreNew' -or $verified.Settings.ExecutionTimeLimit -ne 'PT0S') { throw 'The registered task did not retain the required limited-user recovery settings.' }
if (Test-Path -LiteralPath $shortcutPath) {
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
    if ($shortcut.Description -ne 'Atlas Meridian stable MCP supervisor') { throw 'A different application owns the existing Startup shortcut. It was not changed.' }
    Remove-Item -LiteralPath $shortcutPath
}
[pscustomobject]@{ taskName = $identity.name; launcher = 'windows-task-scheduler'; userSid = $identity.userSid; logonType = 'Interactive'; runLevel = 'Limited'; enabled = $verified.Settings.Enabled; publicChecksPassed = (-not $RegisterOnly); credentialsChanged = $false } | ConvertTo-Json
