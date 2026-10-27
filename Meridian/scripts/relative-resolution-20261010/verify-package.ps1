[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId,[string]$TestMatch='relative-resolution.browser.ts',[Parameter(Mandatory=$true)][ValidateRange(1,100)][int]$ExpectedTests)
. (Join-Path $PSScriptRoot 'common.ps1')
$prepared = Get-Content -LiteralPath (Join-Path $evidence 'preparation.json') -Raw | ConvertFrom-Json
Assert-Inventory $installed $prepared.installedHashes
Assert-Inventory $stage $prepared.packageHashes
Assert-StaticOnly $prepared.installedHashes $prepared.packageHashes
Assert-FinalDist
$hostBefore = Host-Identity $ExpectedProcessId
$nativeBefore = Native-Identity
$sourceBefore = Host-Schema-Inventory
if (!(Equal-Json $hostBefore $prepared.host) -or !(Equal-Json $nativeBefore $prepared.native) -or !(Equal-Json $sourceBefore $prepared.hostSchemaHashes) -or (Get-FileHash -LiteralPath $manifest).Hash -cne $prepared.manifestHash) { throw 'Protected identity changed after preparation.' }
if (@(Get-NetTCPConnection -LocalPort 5310 -State Listen -ErrorAction SilentlyContinue).Count) { throw 'Isolated port5310 is occupied; leave its owner untouched.' }
$dataRoot = [IO.Path]::GetFullPath((Join-Path $workspace ('.test-data/relative-resolution-' + (Get-Date -Format 'yyyyMMdd-HHmmssffff'))))
if (!$dataRoot.StartsWith($workspace + '\.test-data\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected fixture storage path.' }
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$ownedHost=$null; $passed=$false; $previousMatch=$env:MERIDIAN_RELATIVE_RESOLUTION_TEST_MATCH
[ordered]@{ passed=$false; pending=$true; mode='static-only-fixture-package'; originalSavedDataFilesAccessed=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Encoding utf8NoBOM
Push-Location -LiteralPath $workspace
try {
    $ownedHost = Start-Process -FilePath (Resolve-ApplicationFile $stage 'Meridian.Host.exe') -ArgumentList @('--urls','http://127.0.0.1:5310','--data-root',('"'+$dataRoot+'"')) -WorkingDirectory $stage -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'host.stdout.log') -RedirectStandardError (Join-Path $evidence 'host.stderr.log')
    $ready=$false
    for ($at=0; $at -lt 100; $at++) {
        $ownedHost.Refresh(); if ($ownedHost.HasExited) { throw 'Owned fixture backend exited.' }
        try { $status=Invoke-RestMethod 'http://127.0.0.1:5310/__meridian_status' -TimeoutSec 1; if ($status.application -eq 'atlas-meridian') { $ready=$true; break } } catch { }
        Start-Sleep -Milliseconds 100
    }
    if (!$ready) { throw 'Owned fixture backend did not become ready.' }
    $owners=@(Get-NetTCPConnection -LocalPort 5310 -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($owners.Count -ne 1 -or $owners[0] -ne $ownedHost.Id) { throw 'Unexpected fixture listener ownership.' }
    $catalog=Invoke-RestMethod 'http://127.0.0.1:5310/api/projects'; if ($catalog.Count -ne 0) { throw 'Fixture storage was not initially empty.' }
    $env:MERIDIAN_RELATIVE_RESOLUTION_TEST_MATCH=$TestMatch
    & npx playwright test --config scripts/relative-resolution-20261010/package-playwright.config.ts
    if ($LASTEXITCODE -ne 0) { throw 'Final static package browser checks failed.' }
    $results=Get-Content -LiteralPath (Join-Path $evidence 'browser-results.json') -Raw | ConvertFrom-Json
    if ($results.stats.expected -ne $ExpectedTests -or $results.stats.unexpected -ne 0 -or $results.stats.flaky -ne 0 -or $results.stats.skipped -ne 0) { throw 'Package review requires every expected browser test to pass without skips or retries.' }
    $passed=$true
} finally {
    $cleanup=$null
    try {
        if ($ownedHost) {
            $ownedHost.Refresh()
            if (!$ownedHost.HasExited) {
                $identity=Get-CimInstance Win32_Process -Filter "ProcessId=$($ownedHost.Id)"
                if ($identity.ExecutablePath -ne (Join-Path $stage 'Meridian.Host.exe') -or $identity.CommandLine -notmatch [Regex]::Escape($dataRoot)) { throw 'Owned fixture process identity changed; leave it untouched.' }
                $ownedHost.Kill(); if (!$ownedHost.WaitForExit(10000)) { throw 'Owned fixture host did not stop.' }
            }
        }
    } catch { $cleanup=$_.Exception.Message }
    $env:MERIDIAN_RELATIVE_RESOLUTION_TEST_MATCH=$previousMatch
    Pop-Location
    $installedUnchanged=Equal-Json $prepared.installedHashes (Application-Inventory $installed)
    $packageUnchanged=Equal-Json $prepared.packageHashes (Application-Inventory $stage)
    $hostUnchanged=Equal-Json $hostBefore (Host-Identity $ExpectedProcessId)
    $nativeUnchanged=Equal-Json $nativeBefore (Native-Identity)
    $schemaUnchanged=Equal-Json $sourceBefore (Host-Schema-Inventory)
    $manifestUnchanged=$prepared.manifestHash -ceq (Get-FileHash -LiteralPath $manifest).Hash
    [ordered]@{ passed=$passed -and !$cleanup -and $installedUnchanged -and $packageUnchanged -and $hostUnchanged -and $nativeUnchanged -and $schemaUnchanged -and $manifestUnchanged; mode='static-only-fixture-package'; reviewedAt=[DateTimeOffset]::Now.ToString('o'); browserChecks=$ExpectedTests; testMatch=$TestMatch; packageRoot=$stage; isolatedDataRoot=$dataRoot; ownedHostId=$ownedHost.Id; ownedHostStopped=!$ownedHost -or $ownedHost.HasExited; cleanupFailure=$cleanup; originalSavedDataFilesAccessed=$false; installedFilesUnchanged=$installedUnchanged; packageFilesUnchanged=$packageUnchanged; liveHostUnchanged=$hostUnchanged; nativeWindowsUnchanged=$nativeUnchanged; hostSchemaUnchanged=$schemaUnchanged; launcherManifestUnchanged=$manifestUnchanged; packageHashes=$prepared.packageHashes; installedHashes=$prepared.installedHashes; liveHost=$hostBefore; native=$nativeBefore; hostSchemaHashes=$sourceBefore; manifestHash=$prepared.manifestHash } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Encoding utf8NoBOM
    if (!$installedUnchanged -or !$packageUnchanged -or !$hostUnchanged -or !$nativeUnchanged -or !$schemaUnchanged -or !$manifestUnchanged) { throw 'Protected identities changed during isolated review.' }
    if ($cleanup) { throw $cleanup }
}
Write-Output "All $ExpectedTests browser scenarios passed against the final static package. Owned fixture backend stopped; live backend/native windows/application data stayed untouched."
