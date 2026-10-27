[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId,[ValidateRange(1,100)][int]$ExpectedTests=12,[switch]$ValidateOnly)
. (Join-Path $PSScriptRoot 'common.ps1')
$review = Get-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Raw | ConvertFrom-Json
foreach ($flag in @('passed','ownedHostStopped','installedFilesUnchanged','packageFilesUnchanged','liveHostUnchanged','nativeWindowsUnchanged','launcherManifestUnchanged','sourceUnchanged')) { if ($review.$flag -ne $true) { throw "Final review missing: $flag" } }
if ($review.mode -ne 'static-frontend-fixture-only' -or $review.originalSavedDataFilesAccessed -ne $false -or $review.browserChecks -ne $ExpectedTests) { throw 'A complete isolated frontend review is required.' }
$original = Application-Inventory $installed; $candidate = Application-Inventory $stage
if (!(Equal-Json $original $review.installedHashes) -or !(Equal-Json $candidate $review.packageHashes) -or !(Equal-Json (Source-Inventory) $review.sourceHashes)) { throw 'Reviewed source or application bytes changed.' }
Assert-ApplicationFileSet $original $candidate; Assert-FinalDist
$allowed = @($staticNames | ForEach-Object { 'wwwroot\' + $_ })
if (!(Equal-Json @($original | Where-Object relativePath -NotIn $allowed) @($candidate | Where-Object relativePath -NotIn $allowed))) { throw 'Frontend update cannot replace backend or unrelated files.' }
$hostBefore = Host-Identity $ExpectedProcessId; $nativeBefore = Native-Identity; Assert-ExpectedNative $nativeBefore
$manifestHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
if (!(Equal-Json $hostBefore $review.liveHost) -or !(Equal-Json $nativeBefore $review.native) -or $manifestHash -cne $review.manifestHash) { throw 'Protected runtime changed after review.' }
if ($ValidateOnly) {
    [ordered]@{validated=$true;mutationPerformed=$false;processId=$ExpectedProcessId;packageFiles=$candidate.Count;backendRestarted=$false;savedDataFilesAccessed=$false;nativeWindowsChanged=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'install-validation.json') -Encoding utf8NoBOM
    Write-Output 'Validated frontend-only update. No process or application file changed.'
    return
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssffff'
$backup = Join-Path $evidence ('frontend-backup-' + $stamp)
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($name in $staticNames) { Copy-Item -LiteralPath (Resolve-ApplicationFile $installed "wwwroot/$name") -Destination (Resolve-ApplicationFile $backup $name) }
foreach ($name in $staticNames) { if ((Get-FileHash -LiteralPath (Resolve-ApplicationFile $backup $name)).Hash -cne (Get-FileHash -LiteralPath (Resolve-ApplicationFile $installed "wwwroot/$name")).Hash) { throw 'Frontend backup differs.' } }
function Replace-Asset([string]$Source,[string]$Target) {
    $temporary = Resolve-ApplicationFile $installed ([IO.Path]::GetRelativePath($installed,$Target) + '.nested-install-' + $stamp)
    if (Test-Path -LiteralPath $temporary) { throw 'Temporary asset already exists.' }
    try { [IO.File]::Copy($Source,$temporary,$false); if ((Get-FileHash -LiteralPath $temporary).Hash -cne (Get-FileHash -LiteralPath $Source).Hash) { throw 'Temporary asset hash mismatch.' }; [IO.File]::Move($temporary,$Target,$true) }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
try {
    Assert-Inventory $installed $original; Assert-Inventory $stage $candidate
    # Publish the entry point after its assets. Existing windows retain their
    # loaded code; no backend restart or forced refresh is performed.
    foreach ($name in @($staticNames | Where-Object { $_ -ne 'index.html' }) + @('index.html')) { Replace-Asset (Resolve-ApplicationFile $stage "wwwroot/$name") (Resolve-ApplicationFile $installed "wwwroot/$name") }
    Assert-Inventory $installed $candidate
    $served = @()
    foreach ($name in $staticNames) {
        $response = Invoke-WebRequest -Uri ("http://127.0.0.1:5240/"+$name) -TimeoutSec 10
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($response.RawContentStream.ToArray()))
        if ($hash -cne (Get-FileHash -LiteralPath (Resolve-ApplicationFile $stage "wwwroot/$name")).Hash) { throw "Live frontend differs: $name" }
        $served += [pscustomobject]@{relativePath="wwwroot\$name";sha256=$hash}
    }
    if (!(Equal-Json $hostBefore (Host-Identity $ExpectedProcessId)) -or !(Equal-Json $nativeBefore (Native-Identity)) -or $manifestHash -cne (Get-FileHash -LiteralPath $manifest).Hash) { throw 'Protected runtime changed.' }
    [ordered]@{installedAt=[DateTimeOffset]::Now.ToString('o');processId=$ExpectedProcessId;frontendFiles=$staticNames.Count;backup=$backup;backendRestarted=$false;nativeRestarted=$false;nativeWindowsUnchanged=$true;launcherUnchanged=$true;savedDataFilesAccessed=$false;servedHashes=$served} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'package-install.json') -Encoding utf8NoBOM
    Write-Output 'Installed and verified six frontend files. Backend and native windows remained running.'
} catch {
    $failure = $_
    foreach ($name in $staticNames) {
        $target = Resolve-ApplicationFile $installed "wwwroot/$name"
        $hash = (Get-FileHash -LiteralPath $target).Hash
        if ($hash -cne (Get-FileHash -LiteralPath (Resolve-ApplicationFile $backup $name)).Hash -and $hash -cne (Get-FileHash -LiteralPath (Resolve-ApplicationFile $stage "wwwroot/$name")).Hash) { throw "Preserve independently changed asset: $target. Original error: $failure" }
    }
    foreach ($name in $staticNames) { Replace-Asset (Resolve-ApplicationFile $backup $name) (Resolve-ApplicationFile $installed "wwwroot/$name") }
    Assert-Inventory $installed $original
    throw $failure
}
