import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { randomBytes } from 'node:crypto';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { auth as sdkAuthorize } from '@modelcontextprotocol/sdk/client/auth.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';
import { CHATGPT_REDIRECT_URI, READ_SCOPE } from '../src/auth.mjs';
import { TOOL_NAMES, ALL_TOOL_NAMES } from '../src/meridian-tools.mjs';

// Deliberate operator smoke test of the configured endpoint. Never call a model,
// follow the ChatGPT callback, save a token, or print private configuration.
async function verify(configPath, receiptPath) {
  const config = JSON.parse(await readFile(configPath, 'utf8'));
  const origin = config.publicOrigin;
  assert.ok(new URL(origin).protocol === 'https:' && new URL(origin).origin === origin, 'Expected the configured private HTTPS origin.');
  const endpoint = `${origin}/mcp`;
  const checks = [];
  const checkedFetch = (input, init = {}) => {
    const url = new URL(input);
    assert.ok(url.origin === origin, 'The smoke test may contact only the configured MCP origin.');
    return fetch(url, { ...init, signal: AbortSignal.timeout(15_000), redirect: 'manual' });
  };
  const unauthenticated = await checkedFetch(endpoint);
  assert.ok(unauthenticated.status === 401, 'Anonymous MCP access must fail.');
  assert.ok(unauthenticated.headers.get('www-authenticate')?.includes(`${origin}/.well-known/oauth-protected-resource/mcp`), 'Expected protected-resource discovery challenge.');
  checks.push('anonymous-mcp-rejected');
  for (const path of ['/', '/api/library/catalog', '/config.json', '/ChatGPT-Setup.html']) {
    const response = await checkedFetch(`${origin}${path}`);
    assert.ok(response.status === 404, 'Raw editor APIs and private setup paths must not be served.');
  }
  checks.push('editor-and-private-files-not-exposed');
  const resource = await (await checkedFetch(`${origin}/.well-known/oauth-protected-resource/mcp`)).json();
  assert.ok(resource.resource === endpoint && resource.authorization_servers?.[0] === origin, 'Resource discovery must match the configured issuer.');
  const forgedForwarding = await checkedFetch(`${origin}/.well-known/oauth-authorization-server`, { headers: { 'X-Forwarded-Host': 'untrusted.invalid', 'X-Forwarded-Proto': 'http' } });
  const discovery = await forgedForwarding.json();
  assert.ok(discovery.issuer === origin && discovery.authorization_endpoint === `${origin}/authorize`, 'Forwarded headers must not change the OAuth issuer.');
  checks.push('exact-issuer-and-resource');

  let savedTokens;
  let verifier;
  let authorizationUrl;
  let discoveryState;
  const state = randomBytes(24).toString('base64url');
  const provider = {
    redirectUrl: CHATGPT_REDIRECT_URI,
    clientMetadata: {
      client_name: 'Meridian operator smoke test', redirect_uris: [CHATGPT_REDIRECT_URI],
      response_types: ['code'], grant_types: ['authorization_code'], token_endpoint_auth_method: 'client_secret_basic', scope: READ_SCOPE,
    },
    state: () => state,
    clientInformation: () => ({ client_id: config.auth.clientId, client_secret: config.auth.clientSecret, issuer: origin }),
    tokens: () => savedTokens, saveTokens: value => { savedTokens = value; },
    redirectToAuthorization: value => { authorizationUrl = value; },
    saveCodeVerifier: value => { verifier = value; }, codeVerifier: () => verifier,
    discoveryState: () => discoveryState, saveDiscoveryState: value => { discoveryState = value; },
  };
  const initiated = await sdkAuthorize(provider, {
    serverUrl: endpoint, fetchFn: checkedFetch, scope: READ_SCOPE, resourceMetadataUrl: new URL(`${origin}/.well-known/oauth-protected-resource/mcp`),
  });
  assert.ok(initiated === 'REDIRECT' && authorizationUrl.origin === origin, 'OAuth discovery must initiate a same-origin authorization flow.');
  const page = await checkedFetch(authorizationUrl);
  assert.ok(page.status === 200, 'Expected owner approval form.');
  assert.ok(page.headers.get('referrer-policy') === 'strict-origin', 'Browser approval must preserve a genuine same-origin POST without leaking query parameters.');
  assert.ok(page.headers.get('content-security-policy')?.includes(`form-action 'self' ${CHATGPT_REDIRECT_URI}`), 'Browser approval must permit the exact registered callback navigation.');
  const html = await page.text();
  assert.ok(!html.includes(config.auth.pairingCode) && !html.includes(config.auth.clientSecret), 'Approval page must not expose private values.');
  const transaction = /name="transaction" value="([A-Za-z0-9_-]+)"/.exec(html)?.[1];
  assert.ok(transaction, 'Approval form must carry its browser-bound transaction.');
  const approved = await checkedFetch(`${origin}/authorize`, {
    method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', Origin: origin, Cookie: page.headers.get('set-cookie').split(';')[0] },
    body: new URLSearchParams({ transaction, pairing_code: config.auth.pairingCode, decision: 'allow' }),
  });
  assert.ok(approved.status === 302, 'Owner approval must return the registered callback.');
  const callback = new URL(approved.headers.get('location'));
  assert.ok(callback.origin + callback.pathname === CHATGPT_REDIRECT_URI && callback.searchParams.get('state') === state && callback.searchParams.get('iss') === origin, 'OAuth callback bindings must match.');
  // Read its code only; never send a synthetic callback into the real ChatGPT account.
  const authorized = await sdkAuthorize(provider, { serverUrl: endpoint, authorizationCode: callback.searchParams.get('code'), fetchFn: checkedFetch, scope: READ_SCOPE });
  assert.ok(authorized === 'AUTHORIZED' && savedTokens?.scope === READ_SCOPE, 'PKCE code exchange must issue the read-only grant.');
  checks.push('oauth-pkce-through-public-https');

  const client = new Client({ name: 'meridian-operator-smoke', version: '0.1.0' });
  try {
    await client.connect(new StreamableHTTPClientTransport(new URL(endpoint), {
      fetch: checkedFetch, requestInit: { headers: { Authorization: `Bearer ${savedTokens.access_token}` } },
    }));
    const tools = await client.listTools();
    const expectedTools = config.authoringEnabled ? ALL_TOOL_NAMES : TOOL_NAMES;
    assert.ok(JSON.stringify(tools.tools.map(value => value.name).sort()) === JSON.stringify([...expectedTools].sort()), 'Only the configured toolset may be advertised.');
    assert.ok(tools.tools.filter(value => TOOL_NAMES.includes(value.name)).every(value => value.annotations?.readOnlyHint && !value.annotations.destructiveHint), 'Discovery tool annotations must advertise read-only behavior.');
    const capabilities = await client.callTool({ name: 'meridian_capabilities', arguments: {} });
    assert.ok(!capabilities.isError && capabilities.structuredContent.backend.status === 'ready' && capabilities.structuredContent.authoringAvailable === !!config.authoringEnabled, 'Expected the live local Meridian configured capability.');
    const listed = await client.callTool({ name: 'meridian_list_works', arguments: { query: 'C++', limit: 50 } });
    assert.ok(!listed.isError && listed.structuredContent.works.length > 0, 'Expected scoped C++ Work discovery.');
    assert.ok(listed.structuredContent.works.every(value => config.projectIds.includes(value.projectId) && Object.keys(value).sort().join(',') === 'name,projectId,workId'), 'Work results must contain scoped names and IDs only.');
    const rules = await client.callTool({ name: 'meridian_authoring_rules', arguments: {} });
    assert.ok(!rules.isError && /^sha256:[a-f0-9]{64}$/.test(rules.structuredContent.version), 'Expected the current versioned rulebook.');
    assert.ok(rules.structuredContent.rules.some(value => value.id === 'SCALE-005'), 'The accepted Minimum Effective Variance principle must be available.');
    checks.push('official-sdk-all-three-tools-through-public-https');
    const receipt = {
      verifiedAt: new Date().toISOString(), endpoint, checks, toolNames: TOOL_NAMES,
      cppWorkCount: listed.structuredContent.works.length, acceptedOrInheritedRuleCount: rules.structuredContent.rules.length,
      ruleVersion: rules.structuredContent.version, operatorSmokeTest: true, ordinaryChatGptToolCallVerified: false,
      note: 'Authenticated operator SDK test; no actual ChatGPT conversation invoked a tool. No token or private credentials saved in this receipt.',
    };
    if (receiptPath) await writeFile(receiptPath, JSON.stringify(receipt, null, 2) + '\n');
    process.stdout.write(JSON.stringify(receipt, null, 2) + '\n');
  } finally { await client.close(); }
}
if (process.argv.length < 3 || process.argv.length > 4) {
  process.stderr.write('Usage: node scripts/verify-connection.mjs <private-config.json> [sanitized-receipt.json]\n');
  process.exitCode = 1;
} else {
  verify(process.argv[2], process.argv[3]).catch(error => {
    // Avoid dumping request objects, private config, SDK traces or callback URLs.
    process.stderr.write(`Connection verification failed (${error.name}). ${error.name === 'AssertionError' ? error.message : 'Check the local service and public discovery endpoint.'}\n`);
    process.exitCode = 1;
  });
}
