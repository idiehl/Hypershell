import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';
import { createPrivateGrantStore } from './private-grant-store.mjs';

// Private, single-owner connection proof; not a general-purpose identity provider.
// Contract: https://developers.openai.com/plugins/build/auth and
// https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization
export const READ_SCOPE = 'meridian.read';
export const AUTHOR_SCOPE = 'meridian.author';
export const CHATGPT_REDIRECT_URI = 'https://chatgpt.com/connector_platform_oauth_redirect';
const FORM_LIMIT = 8192;
const MAX_ENTRIES = 128;
const MAX_REFRESH_ENTRIES = 2048;
const digest = value => createHash('sha256').update(value).digest('hex');
const opaque = () => randomBytes(32).toString('base64url');
const equal = (left, right) => typeof left === 'string' && typeof right === 'string'
  && timingSafeEqual(createHash('sha256').update(left).digest(), createHash('sha256').update(right).digest());
const escapeHtml = value => String(value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

function send(res, status, body, type = 'application/json; charset=utf-8', headers = {}) {
  res.writeHead(status, {
    'Content-Type': type, 'Cache-Control': 'no-store', Pragma: 'no-cache',
    'X-Content-Type-Options': 'nosniff', 'Referrer-Policy': 'no-referrer', ...headers,
  });
  res.end(type.startsWith('application/json') ? JSON.stringify(body) : body);
}
const oauthError = (res, status, error, description) => send(res, status, { error, error_description: description });
const failure = (status, message) => Object.assign(new Error(message), { status });
const hasScope = (scope, required) => scope.split(' ').includes(required)
  || (required === READ_SCOPE && scope.split(' ').includes(AUTHOR_SCOPE));
function canonicalScope(value) {
  if (typeof value !== 'string' || !value || value.length > 100) return null;
  const parts = value.split(' ');
  if (new Set(parts).size !== parts.length || parts.some(part => ![READ_SCOPE, AUTHOR_SCOPE].includes(part))) return null;
  return [READ_SCOPE, AUTHOR_SCOPE].filter(part => parts.includes(part)).join(' ');
}

function formBody(req) {
  if (!/^application\/x-www-form-urlencoded(?:\s*;|$)/i.test(req.headers['content-type'] ?? '')) {
    req.resume();
    return Promise.reject(failure(415, 'A form-encoded request is required.'));
  }
  if (Number(req.headers['content-length']) > FORM_LIMIT) {
    req.resume();
    return Promise.reject(failure(413, 'Request body is too large.'));
  }
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    const cleanup = () => {
      clearTimeout(timer);
      req.off('data', onData); req.off('end', onEnd); req.off('error', onError); req.off('aborted', onAborted);
    };
    const finishError = error => { cleanup(); req.resume(); reject(error); };
    const onData = chunk => {
      size += chunk.length;
      if (size > FORM_LIMIT) finishError(failure(413, 'Request body is too large.'));
      else chunks.push(chunk);
    };
    const onEnd = () => {
      cleanup();
      const form = new URLSearchParams(Buffer.concat(chunks).toString('utf8'));
      for (const key of form.keys()) {
        if (form.getAll(key).length !== 1) return reject(failure(400, 'Duplicate form parameters are not accepted.'));
      }
      resolve(form);
    };
    const onError = () => finishError(failure(400, 'Request body could not be read.'));
    const onAborted = () => finishError(failure(400, 'Request was interrupted.'));
    const timer = setTimeout(() => finishError(failure(408, 'Request timed out.')), 10_000);
    timer.unref();
    req.on('data', onData); req.on('end', onEnd); req.on('error', onError); req.on('aborted', onAborted);
  });
}

function allowedRedirect(value) {
  if (value === CHATGPT_REDIRECT_URI) return true;
  // Only use a callback-specific URI if copied exactly from ChatGPT's management page.
  return /^https:\/\/chatgpt\.com\/connector\/oauth\/[A-Za-z0-9_-]{1,128}$/.test(value);
}

/** Static credentials come from private local setup. grantStorePath optionally
 * preserves hash-only grant families across restarts, bound to this issuer and
 * client. Refresh rotation follows RFC 9700 section 4.14.2; replay revokes the
 * whole family. Authoring requires a fresh explicit meridian.author consent.
 * Pending browser transactions and unused authorization codes never persist.
 */
export function createAuth(config) {
  const publicUrl = new URL(config.publicOrigin);
  if (publicUrl.protocol !== 'https:' || publicUrl.username || publicUrl.password
      || publicUrl.pathname !== '/' || publicUrl.search || publicUrl.hash) {
    throw new Error('publicOrigin must be an HTTPS origin without credentials or a path.');
  }
  const issuer = publicUrl.origin;
  for (const [name, minimum] of [['clientId', 12], ['clientSecret', 32], ['pairingCode', 16]]) {
    if (typeof config[name] !== 'string' || config[name].length < minimum || config[name].length > 512) {
      throw new Error(`${name} must be a non-empty private setup value of appropriate length.`);
    }
  }
  const redirects = config.redirectUris ?? [CHATGPT_REDIRECT_URI];
  if (!Array.isArray(redirects) || redirects.length < 1 || redirects.length > 4 || !redirects.every(allowedRedirect)) {
    throw new Error('Only exact documented ChatGPT callback URIs may be registered.');
  }
  const redirectSet = new Set(redirects);
  const now = config.now ?? Date.now;
  const ttl = (name, fallback, maximum) => {
    const value = config[name] ?? fallback;
    if (!Number.isSafeInteger(value) || value <= 0 || value > maximum) throw new Error(`Invalid ${name}.`);
    return value;
  };
  const codeTtlMs = ttl('codeTtlMs', 60_000, 300_000);
  const authorizationTtlMs = ttl('authorizationTtlMs', 300_000, 600_000);
  const tokenTtlMs = ttl('tokenTtlMs', 3_600_000, 28_800_000);
  const refreshTtlMs = ttl('refreshTtlMs', 30 * 86400_000, 90 * 86400_000);
  const familyTtlMs = ttl('familyTtlMs', 90 * 86400_000, 180 * 86400_000);
  const resource = `${issuer}/mcp`;
  const protectedResourceMetadataUrl = `${issuer}/.well-known/oauth-protected-resource/mcp`;
  const pending = new Map();
  const codes = new Map();
  const spentCodes = new Map();
  const tokens = new Map();
  const families = new Map();
  const refreshTokens = new Map();
  const limits = new Map();
  const pairingHash = digest(config.pairingCode);
  const secretHash = digest(config.clientSecret);
  const store = config.grantStorePath ? createPrivateGrantStore({
    path: config.grantStorePath,
    binding: digest(JSON.stringify({ issuer, resource, clientIdHash: digest(config.clientId), secretHash })),
  }) : undefined;
  let unavailable = false;

  function loadGrants(value) {
    if (!value) return;
    const hashPattern = /^[a-f0-9]{64}$/;
    const count = (rows, maximum) => Array.isArray(rows) && rows.length <= maximum;
    const time = value => Number.isSafeInteger(value) && value >= 0;
    const keys = (record, names) => record && typeof record === 'object' && !Array.isArray(record)
      && Object.keys(record).sort().join(',') === [...names].sort().join(',');
    const load = (rows, map, check) => {
      for (const row of rows) {
        if (!Array.isArray(row) || row.length !== 2 || !hashPattern.test(row[0]) || map.has(row[0]) || !check(row[1])) throw new Error('Private OAuth grant store contains invalid grant records.');
        map.set(row[0], row[1]);
      }
    };
    if (value.schemaVersion !== 1 || !count(value.families, MAX_ENTRIES) || !count(value.tokens, MAX_ENTRIES)
      || !count(value.refreshTokens, MAX_REFRESH_ENTRIES) || !count(value.spentCodes, MAX_ENTRIES)) throw new Error('Private OAuth grant store contains an unsupported grant schema.');
    load(value.families, families, r => keys(r, ['scope', 'createdAt', 'expiresAt', 'revokedAt']) && canonicalScope(r.scope) === r.scope
      && time(r.createdAt) && time(r.expiresAt) && r.createdAt < r.expiresAt && (r.revokedAt === null || time(r.revokedAt)));
    load(value.tokens, tokens, r => keys(r, ['familyId', 'scope', 'expiresAt']) && families.has(r.familyId) && time(r.expiresAt)
      && canonicalScope(r.scope) === r.scope && r.scope.split(' ').every(scope => hasScope(families.get(r.familyId).scope, scope))
      && r.expiresAt <= families.get(r.familyId).expiresAt);
    load(value.refreshTokens, refreshTokens, r => keys(r, ['familyId', 'scope', 'createdAt', 'expiresAt', 'usedAt'])
      && families.has(r.familyId) && canonicalScope(r.scope) === r.scope && time(r.createdAt) && time(r.expiresAt)
      && r.createdAt < r.expiresAt && (r.usedAt === null || time(r.usedAt))
      && r.scope.split(' ').every(scope => hasScope(families.get(r.familyId).scope, scope)) && r.expiresAt <= families.get(r.familyId).expiresAt);
    load(value.spentCodes, spentCodes, r => keys(r, ['familyId', 'expiresAt']) && families.has(r.familyId) && time(r.expiresAt)
      && r.expiresAt <= families.get(r.familyId).expiresAt);
  }
  loadGrants(store?.load());
  function persist() {
    if (!store) return;
    try {
      store.save({ schemaVersion: 1, families: [...families], tokens: [...tokens], refreshTokens: [...refreshTokens], spentCodes: [...spentCodes] });
    } catch (error) {
      unavailable = true;
      tokens.clear();
      throw error;
    }
  }

  function prune() {
    const time = now();
    for (const map of [pending, codes, spentCodes, tokens, families, limits]) {
      for (const [key, record] of map) if (record.expiresAt <= time) map.delete(key);
    }
    for (const [key, record] of refreshTokens) {
      // Retain used refresh-token digests until the family expires to detect
      // replay even after the particular rotation's inactivity expiry.
      if (!families.has(record.familyId) || (record.usedAt === null && record.expiresAt <= time)) refreshTokens.delete(key);
    }
    for (const map of [tokens, spentCodes]) for (const [key, record] of map) if (!families.has(record.familyId)) map.delete(key);
  }
  function rate(name, maximum, windowMs) {
    const record = limits.get(name);
    if (record && record.expiresAt > now()) {
      if (record.count >= maximum) return false;
      record.count += 1;
    } else limits.set(name, { count: 1, expiresAt: now() + windowMs });
    return true;
  }
  function challenge(error = 'invalid_token', requiredScope = READ_SCOPE) {
    if (![READ_SCOPE, AUTHOR_SCOPE].includes(requiredScope)) throw new Error('Unknown required OAuth scope.');
    return `Bearer resource_metadata="${protectedResourceMetadataUrl}", scope="${requiredScope}", error="${error}", error_description="Link Meridian to authorize the requested access."`;
  }
  function verifyBearer(req) {
    prune();
    if (unavailable) return { ok: false, reason: 'invalid_token' };
    const value = req.headers.authorization;
    if (typeof value !== 'string' || !/^Bearer [A-Za-z0-9_-]{43}$/i.test(value)) return { ok: false, reason: 'invalid_token' };
    const record = tokens.get(digest(value.slice(7)));
    const family = record && families.get(record.familyId);
    if (!record || !family || family.revokedAt !== null || family.expiresAt <= now() || record.expiresAt <= now()) {
      return { ok: false, reason: 'invalid_token' };
    }
    return { ok: true, subject: 'local-owner', scope: record.scope, scopes: record.scope.split(' '), expiresAt: record.expiresAt };
  }
  function requireBearer(req, res) {
    if (verifyBearer(req).ok) return true;
    send(res, 401, { error: 'unauthorized' }, undefined, { 'WWW-Authenticate': challenge() });
    return false;
  }
  function requireScope(req, res, requiredScope) {
    if (![READ_SCOPE, AUTHOR_SCOPE].includes(requiredScope)) throw new Error('Unknown required OAuth scope.');
    const access = verifyBearer(req);
    if (!access.ok) {
      send(res, 401, { error: 'unauthorized' }, undefined, { 'WWW-Authenticate': challenge('invalid_token', requiredScope) });
      return false;
    }
    if (!hasScope(access.scope, requiredScope)) {
      send(res, 403, { error: 'insufficient_scope' }, undefined, { 'WWW-Authenticate': challenge('insufficient_scope', requiredScope) });
      return false;
    }
    return true;
  }

  function validateAuthorization(query) {
    for (const key of query.keys()) if (query.getAll(key).length !== 1) throw failure(400, 'Duplicate authorization parameters are not accepted.');
    if (!equal(query.get('client_id'), config.clientId)) throw failure(400, 'Unknown OAuth client.');
    if (!redirectSet.has(query.get('redirect_uri'))) throw failure(400, 'Redirect URI is not registered.');
    if (query.get('response_type') !== 'code') throw failure(400, 'Only authorization-code responses are supported.');
    if (query.get('resource') !== resource) throw failure(400, 'The requested resource is not this MCP server.');
    if (query.get('code_challenge_method') !== 'S256' || !/^[A-Za-z0-9_-]{43}$/.test(query.get('code_challenge') ?? '')) {
      throw failure(400, 'PKCE S256 is required.');
    }
    const state = query.get('state');
    if (!state || state.length > 1024 || /[\x00-\x1f\x7f]/.test(state)) throw failure(400, 'A valid OAuth state is required.');
    const scope = canonicalScope(query.get('scope') ?? READ_SCOPE);
    if (!scope) throw failure(400, 'Only the Meridian read and author scopes are supported.');
    return {
      clientId: config.clientId, redirectUri: query.get('redirect_uri'), resource,
      challenge: query.get('code_challenge'), state, scope,
    };
  }

  function approvePage(res, transaction) {
    const nonce = opaque();
    pending.set(digest(nonce), { ...transaction, expiresAt: now() + authorizationTtlMs });
    const authoring = hasScope(transaction.scope, AUTHOR_SCOPE);
    const permissions = authoring
      ? 'Allow this ChatGPT connection to inspect approved Meridian Works and create or update Works through Meridian authoring drafts.'
      : 'Allow this ChatGPT connection to read approved Meridian Works and authoring rules.';
    const restrictions = authoring
      ? 'This connection cannot run commands or read arbitrary files. Authoring access stays separate from existing read-only connections.'
      : 'This connection cannot edit Works, run commands, or read arbitrary files.';
    const buttonLabel = authoring ? 'Allow authoring access' : 'Allow read-only access';
    const html = `<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Connect Meridian</title><style>body{font:16px system-ui;max-width:36rem;margin:10vh auto;padding:1.5rem;background:#10202a;color:#e3edf0}input,button{font:inherit;padding:.75rem;margin:.5rem 0}input{box-sizing:border-box;width:100%}button{cursor:pointer}small{display:block;line-height:1.5}</style><h1>Connect Meridian</h1><p>${permissions}</p><p>${restrictions}</p><form method="post" action="/authorize"><input type="hidden" name="transaction" value="${escapeHtml(nonce)}"><label for="pairing">Private pairing code</label><input id="pairing" name="pairing_code" type="password" autocomplete="off" required maxlength="512"><small>Copy the pairing code from this device's private Meridian setup file. Only approve a connection you just started in ChatGPT. Never paste the pairing code into a conversation.</small><button type="submit" name="decision" value="allow">${buttonLabel}</button> <button type="submit" name="decision" value="deny" formnovalidate>Cancel</button></form></html>`;
    send(res, 200, html, 'text/html; charset=utf-8', {
      // Fetch serializes Origin as null on navigation/form POSTs when the page
      // uses no-referrer. Keep the real HTTPS Origin for the strict approval
      // check, while exposing only the origin (never OAuth query parameters).
      // https://fetch.spec.whatwg.org/#append-a-request-origin-header
      'Referrer-Policy': 'strict-origin',
      // Chromium applies form-action to the eventual OAuth 302 destination as
      // well as this same-origin POST. This URI already passed the exact static
      // redirect allowlist; permit this transaction's callback, never its host.
      'Content-Security-Policy': `default-src 'none'; style-src 'unsafe-inline'; form-action 'self' ${transaction.redirectUri}; frame-ancestors 'none'; base-uri 'none'`,
      'X-Frame-Options': 'DENY',
      'Set-Cookie': `__Host-meridian_pair=${nonce}; Path=/; Secure; HttpOnly; SameSite=Strict; Max-Age=${Math.ceil(authorizationTtlMs / 1000)}`,
    });
  }
  function redirect(res, record, values) {
    const target = new URL(record.redirectUri);
    target.searchParams.set('state', record.state);
    target.searchParams.set('iss', issuer);
    for (const [name, value] of Object.entries(values)) target.searchParams.set(name, value);
    send(res, 302, '', 'text/plain; charset=utf-8', {
      Location: target.href,
      'Set-Cookie': '__Host-meridian_pair=; Path=/; Secure; HttpOnly; SameSite=Strict; Max-Age=0',
    });
  }

  function authenticateClient(req, form) {
    let id = form.get('client_id');
    let secret = form.get('client_secret');
    const header = req.headers.authorization;
    if (header !== undefined) {
      if (typeof header !== 'string' || !/^Basic [A-Za-z0-9+/]+={0,2}$/.test(header) || header.length > 2048 || secret !== null) return false;
      const encoded = header.slice(6);
      const decodedBytes = Buffer.from(encoded, 'base64');
      if (decodedBytes.toString('base64').replace(/=+$/, '') !== encoded.replace(/=+$/, '')) return false;
      const decoded = decodedBytes.toString('utf8');
      const colon = decoded.indexOf(':');
      if (colon < 0) return false;
      try {
        const basicId = decodeURIComponent(decoded.slice(0, colon).replace(/\+/g, ' '));
        secret = decodeURIComponent(decoded.slice(colon + 1).replace(/\+/g, ' '));
        if (id !== null && id !== basicId) return false;
        id = basicId;
      } catch { return false; }
    }
    return equal(id, config.clientId) && typeof secret === 'string' && equal(digest(secret), secretHash);
  }

  function revokeFamily(familyId) {
    const family = families.get(familyId);
    if (family) family.revokedAt = now();
    for (const [key, token] of tokens) if (token.familyId === familyId) tokens.delete(key);
  }
  function issueTokens(familyId, scope) {
    const family = families.get(familyId);
    const accessToken = opaque();
    const refreshToken = opaque();
    const expiresAt = Math.min(now() + tokenTtlMs, family.expiresAt);
    tokens.set(digest(accessToken), { familyId, scope, expiresAt });
    refreshTokens.set(digest(refreshToken), { familyId, scope, createdAt: now(), expiresAt: Math.min(now() + refreshTtlMs, family.expiresAt), usedAt: null });
    return { access_token: accessToken, refresh_token: refreshToken, token_type: 'Bearer', expires_in: Math.floor((expiresAt - now()) / 1000), scope };
  }
  function tokenGrant(form) {
    const denied = (error, description, status = 400) => ({ status, body: { error, error_description: description } });
    const capacity = () => tokens.size < MAX_ENTRIES && refreshTokens.size < MAX_REFRESH_ENTRIES;
    if (form.get('grant_type') === 'refresh_token') {
      // PKCE protects the original authorization-code exchange. Refresh follows
      // RFC 6749 section 6: authenticated client, exact resource and no escalation.
      if (form.get('resource') !== resource) return denied('invalid_grant', 'The refresh resource does not match this MCP server.');
      const rawRefresh = form.get('refresh_token') ?? '';
      const refresh = /^[A-Za-z0-9_-]{43}$/.test(rawRefresh) ? refreshTokens.get(digest(rawRefresh)) : undefined;
      const family = refresh && families.get(refresh.familyId);
      if (!refresh || !family || family.revokedAt !== null || family.expiresAt <= now()) return denied('invalid_grant', 'Refresh access is invalid or expired. Link Meridian again.');
      if (refresh.usedAt !== null) {
        revokeFamily(refresh.familyId);
        persist();
        return denied('invalid_grant', 'Refresh-token reuse revoked this connection. Link Meridian again.');
      }
      if (refresh.expiresAt <= now()) return denied('invalid_grant', 'Refresh access has expired. Link Meridian again.');
      const scope = form.has('scope') ? canonicalScope(form.get('scope')) : refresh.scope;
      if (!scope || !scope.split(' ').every(required => hasScope(refresh.scope, required))) return denied('invalid_scope', 'Additional permission requires a new authorization and explicit consent.');
      if (!capacity()) return denied('temporarily_unavailable', 'The connection grant limit was reached. Revoke unused connections locally.', 429);
      refresh.usedAt = now();
      const body = issueTokens(refresh.familyId, scope);
      persist();
      return { status: 200, body };
    }
    if (form.get('grant_type') !== 'authorization_code') return denied('unsupported_grant_type', 'Use authorization_code or refresh_token.');
    const code = form.get('code');
    const codeKey = typeof code === 'string' ? digest(code) : '';
    const spent = spentCodes.get(codeKey);
    if (spent) {
      revokeFamily(spent.familyId);
      persist();
      return denied('invalid_grant', 'Authorization-code reuse revoked this connection. Link Meridian again.');
    }
    const record = codes.get(codeKey);
    codes.delete(codeKey);
    const verifier = form.get('code_verifier') ?? '';
    if (!record || record.expiresAt <= now() || record.clientId !== config.clientId
      || record.redirectUri !== form.get('redirect_uri') || form.get('resource') !== resource
      || !/^[A-Za-z0-9._~-]{43,128}$/.test(verifier)
      || !equal(createHash('sha256').update(verifier).digest('base64url'), record.challenge)) {
      return denied('invalid_grant', 'The code, resource, redirect, or PKCE verifier is invalid or expired.');
    }
    if (families.size >= MAX_ENTRIES || spentCodes.size >= MAX_ENTRIES || !capacity()) return denied('temporarily_unavailable', 'The connection grant limit was reached. Revoke unused connections locally.', 429);
    const familyId = digest(opaque());
    const expiresAt = now() + familyTtlMs;
    families.set(familyId, { scope: record.scope, createdAt: now(), expiresAt, revokedAt: null });
    const body = issueTokens(familyId, record.scope);
    spentCodes.set(codeKey, { familyId, expiresAt });
    persist();
    return { status: 200, body };
  }

  async function handle(req, res, url = new URL(req.url, issuer)) {
    const path = url.pathname;
    if (!['/.well-known/oauth-protected-resource', '/.well-known/oauth-protected-resource/mcp', '/.well-known/oauth-authorization-server', '/authorize', '/token', '/register'].includes(path)) return false;
    prune();
    try {
      if (path.startsWith('/.well-known/')) {
        if (req.method !== 'GET') { send(res, 405, { error: 'method_not_allowed' }, undefined, { Allow: 'GET' }); return true; }
        const metadata = path.includes('oauth-protected-resource') ? {
          resource, authorization_servers: [issuer], scopes_supported: [READ_SCOPE, AUTHOR_SCOPE], bearer_methods_supported: ['header'], resource_name: 'Meridian private authoring connection',
        } : {
          issuer, authorization_endpoint: `${issuer}/authorize`, token_endpoint: `${issuer}/token`,
          authorization_response_iss_parameter_supported: true,
          response_types_supported: ['code'], grant_types_supported: ['authorization_code', 'refresh_token'],
          token_endpoint_auth_methods_supported: ['client_secret_basic', 'client_secret_post'],
          code_challenge_methods_supported: ['S256'], scopes_supported: [READ_SCOPE, AUTHOR_SCOPE],
        };
        send(res, 200, metadata, undefined, { 'Access-Control-Allow-Origin': '*' });
        return true;
      }
      if (unavailable) { oauthError(res, 503, 'temporarily_unavailable', 'Private authorization storage is unavailable. Restore it locally before reconnecting.'); return true; }
      if (path === '/register') { oauthError(res, 405, 'unsupported_operation', 'This private connection uses a pre-registered client.'); return true; }
      if (path === '/authorize' && req.method === 'GET') {
        if (!rate('authorize', 60, 60_000) || pending.size >= MAX_ENTRIES) { oauthError(res, 429, 'temporarily_unavailable', 'Too many authorization attempts. Try again later.'); return true; }
        approvePage(res, validateAuthorization(url.searchParams));
        return true;
      }
      if (path === '/authorize' && req.method === 'POST') {
        if (req.headers.origin !== issuer) { oauthError(res, 403, 'access_denied', 'The approval must be submitted from this authorization page.'); return true; }
        if (!rate('approval', 20, 600_000)) { oauthError(res, 429, 'temporarily_unavailable', 'Too many approval attempts. Try again later.'); return true; }
        const form = await formBody(req);
        const nonce = form.get('transaction');
        const cookie = /(?:^|;\s*)__Host-meridian_pair=([A-Za-z0-9_-]{43})(?:;|$)/.exec(req.headers.cookie ?? '')?.[1];
        const record = typeof nonce === 'string' ? pending.get(digest(nonce)) : undefined;
        if (!record || record.expiresAt <= now() || !equal(nonce, cookie)) { oauthError(res, 403, 'access_denied', 'The approval has expired or does not match this browser. Start again from ChatGPT.'); return true; }
        // Every submitted transaction is single-use, including failures and cancellation.
        pending.delete(digest(nonce));
        if (form.get('decision') === 'deny') { redirect(res, record, { error: 'access_denied' }); return true; }
        if (form.get('decision') !== 'allow' || !equal(digest(form.get('pairing_code') ?? ''), pairingHash)) {
          oauthError(res, 403, 'access_denied', 'Pairing was not approved. Start again from ChatGPT.'); return true;
        }
        if (codes.size >= MAX_ENTRIES) { oauthError(res, 429, 'temporarily_unavailable', 'Too many pending grants. Try again later.'); return true; }
        const code = opaque();
        codes.set(digest(code), { ...record, expiresAt: now() + codeTtlMs });
        redirect(res, record, { code });
        return true;
      }
      if (path === '/token' && req.method === 'POST') {
        if (req.headers.origin && req.headers.origin !== issuer) { oauthError(res, 403, 'access_denied', 'Cross-origin browser requests are not supported.'); return true; }
        if (!rate('token', 60, 60_000)) { oauthError(res, 429, 'temporarily_unavailable', 'Too many token requests. Try again later.'); return true; }
        const form = await formBody(req);
        if (!authenticateClient(req, form)) { send(res, 401, { error: 'invalid_client' }, undefined, { 'WWW-Authenticate': 'Basic realm="Meridian MCP"' }); return true; }
        const result = tokenGrant(form);
        send(res, result.status, result.body);
        return true;
      }
      send(res, 405, { error: 'method_not_allowed' }, undefined, { Allow: path === '/authorize' ? 'GET, POST' : 'POST' });
      return true;
    } catch (error) {
      oauthError(res, error.status ?? 500, error.status ? 'invalid_request' : 'server_error', error.status ? error.message : 'Authorization could not be completed.');
      return true;
    }
  }
  return {
    handle, verifyBearer, requireBearer, requireScope, challenge, protectedResourceMetadataUrl, resource,
    revokeAll() { pending.clear(); codes.clear(); spentCodes.clear(); tokens.clear(); refreshTokens.clear(); families.clear(); persist(); },
    close() { pending.clear(); codes.clear(); unavailable = true; },
  };
}
