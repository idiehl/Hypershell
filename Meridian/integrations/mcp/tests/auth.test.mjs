import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createServer } from 'node:http';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { test } from 'node:test';
import { createAuth, CHATGPT_REDIRECT_URI, READ_SCOPE, AUTHOR_SCOPE } from '../src/auth.mjs';

const publicOrigin = 'https://meridian-private.example';
const config = {
  publicOrigin, clientId: 'meridian-test-client',
  clientSecret: 'test-secret-value-that-is-at-least-32-characters',
  pairingCode: 'private-test-pairing-code',
};
const verifier = 'abcdefghijklmnopqrstuvwxyz0123456789-._~ABCDEFGHIJKLMNOPQRSTUVWXYZ';
const challenge = createHash('sha256').update(verifier).digest('base64url');
const resource = `${publicOrigin}/mcp`;

async function fixture(t, overrides = {}) {
  const auth = createAuth({ ...config, ...overrides });
  const server = createServer(async (req, res) => {
    if (await auth.handle(req, res)) return;
    if (req.url.startsWith('/mcp')) {
      if (auth.requireBearer(req, res)) { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(auth.verifyBearer(req))); }
      return;
    }
    if (req.url.startsWith('/author')) {
      if (auth.requireScope(req, res, AUTHOR_SCOPE)) res.writeHead(200).end('authorized');
      return;
    }
    res.writeHead(404).end();
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(async () => { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); });
  const local = `http://127.0.0.1:${server.address().port}`;
  const request = (path, options) => fetch(`${local}${path}`, { ...options, redirect: 'manual' });
  const query = overrides => new URLSearchParams({
    client_id: config.clientId, redirect_uri: CHATGPT_REDIRECT_URI, response_type: 'code',
    resource, scope: READ_SCOPE, code_challenge_method: 'S256', code_challenge: challenge,
    state: 'test-state', ...overrides,
  });
  async function start(overrides = {}) {
    const response = await request(`/authorize?${query(overrides)}`);
    const html = await response.text();
    return {
      response, html, transaction: /name="transaction" value="([A-Za-z0-9_-]+)"/.exec(html)?.[1],
      cookie: response.headers.get('set-cookie')?.split(';')[0],
    };
  }
  async function approve(started, overrides = {}, headerOverrides = {}) {
    return request('/authorize', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded', Origin: publicOrigin, Cookie: started.cookie ?? '', ...headerOverrides },
      body: new URLSearchParams({ transaction: started.transaction ?? '', pairing_code: config.pairingCode, decision: 'allow', ...overrides }),
    });
  }
  async function code(overrides = {}) {
    const response = await approve(await start(overrides));
    assert.equal(response.status, 302);
    const callback = new URL(response.headers.get('location'));
    assert.equal(callback.origin + callback.pathname, CHATGPT_REDIRECT_URI);
    assert.equal(callback.searchParams.get('state'), 'test-state');
    assert.equal(callback.searchParams.get('iss'), publicOrigin);
    return callback.searchParams.get('code');
  }
  const tokenForm = (code, overrides = {}) => new URLSearchParams({
    client_id: config.clientId, client_secret: config.clientSecret,
    grant_type: 'authorization_code', code, redirect_uri: CHATGPT_REDIRECT_URI, resource, code_verifier: verifier, ...overrides,
  });
  const exchange = (code, overrides = {}, headerOverrides = {}) => request('/token', {
    method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded', ...headerOverrides }, body: tokenForm(code, overrides),
  });
  return { auth, request, start, approve, code, exchange, tokenForm, query };
}

test('configuration requires HTTPS, strong private values, and exact ChatGPT redirects', () => {
  for (const publicOrigin of ['http://example.test', 'https://example.test/path', 'https://u:p@example.test', 'https://example.test?x=1']) {
    assert.throws(() => createAuth({ ...config, publicOrigin }), /publicOrigin/);
  }
  for (const redirect of ['https://evil.example/callback', `${CHATGPT_REDIRECT_URI}?evil=1`, 'https://chatgpt.com.evil.example/connector_platform_oauth_redirect', 'https://chatgpt.com/connector/oauth/../../evil']) {
    assert.throws(() => createAuth({ ...config, redirectUris: [redirect] }), /callback/);
  }
  assert.throws(() => createAuth({ ...config, clientSecret: 'weak' }), /clientSecret/);
  assert.doesNotThrow(() => createAuth({ ...config, redirectUris: ['https://chatgpt.com/connector/oauth/approved_callback-123'] }));
});

test('discovery advertises static confidential client, S256, issuer binding and read-only resource', async t => {
  const f = await fixture(t);
  for (const path of ['/.well-known/oauth-protected-resource', '/.well-known/oauth-protected-resource/mcp']) {
    const response = await f.request(path);
    const metadata = await response.json();
    assert.equal(metadata.resource, resource);
    assert.deepEqual(metadata.authorization_servers, [publicOrigin]);
    assert.deepEqual(metadata.scopes_supported, [READ_SCOPE, AUTHOR_SCOPE]);
    assert.equal(response.headers.get('cache-control'), 'no-store');
  }
  const metadata = await (await f.request('/.well-known/oauth-authorization-server')).json();
  assert.equal(metadata.issuer, publicOrigin);
  assert.equal(metadata.authorization_response_iss_parameter_supported, true);
  assert.deepEqual(metadata.code_challenge_methods_supported, ['S256']);
  assert.deepEqual(metadata.token_endpoint_auth_methods_supported, ['client_secret_basic', 'client_secret_post']);
  assert.equal(metadata.registration_endpoint, undefined);
  assert.equal(metadata.client_id_metadata_document_supported, undefined);
  assert.equal((await f.request('/register', { method: 'POST' })).status, 405);
});

test('unauthenticated requests receive discovery challenge and query tokens do not authenticate', async t => {
  const f = await fixture(t);
  const response = await f.request('/mcp?access_token=untrusted');
  assert.equal(response.status, 401);
  assert.match(response.headers.get('www-authenticate'), /resource_metadata="https:\/\/meridian-private\.example\/\.well-known\/oauth-protected-resource\/mcp"/);
  assert.match(response.headers.get('www-authenticate'), /scope="meridian.read"/);
});

test('pairing plus PKCE completes, bearer works, approval does not expose secrets', async t => {
  const f = await fixture(t);
  const started = await f.start();
  assert.equal(started.response.status, 200);
  assert.ok(started.transaction);
  assert.ok(!started.html.includes(config.pairingCode));
  assert.ok(!started.html.includes(config.clientSecret));
  assert.match(started.response.headers.get('set-cookie'), /Secure; HttpOnly; SameSite=Strict/);
  assert.match(started.response.headers.get('content-security-policy'), /frame-ancestors 'none'/);
  assert.match(started.response.headers.get('content-security-policy'), /form-action 'self' https:\/\/chatgpt\.com\/connector_platform_oauth_redirect;/);
  assert.equal(started.response.headers.get('referrer-policy'), 'strict-origin');
  const approved = await f.approve(started);
  assert.equal(approved.headers.get('referrer-policy'), 'no-referrer');
  const callback = new URL(approved.headers.get('location'));
  assert.equal(callback.searchParams.get('iss'), publicOrigin);
  const response = await f.exchange(callback.searchParams.get('code'));
  assert.equal(response.status, 200);
  const token = await response.json();
  assert.match(token.access_token, /^[A-Za-z0-9_-]{43}$/);
  assert.equal(token.token_type, 'Bearer');
  assert.equal(token.scope, READ_SCOPE);
  assert.match(token.refresh_token, /^[A-Za-z0-9_-]{43}$/);
  const bearer = await f.request('/mcp', { headers: { Authorization: `Bearer ${token.access_token}` } });
  assert.equal(bearer.status, 200);
  assert.deepEqual((await bearer.json()).subject, 'local-owner');
  f.auth.revokeAll();
  assert.equal((await f.request('/mcp', { headers: { Authorization: `Bearer ${token.access_token}` } })).status, 401);
});

test('approval form CSP permits only self and the exact validated callback for this transaction', async t => {
  function formSources(response) {
    const policy = response.headers.get('content-security-policy');
    const directive = policy.split(';').map(value => value.trim()).find(value => value.startsWith('form-action '));
    return directive.split(/\s+/).slice(1);
  }
  const stable = await fixture(t);
  const started = await stable.start();
  assert.deepEqual(formSources(started.response), ["'self'", CHATGPT_REDIRECT_URI]);
  assert.ok(!formSources(started.response).some(source => source === 'https:' || source === 'https://chatgpt.com' || source.includes('*')));

  const callback = 'https://chatgpt.com/connector/oauth/exact-fixture-callback';
  const specific = await fixture(t, { redirectUris: [callback] });
  const callbackStarted = await specific.start({ redirect_uri: callback });
  assert.equal(callbackStarted.response.status, 200);
  assert.deepEqual(formSources(callbackStarted.response), ["'self'", callback]);
  assert.ok(!formSources(callbackStarted.response).includes(CHATGPT_REDIRECT_URI));
  const wrong = await specific.start({ redirect_uri: 'https://chatgpt.com/connector/oauth/unregistered' });
  assert.equal(wrong.response.status, 400);
  assert.equal(wrong.response.headers.get('location'), null);
});

test('client_secret_basic works without a posted secret', async t => {
  const f = await fixture(t);
  const code = await f.code();
  const form = f.tokenForm(code);
  form.delete('client_secret');
  const response = await f.request('/token', {
    method: 'POST', headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      Authorization: `Basic ${Buffer.from(`${config.clientId}:${config.clientSecret}`).toString('base64')}`,
    }, body: form,
  });
  assert.equal(response.status, 200);
});

test('malformed, unregistered, broad-scope, and non-S256 requests never redirect', async t => {
  const f = await fixture(t);
  for (const patch of [
    { client_id: 'attacker-client' }, { redirect_uri: 'https://evil.example/callback' },
    { response_type: 'token' }, { resource: `${publicOrigin}/other` },
    { code_challenge_method: 'plain' }, { code_challenge: 'bad' },
    { scope: 'meridian.write' }, { scope: 'meridian.read meridian.write' }, { state: '' },
  ]) {
    const started = await f.start(patch);
    assert.equal(started.response.status, 400, JSON.stringify(patch));
    assert.equal(started.response.headers.get('location'), null);
  }
  const duplicate = await f.request(`/authorize?${f.query()}&resource=${encodeURIComponent(resource)}`);
  assert.equal(duplicate.status, 400);
});

test('approval requires same-origin form, browser cookie, correct pairing code, and is single-use', async t => {
  const f = await fixture(t);
  for (const headers of [{ Origin: 'https://evil.example' }, { Origin: 'null' }, { Origin: '' }, { Cookie: '' }]) {
    const response = await f.approve(await f.start(), {}, headers);
    assert.equal(response.status, 403);
    assert.equal(response.headers.get('location'), null);
  }
  const started = await f.start();
  assert.equal((await f.approve(started, { pairing_code: 'wrong' })).status, 403);
  assert.equal((await f.approve(started)).status, 403);
  const denied = await f.approve(await f.start(), { decision: 'deny', pairing_code: '' });
  assert.equal(denied.status, 302);
  const callback = new URL(denied.headers.get('location'));
  assert.equal(callback.searchParams.get('error'), 'access_denied');
  assert.equal(callback.searchParams.get('iss'), publicOrigin);
  assert.equal(callback.searchParams.get('code'), null);
});

test('token exchange binds exact resource, redirect and verifier; failed grant is consumed', async t => {
  const f = await fixture(t);
  for (const patch of [
    { resource: 'https://elsewhere.example/mcp' }, { resource: '' },
    { redirect_uri: 'https://evil.example/callback' }, { code_verifier: 'x'.repeat(43) },
    { code_verifier: '' },
  ]) {
    const code = await f.code();
    const bad = await f.exchange(code, patch);
    assert.equal(bad.status, 400);
    assert.equal((await bad.json()).error, 'invalid_grant');
    assert.equal((await f.exchange(code)).status, 400);
  }
});

test('wrong client credentials do not consume the legitimate grant; code reuse revokes its issued token', async t => {
  const f = await fixture(t);
  const code = await f.code();
  assert.equal((await f.exchange(code, { client_secret: 'wrong-secret' })).status, 401);
  const token = await (await f.exchange(code)).json();
  assert.equal((await f.request('/mcp', { headers: { Authorization: `Bearer ${token.access_token}` } })).status, 200);
  assert.equal((await f.exchange(code)).status, 400);
  assert.equal((await f.request('/mcp', { headers: { Authorization: `Bearer ${token.access_token}` } })).status, 401);
});

test('approval transactions, authorization codes and access tokens expire', async t => {
  let time = 1000;
  const f = await fixture(t, { now: () => time, authorizationTtlMs: 100, codeTtlMs: 100, tokenTtlMs: 1000 });
  const started = await f.start(); time += 101;
  assert.equal((await f.approve(started)).status, 403);
  const expiredCode = await f.code(); time += 101;
  assert.equal((await f.exchange(expiredCode)).status, 400);
  const code = await f.code();
  const token = await (await f.exchange(code)).json(); time += 1001;
  assert.equal((await f.request('/mcp', { headers: { Authorization: `Bearer ${token.access_token}` } })).status, 401);
});

test('body size, content type, duplicate fields, and repeated approval attempts are bounded', async t => {
  const f = await fixture(t);
  assert.equal((await f.request('/token', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' })).status, 415);
  assert.equal((await f.request('/token', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: `x=${'a'.repeat(9000)}` })).status, 413);
  assert.equal((await f.request('/token', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, body: 'client_id=first&client_id=second' })).status, 400);
  let response;
  for (let i = 0; i < 21; i++) response = await f.approve(await f.start(), { pairing_code: 'wrong' });
  assert.equal(response.status, 429);
});

test('unsupported methods and grants are rejected without credentials in responses', async t => {
  const f = await fixture(t);
  assert.equal((await f.request('/token')).status, 405);
  assert.equal((await f.request('/.well-known/oauth-authorization-server', { method: 'POST' })).status, 405);
  const response = await f.exchange('nonexistent', { grant_type: 'client_credentials' });
  assert.equal(response.status, 400);
  const body = await response.text();
  assert.ok(!body.includes(config.clientSecret));
  assert.ok(!body.includes(config.pairingCode));
  assert.match(body, /unsupported_grant_type/);
});

function privateStorePath(t) {
  const directory = mkdtempSync(join(tmpdir(), 'meridian-grants-'));
  t.after(() => {
    const absolute = resolve(directory);
    assert.ok(absolute.startsWith(`${resolve(tmpdir())}\\meridian-grants-`) || absolute.startsWith(`${resolve(tmpdir())}/meridian-grants-`));
    rmSync(absolute, { recursive: true, force: true });
  });
  return join(directory, 'grants.json');
}
const bearer = accessToken => ({ headers: { Authorization: `Bearer ${accessToken}` } });
const refresh = (f, token, overrides = {}) => f.request('/token', {
  method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
  body: new URLSearchParams({ client_id: config.clientId, client_secret: config.clientSecret, grant_type: 'refresh_token', refresh_token: token, resource, ...overrides }),
});

test('read grants remain read-only across refresh; authoring requires a fresh consent', async t => {
  const f = await fixture(t);
  const original = await (await f.exchange(await f.code())).json();
  const denied = await f.request('/author', bearer(original.access_token));
  assert.equal(denied.status, 403);
  assert.match(denied.headers.get('www-authenticate'), /scope="meridian.author"/);
  assert.equal((await refresh(f, original.refresh_token, { scope: AUTHOR_SCOPE })).status, 400);
  const renewed = await (await refresh(f, original.refresh_token)).json();
  assert.equal(renewed.scope, READ_SCOPE);
  assert.equal((await f.request('/author', bearer(renewed.access_token))).status, 403);
  const authorPage = await f.start({ scope: AUTHOR_SCOPE });
  assert.match(authorPage.html, /Allow authoring access/);
  assert.match(authorPage.html, /create or update Works/);
  const author = await (await f.exchange(await f.code({ scope: AUTHOR_SCOPE }))).json();
  assert.equal(author.scope, AUTHOR_SCOPE);
  assert.equal((await f.request('/author', bearer(author.access_token))).status, 200);
  assert.equal((await f.request('/mcp', bearer(author.access_token))).status, 200);
  assert.equal((await f.request('/author', bearer(original.access_token))).status, 403);
  assert.deepEqual(f.auth.verifyBearer({ headers: { authorization: `Bearer ${author.access_token}` } }).scopes, [AUTHOR_SCOPE]);
});

test('refresh rotation authenticates the same client/resource and cannot restore downscoped authoring', async t => {
  const f = await fixture(t);
  const author = await (await f.exchange(await f.code({ scope: `${READ_SCOPE} ${AUTHOR_SCOPE}` }))).json();
  assert.equal((await refresh(f, author.refresh_token, { client_secret: 'wrong' })).status, 401);
  assert.equal((await refresh(f, author.refresh_token, { resource: `${publicOrigin}/other` })).status, 400);
  const response = await refresh(f, author.refresh_token, { scope: READ_SCOPE });
  assert.equal(response.status, 200);
  const downscoped = await response.json();
  assert.notEqual(downscoped.refresh_token, author.refresh_token);
  assert.equal(downscoped.scope, READ_SCOPE);
  assert.equal((await f.request('/author', bearer(downscoped.access_token))).status, 403);
  const escalated = await refresh(f, downscoped.refresh_token, { scope: AUTHOR_SCOPE });
  assert.equal(escalated.status, 400);
  assert.equal((await escalated.json()).error, 'invalid_scope');
  assert.equal((await refresh(f, downscoped.refresh_token)).status, 200);
});

test('durable hashed grants survive restarts and refresh replay revokes the complete family durably', async t => {
  const grantStorePath = privateStorePath(t);
  const first = await fixture(t, { grantStorePath });
  const initial = await (await first.exchange(await first.code())).json();
  first.auth.close();
  const second = await fixture(t, { grantStorePath });
  assert.equal((await second.request('/mcp', bearer(initial.access_token))).status, 200);
  const rotated = await (await refresh(second, initial.refresh_token)).json();
  assert.equal((await second.request('/mcp', bearer(rotated.access_token))).status, 200);
  const saved = readFileSync(grantStorePath, 'utf8');
  for (const secret of [initial.access_token, initial.refresh_token, rotated.access_token, rotated.refresh_token, config.clientSecret, config.pairingCode]) assert.ok(!saved.includes(secret));
  second.auth.close();
  const third = await fixture(t, { grantStorePath });
  const replay = await refresh(third, initial.refresh_token);
  assert.equal(replay.status, 400);
  assert.equal((await third.request('/mcp', bearer(rotated.access_token))).status, 401);
  assert.equal((await refresh(third, rotated.refresh_token)).status, 400);
  third.auth.close();
  const fourth = await fixture(t, { grantStorePath });
  assert.equal((await fourth.request('/mcp', bearer(rotated.access_token))).status, 401);
  assert.equal((await refresh(fourth, rotated.refresh_token)).status, 400);
});

test('simultaneous refresh use issues once then revokes the family on reuse', async t => {
  const f = await fixture(t);
  const initial = await (await f.exchange(await f.code())).json();
  const results = await Promise.all([refresh(f, initial.refresh_token), refresh(f, initial.refresh_token)]);
  assert.deepEqual(results.map(response => response.status).sort(), [200, 400]);
  const issued = await results.find(response => response.status === 200).json();
  assert.equal((await f.request('/mcp', bearer(issued.access_token))).status, 401);
  assert.equal((await refresh(f, issued.refresh_token)).status, 400);
});

test('refresh inactivity and absolute grant expiry are enforced', async t => {
  let time = 1000;
  const f = await fixture(t, { now: () => time, tokenTtlMs: 1000, refreshTtlMs: 1500, familyTtlMs: 3000 });
  const initial = await (await f.exchange(await f.code())).json();
  time += 1001;
  assert.equal((await f.request('/mcp', bearer(initial.access_token))).status, 401);
  const renewed = await (await refresh(f, initial.refresh_token)).json();
  assert.match(renewed.refresh_token, /^[A-Za-z0-9_-]{43}$/);
  time += 1501;
  assert.equal((await refresh(f, renewed.refresh_token)).status, 400);
  const another = await (await f.exchange(await f.code())).json();
  time += 1000;
  const next = await (await refresh(f, another.refresh_token)).json();
  time += 1000;
  const last = await (await refresh(f, next.refresh_token)).json();
  time += 1001;
  assert.equal((await refresh(f, last.refresh_token)).status, 400);
  assert.equal((await f.request('/mcp', bearer(last.access_token))).status, 401);
});

test('issuer or static credential changes fail closed instead of reusing a persisted grant', async t => {
  const grantStorePath = privateStorePath(t);
  const f = await fixture(t, { grantStorePath });
  await f.exchange(await f.code());
  assert.throws(() => createAuth({ ...config, grantStorePath, publicOrigin: 'https://different.example' }), /binding/);
  assert.throws(() => createAuth({ ...config, grantStorePath, clientSecret: 'different-but-long-enough-private-secret' }), /binding/);
  f.auth.revokeAll();
  f.auth.close();
  assert.doesNotThrow(() => createAuth({ ...config, grantStorePath }));
});

test('a failed durable rotation returns no tokens and disables access; incomplete state fails closed after restart', async t => {
  const grantStorePath = privateStorePath(t);
  const f = await fixture(t, { grantStorePath });
  const initial = await (await f.exchange(await f.code())).json();
  writeFileSync(`${grantStorePath}.pending`, '{}', { mode: 0o600 });
  const response = await refresh(f, initial.refresh_token);
  assert.equal(response.status, 500);
  const error = await response.json();
  assert.equal(error.access_token, undefined);
  assert.equal(error.refresh_token, undefined);
  assert.equal((await f.request('/mcp', bearer(initial.access_token))).status, 401);
  assert.throws(() => createAuth({ ...config, grantStorePath }), /incomplete transaction/);
});
