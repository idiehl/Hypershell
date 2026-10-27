[CmdletBinding()]
param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($workspaceRoot -ne 'I:\dev\Meridian') { throw 'Unexpected Meridian workspace.' }
$qaRoot = Join-Path $workspaceRoot 'output/canvas-launcher-qa-20261010'
$packageRoot = Join-Path $workspaceRoot 'output/canvas-launcher-package-20261010'
$packageApp = [System.IO.Path]::GetFullPath((Join-Path $packageRoot 'app'))
$installedRoot = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot 'output/app'))
$manifestPath = Join-Path $workspaceRoot 'output/meridian.app.json'
$dataRoot = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot ('.test-data/canvas-launcher-package-20261010-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))))
if (!$dataRoot.StartsWith($workspaceRoot + '\.test-data\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected isolated data directory.' }
if ($packageApp -eq $installedRoot) { throw 'Review locations must remain separate from the installed app.' }
if (!(Test-Path -LiteralPath (Join-Path $packageApp 'Meridian.Host.exe') -PathType Leaf)) { throw 'The final production package must be staged before this review.' }
foreach ($name in @('editor.js','editor.js.map','viewer.js','app.css','scene.css','index.html')) {
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
$installedBefore = Snapshot-Files $installedRoot
$stagedBefore = Snapshot-Files $packageApp
$frontendFiles = @('wwwroot\index.html','wwwroot\editor.js','wwwroot\editor.js.map','wwwroot\viewer.js','wwwroot\app.css','wwwroot\scene.css')
if (($installedBefore.Path | ConvertTo-Json -Compress) -cne ($stagedBefore.Path | ConvertTo-Json -Compress)) { throw 'The mixed review stage must have exactly the installed application inventory.' }
foreach ($item in $installedBefore | Where-Object { $_.Path -notin $frontendFiles }) {
    $staged = @($stagedBefore | Where-Object Path -EQ $item.Path)
    if ($staged.Count -ne 1 -or $staged[0].Hash -ne $item.Hash) { throw "An installed backend/application file differs in the static-asset review stage: $($item.Path)" }
}
$liveBefore = Live-Identity
$nativeBefore = Native-Identity
$manifestBefore = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
if ($liveBefore.Count -ne 1) { throw 'Expected exactly one installed live Meridian backend.' }
$liveStatus = Invoke-RestMethod -Uri 'http://127.0.0.1:5240/__meridian_status'
if ($liveStatus.application -ne 'atlas-meridian') { throw 'Unexpected live loopback application.' }
$liveListeners = @(Get-NetTCPConnection -State Listen -LocalPort 5240 | Select-Object -ExpandProperty OwningProcess -Unique)
if ($liveListeners.Count -ne 1 -or $liveListeners[0] -ne $liveBefore[0].ProcessId) { throw 'Installed process identity does not own the live backend listener.' }
if (@(Get-NetTCPConnection -State Listen -LocalPort 5300 -ErrorAction SilentlyContinue).Count) { throw 'QA port 5300 is occupied; review will not stop another process.' }
New-Item -ItemType Directory -Path $qaRoot, $dataRoot -Force | Out-Null
$configuration = [ordered]@{ origin = 'http://127.0.0.1:5300'; dataRoot = $dataRoot; packageRoot = $packageRoot; qaRoot = $qaRoot; reviewMode = 'static-assets-on-installed-backend'; originalDataAccessed = $false; fixtureOnly = $true; liveHost = $liveBefore[0] }
$configurationPath = Join-Path $qaRoot 'package-configuration.json'
$configuration | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configurationPath -Encoding utf8NoBOM
[ordered]@{ installed = $installedBefore; staged = $stagedBefore; backendBitsMatchedInstalled = $true; originalDataAccessed = $false; host = $liveBefore; native = $nativeBefore; manifestHash = $manifestBefore } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $qaRoot 'package-protected-before.json') -Encoding utf8NoBOM
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
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 5300)
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
    $installedAfter = Snapshot-Files $installedRoot
    $liveAfter = Live-Identity
    $nativeAfter = Native-Identity
    $installedUnchanged = ($installedBefore | ConvertTo-Json -Compress -Depth 4) -ceq ($installedAfter | ConvertTo-Json -Compress -Depth 4)
    $hostUnchanged = ($liveBefore | ConvertTo-Json -Compress -Depth 4) -ceq ($liveAfter | ConvertTo-Json -Compress -Depth 4)
    $nativeUnchanged = ($nativeBefore | ConvertTo-Json -Compress -Depth 4) -ceq ($nativeAfter | ConvertTo-Json -Compress -Depth 4)
    $manifestUnchanged = $manifestBefore -ceq (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    [ordered]@{ succeeded = $reviewSucceeded -and !$cleanupFailure; isolatedDataRoot = $dataRoot; packageRoot = $packageRoot; ownedHostId = $ownedHost.Id; ownedHostStopped = !$ownedHost -or $ownedHost.HasExited; cleanupFailure = $cleanupFailure; originalDataAccessed = $false; fixtureOnly = $true; backendBitsMatchedInstalled = $true; installedFilesUnchanged = $installedUnchanged; liveHostUnchanged = $hostUnchanged; nativeWindowsUnchanged = $nativeUnchanged; launcherManifestUnchanged = $manifestUnchanged; installed = $installedAfter; liveHost = $liveAfter; native = $nativeAfter } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $qaRoot 'package-protected-after.json') -Encoding utf8NoBOM
    if (!$installedUnchanged -or !$hostUnchanged -or !$nativeUnchanged -or !$manifestUnchanged) { throw 'A protected application identity or hash changed during isolated review; inspect the evidence without changing saved content.' }
    if ($cleanupFailure) { throw $cleanupFailure }
}
Write-Output "Verified new frontend assets against the exact installed backend in $packageRoot. QA used only fresh fixture storage; original saved data was not accessed. Installed files, backend, native windows and launcher manifest remain unchanged."







