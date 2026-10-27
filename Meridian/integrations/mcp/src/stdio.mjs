import { appendFile, readFile } from 'node:fs/promises';
import { lstatSync } from 'node:fs';
import { dirname, isAbsolute, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { createMeridianTools, createMcpServer, ALL_TOOL_NAMES } from './meridian-tools.mjs';
import { createAuthoringService } from './authoring-service.mjs';
import { privatePreviewFailureMetadata } from './authoring-tools.mjs';

export const PRIVATE_STDIO_TRANSPORT = 'openai-secure-tunnel-stdio';
export const MAX_STDIO_MESSAGE_BYTES = 2 * 1024 * 1024 + 32 * 1024;
const TUNNEL_SECRET_VARIABLES = new Set([
  'CONTROL_PLANE_API_KEY', 'OPENAI_API_KEY', 'OPENAI_ADMIN_KEY',
  'TUNNEL_TOKEN', 'TUNNEL_CREDENTIALS', 'CLOUDFLARED_TOKEN',
  'CLOUDFLARED_TUNNEL_TOKEN', 'NGROK_AUTHTOKEN',
]);

export function scrubTunnelSecrets(environment = process.env) {
  // The native tunnel may pass its own environment to its MCP subprocess.
  // Neither this server, the authoring worker nor Chromium needs those keys.
  // Case-insensitive matching also handles Windows environment spelling.
  for (const key of Object.keys(environment)) if (TUNNEL_SECRET_VARIABLES.has(key.toUpperCase())) delete environment[key];
}

/** The OpenAI tunnel authenticates its private connection. This process has no
 * network listener or public OAuth endpoint: its stdin/stdout belong solely to
 * the locally configured tunnel client. Owner consent and project scope must be
 * supplied in the private local configuration, never by a tool argument. */
export function validateStdioConfig(config) {
  if (!config || config.transport !== PRIVATE_STDIO_TRANSPORT || config.authoringEnabled !== true || config.authoringConsent !== true) {
    throw new Error('Private stdio requires its explicit transport, authoring enablement, and local-owner authoring consent.');
  }
  for (const field of ['stateRoot', 'rulesPath', 'viewerAssetsRoot', 'auditPath']) {
    if ((field === 'stateRoot' || config[field] !== undefined) && (typeof config[field] !== 'string' || !isAbsolute(config[field]))) {
      throw new Error('Private stdio storage, rulebook, viewer and audit locations must be absolute local paths.');
    }
  }
  if (config.browserExecutablePath !== undefined) {
    const path = config.browserExecutablePath;
    if (typeof path !== 'string' || !isAbsolute(path) || /^[\\/]{2}/.test(path) || /[\r\n\0]/.test(path)) {
      throw new Error('The private preview browser must use an absolute local file path.');
    }
    try {
      if (!lstatSync(path).isFile()) throw new Error();
      for (let current = resolve(path);; current = dirname(current)) {
        if (lstatSync(current).isSymbolicLink()) throw new Error();
        if (dirname(current) === current) break;
      }
    } catch { throw new Error('The private preview browser must be an existing regular file without symbolic links or junctions.'); }
  }
  return config;
}

export function createStdioCompanion(config, { onAudit, authoringService, ...dependencies } = {}) {
  validateStdioConfig(config);
  // These factories retain the existing numeric-loopback backend restriction,
  // explicit project allowlist and Work identity/revision/recovery protections.
  const tools = createMeridianTools(config, dependencies);
  const authoring = authoringService ?? createAuthoringService(config, dependencies);
  let auditQueue = Promise.resolve();
  const audit = async event => {
    if (!['mcp.initialize', 'tools.list', 'tools.call'].includes(event.phase)) return;
    if (event.phase === 'tools.call' && !ALL_TOOL_NAMES.includes(event.tool)) return;
    const record = { timestamp: new Date().toISOString(), transport: PRIVATE_STDIO_TRANSPORT, phase: event.phase, success: event.success === true,
      ...(event.phase === 'tools.call' ? { tool: event.tool } : {}) };
    if (event.phase === 'tools.call' && !record.success && ['meridian_preview', 'meridian_preview_image'].includes(event.tool) && event.errorCode !== undefined) {
      Object.assign(record, privatePreviewFailureMetadata({ code: event.errorCode, previewStage: event.previewStage }));
    }
    // Never record arguments/results, paths, Work contents, credentials or keys.
    auditQueue = auditQueue.then(async () => {
      if (config.auditPath) await appendFile(config.auditPath, `${JSON.stringify(record)}\n`, { encoding: 'utf8', mode: 0o600 });
      if (onAudit) await onAudit(record);
    }).catch(() => { process.stderr.write('Meridian private MCP audit could not be written.\n'); });
    await auditQueue;
  };
  const server = createMcpServer(tools, {
    onAudit: audit, authoring, securityMode: 'private-stdio',
    authorize: required => ({ ok: required === 'meridian.author' && config.authoringConsent === true }),
  });
  // The factory deliberately does not accept an artifact URL callback. PNGs
  // travel in authenticated tunnel MCP messages, with no public browser route.
  return { server, tools, authoring, flushAudit: () => auditQueue };
}

export async function startStdioFromConfig(path, dependencies = {}) {
  scrubTunnelSecrets();
  const config = JSON.parse(await readFile(path, 'utf8'));
  const companion = createStdioCompanion(config, dependencies);
  const transport = new StdioServerTransport(process.stdin, process.stdout, { maxBufferSize: MAX_STDIO_MESSAGE_BYTES });
  await companion.server.connect(transport);
  let closing;
  const close = () => closing ??= (async () => {
    await companion.server.close();
    await companion.flushAudit();
  })();
  const protocolError = transport.onerror;
  transport.onerror = error => {
    protocolError?.(error);
    // The SDK pauses stdin on a buffer overflow. Destroy the owned pipe too so
    // a sender blocked on a large write is notified immediately and the native
    // tunnel can restart a clean MCP process rather than retain a stuck pipe.
    process.stderr.write('Meridian private MCP transport rejected an invalid or oversized message.\n');
    process.exitCode = 1;
    process.stdin.destroy();
    void close();
  };
  process.stdin.once('end', () => { void close(); });
  process.once('SIGTERM', () => { void close(); });
  process.once('SIGINT', () => { void close(); });
  return { ...companion, transport, close };
}

if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
  const args = process.argv.slice(2);
  if (args.length !== 2 || args[0] !== '--config') {
    process.stderr.write('Usage: node src/stdio.mjs --config <private-config.json>\n');
    process.exitCode = 1;
  } else {
    startStdioFromConfig(args[1]).catch(() => {
      process.stderr.write('Meridian private MCP startup failed. Check local consent, shared projects, and configuration.\n');
      process.exitCode = 1;
    });
  }
}
