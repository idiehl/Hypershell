[CmdletBinding()]
param([Parameter(Mandatory = $true)][int]$ExpectedProcessId)
$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($workspace -ne 'I:\dev\Meridian') { throw 'Unexpected Meridian workspace.' }
$stage = Join-Path $workspace 'output/scale-package-20261010/app'
$installed = Join-Path $workspace 'output/app'
$evidence = Join-Path $workspace 'output/scale-qa-20261010/package'
$manifestPath = Join-Path $workspace 'output/meridian.app.json'
$dataRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace ('.test-data/scale-package-' + (Get-Date -Format 'yyyyMMdd-HHmmssffff'))))
if (!$dataRoot.StartsWith($workspace + '\.test-data\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected isolated storage path.' }
function Inventory([string]$Root) {
    $resolved = [System.IO.Path]::GetFullPath($Root)
    if (!$resolved.StartsWith($workspace + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Inventory root escaped the workspace.' }
    $items = @(Get-ChildItem -LiteralPath $Root -Force -Recurse)
    if ((Get-Item -LiteralPath $Root).Attributes -band [IO.FileAttributes]::ReparsePoint -or @($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Package links are not eligible for review.' }
    @($items | Where-Object { !$_.PSIsContainer } | ForEach-Object { [pscustomobject]@{ relativePath = [IO.Path]::GetRelativePath($resolved,$_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } } | Sort-Object relativePath)
}
function Native-Identity { @(Get-CimInstance Win32_Process -Filter "Name='Hypershell.exe'" | Select-Object ProcessId,ExecutablePath,CommandLine,CreationDate | Sort-Object ProcessId) }
function Host-Identity {
    $identities = @(Get-CimInstance Win32_Process -Filter "Name='Meridian.Host.exe'" | Where-Object ExecutablePath -EQ (Join-Path $installed 'Meridian.Host.exe'))
    $listeners = @(Get-NetTCPConnection -LocalPort 5240 -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($identities.Count -ne 1 -or $identities[0].ProcessId -ne $ExpectedProcessId -or $listeners.Count -ne 1 -or $listeners[0] -ne $ExpectedProcessId -or $identities[0].CommandLine -notmatch 'http://127\.0\.0\.1:5240') { throw 'The installed live backend identity changed; no process will be stopped.' }
    $identities[0] | Select-Object ProcessId,ExecutablePath,CommandLine,CreationDate
}
function Equal-Json($A,$B) { ($A | ConvertTo-Json -Depth 6 -Compress) -ceq ($B | ConvertTo-Json -Depth 6 -Compress) }
if (!(Test-Path -LiteralPath (Join-Path $stage 'Meridian.Host.exe'))) { throw 'The final full host package must be staged first.' }
foreach ($name in @('index.html','editor.js','editor.js.map','viewer.js','app.css','scene.css')) {
    if ((Get-FileHash -LiteralPath (Join-Path $stage "wwwroot/$name")).Hash -ne (Get-FileHash -LiteralPath (Join-Path $workspace "dist/$name")).Hash) { throw "The staged frontend does not match final dist: $name" }
}
if (@(Get-NetTCPConnection -LocalPort 5300 -State Listen -ErrorAction SilentlyContinue).Count) { throw 'Isolated review port 5300 is occupied; leave it untouched.' }
New-Item -ItemType Directory -Path $evidence,$dataRoot -Force | Out-Null
$installedBefore = Inventory $installed
$packageBefore = Inventory $stage
$nativeBefore = Native-Identity
$hostBefore = Host-Identity
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$ownedHost = $null
$passed = $false
[ordered]@{ passed = $false; pending = $true; mode = 'full-host-fixture-only'; originalSavedDataFilesAccessed = $false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Encoding utf8NoBOM
Push-Location -LiteralPath $workspace
try {
    $ownedHost = Start-Process -FilePath (Join-Path $stage 'Meridian.Host.exe') -ArgumentList @('--urls','http://127.0.0.1:5300','--data-root',('"'+$dataRoot+'"')) -WorkingDirectory $stage -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'host.stdout.log') -RedirectStandardError (Join-Path $evidence 'host.stderr.log')
    $ready = $false
    for ($attempt=0; $attempt -lt 100; $attempt++) {
        $ownedHost.Refresh(); if ($ownedHost.HasExited) { throw 'The owned review backend exited.' }
        try { $status = Invoke-RestMethod 'http://127.0.0.1:5300/__meridian_status' -TimeoutSec 1; if ($status.application -eq 'atlas-meridian') { $ready = $true; break } } catch { }
        Start-Sleep -Milliseconds 100
    }
    if (!$ready) { throw 'The owned review backend did not become ready.' }
    $owners = @(Get-NetTCPConnection -LocalPort 5300 -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($owners.Count -ne 1 -or $owners[0] -ne $ownedHost.Id) { throw 'Unexpected review port ownership.' }
    $catalog = Invoke-RestMethod 'http://127.0.0.1:5300/api/projects'
    if ($catalog.Count -ne 0) { throw 'Review storage was not initially empty.' }
    & npx playwright test --config scripts/scale-cycle/package-playwright.config.ts
    if ($LASTEXITCODE -ne 0) { throw 'The staged package browser checks failed.' }
    $results = Get-Content -LiteralPath (Join-Path $evidence 'browser-results.json') -Raw | ConvertFrom-Json
    if ($results.stats.unexpected -ne 0 -or $results.stats.flaky -ne 0 -or $results.stats.expected -ne 6) { throw 'Expected six passing scale package browser scenarios.' }
    $passed = $true
} finally {
    $cleanup = $null
    try {
        if ($ownedHost) {
            $ownedHost.Refresh()
            if (!$ownedHost.HasExited) {
                $identity = Get-CimInstance Win32_Process -Filter "ProcessId=$($ownedHost.Id)"
                if ($identity.ExecutablePath -ne (Join-Path $stage 'Meridian.Host.exe') -or $identity.CommandLine -notmatch [Regex]::Escape($dataRoot)) { throw 'Owned QA process identity changed; leave it untouched.' }
                $ownedHost.Kill(); if (!$ownedHost.WaitForExit(10000)) { throw 'Owned QA process did not stop.' }
            }
        }
    } catch { $cleanup = $_.Exception.Message }
    Pop-Location
    $installedUnchanged = Equal-Json $installedBefore (Inventory $installed)
    $packageUnchanged = Equal-Json $packageBefore (Inventory $stage)
    $nativeUnchanged = Equal-Json $nativeBefore (Native-Identity)
    $hostUnchanged = Equal-Json $hostBefore (Host-Identity)
    $manifestUnchanged = $manifestHash -ceq (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    $report = [ordered]@{ passed = $passed -and !$cleanup; mode = 'full-host-fixture-only'; packageRoot = $stage; isolatedDataRoot = $dataRoot; originalSavedDataFilesAccessed = $false; ownedHostId = $ownedHost.Id; ownedHostStopped = !$ownedHost -or $ownedHost.HasExited; cleanupFailure = $cleanup; browserChecks = 6; installedFilesUnchanged = $installedUnchanged; packageFilesUnchanged = $packageUnchanged; liveHostUnchanged = $hostUnchanged; nativeWindowsUnchanged = $nativeUnchanged; launcherManifestUnchanged = $manifestUnchanged; packageHashes = $packageBefore; installedHashes = $installedBefore; liveHost = $hostBefore; native = $nativeBefore; manifestHash = $manifestHash }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Encoding utf8NoBOM
    if (!$installedUnchanged -or !$packageUnchanged -or !$nativeUnchanged -or !$hostUnchanged -or !$manifestUnchanged) { throw 'Protected application identity changed during isolated review; inspect evidence.' }
    if ($cleanup) { throw $cleanup }
}
Write-Output "Verified all six scale scenarios against the isolated full package. Owned QA host stopped. Live backend PID $ExpectedProcessId, native windows and launcher stayed unchanged. Saved data files were never read, copied or restored."
