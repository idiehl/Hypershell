import assert from 'node:assert/strict';
import { createHash, randomBytes } from 'node:crypto';
import { test } from 'node:test';
import { chromium } from '@playwright/test';
import { createAuth, CHATGPT_REDIRECT_URI } from '../src/auth.mjs';
import { createCompanion } from '../src/server.mjs';

// Uses the repository's existing Playwright dev dependency. Every browser URL
// is intercepted and forwarded to an ephemeral loopback fixture or fulfilled.
// Offline mode also blocks redirect-following network requests, which Chromium
// can make without re-running Playwright's route handler. The callback request
// is observed, never sent to ChatGPT. No public tunnel or live backend is used.
for (const originalPolicy of [true, false]) {
  test(originalPolicy ? 'Chromium reproduces original no-referrer approval rejection' : 'Chromium submits approval with exact Origin and allows registered ChatGPT callback navigation', async t => {
    const publicOrigin = 'https://meridian-browser.example';
    const credentials = {
      publicOrigin, clientId: 'browser-fixture-client',
      clientSecret: 'browser-fixture-secret-that-is-long-enough', pairingCode: 'browser-fixture-pairing-code',
    };
    const auth = createAuth(credentials);
    const companion = createCompanion({ publicOrigin, port: 0, meridianOrigin: 'http://127.0.0.1:9', projectIds: ['fixture-project'] }, {
      auth, fetchImpl: async () => { throw new Error('Browser approval test must never access a backend.'); },
    });
    await companion.listen();
    t.after(async () => { companion.server.closeAllConnections(); await new Promise(resolve => companion.server.close(resolve)); });
    const localOrigin = `http://127.0.0.1:${companion.server.address().port}`;
    const browser = await chromium.launch({ headless: true });
    t.after(() => browser.close());
    const context = await browser.newContext();
    await context.setOffline(true);
    const page = await context.newPage();
    let postedOrigin;
    let postedReferrer;
    let callback;
    page.on('request', request => {
      const url = new URL(request.url());
      if (url.origin + url.pathname === CHATGPT_REDIRECT_URI) callback = url;
    });
    let cspBlocked = false;
    page.on('console', message => { if (message.type() === 'error' && /Content Security Policy|form-action/.test(message.text())) cspBlocked = true; });
    await page.route('**/*', async route => {
      const request = route.request();
      const url = new URL(request.url());
      if (url.origin === publicOrigin) {
        const headers = await request.allHeaders();
        headers.host = new URL(publicOrigin).host;
        if (request.method() === 'POST') { postedOrigin = headers.origin; postedReferrer = headers.referer; }
        const response = await fetch(`${localOrigin}${url.pathname}${url.search}`, {
          method: request.method(), headers, body: request.postDataBuffer() ?? undefined,
          redirect: 'manual', signal: AbortSignal.timeout(5000),
        });
        const responseHeaders = Object.fromEntries(response.headers);
        if (originalPolicy && request.method() === 'GET' && url.pathname === '/authorize') responseHeaders['referrer-policy'] = 'no-referrer';
        delete responseHeaders['transfer-encoding']; delete responseHeaders['content-length'];
        await route.fulfill({ status: response.status, headers: responseHeaders, body: Buffer.from(await response.arrayBuffer()) });
      } else if (url.origin + url.pathname === CHATGPT_REDIRECT_URI) {
        callback = url;
        await route.fulfill({ status: 200, contentType: 'text/html', body: '<h1>Registered callback reached</h1>' });
      } else if (url.origin === 'https://chatgpt.com' && url.pathname === '/favicon.ico') {
        await route.fulfill({ status: 204, body: '' });
      } else {
        await route.abort();
        throw new Error(`Unexpected fixture browser request blocked: ${url.origin}${url.pathname}`);
      }
    });
    const verifier = randomBytes(32).toString('base64url');
    const authorize = new URL('/authorize', publicOrigin);
    authorize.search = new URLSearchParams({
      client_id: credentials.clientId, redirect_uri: CHATGPT_REDIRECT_URI,
      response_type: 'code', resource: `${publicOrigin}/mcp`, scope: 'meridian.read',
      code_challenge_method: 'S256', code_challenge: createHash('sha256').update(verifier).digest('base64url'), state: 'browser-fixture-state',
    }).toString();
    await page.goto(authorize.href);
    await page.getByLabel('Private pairing code').fill(credentials.pairingCode);
    const callbackNavigation = !originalPolicy ? page.waitForRequest(request => {
      const url = new URL(request.url());
      return url.origin + url.pathname === CHATGPT_REDIRECT_URI;
    }, { timeout: 5000 }) : undefined;
    await page.getByRole('button', { name: 'Allow read-only access' }).click({ noWaitAfter: true });
    if (originalPolicy) {
      await page.waitForFunction(() => document.body.textContent.includes('Origin is not allowed.'), { timeout: 5000 });
      assert.equal(postedOrigin, 'null');
      assert.equal(callback, undefined);
    } else {
      await callbackNavigation.catch(() => {
        assert.fail(cspBlocked ? 'Approval CSP blocked the registered OAuth callback.' : 'Browser did not reach the registered OAuth callback.');
      });
      assert.equal(postedOrigin, publicOrigin, 'Browser must preserve the genuine same-origin POST header.');
      assert.equal(postedReferrer, `${publicOrigin}/`, 'Referrer must exclude authorization query parameters.');
      assert.equal(cspBlocked, false);
      assert.equal(callback.searchParams.get('state'), 'browser-fixture-state');
      assert.equal(callback.searchParams.get('iss'), publicOrigin);
      assert.ok(callback.searchParams.get('code'));
      const exchanged = await fetch(`${localOrigin}/token`, {
        method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({
          client_id: credentials.clientId, client_secret: credentials.clientSecret,
          grant_type: 'authorization_code', code: callback.searchParams.get('code'),
          redirect_uri: CHATGPT_REDIRECT_URI, resource: `${publicOrigin}/mcp`, code_verifier: verifier,
        }),
      });
      assert.equal(exchanged.status, 200);
      const token = await exchanged.json();
      assert.equal(auth.verifyBearer({ headers: { authorization: `Bearer ${token.access_token}` } }).ok, true);
    }
  });
}
