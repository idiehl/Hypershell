import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createDemoProject } from '../src/core/demo.ts';
import { buildPublication, readPublication } from '../src/publication.ts';

// Run after npm run build. Node 24 supplies native TypeScript type stripping.
const root = fileURLToPath(new URL('../', import.meta.url));
const destination = path.join(root, 'examples');
const [viewerJs, sceneCss] = await Promise.all([
  readFile(path.join(root, 'dist', 'viewer.js'), 'utf8'),
  readFile(path.join(root, 'dist', 'scene.css'), 'utf8'),
]);
const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-examples-'));
let host;
let hostLog = '';
let launchError;

async function freePort() {
  const server = createServer();
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  const port = server.address().port;
  await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  return port;
}

async function checked(response, description) {
  if (!response.ok) throw new Error(`${description} failed (${response.status}): ${await response.text()}`);
  return response;
}

try {
  const hostOutput = path.join(temporary, 'host');
  const build = spawnSync('dotnet', [
    'build', path.join(root, 'src', 'host', 'Meridian.Host.csproj'),
    '--artifacts-path', path.join(temporary, 'build'), '--output', hostOutput,
    '--nologo', '--verbosity', 'quiet',
  ], { cwd: root, encoding: 'utf8', windowsHide: true, timeout: 120_000 });
  if (build.status !== 0) throw new Error(`Isolated example host build failed.\n${build.stdout ?? ''}\n${build.stderr ?? ''}\n${build.error?.message ?? ''}`);
  const base = `http://127.0.0.1:${await freePort()}`;
  host = spawn('dotnet', [path.join(hostOutput, 'Meridian.Host.dll'), '--data-root', path.join(temporary, 'data'), '--urls', base], {
    cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
  });
  host.on('error', error => { launchError = error; });
  for (const output of [host.stdout, host.stderr]) output.on('data', chunk => { hostLog = (hostLog + chunk).slice(-12_000); });
  let healthy = false;
  for (let attempt = 0; attempt < 200; attempt++) {
    if (launchError || host.exitCode !== null) throw new Error(`The isolated example host failed to start: ${launchError?.message ?? hostLog}`);
    try {
      const status = await fetch(base + '/__meridian_status', { signal: AbortSignal.timeout(1000) });
      if (status.ok && status.headers.get('X-Atlas-Meridian') === 'true') { healthy = true; break; }
    } catch { /* This child is still starting. */ }
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  if (!healthy) throw new Error(`The isolated example host did not become ready.\n${hostLog}`);

  const project = createDemoProject();
  const exported = await checked(await fetch(base + '/api/export', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(project),
  }), 'Example project export');
  const packageBytes = new Uint8Array(await exported.arrayBuffer());
  assert.equal(Buffer.from(packageBytes.subarray(0, 2)).toString(), 'PK', 'The editable example must be a ZIP package.');
  const form = new FormData();
  form.append('file', new Blob([packageBytes], { type: 'application/octet-stream' }), 'first-connections.meridian');
  const imported = await checked(await fetch(base + '/api/import', { method: 'POST', body: form }), 'Example project verification');
  assert.deepEqual(await imported.json(), JSON.parse(JSON.stringify(project)), 'Project ZIP must preserve all authored content and original attachment bytes.');
  const published = buildPublication(project, viewerJs, sceneCss);
  const fork = readPublication(published);
  assert.equal(fork.canvases.length, project.canvases.length);
  assert.equal(fork.nodes.length, project.nodes.length);
  assert.notEqual(fork.id, project.id, 'Importing a publication must create an independent project.');

  const embed = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Embed example · Atlas Meridian</title>
<style>body{margin:0;background:#0b1420;color:#dbe7f3;font:15px/1.6 system-ui,sans-serif}main{max-width:1240px;margin:0 auto;padding:28px 20px}h1{font-size:25px;margin:0 0 8px}p{color:#9db1c7;max-width:76ch}a{color:#72e0c8}iframe{display:block;width:100%;height:680px;border:1px solid #30455c;border-radius:12px;background:#101d2c}code{color:#9decd9;background:#172738;padding:2px 5px;border-radius:4px}pre{overflow:auto;padding:14px;background:#172738;border-radius:8px;font-size:12px}pre code{padding:0}@media(max-width:600px){main{padding:16px 10px}iframe{height:720px}h1{font-size:20px}}</style></head>
<body><main><h1>A Meridian canvas, embedded in a website.</h1><p>This page embeds the portable published HTML below. Open Logic lab, then press Run to try its controls. Its project and original attachments remain available separately.</p>
<p><a href="first-connections.html">Open the standalone publication</a> · <a href="first-connections.meridian" download>Download the editable project</a></p>
<iframe src="first-connections.html" title="Atlas Meridian — First connections" loading="lazy" allowfullscreen></iframe>
<p>Keep the published HTML next to this page, or update the <code>src</code> to wherever you host it. The publication includes its viewer and media.</p>
<pre><code>&lt;iframe src="first-connections.html" title="Meridian canvas" width="100%" height="680" style="border:0" allowfullscreen&gt;&lt;/iframe&gt;</code></pre>
</main></body></html>`;
  await mkdir(destination, { recursive: true });
  await writeFile(path.join(destination, 'first-connections.meridian'), packageBytes);
  await writeFile(path.join(destination, 'first-connections.html'), published, 'utf8');
  await writeFile(path.join(destination, 'embed-example.html'), embed, 'utf8');
  console.log(`Generated and verified ${project.canvases.length} canvases, ${project.nodes.length} nodes, and ${project.assets.length} attachments.`);
  console.log(`examples/first-connections.meridian (${packageBytes.byteLength} bytes)`);
  console.log(`examples/first-connections.html (${Buffer.byteLength(published)} bytes)`);
  console.log(`examples/embed-example.html (${Buffer.byteLength(embed)} bytes)`);
} finally {
  if (host && host.exitCode === null && host.pid) {
    const exited = once(host, 'exit');
    host.kill();
    await exited;
  }
  // The only recursive removal is this script's own verified mkdtemp directory.
  const resolved = path.resolve(temporary);
  if (!resolved.startsWith(path.resolve(tmpdir()) + path.sep) || !path.basename(resolved).startsWith('meridian-examples-')) throw new Error('Refusing to clean an unexpected temporary directory.');
  await rm(resolved, { recursive: true, force: true });
}
