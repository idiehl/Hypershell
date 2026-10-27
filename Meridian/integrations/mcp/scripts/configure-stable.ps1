#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublicOrigin,
    [ValidateSet('external','ngrok','cloudflare')][string]$Provider = 'external',
    [int]$Port = 5242,
    [string]$TunnelExecutable = '',
    [string]$TunnelSha256 = '',
    [string]$TunnelConfigPath = '',
    [string]$TunnelId = '',
    [string]$NodePath = '',
    [switch]$ResetAuthorization
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stable-common.ps1')
$origin = Assert-StableOrigin $PublicOrigin
if ($Port -lt 1024 -or $Port -gt 65535 -or $Port -eq 5241) { throw 'Use an unused non-privileged stable companion port, separate from the current prototype (5241).' }
$root = Initialize-StableState
$runPath = Join-Path $root 'run.json'
if (Test-Path -LiteralPath $runPath) {
    $run = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json -AsHashtable
    if ((Get-OwnedProcess $run.supervisor) -or (Get-OwnedProcess $run.companion) -or (Get-OwnedProcess $run.tunnel)) { throw 'Stop the stable supervisor before changing its configuration.' }
}
$configPath = Join-Path $root 'config.json'
if (Test-Path -LiteralPath $configPath) { $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json -AsHashtable }
else {
    $prototypePath = Join-Path (Split-Path $root -Parent) 'config.json'
    if (-not (Test-Path -LiteralPath $prototypePath)) { throw 'No existing private companion configuration is available to migrate.' }
    $config = Get-Content -LiteralPath $prototypePath -Raw | ConvertFrom-Json -AsHashtable
}
if ((Test-Path -LiteralPath $configPath) -and $config.publicOrigin -ne $origin -and -not $ResetAuthorization) { throw 'Changing the stable OAuth issuer requires -ResetAuthorization and relinking ChatGPT.' }
if (-not $NodePath) {
    $systemNode = Get-Command node.exe -ErrorAction SilentlyContinue
    $NodePath = if ($systemNode) { $systemNode.Source } else { Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe' }
}
$NodePath = [IO.Path]::GetFullPath($NodePath)
$version = & $NodePath --version
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^v(\d+)\.' -or [int]$Matches[1] -lt 24) { throw 'Node.js 24 or later is required.' }
$launcher = @{ nodePath = $NodePath; serverPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src/server.mjs'); provider = $Provider }
if ($Provider -ne 'external') {
    $TunnelExecutable = [IO.Path]::GetFullPath($TunnelExecutable)
    Assert-PinnedTunnelBinary $TunnelExecutable $TunnelSha256
    $TunnelConfigPath = [IO.Path]::GetFullPath($TunnelConfigPath)
    if (-not (Test-Path -LiteralPath $TunnelConfigPath -PathType Leaf)) { throw 'An existing private provider configuration file is required. Account provisioning is not performed by this script.' }
    if ($Provider -eq 'cloudflare' -and $TunnelId -notmatch '^[a-fA-F0-9]{8}(-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}$') { throw 'Cloudflare requires the existing named tunnel UUID.' }
    $launcher.tunnelPath = $TunnelExecutable; $launcher.tunnelSha256 = $TunnelSha256.ToLowerInvariant()
    $launcher.tunnelConfigPath = $TunnelConfigPath; $launcher.tunnelId = $TunnelId
}
$config.publicOrigin = $origin; $config.port = $Port
$config.stateRoot = $root
$config.authoringEnabled = $true
$config.auditPath = Join-Path $root 'audit.jsonl'
if ($ResetAuthorization) { $config.auth.grantStorePath = Join-Path $root ('grants-' + [Guid]::NewGuid().ToString('N') + '.json') }
elseif (-not $config.auth.ContainsKey('grantStorePath') -or $config.auth.grantStorePath -notlike "$root\*") { $config.auth.grantStorePath = Join-Path $root 'grants.json' }
$config.launcher = $launcher
Write-StableJson $configPath $config
[pscustomobject]@{ configured = $true; endpoint = "$origin/mcp"; port = $Port; provider = $Provider; startupInstalled = $false; currentPrototypeUnchanged = $true } | ConvertTo-Json
