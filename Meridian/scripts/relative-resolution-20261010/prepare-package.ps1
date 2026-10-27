[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId)
. (Join-Path $PSScriptRoot 'common.ps1')
if (Test-Path -LiteralPath $stage) { throw 'Candidate already exists. Preserve it and use the final-dist restager for an explicit update.' }
$installedBefore = Application-Inventory $installed
$hostBefore = Host-Identity $ExpectedProcessId
$nativeBefore = Native-Identity
$manifestHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
$sourceBefore = Host-Schema-Inventory
New-Item -ItemType Directory -Path $stage,$evidence -Force | Out-Null
foreach ($file in $installedBefore) {
    $target = Resolve-ApplicationFile $stage $file.relativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-ApplicationFile $installed $file.relativePath) -Destination $target
}
Assert-Inventory $stage $installedBefore
foreach ($name in $staticNames) { Copy-Item -LiteralPath (Resolve-ApplicationFile (Join-Path $workspace 'dist') $name) -Destination (Resolve-ApplicationFile $stage "wwwroot/$name") -Force }
$candidate = Application-Inventory $stage
Assert-StaticOnly $installedBefore $candidate
Assert-FinalDist
Assert-Inventory $installed $installedBefore
if (!(Equal-Json $hostBefore (Host-Identity $ExpectedProcessId)) -or !(Equal-Json $nativeBefore (Native-Identity)) -or $manifestHash -cne (Get-FileHash -LiteralPath $manifest).Hash -or !(Equal-Json $sourceBefore (Host-Schema-Inventory))) { throw 'Protected identity changed during staging.' }
[ordered]@{ preparedAt=[DateTimeOffset]::Now.ToString('o'); mode='static-only'; packageRoot=$stage; packageHashes=$candidate; installedHashes=$installedBefore; host=$hostBefore; native=$nativeBefore; manifestHash=$manifestHash; hostSchemaHashes=$sourceBefore; savedDataFilesAccessed=$false; installedFilesChanged=$false; backendRestarted=$false; nativeRestarted=$false } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'preparation.json') -Encoding utf8NoBOM
Write-Output "Prepared $($candidate.Count) application files with only six final frontend replacements. Backend and native windows remain untouched."
