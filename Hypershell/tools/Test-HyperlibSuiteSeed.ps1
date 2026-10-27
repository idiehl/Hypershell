[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $HyperlibPayloadDirectory,
    [Parameter(Mandatory)][string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$payload = [IO.Path]::GetFullPath($HyperlibPayloadDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new isolated seed-test output directory.' }
if ($output.StartsWith($payload.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Seed verification output must be outside the installed payload.'
}
$hostExecutable = Join-Path $payload 'app/HyperLib.Host.exe'
foreach ($required in @('app/HyperLib.Host.exe', 'hyperlib.app.json', 'src/HyperLib.Core/HyperLib.Core.csproj',
    'src/HyperLib.Runtime/HyperLib.Runtime.csproj', 'Starters/standard/src/Host/Host.csproj', 'Starters/standard/src/Pages/Pages.csproj')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) { throw "Incomplete Hyperlib payload: $required" }
}
function Assert-OrdinaryPath([string] $Path) {
    for ($current = [IO.Path]::GetFullPath($Path); $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Seed test paths cannot contain symbolic links or junctions.'
        }
    }
}
function Copy-StockTree([string] $Source, [string] $Target) {
    Assert-OrdinaryPath $Source
    [IO.Directory]::CreateDirectory($Target) | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Assert-OrdinaryPath $item.FullName
        if ($item.PSIsContainer -and $item.Name -in @('bin', 'obj')) { continue }
        $destination = Join-Path $Target $item.Name
        if ($item.PSIsContainer) { Copy-StockTree $item.FullName $destination }
        else { Copy-Item -LiteralPath $item.FullName -Destination $destination }
    }
}
Assert-OrdinaryPath $hostExecutable
Assert-OrdinaryPath $output
$seed = Join-Path $output 'seed'
[IO.Directory]::CreateDirectory($seed) | Out-Null
Assert-OrdinaryPath (Join-Path $payload 'hyperlib.app.json')
Copy-Item -LiteralPath (Join-Path $payload 'hyperlib.app.json') -Destination $seed
foreach ($relative in @('src/HyperLib.Core', 'src/HyperLib.Runtime', 'Starters/standard')) {
    Copy-StockTree (Join-Path $payload $relative) (Join-Path $seed $relative)
}
foreach ($directory in @('Media', 'Works', 'Content')) { [IO.Directory]::CreateDirectory((Join-Path $seed $directory)) | Out-Null }
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
try { $listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port }
finally { $listener.Stop() }
$origin = "http://127.0.0.1:$port"
$startOptions = @{
    FilePath = $hostExecutable
    ArgumentList = @('--root', ('"' + $seed + '"'), '--port', "$port")
    WorkingDirectory = Join-Path $payload 'app'
    WindowStyle = 'Hidden'
    PassThru = $true
    RedirectStandardOutput = Join-Path $output 'host.stdout.log'
    RedirectStandardError = Join-Path $output 'host.stderr.log'
}
$process = Start-Process @startOptions
$startedAt = $process.StartTime.ToUniversalTime()
$evidence = [ordered]@{ payload = $payload; seed = $seed; processId = $process.Id; startedAt = $startedAt; origin = $origin }
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $health = $null
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "The isolated Hyperlib host exited with $($process.ExitCode)." }
        try { $health = Invoke-RestMethod "$origin/api/health" -TimeoutSec 2 }
        catch { Start-Sleep -Milliseconds 250 }
    } while ($null -eq $health -and [DateTime]::UtcNow -lt $deadline)
    if ($null -eq $health -or $health.root -ne $seed) { throw 'The health endpoint did not confirm the isolated seed.' }
    $evidence.health = $health
    $shell = Invoke-WebRequest "$origin/" -TimeoutSec 10
    $evidence.shellStatus = [int]$shell.StatusCode
    $templates = Invoke-RestMethod "$origin/api/base-templates" -TimeoutSec 15
    $evidence.templateCount = @($templates).Count
    if ($evidence.templateCount -eq 0) { throw 'The seed produced an empty reusable template catalog.' }
    $generation = Invoke-RestMethod "$origin/api/runtime/library" -TimeoutSec 120
    $evidence.generation = $generation
    $render = Invoke-WebRequest ($origin + $generation.proxyPrefix + 'browse/library') -TimeoutSec 30
    if ($render.Content.Contains('class="hl-error"') -or $render.Content.Contains('This component could not render:')) {
        throw 'The library renderer returned a content error.'
    }
    $render.Content | Set-Content -LiteralPath (Join-Path $output 'render.html') -Encoding utf8NoBOM
    $evidence.renderStatus = [int]$render.StatusCode
    $evidence.result = 'HYPERLIB_SUITE_SEED_OK'
}
catch { $evidence.result = 'HYPERLIB_SUITE_SEED_FAILED'; $evidence.error = $_.Exception.Message; throw }
finally {
    $current = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    if ($null -ne $current -and $current.Path -eq $hostExecutable -and $current.StartTime.ToUniversalTime() -eq $startedAt) {
        Stop-Process -Id $process.Id
        $process.WaitForExit(10000) | Out-Null
    }
    $evidence.hostStopped = -not [bool](Get-Process -Id $process.Id -ErrorAction SilentlyContinue)
    $evidence | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $output 'evidence.json') -Encoding utf8NoBOM
}
Write-Output "HYPERLIB_SUITE_SEED_OK|shell:$($evidence.shellStatus)|render:$($evidence.renderStatus)|templates:$($evidence.templateCount)|$output"
