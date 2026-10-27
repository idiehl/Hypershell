$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$packageApp = Join-Path $workspace 'output/spatial-20261005/app'
$packageExecutable = Join-Path $packageApp 'Meridian.Host.exe'
$packageData = Join-Path $workspace '.test-data/spatial-packaged'
$packageOutput = Join-Path $workspace 'output/spatial-qa'
if (Get-NetTCPConnection -LocalPort 5296 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 5296 is already in use; no process was changed.' }
New-Item -ItemType Directory -Force -Path $packageOutput | Out-Null
$packageProcess = Start-Process -FilePath $packageExecutable -WorkingDirectory $packageApp -ArgumentList @('--urls', 'http://127.0.0.1:5296', '--data-root', $packageData) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $packageOutput 'package-host.stdout.log') -RedirectStandardError (Join-Path $packageOutput 'package-host.stderr.log')
$passed = $false
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($packageProcess.HasExited) { throw 'Packaged host exited before readiness.' }
        try { $status = Invoke-RestMethod 'http://127.0.0.1:5296/__meridian_status'; $ready = $status.status -eq 'ready'; if ($ready) { break } } catch {}
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'Packaged host did not become ready.' }
    Push-Location $workspace
    try {
        & node --experimental-strip-types scripts/spatial-reveal/verify-package.mjs
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
    [pscustomobject]@{ processId = $packageProcess.Id; executable = $packageExecutable; dataRoot = $packageData; origin = 'http://127.0.0.1:5296'; stopped = $packageProcess.HasExited; passed = $passed; completedAt = [DateTimeOffset]::Now.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageOutput 'package-process.json') -Encoding utf8
}
