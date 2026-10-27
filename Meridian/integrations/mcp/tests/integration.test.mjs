import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { test } from 'node:test';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { auth as sdkAuthorize } from '@modelcontextprotocol/sdk/client/auth.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';
import { createAuth, CHATGPT_REDIRECT_URI, READ_SCOPE } from '../src/auth.mjs';
import { createCompanion } from '../src/server.mjs';
import { TOOL_NAMES } from '../src/meridian-tools.mjs';

const RULES = `MERIDIAN WORK RULEBOOK
PURPOSE-001 [INHERITED]
Compose a graph of the subject's relationships.

NODE-005 [ACCEPTED — user direction]
Show the object itself.

NODE-999 [PROPOSED]
This proposal must not be returned.

Your additions or revisions:
`;

test('OAuth SDK discovery, browser approval, PKCE exchange and all three MCP tools work together; expiry and revocation block access', async t => {
  // Every network request below is rewritten to an ephemeral loopback fixture.
  // The fake public origin exercises Host checks, metadata and resource binding.
  const publicOrigin = 'https://meridian-integration.example';
  const authConfig = {
    publicOrigin, clientId: 'integration-fixture-client',
    clientSecret: 'integration-fixture-secret-that-is-long-enough',
    pairingCode: 'integration-private-pairing-code',
  };
  let time = 1_000_000;
  const backendRequests = [];
  const publicRequests = [];
  const backend = createServer((req, res) => {
    backendRequests.push({ method: req.method, path: req.url });
    res.setHeader('Content-Type', 'application/json');
    if (req.method !== 'GET') { res.writeHead(405).end('{}'); return; }
    if (req.url === '/__meridian_status') {
      res.end(JSON.stringify({ application: 'atlas-meridian', status: 'ready', capabilities: ['library-v1'] }));
    } else if (req.url === '/api/library/catalog') {
      res.end(JSON.stringify([
        { id: 'approved-project', title: 'Fixture project', privateContent: 'never expose private node text', canvases: [
          { id: 'cpp-work', title: 'C++ Classes' }, { id: 'csharp-work', title: 'C# Namespaces' },
        ] },
        { id: 'unshared-project', canvases: [{ id: 'unshared-work', title: 'Never expose this Work' }] },
      ]));
    } else res.writeHead(404).end('{}');
  });
  await new Promise(resolve => backend.listen(0, '127.0.0.1', resolve));
  t.after(async () => { backend.closeAllConnections(); await new Promise(resolve => backend.close(resolve)); });
  const backendOrigin = `http://127.0.0.1:${backend.address().port}`;
  const provider = createAuth({ ...authConfig, now: () => time, tokenTtlMs: 1000 });
  const companion = createCompanion({ publicOrigin, port: 0, meridianOrigin: backendOrigin, projectIds: ['approved-project'] }, {
    auth: provider, readRules: async () => RULES,
  });
  await companion.listen();
  t.after(async () => { companion.server.closeAllConnections(); await new Promise(resolve => companion.server.close(resolve)); });
  const companionOrigin = `http://127.0.0.1:${companion.server.address().port}`;
  async function localFetch(input, init = {}) {
    assert.ok(typeof input === 'string' || input instanceof URL, 'SDK requests must use a URL; no arbitrary Request passthrough.');
    const url = new URL(input);
    assert.equal(url.origin, publicOrigin, 'A test must never make an external network request.');
    const headers = new Headers(init.headers);
    headers.set('Host', new URL(publicOrigin).host);
    publicRequests.push({ method: init.method ?? 'GET', path: url.pathname });
    return fetch(`${companionOrigin}${url.pathname}${url.search}`, { ...init, headers, redirect: 'manual' });
  }

  // Confirm the real companion gates SDK initialization before any backend read.
  const unauthorizedClient = new Client({ name: 'integration-unauthorized', version: '1.0.0' });
  const unauthorizedTransport = new StreamableHTTPClientTransport(new URL(`${publicOrigin}/mcp`), { fetch: localFetch });
  await assert.rejects(unauthorizedClient.connect(unauthorizedTransport), /unauthorized/i);
  await unauthorizedClient.close();
  assert.deepEqual(backendRequests, []);

  // The official SDK handles discovery, client authentication and PKCE. Only the
  // browser interaction is simulated; no live ChatGPT session is claimed here.
  let savedTokens;
  let savedVerifier;
  let authorizationUrl;
  let discovery;
  const oauthClient = {
    redirectUrl: CHATGPT_REDIRECT_URI,
    clientMetadata: {
      client_name: 'Meridian isolated integration fixture',
      redirect_uris: [CHATGPT_REDIRECT_URI], response_types: ['code'],
      grant_types: ['authorization_code'], token_endpoint_auth_method: 'client_secret_basic', scope: READ_SCOPE,
    },
    state: () => 'integration-browser-state',
    clientInformation: () => ({ client_id: authConfig.clientId, client_secret: authConfig.clientSecret, issuer: publicOrigin }),
    tokens: () => savedTokens,
    saveTokens: tokens => { savedTokens = tokens; },
    redirectToAuthorization: url => { authorizationUrl = url; },
    saveCodeVerifier: value => { savedVerifier = value; },
    codeVerifier: () => savedVerifier,
    saveDiscoveryState: value => { discovery = value; },
    discoveryState: () => discovery,
    invalidateCredentials: scope => {
      if (scope === 'tokens' || scope === 'all') savedTokens = undefined;
      if (scope === 'verifier' || scope === 'all') savedVerifier = undefined;
      if (scope === 'discovery' || scope === 'all') discovery = undefined;
    },
  };
  async function link() {
    savedTokens = undefined;
    authorizationUrl = undefined;
    const started = await sdkAuthorize(oauthClient, {
      serverUrl: `${publicOrigin}/mcp`, fetchFn: localFetch,
      scope: READ_SCOPE,
      resourceMetadataUrl: new URL(`${publicOrigin}/.well-known/oauth-protected-resource/mcp`),
    });
    assert.equal(started, 'REDIRECT');
    assert.equal(authorizationUrl.origin, publicOrigin);
    assert.equal(authorizationUrl.searchParams.get('code_challenge_method'), 'S256');
    assert.equal(authorizationUrl.searchParams.get('resource'), `${publicOrigin}/mcp`);
    const page = await localFetch(authorizationUrl);
    assert.equal(page.status, 200);
    const html = await page.text();
    const transaction = /name="transaction" value="([A-Za-z0-9_-]+)"/.exec(html)?.[1];
    assert.ok(transaction);
    assert.ok(!html.includes(authConfig.pairingCode));
    assert.ok(!html.includes(authConfig.clientSecret));
    const approved = await localFetch(`${publicOrigin}/authorize`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded', Origin: publicOrigin,
        Cookie: page.headers.get('set-cookie').split(';')[0],
      },
      body: new URLSearchParams({ transaction, pairing_code: authConfig.pairingCode, decision: 'allow' }),
    });
    assert.equal(approved.status, 302);
    const callback = new URL(approved.headers.get('location'));
    assert.equal(callback.origin + callback.pathname, CHATGPT_REDIRECT_URI);
    assert.equal(callback.searchParams.get('iss'), publicOrigin);
    assert.equal(callback.searchParams.get('state'), 'integration-browser-state');
    const exchanged = await sdkAuthorize(oauthClient, {
      serverUrl: `${publicOrigin}/mcp`, authorizationCode: callback.searchParams.get('code'), fetchFn: localFetch, scope: READ_SCOPE,
    });
    assert.equal(exchanged, 'AUTHORIZED');
    assert.equal(savedTokens.scope, READ_SCOPE);
    assert.match(savedTokens.refresh_token, /^[A-Za-z0-9_-]{43}$/);
    assert.match(savedTokens.access_token, /^[A-Za-z0-9_-]{43}$/);
    return savedTokens.access_token;
  }

  const token = await link();
  assert.deepEqual(backendRequests, []);
  const client = new Client({ name: 'integration-authorized-sdk', version: '1.0.0' });
  t.after(() => client.close());
  const transport = new StreamableHTTPClientTransport(new URL(`${publicOrigin}/mcp`), {
    fetch: localFetch, requestInit: { headers: { Authorization: `Bearer ${token}` } },
  });
  await client.connect(transport);
  assert.equal(transport.sessionId, undefined);
  assert.equal(client.getServerVersion().name, 'atlas-meridian');
  const listed = await client.listTools();
  assert.deepEqual(listed.tools.map(tool => tool.name).sort(), [...TOOL_NAMES].sort());
  assert.ok(listed.tools.every(tool => tool.annotations.readOnlyHint && !tool.annotations.destructiveHint));

  const capabilities = await client.callTool({ name: 'meridian_capabilities', arguments: {} });
  assert.notEqual(capabilities.isError, true);
  assert.equal(capabilities.structuredContent.authoringAvailable, false);
  assert.equal(capabilities.structuredContent.backend.status, 'ready');
  const works = await client.callTool({ name: 'meridian_list_works', arguments: { query: 'C++' } });
  assert.notEqual(works.isError, true);
  assert.deepEqual(works.structuredContent.works, [{ projectId: 'approved-project', workId: 'cpp-work', name: 'C++ Classes' }]);
  assert.ok(!JSON.stringify(works).includes('unshared'));
  assert.ok(!JSON.stringify(works).includes('privateContent'));
  const rules = await client.callTool({ name: 'meridian_authoring_rules', arguments: {} });
  assert.notEqual(rules.isError, true);
  assert.deepEqual(rules.structuredContent.rules.map(rule => rule.id), ['PURPOSE-001', 'NODE-005']);
  assert.ok(!JSON.stringify(rules).includes('proposal must not'));
  assert.match(rules.structuredContent.version, /^sha256:[a-f0-9]{64}$/);
  assert.deepEqual(backendRequests, [
    { method: 'GET', path: '/__meridian_status' },
    { method: 'GET', path: '/api/library/catalog' },
  ]);
  assert.ok(publicRequests.some(request => request.path === '/.well-known/oauth-authorization-server'));
  assert.ok(!publicRequests.some(request => request.path === '/register'));

  // Token expiry rejects the existing SDK connection before any upstream access.
  time += 1001;
  const beforeExpiredCall = backendRequests.length;
  await assert.rejects(client.callTool({ name: 'meridian_list_works', arguments: {} }), /unauthorized/i);
  assert.equal(backendRequests.length, beforeExpiredCall);

  // Relink gives a new grant; revocation blocks an otherwise valid new token.
  const relinkedToken = await link();
  const relinkedClient = new Client({ name: 'integration-relinked-sdk', version: '1.0.0' });
  t.after(() => relinkedClient.close());
  await relinkedClient.connect(new StreamableHTTPClientTransport(new URL(`${publicOrigin}/mcp`), {
    fetch: localFetch, requestInit: { headers: { Authorization: `Bearer ${relinkedToken}` } },
  }));
  assert.equal((await relinkedClient.listTools()).tools.length, 3);
  provider.revokeAll();
  const beforeRevokedCall = backendRequests.length;
  await assert.rejects(relinkedClient.callTool({ name: 'meridian_capabilities', arguments: {} }), /unauthorized/i);
  assert.equal(backendRequests.length, beforeRevokedCall);
});
