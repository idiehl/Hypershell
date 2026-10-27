#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'secure-tunnel-common.ps1')
$root = Get-SecureTunnelRoot
$config = Get-SecureTunnelConfig
$identity = Get-SecureTunnelTaskIdentity
$task = Get-ScheduledTask -TaskName $identity.name -TaskPath $identity.path -ErrorAction SilentlyContinue
if ($task) { Assert-OwnedSecureTunnelTask $task | Out-Null }
$summary = Get-SecureTunnelSummary (Invoke-SecureTunnelClient $config @('runtimes','status','meridian-authoring','--json') 45)
$backendReady = $false
try { $backend = Invoke-RestMethod "$($config.meridianOrigin)/__meridian_status" -TimeoutSec 3; $backendReady = $backend.application -eq 'atlas-meridian' -and $backend.status -eq 'ready' } catch { }
$result = @{ provider='openai-secure-mcp-tunnel'; tunnelId=$config.tunnelId; backendReady=$backendReady; projects=$config.projectIds.Count; stopRequested=(Test-Path -LiteralPath (Join-Path $root 'stop.request')); taskRegistered=($null -ne $task); taskEnabled=($task -and $task.Settings.Enabled); runtime=$summary; endToEndChatGPTVerified=$null; verificationScope='This command checks local runtime health only; it does not invoke ChatGPT tools or invalidate a separately recorded end-to-end acceptance check.' }
$result | ConvertTo-Json -Depth 8
