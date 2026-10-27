#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$StateDirectory = (Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion'),
    [int]$Port = 5241,
    [string[]]$ProjectIds = @(),
    [string]$NodePath = ''
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$packageRoot = Split-Path $PSScriptRoot -Parent
$serverPath = Join-Path $packageRoot 'src/server.mjs'
$stateRoot = [IO.Path]::GetFullPath($StateDirectory)
$expectedStateRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion'))
if ($stateRoot.TrimEnd('\','/') -ne $expectedStateRoot.TrimEnd('\','/')) { throw 'Private state must use the dedicated AtlasMeridian/McpCompanion directory.' }
foreach ($candidate in @($stateRoot, (Split-Path $stateRoot -Parent))) {
    if ((Test-Path -LiteralPath $candidate) -and ((Get-Item -LiteralPath $candidate -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'The private companion state directory and its Meridian parent must not be junctions or symbolic links.' }
}
$ownerMarkerPath = Join-Path $stateRoot '.meridian-mcp-private-state'
if ((Test-Path -LiteralPath $stateRoot) -and -not (Test-Path -LiteralPath $ownerMarkerPath) -and @(Get-ChildItem -LiteralPath $stateRoot -Force).Count) { throw 'The dedicated companion folder contains unrecognized files. Its permissions have not been changed.' }
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Choose an unused non-privileged companion port.' }
if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { throw 'The companion port is already in use. Stop the existing companion before starting another.' }
if (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'node_modules/@modelcontextprotocol/sdk/package.json'))) { throw 'Install the isolated companion dependencies with npm ci in integrations/mcp first.' }
if (-not $NodePath) {
    $bundledNode = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe'
    $NodePath = if (Test-Path -LiteralPath $bundledNode) { $bundledNode } else { (Get-Command node.exe -ErrorAction Stop).Source }
}
$NodePath = [IO.Path]::GetFullPath($NodePath)
$nodeVersion = & $NodePath --version
if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v(\d+)\.' -or [int]$Matches[1] -lt 22) { throw 'The companion requires Node.js 22 or later.' }
$status = Invoke-RestMethod 'http://127.0.0.1:5240/__meridian_status' -TimeoutSec 5
if ($status.application -ne 'atlas-meridian' -or $status.status -ne 'ready') { throw 'Open the local Meridian application before starting its companion.' }
$catalog = Invoke-RestMethod 'http://127.0.0.1:5240/api/library/catalog' -TimeoutSec 5
if (-not $ProjectIds.Count) { $ProjectIds = @($catalog | ForEach-Object { $_.id }) }
$knownIds = @($catalog | ForEach-Object { $_.id })
if (-not $ProjectIds.Count -or $ProjectIds.Count -gt 100 -or @($ProjectIds | Where-Object { $_ -notin $knownIds }).Count) { throw 'Only saved Meridian projects can be included in this explicit read-only allowlist.' }

# Restrict only the new companion folder. Never change the ACL of Meridian data.
[IO.Directory]::CreateDirectory($stateRoot) | Out-Null
$acl = [Security.AccessControl.DirectorySecurity]::new()
$acl.SetAccessRuleProtection($true, $false)
$inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
$propagation = [Security.AccessControl.PropagationFlags]::None
$rights = [Security.AccessControl.FileSystemRights]::FullControl
$allow = [Security.AccessControl.AccessControlType]::Allow
$owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl.SetOwner($owner)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($owner, $rights, $inheritance, $propagation, $allow))
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-18'), $rights, $inheritance, $propagation, $allow))
Set-Acl -LiteralPath $stateRoot -AclObject $acl
Set-Content -LiteralPath $ownerMarkerPath -Value 'atlas-meridian-private-mcp-v1' -Encoding ascii
$runPath = Join-Path $stateRoot 'run.json'
if (Test-Path -LiteralPath $runPath) {
    $prior = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json
    foreach ($processId in @($prior.companionPid, $prior.tunnelPid)) {
        if ($processId -and (Get-Process -Id $processId -ErrorAction SilentlyContinue)) { throw 'A recorded helper process is still running. Use stop-private.ps1 first.' }
    }
}
$binRoot = Join-Path $stateRoot 'bin'
[IO.Directory]::CreateDirectory($binRoot) | Out-Null
$cloudflaredPath = Join-Path $binRoot 'cloudflared-2026.10.3.exe'
$expectedHash = '76b2805253d0d6ffacb092a7acfbee99e7af6917835c0c5410e23f3ef5052da2'
if (-not (Test-Path -LiteralPath $cloudflaredPath)) {
    Invoke-WebRequest 'https://github.com/cloudflare/cloudflared/releases/download/2026.10.3/cloudflared-windows-amd64.exe' -OutFile $cloudflaredPath -TimeoutSec 60
}
if ((Get-Item -LiteralPath $cloudflaredPath).Length -ne 55329336 -or (Get-FileHash -LiteralPath $cloudflaredPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) { throw 'The Cloudflare binary does not match its pinned official release checksum. It has not been executed.' }

$tunnelOut = Join-Path $stateRoot 'tunnel.stdout.log'
$tunnelErr = Join-Path $stateRoot 'tunnel.stderr.log'
$companionOut = Join-Path $stateRoot 'companion.stdout.log'
$companionErr = Join-Path $stateRoot 'companion.stderr.log'
$configPath = Join-Path $stateRoot 'config.json'
$setupPath = Join-Path $stateRoot 'ChatGPT-Setup.html'
$tunnel = $null
$companion = $null
try {
    # The temporary tunnel initially has no origin listener: it cannot expose data.
    $tunnel = Start-Process -FilePath $cloudflaredPath -ArgumentList @('tunnel','--no-autoupdate','--url',"http://127.0.0.1:$Port",'--protocol','http2') -WindowStyle Hidden -PassThru -RedirectStandardOutput $tunnelOut -RedirectStandardError $tunnelErr
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $publicOrigin = $null
    do {
        Start-Sleep -Milliseconds 250
        if ($tunnel.HasExited) { throw 'The HTTPS helper exited before assigning an address. Check its private logs.' }
        $logs = (Get-Content -LiteralPath $tunnelOut -Raw -ErrorAction SilentlyContinue) + (Get-Content -LiteralPath $tunnelErr -Raw -ErrorAction SilentlyContinue)
        $urlMatch = [regex]::Match($logs, 'https://[a-z0-9-]+\.trycloudflare\.com')
        if ($urlMatch.Success) { $publicOrigin = $urlMatch.Value }
    } until ($publicOrigin -or [DateTime]::UtcNow -ge $deadline)
    if (-not $publicOrigin) { throw 'No temporary HTTPS address was assigned. Check the private tunnel logs.' }
    function New-PrivateValue {
        $bytes = New-Object byte[] 32
        [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
        [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
    }
    $config = [ordered]@{
        publicOrigin = $publicOrigin; port = $Port; meridianOrigin = 'http://127.0.0.1:5240'
        projectIds = @($ProjectIds); auditPath = (Join-Path $stateRoot 'audit.jsonl')
        auth = [ordered]@{ clientId = ('meridian-' + (New-PrivateValue)); clientSecret = (New-PrivateValue); pairingCode = (New-PrivateValue) }
    }
    $config | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM
    $companion = Start-Process -FilePath $NodePath -ArgumentList @(('"' + $serverPath + '"'),'--config',('"' + $configPath + '"')) -WorkingDirectory $packageRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput $companionOut -RedirectStandardError $companionErr
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 200
        if ($companion.HasExited) { throw 'The companion exited before it was ready. Check its private logs.' }
        try {
            $discovery = Invoke-RestMethod "http://127.0.0.1:$Port/.well-known/oauth-authorization-server" -TimeoutSec 2
            $ready = $discovery.issuer -eq $publicOrigin
        } catch { }
    } until ($ready -or [DateTime]::UtcNow -ge $deadline)
    if (-not $ready) { throw 'The companion did not become ready.' }
    $clientIdHtml = [Net.WebUtility]::HtmlEncode($config.auth.clientId)
    $clientSecretHtml = [Net.WebUtility]::HtmlEncode($config.auth.clientSecret)
    $pairingHtml = [Net.WebUtility]::HtmlEncode($config.auth.pairingCode)
    $setup = @"
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Meridian — Private ChatGPT setup</title>
<style>body{font:16px system-ui;line-height:1.6;max-width:760px;margin:48px auto;padding:0 24px;background:#0e1924;color:#e0edf1}h1{font-size:28px}label{display:block;margin-top:18px;font-weight:600}input{box-sizing:border-box;width:100%;font:14px ui-monospace,monospace;background:#172b39;color:#e0edf1;border:1px solid #3c616e;border-radius:6px;padding:12px}small{color:#a6c5cd}a{color:#83d9cd}button{background:#204c4b;color:#e0edf1;border:1px solid #638c8b;border-radius:5px;padding:6px 12px;cursor:pointer}li{margin:10px 0}</style>
<h1>Connect Meridian to ChatGPT</h1><p>This private file contains the credentials for this device. Copy them only into ChatGPT's connection settings and the Meridian authorization form. <strong>Never paste these secrets into a conversation or screenshot.</strong></p>
<ol><li>Open <a href="https://chatgpt.com/plugins" target="_blank" rel="noreferrer">ChatGPT Plugins</a> and choose <strong>+ → Add custom MCP server</strong>. If <strong>New Plugin</strong> shows archive upload, click <strong>Create MCP App</strong> to open the MCP configuration form.</li><li>Set the name to <strong>Meridian</strong>. Paste the Server URL below. Choose <strong>OAuth</strong> and enter the Client ID and Client Secret in the manual OAuth fields.</li><li>Start the connection. On the <strong>Connect Meridian</strong> authorization page, paste the private pairing code and click <strong>Allow read-only access</strong>.</li><li>Finish adding/installing the personal plugin. Select it in an ordinary ChatGPT conversation and ask: <em>Use Meridian to list my C++ Works, then read my authoring rules.</em></li></ol>
<label>Server URL</label><input id="endpoint" value="$publicOrigin/mcp" readonly><button onclick="copy('endpoint')">Copy URL</button>
<label>Client ID</label><input id="client" value="$clientIdHtml" readonly><button onclick="copy('client')">Copy Client ID</button>
<label>Client Secret</label><input id="secret" value="$clientSecretHtml" type="password" readonly><button onclick="copy('secret')">Copy Client Secret</button>
<label>Private pairing code</label><input id="pairing" value="$pairingHtml" type="password" readonly><button onclick="copy('pairing')">Copy pairing code</button>
<p><small>Read-only scope: saved Work names and IDs in $($ProjectIds.Count) explicitly shared projects, plus accepted authoring rules. No node contents, attachments, commands, or editing. This is a temporary connection proof. Keep Meridian and both helpers running. Tokens expire after one hour; reconnect afterward. Restarting the helper revokes tokens; restarting the tunnel changes the URL.</small></p>
<script>async function copy(id){const e=document.getElementById(id);try{await navigator.clipboard.writeText(e.value)}catch{const t=document.createElement('textarea');t.value=e.value;document.body.append(t);t.select();document.execCommand('copy');t.remove()}}</script></html>
"@
    $setup | Set-Content -LiteralPath $setupPath -Encoding utf8NoBOM
    $receipt = [ordered]@{
        startedAt = [DateTime]::UtcNow.ToString('o'); mode = 'read-only-connection-probe'
        endpoint = "$publicOrigin/mcp"; companionPid = $companion.Id; tunnelPid = $tunnel.Id
        companionStartedAt = $companion.StartTime.ToUniversalTime().ToString('o'); tunnelStartedAt = $tunnel.StartTime.ToUniversalTime().ToString('o')
        nodePath = $NodePath; serverPath = $serverPath; configPath = $configPath; tunnelPath = $cloudflaredPath
        cloudflaredSha256 = $expectedHash; sharedProjectCount = $ProjectIds.Count; setupPath = $setupPath
        ordinaryChatGptToolCallVerified = $false
    }
    $receipt | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $runPath -Encoding utf8NoBOM
    [pscustomobject]@{ endpoint = $receipt.endpoint; companionPid = $receipt.companionPid; tunnelPid = $receipt.tunnelPid; setupPath = $setupPath; stateDirectory = $stateRoot; sharedProjectCount = $ProjectIds.Count } | ConvertTo-Json
} catch {
    if ($companion -and -not $companion.HasExited) { Stop-Process -Id $companion.Id -ErrorAction SilentlyContinue }
    if ($tunnel -and -not $tunnel.HasExited) { Stop-Process -Id $tunnel.Id -ErrorAction SilentlyContinue }
    throw
}
