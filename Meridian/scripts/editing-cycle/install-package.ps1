$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ($workspace -ne 'I:\dev\Meridian') { throw 'Unexpected workspace' }
$source = (Resolve-Path (Join-Path $workspace 'output/editing-20261005/app')).Path
$destination = (Resolve-Path (Join-Path $workspace 'output/app')).Path
if ($destination -ne 'I:\dev\Meridian\output\app') { throw 'Unexpected installed app path' }
$dataRoot = Join-Path $env:LOCALAPPDATA 'AtlasMeridian'
$baseline = Get-Content -LiteralPath (Join-Path $workspace '.cache/editing-cycle/protected-files.json') -Raw | ConvertFrom-Json
$backup = Join-Path $workspace ('output/editing-qa/app-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
function Assert-SavedContentUnchanged {
  foreach ($entry in $baseline) {
    if (!(Test-Path -LiteralPath $entry.Path -PathType Leaf) -or (Get-FileHash -LiteralPath $entry.Path).Hash -ne $entry.Hash) {
      throw "Saved content changed since this cycle began: $($entry.Path)"
    }
  }
  $paths = @($baseline | ForEach-Object Path)
  foreach ($file in Get-ChildItem -LiteralPath $dataRoot -File -Recurse | Where-Object Name -NE '.host.lock') {
    if ($file.FullName -notin $paths) { throw "Unexpected saved content file: $($file.FullName)" }
  }
}
$ownership = [System.IO.File]::Open((Join-Path $dataRoot '.host.lock'), [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
try {
  $active = Get-CimInstance Win32_Process -Filter "Name = 'Meridian.Host.exe'" | Where-Object ExecutablePath -EQ (Join-Path $destination 'Meridian.Host.exe')
  if ($active) { throw 'Installed Meridian is running. Preserve its session before replacing the package.' }
  Assert-SavedContentUnchanged
  New-Item -ItemType Directory -Path $backup | Out-Null
  Get-ChildItem -LiteralPath $destination | Copy-Item -Destination $backup -Recurse
  Get-ChildItem -LiteralPath $source | Copy-Item -Destination $destination -Recurse -Force
  $checked = 0
  foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
    $relative = [System.IO.Path]::GetRelativePath($source, $file.FullName)
    $installed = Join-Path $destination $relative
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $installed).Hash) { throw "Package hash mismatch: $relative" }
    $checked++
  }
  Assert-SavedContentUnchanged
  @{ source=$source; destination=$destination; backup=$backup; verifiedFiles=$checked; protectedContentFiles=$baseline.Count; savedContentUnchanged=$true; installedAt=(Get-Date).ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspace 'output/editing-qa/package-install.json') -Encoding utf8
  Write-Output "Installed and hash-verified $checked package files; all $($baseline.Count) saved-content files are unchanged. Backup: $backup"
} finally { $ownership.Dispose() }
