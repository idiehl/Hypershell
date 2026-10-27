#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'secure-tunnel-common.ps1')
$root = Get-SecureTunnelRoot
$config = Get-SecureTunnelConfig
Assert-SecureTunnelBinary $config
$key = Get-SecureTunnelKey
$key.Dispose()
Assert-TaskLauncherInstallation | Out-Null
$identity = Get-SecureTunnelTaskIdentity
$task = Get-ScheduledTask -TaskName $identity.name -TaskPath $identity.path -ErrorAction SilentlyContinue
if (-not $task) { & (Join-Path $PSScriptRoot 'install-secure-tunnel-startup.ps1') | Out-Null; $task = Get-ScheduledTask -TaskName $identity.name -TaskPath $identity.path }
Assert-OwnedSecureTunnelTask $task | Out-Null
$stopPath = Join-Path $root 'stop.request'
Assert-SecureRegularPath $stopPath | Out-Null
if (Test-Path -LiteralPath $stopPath) { Remove-Item -LiteralPath $stopPath }
Enable-ScheduledTask -TaskName $identity.name -TaskPath $identity.path | Out-Null
Start-ScheduledTask -TaskName $identity.name -TaskPath $identity.path
Write-Output 'Secure Tunnel startup requested. Run status-secure-tunnel.ps1 to verify readiness; ChatGPT tool success is still required for end-to-end confirmation.'
