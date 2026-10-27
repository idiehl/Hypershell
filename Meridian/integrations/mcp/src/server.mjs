import { createServer } from 'node:http';
import { appendFile, readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash, randomBytes } from 'node:crypto';
import { StreamableHTTPServerTransport } from '@modelcontextprotocol/sdk/server/streamableHttp.js';
import { createMeridianTools, createMcpServer, ALL_TOOL_NAMES } from './meridian-tools.mjs';
import { createAuthoringService } from './authoring-service.mjs';

const MAX_BODY = 32 * 1024;
const MAX_AUTHOR_BODY = 2 * 1024 * 1024;
const AUTHOR_SCOPE = 'meridian.author';

function json(res, status, body, headers = {}) {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', ...headers });
  res.end(JSON.stringify(body));
}

export function validatePublicOrigin(value) {
  const url = new URL(value);
  const local = ['127.0.0.1', '[::1]', 'localhost'].includes(url.hostname);
  if ((url.protocol !== 'https:' && !(url.protocol === 'http:' && local)) || url.username || url.password
      || url.pathname !== '/' || url.search || url.hash) throw new Error('Configure an HTTPS public origin, or a loopback HTTP origin for local testing.');
  return url.origin;
}

async function requestJson(req, maximum = MAX_BODY) {
  let size = 0;
  const chunks = [];
  for await (const chunk of req) {
    size += chunk.length;
    if (size > maximum) throw Object.assign(new Error('Oversized request.'), { status: 413 });
    chunks.push(chunk);
  }
  try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { throw Object.assign(new Error('Invalid JSON.'), { status: 400 }); }
}

/** The host remains loopback-only; the HTTPS forwarder exposes this companion, never Meridian itself. */
export function createCompanion(config, { auth, onAudit, authoringService, ...dependencies } = {}) {
  const publicOrigin = validatePublicOrigin(config.publicOrigin);
  if (!auth || typeof auth.handle !== 'function' || typeof auth.requireBearer !== 'function') throw new Error('A configured OAuth provider is required.');
  const port = config.port ?? 5241;
  if (!Number.isInteger(port) || port < 0 || port > 65535) throw new Error('Invalid companion port.');
  const tools = createMeridianTools(config, dependencies);
  const authoring = config.authoringEnabled ? (authoringService ?? createAuthoringService(config, dependencies)) : undefined;
  const tickets = new Map();
  const ticketHash = value => createHash('sha256').update(value).digest('hex');
  const pruneTickets = () => { for (const [key, value] of tickets) if (value.expiresAt <= Date.now()) tickets.delete(key); };
  const createArtifactUrl = artifactId => {
    pruneTickets();
    while (tickets.size >= 512) tickets.delete(tickets.keys().next().value);
    const token = randomBytes(32).toString('base64url');
    tickets.set(ticketHash(token), { artifactId, expiresAt: Date.now() + 900000 });
    return `${publicOrigin}/artifacts/${artifactId}?ticket=${token}`;
  };
  if (config.auditPath !== undefined && (typeof config.auditPath !== 'string' || !config.auditPath)) throw new Error('Invalid local audit path.');
  let auditQueue = Promise.resolve();
  const audit = async event => {
    if (!['mcp.initialize', 'tools.list', 'tools.call'].includes(event.phase)) return;
    const record = { timestamp: new Date().toISOString(), phase: event.phase, success: event.success === true };
    if (event.phase === 'tools.call') {
      if (!ALL_TOOL_NAMES.includes(event.tool)) return;
      record.tool = event.tool;
    }
    // Audit only the protocol operation and outcome; never arguments, results, headers, or credentials.
    auditQueue = auditQueue.then(async () => {
      if (config.auditPath) await appendFile(config.auditPath, `${JSON.stringify(record)}\n`, { encoding: 'utf8', mode: 0o600 });
      if (onAudit) await onAudit(record);
    }).catch(() => {});
    await auditQueue;
  };
  const publicHost = new URL(publicOrigin).host.toLowerCase();
  let active = 0;
  const server = createServer(async (req, res) => {
    res.setHeader('cache-control', 'no-store');
    res.setHeader('x-content-type-options', 'nosniff');
    res.setHeader('referrer-policy', 'no-referrer');
    res.setHeader('content-security-policy', "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'");
    try {
      const hostname = (req.headers.host ?? '').toLowerCase();
      const localPort = server.address()?.port ?? port;
      if (![publicHost, `127.0.0.1:${localPort}`, `localhost:${localPort}`, `[::1]:${localPort}`].includes(hostname)) {
        return json(res, 403, { error: 'Host is not allowed.' });
      }
      if (!req.url.startsWith('/') || req.url.startsWith('//')) return json(res, 400, { error: 'Use a local request path.' });
      if (req.url.length > 2048 || Number(req.headers['content-length'] ?? 0) > MAX_AUTHOR_BODY) return json(res, 413, { error: 'Request exceeds the companion limit.' });
      if (req.headers.origin && req.headers.origin !== publicOrigin) return json(res, 403, { error: 'Origin is not allowed.' });
      const url = new URL(req.url, publicOrigin);
      if (await auth.handle(req, res, url)) return;
      const artifactMatch = /^\/artifacts\/([a-f0-9-]{36})$/.exec(url.pathname);
      if (authoring && artifactMatch) {
        if (req.method !== 'GET') return json(res, 405, { error: 'Use GET.' }, { allow: 'GET' });
        if ([...url.searchParams.keys()].some(key => key !== 'ticket') || url.searchParams.getAll('ticket').length > 1) return json(res, 400, { error: 'Invalid artifact request.' });
        const granted = auth.verifyBearer?.(req);
        const hasAuthorScope = granted?.ok && (granted.scopes ?? granted.scope?.split(' ') ?? []).includes(AUTHOR_SCOPE);
        const token = url.searchParams.get('ticket') ?? '';
        pruneTickets();
        const ticket = /^[A-Za-z0-9_-]{43}$/.test(token) ? tickets.get(ticketHash(token)) : undefined;
        if (!hasAuthorScope && !(ticket?.artifactId === artifactMatch[1] && ticket.expiresAt > Date.now())) return json(res, 401, { error: 'Authorize authoring access or use the private preview link returned by Meridian.' });
        let artifact;
        try { artifact = await authoring.artifact({ artifactId: artifactMatch[1] }); }
        catch { return json(res, 404, { error: 'Preview unavailable.' }); }
        if (!['image/png', 'text/html; charset=utf-8'].includes(artifact.mime) || artifact.bytes.length > 64 * 1024 * 1024) return json(res, 413, { error: 'Preview exceeds the browser limit. Use a focused PNG preview.' });
        res.setHeader('content-type', artifact.mime);
        res.setHeader('content-length', artifact.bytes.length);
        res.setHeader('content-disposition', artifact.mime === 'image/png' ? 'inline; filename="preview.png"' : 'inline; filename="meridian-preview.html"');
        // Viewer code is the trusted installed renderer. Sandbox prevents the
        // preview from acquiring this origin's cookies/OAuth or making requests.
        res.setHeader('content-security-policy', "sandbox allow-scripts allow-downloads allow-popups; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: blob:; font-src data:; media-src data: blob:; frame-src blob: data:; connect-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'");
        res.writeHead(200); res.end(artifact.bytes); return;
      }
      if (url.pathname !== '/mcp' || url.search) return json(res, 404, { error: 'Not found.' });
      if (!auth.requireBearer(req, res)) return;
      if (req.method !== 'POST') return json(res, 405, { error: 'Use POST for stateless MCP requests.' }, { allow: 'POST' });
      if (!/^application\/json(?:\s*;|$)/i.test(req.headers['content-type'] ?? '')) return json(res, 415, { error: 'Use application/json.' });
      if (active >= 16) return json(res, 429, { error: 'Companion is busy. Retry shortly.' }, { 'retry-after': '1' });
      active++;
      let mcp;
      let transport;
      try {
        const grant = auth.verifyBearer?.(req);
        const scopes = grant?.ok ? (grant.scopes ?? grant.scope?.split(' ') ?? []) : [];
        const maximum = authoring && scopes.includes(AUTHOR_SCOPE) ? MAX_AUTHOR_BODY : MAX_BODY;
        if (Number(req.headers['content-length'] ?? 0) > maximum) return json(res, 413, { error: 'Request exceeds this permission level limit.' });
        const body = await requestJson(req, maximum);
        const authorize = required => ({ ok: scopes.includes(required), challenge: auth.challenge?.('insufficient_scope', required) ?? `Bearer scope="${required}", error="insufficient_scope"` });
        mcp = createMcpServer(tools, { onAudit: audit, authoring, authorize, createArtifactUrl });
        transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined, enableJsonResponse: true, maxRequestBodySize: maximum });
        await mcp.connect(transport);
        await transport.handleRequest(req, res, body);
        if (res.statusCode >= 200 && res.statusCode < 300) {
          if (body?.method === 'initialize') await audit({ phase: 'mcp.initialize', success: true });
          else if (body?.method === 'tools/list') await audit({ phase: 'tools.list', success: true });
        }
      } finally {
        active--;
        await mcp?.close().catch(() => {});
        await transport?.close().catch(() => {});
      }
    } catch (error) {
      if (!res.headersSent) json(res, error.status ?? 500, { error: error.status ? error.message : 'Companion request failed.' });
      else res.end();
    }
  });
  server.requestTimeout = 15000;
  server.headersTimeout = 10000;
  server.keepAliveTimeout = 5000;
  return { server, tools, authoring, publicOrigin, port, listen: () => new Promise((resolveListen, reject) => {
    server.once('error', reject);
    server.listen(port, '127.0.0.1', () => { server.removeListener('error', reject); resolveListen(server.address()); });
  }) };
}

export async function startFromConfig(path) {
  const config = JSON.parse(await readFile(path, 'utf8'));
  const { createAuth } = await import('./auth.mjs');
  const companion = createCompanion(config, { auth: createAuth({ ...config.auth, publicOrigin: config.publicOrigin }) });
  const address = await companion.listen();
  process.stdout.write(`Meridian MCP companion listening on 127.0.0.1:${address.port}\n`);
  return companion;
}

if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
  const args = process.argv.slice(2);
  if (args.length !== 2 || args[0] !== '--config') {
    process.stderr.write('Usage: node src/server.mjs --config <private-config.json>\n');
    process.exitCode = 1;
  } else {
    startFromConfig(args[1]).catch(() => { process.stderr.write('Companion startup failed. Check the private configuration.\n'); process.exitCode = 1; });
  }
}
