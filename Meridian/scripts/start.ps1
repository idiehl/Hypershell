[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 5240,
    [string]$DataRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AtlasMeridian'),
    [switch]$Foreground
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'output/app/Meridian.Host.exe'
$dll = Join-Path $projectRoot 'src/host/bin/Debug/net10.0/Meridian.Host.dll'
if (-not (Test-Path -LiteralPath $executable)) {
    Push-Location -LiteralPath $projectRoot
    try {
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Meridian browser build failed.' }
        & dotnet build (Join-Path $projectRoot 'src/host/Meridian.Host.csproj') --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Meridian host build failed.' }
    } finally { Pop-Location }
    $executable = (Get-Command dotnet -ErrorAction Stop).Source
    $arguments = @($dll)
} else { $arguments = @() }
$DataRoot = [System.IO.Path]::GetFullPath($DataRoot)
$arguments += @('--urls', "http://127.0.0.1:$Port", '--data-root', $DataRoot)
if ($Foreground) {
    & $executable @arguments
    exit $LASTEXITCODE
}
$probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
try { $probe.Start() } catch { throw "Port $Port is already occupied. Open the existing app or choose another port with -Port." } finally { $probe.Stop() }
$logRoot = Join-Path $DataRoot 'logs'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-ffff')
$stdout = Join-Path $logRoot "$stamp.out.log"
$stderr = Join-Path $logRoot "$stamp.err.log"
# Start-Process joins ArgumentList into a Windows command line; quote each path/value.
$quotedArguments = $arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }
$process = Start-Process -FilePath $executable -ArgumentList $quotedArguments -WorkingDirectory $projectRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
$ready = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    $process.Refresh()
    if ($process.HasExited) { throw "Meridian exited before becoming ready. See $stderr" }
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/__meridian_status" -TimeoutSec 1
        if ($response.Headers['X-Atlas-Meridian'] -contains 'true') { $ready = $true; break }
    } catch { }
    Start-Sleep -Milliseconds 250
}
if (-not $ready) { throw "Meridian has not passed its health check yet (PID $($process.Id)). See $stderr" }
Write-Output "Atlas Meridian: http://127.0.0.1:$Port/"
Write-Output "Process: $($process.Id); data: $DataRoot"
Write-Output "Logs: $stdout"
