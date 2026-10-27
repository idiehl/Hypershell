#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$stateRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion'))
$runPath = Join-Path $stateRoot 'run.json'
$run = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json
$config = Get-Content -LiteralPath $run.configPath -Raw | ConvertFrom-Json
$expectedConfigPath = Join-Path $stateRoot 'config.json'
$expectedServerPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/server.mjs'
if ($run.configPath -ne $expectedConfigPath -or $run.serverPath -ne $expectedServerPath -or $run.endpoint -ne ($config.publicOrigin + '/mcp')) { throw 'The recorded companion configuration does not match this private installation.' }
$tunnel = Get-Process -Id $run.tunnelPid -ErrorAction Stop
if ($tunnel.Path -ne $run.tunnelPath -or [Math]::Abs(($tunnel.StartTime.ToUniversalTime() - ([DateTimeOffset]$run.tunnelStartedAt).UtcDateTime).TotalSeconds) -gt 1) { throw 'The recorded tunnel process identity changed. No helper was restarted.' }
$previous = Get-Process -Id $run.companionPid -ErrorAction SilentlyContinue
if ($previous) {
    if ($previous.Path -ne $run.nodePath -or [Math]::Abs(($previous.StartTime.ToUniversalTime() - ([DateTimeOffset]$run.companionStartedAt).UtcDateTime).TotalSeconds) -gt 1) { throw 'The recorded companion process identity changed. No process was stopped.' }
    Stop-Process -Id $previous.Id
    $previous.WaitForExit(5000) | Out-Null
}
$companion = Start-Process -FilePath $run.nodePath -ArgumentList @(('"' + $run.serverPath + '"'),'--config',('"' + $run.configPath + '"')) -WorkingDirectory (Split-Path $PSScriptRoot -Parent) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stateRoot 'companion.stdout.log') -RedirectStandardError (Join-Path $stateRoot 'companion.stderr.log')
try {
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 200
        if ($companion.HasExited) { throw 'The companion exited. Check its private error log.' }
        try {
            $discovery = Invoke-RestMethod "http://127.0.0.1:$($config.port)/.well-known/oauth-authorization-server" -TimeoutSec 2
            $ready = $discovery.issuer -eq $config.publicOrigin
        } catch { }
    } until ($ready -or [DateTime]::UtcNow -ge $deadline)
    if (-not $ready) { throw 'The restarted companion did not become ready.' }
    $run.companionPid = $companion.Id
    $run.companionStartedAt = $companion.StartTime.ToUniversalTime().ToString('o')
    $run | Add-Member -NotePropertyName companionRestartedAt -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
    $run | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $runPath -Encoding utf8NoBOM
    [pscustomobject]@{ endpoint=$run.endpoint; companionPid=$run.companionPid; tunnelPid=$run.tunnelPid; endpointAndCredentialsPreserved=$true; pendingAuthorizationsRevoked=$true } | ConvertTo-Json
} catch {
    if (-not $companion.HasExited) { Stop-Process -Id $companion.Id -ErrorAction SilentlyContinue }
    throw
}
