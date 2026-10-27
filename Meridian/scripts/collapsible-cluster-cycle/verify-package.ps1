[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId,[ValidateRange(1024,65535)][int]$ReviewPort=5433)
. (Join-Path $PSScriptRoot 'common.ps1')
$preparation = Get-Content -LiteralPath (Join-Path $evidence 'preparation.json') -Raw | ConvertFrom-Json
Assert-Inventory $stage $preparation.packageHashes; Assert-Inventory $installed $preparation.installedHashes
Assert-FinalDist
if (!(Equal-Json $preparation.sourceHashes (Source-Inventory))) { throw 'Production source changed since candidate preparation; rebuild the candidate first.' }
if ($ReviewPort -eq 5240) { throw 'The review must not use the live Meridian port.' }
if (@(Get-NetTCPConnection -LocalPort $ReviewPort -State Listen -ErrorAction SilentlyContinue).Count) { throw "Isolated review port $ReviewPort is occupied; leave it untouched." }
$reviewUrl = 'http://127.0.0.1:' + $ReviewPort
$dataRoot = [IO.Path]::GetFullPath((Join-Path $workspace ('.test-data/collapsible-cluster-package-' + (Get-Date -Format 'yyyyMMdd-HHmmssffff'))))
if (!$dataRoot.StartsWith($workspace + '\.test-data\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Isolated fixture storage escaped its root.' }
if (Test-Path -LiteralPath $dataRoot) { throw 'Fixture storage must be a new directory.' }
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$installedBefore = Application-Inventory $installed; $packageBefore = Application-Inventory $stage
$nativeBefore = Native-Identity; Assert-ExpectedNative $nativeBefore $preparation.native
$hostBefore = Host-Identity $ExpectedProcessId
if (!(Equal-Json $hostBefore $preparation.host) -or !(Equal-Json $nativeBefore $preparation.native)) { throw 'Runtime identity changed since candidate preparation.' }
$manifestHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
if ($manifestHash -cne $preparation.manifestHash) { throw 'The launcher changed since preparation.' }
$sourceBefore = Source-Inventory
$ownedHost = $null; $passed = $false
$expectedTests = 0; $browserPlanHash = $null; $browserResultsHash = $null
$browserEnvironmentPath = Join-Path $evidence 'browser-environment.json'
$browserEnvironmentHash = if (Test-Path -LiteralPath $browserEnvironmentPath) { (Get-FileHash -LiteralPath $browserEnvironmentPath -Algorithm SHA256).Hash } else { $null }
$previousReviewUrl = $env:MERIDIAN_CLUSTER_REVIEW_URL
$env:MERIDIAN_CLUSTER_REVIEW_URL = $reviewUrl
[ordered]@{ passed=$false; pending=$true; mode='full-host-fixture-only'; originalSavedDataFilesAccessed=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Encoding utf8NoBOM
Push-Location -LiteralPath $workspace
try {
    $ownedHost = Start-Process -FilePath (Join-Path $stage 'Meridian.Host.exe') -ArgumentList @('--urls',$reviewUrl,'--data-root',('"'+$dataRoot+'"')) -WorkingDirectory $stage -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'host.stdout.log') -RedirectStandardError (Join-Path $evidence 'host.stderr.log')
    $ready = $false
    for ($attempt=0; $attempt -lt 100; $attempt++) {
        $ownedHost.Refresh(); if ($ownedHost.HasExited) { throw 'The owned review backend exited.' }
        try { $status = Invoke-RestMethod ($reviewUrl + '/__meridian_status') -TimeoutSec 1; if ($status.application -eq 'atlas-meridian') { $ready=$true; break } } catch { }
        Start-Sleep -Milliseconds 100
    }
    if (!$ready) { throw 'Owned review backend did not become ready.' }
    $owners = @(Get-NetTCPConnection -LocalPort $ReviewPort -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($owners.Count -ne 1 -or $owners[0] -ne $ownedHost.Id) { throw 'Unexpected review port ownership.' }
    $catalog = Invoke-RestMethod ($reviewUrl + '/api/projects')
    if ($catalog.Count -ne 0) { throw 'Fixture storage was not initially empty.' }
    $listOutput = & npx playwright test --config scripts/collapsible-cluster-cycle/package-playwright.config.ts --list --reporter=json
    if ($LASTEXITCODE -ne 0) { throw 'Candidate browser cases could not be enumerated.' }
    $planText = $listOutput -join [Environment]::NewLine
    $plan = $planText | ConvertFrom-Json
    $expectedTests = Count-BrowserCases $plan
    if ($expectedTests -le 0) { throw 'Candidate browser suite must contain at least one planned case.' }
    Set-Content -LiteralPath (Join-Path $evidence 'browser-plan.json') -Value $planText -Encoding utf8NoBOM
    $browserPlanHash = (Get-FileHash -LiteralPath (Join-Path $evidence 'browser-plan.json') -Algorithm SHA256).Hash
    & npx playwright test --config scripts/collapsible-cluster-cycle/package-playwright.config.ts
    if ($LASTEXITCODE -ne 0) { throw 'Candidate browser checks failed.' }
    $results = Get-Content -LiteralPath (Join-Path $evidence 'browser-results.json') -Raw | ConvertFrom-Json
    if ($results.stats.unexpected -ne 0 -or $results.stats.flaky -ne 0 -or $results.stats.skipped -ne 0 -or $results.stats.expected -ne $expectedTests -or (Count-BrowserCases $results) -ne $expectedTests) { throw "Expected $expectedTests passing candidate browser cases without skips or retries." }
    $browserResultsHash = (Get-FileHash -LiteralPath (Join-Path $evidence 'browser-results.json') -Algorithm SHA256).Hash
    $passed = $true
} finally {
    $cleanup = $null
    try {
        if ($ownedHost) {
            $ownedHost.Refresh()
            if (!$ownedHost.HasExited) {
                $identity = Get-CimInstance Win32_Process -Filter "ProcessId=$($ownedHost.Id)"
                if ($identity.ExecutablePath -ne (Join-Path $stage 'Meridian.Host.exe') -or $identity.CommandLine -notmatch [Regex]::Escape($dataRoot)) { throw 'Owned QA identity changed; leave the process untouched.' }
                $ownedHost.Kill(); if (!$ownedHost.WaitForExit(10000)) { throw 'Owned QA process did not stop.' }
            }
        }
    } catch { $cleanup = $_.Exception.Message }
    Pop-Location
    $env:MERIDIAN_CLUSTER_REVIEW_URL = $previousReviewUrl
    $installedUnchanged = Equal-Json $installedBefore (Application-Inventory $installed)
    $packageUnchanged = Equal-Json $packageBefore (Application-Inventory $stage)
    $nativeUnchanged = Equal-Json $nativeBefore (Native-Identity)
    $hostUnchanged = Equal-Json $hostBefore (Host-Identity $ExpectedProcessId)
    $manifestUnchanged = $manifestHash -ceq (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
    $sourceUnchanged = Equal-Json $sourceBefore (Source-Inventory)
    $allProtected = $installedUnchanged -and $packageUnchanged -and $nativeUnchanged -and $hostUnchanged -and $manifestUnchanged -and $sourceUnchanged
    [ordered]@{ passed=$passed -and !$cleanup -and $allProtected; mode='full-host-fixture-only'; packageRoot=$stage; isolatedDataRoot=$dataRoot; originalSavedDataFilesAccessed=$false;
        ownedHostId=$ownedHost.Id; ownedHostStopped=!$ownedHost -or $ownedHost.HasExited; cleanupFailure=$cleanup; browserChecks=$expectedTests; browserPlanHash=$browserPlanHash; browserResultsHash=$browserResultsHash; browserEnvironmentHash=$browserEnvironmentHash; reviewPort=$ReviewPort; installedFilesUnchanged=$installedUnchanged;
        packageFilesUnchanged=$packageUnchanged; liveHostUnchanged=$hostUnchanged; nativeWindowsUnchanged=$nativeUnchanged; launcherManifestUnchanged=$manifestUnchanged; sourceUnchanged=$sourceUnchanged;
        packageHashes=$packageBefore; installedHashes=$installedBefore; liveHost=$hostBefore; native=$nativeBefore; manifestHash=$manifestHash; sourceHashes=$sourceBefore } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'package-review.json') -Encoding utf8NoBOM
    if (!$allProtected) { throw 'Protected application/source identity changed during isolated review; inspect evidence.' }
    if ($cleanup) { throw $cleanup }
}
Write-Output "Verified $expectedTests browser scenarios against the isolated full package. Owned QA host stopped. Live PID $ExpectedProcessId, native windows and launcher stayed unchanged. Saved data was never read, copied or restored."






