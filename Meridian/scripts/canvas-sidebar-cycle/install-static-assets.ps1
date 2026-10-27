[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId,[ValidateRange(1,100)][int]$ExpectedTests=6,[switch]$ValidateOnly)
. (Join-Path $PSScriptRoot 'common.ps1')
$review = Get-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Raw | ConvertFrom-Json
foreach ($name in @('passed','ownedHostStopped','installedFilesUnchanged','packageFilesUnchanged','liveHostUnchanged','nativeWindowsUnchanged','launcherManifestUnchanged','sourceUnchanged')) { if ($review.$name -ne $true) { throw "Complete candidate review required: $name" } }
if ($review.mode -ne 'static-assets-on-installed-backend' -or $review.originalSavedDataFilesAccessed -ne $false -or $review.browserChecks -ne $ExpectedTests) { throw 'A complete fixture-only frontend review is required.' }
$original = Application-Inventory $installed
$candidate = Application-Inventory $stage
Assert-Inventory $installed $review.installedHashes
Assert-Inventory $stage $review.packageHashes
Assert-FinalDist; Assert-ApplicationFileSet $original $candidate
if (!(Equal-Json (Source-Inventory) $review.sourceHashes)) { throw 'Source changed since package review.' }
$assetPaths = @($staticNames | ForEach-Object { 'wwwroot\' + $_ })
$backendBefore = @($original | Where-Object relativePath -NotIn $assetPaths)
if (!(Equal-Json $backendBefore @($candidate | Where-Object relativePath -NotIn $assetPaths))) { throw 'Candidate backend/application bytes differ from installed bytes.' }
$hostBefore = Host-Identity $ExpectedProcessId
$nativeBefore = Native-Identity; Assert-ExpectedNative $nativeBefore
$manifestHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
if (!(Equal-Json $hostBefore $review.liveHost) -or !(Equal-Json $nativeBefore $review.native) -or $manifestHash -cne $review.manifestHash) { throw 'Protected runtime or launcher changed since review.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssffff'
$backup = Resolve-ApplicationFile $evidence ('static-application-backup-' + $stamp)
if (Test-Path -LiteralPath $backup) { throw 'Application backup path already exists.' }
$assets = @($staticNames | ForEach-Object {
    $relative = 'wwwroot\' + $_
    [pscustomobject]@{name=$_;relativePath=$relative;source=(Resolve-ApplicationFile $stage $relative);target=(Resolve-ApplicationFile $installed $relative);previousHash=($original | Where-Object relativePath -EQ $relative).sha256;reviewedHash=($candidate | Where-Object relativePath -EQ $relative).sha256;backup=(Resolve-ApplicationFile $backup $_)}
})
if ($ValidateOnly) {
    [ordered]@{validated=$true;mutationPerformed=$false;processId=$ExpectedProcessId;assetCount=$assets.Count;originalSavedDataFilesAccessed=$false;backendStopped=$false;nativeWindowsChanged=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'install-validation.json') -Encoding utf8NoBOM
    Write-Output "Validated six reviewed frontend assets. Backend PID $ExpectedProcessId and native sessions remain running. No installed files or saved data changed."
    return
}
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($asset in $assets) {
    Copy-Item -LiteralPath $asset.target -Destination $asset.backup
    if ((Get-FileHash -LiteralPath $asset.backup).Hash -cne $asset.previousHash) { throw 'Application assets changed during backup; no files replaced.' }
}
Assert-Inventory $installed $original
$assets | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'assets.json') -Encoding utf8NoBOM
$replaced = [Collections.Generic.List[object]]::new()
$temporary = [Collections.Generic.List[object]]::new()
$client = [Net.Http.HttpClient]::new(); $client.Timeout = [TimeSpan]::FromSeconds(10)
try {
    if (!(Equal-Json $hostBefore (Host-Identity $ExpectedProcessId)) -or !(Equal-Json $nativeBefore (Native-Identity))) { throw 'Runtime identity changed before frontend replacement.' }
    foreach ($asset in $assets) {
        if ((Get-FileHash -LiteralPath $asset.target).Hash -cne $asset.previousHash) { throw "Frontend file changed independently: $($asset.name)" }
        $temporaryPath = Resolve-ApplicationFile $installed ('wwwroot\.' + $asset.name + '.canvas-sidebar-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        $temporary.Add([pscustomobject]@{path=$temporaryPath;hash=$asset.reviewedHash})
        [IO.File]::Copy($asset.source,$temporaryPath,$false)
        if ((Get-FileHash -LiteralPath $temporaryPath).Hash -cne $asset.reviewedHash) { throw 'Temporary frontend bytes differ from reviewed candidate.' }
        [IO.File]::Replace($temporaryPath,$asset.target,[NullString]::Value)
        $replaced.Add($asset)
    }
    Assert-Inventory $installed $candidate
    $servedHashes = @()
    foreach ($asset in $assets) {
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,('http://127.0.0.1:5240/' + $asset.name + '?canvas-sidebar-review=' + $stamp))
        $null = $request.Headers.TryAddWithoutValidation('Accept-Encoding','identity')
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $null = $response.EnsureSuccessStatusCode()
            $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
            if ($hash -cne $asset.reviewedHash) { throw "Live host serves unexpected bytes: $($asset.name)" }
            $servedHashes += [pscustomobject]@{name=$asset.name;sha256=$hash}
        } finally { $response.Dispose(); $request.Dispose() }
    }
    if (!(Equal-Json $hostBefore (Host-Identity $ExpectedProcessId)) -or !(Equal-Json $nativeBefore (Native-Identity)) -or (Get-FileHash -LiteralPath $manifest).Hash -cne $manifestHash) { throw 'Protected runtime or launcher changed during frontend install.' }
    $backendAfter = @((Application-Inventory $installed) | Where-Object relativePath -NotIn $assetPaths)
    if (!(Equal-Json $backendBefore $backendAfter)) { throw 'Protected backend/application files changed.' }
    [ordered]@{installedAt=[DateTimeOffset]::Now.ToString('o');mode='static-assets-only';backup=$backup;assetCount=$assets.Count;servedHashes=$servedHashes;backendFilesUnchanged=$true;hostUnchanged=$true;processId=$ExpectedProcessId;nativeWindowsUnchanged=$true;launcherUnchanged=$true;runtimeStopped=$false;originalSavedDataFilesAccessed=$false;host=$hostBefore;native=$nativeBefore} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $evidence 'static-asset-install.json') -Encoding utf8NoBOM
    Write-Output "Installed and verified six frontend assets. Backend PID $ExpectedProcessId and native windows stayed open. User storage was never accessed. Application backup: $backup"
} catch {
    $failure = $_; $rollback = @()
    foreach ($asset in $replaced.ToArray()) {
        if ((Get-FileHash -LiteralPath $asset.target).Hash -cne $asset.reviewedHash -or (Get-FileHash -LiteralPath $asset.backup).Hash -cne $asset.previousHash) { $rollback += [pscustomobject]@{name=$asset.name;restored=$false;reason='Current or backup bytes changed independently; preserved.'}; continue }
        $restore = Resolve-ApplicationFile $installed ('wwwroot\.' + $asset.name + '.canvas-sidebar-rollback-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        $temporary.Add([pscustomobject]@{path=$restore;hash=$asset.previousHash})
        [IO.File]::Copy($asset.backup,$restore,$false)
        if ((Get-FileHash -LiteralPath $asset.target).Hash -cne $asset.reviewedHash) { throw 'Target changed before rollback; preserve the external edit.' }
        [IO.File]::Replace($restore,$asset.target,[NullString]::Value)
        $rollback += [pscustomobject]@{name=$asset.name;restored=$true}
    }
    [ordered]@{failedAt=[DateTimeOffset]::Now.ToString('o');message=$failure.Exception.Message;backup=$backup;rollback=$rollback;runtimeStopped=$false;originalSavedDataFilesAccessed=$false} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'static-install-failure.json') -Encoding utf8NoBOM
    throw $failure
} finally {
    $client.Dispose()
    foreach ($item in $temporary) { if ((Test-Path -LiteralPath $item.path -PathType Leaf) -and (Get-FileHash -LiteralPath $item.path).Hash -ceq $item.hash) { Remove-Item -LiteralPath $item.path -Force } }
}
