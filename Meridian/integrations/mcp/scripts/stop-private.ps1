#Requires -Version 7.0
[CmdletBinding()]
param([string]$StateDirectory = (Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion'))
$ErrorActionPreference = 'Stop'
$stateRoot = [IO.Path]::GetFullPath($StateDirectory)
$expectedStateRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion'))
if ($stateRoot.TrimEnd('\','/') -ne $expectedStateRoot.TrimEnd('\','/')) { throw 'Private state must use the dedicated AtlasMeridian/McpCompanion directory.' }
$runPath = Join-Path $stateRoot 'run.json'
if (-not (Test-Path -LiteralPath $runPath)) { throw 'No private companion run receipt was found.' }
$run = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json
foreach ($entry in @(
    @{ id = $run.tunnelPid; path = $run.tunnelPath; startedAt = $run.tunnelStartedAt; label = 'tunnel' },
    @{ id = $run.companionPid; path = $run.nodePath; startedAt = $run.companionStartedAt; label = 'companion' }
)) {
    $process = Get-Process -Id $entry.id -ErrorAction SilentlyContinue
    if (-not $process) { continue }
    if ($process.Path -ne $entry.path -or [Math]::Abs(($process.StartTime.ToUniversalTime() - ([DateTimeOffset]$entry.startedAt).UtcDateTime).TotalSeconds) -gt 1) { throw "The recorded $($entry.label) process identity changed. No action was taken against that process." }
    Stop-Process -Id $process.Id
}
$run | Add-Member -NotePropertyName stoppedAt -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
$run | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $runPath -Encoding utf8NoBOM
Write-Output 'Stopped the private HTTPS tunnel and MCP companion. In-memory authorization grants are revoked. Meridian remains open.'
