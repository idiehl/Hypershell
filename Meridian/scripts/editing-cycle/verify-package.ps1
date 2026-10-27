$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$packageApp = Join-Path $workspace 'output/editing-20261005/app'
$packageExecutable = Join-Path $packageApp 'Meridian.Host.exe'
$packageData = Join-Path $workspace ('.test-data/editing-package-' + [Guid]::NewGuid().ToString('N'))
$packageOutput = Join-Path $workspace 'output/editing-qa'
if (-not (Test-Path -LiteralPath $packageExecutable -PathType Leaf)) { throw 'The staged editing package is not ready.' }
if (Get-NetTCPConnection -LocalPort 5293 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 5293 is already in use; no process was changed.' }
New-Item -ItemType Directory -Force -Path $packageOutput | Out-Null
$packageProcess = Start-Process -FilePath $packageExecutable -WorkingDirectory $packageApp -ArgumentList @('--urls', 'http://127.0.0.1:5293', '--data-root', $packageData) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $packageOutput 'package-host.stdout.log') -RedirectStandardError (Join-Path $packageOutput 'package-host.stderr.log')
$passed = $false
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($packageProcess.HasExited) { throw 'The isolated packaged host exited before readiness.' }
        try { $status = Invoke-RestMethod 'http://127.0.0.1:5293/__meridian_status'; $ready = $status.status -eq 'ready'; if ($ready) { break } } catch {}
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'The isolated packaged host did not become ready.' }
    Push-Location $workspace
    try {
        & node --experimental-strip-types scripts/editing-cycle/verify-package.mjs
        if ($LASTEXITCODE -ne 0) { throw "Package verification exited with code $LASTEXITCODE" }
        $passed = $true
    } finally { Pop-Location }
} finally {
    if (-not $packageProcess.HasExited) {
        $ownedProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$($packageProcess.Id)"
        if ($ownedProcess.ExecutablePath -ne $packageExecutable) { throw 'The package process identity changed; refusing to stop an unrelated process.' }
        Stop-Process -Id $packageProcess.Id
        $packageProcess.WaitForExit(10000) | Out-Null
    }
    [pscustomobject]@{ processId = $packageProcess.Id; executable = $packageExecutable; dataRoot = $packageData; origin = 'http://127.0.0.1:5293'; stopped = $packageProcess.HasExited; passed = $passed; completedAt = [DateTimeOffset]::Now.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageOutput 'package-process.json') -Encoding utf8
}
