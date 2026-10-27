#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TunnelId,
    [Parameter(Mandatory)][string[]]$ProjectIds,
    [Parameter(Mandatory)][string]$TunnelPath,
    [Parameter(Mandatory)][string]$TunnelSha256,
    [string]$NodePath = 'C:\Program Files\nodejs\node.exe',
    [string]$MeridianOrigin = 'http://127.0.0.1:5240',
    [string]$BrowserExecutablePath,
    [Security.SecureString]$RuntimeKey,
    [switch]$KeepExistingKey
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'secure-tunnel-common.ps1')
$root = Initialize-SecureTunnelState
$configPath = Join-Path $root 'config.json'
$existing = if (Test-Path -LiteralPath $configPath) { Assert-SecureRegularPath $configPath | Out-Null; Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
$updates = @{
    transport='openai-secure-tunnel-stdio'; authoringEnabled=$true; authoringConsent=$true
    projectIds=@($ProjectIds | Select-Object -Unique); meridianOrigin=$MeridianOrigin; stateRoot=$root
    tunnelId=$TunnelId; auditPath=(Join-Path $root 'audit.jsonl')
    launcher=@{ nodePath=$NodePath; serverPath=(Join-Path (Split-Path $PSScriptRoot -Parent) 'src/stdio.mjs'); tunnelPath=$TunnelPath; tunnelSha256=$TunnelSha256.ToLowerInvariant() }
}
if ($PSBoundParameters.ContainsKey('BrowserExecutablePath')) { $updates.browserExecutablePath = $BrowserExecutablePath }
if ($existing.Count -and -not $PSBoundParameters.ContainsKey('NodePath')) { $updates.launcher.nodePath = $existing.launcher.nodePath }
if ($existing.Count -and -not $PSBoundParameters.ContainsKey('MeridianOrigin')) { $updates.meridianOrigin = $existing.meridianOrigin }
$config = Merge-SecureTunnelConfig $existing $updates
Assert-SecureTunnelConfig $config $root | Out-Null
Assert-SecureTunnelBinary $config
if ($KeepExistingKey) { $key = Get-SecureTunnelKey; $key.Dispose() }
else {
    if (-not $RuntimeKey) { $RuntimeKey = Read-Host 'Paste the restricted OpenAI runtime key (Tunnels Read + Use; input hidden)' -AsSecureString }
    try { Save-SecureTunnelKey $RuntimeKey $root } finally { $RuntimeKey.Dispose() }
}
if (Test-Path -LiteralPath $configPath) { Copy-Item -LiteralPath $configPath -Destination (Join-Path $root ('config.backup.' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + '.json')) }
Write-SecureTunnelJson $configPath $config
# Explicit start is required; setup never re-enables the legacy ngrok task.
[IO.File]::WriteAllText((Join-Path $root 'stop.request'), 'Configured; awaiting explicit Secure Tunnel start.')
[pscustomobject]@{ configured=$true; transport=$config.transport; tunnelId=$TunnelId; projects=$config.projectIds.Count; keyStorage='Windows DPAPI CurrentUser'; stateRoot=$root; started=$false } | ConvertTo-Json
