import assert from 'node:assert/strict';
import { createServer, request } from 'node:http';
import { once } from 'node:events';
import { createHash } from 'node:crypto';
import { mkdtemp, readFile, unlink, rmdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';
import { createMeridianTools, acceptedRules, TOOL_NAMES, validateMeridianOrigin } from '../src/meridian-tools.mjs';
import { createCompanion, validatePublicOrigin } from '../src/server.mjs';
import { createAuth, CHATGPT_REDIRECT_URI } from '../src/auth.mjs';

const RULES = `MERIDIAN WORK RULEBOOK\nPURPOSE-001 [INHERITED]\nGraphs express relationships.\n\nPURPOSE-002 [PROPOSED]\nUnaccepted suggestion.\n\nNODE-005 [ACCEPTED — user direction]\nShow the object itself.\n\nYour additions or revisions:\n\n2. EMPTY SECTION\n`;
const PROJECT_ID = 'project-one';

async function fixture(t, { catalog, health, headers = {}, redirect = false } = {}) {
  const requests = [];
  const backend = createServer((req, res) => {
    requests.push({ method: req.method, path: req.url });
    if (redirect) { res.writeHead(302, { location: 'http://example.invalid/private' }); return res.end(); }
    res.writeHead(200, { 'content-type': 'application/json', ...headers });
    if (req.url === '/__meridian_status') res.end(JSON.stringify(health ?? { application: 'atlas-meridian', status: 'ready', capabilities: ['library-v1'] }));
    else if (req.url === '/api/library/catalog') res.end(JSON.stringify(catalog ?? [
      { id: PROJECT_ID, title: 'Local project', canvases: [{ id: 'work-one', title: 'C++ Classes' }, { id: 'work-two', title: 'C# Namespaces' }], privateNodeContent: 'never return this' },
      { id: 'not-shared', canvases: [{ id: 'secret-work', title: 'Not shared' }] },
    ]));
    else { res.end('{}'); }
  });
  backend.listen(0, '127.0.0.1');
  await once(backend, 'listening');
  t.after(() => new Promise(resolve => backend.close(resolve)));
  return { origin: `http://127.0.0.1:${backend.address().port}`, requests };
}

test('backend origin accepts numeric loopback only, rejecting URL/file selection and credential paths', () => {
  assert.equal(validateMeridianOrigin('http://127.0.0.1:5240'), 'http://127.0.0.1:5240');
  assert.equal(validateMeridianOrigin('http://[::1]:5240/'), 'http://[::1]:5240');
  for (const value of ['https://example.com', 'http://localhost:5240', 'file:///tmp/project', 'http://127.0.0.1:5240/api/projects', 'http://user:password@127.0.0.1:5240', 'http://127.0.0.1:5240/?x=1']) {
    assert.throws(() => validateMeridianOrigin(value));
  }
});

test('explicit shared project scope is mandatory', () => {
  for (const projectIds of [undefined, [], ['../secret'], ['*'], ['http://127.0.0.1']]) {
    assert.throws(() => createMeridianTools({ projectIds }));
  }
});

test('accepted rule projection excludes proposals and section templates', () => {
  const rules = acceptedRules(RULES);
  assert.deepEqual(rules.map(rule => rule.id), ['PURPOSE-001', 'NODE-005']);
  assert.equal(rules[1].text, 'Show the object itself.');
  assert(!JSON.stringify(rules).includes('Unaccepted'));
});

test('Work list projects only names and IDs, filters scope, and paginates', async t => {
  const f = await fixture(t);
  const tools = createMeridianTools({ meridianOrigin: f.origin, projectIds: [PROJECT_ID] });
  const first = await tools.listWorks({ limit: 1 });
  assert.deepEqual(first, { works: [{ projectId: PROJECT_ID, workId: 'work-two', name: 'C# Namespaces' }], nextOffset: 1 });
  const second = await tools.listWorks({ offset: 1, limit: 1 });
  assert.equal(second.works[0].name, 'C++ Classes');
  assert.equal(second.nextOffset, null);
  const filtered = await tools.listWorks({ query: 'C++' });
  assert.deepEqual(filtered.works.map(work => work.workId), ['work-one']);
  assert(!JSON.stringify(first).includes('not-shared'));
  assert(!JSON.stringify(first).includes('privateNodeContent'));
  assert(f.requests.every(request => request.method === 'GET' && request.path === '/api/library/catalog'));
  await assert.rejects(tools.listWorks({ limit: 1000 }));
});

test('health data is constrained and current rules carry a stable digest', async t => {
  const f = await fixture(t);
  const tools = createMeridianTools({ meridianOrigin: f.origin, projectIds: [PROJECT_ID] }, { readRules: async () => RULES });
  const caps = await tools.capabilities();
  assert.deepEqual(caps.tools, TOOL_NAMES);
  assert.equal(caps.authoringAvailable, false);
  const rules = await tools.authoringRules();
  assert.match(rules.version, /^sha256:[a-f0-9]{64}$/);
  assert.equal(rules.rules.length, 2);
  assert.equal(rules.version, (await tools.authoringRules()).version);
});

test('catalogue reads have a bounded cold-load deadline while health stays short and aborted reads are not retried', async () => {
  const deadlines = [], requests = [];
  const tools = createMeridianTools({ projectIds: [PROJECT_ID] }, {
    timeoutSignal: milliseconds => { deadlines.push(milliseconds); return new AbortController().signal; },
    fetchImpl: async (url, options) => {
      requests.push({ path: new URL(url).pathname, method: options.method, redirect: options.redirect });
      assert(options.signal instanceof AbortSignal);
      return new Response(JSON.stringify(url.endsWith('/__meridian_status')
        ? { application: 'atlas-meridian', status: 'ready' }
        : [{ id: PROJECT_ID, canvases: [{ id: 'one', title: 'One' }] }]));
    },
  });
  await tools.capabilities();
  assert.equal((await tools.listWorks()).works.length, 1);
  assert.deepEqual(deadlines, [5_000, 20_000]);
  assert.deepEqual(requests, [
    { path: '/__meridian_status', method: 'GET', redirect: 'error' },
    { path: '/api/library/catalog', method: 'GET', redirect: 'error' },
  ]);
  let abortedRequests = 0;
  const aborted = createMeridianTools({ projectIds: [PROJECT_ID] }, {
    timeoutSignal: milliseconds => { assert.equal(milliseconds, 20_000); return AbortSignal.abort(); },
    fetchImpl: async (_url, { signal }) => { abortedRequests++; signal.throwIfAborted(); },
  });
  await assert.rejects(aborted.listWorks(), /Meridian is not reachable/);
  assert.equal(abortedRequests, 1);
});

test('backend redirects and non-Meridian services are rejected', async t => {
  const redirect = await fixture(t, { redirect: true });
  const redirectTools = createMeridianTools({ meridianOrigin: redirect.origin, projectIds: [PROJECT_ID] });
  await assert.rejects(redirectTools.capabilities());
  const wrong = await fixture(t, { health: { application: 'other' } });
  const wrongTools = createMeridianTools({ meridianOrigin: wrong.origin, projectIds: [PROJECT_ID] });
  await assert.rejects(wrongTools.capabilities(), /not Meridian/);
});

test('oversized backend responses are bounded', async t => {
  const f = await fixture(t, { headers: { 'content-length': String(3 * 1024 * 1024) } });
  const tools = createMeridianTools({ meridianOrigin: f.origin, projectIds: [PROJECT_ID] });
  await assert.rejects(tools.listWorks(), /exceeds/);
});

test('public origins reject non-HTTPS remote and path URLs', () => {
  assert.equal(validatePublicOrigin('https://private.example'), 'https://private.example');
  assert.equal(validatePublicOrigin('http://127.0.0.1:5241'), 'http://127.0.0.1:5241');
  for (const value of ['http://private.example', 'https://private.example/mcp', 'https://u:p@private.example', 'https://private.example?secret=1']) assert.throws(() => validatePublicOrigin(value));
});

async function companionFixture(t, auditConfig = {}) {
  const f = await fixture(t);
  const auth = {
    handle: async () => false,
    requireBearer: (req, res) => {
      if (req.headers.authorization === 'Bearer fixture-token') return true;
      res.writeHead(401, { 'www-authenticate': 'Bearer resource_metadata="https://private.example/.well-known/oauth-protected-resource/mcp"' });
      res.end(); return false;
    },
  };
  const companion = createCompanion({ publicOrigin: 'https://private.example', port: 0, meridianOrigin: f.origin, projectIds: [PROJECT_ID], auditPath: auditConfig.path }, { auth, readRules: async () => RULES, onAudit: auditConfig.callback });
  await companion.listen();
  t.after(() => new Promise(resolve => companion.server.close(resolve)));
  return { ...f, companion, origin: `http://127.0.0.1:${companion.server.address().port}` };
}

test('native HTTP companion requires auth, checks hosts/origins, JSON and size', async t => {
  const f = await companionFixture(t);
  const endpoint = `${f.origin}/mcp`;
  const unauth = await fetch(endpoint, { method: 'POST' });
  assert.equal(unauth.status, 401);
  const headers = { authorization: 'Bearer fixture-token' };
  assert.equal((await fetch(endpoint, { headers })).status, 405);
  assert.equal((await fetch(endpoint, { method: 'POST', headers })).status, 415);
  assert.equal((await fetch(endpoint, { method: 'POST', headers: { ...headers, origin: 'https://evil.example' } })).status, 403);
  const invalidHostStatus = await new Promise((resolve, reject) => {
    const req = request(endpoint, { method: 'POST', headers: { ...headers, host: 'evil.example' } }, res => { res.resume(); resolve(res.statusCode); });
    req.once('error', reject); req.end();
  });
  assert.equal(invalidHostStatus, 403);
  assert.equal((await fetch(endpoint, { method: 'POST', headers: { ...headers, 'content-type': 'application/json' }, body: 'bad json' })).status, 400);
  assert.equal((await fetch(endpoint, { method: 'POST', headers: { ...headers, 'content-type': 'application/json' }, body: 'x'.repeat(40000) })).status, 413);
  assert.deepEqual(f.requests, []);
});

test('official SDK client initializes, discovers only three read-only tools, and calls them over stateless JSON HTTP', async t => {
  const f = await companionFixture(t);
  const client = new Client({ name: 'meridian-fixture-client', version: '1.0.0' });
  const transport = new StreamableHTTPClientTransport(new URL(`${f.origin}/mcp`), { requestInit: { headers: { authorization: 'Bearer fixture-token' } } });
  t.after(() => client.close());
  await client.connect(transport);
  assert.equal(transport.sessionId, undefined);
  const list = await client.listTools();
  assert.deepEqual(list.tools.map(tool => tool.name).sort(), [...TOOL_NAMES].sort());
  assert(list.tools.every(tool => tool.annotations.readOnlyHint && !tool.annotations.destructiveHint));
  assert(list.tools.every(tool => tool._meta.securitySchemes[0].type === 'oauth2' && tool._meta.securitySchemes[0].scopes[0] === 'meridian.read'));
  const works = await client.callTool({ name: 'meridian_list_works', arguments: { query: 'C++' } });
  assert.equal(works.isError, undefined);
  assert.equal(works.structuredContent.works[0].name, 'C++ Classes');
  const health = await client.callTool({ name: 'meridian_capabilities', arguments: {} });
  assert.equal(health.structuredContent.authoringAvailable, false);
  const rules = await client.callTool({ name: 'meridian_authoring_rules', arguments: {} });
  assert.equal(rules.structuredContent.rules.length, 2);
  const invalid = await client.callTool({ name: 'meridian_list_works', arguments: { limit: 10000 } });
  assert.equal(invalid.isError, true);
  assert(f.requests.every(request => request.method === 'GET'));
});

test('optional protocol audit contains only timestamps, operation names and outcomes, never arguments or returned data', async t => {
  const folder = await mkdtemp(join(tmpdir(), 'meridian-mcp-audit-'));
  const path = join(folder, 'audit.jsonl');
  t.after(async () => { await unlink(path).catch(() => {}); await rmdir(folder); });
  const records = [];
  const f = await companionFixture(t, { path, callback: record => records.push(record) });
  const client = new Client({ name: 'private-client-name-not-for-audit', version: '1.0.0' });
  t.after(() => client.close());
  await client.connect(new StreamableHTTPClientTransport(new URL(`${f.origin}/mcp`), { requestInit: { headers: { authorization: 'Bearer fixture-token' } } }));
  await client.listTools();
  await client.callTool({ name: 'meridian_list_works', arguments: { query: 'private-argument-value' } });
  await client.callTool({ name: 'meridian_authoring_rules', arguments: {} });
  const text = await readFile(path, 'utf8');
  const lines = text.trim().split('\n').map(line => JSON.parse(line));
  assert.deepEqual(lines.map(line => line.phase), ['mcp.initialize', 'tools.list', 'tools.call', 'tools.call']);
  assert.deepEqual(lines, records);
  for (const record of lines) {
    assert.deepEqual(Object.keys(record).sort(), (record.tool ? ['phase', 'success', 'timestamp', 'tool'] : ['phase', 'success', 'timestamp']).sort());
    assert(record.success);
  }
  for (const privateValue of ['fixture-token', 'private-argument-value', 'private-client-name-not-for-audit', 'Graphs express', 'C++ Classes', PROJECT_ID, '127.0.0.1']) assert(!text.includes(privateValue));
});

test('real OAuth provider and companion complete approval, PKCE, discovery, and an authenticated SDK call together', async t => {
  const f = await fixture(t);
  const publicOrigin = 'https://meridian-private.example';
  const authConfig = { publicOrigin, clientId: 'fixture-client-identity', clientSecret: 'fixture-private-secret-at-least-32-characters', pairingCode: 'fixture-owner-pairing-code' };
  const auth = createAuth(authConfig);
  const companion = createCompanion({ publicOrigin, port: 0, meridianOrigin: f.origin, projectIds: [PROJECT_ID] }, { auth, readRules: async () => RULES });
  await companion.listen();
  t.after(() => new Promise(resolve => companion.server.close(resolve)));
  const origin = `http://127.0.0.1:${companion.server.address().port}`;
  const metadata = await (await fetch(`${origin}/.well-known/oauth-protected-resource/mcp`)).json();
  assert.equal(metadata.resource, `${publicOrigin}/mcp`);
  const verifier = 'fixture-verifier-abcdefghijklmnopqrstuvwxyz0123456789';
  const query = new URLSearchParams({
    client_id: authConfig.clientId, redirect_uri: CHATGPT_REDIRECT_URI, response_type: 'code', resource: `${publicOrigin}/mcp`,
    scope: 'meridian.read', state: 'fixture-browser-state', code_challenge_method: 'S256', code_challenge: createHash('sha256').update(verifier).digest('base64url'),
  });
  const started = await fetch(`${origin}/authorize?${query}`);
  assert.equal(started.status, 200);
  const html = await started.text();
  const transaction = /name="transaction" value="([A-Za-z0-9_-]+)"/.exec(html)[1];
  const cookie = started.headers.get('set-cookie').split(';')[0];
  const approval = await fetch(`${origin}/authorize`, {
    method: 'POST', redirect: 'manual', headers: { 'content-type': 'application/x-www-form-urlencoded', origin: publicOrigin, cookie },
    body: new URLSearchParams({ transaction, pairing_code: authConfig.pairingCode, decision: 'allow' }),
  });
  assert.equal(approval.status, 302);
  const callback = new URL(approval.headers.get('location'));
  const exchange = await fetch(`${origin}/token`, {
    method: 'POST', headers: { 'content-type': 'application/x-www-form-urlencoded' }, body: new URLSearchParams({
      client_id: authConfig.clientId, client_secret: authConfig.clientSecret, grant_type: 'authorization_code',
      code: callback.searchParams.get('code'), code_verifier: verifier, redirect_uri: CHATGPT_REDIRECT_URI, resource: `${publicOrigin}/mcp`,
    }),
  });
  assert.equal(exchange.status, 200);
  const token = await exchange.json();
  const client = new Client({ name: 'oauth-integrated-fixture', version: '1.0.0' });
  t.after(() => client.close());
  await client.connect(new StreamableHTTPClientTransport(new URL(`${origin}/mcp`), { requestInit: { headers: { authorization: `Bearer ${token.access_token}` } } }));
  const works = await client.callTool({ name: 'meridian_list_works', arguments: {} });
  assert.equal(works.structuredContent.works.length, 2);
  assert.equal(works.isError, undefined);
  auth.revokeAll();
  await assert.rejects(client.callTool({ name: 'meridian_list_works', arguments: {} }));
});
