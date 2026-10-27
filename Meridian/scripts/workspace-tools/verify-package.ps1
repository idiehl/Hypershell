[CmdletBinding()]
param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($workspaceRoot -ne 'I:\dev\Meridian') { throw 'Unexpected Meridian workspace.' }
$qaRoot = Join-Path $workspaceRoot 'output/workspace-tools-qa'
$packageRoot = Join-Path $workspaceRoot 'output/workspace-tools-package'
$sourceRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian'))
$installedRoot = Join-Path $workspaceRoot 'output/app'
$dataRoot = Join-Path $workspaceRoot ('.test-data/workspace-tools-package-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
if (!$dataRoot.StartsWith($workspaceRoot + '\.test-data\', [System.StringComparison]::OrdinalIgnoreCase) -and !$dataRoot.StartsWith($workspaceRoot + '\.test-data/', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected isolated data directory.' }
if ($dataRoot -eq $sourceRoot -or $packageRoot -eq $installedRoot) { throw 'The review must stay isolated from the installed app and data.' }
New-Item -ItemType Directory -Path $qaRoot, $dataRoot -Force | Out-Null
function Snapshot-Files([string]$Root) {
    @(Get-ChildItem -LiteralPath $Root -File -Recurse | Where-Object Name -NE '.host.lock' | Sort-Object FullName | ForEach-Object {
        [ordered]@{ Path = [System.IO.Path]::GetRelativePath($Root, $_.FullName); Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
function Live-Identity {
    @(Get-CimInstance Win32_Process -Filter "Name = 'Meridian.Host.exe'" | Where-Object ExecutablePath -EQ (Join-Path $installedRoot 'Meridian.Host.exe') | Select-Object ProcessId, ExecutablePath, CommandLine)
}
$sourceBefore = Snapshot-Files $sourceRoot
$installedBefore = Snapshot-Files $installedRoot
$liveBefore = Live-Identity
if ($liveBefore.Count -ne 1) { throw 'Expected one installed live Meridian host.' }
$liveStatus = Invoke-RestMethod -Uri 'http://127.0.0.1:5240/__meridian_status'
if ($liveStatus.application -ne 'atlas-meridian') { throw 'Unexpected live loopback application.' }
foreach ($item in $sourceBefore) {
    $target = [System.IO.Path]::GetFullPath((Join-Path $dataRoot $item.Path))
    if (!$target.StartsWith($dataRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'A copied file escaped the isolated data directory.' }
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot $item.Path) -Destination $target
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $item.Hash) { throw 'A source file changed during its read-only copy.' }
}
$configuration = [ordered]@{ origin = 'http://127.0.0.1:5293'; dataRoot = $dataRoot; packageRoot = $packageRoot; qaRoot = $qaRoot; sourceRoot = $sourceRoot; copiedFiles = $sourceBefore.Count; liveHost = $liveBefore[0] }
$configurationPath = Join-Path $qaRoot 'package-configuration.json'
$configuration | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configurationPath -Encoding utf8NoBOM
[ordered]@{ data = $sourceBefore; installed = $installedBefore; host = $liveBefore } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $qaRoot 'package-protected-before.json') -Encoding utf8NoBOM
if ($PrepareOnly) { Write-Output "Prepared isolated review data: $dataRoot"; return }
if (@(Get-NetTCPConnection -State Listen -LocalPort 5293 -ErrorAction SilentlyContinue).Count) { throw 'Review port 5293 is already in use.' }
$ownedHost = $null
$reviewSucceeded = $false
Push-Location -LiteralPath $workspaceRoot
try {
    & (Join-Path $workspaceRoot 'scripts/package.ps1') -OutputDirectory $packageRoot -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Production packaging failed.' }
    $ownedHost = Start-Process -FilePath (Join-Path $packageRoot 'app/Meridian.Host.exe') -ArgumentList @('--urls', $configuration.origin, '--data-root', ('"' + $dataRoot + '"')) -WorkingDirectory (Join-Path $packageRoot 'app') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $qaRoot 'package-host.stdout.log') -RedirectStandardError (Join-Path $qaRoot 'package-host.stderr.log')
    $healthy = $false
    for ($attempt = 0; $attempt -lt 150; $attempt++) {
        if ($ownedHost.HasExited) { throw 'Packaged host exited during startup.' }
        try { $status = Invoke-RestMethod -Uri ($configuration.origin + '/__meridian_status'); if ($status.application -eq 'atlas-meridian') { $healthy = $true; break } } catch { }
        Start-Sleep -Milliseconds 100
    }
    if (!$healthy) { throw 'Packaged host did not become ready.' }
    $listener = @(Get-NetTCPConnection -State Listen -LocalPort 5293)
    if (!$listener.Count -or @($listener | Where-Object OwningProcess -NE $ownedHost.Id).Count) { throw 'The review port is not owned by this packaged host.' }
    $configuration.ownedHostId = $ownedHost.Id
    $configuration | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configurationPath -Encoding utf8NoBOM
    & node --experimental-strip-types (Join-Path $PSScriptRoot 'verify-package.mjs') $configurationPath
    if ($LASTEXITCODE -ne 0) { throw 'Packaged functional/visual review failed.' }
    $reviewSucceeded = $true
} finally {
    if ($ownedHost -and !$ownedHost.HasExited) { Stop-Process -Id $ownedHost.Id; $ownedHost.WaitForExit() }
    Pop-Location
    $sourceAfter = Snapshot-Files $sourceRoot
    $installedAfter = Snapshot-Files $installedRoot
    $liveAfter = Live-Identity
    $dataUnchanged = ($sourceBefore | ConvertTo-Json -Compress -Depth 4) -ceq ($sourceAfter | ConvertTo-Json -Compress -Depth 4)
    $installedUnchanged = ($installedBefore | ConvertTo-Json -Compress -Depth 4) -ceq ($installedAfter | ConvertTo-Json -Compress -Depth 4)
    $hostUnchanged = ($liveBefore | ConvertTo-Json -Compress -Depth 4) -ceq ($liveAfter | ConvertTo-Json -Compress -Depth 4)
    [ordered]@{ succeeded = $reviewSucceeded; isolatedDataRoot = $dataRoot; packageRoot = $packageRoot; ownedHostId = $ownedHost.Id; ownedHostStopped = !$ownedHost -or $ownedHost.HasExited; copiedSourceFiles = $sourceBefore.Count; savedDataUnchanged = $dataUnchanged; installedFilesUnchanged = $installedUnchanged; liveHostUnchanged = $hostUnchanged; installed = $installedAfter; data = $sourceAfter; liveHost = $liveAfter } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $qaRoot 'package-protected-after.json') -Encoding utf8NoBOM
    if (!$dataUnchanged -or !$installedUnchanged -or !$hostUnchanged) { throw 'Live identity or protected hashes changed during isolated review; inspect the evidence before attributing the change.' }
}
Write-Output "Verified isolated package: $packageRoot; live host and saved data unchanged."
