[CmdletBinding()]
param([Parameter(Mandatory = $true)][int]$ExpectedProcessId,[Parameter(Mandatory = $true)][ValidateRange(1,100)][int]$ExpectedTests,[switch]$ValidateOnly,[switch]$SavedCheckpointVerified)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($workspace -ne 'I:\dev\Meridian') { throw 'Unexpected Meridian workspace.' }
$stage = Join-Path $workspace 'output/plain-tray-20261018/package/app'
$installed = Join-Path $workspace 'output/app'
$executable = Join-Path $installed 'Meridian.Host.exe'
$evidence = Join-Path $workspace 'output/plain-tray-20261018/package/qa'
$manifest = Join-Path $workspace 'output/meridian.app.json'
$storage = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian'))
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssffff'
$backup = Join-Path $evidence ('application-backup-' + $stamp)
$review = Get-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Raw | ConvertFrom-Json
foreach ($name in @('passed','ownedHostStopped','installedFilesUnchanged','packageFilesUnchanged','liveHostUnchanged','nativeWindowsUnchanged','launcherManifestUnchanged','sourceUnchanged')) { if ($review.$name -ne $true) { throw "Verified package evidence is required: $name" } }
if ($review.mode -ne 'full-host-fixture-only' -or $review.originalSavedDataFilesAccessed -ne $false -or $review.browserChecks -ne $ExpectedTests) { throw 'A complete fixture-only review of the updated host is required.' }
function Resolve-AppFile([string]$Root,[string]$Relative) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (!$rootPath.StartsWith($workspace + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Application root escaped the workspace.' }
    $resolved = [IO.Path]::GetFullPath((Join-Path $rootPath $Relative))
    if (!$resolved.StartsWith($rootPath + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Application file escaped its intended root.' }
    $probe = $resolved
    while ($probe.Length -ge $workspace.Length) {
        if ((Test-Path -LiteralPath $probe) -and ((Get-Item -LiteralPath $probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Application links cannot be replaced: $probe" }
        if ($probe -eq $workspace) { break }; $probe = [IO.Path]::GetDirectoryName($probe)
    }
    return $resolved
}
function Inventory([string]$Root) {
    @(Get-ChildItem -LiteralPath $Root -Recurse -Force -File | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($Root,$_.FullName)
        [pscustomobject]@{ relativePath = $relative; sha256 = (Get-FileHash -LiteralPath (Resolve-AppFile $Root $relative) -Algorithm SHA256).Hash }
    } | Sort-Object relativePath)
}
function Equal-Json($A,$B) { ($A | ConvertTo-Json -Depth 6 -Compress) -ceq ($B | ConvertTo-Json -Depth 6 -Compress) }
function Native-Identity { @(Get-CimInstance Win32_Process -Filter "Name='Hypershell.exe'" | Where-Object CommandLine -Match '(?:^|\s)--mynet-desktop(?:\s|$)' | Select-Object ProcessId,ExecutablePath,CommandLine,CreationDate | Sort-Object ProcessId) }
function Host-Identity {
    $found = @(Get-CimInstance Win32_Process -Filter "Name='Meridian.Host.exe'" | Where-Object ExecutablePath -EQ $executable)
    $owners = @(Get-NetTCPConnection -LocalPort 5240 -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($found.Count -ne 1 -or $found[0].ProcessId -ne $ExpectedProcessId -or $found[0].CommandLine -notmatch 'http://127\.0\.0\.1:5240' -or $owners.Count -ne 1 -or $owners[0] -ne $ExpectedProcessId) { throw 'Installed host identity changed; no process will be stopped.' }
    # The reviewed live host uses the standard storage default and loopback args.
    # Never silently substitute a nonstandard project storage path on restart.
    if ($found[0].CommandLine -match '--data-root' -or $found[0].CommandLine -notmatch '^"?[^\r\n]+Meridian\.Host\.exe"?\s+--urls\s+http://127\.0\.0\.1:5240\s*$') { throw 'Host startup arguments are nonstandard; inspect before replacement.' }
    $found[0] | Select-Object ProcessId,ExecutablePath,CommandLine,CreationDate
}
function Assert-App([string]$Root,$Expected) { if (!(Equal-Json (Inventory $Root) $Expected)) { throw "Application bytes changed: $Root" } }
function Acquire-StorageLock {
    # Access the ownership lock only. Saved project, library and recovery files
    # are never read, copied, enumerated, deleted or restored by this installer.
    $lockPath = [IO.Path]::GetFullPath((Join-Path $storage '.host.lock'))
    if (!$lockPath.StartsWith($storage + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Storage ownership lock escaped its root.' }
    if ((Get-Item -LiteralPath $storage).Attributes -band [IO.FileAttributes]::ReparsePoint -or (Test-Path -LiteralPath $lockPath) -and ((Get-Item -LiteralPath $lockPath).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Storage ownership cannot follow filesystem links.' }
    for ($attempt=0; $attempt -lt 40; $attempt++) {
        try { return [IO.File]::Open($lockPath,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None) }
        catch [IO.IOException] { if ($attempt -eq 39) { throw }; Start-Sleep -Milliseconds 100 }
    }
}
function Start-InstalledHost([string]$Label) {
    Start-Process -FilePath $executable -WorkingDirectory $installed -ArgumentList @('--urls','http://127.0.0.1:5240') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence "$Label-$stamp.stdout.log") -RedirectStandardError (Join-Path $evidence "$Label-$stamp.stderr.log")
}
function Wait-Ready([System.Diagnostics.Process]$Process) {
    for ($attempt=0; $attempt -lt 100; $attempt++) {
        $Process.Refresh(); if ($Process.HasExited) { throw 'Replacement backend exited before readiness.' }
        try {
            $status = Invoke-RestMethod 'http://127.0.0.1:5240/__meridian_status' -TimeoutSec 1
            $owners = @(Get-NetTCPConnection -LocalPort 5240 -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
            if ($status.application -eq 'atlas-meridian' -and $owners.Count -eq 1 -and $owners[0] -eq $Process.Id) { return $status }
        } catch { }
        Start-Sleep -Milliseconds 100
    }
    throw 'Replacement backend did not own the ready live listener.'
}
$packageFiles = Inventory $stage
$originalFiles = Inventory $installed
if (!(Equal-Json $packageFiles $review.packageHashes) -or !(Equal-Json $originalFiles $review.installedHashes)) { throw 'The package or installed inventory changed after review.' }
Assert-ApplicationFileSet $originalFiles $packageFiles
if (!(Equal-Json (Source-Inventory) $review.sourceHashes)) { throw 'Production source changed after the full candidate review; rebuild and verify it.' }
foreach ($name in @('index.html','editor.js','editor.js.map','viewer.js','app.css','scene.css')) {
    if ((Get-FileHash -LiteralPath (Resolve-AppFile $stage "wwwroot/$name")).Hash -ne (Get-FileHash -LiteralPath (Resolve-AppFile (Join-Path $workspace 'dist') $name)).Hash) { throw 'The frontend changed after package review.' }
}
$hostBefore = Host-Identity
$nativeBefore = Native-Identity
Assert-ExpectedNative $nativeBefore
if (!(Equal-Json $hostBefore $review.liveHost) -or !(Equal-Json $nativeBefore $review.native)) { throw 'Live host or native window identities changed since review.' }
$manifestHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
if ($manifestHash -ne $review.manifestHash) { throw 'The launcher manifest changed after review.' }
if ($ValidateOnly) {
    [ordered]@{ validated = $true; mutationPerformed = $false; processId = $ExpectedProcessId; packageFiles = $packageFiles.Count; savedDataFilesAccessed = $false; nativeWindowsChanged = $false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'install-validation.json') -Encoding utf8NoBOM
    Write-Output "Validated $($packageFiles.Count) reviewed package files. Live host PID $ExpectedProcessId and all native windows remain running."
    return
}
if (!$SavedCheckpointVerified) { throw 'Before restarting the backend, finish editing and verify File > Save reports all changes saved in each open Meridian window. Then rerun with -SavedCheckpointVerified. No process or application files were changed.' }
if (Test-Path -LiteralPath $backup) { throw 'Application backup path already exists.' }
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($file in $originalFiles) {
    $to = Resolve-AppFile $backup $file.relativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
    Copy-Item -LiteralPath (Resolve-AppFile $installed $file.relativePath) -Destination $to
}
Assert-App $backup $originalFiles
Assert-App $installed $originalFiles
Assert-App $stage $packageFiles
$originalMap = @{}; foreach ($file in $originalFiles) { $originalMap[$file.relativePath] = $file.sha256 }
$packageMap = @{}; foreach ($file in $packageFiles) { $packageMap[$file.relativePath] = $file.sha256 }
$oldProcess = $null; $replacement = $null; $lock = $null; $copied = $false
try {
    $oldProcess = Get-Process -Id $ExpectedProcessId; $null = $oldProcess.Handle
    if (!(Equal-Json (Host-Identity) $hostBefore) -or !(Equal-Json (Native-Identity) $nativeBefore)) { throw 'A runtime identity changed before replacement.' }
    $oldProcess.Kill(); if (!$oldProcess.WaitForExit(10000)) { throw 'Original backend did not exit; files remain untouched.' }
    $lock = Acquire-StorageLock
    if (@(Get-NetTCPConnection -LocalPort 5240 -State Listen -ErrorAction SilentlyContinue).Count) { throw 'Another process owns the live port; leave files untouched.' }
    Assert-App $installed $originalFiles; Assert-App $stage $packageFiles
    $copied = $true
    foreach ($file in $packageFiles) {
        $target = Resolve-AppFile $installed $file.relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        $temporary = Resolve-AppFile $installed ($file.relativePath + '.plain-tray-install-' + $stamp)
        if (Test-Path -LiteralPath $temporary) { throw 'Replacement temporary file already exists.' }
        try {
            [IO.File]::Copy((Resolve-AppFile $stage $file.relativePath),$temporary,$false)
            if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -cne $file.sha256) { throw 'Replacement temporary bytes differ from the reviewed candidate.' }
            [IO.File]::Move($temporary,$target,$true)
        } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
    }
    Assert-App $installed $packageFiles
    if ((Get-FileHash -LiteralPath $manifest).Hash -ne $manifestHash -or !(Equal-Json (Native-Identity) $nativeBefore)) { throw 'A protected launcher or native identity changed.' }
    $lock.Dispose(); $lock = $null
    $replacement = Start-InstalledHost 'installed-host'; $status = Wait-Ready $replacement
    Assert-App $installed $packageFiles
    if (!(Equal-Json (Native-Identity) $nativeBefore)) { throw 'Native identities changed after backend startup.' }
    if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -cne $manifestHash) { throw 'Launcher manifest changed after backend startup.' }
    $servedHashes = @()
    foreach ($name in $staticNames) {
        $response = Invoke-WebRequest -Uri ("http://127.0.0.1:5240/" + $name) -TimeoutSec 10
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($response.RawContentStream.ToArray()))
        if ($digest -cne (Get-FileHash -LiteralPath (Resolve-AppFile $stage "wwwroot/$name") -Algorithm SHA256).Hash) { throw "The live host serves unexpected frontend bytes: $name" }
        $servedHashes += [pscustomobject]@{ relativePath="wwwroot\$name"; sha256=$digest }
    }
    [ordered]@{ installedAt = [DateTimeOffset]::Now.ToString('o'); processId = $replacement.Id; previousProcessId = $ExpectedProcessId; packageFiles = $packageFiles.Count; backup = $backup; nativeWindowsUnchanged = $true; launcherUnchanged = $true; savedDataFilesAccessed = $false; savedDataRestored = $false; nativeRestarted = $false; servedHashes=$servedHashes; status = $status } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'package-install.json') -Encoding utf8NoBOM
    Write-Output "Installed $($packageFiles.Count) reviewed files; backend restarted at PID $($replacement.Id). Native windows remain open. Saved data was never read, copied or restored. Application backup: $backup"
} catch {
    $failure = $_
    try {
        if ($replacement) {
            $replacement.Refresh()
            if (!$replacement.HasExited) {
                $identity = Get-CimInstance Win32_Process -Filter "ProcessId=$($replacement.Id)"
                if ($identity.ExecutablePath -ne $executable -or $identity.CommandLine -notmatch 'http://127\.0\.0\.1:5240') { throw 'Replacement identity changed; rollback will not stop it.' }
                $replacement.Kill(); if (!$replacement.WaitForExit(10000)) { throw 'Replacement did not exit.' }
            }
        }
        if ($oldProcess -and !$oldProcess.HasExited) { throw 'Original backend remains running; do not replace files.' }
        if ($oldProcess -and $oldProcess.HasExited) {
            if (!$lock) { $lock = Acquire-StorageLock }
            if (@(Get-NetTCPConnection -LocalPort 5240 -State Listen -ErrorAction SilentlyContinue).Count) { throw 'Another process owns the live port; do not replace files or start another backend.' }
            if ($copied) {
                Assert-App $backup $originalFiles
                foreach ($file in (Inventory $installed)) {
                    $old = $originalMap[$file.relativePath]; $new = $packageMap[$file.relativePath]
                    if ($file.sha256 -ne $old -and $file.sha256 -ne $new) { throw "An installed file changed independently; preserve it: $($file.relativePath)" }
                }
                foreach ($file in $packageFiles | Where-Object { !$originalMap.ContainsKey($_.relativePath) }) {
                    $target = Resolve-AppFile $installed $file.relativePath
                    if (Test-Path -LiteralPath $target) {
                        if ((Get-FileHash -LiteralPath $target).Hash -ne $file.sha256) { throw 'New package file changed before rollback deletion.' }
                        Remove-Item -LiteralPath $target -Force
                    }
                }
                foreach ($file in $originalFiles) { Copy-Item -LiteralPath (Resolve-AppFile $backup $file.relativePath) -Destination (Resolve-AppFile $installed $file.relativePath) -Force }
                Assert-App $installed $originalFiles
            }
            if ((Get-FileHash -LiteralPath $manifest).Hash -ne $manifestHash) { throw 'Launcher changed; inspect before restarting original backend.' }
            $lock.Dispose(); $lock = $null
            $rollback = Start-InstalledHost 'rollback-host'; $null = Wait-Ready $rollback
            Write-Output "Original application backend restored at PID $($rollback.Id). Current saved data was preserved."
        }
    } catch { throw [InvalidOperationException]::new("Install failed: $($failure.Exception.Message) Rollback stopped safely: $($_.Exception.Message) Application backup: $backup",$_.Exception) }
    throw $failure
} finally { if ($lock) { $lock.Dispose() } }






