[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId,[switch]$ValidateOnly)
. (Join-Path $PSScriptRoot 'common.ps1')
$review=Get-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Raw | ConvertFrom-Json
foreach ($name in @('passed','ownedHostStopped','installedFilesUnchanged','packageFilesUnchanged','liveHostUnchanged','nativeWindowsUnchanged','hostSchemaUnchanged','launcherManifestUnchanged')) { if ($review.$name -ne $true) { throw "A complete reviewed static package is required: $name" } }
if ($review.mode -ne 'static-only-fixture-package' -or $review.originalSavedDataFilesAccessed -ne $false -or $review.browserChecks -lt 1 -or $review.packageRoot -ne $stage) { throw 'Fixture-only final static package proof is required.' }
Assert-Inventory $stage $review.packageHashes
Assert-Inventory $installed $review.installedHashes
Assert-StaticOnly $review.installedHashes $review.packageHashes
Assert-FinalDist
$hostBefore=Host-Identity $ExpectedProcessId
$nativeBefore=Native-Identity
function Assert-Runtime {
    if (!(Equal-Json (Host-Identity $ExpectedProcessId) $hostBefore) -or !(Equal-Json (Native-Identity) $nativeBefore) -or
        !(Equal-Json (Host-Schema-Inventory) $review.hostSchemaHashes) -or (Get-FileHash -LiteralPath $manifest).Hash -cne $review.manifestHash) { throw 'A protected backend, native, schema or launcher identity changed. No process will be stopped.' }
}
if (!(Equal-Json $hostBefore $review.liveHost) -or !(Equal-Json $nativeBefore $review.native)) { throw 'Runtime identities changed after package review.' }
Assert-Runtime
$originalMap=@{}; foreach ($file in $review.installedHashes) { $originalMap[$file.relativePath]=$file.sha256 }
$packageMap=@{}; foreach ($file in $review.packageHashes) { $packageMap[$file.relativePath]=$file.sha256 }
$expectedInstalled=@($review.installedHashes | ForEach-Object { [pscustomobject]@{ relativePath=$_.relativePath; sha256=if ($_.relativePath -in $staticPaths) { $packageMap[$_.relativePath] } else { $_.sha256 } } })
if ($ValidateOnly) {
    [ordered]@{ validated=$true; mutationPerformed=$false; mode='static-only'; processId=$ExpectedProcessId; packageFiles=$review.packageHashes.Count; replacementFiles=$staticPaths.Count; savedDataFilesAccessed=$false; backendRestarted=$false; nativeWindowsChanged=$false; hostSchemaUnchanged=$true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'install-validation.json') -Encoding utf8NoBOM
    Write-Output "Validated six static replacements in $($review.packageHashes.Count) reviewed application files. Backend PID$ExpectedProcessId and native windows remain running."
    return
}
$stamp=Get-Date -Format 'yyyyMMdd-HHmmssffff'
$backup=Join-Path $evidence ("application-file-backup-$stamp")
$replacement=Join-Path $evidence ("replacement-files-$stamp")
if ((Test-Path -LiteralPath $backup) -or (Test-Path -LiteralPath $replacement)) { throw 'Deployment backup/temporary path already exists. Preserve it.' }
New-Item -ItemType Directory -Path $backup,$replacement | Out-Null
foreach ($relative in $staticPaths) {
    $copy=Resolve-ApplicationFile $backup $relative
    $fresh=Resolve-ApplicationFile $replacement $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $copy),(Split-Path -Parent $fresh) -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-ApplicationFile $installed $relative) -Destination $copy
    Copy-Item -LiteralPath (Resolve-ApplicationFile $stage $relative) -Destination $fresh
    if ((Get-FileHash -LiteralPath $copy).Hash -cne $originalMap[$relative] -or (Get-FileHash -LiteralPath $fresh).Hash -cne $packageMap[$relative]) { throw 'An application static file changed while preparing atomic replacements.' }
}
[ordered]@{ backedUpAt=[DateTimeOffset]::Now.ToString('o'); originalStaticHashes=@($review.installedHashes | Where-Object relativePath -In $staticPaths); installedHashes=$review.installedHashes; packageHashes=$review.packageHashes; savedDataFilesAccessed=$false } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $backup 'application-hashes.json') -Encoding utf8NoBOM
Assert-Inventory $installed $review.installedHashes
Assert-Inventory $stage $review.packageHashes
Assert-Runtime
$applied=[Collections.Generic.List[string]]::new()
try {
    foreach ($relative in $staticPaths) {
        $target=Resolve-ApplicationFile $installed $relative
        if ((Get-FileHash -LiteralPath $target).Hash -cne $originalMap[$relative]) { throw "Installed static file changed independently: $relative" }
        $applied.Add($relative)
        # Both application and evidence roots are on the same checked volume.
        # Rename each fully hashed file atomically; never stream partial JS/CSS
        # into the running application's serving directory.
        [IO.File]::Move((Resolve-ApplicationFile $replacement $relative),$target,$true)
    }
    Assert-Inventory $installed $expectedInstalled
    Assert-Runtime
    $http=[Net.Http.HttpClient]::new(); $http.Timeout=[TimeSpan]::FromSeconds(10)
    try {
        foreach ($name in $staticNames) {
            $bytes=$http.GetByteArrayAsync("http://127.0.0.1:5240/$name").GetAwaiter().GetResult()
            $served=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
            if ($served -cne $packageMap["wwwroot\$name"]) { throw "Live static response differs from reviewed package: $name" }
        }
    } finally { $http.Dispose() }
    Assert-Inventory $installed $expectedInstalled
    Assert-Runtime
    [ordered]@{ installed=$true; installedAt=[DateTimeOffset]::Now.ToString('o'); mode='static-only'; processId=$ExpectedProcessId; backup=$backup; replacementFiles=$staticPaths.Count; packageFiles=$review.packageHashes.Count; servedFrontendHashesVerified=$true; hostSchemaUnchanged=$true; backendRestarted=$false; nativeWindowsUnchanged=$true; launcherUnchanged=$true; savedDataFilesAccessed=$false; savedDataRestored=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'static-install.json') -Encoding utf8NoBOM
    Write-Output "Installed six reviewed static files and verified served bytes. Backend PID$ExpectedProcessId and all native windows stayed running. Application static backup: $backup"
} catch {
    $failure=$_
    try {
        Assert-Runtime
        foreach ($relative in $applied) {
            $target=Resolve-ApplicationFile $installed $relative
            $current=(Get-FileHash -LiteralPath $target).Hash
            if ($current -ceq $originalMap[$relative]) { continue }
            if ($current -cne $packageMap[$relative]) { throw "A static file changed independently; preserve it: $relative" }
            $original=Resolve-ApplicationFile $backup $relative
            if ((Get-FileHash -LiteralPath $original).Hash -cne $originalMap[$relative]) { throw 'Original application static backup changed; stop rollback.' }
            $rollback=Resolve-ApplicationFile $replacement ("rollback/$relative")
            New-Item -ItemType Directory -Path (Split-Path -Parent $rollback) -Force | Out-Null
            Copy-Item -LiteralPath $original -Destination $rollback
            if ((Get-FileHash -LiteralPath $rollback).Hash -cne $originalMap[$relative]) { throw 'Rollback application file differs.' }
            [IO.File]::Move($rollback,$target,$true)
        }
        Assert-Inventory $installed $review.installedHashes
        Assert-Runtime
        Write-Output 'Original static application files restored. Running backend, native windows and current saved data stayed untouched.'
    } catch { throw [InvalidOperationException]::new("Static install failed: $($failure.Exception.Message) Rollback stopped safely: $($_.Exception.Message) Application file backup: $backup",$_.Exception) }
    throw $failure
}
