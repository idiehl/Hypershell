[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId,[switch]$RebuildCandidate)
. (Join-Path $PSScriptRoot 'common.ps1')
$installedBefore = Application-Inventory $installed
$hostBefore = Host-Identity $ExpectedProcessId
$nativeBefore = Native-Identity; Assert-ExpectedNative $nativeBefore
$manifestHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
$sourceBefore = Source-Inventory
$installedSource = Get-Content -LiteralPath (Join-Path $workspace 'output/gallery-fit-20261018/package/qa/package-review.json') -Raw | ConvertFrom-Json
$hostExtensions = @('.cs','.csproj')
if (!(Equal-Json @($sourceBefore | Where-Object { [IO.Path]::GetExtension($_.relativePath) -in $hostExtensions }) @($installedSource.sourceHashes | Where-Object { [IO.Path]::GetExtension($_.relativePath) -in $hostExtensions }))) { throw 'Host source differs from the verified installed schema; a frontend-only update is not eligible.' }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
[ordered]@{ passed=$false; pending=$true; reason='Preparation invalidates earlier review.'; originalSavedDataFilesAccessed=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Encoding utf8NoBOM
if (Test-Path -LiteralPath $stage) {
    if (!$RebuildCandidate) { throw 'Candidate exists. Preserve it or use -RebuildCandidate to archive it.' }
    $archiveName = 'app-before-rebuild-' + (Get-Date -Format 'yyyyMMdd-HHmmssffff')
    $null = Resolve-ApplicationFile $packageRoot 'app'
    $archive = Resolve-ApplicationFile $packageRoot $archiveName
    $null = Application-Inventory $stage
    Move-Item -LiteralPath $stage -Destination $archive
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
# Only installed application files are copied. User storage and ownership locks
# are never enumerated, read, copied, or restored by this frontend-only cycle.
foreach ($file in $installedBefore) {
    $target = Resolve-ApplicationFile $stage $file.relativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-ApplicationFile $installed $file.relativePath) -Destination $target
}
Assert-Inventory $stage $installedBefore
foreach ($name in $staticNames) {
    Copy-Item -LiteralPath (Resolve-ApplicationFile (Join-Path $workspace 'dist') $name) -Destination (Resolve-ApplicationFile $stage "wwwroot/$name") -Force
}
$candidate = Application-Inventory $stage
Assert-ApplicationFileSet $installedBefore $candidate
$assetPaths = @($staticNames | ForEach-Object { 'wwwroot\' + $_ })
if (!(Equal-Json @($installedBefore | Where-Object relativePath -NotIn $assetPaths) @($candidate | Where-Object relativePath -NotIn $assetPaths))) { throw 'The frontend package changed backend/application bits.' }
Assert-FinalDist; Assert-Inventory $installed $installedBefore
if (!(Equal-Json $hostBefore (Host-Identity $ExpectedProcessId)) -or !(Equal-Json $nativeBefore (Native-Identity)) -or $manifestHash -cne (Get-FileHash -LiteralPath $manifest).Hash -or !(Equal-Json $sourceBefore (Source-Inventory))) { throw 'Protected runtime or source changed during preparation.' }
[ordered]@{ preparedAt=[DateTimeOffset]::Now.ToString('o'); mode='static-assets-on-installed-backend'; packageRoot=$stage; packageHashes=$candidate; installedHashes=$installedBefore; host=$hostBefore; native=$nativeBefore; manifestHash=$manifestHash; sourceHashes=$sourceBefore; backendBitsMatchedInstalled=$true; savedDataFilesAccessed=$false; installedFilesChanged=$false; backendRestarted=$false; nativeRestarted=$false } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'preparation.json') -Encoding utf8NoBOM
Write-Output "Prepared $($candidate.Count) isolated application files with six final frontend assets. Live backend, native windows, launcher, and user storage remain untouched."


