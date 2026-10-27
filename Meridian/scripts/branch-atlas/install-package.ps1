$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ($workspace -ne 'I:\dev\Meridian') { throw 'Unexpected workspace' }
$source = (Resolve-Path (Join-Path $workspace 'output/branch-20261005/app')).Path
$destination = (Resolve-Path (Join-Path $workspace 'output/app')).Path
if ($destination -ne 'I:\dev\Meridian\output\app') { throw 'Unexpected installed app path' }
$dataRoot = Join-Path $env:LOCALAPPDATA 'AtlasMeridian'
$backup = Join-Path $workspace ('output/branch-atlas/app-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$ownership = [System.IO.File]::Open((Join-Path $dataRoot '.host.lock'), [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
try {
  $active = Get-CimInstance Win32_Process -Filter "Name = 'Meridian.Host.exe'" | Where-Object ExecutablePath -EQ (Join-Path $destination 'Meridian.Host.exe')
  if ($active) { throw 'Installed Meridian is running. Preserve the session before replacing its package.' }
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
  @{ source=$source; destination=$destination; backup=$backup; verifiedFiles=$checked; installedAt=(Get-Date).ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspace 'output/branch-atlas/package-install.json') -Encoding utf8
  Write-Output "Installed and hash-verified $checked package files. Backup: $backup"
} finally { $ownership.Dispose() }
