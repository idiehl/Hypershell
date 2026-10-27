[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$ExpectedProcessId)
. (Join-Path $PSScriptRoot 'common.ps1')
if (@(Get-NetTCPConnection -LocalPort 5315 -State Listen -ErrorAction SilentlyContinue).Count) { throw 'Inspection port 5315 is occupied; leave it untouched.' }
$before = Application-Inventory $installed; $hostBefore=Host-Identity $ExpectedProcessId; $nativeBefore=Native-Identity; Assert-ExpectedNative $nativeBefore
$manifestHash=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
$dataRoot=[IO.Path]::GetFullPath((Join-Path $workspace ('.test-data/clusters-outline-' + (Get-Date -Format 'yyyyMMdd-HHmmssffff'))))
if (!$dataRoot.StartsWith($workspace+'\.test-data\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $dataRoot)) { throw 'Inspection data must be fresh under .test-data.' }
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$owned=$null
Push-Location -LiteralPath $workspace
try {
    $owned=Start-Process -FilePath (Join-Path $stage 'Meridian.Host.exe') -WorkingDirectory $stage -ArgumentList @('--urls','http://127.0.0.1:5315','--data-root',('"'+$dataRoot+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'outline-host.stdout.log') -RedirectStandardError (Join-Path $evidence 'outline-host.stderr.log')
    $ready=$false
    for($attempt=0;$attempt -lt 100;$attempt++){ $owned.Refresh();if($owned.HasExited){throw 'Inspection host exited.'};try{$status=Invoke-RestMethod 'http://127.0.0.1:5315/__meridian_status' -TimeoutSec 1;if($status.application -eq 'atlas-meridian'){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 100 }
    if(!$ready){throw 'Inspection host did not become ready.'}
    $owners=@(Get-NetTCPConnection -LocalPort 5315 -State Listen | Select-Object -ExpandProperty OwningProcess -Unique)
    if($owners.Count -ne 1 -or $owners[0] -ne $owned.Id){throw 'Inspection listener ownership changed.'}
    & npx playwright test --config output/clusters-modules-20261010/package/qa/outline-playwright.config.ts
    if($LASTEXITCODE -ne 0){throw 'Outline inspection failed.'}
}finally{
    if($owned){$owned.Refresh();if(!$owned.HasExited){$identity=Get-CimInstance Win32_Process -Filter "ProcessId=$($owned.Id)";if($identity.ExecutablePath -ne (Join-Path $stage 'Meridian.Host.exe') -or $identity.CommandLine -notmatch [Regex]::Escape($dataRoot)){throw 'Inspection process identity changed; leave it untouched.'};$owned.Kill();if(!$owned.WaitForExit(10000)){throw 'Owned inspection host did not stop.'}}}
    Pop-Location
    Assert-Inventory $installed $before
    if(!(Equal-Json $hostBefore (Host-Identity $ExpectedProcessId)) -or !(Equal-Json $nativeBefore (Native-Identity)) -or $manifestHash -cne (Get-FileHash -LiteralPath $manifest).Hash){throw 'Protected runtime changed during inspection.'}
}
Write-Output 'Read-only outline inspection complete. Owned fixture host stopped; installed application/runtime and saved data preserved.'
