$ErrorActionPreference = 'Stop'
$workspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ($workspaceRoot -ne 'I:\dev\Meridian') { throw 'Unexpected workspace.' }
$stageRoot = (Resolve-Path (Join-Path $workspaceRoot '.cache/right-panel-package/wwwroot')).Path
$installedRoot = (Resolve-Path (Join-Path $workspaceRoot 'output/app/wwwroot')).Path
if ($stageRoot -ne 'I:\dev\Meridian\.cache\right-panel-package\wwwroot' -or $installedRoot -ne 'I:\dev\Meridian\output\app\wwwroot') { throw 'Unexpected static asset paths.' }
$liveHosts = @(Get-CimInstance Win32_Process -Filter "Name = 'Meridian.Host.exe'" | Where-Object ExecutablePath -EQ 'I:\dev\Meridian\output\app\Meridian.Host.exe')
if ($liveHosts.Count -ne 1) { throw 'Expected one running installed Meridian host.' }
$status = Invoke-RestMethod -Uri 'http://127.0.0.1:5240/__meridian_status'
if ($status.application -ne 'atlas-meridian') { throw 'Unexpected loopback app.' }
$dataRoot = Join-Path $env:LOCALAPPDATA 'AtlasMeridian'
function Get-ProtectedFiles {
  @(Get-ChildItem -LiteralPath $dataRoot -File -Recurse | Where-Object Name -NE '.host.lock' | Sort-Object FullName | ForEach-Object {
    @{ Path = $_.FullName; Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
  })
}
$before = Get-ProtectedFiles
$qaRoot = Join-Path $workspaceRoot 'output/right-panel-qa'
$backupRoot = Join-Path $qaRoot ('static-before-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
Copy-Item -LiteralPath $installedRoot -Destination (Join-Path $backupRoot 'wwwroot') -Recurse
$checked = 0
foreach ($file in Get-ChildItem -LiteralPath $stageRoot -File -Recurse) {
  $relative = [System.IO.Path]::GetRelativePath($stageRoot, $file.FullName)
  $destination = [System.IO.Path]::GetFullPath((Join-Path $installedRoot $relative))
  if (!$destination.StartsWith($installedRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Static target escaped the package.' }
  New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
  Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
  if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw "Static asset hash mismatch: $relative" }
  $checked++
}
$after = Get-ProtectedFiles
$sameContent = ($before | ConvertTo-Json -Compress -Depth 4) -ceq ($after | ConvertTo-Json -Compress -Depth 4)
$report = @{ installedAt = (Get-Date).ToString('o'); backup = $backupRoot; verifiedFiles = $checked; protectedFiles = $before.Count; savedContentUnchanged = $sameContent; hostProcessId = $liveHosts[0].ProcessId; hostRestarted = $false; staticOnly = $true }
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $qaRoot 'static-install.json') -Encoding utf8
if (!$sameContent) { throw 'Saved content changed during the static update; inspect the report and current user activity.' }
Write-Output "Installed $checked matching static assets without restarting host $($liveHosts[0].ProcessId); $($before.Count) saved-content files unchanged. Backup: $backupRoot"
