#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stable-common.ps1')
$root = Get-StableStateRoot
$runPath = Join-Path $root 'run.json'
if (-not (Test-Path -LiteralPath $runPath)) { @{ status = 'not-started'; live = $false } | ConvertTo-Json; exit 0 }
$run = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json -AsHashtable
$supervisor = $null; $companion = $null; $tunnel = $null; $identityValid = $true
try { $supervisor = Get-OwnedProcess $run.supervisor; $companion = Get-OwnedProcess $run.companion; $tunnel = Get-OwnedProcess $run.tunnel } catch { $identityValid = $false }
$age = ([DateTime]::UtcNow - ([DateTimeOffset]$run.updatedAt).UtcDateTime).TotalSeconds
$fresh = $age -ge 0 -and $age -lt 90
$config = Get-StableConfig
$listener = Get-NetTCPConnection -State Listen -LocalPort $config.port -ErrorAction SilentlyContinue
$ownedListener = $companion -and @($listener | Where-Object { $_.OwningProcess -eq $companion.Id -and $_.LocalAddress -eq '127.0.0.1' }).Count -gt 0
$localReady = $false
if ($ownedListener) { try { $localReady = (Invoke-RestMethod "http://127.0.0.1:$($config.port)/.well-known/oauth-authorization-server" -TimeoutSec 3).issuer -eq $config.publicOrigin } catch { } }
$live = $identityValid -and $supervisor -and $companion -and $fresh -and $localReady -and ($run.provider -eq 'external' -or $tunnel)
$status = if (Test-Path -LiteralPath (Join-Path $root 'stop.request')) { 'explicitly-stopped' } elseif (-not $supervisor -or -not $fresh) { 'offline-stale-receipt' } elseif ($live) { 'local-ready' } else { 'recovering-or-waiting' }
[pscustomobject]@{ status = $status; live = [bool]$live; receiptStatus = $run.status; heartbeatAgeSeconds = [Math]::Round($age, 1); processIdentitiesValid = $identityValid; supervisorAlive = [bool]$supervisor; companionAlive = [bool]$companion; tunnelAlive = [bool]$tunnel; loopbackListenerOwned = [bool]$ownedListener; localMetadataReady = $localReady; endpoint = $run.endpoint } | ConvertTo-Json
