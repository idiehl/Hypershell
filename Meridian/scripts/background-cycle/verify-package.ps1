[CmdletBinding()]
param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($workspaceRoot -ne 'I:\dev\Meridian') { throw 'Unexpected Meridian workspace.' }
$qaRoot = Join-Path $workspaceRoot 'output/background-qa-20261008'
$packageRoot = Join-Path $workspaceRoot 'output/background-package-20261008'
$packageApp = [System.IO.Path]::GetFullPath((Join-Path $packageRoot 'app'))
$sourceRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian'))
$installedRoot = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot 'output/app'))
$manifestPath = Join-Path $workspaceRoot 'output/meridian.app.json'
$dataRoot = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot ('.test-data/background-package-20261008-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))))
if (!$dataRoot.StartsWith($workspaceRoot + '\.test-data\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected isolated data directory.' }
if ($dataRoot -eq $sourceRoot -or $packageApp -eq $installedRoot) { throw 'Review locations must remain separate from the installed app and live saved content.' }
if (!(Test-Path -LiteralPath (Join-Path $packageApp 'Meridian.Host.exe') -PathType Leaf)) { throw 'The final production package must be staged before this review.' }
foreach ($name in @('editor.js','viewer.js','app.css','scene.css','index.html')) {
    if ((Get-FileHash -LiteralPath (Join-Path $packageApp "wwwroot/$name")).Hash -ne (Get-FileHash -LiteralPath (Join-Path $workspaceRoot "dist/$name")).Hash) { throw "The staged package does not match the final build: $name" }
}
function Snapshot-Files([string]$Root) {
    @(Get-ChildItem -LiteralPath $Root -File -Recurse | Where-Object Name -NE '.host.lock' | Sort-Object FullName | ForEach-Object {
        [ordered]@{ Path = [System.IO.Path]::GetRelativePath($Root, $_.FullName); Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
function Live-Identity {
    @(Get-CimInstance Win32_Process -Filter "Name = 'Meridian.Host.exe'" | Where-Object ExecutablePath -EQ (Join-Path $installedRoot 'Meridian.Host.exe') | Select-Object ProcessId, ExecutablePath, CommandLine)
}
function Native-Identity {
    @(Get-CimInstance Win32_Process -Filter "Name = 'Hypershell.exe'" | Select-Object ProcessId, ExecutablePath, CommandLine | Sort-Object ProcessId)
}
$sourceBefore = Snapshot-Files $sourceRoot
$installedBefore = Snapshot-Files $installedRoot
$liveBefore = Live-Identity
$nativeBefore = Native-Identity
$manifestBefore = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
if ($liveBefore.Count -ne 1) { throw 'Expected exactly one installed live Meridian backend.' }
$liveStatus = Invoke-RestMethod -Uri 'http://127.0.0.1:5240/__meridian_status'
if ($liveStatus.application -ne 'atlas-meridian') { throw 'Unexpected live loopback application.' }
$liveListeners = @(Get-NetTCPConnection -State Listen -LocalPort 5240 | Select-Object -ExpandProperty OwningProcess -Unique)
if ($liveListeners.Count -ne 1 -or $liveListeners[0] -ne $liveBefore[0].ProcessId) { throw 'Installed process identity does not own the live backend listener.' }
if (@(Get-NetTCPConnection -State Listen -LocalPort 5293 -ErrorAction SilentlyContinue).Count) { throw 'QA port 5293 is occupied; review will not stop another process.' }
New-Item -ItemType Directory -Path $qaRoot, $dataRoot -Force | Out-Null
foreach ($item in $sourceBefore) {
    $target = [System.IO.Path]::GetFullPath((Join-Path $dataRoot $item.Path))
    if (!$target.StartsWith($dataRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'A copied saved file escaped the isolated data directory.' }
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot $item.Path) -Destination $target
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $item.Hash) { throw 'A live saved file changed during its read-only copy.' }
}
$configuration = [ordered]@{ origin = 'http://127.0.0.1:5293'; dataRoot = $dataRoot; packageRoot = $packageRoot; qaRoot = $qaRoot; sourceRoot = $sourceRoot; copiedFiles = $sourceBefore.Count; liveHost = $liveBefore[0] }
$configurationPath = Join-Path $qaRoot 'package-configuration.json'
$configuration | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configurationPath -Encoding utf8NoBOM
[ordered]@{ data = $sourceBefore; installed = $installedBefore; host = $liveBefore; native = $nativeBefore; manifestHash = $manifestBefore } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $qaRoot 'package-protected-before.json') -Encoding utf8NoBOM
[ordered]@{ passed = $false; pending = $true; origin = $configuration.origin; isolatedDataRoot = $dataRoot } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $qaRoot 'package-review.json') -Encoding utf8NoBOM
if ($PrepareOnly) { Write-Output "Prepared isolated review data: $dataRoot"; return }
$ownedHost = $null
$reviewSucceeded = $false
Push-Location -LiteralPath $workspaceRoot
try {
    $ownedHost = Start-Process -FilePath (Join-Path $packageApp 'Meridian.Host.exe') -ArgumentList @('--urls', $configuration.origin, '--data-root', ('"' + $dataRoot + '"')) -WorkingDirectory $packageApp -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $qaRoot 'package-host.stdout.log') -RedirectStandardError (Join-Path $qaRoot 'package-host.stderr.log')
    $healthy = $false
    for ($attempt = 0; $attempt -lt 150; $attempt++) {
        $ownedHost.Refresh()
        if ($ownedHost.HasExited) { throw 'The isolated packaged host exited during startup.' }
        try { $status = Invoke-RestMethod -Uri ($configuration.origin + '/__meridian_status'); if ($status.application -eq 'atlas-meridian') { $healthy = $true; break } } catch { }
        Start-Sleep -Milliseconds 100
    }
    if (!$healthy) { throw 'The isolated packaged host did not become ready.' }
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 5293)
    if (!$listeners.Count -or @($listeners | Where-Object OwningProcess -NE $ownedHost.Id).Count) { throw 'The QA listener is not owned by the process launched for this review.' }
    $configuration.ownedHostId = $ownedHost.Id
    $configuration | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configurationPath -Encoding utf8NoBOM
    & node --experimental-strip-types (Join-Path $PSScriptRoot 'verify-package.mjs') $configurationPath
    if ($LASTEXITCODE -ne 0) { throw 'Production functional/visual review failed.' }
    $reviewSucceeded = $true
} finally {
    $cleanupFailure = $null
    try {
        if ($ownedHost) {
            $ownedHost.Refresh()
            if (!$ownedHost.HasExited) {
                $identity = Get-CimInstance Win32_Process -Filter "ProcessId=$($ownedHost.Id)"
                if ($identity.ExecutablePath -ne (Join-Path $packageApp 'Meridian.Host.exe') -or $identity.CommandLine -notmatch [Regex]::Escape($dataRoot)) { throw 'The QA process identity changed; no unrelated process will be stopped.' }
                $ownedHost.Kill()
                if (!$ownedHost.WaitForExit(10000)) { throw 'The owned QA host did not stop within ten seconds.' }
            }
        }
    } catch { $cleanupFailure = $_.Exception.Message }
    Pop-Location
    $sourceAfter = Snapshot-Files $sourceRoot
    $installedAfter = Snapshot-Files $installedRoot
    $liveAfter = Live-Identity
    $nativeAfter = Native-Identity
    $dataUnchanged = ($sourceBefore | ConvertTo-Json -Compress -Depth 4) -ceq ($sourceAfter | ConvertTo-Json -Compress -Depth 4)
    $installedUnchanged = ($installedBefore | ConvertTo-Json -Compress -Depth 4) -ceq ($installedAfter | ConvertTo-Json -Compress -Depth 4)
    $hostUnchanged = ($liveBefore | ConvertTo-Json -Compress -Depth 4) -ceq ($liveAfter | ConvertTo-Json -Compress -Depth 4)
    $nativeUnchanged = ($nativeBefore | ConvertTo-Json -Compress -Depth 4) -ceq ($nativeAfter | ConvertTo-Json -Compress -Depth 4)
    $manifestUnchanged = $manifestBefore -ceq (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    [ordered]@{ succeeded = $reviewSucceeded -and !$cleanupFailure; isolatedDataRoot = $dataRoot; packageRoot = $packageRoot; ownedHostId = $ownedHost.Id; ownedHostStopped = !$ownedHost -or $ownedHost.HasExited; cleanupFailure = $cleanupFailure; copiedSourceFiles = $sourceBefore.Count; savedDataUnchanged = $dataUnchanged; installedFilesUnchanged = $installedUnchanged; liveHostUnchanged = $hostUnchanged; nativeWindowsUnchanged = $nativeUnchanged; launcherManifestUnchanged = $manifestUnchanged; installed = $installedAfter; data = $sourceAfter; liveHost = $liveAfter; native = $nativeAfter } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $qaRoot 'package-protected-after.json') -Encoding utf8NoBOM
    if (!$dataUnchanged -or !$installedUnchanged -or !$hostUnchanged -or !$nativeUnchanged -or !$manifestUnchanged) { throw 'A protected identity or hash changed during isolated review; inspect the evidence without restoring over current saved content.' }
    if ($cleanupFailure) { throw $cleanupFailure }
}
Write-Output "Verified production package at $packageRoot using $($sourceBefore.Count) copied saved files. Installed files, saved content, backend, native windows and launcher manifest remain unchanged."


