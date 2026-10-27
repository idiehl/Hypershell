import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createModule } from '../src/core/modules.ts';
import type { MeridianProject } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));

test('grid metadata and addressed cell references remain validated across host storage boundaries', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-grid-tests-'));
  const buildRoot = path.join(temporary, 'build'), dataRoot = path.join(temporary, 'data');
  let host: ChildProcess | undefined, logs = '', base = '';
  const json = (method: string, route: string, body?: unknown) => fetch(base + route, { method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  const read = async (route: string) => {
    const response = await json('GET', route); assert.equal(response.status, 200, await response.clone().text()); return response.json();
  };
  const create = async (project = fixture().project): Promise<MeridianProject> => {
    const response = await json('POST', '/api/projects', project); assert.equal(response.status, 201, await response.clone().text()); return response.json();
  };
  try {
    const intermediate = path.join(temporary, 'obj') + path.sep;
    const build = spawnSync('dotnet', ['build', path.join(root, 'src/host/Meridian.Host.csproj'), '--nologo', '--output', buildRoot,
      `-p:MSBuildProjectExtensionsPath=${intermediate}`, `-p:IntermediateOutputPath=${intermediate}`], { cwd: root, encoding: 'utf8', timeout: 60_000, windowsHide: true });
    assert.equal(build.status, 0, `${build.stdout}\n${build.stderr}`);
    base = `http://127.0.0.1:${await freePort()}`;
    host = spawn('dotnet', [path.join(buildRoot, 'Meridian.Host.dll'), '--data-root', dataRoot, '--urls', base], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    host.stdout!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    host.stderr!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    let started = false;
    for (let attempt = 0; attempt < 200; attempt++) {
      if (host.exitCode !== null) assert.fail(`Host exited during startup: ${logs}`);
      try { if ((await fetch(base + '/__meridian_status')).ok) { started = true; break; } } catch { /* Listener is starting. */ }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(started, `Host did not start: ${logs}`);

    await t.test('spreadsheet cells, names, ordinary-node grids and all child arrangements survive snapshots, save and native package', async () => {
      const f = fixture();
      const arrangements = ['free', 'grid', 'row', 'column', 'radial', 'stack'] as const;
      f.project.nodes[0].grid = { columns: 1, rows: 512, cellWidth: 36, cellHeight: 10000, gap: 1000, showHeaders: false };
      for (const [index, node] of f.project.nodes.filter(node => node.kind !== 'canvas').entries()) node.childArrangement = arrangements[index % arrangements.length];
      const committed = await create(commitSnapshots(f.project));
      assert.deepEqual(committed.nodes, f.project.nodes);
      for (const node of committed.nodes) assert.deepEqual(committed.snapshots[node.id].node, node);
      assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      const exported = await json('POST', '/api/export', committed); assert.equal(exported.status, 200, await exported.clone().text());
      const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'grid.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body }); assert.equal(imported.status, 200, await imported.clone().text());
      assert.deepEqual(await imported.json(), committed);
      assert.ok((await read('/__meridian_status')).capabilities.includes('grid-cells-v1'));
    });

    await t.test('malformed grid, cell and reference fields are rejected for both live nodes and saved snapshots without changing the saved revision', async () => {
      const f = fixture(), committed = await create(commitSnapshots(f.project));
      const changes: Array<[string, string, (node: any) => void]> = [
        ['missing grid dimension', f.sheetId, node => { delete node.grid.columns; }],
        ['unknown grid key', f.sheetId, node => { node.grid.script = 'run'; }],
        ['null grid', f.sheetId, node => { node.grid = null; }],
        ['fractional grid count', f.sheetId, node => { node.grid.rows = 2.5; }],
        ['zero grid count', f.sheetId, node => { node.grid.columns = 0; }],
        ['large grid axis', f.sheetId, node => { node.grid.columns = 513; }],
        ['large grid product', f.sheetId, node => { node.grid.columns = 512; node.grid.rows = 512; }],
        ['small cell width', f.sheetId, node => { node.grid.cellWidth = 35; }],
        ['large cell height', f.sheetId, node => { node.grid.cellHeight = 10001; }],
        ['string dimension', f.sheetId, node => { node.grid.cellWidth = '180'; }],
        ['negative gap', f.sheetId, node => { node.grid.gap = -1; }],
        ['large gap', f.sheetId, node => { node.grid.gap = 1001; }],
        ['numeric headers', f.sheetId, node => { node.grid.showHeaders = 1; }],
        ['unknown arrangement', f.sheetId, node => { node.childArrangement = 'orbit'; }],
        ['null arrangement', f.sheetId, node => { node.childArrangement = null; }],
        ['null cell', f.priceId, node => { node.cell = null; }],
        ['unknown cell key', f.priceId, node => { node.cell.span = 2; }],
        ['missing cell row', f.priceId, node => { delete node.cell.row; }],
        ['zero cell column', f.priceId, node => { node.cell.column = 0; }],
        ['large cell row', f.priceId, node => { node.cell.row = 513; }],
        ['fractional cell column', f.priceId, node => { node.cell.column = 1.2; }],
        ['blank name', f.priceId, node => { node.cell.name = ''; }],
        ['untrimmed name', f.priceId, node => { node.cell.name = ' Price'; }],
        ['numeric name', f.priceId, node => { node.cell.name = '3Price'; }],
        ['reserved address name', f.priceId, node => { node.cell.name = 'A1'; }],
        ['reserved large address name', f.priceId, node => { node.cell.name = 'A99999999999999999999'; }],
        ['long name', f.priceId, node => { node.cell.name = 'P'.repeat(65); }],
        ['null target cell', f.referenceId, node => { node.targetCell = null; }],
        ['blank target cell', f.referenceId, node => { node.targetCell = ''; }],
        ['untrimmed target', f.referenceId, node => { node.targetCell = ' A1'; }],
        ['large target column', f.referenceId, node => { node.targetCell = 'SS1'; }],
        ['large target row', f.referenceId, node => { node.targetCell = 'A513'; }],
        ['long target', f.referenceId, node => { node.targetCell = 'P'.repeat(65); }],
        ['target on wrong kind', f.referenceId, node => { node.kind = 'text'; }],
        ['target without parent reference', f.referenceId, node => { delete node.targetId; }],
      ];
      for (const [label, id, change] of changes) for (const location of ['live', 'snapshot']) {
        const invalid: any = structuredClone(committed);
        change(location === 'live' ? invalid.nodes.find((node: any) => node.id === id) : invalid.snapshots[id].node);
        const response = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision });
        assert.equal(response.status, 400, `${location} ${label}: ${await response.clone().text()}`);
      }
      const rootCell: any = structuredClone(committed); rootCell.nodes[0].cell = { column: 1, row: 1 };
      assert.equal((await json('POST', '/api/export', rootCell)).status, 400, 'Canvas root cannot be a child cell');
      assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
    });

    await t.test('live cells must have unique coordinates and case-insensitive names within their immediate parent and fit explicit grid bounds', async () => {
      const f = fixture(), committed = await create(commitSnapshots(f.project));
      const changes: Array<[string, (project: any) => void]> = [
        ['duplicate coordinates', project => { project.nodes.find((node: any) => node.id === f.quantityId).cell = { column: 1, row: 1, name: 'Quantity' }; }],
        ['duplicate name', project => { project.nodes.find((node: any) => node.id === f.quantityId).cell.name = 'pRiCe'; }],
        ['outside grid column', project => { project.nodes.find((node: any) => node.id === f.quantityId).cell.column = 4; }],
        ['outside grid row', project => { project.nodes.find((node: any) => node.id === f.quantityId).cell.row = 3; }],
        ['shrunken grid', project => { project.nodes.find((node: any) => node.id === f.sheetId).grid.columns = 1; }],
      ];
      for (const [label, change] of changes) {
        const invalid = structuredClone(committed); change(invalid);
        assert.equal((await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision })).status, 400, label);
        assert.deepEqual(await read(`/api/projects/${committed.id}`), committed, label);
      }
      const edited = structuredClone(committed); edited.nodes.find(node => node.id === f.priceId)!.cell = { column: 1, row: 1, name: 'NewPrice' };
      const response = await json('PUT', `/api/projects/${committed.id}`, { project: edited, expectedRevision: committed.revision });
      assert.equal(response.status, 200, await response.clone().text());
      const saved = await response.json(); assert.equal(saved.snapshots[f.priceId].node.cell.name, 'Price', 'Old observations retain their own name');
    });

    await t.test('A1 and named live references use addressed child value types and allow missing cells while rejecting mismatched numeric channels', async () => {
      for (const target of ['a1', 'PRICE', 'C1', 'Absent', 'SR512']) {
        const f = fixture(); f.project.nodes.find(node => node.id === f.referenceId)!.targetCell = target;
        assert.equal((await json('POST', '/api/projects', f.project)).status, 201, target);
      }
      const wrong = fixture(); wrong.project.nodes.find(node => node.id === wrong.priceId)!.kind = 'input'; wrong.project.nodes.find(node => node.id === wrong.priceId)!.value = true;
      assert.equal((await json('POST', '/api/projects', wrong.project)).status, 400, 'Boolean cell cannot supply a numeric connection');
      const feedback = fixture(), output = feedback.project.nodes.find(node => node.id === feedback.outputId)!;
      output.cell = { column: 1, row: 2, name: 'Total' };
      feedback.project.nodes.find(node => node.id === feedback.referenceId)!.targetCell = 'total';
      assert.equal((await json('POST', '/api/projects', feedback.project)).status, 400, 'Addressed child contributes the dependency edge for feedback detection');
    });

    await t.test('cross-canvas cell type resolution uses committed parent and child observations, never unsaved live cell types or renamed live cells', async () => {
      const f = fixture(), other = addCanvas(f.project, 'Consumer'), reference = addNode(f.project, other.id, 'reference');
      reference.targetId = f.sheetId; reference.targetCell = 'Price';
      const result = addNode(f.project, other.id, 'output'); result.valueType = 'number';
      f.project.links.push({ id: crypto.randomUUID(), canvasId: other.id, sourceId: reference.id, targetId: result.id, kind: 'numeric', relation: 'causal' });
      let candidate = commitSnapshots(f.project);
      const live = candidate.nodes.find(node => node.id === f.priceId)!; live.kind = 'input'; live.value = true; live.cell!.name = 'Renamed';
      candidate.links = candidate.links.filter(link => link.canvasId === other.id);
      assert.equal((await json('POST', '/api/projects', candidate)).status, 201, 'Saved numeric cell remains numeric despite unsaved live Boolean replacement');
      candidate = structuredClone(candidate); candidate.id = crypto.randomUUID();
      candidate.snapshots[f.priceId].value = true; candidate.snapshots[f.priceId].node.kind = 'input'; candidate.snapshots[f.priceId].node.value = true;
      assert.equal((await json('POST', '/api/projects', candidate)).status, 400, 'Saved Boolean cell contradicts the numeric consumer');
      delete candidate.snapshots[f.priceId]; candidate.id = crypto.randomUUID();
      assert.equal((await json('POST', '/api/projects', candidate)).status, 201, 'Unavailable cell observation remains an unresolved reference');
    });

    await t.test('grid assemblies round-trip as inert modules; invalid imports and named-cell feedback remain rejected', async () => {
      const f = fixture(), stored = createModule(f.project, f.groupId, 'Grid assembly');
      const response = await json('POST', '/api/modules', { module: stored }); assert.equal(response.status, 201, await response.clone().text());
      assert.deepEqual(await read(`/api/modules/${stored.id}`), stored);
      for (const change of [
        (project: any) => { project.nodes.find((node: any) => node.kind === 'spreadsheet').grid.rows = 513; },
        (project: any) => { const numbers = project.nodes.filter((node: any) => node.kind === 'number' && node.cell); numbers[1].cell.name = numbers[0].cell.name.toUpperCase(); },
        (project: any) => { const reference = project.nodes.find((node: any) => node.kind === 'reference'); reference.targetCell = 'Total'; },
      ]) {
        const invalid = structuredClone(stored); invalid.id = crypto.randomUUID(); change(invalid.project);
        assert.equal((await json('POST', '/api/modules', { module: invalid })).status, 400);
      }
      assert.equal((await read('/api/modules')).length, 1);
    });

    await t.test('new ordered variants, Window metadata, X:Y references and spacing survive native storage with strict live/snapshot validation', async () => {
      const f = fixture(), sheet = f.project.nodes.find(node => node.id === f.sheetId)!;
      sheet.containerVariant = 'window';
      sheet.grid = { ...sheet.grid!, fillOrder: 'column', windowColumns: 2, windowRows: 1, scrollDirection: 'horizontal' };
      sheet.style = { padding: 5, contentGap: 0, textMediaGap: 12, childPadding: 40, borderStyle: 'double', borderWidth: 3, shape: 'hexagon' };
      f.project.nodes.find(node => node.id === f.referenceId)!.targetCell = '1:1';
      const committed = await create(commitSnapshots(f.project));
      assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      assert.equal(committed.snapshots[f.sheetId].node.grid!.windowRows, 1);
      for (const variant of ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'gallery', 'window', 'spatial']) {
        const changed = structuredClone(committed); changed.id = crypto.randomUUID(); changed.revision = 0;
        changed.nodes.find(node => node.id === f.sheetId)!.containerVariant = variant as any;
        changed.snapshots[f.sheetId].node.containerVariant = variant as any;
        assert.equal((await json('POST', '/api/projects', changed)).status, 201, variant);
      }
      const mutations: Array<[string, (node: any) => void]> = [
        ['missing window axes', node => { delete node.grid.windowRows; }],
        ['zero viewport', node => { node.grid.windowRows = 0; }],
        ['oversized viewport', node => { node.grid.windowColumns = 513; }],
        ['fractional viewport', node => { node.grid.windowRows = 1.5; }],
        ['null viewport', node => { node.grid.windowRows = null; }],
        ['unsupported traversal', node => { node.grid.fillOrder = 'diagonal'; }],
        ['unsupported direction', node => { node.grid.scrollDirection = 'both'; }],
        ['injected grid property', node => { node.grid.script = 'alert(1)'; }],
        ['unsupported border', node => { node.style.borderStyle = 'url(x)'; }],
        ['negative padding', node => { node.style.padding = -1; }],
        ['oversized child inset', node => { node.style.childPadding = 1001; }],
        ['typed text gap', node => { node.style.textMediaGap = '5'; }],
      ];
      for (const [label, mutate] of mutations) for (const saved of [false, true]) {
        const invalid = structuredClone(committed), node = saved ? invalid.snapshots[f.sheetId].node : invalid.nodes.find(node => node.id === f.sheetId)!;
        mutate(node);
        const response = await json('PUT', `/api/projects/${committed.id}`, { project: invalid, expectedRevision: committed.revision });
        assert.equal(response.status, 400, `${label} ${saved}: ${await response.clone().text()}`);
        assert.deepEqual(await read(`/api/projects/${committed.id}`), committed);
      }
      for (const address of ['0:1', '1:0', '1:513', '513:1', '01:1', '1.5:1']) {
        const invalid = structuredClone(committed); invalid.nodes.find(node => node.id === f.referenceId)!.targetCell = address;
        assert.equal((await json('POST', '/api/export', invalid)).status, 400, address);
      }
      const exported = await json('POST', '/api/export', committed); assert.equal(exported.status, 200, await exported.clone().text());
      const body = new FormData(); body.append('file', new Blob([await exported.arrayBuffer()]), 'window.meridian');
      const imported = await fetch(base + '/api/import', { method: 'POST', body }); assert.equal(imported.status, 200, await imported.clone().text());
      assert.deepEqual(await imported.json(), committed);
    });

    await t.test('authoring and recovery validate grids before writes while retaining exact revision and checkpoint guards', async () => {
      const f = fixture(), committed = await create(commitSnapshots(f.project)), invalid = structuredClone(committed);
      invalid.nodes.find(node => node.id === f.sheetId)!.grid!.rows = 513;
      const body = { project: invalid, expectedRevision: committed.revision, expectedRecoverySavedAt: null, checkpointName: 'Grid edit' };
      assert.equal((await json('POST', `/api/projects/${committed.id}/authoring`, body)).status, 400);
      assert.equal((await json('PUT', `/api/projects/${committed.id}/recovery`, { project: invalid, baseRevision: committed.revision, expectedSavedAt: null })).status, 400);
      assert.deepEqual(await read(`/api/projects/${committed.id}`), committed); assert.deepEqual(await read(`/api/projects/${committed.id}/checkpoints`), []);
      const valid = structuredClone(committed); valid.nodes.find(node => node.id === f.sheetId)!.grid!.gap = 24;
      const response = await json('POST', `/api/projects/${committed.id}/authoring`, { ...body, project: valid });
      assert.equal(response.status, 200, await response.clone().text());
      assert.deepEqual(await response.json(), { ...valid, revision: committed.revision + 1 });
      const checkpoints = await read(`/api/projects/${committed.id}/checkpoints`); assert.equal(checkpoints.length, 1);
      assert.deepEqual(await read(`/api/projects/${committed.id}/checkpoints/${checkpoints[0].id}`), committed);
      assert.equal((await json('POST', `/api/projects/${committed.id}/authoring`, { ...body, project: valid })).status, 409);
    });
  } finally {
    if (host && host.exitCode === null) { const exit = once(host, 'exit'); host.kill(); await exit; }
    assert.ok(path.resolve(temporary).startsWith(path.resolve(tmpdir()) + path.sep + 'meridian-grid-tests-'));
    await rm(temporary, { recursive: true, force: true });
  }
});

async function freePort(): Promise<number> {
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const address = server.address(); assert.ok(address && typeof address !== 'string');
  const port = address.port;
  await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  return port;
}

function fixture() {
  const project = newProject('Grid host fixture'), canvasId = project.entryCanvasId;
  const group = addNode(project, canvasId, 'module', 'Grid assembly');
  group.rules = { nodeKinds: ['spreadsheet', 'number', 'input', 'text', 'document', 'output', 'reference'], linkKinds: ['numeric', 'visual'] };
  const sheet = addNode(project, canvasId, 'spreadsheet', 'Costs');
  Object.assign(sheet, { parentId: group.id, childLayout: 'contained', childArrangement: 'grid', grid: { columns: 3, rows: 2, cellWidth: 180, cellHeight: 100, gap: 12, showHeaders: true } });
  const price = addNode(project, canvasId, 'number', 'Price'); Object.assign(price, { parentId: sheet.id, value: 12, cell: { column: 1, row: 1, name: 'Price' } });
  const quantity = addNode(project, canvasId, 'number', 'Quantity'); Object.assign(quantity, { parentId: sheet.id, value: 3, cell: { column: 2, row: 1, name: 'Quantity' } });
  const note = addNode(project, canvasId, 'text', 'Source'); Object.assign(note, { parentId: sheet.id, cell: { column: 3, row: 1 }, text: 'Ordinary rich nodes occupy cells.' });
  const output = addNode(project, canvasId, 'output', 'Total'); Object.assign(output, { parentId: sheet.id, valueType: 'number', cell: { column: 1, row: 2, name: 'Total' } });
  const reference = addNode(project, canvasId, 'reference', 'Read price'); Object.assign(reference, { parentId: sheet.id, targetId: sheet.id, targetCell: 'A1', cell: { column: 2, row: 2, name: 'ReadPrice' } });
  const document = addNode(project, canvasId, 'document', 'Evidence'); Object.assign(document, { parentId: sheet.id, cell: { column: 3, row: 2 }, assetId: 'grid-evidence' });
  project.assets.push({ id: 'grid-evidence', name: 'evidence.txt', mime: 'text/plain', base64: btoa('Cell contents preserve attachments.') });
  const nested = addNode(project, canvasId, 'text', 'Nested child'); Object.assign(nested, { parentId: price.id, cell: { column: 512, row: 512, name: 'Price' }, collapsed: true });
  project.links.push({ id: crypto.randomUUID(), canvasId, sourceId: reference.id, targetId: output.id, kind: 'numeric', relation: 'causal' });
  return { project, groupId: group.id, sheetId: sheet.id, priceId: price.id, quantityId: quantity.id, referenceId: reference.id, outputId: output.id };
}
