param([int]$Port = 54180, [switch]$OpenBrowser)
$ErrorActionPreference = 'Stop'
$libraryRoot = $PSScriptRoot
$url = "http://127.0.0.1:$Port"
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Choose a port between 1024 and 65535.' }

function Assert-NoHostLinks([string]$Path) {
    for ($current = [IO.Path]::GetFullPath($Path); $null -ne $current; $current = [IO.Path]::GetDirectoryName($current)) {
        try { $attributes = [IO.File]::GetAttributes($current) }
        catch [IO.FileNotFoundException] { continue }
        catch [IO.DirectoryNotFoundException] { continue }
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Linked HyperLib host targets are not supported.'
        }
    }
}
function Test-HostPathEntry([string]$Path) {
    try { $null = [IO.File]::GetAttributes($Path); return $true }
    catch [IO.FileNotFoundException] { return $false }
    catch [IO.DirectoryNotFoundException] { return $false }
    catch { return $true }
}
function Resolve-InstalledHost([string]$Pointer) {
    try {
        Assert-NoHostLinks $Pointer
        if (-not [IO.File]::Exists($Pointer) -or (Get-Item -LiteralPath $Pointer).Length -gt 16384) { throw 'The pointer is missing or too large.' }
        $target = [IO.File]::ReadAllText($Pointer) | ConvertFrom-Json -ErrorAction Stop
        if ($target -isnot [pscustomobject] -or $target.relativeExecutable -isnot [string]) { throw 'The pointer must name a relative executable.' }
        $relative = $target.relativeExecutable
        $parts = $relative.Replace('\', '/').Split('/')
        if ($relative.Length -eq 0 -or $relative.Length -gt 400 -or [IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or
            @($relative.ToCharArray() | Where-Object { [char]::IsControl($_) }).Count -gt 0 -or
            @($parts | Where-Object { $_ -in @('', '.', '..') -or $_.EndsWith('.') -or $_.EndsWith(' ') }).Count -gt 0 -or
            -not [string]::Equals($parts[-1], 'HyperLib.Host.exe', [StringComparison]::OrdinalIgnoreCase)) { throw 'The pointer contains an unsafe executable path.' }
        $appRoot = [IO.Path]::GetFullPath((Join-Path $libraryRoot 'app')) + [IO.Path]::DirectorySeparatorChar
        $candidate = [IO.Path]::GetFullPath((Join-Path $appRoot $relative))
        if (-not $candidate.StartsWith($appRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'The executable is outside the app directory.' }
        Assert-NoHostLinks $candidate
        if (-not [IO.File]::Exists($candidate)) { throw 'The installed executable is missing.' }
        return $candidate
    } catch {
        throw "The installed HyperLib host target is invalid. Restore app/host-target.json or reinstall HyperLib. $($_.Exception.Message)"
    }
}
function Test-ExpectedHostExecutable([string]$Expected) {
    try {
        $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop |
            Where-Object { $_.LocalAddress -in @('127.0.0.1', '0.0.0.0', '::', '::ffff:127.0.0.1') } |
            Sort-Object @{ Expression = { if ($_.LocalAddress -eq '127.0.0.1') { 0 } else { 1 } } } |
            Select-Object -First 1
        if ($null -eq $listener) { return $false }
        $actual = (Get-Process -Id $listener.OwningProcess -ErrorAction Stop).Path
        return -not [string]::IsNullOrWhiteSpace($actual) -and
            [string]::Equals([IO.Path]::GetFullPath($actual), [IO.Path]::GetFullPath($Expected), [StringComparison]::OrdinalIgnoreCase)
    } catch { return $false }
}

$pointer = Join-Path $libraryRoot 'app/host-target.json'
$hasInstalledTarget = Test-HostPathEntry $pointer
$executable = if ($hasInstalledTarget) { Resolve-InstalledHost $pointer } else { Join-Path $libraryRoot 'app/HyperLib.Host.exe' }
$running = $false
$health = $null
try {
    $health = Invoke-RestMethod -Uri "$url/api/health" -TimeoutSec 2
} catch { }
if ($null -ne $health) {
    if ($health.app -ne 'hyperlib' -or [IO.Path]::GetFullPath($health.root) -ne $libraryRoot) { throw 'This port belongs to a different application or library.' }
    if (-not (Test-ExpectedHostExecutable $executable)) {
        throw "Port $Port is serving an older or unverified HyperLib host. That session was left running. Start with -Port followed by an unused port, or close that session before retrying."
    }
    $running = $true
}
if (-not $running) {
    if (-not (Test-Path -LiteralPath $executable)) {
        if ($hasInstalledTarget) { throw 'The installed HyperLib host disappeared. Reinstall it before launching.' }
        dotnet publish (Join-Path $libraryRoot 'src/HyperLib.Host/HyperLib.Host.csproj') -c Release -o (Join-Path $libraryRoot 'app') --nologo
        if ($LASTEXITCODE -ne 0) { throw 'HyperLib could not be built.' }
    }
    $arguments = '--root "{0}" --port {1}' -f $libraryRoot, $Port
    $logDirectory = Join-Path $libraryRoot '.hyperlib'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $libraryRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory "host-$stamp.log") -RedirectStandardError (Join-Path $logDirectory "host-$stamp-error.log")
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try { $health = Invoke-RestMethod -Uri "$url/api/health" -TimeoutSec 1; if ($health.app -eq 'hyperlib' -and [IO.Path]::GetFullPath($health.root) -eq $libraryRoot -and (Test-ExpectedHostExecutable $executable)) { $ready = $true; break } } catch { }
        if ($process.HasExited) { throw "HyperLib stopped during startup. Check the host logs in $logDirectory." }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'HyperLib did not become ready. Check that the selected port is available.' }
}
if ($OpenBrowser) { Start-Process $url }
Write-Output $url
