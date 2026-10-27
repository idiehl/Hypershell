/**
 * Export and verify the editable atlas using only stateless host conversion APIs.
 * No save, installation, recovery write or managed-project mutation is performed.
 * Run: node scripts/mythology/verify-expanded-native.mjs
 * Optional: --host http://127.0.0.1:5240 --project examples/visual-authoring/visual-atlas.project.json
 *           --output examples/visual-authoring/visual-atlas.meridian --report .cache/expanded-atlas/native-roundtrip.json
 */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, rename } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { isDeepStrictEqual } from 'node:util';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = fileURLToPath(new URL('../../', import.meta.url));
const defaults = { host: process.env.MERIDIAN_URL ?? 'http://127.0.0.1:5240', project: 'examples/visual-authoring/visual-atlas.project.json', output: 'examples/visual-authoring/visual-atlas.meridian', report: '.cache/expanded-atlas/native-roundtrip.json' };
const options = { ...defaults };
const args = process.argv.slice(2);
if (args.includes('--help')) {
  console.log('node scripts/mythology/verify-expanded-native.mjs [--host URL] [--project JSON] [--output MERIDIAN] [--report JSON]');
  process.exit(0);
}
for (let i = 0; i < args.length; i += 2) {
  const key = args[i].slice(2);
  if (!args[i].startsWith('--') || !Object.hasOwn(defaults, key) || !args[i + 1] || args[i + 1].startsWith('--')) throw new Error(`Unknown or incomplete option ${args[i]}`);
  options[key] = args[i + 1];
}
const base = new URL(options.host);
assert.ok(['http:', 'https:'].includes(base.protocol) && ['localhost', '127.0.0.1', '[::1]'].includes(base.hostname), 'Use a loopback Meridian host.');
assert.ok(!base.username && !base.password && base.pathname === '/' && !base.search && !base.hash, 'Host must be a plain loopback origin.');
const input = path.resolve(root, options.project), output = path.resolve(root, options.output), reportFile = path.resolve(root, options.report);
const within = (directory, file) => { const relative = path.relative(path.join(root, directory), file); return relative !== '' && !relative.startsWith('..') && !path.isAbsolute(relative); };
assert.ok(within('examples', output) && output.endsWith('.meridian'), 'Archive output must be a .meridian file within examples.');
assert.ok(within('.cache', reportFile) || within('output', reportFile), 'Report must be within .cache or output.');
assert.notEqual(input.toLowerCase(), output.toLowerCase());
assert.notEqual(input.toLowerCase(), reportFile.toLowerCase());
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const without = (value, field) => Object.fromEntries(Object.entries(value).filter(([key]) => key !== field));
const report = { verifiedAt: new Date().toISOString(), host: base.origin, source: input, output, status: 'running', checks: [], requests: [], assetHashes: [], managedWrites: 0 };
async function request(route, init = {}) {
  const method = init.method ?? 'GET';
  const safeRead = method === 'GET' && (route === '/__meridian_status' || route === '/api/projects' || /^\/api\/projects\/[A-Za-z0-9_-]+(?:\/recovery)?$/.test(route));
  const conversion = method === 'POST' && ['/api/export', '/api/import'].includes(route);
  assert.ok(safeRead || conversion, `Refusing non-conversion mutation: ${method} ${route}`);
  const response = await fetch(new URL(route, base), { ...init, redirect: 'error', signal: AbortSignal.timeout(60_000) });
  report.requests.push({ method, route, status: response.status });
  const noRecovery = method === 'GET' && route.endsWith('/recovery') && response.status === 404;
  if (!response.ok && !noRecovery) throw new Error(`${method} ${route}: HTTP ${response.status}: ${(await response.text()).slice(0, 500)}`);
  return response;
}
async function libraryState() {
  const list = await (await request('/api/projects')).json();
  const state = [];
  for (const item of list) {
    assert.match(item.id, /^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$/);
    const project = await (await request(`/api/projects/${item.id}`)).text();
    const recoveryResponse = await request(`/api/projects/${item.id}/recovery`);
    const recovery = await recoveryResponse.text();
    state.push({ id: item.id, revision: item.revision, projectSha256: sha(project), recoveryStatus: recoveryResponse.status, recoverySha256: sha(recovery) });
  }
  return { list, state };
}
function check(name, action) { action(); report.checks.push(name); }
try {
  const sourceBytes = await readFile(input);
  const source = JSON.parse(sourceBytes.toString('utf8'));
  report.sourceSha256 = sha(sourceBytes);
  report.projectId = source.id; report.revision = source.revision;
  report.counts = Object.fromEntries(['canvases', 'nodes', 'links', 'assets'].map(key => [key, source[key].length]));
  report.counts.snapshots = Object.keys(source.snapshots).length;
  const status = await request('/__meridian_status');
  assert.equal(status.headers.get('X-Atlas-Meridian'), 'true', 'Host identity header missing.');
  assert.equal((await status.json()).application, 'atlas-meridian', 'Unexpected host application.');
  const before = await libraryState();
  const exported = await request('/api/export', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: sourceBytes });
  assert.match(exported.headers.get('Content-Type') ?? '', /^application\/octet-stream/);
  const archive = Buffer.from(await exported.arrayBuffer());
  check('Native export is a ZIP package', () => assert.equal(archive.subarray(0, 4).toString('hex'), '504b0304'));
  const form = new FormData();
  form.append('file', new Blob([archive], { type: 'application/octet-stream' }), path.basename(output));
  const imported = await (await request('/api/import', { method: 'POST', body: form })).json();
  check('Project identity, revision and entry canvas are preserved', () => {
    for (const field of ['id', 'schemaVersion', 'revision', 'entryCanvasId', 'title']) assert.equal(imported[field], source[field], `Changed ${field}`);
  });
  for (const [key, description] of [['canvases', 'Canvas identities and appearance'], ['nodes', 'Node identities, containment, references, geometry and authored defaults'], ['links', 'Relationship identities, endpoints, routing and styles'], ['snapshots', 'Saved reference content, values, identities and timestamps']])
    check(`${description} are preserved`, () => assert.ok(isDeepStrictEqual(imported[key], source[key]), `Changed ${key}`));
  check('Every attachment and snapshot attachment retains exact bytes and metadata', () => {
    const compare = (original, copy, location) => {
      assert.ok(copy, `Missing asset at ${location}`);
      assert.ok(isDeepStrictEqual(without(original, 'base64'), without(copy, 'base64')), `Changed asset metadata at ${location}`);
      const originalBytes = Buffer.from(original.base64, 'base64'), copiedBytes = Buffer.from(copy.base64, 'base64');
      assert.ok(originalBytes.equals(copiedBytes), `Changed asset bytes at ${location}`);
      report.assetHashes.push({ location, id: original.id, name: original.name, bytes: originalBytes.length, sha256: sha(originalBytes) });
    };
    assert.equal(imported.assets.length, source.assets.length);
    source.assets.forEach((asset, index) => compare(asset, imported.assets[index], `assets[${index}]`));
    for (const [id, snapshot] of Object.entries(source.snapshots)) if (snapshot.asset) compare(snapshot.asset, imported.snapshots[id]?.asset, `snapshots.${id}.asset`);
  });
  check('Complete imported project equals the authored source', () => assert.ok(isDeepStrictEqual(imported, source), 'Native roundtrip changed the project.'));
  const after = await libraryState();
  check('Managed library identities, revisions, saved projects and recovery drafts remain unchanged', () => assert.ok(isDeepStrictEqual(after, before), 'Live library changed during conversion; inspect concurrent activity.'));
  report.managedLibrary = after.state;
  const finalSourceBytes = await readFile(input);
  check('Source JSON remains byte-for-byte unchanged during conversion', () => assert.equal(sha(finalSourceBytes), report.sourceSha256, 'Source regenerated during verification; rerun on the final bundle.'));
  await mkdir(path.dirname(output), { recursive: true });
  const temporary = `${output}.verified-${process.pid}.tmp`;
  await writeFile(temporary, archive);
  await rename(temporary, output);
  const diskArchive = await readFile(output);
  check('Written editable artifact matches verified archive bytes', () => assert.equal(sha(diskArchive), sha(archive), 'Archive disk bytes changed.'));
  report.packageBytes = archive.length; report.packageSha256 = sha(archive);
  report.distinctAssetHashes = new Set(report.assetHashes.map(asset => asset.sha256)).size;
  report.status = 'passed';
} catch (error) {
  report.status = 'failed'; report.error = String(error.message ?? error).slice(0, 1200); process.exitCode = 1;
}
await mkdir(path.dirname(reportFile), { recursive: true });
await writeFile(reportFile, JSON.stringify(report, null, 2));
console.log(JSON.stringify({ status: report.status, projectId: report.projectId, revision: report.revision, counts: report.counts, checks: report.checks.length, packageBytes: report.packageBytes, packageSha256: report.packageSha256, error: report.error, report: reportFile }, null, 2));
