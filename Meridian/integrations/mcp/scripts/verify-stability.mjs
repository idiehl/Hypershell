import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { auth as sdkAuthorize } from '@modelcontextprotocol/sdk/client/auth.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';
import { AUTHOR_SCOPE, CHATGPT_REDIRECT_URI } from '../src/auth.mjs';

// Deliberate operator acceptance test. It authorizes a separate scoped SDK
// connection, reads only, and restarts exactly the Stable supervisor's helpers.
// Credentials, callback codes, PKCE verifiers and tokens never leave memory.
const stableRoot = join(process.env.LOCALAPPDATA ?? '', 'AtlasMeridian', 'McpCompanion', 'Stable');
const configPath = join(stableRoot, 'config.json');
const runPath = join(stableRoot, 'run.json');
const commonScript = fileURLToPath(new URL('./stable-common.ps1', import.meta.url));
const projectRoot = fileURLToPath(new URL('../../../', import.meta.url));
const receiptPath = resolve(process.argv[2] ?? join(projectRoot, 'output', 'mcp-authoring-20261023', 'stability-receipt.json'));
const pwsh = join(process.env.USERPROFILE ?? '', '.cache', 'codex-runtimes', 'codex-primary-runtime', 'dependencies', 'native', 'powershell', 'pwsh.exe');
const legacyIds = [306840, 350920];
const secrets = new Set();
const receipt = {
  schemaVersion: 1, startedAt: new Date().toISOString(), operatorSdkTest: true,
  ordinaryChatGptToolCallVerified: false, liveWorkWrites: false, passes: false,
  checks: [], helpers: {},
};
let phase = 'initializing';
function progress(value) { process.stdout.write(JSON.stringify({ phase: value, at: new Date().toISOString() }) + '\n'); }
function passed(name) { receipt.checks.push({ name, passed: true, at: new Date().toISOString() }); progress(name); }
const quote = value => `'${String(value).replaceAll("'", "''")}'`;
const delay = milliseconds => new Promise(resolveDelay => setTimeout(resolveDelay, milliseconds));
async function readRun() { return JSON.parse(await readFile(runPath, 'utf8')); }
function powershell(script) {
  return JSON.parse(execFileSync(pwsh, ['-NoProfile', '-NonInteractive', '-Command', script], {
    encoding: 'utf8', timeout: 20_000, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
  }).trim());
}
function legacySnapshot() {
  return powershell(`$ErrorActionPreference = 'Stop'; $items = @(foreach ($itemId in @(306840,350920)) { $p = Get-Process -Id $itemId -ErrorAction SilentlyContinue; if ($p) { [pscustomobject]@{ id = $p.Id; path = $p.Path; startedAt = $p.StartTime.ToUniversalTime().ToString('o') } } }); ConvertTo-Json -InputObject $items -Compress`);
}
function stopOwned(role, expectedIdentity) {
  assert.ok(['companion', 'tunnel'].includes(role), 'Only a Stable helper can be restarted.');
  assert.ok(Number.isInteger(expectedIdentity?.id) && !legacyIds.includes(expectedIdentity.id), 'Never stop a legacy helper.');
  return powershell(`
    $ErrorActionPreference = 'Stop'
    . ${quote(commonScript)}
    $root = Get-StableStateRoot
    $config = Get-StableConfig
    $run = Get-Content -LiteralPath (Join-Path $root 'run.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($run.provider -ne 'ngrok' -or $config.launcher.provider -ne 'ngrok') { throw 'Expected the configured Stable ngrok profile.' }
    $identity = $run[${quote(role)}]
    $expectedStart = [DateTimeOffset]${quote(expectedIdentity.startedAt)}
    if (-not $identity -or [int]$identity.id -ne ${expectedIdentity.id} -or $identity.path -ne ${quote(expectedIdentity.path)} -or ([DateTimeOffset]$identity.startedAt).UtcDateTime.Ticks -ne $expectedStart.UtcDateTime.Ticks) { throw 'The helper identity changed; no process was stopped.' }
    if ([int]$identity.id -in @(306840,350920)) { throw 'Legacy helpers are excluded.' }
    $supervisor = Get-OwnedProcess $run.supervisor
    if (-not $supervisor) { throw 'The Stable supervisor is not running.' }
    $expectedPath = ${role === 'companion' ? '$config.launcher.nodePath' : '$config.launcher.tunnelPath'}
    if ([IO.Path]::GetFullPath($identity.path) -ne [IO.Path]::GetFullPath($expectedPath)) { throw 'The helper executable is not the Stable configured executable.' }
    ${role === 'tunnel' ? 'Assert-PinnedTunnelBinary $config.launcher.tunnelPath $config.launcher.tunnelSha256' : ''}
    $owned = Get-OwnedProcess $identity
    if (-not $owned) { throw 'The Stable helper is no longer running.' }
    Stop-Process -Id $owned.Id -Force
    [pscustomobject]@{ stoppedId = $identity.id; role = ${quote(role)}; identityVerified = $true } | ConvertTo-Json -Compress
  `);
}

async function verify() {
  assert.ok(process.env.LOCALAPPDATA && process.env.USERPROFILE && process.platform === 'win32', 'This operator test requires the configured Windows Stable profile.');
  const config = JSON.parse(await readFile(configPath, 'utf8'));
  const origin = config.publicOrigin;
  const endpoint = `${origin}/mcp`;
  assert.ok(new URL(origin).protocol === 'https:' && new URL(origin).origin === origin && !new URL(origin).hostname.endsWith('.trycloudflare.com'), 'A stable HTTPS origin is required.');
  assert.ok(config.authoringEnabled && config.auth?.grantStorePath && config.launcher?.provider === 'ngrok', 'The durable authoring Stable profile is required.');
  for (const secret of [config.auth.clientId, config.auth.clientSecret, config.auth.pairingCode]) secrets.add(secret);
  receipt.endpoint = endpoint;
  const before = await readRun();
  assert.ok(before.endpoint === endpoint && before.companion?.id && before.tunnel?.id && before.supervisor?.id, 'Expected the running Stable supervisor and helpers.');
  const legacyBefore = legacySnapshot();
  assert.ok(legacyBefore.length === 2, 'Both excluded legacy helpers must be present before this acceptance test.');
  receipt.legacyProcesses = { before: legacyBefore };
  const checkedFetch = async (input, init = {}) => {
    const url = new URL(input);
    assert.ok(url.origin === origin, 'The operator test may contact only its configured Stable MCP origin.');
    const headers = new Headers(init.headers);
    // Native HTTPS API requests must not receive the provider's browser warning.
    // No ngrok bypass header is used and no provider credentials are transmitted.
    if (!headers.has('Accept')) headers.set('Accept', 'application/json');
    return fetch(url, { ...init, headers, redirect: 'manual', signal: AbortSignal.timeout(15_000) });
  };
  async function ready() {
    const response = await checkedFetch(`${origin}/.well-known/oauth-authorization-server`);
    if (response.status !== 200) return false;
    const metadata = await response.json();
    return metadata.issuer === origin && metadata.grant_types_supported?.includes('refresh_token');
  }
  async function waitForRestart(role, oldIdentity) {
    const deadline = Date.now() + 120_000;
    let lastProgress = Date.now();
    while (Date.now() < deadline) {
      try {
        const run = await readRun();
        assert.ok(run.endpoint === endpoint, 'The Stable hostname changed unexpectedly.');
        assert.ok(run.supervisor?.id === before.supervisor.id && run.supervisor.startedAt === before.supervisor.startedAt, 'The supervising process changed unexpectedly.');
        if (run[role]?.id && run[role].id !== oldIdentity.id && await ready()) return run[role];
      } catch (error) {
        if (error.name === 'AssertionError') throw error;
      }
      if (Date.now() - lastProgress >= 15_000) { progress(`waiting-for-${role}-recovery`); lastProgress = Date.now(); }
      await delay(1000);
    }
    throw new Error('Stable helper recovery timed out.');
  }

  phase = 'author-oauth'; progress(phase);
  assert.ok(await ready(), 'The configured Stable discovery endpoint is unavailable.');
  let tokens, verifier, authorizationUrl, discovery;
  const state = randomBytes(24).toString('base64url');
  secrets.add(state);
  const provider = {
    redirectUrl: CHATGPT_REDIRECT_URI,
    clientMetadata: {
      client_name: 'Meridian authorized stability operator', redirect_uris: [CHATGPT_REDIRECT_URI],
      response_types: ['code'], grant_types: ['authorization_code', 'refresh_token'], token_endpoint_auth_method: 'client_secret_basic', scope: AUTHOR_SCOPE,
    },
    state: () => state,
    clientInformation: () => ({ client_id: config.auth.clientId, client_secret: config.auth.clientSecret, issuer: origin }),
    tokens: () => tokens,
    saveTokens: value => { tokens = value; secrets.add(value.access_token); if (value.refresh_token) secrets.add(value.refresh_token); },
    redirectToAuthorization: value => { authorizationUrl = value; },
    saveCodeVerifier: value => { verifier = value; secrets.add(value); }, codeVerifier: () => verifier,
    discoveryState: () => discovery, saveDiscoveryState: value => { discovery = value; },
  };
  assert.ok(await sdkAuthorize(provider, {
    serverUrl: endpoint, fetchFn: checkedFetch, scope: AUTHOR_SCOPE,
    resourceMetadataUrl: new URL(`${origin}/.well-known/oauth-protected-resource/mcp`),
  }) === 'REDIRECT', 'Expected SDK authoring authorization.');
  const page = await checkedFetch(authorizationUrl);
  assert.ok(page.status === 200, 'Expected the private pairing form.');
  const html = await page.text();
  assert.ok(html.includes('Allow authoring access'), 'The approval must explicitly request authoring.');
  const transaction = /name="transaction" value="([A-Za-z0-9_-]+)"/.exec(html)?.[1];
  assert.ok(transaction, 'Expected a browser-bound authorization transaction.');
  secrets.add(transaction);
  const approval = await checkedFetch(`${origin}/authorize`, {
    method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', Origin: origin, Cookie: page.headers.get('set-cookie').split(';')[0] },
    body: new URLSearchParams({ transaction, pairing_code: config.auth.pairingCode, decision: 'allow' }),
  });
  assert.ok(approval.status === 302, 'Expected an issuer-bound authorization callback.');
  const callback = new URL(approval.headers.get('location'));
  assert.ok(callback.origin + callback.pathname === CHATGPT_REDIRECT_URI && callback.searchParams.get('state') === state && callback.searchParams.get('iss') === origin, 'OAuth callback bindings must match.');
  const code = callback.searchParams.get('code'); secrets.add(code);
  // Never navigate to, fetch, or invoke the synthetic ChatGPT callback.
  assert.ok(await sdkAuthorize(provider, { serverUrl: endpoint, authorizationCode: code, fetchFn: checkedFetch, scope: AUTHOR_SCOPE }) === 'AUTHORIZED', 'Expected an SDK PKCE code exchange.');
  assert.ok(tokens.scope === AUTHOR_SCOPE && tokens.refresh_token, 'Expected a fresh author grant with durable refresh.');
  passed('fresh-author-consent-and-sdk-pkce-over-stable-https');

  async function withClient(accessToken, action) {
    const client = new Client({ name: 'meridian-stability-operator', version: '0.2.0' });
    try {
      await client.connect(new StreamableHTTPClientTransport(new URL(endpoint), {
        fetch: checkedFetch, requestInit: { headers: { Authorization: `Bearer ${accessToken}` } },
      }));
      return await action(client);
    } finally { await client.close(); }
  }
  async function readOnlyProbe(accessToken, initial = false) {
    await withClient(accessToken, async client => {
      const listed = await client.listTools();
      assert.ok(listed.tools.length >= 13, 'The full authoring toolset must be advertised.');
      const schema = await client.callTool({ name: 'meridian_authoring_schema', arguments: {} });
      assert.ok(!schema.isError && schema.structuredContent, 'Authoring schema must be accessible with author consent.');
      if (initial) {
        const drafts = await client.callTool({ name: 'meridian_drafts', arguments: { projectId: config.projectIds[0], limit: 1 } });
        assert.ok(!drafts.isError && drafts.structuredContent, 'Existing draft metadata must be readable.');
        const inspect = await client.callTool({ name: 'meridian_inspect', arguments: { projectId: config.projectIds[0], entity: 'works', limit: 1 } });
        assert.ok(!inspect.isError && inspect.structuredContent, 'Saved Work metadata must be readable.');
      }
    });
  }
  async function rotate() {
    const priorRefresh = tokens.refresh_token;
    const response = await checkedFetch(`${origin}/token`, {
      method: 'POST', headers: {
        'Content-Type': 'application/x-www-form-urlencoded',
        Authorization: `Basic ${Buffer.from(`${encodeURIComponent(config.auth.clientId)}:${encodeURIComponent(config.auth.clientSecret)}`).toString('base64')}`,
      }, body: new URLSearchParams({ grant_type: 'refresh_token', refresh_token: priorRefresh, resource: endpoint }),
    });
    assert.ok(response.status === 200, 'Refresh rotation must remain available.');
    const next = await response.json();
    assert.ok(next.scope === AUTHOR_SCOPE && next.access_token && next.refresh_token && next.refresh_token !== priorRefresh, 'Refresh must rotate without changing consent.');
    tokens = next; secrets.add(next.access_token); secrets.add(next.refresh_token);
    await readOnlyProbe(next.access_token);
  }
  await readOnlyProbe(tokens.access_token, true);
  passed('schema-drafts-and-saved-work-metadata-read-only');
  await rotate();
  passed('refresh-rotates-before-companion-restart');

  phase = 'companion-recovery'; progress(phase);
  const companionBefore = (await readRun()).companion;
  const persistedAccessToken = tokens.access_token;
  assert.ok(stopOwned('companion', companionBefore).identityVerified, 'Companion process identity must be verified before termination.');
  const companionAfter = await waitForRestart('companion', companionBefore);
  receipt.helpers.companion = { before: companionBefore, after: companionAfter };
  await readOnlyProbe(persistedAccessToken);
  passed('supervisor-replaces-companion-and-existing-access-token-survives');
  await rotate();
  passed('refresh-and-new-access-token-work-after-companion-restart');

  phase = 'tunnel-recovery'; progress(phase);
  const tunnelBefore = (await readRun()).tunnel;
  const tunnelAccessToken = tokens.access_token;
  assert.ok(stopOwned('tunnel', tunnelBefore).identityVerified, 'Tunnel process identity must be verified before termination.');
  const tunnelAfter = await waitForRestart('tunnel', tunnelBefore);
  receipt.helpers.tunnel = { before: tunnelBefore, after: tunnelAfter };
  await readOnlyProbe(tunnelAccessToken);
  passed('supervisor-replaces-tunnel-with-same-hostname-and-valid-auth');
  await rotate();
  passed('refresh-and-new-access-token-work-after-tunnel-restart');

  const after = await readRun();
  assert.ok(after.endpoint === endpoint && after.supervisor.id === before.supervisor.id, 'The original supervisor and hostname must remain stable.');
  const legacyAfter = legacySnapshot();
  assert.ok(JSON.stringify(legacyAfter) === JSON.stringify(legacyBefore), 'The excluded legacy processes must remain identical.');
  receipt.legacyProcesses.after = legacyAfter;
  receipt.supervisor = after.supervisor;
  receipt.restartCount = after.restartCount;
  passed('legacy-quick-tunnel-and-companion-untouched');
  receipt.passes = true;
  receipt.completedAt = new Date().toISOString();
}

try { await verify(); }
catch (error) {
  receipt.failure = { phase, name: error.name, message: error.name === 'AssertionError' ? error.message : 'Stability verification failed; inspect the private local service logs.' };
  receipt.completedAt = new Date().toISOString();
  process.exitCode = 1;
}
const sanitized = JSON.stringify(receipt, null, 2) + '\n';
assert.ok([...secrets].filter(value => typeof value === 'string' && value.length > 8).every(value => !sanitized.includes(value)), 'A secret must never be written into the sanitized receipt.');
await mkdir(dirname(receiptPath), { recursive: true });
await writeFile(receiptPath, sanitized);
process.stdout.write(sanitized);
