[CmdletBinding()]
param([Parameter(Mandatory = $true)][int]$ExpectedProcessId,[switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($workspace -ne 'I:\dev\Meridian') { throw 'Unexpected Meridian workspace.' }
$stage = Join-Path $workspace 'output/canvas-launcher-package-20261010/app'
$installed = Join-Path $workspace 'output/app'
$targetRoot = Join-Path $installed 'wwwroot'
$evidence = Join-Path $workspace 'output/canvas-launcher-qa-20261010'
$manifestPath = Join-Path $workspace 'output/meridian.app.json'
$names = @('index.html','editor.js','editor.js.map','viewer.js','app.css','scene.css')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssffff'
$backupRoot = Join-Path $evidence ('static-install-backup-' + $stamp)
$review = Get-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Raw | ConvertFrom-Json
$protection = Get-Content -LiteralPath (Join-Path $evidence 'package-protected-after.json') -Raw | ConvertFrom-Json
if ($review.passed -ne $true -or $review.reviewMode -ne 'static-assets-on-installed-backend' -or $review.fixtureOnly -ne $true -or $review.originalDataAccessed -ne $false -or $review.backendBitsMatchedInstalled -ne $true -or $review.fixtureUnchanged -ne $true) { throw 'A successful static-only review against the installed backend is required.' }
foreach ($name in @('succeeded','ownedHostStopped','fixtureOnly','backendBitsMatchedInstalled','installedFilesUnchanged','liveHostUnchanged','nativeWindowsUnchanged','launcherManifestUnchanged')) { if ($protection.$name -ne $true) { throw "Isolated static-asset verification did not complete safely: $name" } }
if ($protection.originalDataAccessed -ne $false -or !$review.packageHashes -or @($review.browserErrors).Count -or @($review.remoteRequests).Count -or @($review.blockedWrites).Count) { throw 'Static-only review evidence is incomplete.' }

function Resolve-WorkspaceFile([string]$Root,[string]$RelativePath) {
    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (!$resolvedRoot.StartsWith($workspace + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'An application/evidence path is outside the workspace.' }
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot $RelativePath))
    if (!$resolved.StartsWith($resolvedRoot + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'An application/evidence file escaped its intended directory.' }
    $probe = $resolved
    while ($probe.Length -ge $workspace.Length) {
        if (Test-Path -LiteralPath $probe) { if ((Get-Item -LiteralPath $probe -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "Application links are not eligible for replacement: $probe" } }
        if ($probe -eq $workspace) { break }
        $probe = [System.IO.Path]::GetDirectoryName($probe)
    }
    return $resolved
}
function Inventory([string]$Root) {
    @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force | ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($Root,$_.FullName)
        $verified = Resolve-WorkspaceFile $Root $relative
        [pscustomobject]@{ relativePath = $relative; sha256 = (Get-FileHash -LiteralPath $verified -Algorithm SHA256).Hash }
    } | Sort-Object relativePath)
}
function Native-Identity { @(Get-CimInstance Win32_Process -Filter "Name='Hypershell.exe'" | Select-Object ProcessId,ExecutablePath,CommandLine,CreationDate | Sort-Object ProcessId) }
function Host-Identity {
    $hosts = @(Get-CimInstance Win32_Process -Filter "Name='Meridian.Host.exe'" | Where-Object ExecutablePath -EQ (Join-Path $installed 'Meridian.Host.exe'))
    if ($hosts.Count -ne 1 -or $hosts[0].ProcessId -ne $ExpectedProcessId -or $hosts[0].CommandLine -notmatch 'http://127\.0\.0\.1:5240') { throw 'The installed backend identity changed; no runtime will be stopped.' }
    $listeners = @(Get-NetTCPConnection -LocalPort 5240 -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($listeners.Count -ne 1 -or $listeners[0] -ne $ExpectedProcessId) { throw 'The installed backend no longer owns the live listener.' }
    $hosts[0] | Select-Object ProcessId,ExecutablePath,CommandLine,CreationDate
}
function Equal-Json($Before,$After) { ($Before | ConvertTo-Json -Compress -Depth 5) -ceq ($After | ConvertTo-Json -Compress -Depth 5) }

$stageInventory = Inventory $stage
if ($stageInventory.Count -ne @($review.packageHashes).Count) { throw 'The reviewed staging inventory changed.' }
foreach ($file in $stageInventory) {
    $reviewedPath = Resolve-WorkspaceFile $stage $file.relativePath
    $matching = @($review.packageHashes | Where-Object { [System.IO.Path]::GetFullPath($_.path) -ieq $reviewedPath })
    if ($matching.Count -ne 1 -or $matching[0].sha256 -ine $file.sha256) { throw "A staged file is not covered by its exact reviewed hash: $reviewedPath" }
}
$installedBefore = Inventory $installed
$assetPaths = @($names | ForEach-Object { 'wwwroot\' + $_ })
$backendBefore = @($installedBefore | Where-Object relativePath -NotIn $assetPaths)
$stageBackend = @($stageInventory | Where-Object relativePath -NotIn $assetPaths)
if (!(Equal-Json $backendBefore $stageBackend)) { throw 'Reviewed staging no longer has exactly the installed backend/application bits.' }
$hostBefore = Host-Identity
$nativeBefore = Native-Identity
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$assets = @($names | ForEach-Object {
    $source = Resolve-WorkspaceFile (Join-Path $stage 'wwwroot') $_
    $destination = Resolve-WorkspaceFile $targetRoot $_
    $built = Resolve-WorkspaceFile (Join-Path $workspace 'dist') $_
    if (!(Test-Path -LiteralPath $source -PathType Leaf) -or !(Test-Path -LiteralPath $destination -PathType Leaf)) { throw 'Every reviewed frontend file must exist in staging and the installed app.' }
    $newHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash -ne $newHash) { throw "The frontend build changed after review: $_" }
    [pscustomobject]@{ name = $_; source = $source; destination = $destination; previousHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash; reviewedHash = $newHash; backup = (Resolve-WorkspaceFile $backupRoot $_) }
})
if (Test-Path -LiteralPath $backupRoot) { throw 'The static-asset backup directory already exists.' }
if ($ValidateOnly) {
    [ordered]@{ validatedAt = [DateTimeOffset]::Now.ToString('o'); mode = 'static-assets-only'; expectedProcessId = $ExpectedProcessId; packageFiles = $stageInventory.Count; assetCount = $assets.Count; originalSavedDataAccessed = $false; mutationPerformed = $false; backendBitsMatchedInstalled = $true; assets = $assets } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'static-install-validation.json') -Encoding utf8NoBOM
    Write-Output "Validated $($assets.Count) reviewed frontend assets against backend PID $ExpectedProcessId. No installed files, runtime or saved data were changed."
    return
}
New-Item -ItemType Directory -Path $backupRoot | Out-Null
foreach ($asset in $assets) {
    Copy-Item -LiteralPath $asset.destination -Destination $asset.backup
    if ((Get-FileHash -LiteralPath $asset.backup -Algorithm SHA256).Hash -ne $asset.previousHash) { throw 'Installed assets changed during backup; nothing was replaced.' }
}
$assets | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backupRoot 'assets.json') -Encoding utf8NoBOM
$replaced = [System.Collections.Generic.List[object]]::new()
$temporary = [System.Collections.Generic.List[object]]::new()
$servedHashes = @()
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(10)
try {
    if (!(Equal-Json $hostBefore (Host-Identity)) -or !(Equal-Json $nativeBefore (Native-Identity))) { throw 'A live runtime/native identity changed before replacement.' }
    foreach ($asset in $assets) {
        if ((Get-FileHash -LiteralPath $asset.destination -Algorithm SHA256).Hash -ne $asset.previousHash) { throw "A frontend file changed after backup: $($asset.name)" }
        $temporaryPath = Resolve-WorkspaceFile $targetRoot ('.' + $asset.name + '.canvas-launcher-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        $temporary.Add([pscustomobject]@{ path = $temporaryPath; hash = $asset.reviewedHash })
        Copy-Item -LiteralPath $asset.source -Destination $temporaryPath
        if ((Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash -ne $asset.reviewedHash) { throw 'Prepared frontend bytes differ from their reviewed hash.' }
        [System.IO.File]::Replace($temporaryPath,$asset.destination,[NullString]::Value)
        $replaced.Add($asset)
        if ((Get-FileHash -LiteralPath $asset.destination -Algorithm SHA256).Hash -ne $asset.reviewedHash) { throw "Installed frontend hash mismatch: $($asset.name)" }
    }
    foreach ($asset in $assets) {
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get,('http://127.0.0.1:5240/' + $asset.name + '?canvas-launcher-review=' + $stamp))
        $request.Headers.TryAddWithoutValidation('Accept-Encoding','identity') | Out-Null
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $response.EnsureSuccessStatusCode() | Out-Null
            $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
            if ($hash -ne $asset.reviewedHash) { throw "The running backend serves different frontend bytes: $($asset.name)" }
            $servedHashes += [pscustomobject]@{ name = $asset.name; sha256 = $hash }
        } finally { $response.Dispose(); $request.Dispose() }
    }
    $hostAfter = Host-Identity; $nativeAfter = Native-Identity
    $backendAfter = @(Inventory $installed | Where-Object relativePath -NotIn $assetPaths)
    if (!(Equal-Json $backendBefore $backendAfter) -or !(Equal-Json $hostBefore $hostAfter) -or !(Equal-Json $nativeBefore $nativeAfter) -or (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $manifestHash) { throw 'A protected backend/native/launcher identity changed; only eligible frontend files may roll back.' }
    [ordered]@{ installedAt = [DateTimeOffset]::Now.ToString('o'); mode = 'static-assets-only'; backup = $backupRoot; assetCount = $assets.Count; assets = $assets; servedHashes = $servedHashes; backendFilesUnchanged = $true; hostUnchanged = $true; processId = $ExpectedProcessId; nativeWindowsUnchanged = $true; manifestUnchanged = $true; runtimeStopped = $false; originalSavedDataAccessed = $false; host = $hostAfter; native = $nativeAfter } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'static-asset-install.json') -Encoding utf8NoBOM
    Write-Output "Installed and verified $($assets.Count) frontend assets atomically. Backend PID $ExpectedProcessId and native sessions remain running; saved data was never accessed. Backup: $backupRoot"
} catch {
    $failure = $_; $rollback = @()
    foreach ($asset in @($replaced.ToArray()) | Select-Object -Last $replaced.Count) {
        if ((Get-FileHash -LiteralPath $asset.destination -Algorithm SHA256).Hash -ne $asset.reviewedHash) { $rollback += [pscustomobject]@{ name = $asset.name; restored = $false; reason = 'Current file differs from the reviewed bytes; preserve the external change.' }; continue }
        if ((Get-FileHash -LiteralPath $asset.backup -Algorithm SHA256).Hash -ne $asset.previousHash) { $rollback += [pscustomobject]@{ name = $asset.name; restored = $false; reason = 'Backup changed; preserve current bytes.' }; continue }
        $restorePath = Resolve-WorkspaceFile $targetRoot ('.' + $asset.name + '.canvas-launcher-rollback-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        $temporary.Add([pscustomobject]@{ path = $restorePath; hash = $asset.previousHash })
        Copy-Item -LiteralPath $asset.backup -Destination $restorePath
        if ((Get-FileHash -LiteralPath $asset.destination -Algorithm SHA256).Hash -eq $asset.reviewedHash) { [System.IO.File]::Replace($restorePath,$asset.destination,[NullString]::Value); $rollback += [pscustomobject]@{ name = $asset.name; restored = $true } }
    }
    [ordered]@{ failedAt = [DateTimeOffset]::Now.ToString('o'); message = $failure.Exception.Message; mode = 'static-assets-only'; backup = $backupRoot; rollback = $rollback; runtimeStopped = $false; originalSavedDataAccessed = $false } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'static-asset-install-failure.json') -Encoding utf8NoBOM
    throw $failure
} finally {
    $client.Dispose()
    foreach ($item in $temporary) {
        $verifiedTemporary = Resolve-WorkspaceFile $targetRoot ([System.IO.Path]::GetFileName($item.path))
        if ((Test-Path -LiteralPath $verifiedTemporary -PathType Leaf) -and (Get-FileHash -LiteralPath $verifiedTemporary -Algorithm SHA256).Hash -eq $item.hash) { Remove-Item -LiteralPath $verifiedTemporary -Force }
    }
}
