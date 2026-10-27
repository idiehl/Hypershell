#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stable-common.ps1')
$stateRoot = Get-StableStateRoot
$config = Get-StableConfig
if ($config.meridianOrigin -ne 'http://127.0.0.1:5240') { throw 'This installed-backend task is restricted to the verified local Meridian origin.' }
if (Test-Path -LiteralPath (Join-Path $stateRoot 'backend.stop.request')) { exit 0 }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$installedRoot = Join-Path $projectRoot 'output/app'
$executable = Join-Path $installedRoot 'Meridian.Host.exe'
$dataRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian'))
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'The installed Meridian backend executable is missing.' }
try {
    $health = Invoke-RestMethod "$($config.meridianOrigin)/__meridian_status" -TimeoutSec 3
    if ($health.application -eq 'atlas-meridian' -and $health.status -eq 'ready') { exit 0 }
} catch { }
if (Get-NetTCPConnection -State Listen -LocalPort 5240 -ErrorAction SilentlyContinue) { throw 'The Meridian backend port is occupied; no process will be adopted or stopped.' }
$receiptPath = Join-Path $stateRoot 'backend-run.json'
$process = Start-Process -FilePath $executable -ArgumentList @('--urls','http://127.0.0.1:5240','--data-root',('"' + $dataRoot + '"')) -WorkingDirectory $installedRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stateRoot 'backend.stdout.log') -RedirectStandardError (Join-Path $stateRoot 'backend.stderr.log')
$receipt = @{ owner = 'atlas-meridian-local-backend-task-v1'; process = (Get-ProcessIdentity $process); wrapper = (Get-ProcessIdentity (Get-Process -Id $PID)); dataRoot = $dataRoot; status = 'running'; startedAt = [DateTime]::UtcNow.ToString('o') }
Write-StableJson $receiptPath $receipt
try {
    $process.WaitForExit()
    $receipt.exitCode = $process.ExitCode
    $receipt.status = 'stopped'
} finally {
    $receipt.stoppedAt = [DateTime]::UtcNow.ToString('o')
    Write-StableJson $receiptPath $receipt
}
exit $process.ExitCode
