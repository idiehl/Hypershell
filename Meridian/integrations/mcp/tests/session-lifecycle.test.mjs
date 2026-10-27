import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { setTimeout as delay } from 'node:timers/promises';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';
import { createCompanion } from '../src/server.mjs';

// Fixture-only HTTP origins and credentials. These tests never load the private
// companion configuration, contact the installed host, or change saved Works.
async function fixture(t, { keepAliveTimeout = 5000 } = {}) {
  const backend = createServer((req, res) => {
    res.setHeader('content-type', 'application/json');
    res.end(JSON.stringify({ application: 'atlas-meridian', status: 'ready' }));
  });
  backend.listen(0, '127.0.0.1');
  await once(backend, 'listening');
  const audits = [], calls = [];
  const auth = {
    handle: async () => false,
    requireBearer(req, res) {
      if (req.headers.authorization === 'Bearer lifecycle-fixture') return true;
      res.writeHead(401); res.end(); return false;
    },
    verifyBearer: () => ({ ok: true, scopes: ['meridian.read', 'meridian.author'] }),
  };
  const authoringService = {
    async schema() {
      const call = calls.length;
      calls.push(call);
      // Overlapping requests complete in different orders, so closing one
      // request's transport must not interrupt another active request.
      await delay(call % 2 ? 80 : 160);
      return { version: 1, fixtureCall: call };
    },
  };
  const companion = createCompanion({
    publicOrigin: 'https://lifecycle-fixture.example', port: 0,
    meridianOrigin: `http://127.0.0.1:${backend.address().port}`,
    projectIds: ['fixture-project'], authoringEnabled: true,
  }, { auth, authoringService, readRules: async () => 'PURPOSE-001 [ACCEPTED]\nCompose a graph.\n', onAudit: record => audits.push(record) });
  await companion.listen();
  companion.server.keepAliveTimeout = keepAliveTimeout;
  // Node 24 adds a buffer before physically closing an idle socket. Remove it
  // only in this disposable fixture to make expiry deterministic and quick.
  companion.server.keepAliveTimeoutBuffer = 0;
  const sockets = new Set();
  companion.server.on('connection', socket => {
    sockets.add(socket);
    socket.once('close', () => sockets.delete(socket));
  });
  const clients = [], responses = [];
  t.after(async () => {
    await Promise.all(clients.map(client => client.close()));
    companion.server.closeAllConnections(); backend.closeAllConnections();
    await Promise.all([
      new Promise(resolve => companion.server.close(resolve)),
      new Promise(resolve => backend.close(resolve)),
    ]);
  });
  async function client(name) {
    const result = new Client({ name, version: '1.0.0' });
    clients.push(result);
    const transport = new StreamableHTTPClientTransport(new URL(`http://127.0.0.1:${companion.server.address().port}/mcp`), {
      requestInit: { headers: { authorization: 'Bearer lifecycle-fixture' } },
      fetch: async (input, init) => {
        const response = await fetch(input, init);
        responses.push({ method: init?.method, status: response.status, session: response.headers.get('mcp-session-id'), contentType: response.headers.get('content-type') });
        return response;
      },
    });
    await result.connect(transport);
    assert.equal(transport.sessionId, undefined);
    const tools = await result.listTools();
    assert(tools.tools.some(tool => tool.name === 'meridian_authoring_schema'));
    return { client: result, transport };
  }
  return { client, audits, calls, responses, sockets };
}

test('stateless SDK requests survive delayed and concurrent authoring calls after discovery', { timeout: 10000 }, async t => {
  const f = await fixture(t);
  const first = await f.client('lifecycle-first'), second = await f.client('lifecycle-second');
  const results = await Promise.all(Array.from({ length: 8 }, (_, index) =>
    (index % 2 ? first : second).client.callTool({ name: 'meridian_authoring_schema', arguments: {} })));
  assert.equal(f.calls.length, 8);
  assert.deepEqual(results.map(result => result.structuredContent.fixtureCall).sort((a, b) => a - b), [0, 1, 2, 3, 4, 5, 6, 7]);
  assert(results.every(result => !result.isError));
  const again = await first.client.callTool({ name: 'meridian_authoring_schema', arguments: {} });
  assert.equal(again.structuredContent.fixtureCall, 8);
  assert.equal(first.transport.sessionId, undefined);
  assert(f.responses.every(response => response.session === null));
  assert(f.responses.filter(response => response.method === 'POST' && response.status === 200).every(response => /^application\/json\b/.test(response.contentType)));
  assert.equal(f.audits.filter(event => event.tool === 'meridian_authoring_schema' && event.success).length, 9);
});

test('same initialized SDK client continues after the idle keep-alive socket actually closes', { timeout: 10000 }, async t => {
  const f = await fixture(t, { keepAliveTimeout: 40 });
  const { client, transport } = await f.client('lifecycle-idle');
  const before = await client.callTool({ name: 'meridian_authoring_schema', arguments: {} });
  assert.equal(before.structuredContent.fixtureCall, 0);
  const deadline = Date.now() + 3000;
  while (f.sockets.size && Date.now() < deadline) await delay(20);
  assert.equal(f.sockets.size, 0, 'The fixture must actually close the old idle HTTP connection.');
  const after = await client.callTool({ name: 'meridian_authoring_schema', arguments: {} });
  assert.equal(after.structuredContent.fixtureCall, 1);
  assert.equal(transport.sessionId, undefined);
  assert.equal(f.audits.filter(event => event.phase === 'mcp.initialize').length, 1, 'No MCP reinitialization or reconnect should be required for an idle HTTP socket.');
  assert(f.responses.every(response => response.session === null));
});
