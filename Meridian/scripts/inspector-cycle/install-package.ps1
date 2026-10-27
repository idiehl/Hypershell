[CmdletBinding()]
param([int]$ExpectedProcessId)
$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($workspace -ne 'I:\dev\Meridian') { throw 'Unexpected workspace.' }
$source = Join-Path $workspace 'output/inspector-package/app'
$destination = Join-Path $workspace 'output/app'
$installedExecutable = Join-Path $destination 'Meridian.Host.exe'
$dataRoot = Join-Path $env:LOCALAPPDATA 'AtlasMeridian'
$evidence = Join-Path $workspace 'output/inspector-qa'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $evidence ('installation-backup-' + $stamp)
if (!(Test-Path -LiteralPath (Join-Path $source 'Meridian.Host.exe') -PathType Leaf)) { throw 'Verified package is missing.' }
$review = Get-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Raw | ConvertFrom-Json
if ($review.passed -ne $true) { throw 'A successful final package review is required.' }
$protection = Get-Content -LiteralPath (Join-Path $evidence 'package-protected-after.json') -Raw | ConvertFrom-Json
if ($protection.succeeded -ne $true -or $protection.ownedHostStopped -ne $true -or $protection.savedDataUnchanged -ne $true) { throw 'Isolated package verification did not complete safely.' }
if (!$review.packageHashes -or @($review.browserErrors).Count) { throw 'Package review evidence is incomplete.' }
foreach ($file in $review.packageHashes) {
  $reviewedPath = [System.IO.Path]::GetFullPath($file.path)
  if (!$reviewedPath.StartsWith($source + '\',[System.StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $reviewedPath -PathType Leaf)) { throw 'A reviewed hash is outside this package.' }
  if ((Get-FileHash -LiteralPath $reviewedPath -Algorithm SHA256).Hash -ine $file.sha256) { throw "Reviewed package bytes changed: $reviewedPath" }
}
foreach ($name in @('editor.js','viewer.js','app.css','scene.css','index.html')) {
  if ((Get-FileHash -LiteralPath (Join-Path $source "wwwroot/$name")).Hash -ne (Get-FileHash -LiteralPath (Join-Path $workspace "dist/$name")).Hash) { throw "The package is older than the final build: $name" }
}
# Package replacement must never change the trusted launcher definition or close
# WebView windows. Only the verified local backend process is restarted.
$manifestPath = Join-Path $workspace 'output/meridian.app.json'
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$runtime = @(Get-CimInstance Win32_Process -Filter "Name='Meridian.Host.exe'" | Where-Object ExecutablePath -EQ $installedExecutable)
if ($runtime.Count -ne 1 -or !$ExpectedProcessId -or $runtime[0].ProcessId -ne $ExpectedProcessId) { throw 'The installed host identity changed; inspect it before installing.' }
if ($runtime[0].CommandLine -notmatch 'http://127\.0\.0\.1:5240') { throw 'The installed host is using an unexpected address.' }
$listeners = @(Get-NetTCPConnection -LocalPort 5240 -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
if ($listeners.Count -ne 1 -or $listeners[0] -ne $ExpectedProcessId) { throw 'The live listener does not match the verified host.' }
function Snapshot-Content {
  @(Get-ChildItem -LiteralPath $dataRoot -File -Recurse | Where-Object Name -NE '.host.lock' | ForEach-Object {
    [pscustomobject]@{ Path=$_.FullName; Hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
  } | Sort-Object Path)
}
function Assert-Content($baseline) {
  $current = Snapshot-Content
  if (($baseline | ConvertTo-Json -Compress) -ne ($current | ConvertTo-Json -Compress)) { throw 'Saved content changed during installation; preserve and inspect the current files.' }
}
function Start-InstalledHost($label) {
  Start-Process -FilePath $installedExecutable -WorkingDirectory $destination -ArgumentList @('--urls','http://127.0.0.1:5240') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence "$label-$stamp.stdout.log") -RedirectStandardError (Join-Path $evidence "$label-$stamp.stderr.log")
}
function Resolve-AppFile([string]$Root,[string]$RelativePath) {
  $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
  if (!$resolvedRoot.StartsWith($workspace + '\',[System.StringComparison]::OrdinalIgnoreCase)) { throw 'An application path is outside the workspace.' }
  $resolved = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot $RelativePath))
  if (!$resolved.StartsWith($resolvedRoot + '\',[System.StringComparison]::OrdinalIgnoreCase)) { throw 'An application file is outside its verified directory.' }
  # A lexical boundary is insufficient if a file or parent directory is a link.
  $probe = $resolved
  while ($probe.Length -ge $resolvedRoot.Length) {
    if (Test-Path -LiteralPath $probe) {
      if ((Get-Item -LiteralPath $probe -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "Application links are not eligible for replacement: $probe" }
    }
    if ($probe -eq $resolvedRoot) { break }
    $probe = [System.IO.Path]::GetDirectoryName($probe)
  }
  return $resolved
}
function Snapshot-App([string]$Root) {
  $items = @(Get-ChildItem -LiteralPath $Root -Force -Recurse)
  if ((Get-Item -LiteralPath $Root -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint -or @($items | Where-Object { $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint }).Count) { throw 'Application directories must not contain links.' }
  @($items | Where-Object { !$_.PSIsContainer } | ForEach-Object {
    $relative = [System.IO.Path]::GetRelativePath($Root,$_.FullName)
    $verifiedPath = Resolve-AppFile $Root $relative
    [pscustomobject]@{ RelativePath=$relative; Hash=(Get-FileHash -LiteralPath $verifiedPath -Algorithm SHA256).Hash }
  } | Sort-Object RelativePath)
}
function Assert-AppSnapshot([string]$Root,$baseline) {
  $current = Snapshot-App $Root
  if (($baseline | ConvertTo-Json -Compress) -ne ($current | ConvertTo-Json -Compress)) { throw "Application files differ from their verified inventory: $Root" }
}
function Stop-OwnedReplacement([System.Diagnostics.Process]$process) {
  $process.Refresh()
  if (!$process.HasExited) {
    $owned = Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)"
    if ($owned.ExecutablePath -ne $installedExecutable -or $owned.CommandLine -notmatch 'http://127\.0\.0\.1:5240') { throw 'The replacement process identity changed; rollback will not stop it or replace files.' }
    # The retained Process object identifies the launched instance, avoiding a
    # new PID lookup after the identity check when requesting termination.
    $process.Kill()
  }
  if (!$process.WaitForExit(10000)) { throw 'The replacement backend did not exit; rollback files were left untouched.' }
}
function Acquire-StorageOwnership {
  $lockPath = Join-Path $dataRoot '.host.lock'
  # Windows can retain an exiting process's handles briefly. Wait for this
  # specific storage lock without touching another process or any saved files.
  for ($attempt=0; $attempt -lt 40; $attempt++) {
    try {
      return [System.IO.File]::Open($lockPath,[System.IO.FileMode]::OpenOrCreate,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None)
    } catch [System.IO.IOException] {
      if ($attempt -eq 39) { throw }
      Start-Sleep -Milliseconds 250
    }
  }
}
$content = Snapshot-Content
$originalApp = Snapshot-App $destination
$packageApp = Snapshot-App $source
$originalPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($file in $originalApp) { [void]$originalPaths.Add($file.RelativePath) }
$newOnlyFiles = @($packageApp | Where-Object { !$originalPaths.Contains($_.RelativePath) })
if (Test-Path -LiteralPath $backup) { throw 'The installation backup directory already exists; keep it intact and retry with a new timestamp.' }
New-Item -ItemType Directory -Path (Join-Path $backup 'app'),(Join-Path $backup 'data') -Force | Out-Null
Get-ChildItem -LiteralPath $destination -Force | Copy-Item -Destination (Join-Path $backup 'app') -Recurse -Force
Assert-AppSnapshot (Join-Path $backup 'app') $originalApp
Assert-AppSnapshot $destination $originalApp
Assert-AppSnapshot $source $packageApp
[ordered]@{ originalApp=$originalApp; packageApp=$packageApp; newOnlyFiles=$newOnlyFiles } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'package-hashes.json') -Encoding utf8NoBOM
foreach ($file in $content) {
  $relative = [System.IO.Path]::GetRelativePath($dataRoot,$file.Path)
  $copy = Join-Path (Join-Path $backup 'data') $relative
  New-Item -ItemType Directory -Path (Split-Path -Parent $copy) -Force | Out-Null
  Copy-Item -LiteralPath $file.Path -Destination $copy
  if ((Get-FileHash -LiteralPath $copy).Hash -ne $file.Hash) { throw 'Saved content changed during backup; host was left running.' }
}
Assert-Content $content
$newRuntime = $null
$previousRuntime = $null
$ownership = $null
$replaced = $false
try {
  $currentIdentity = Get-CimInstance Win32_Process -Filter "ProcessId=$ExpectedProcessId"
  if ($currentIdentity.ExecutablePath -ne $installedExecutable -or $currentIdentity.CommandLine -ne $runtime[0].CommandLine) { throw 'The host identity changed before replacement.' }
  Assert-AppSnapshot (Join-Path $backup 'app') $originalApp
  Assert-AppSnapshot $destination $originalApp
  Assert-AppSnapshot $source $packageApp
  $previousRuntime = Get-Process -Id $ExpectedProcessId
  Stop-Process -Id $ExpectedProcessId
  if (!$previousRuntime.WaitForExit(10000)) { throw 'The original backend did not exit; package files were left untouched.' }
  $ownership = Acquire-StorageOwnership
  if (Get-NetTCPConnection -LocalPort 5240 -State Listen -ErrorAction SilentlyContinue) { throw 'Another process acquired the live port; package files were left untouched.' }
  Assert-Content $content
  $replaced = $true
  Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force
  $checked = 0
  foreach ($file in $packageApp) {
    $installedPath = Resolve-AppFile $destination $file.RelativePath
    if ((Get-FileHash -LiteralPath $installedPath -Algorithm SHA256).Hash -ne $file.Hash) { throw "Package mismatch: $($file.RelativePath)" }
    $checked++
  }
  Assert-Content $content
  if ((Get-FileHash -LiteralPath $manifestPath).Hash -ne $manifestHash) { throw 'The trusted launcher changed.' }
  $ownership.Dispose(); $ownership = $null
  $newRuntime = Start-InstalledHost 'installed-host'
  $ready = $false
  for ($attempt=0; $attempt -lt 60; $attempt++) {
    if ($newRuntime.HasExited) { throw 'The installed backend exited before readiness.' }
    try { $status=Invoke-RestMethod 'http://127.0.0.1:5240/__meridian_status' -TimeoutSec 1; if ($status.capabilities -contains 'library-canvas-v2') { $ready=$true;break } } catch {}
    Start-Sleep -Milliseconds 250
  }
  if (!$ready) { throw 'The updated backend did not become ready.' }
  $installedListeners = @(Get-NetTCPConnection -LocalPort 5240 -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
  if ($installedListeners.Count -ne 1 -or $installedListeners[0] -ne $newRuntime.Id) { throw 'The updated backend does not own the live listener.' }
  $after = Snapshot-Content
  $report = [ordered]@{ installedAt=[DateTimeOffset]::Now.ToString('o'); source=$source; destination=$destination; backup=$backup; verifiedPackageFiles=$checked; previousProcessId=$ExpectedProcessId; processId=$newRuntime.Id; executable=$installedExecutable; savedContentFiles=$content.Count; savedContentUnchangedBeforeStartup=$true; savedContentUnchangedAfterStartup=(($content | ConvertTo-Json -Compress) -eq ($after | ConvertTo-Json -Compress)); manifestUnchanged=$true; windowsClosed=$false; status=$status }
  $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'package-install.json') -Encoding utf8NoBOM
  Write-Output "Installed $checked verified package files. Updated backend PID $($newRuntime.Id); existing windows remain open. Backup: $backup"
} catch {
  $installationFailure = $_
  try {
    if ($newRuntime) { Stop-OwnedReplacement $newRuntime }
    if ($previousRuntime -and !$previousRuntime.HasExited) { throw 'The original backend is still running; rollback will not replace files or start another backend.' }
    if ($replaced) {
      if (!$ownership) { $ownership = Acquire-StorageOwnership }
      if (Get-NetTCPConnection -LocalPort 5240 -State Listen -ErrorAction SilentlyContinue) { throw 'Another process owns the live port; rollback files were left untouched.' }
      Assert-AppSnapshot (Join-Path $backup 'app') $originalApp
      # Only files identified by the reviewed package inventory are eligible for
      # deletion. Validate every path and hash before changing any rollback file.
      $removals = @()
      foreach ($file in $newOnlyFiles) {
        $path = Resolve-AppFile $destination $file.RelativePath
        if (!(Test-Path -LiteralPath $path)) { continue }
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Hash) { throw "A new package file changed; preserve it and inspect before rollback: $path" }
        $removals += $file
      }
      foreach ($file in $removals) {
        $path = Resolve-AppFile $destination $file.RelativePath
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Hash) { throw "A package file changed before removal; rollback stopped: $path" }
        Remove-Item -LiteralPath $path -Force
      }
      foreach ($file in $originalApp) {
        $original = Resolve-AppFile (Join-Path $backup 'app') $file.RelativePath
        $restored = Resolve-AppFile $destination $file.RelativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $restored) -Force | Out-Null
        Copy-Item -LiteralPath $original -Destination $restored -Force
      }
      Assert-AppSnapshot $destination $originalApp
      if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $manifestHash) { throw 'The launcher manifest changed; preserve it and inspect before restarting.' }
    }
    if ($previousRuntime -and $previousRuntime.HasExited) {
      if (!$ownership) { $ownership = Acquire-StorageOwnership }
      if (Get-NetTCPConnection -LocalPort 5240 -State Listen -ErrorAction SilentlyContinue) { throw 'Another process owns the live port; rollback will not launch another backend.' }
      $ownership.Dispose();$ownership=$null
      $rollbackRuntime=Start-InstalledHost 'rollback-host'
      Write-Output "Original backend restored and started at PID $($rollbackRuntime.Id). Saved content was not restored or overwritten."
    }
  } catch {
    throw [System.InvalidOperationException]::new("Installation failed: $($installationFailure.Exception.Message) Rollback stopped safely: $($_.Exception.Message) Backup: $backup",$_.Exception)
  }
  throw $installationFailure
} finally { if ($ownership) { $ownership.Dispose() } }
