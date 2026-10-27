#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'stable-common.ps1')
$root = Get-StableStateRoot
$inputPath = Join-Path $root 'ngrok-setup.json'
if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf) -or (Get-Item -LiteralPath $inputPath).Length -gt 8192 -or ((Get-Item -LiteralPath $inputPath).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'The private ngrok setup file is missing or invalid.' }
try { $inputValues = Get-Content -LiteralPath $inputPath -Raw | ConvertFrom-Json -AsHashtable }
catch { throw 'The private ngrok setup file must contain a domain and authtoken as valid JSON. Its contents were not logged.' }
if (($inputValues.Keys | Sort-Object) -join ',' -ne 'authtoken,domain') { throw 'The private setup file must contain only domain and authtoken.' }
$domain = [string]$inputValues.domain
if ($domain -notmatch '^https://') { $domain = 'https://' + $domain }
try { $origin = Assert-StableOrigin $domain } catch { throw 'Copy the assigned HTTPS Dev Domain from the ngrok Domains page.' }
$hostname = ([Uri]$origin).Host
if ($hostname -notmatch '^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.(ngrok-free\.(app|dev)|ngrok\.(app|dev))$') { throw 'Use the ngrok-managed Dev Domain assigned to this account, without a path.' }
$token = [string]$inputValues.authtoken
if ($token -notmatch '^[a-zA-Z0-9._-]{20,512}$' -or $token -match '^PASTE_') { throw 'The private setup file still needs a valid ngrok authtoken. Never paste it into a conversation.' }
$binary = Join-Path $root 'bin/ngrok.exe'
$sha256 = '082f0336d62544eb8a8e48fb5d48ea9b4028f90a4e20d22e71f16c542875baca'
Assert-PinnedTunnelBinary $binary $sha256
$signature = Get-AuthenticodeSignature -LiteralPath $binary
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O="?ngrok, Inc\."?') { throw 'The ngrok executable publisher could not be verified.' }
$yamlPath = Join-Path $root 'ngrok.yml'
$quotedToken = ConvertTo-Json -InputObject $token -Compress
$yaml = "version: 3`nagent:`n  authtoken: $quotedToken`n  web_addr: false`n  console_ui: false`n  remote_management: false`n  update_check: false`n"
$temporary = "$yamlPath.new"
Set-Content -LiteralPath $temporary -Value $yaml -Encoding utf8NoBOM
Move-Item -LiteralPath $temporary -Destination $yamlPath -Force
$check = Start-Process -FilePath $binary -ArgumentList @('config','check','--config',('"' + $yamlPath + '"')) -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $root 'ngrok-config-check.stdout.log') -RedirectStandardError (Join-Path $root 'ngrok-config-check.stderr.log')
if ($check.ExitCode -ne 0) { throw 'The private ngrok configuration failed validation. Its contents were not logged to this conversation.' }
& (Join-Path $PSScriptRoot 'configure-stable.ps1') -PublicOrigin $origin -Provider ngrok -TunnelExecutable $binary -TunnelSha256 $sha256 -TunnelConfigPath $yamlPath
$config = Get-StableConfig
function Safe-Text([string]$Value) { [Net.WebUtility]::HtmlEncode($Value) }
$client = Safe-Text $config.auth.clientId
$secret = Safe-Text $config.auth.clientSecret
$pairing = Safe-Text $config.auth.pairingCode
$endpoint = Safe-Text "$origin/mcp"
$html = @"
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Meridian stable ChatGPT setup</title><style>body{font:16px system-ui;line-height:1.6;max-width:760px;margin:48px auto;padding:0 24px;background:#0e1924;color:#e0edf1}label{display:block;margin-top:18px;font-weight:600}input{box-sizing:border-box;width:100%;font:14px ui-monospace,monospace;background:#172b39;color:#e0edf1;border:1px solid #3c616e;padding:12px}a{color:#83d9cd}button{padding:8px 14px;margin-top:6px}small{color:#a6c5cd}</style>
<h1>Meridian stable connection</h1><p>This is a private local file. Never upload it, include its secret fields in screenshots, or paste credentials into a conversation.</p>
<ol><li>Open <a href="https://chatgpt.com/plugins" target="_blank" rel="noreferrer">ChatGPT Plugins</a> and create or update the Meridian custom MCP connection.</li><li>Enter this Server URL, select OAuth, and supply the Client ID and Client Secret in the manual settings.</li><li>Approve the Meridian authorization form with the private pairing code. Authoring needs additional consent; an existing read-only connection is not silently upgraded.</li><li>Install or refresh the plugin tools, then ask Meridian to read the current authoring rules and create a draft.</li></ol>
<label>Server URL</label><input id="endpoint" value="$endpoint" readonly><button onclick="copy('endpoint')">Copy URL</button>
<label>Client ID</label><input id="client" value="$client" readonly><button onclick="copy('client')">Copy Client ID</button>
<label>Client Secret</label><input id="secret" value="$secret" type="password" readonly><button onclick="copy('secret')">Copy Client Secret</button>
<label>Private pairing code</label><input id="pairing" value="$pairing" type="password" readonly><button onclick="copy('pairing')">Copy pairing code</button>
<p><small>The stable profile keeps client credentials and durable grants across ordinary helper restarts. Keep this computer online and Meridian open. The supervisor restarts its own helpers; it does not start Meridian or keep a sleeping computer online.</small></p>
<script>async function copy(id){const e=document.getElementById(id);try{await navigator.clipboard.writeText(e.value)}catch{const t=document.createElement('textarea');t.value=e.value;document.body.append(t);t.select();document.execCommand('copy');t.remove()}}</script></html>
"@
Set-Content -LiteralPath (Join-Path $root 'ChatGPT-Setup.html') -Value $html -Encoding utf8NoBOM
Write-Output 'Private ngrok configuration and stable authoring profile are ready. No tunnel was started; the existing connection is unchanged.'
