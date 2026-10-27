[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId,[switch]$RebuildCandidate)
. (Join-Path $PSScriptRoot 'common.ps1')
$original = Application-Inventory $installed
$hostBefore = Host-Identity $ExpectedProcessId
$nativeBefore = Native-Identity; Assert-ExpectedNative $nativeBefore
$manifestHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
$sourceBefore = Source-Inventory
$prior = Get-Content -LiteralPath (Join-Path $workspace 'output/canvas-interaction-20261017/package/qa/package-review.json') -Raw | ConvertFrom-Json
if ($prior.passed -ne $true -or $prior.mode -ne 'full-host-fixture-only') { throw 'A previously verified backend package baseline is required.' }
$hostSources = @($sourceBefore | Where-Object relativePath -Match '^src\\host\\')
$priorHostSources = @($prior.sourceHashes | Where-Object relativePath -Match '^src\\host\\')
if (!(Equal-Json $hostSources $priorHostSources)) { throw 'Host sources changed; a frontend-only update is inappropriate.' }
if (!(Equal-Json @($original | Where-Object relativePath -NotMatch '^wwwroot\\') @($prior.packageHashes | Where-Object relativePath -NotMatch '^wwwroot\\'))) { throw 'Backend application bytes differ from the previous verified installation.' }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
[ordered]@{passed=$false;pending=$true;mode='static-frontend-fixture-only';originalSavedDataFilesAccessed=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Encoding utf8NoBOM
if (Test-Path -LiteralPath $stage) {
    if (!$RebuildCandidate) { throw 'Candidate exists. Use RebuildCandidate to retain and replace it.' }
    $archive = Join-Path $packageRoot ('app-before-rebuild-' + (Get-Date -Format 'yyyyMMdd-HHmmssffff'))
    $null = Resolve-ApplicationFile $packageRoot 'app'
    $null = Resolve-ApplicationFile $packageRoot ([IO.Path]::GetFileName($archive))
    $null = Application-Inventory $stage
    Move-Item -LiteralPath $stage -Destination $archive
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($file in $original) {
    $target = Resolve-ApplicationFile $stage $file.relativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-ApplicationFile $installed $file.relativePath) -Destination $target
}
foreach ($name in $staticNames) { Copy-Item -LiteralPath (Resolve-ApplicationFile (Join-Path $workspace 'dist') $name) -Destination (Resolve-ApplicationFile $stage "wwwroot/$name") -Force }
$candidate = Application-Inventory $stage
Assert-ApplicationFileSet $original $candidate
Assert-FinalDist; Assert-Inventory $installed $original
if (!(Equal-Json @($original | Where-Object relativePath -NotMatch '^wwwroot\\') @($candidate | Where-Object relativePath -NotMatch '^wwwroot\\'))) { throw 'Candidate changed backend bytes.' }
if (!(Equal-Json $sourceBefore (Source-Inventory)) -or !(Equal-Json $hostBefore (Host-Identity $ExpectedProcessId)) -or !(Equal-Json $nativeBefore (Native-Identity)) -or $manifestHash -cne (Get-FileHash -LiteralPath $manifest).Hash) { throw 'Protected source/runtime identity changed during preparation.' }
[ordered]@{preparedAt=[DateTimeOffset]::Now.ToString('o');mode='static-frontend-fixture-only';packageRoot=$stage;packageHashes=$candidate;installedHashes=$original;host=$hostBefore;native=$nativeBefore;manifestHash=$manifestHash;sourceHashes=$sourceBefore;savedDataFilesAccessed=$false;installedFilesChanged=$false;backendRestarted=$false;nativeRestarted=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'preparation.json') -Encoding utf8NoBOM
Write-Output "Prepared $($candidate.Count) isolated files with unchanged backend and six reviewed frontend assets."
