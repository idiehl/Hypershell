[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId)
. (Join-Path $PSScriptRoot 'common.ps1')
$reviewPath = Join-Path $evidence 'package-review.json'
$review = Get-Content -LiteralPath $reviewPath -Raw | ConvertFrom-Json
if (!$review.passed -or $review.mode -ne 'full-host-fixture-only' -or $review.browserChecks -ne 19) { throw 'A passing exact-package review is required.' }
Assert-Inventory $stage $review.packageHashes
Assert-Inventory $installed $review.installedHashes
Assert-FinalDist
if (!(Equal-Json (Source-Inventory) $review.sourceHashes)) { throw 'Production source changed; a new package review is required.' }
if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -cne $review.manifestHash) { throw 'Launcher changed; inspect before replacement.' }
$native = Native-Identity
Assert-ExpectedNative $native
$previousPreservedNative = @($review.native | Where-Object ProcessId -EQ 21144)
if (!(Equal-Json @($native) $previousPreservedNative)) { throw 'The protected MyNet window changed; inspect before replacement.' }
# Studio closed independently during the first install. Only that known departed
# process may differ; the installed application has already been rolled back.
if (@($review.native | Where-Object { $_.ProcessId -notin @(21144,137328) }).Count -or
    (Get-Process -Id 137328 -ErrorAction SilentlyContinue)) { throw 'Unexpected native process transition.' }
$hostIdentity = Host-Identity $ExpectedProcessId
$status = Invoke-RestMethod 'http://127.0.0.1:5240/__meridian_status' -TimeoutSec 5
if ($status.application -ne 'atlas-meridian') { throw 'Restored backend is not ready.' }
$archive = Join-Path $evidence 'package-review-before-runtime-refresh.json'
if (Test-Path -LiteralPath $archive) { throw 'Runtime review refresh was already recorded.' }
Copy-Item -LiteralPath $reviewPath -Destination $archive
[ordered]@{
  refreshedAt=[DateTimeOffset]::Now.ToString('o'); reason='Independent Studio process exited during install; prior application restored and all byte inventories reverified.';
  previousHost=$review.liveHost; currentHost=$hostIdentity; previousNative=$review.native; currentNative=$native;
  packageBytesUnchanged=$true; installedOriginalBytesRestored=$true; sourceUnchanged=$true; launcherUnchanged=$true;
  savedDataFilesAccessed=$false; nativeProcessesStopped=$false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'runtime-checkpoint-refresh.json') -Encoding utf8NoBOM
$review.liveHost = $hostIdentity
$review.native = @($native)
$review | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reviewPath -Encoding utf8NoBOM
Write-Output "Reverified unchanged source/package/original installed bytes and preserved native MyNet. Restored live backend PID $ExpectedProcessId is ready. No saved-data access."
