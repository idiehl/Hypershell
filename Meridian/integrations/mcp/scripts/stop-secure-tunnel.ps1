#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'secure-tunnel-common.ps1')
$root = Get-SecureTunnelRoot
$marker = Join-Path $root 'stop.request'
Assert-SecureRegularPath $marker | Out-Null
[IO.File]::WriteAllText($marker, 'Explicit local user stop; watchdog must not restart.')
$identity = Get-SecureTunnelTaskIdentity
$task = Get-ScheduledTask -TaskName $identity.name -TaskPath $identity.path -ErrorAction SilentlyContinue
if ($task) { Assert-OwnedSecureTunnelTask $task | Out-Null; Disable-ScheduledTask -TaskName $identity.name -TaskPath $identity.path | Out-Null }
$config = Get-SecureTunnelConfig
$result = Invoke-SecureTunnelClient $config @('runtimes','stop','meridian-authoring','--json') 45
if ($result.exitCode -ne 0) { throw 'The stop request is persistent, but native runtime shutdown could not be confirmed. Inspect status-secure-tunnel.ps1.' }
Write-Output 'Stopped the owned Secure Tunnel runtime and disabled its watchdog. Meridian and the legacy disabled ngrok profile were not changed.'
