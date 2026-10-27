[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId)
. (Join-Path $PSScriptRoot 'common.ps1')
$prepared = Get-Content -LiteralPath (Join-Path $evidence 'preparation.json') -Raw | ConvertFrom-Json
Assert-Inventory $installed $prepared.installedHashes
Assert-StaticOnly $prepared.installedHashes (Application-Inventory $stage)
if (!(Equal-Json (Host-Identity $ExpectedProcessId) $prepared.host) -or !(Equal-Json (Native-Identity) $prepared.native) -or !(Equal-Json (Host-Schema-Inventory) $prepared.hostSchemaHashes) -or (Get-FileHash -LiteralPath $manifest).Hash -cne $prepared.manifestHash) { throw 'Protected identity changed after preparation.' }
foreach ($name in $staticNames) { Copy-Item -LiteralPath (Resolve-ApplicationFile (Join-Path $workspace 'dist') $name) -Destination (Resolve-ApplicationFile $stage "wwwroot/$name") -Force }
Assert-FinalDist
$prepared.packageHashes = Application-Inventory $stage
Assert-StaticOnly $prepared.installedHashes $prepared.packageHashes
$prepared | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'preparation.json') -Encoding utf8NoBOM
[ordered]@{ passed=$false; pending=$true; reason='Final frontend was explicitly restaged and needs fresh package QA.'; savedDataFilesAccessed=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Encoding utf8NoBOM
Write-Output 'Restaged all six final frontend files. A fresh isolated package review is required.'
