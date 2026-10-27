import test from 'node:test';
import assert from 'node:assert/strict';
import { applyRecipe, inspectProject, AuthoringError, type AuthoringRecipe } from '../src/core/authoring.ts';
import { addNode, newProject } from '../src/core/model.ts';
import { validateProject } from '../src/core/rules.ts';
import { evaluate } from '../src/core/engine.ts';
import { buildPublication, readPublication } from '../src/publication.ts';

test('numeric recipes and publication retain explicit channels, ports, and passive routing', () => {
  const base = newProject('Typed composition');
  const result = applyRecipe(base, { version: 1, nodes: [
    {key:'a',canvas:base.entryCanvasId,kind:'number',value:3}, {key:'b',canvas:base.entryCanvasId,kind:'number',value:4},
    {key:'sum',canvas:base.entryCanvasId,kind:'joint',operation:'ADD'}, {key:'route',canvas:base.entryCanvasId,kind:'junction',valueType:'number'},
    {key:'result',canvas:base.entryCanvasId,kind:'output',valueType:'number'},
  ], links: [
    {key:'a-sum',source:'a',target:'sum',kind:'numeric',targetPort:0},
    {key:'b-sum',source:'b',target:'sum',kind:'numeric',targetPort:1},
    {key:'sum-route',source:'sum',target:'route',kind:'numeric'},
    {key:'route-result',source:'route',target:'result',kind:'numeric'},
  ] });
  const published = readPublication(buildPublication(result.project,'',''));
  assert.equal(evaluate(published).values[published.nodes.find(n=>n.kind==='output')!.id],7);
  assert.equal(result.report.canvases[0].links.numeric,4);
  assert.equal(published.nodes.find(n=>n.kind==='junction')?.valueType,'number');
  assert.throws(()=>applyRecipe(base,{version:1,nodes:[{key:'bad',canvas:base.entryCanvasId,kind:'number',value:Infinity}]}),/finite/);
});

test('one batch resolves forward aliases, creates visual hierarchy and preserves its original project', () => {
  const base = newProject('Before'), original = structuredClone(base);
  const recipe: AuthoringRecipe = {
    version: 1, title: 'Visual essay', entryCanvas: 'chapter',
    canvases: [{ key: 'home', existingId: base.entryCanvasId, title: 'Overview', style: { background: '#102030' } }, { key: 'chapter', title: 'Chapter' }],
    nodes: [
      { key: 'child', canvas: 'home', parent: 'group', kind: 'image', asset: 'photo', title: 'Focal image', x: 30, y: 60, width: 240, height: 180, style: { imageFit: 'cover' } },
      { key: 'jump', canvas: 'home', kind: 'canvas-link', target: 'chapter', x: 500, y: 400 },
      { key: 'reference', canvas: 'chapter', kind: 'reference', target: 'child' },
      { key: 'group', canvas: 'home', kind: 'container', width: 400, height: 340, x: 10, y: 10, style: { fill: '#445566', titleSize: 24 } },
    ],
    assets: [{ key: 'photo', name: 'image.png', mime: 'image/png', base64: 'AQID' }],
    links: [{ key: 'path', source: 'group', target: 'jump', label: 'Continue', style: { color: '#AABBCC', width: 3 } }],
  };
  const savedRecipe = structuredClone(recipe), result = applyRecipe(base, recipe);
  assert.deepEqual(base, original); assert.deepEqual(recipe, savedRecipe);
  assert.equal(result.project.id, base.id); assert.equal(result.project.revision, base.revision);
  assert.equal(result.project.title, 'Visual essay'); assert.equal(result.project.entryCanvasId, result.aliases.canvases.chapter);
  assert.equal(result.project.nodes.find(n => n.id === result.aliases.nodes.child)!.parentId, result.aliases.nodes.group);
  assert.equal(result.project.nodes.find(n => n.id === result.aliases.nodes.child)!.assetId, result.aliases.assets.photo);
  assert.equal(result.project.nodes.find(n => n.id === result.aliases.nodes.jump)!.targetId, result.aliases.roots.chapter);
  assert.equal(result.project.nodes.find(n => n.id === result.aliases.nodes.reference)!.targetId, result.aliases.nodes.child);
  assert.equal(result.project.links[0].sourceId, result.aliases.nodes.group);
  assert.equal(result.project.links[0].canvasId, base.entryCanvasId);
  assert.deepEqual(validateProject(result.project), []);
  assert.ok(result.report.warnings.some(w => w.code === 'unsaved-reference'));
});

test('updates preserve IDs, omitted fields and unmentioned style/snapping subfields', () => {
  const base = newProject(), node = addNode(base, base.entryCanvasId, 'text', 'Existing');
  node.text = 'Keep this text'; node.style = { fill: '#112233', fontSize: 14 }; node.snapping = { stickyEdges: false };
  const result = applyRecipe(base, { version: 1, nodes: [{ key: 'edited', existingId: node.id, title: 'Changed', style: { fontSize: 24 }, snapping: { smartAlign: true } }] });
  const changed = result.project.nodes.find(n => n.id === node.id)!;
  assert.equal(result.aliases.nodes.edited, node.id); assert.equal(changed.text, node.text); assert.equal(changed.x, node.x);
  assert.deepEqual(changed.style, { fill: '#112233', fontSize: 24 });
  assert.deepEqual(changed.snapping, { stickyEdges: false, smartAlign: true });
  assert.equal(node.title, 'Existing'); assert.equal(node.style.fontSize, 14);
  assert.throws(() => applyRecipe(base, { version: 1, nodes: [{ key: 'edited', existingId: node.id, kind: 'image' }] }), /cannot change/);
});

test('malformed recipes and late graph failures reject atomically with actionable diagnostics', () => {
  const base = newProject(), before = structuredClone(base);
  const invalid: unknown[] = [
    { version: 9 }, { version: 1, execute: 'alert(1)' }, { version: 1, nodes: null },
    { version: 1, canvases: [{ key: 'same' }, { key: 'same' }] },
    { version: 1, nodes: [{ key: 'n', canvas: base.entryCanvasId, kind: 'text', id: 'override' }] },
    { version: 1, nodes: [{ key: 'n', canvas: base.entryCanvasId, kind: 'text', style: { fill: 'url(https://example.test)' } }] },
    { version: 1, nodes: [{ key: 'n', canvas: base.entryCanvasId, kind: 'text', x: NaN }] },
    { version: 1, nodes: [{ key: 'n', existingId: 'missing' }] },
    { version: 1, nodes: [{ key: 'n', canvas: base.entryCanvasId, kind: 'text', parent: 'missing' }] },
    { version: 1, nodes: [{ key: 'a', canvas: base.entryCanvasId, kind: 'text', parent: 'b' }, { key: 'b', canvas: base.entryCanvasId, kind: 'text', parent: 'a' }] },
    { version: 1, nodes: [{ key: 'group', canvas: base.entryCanvasId, kind: 'container', rules: { nodeKinds: ['input'] } }, { key: 'n', canvas: base.entryCanvasId, kind: 'text', parent: 'group' }] },
    { version: 1, links: [{ key: 'link', source: 'missing', target: 'missing' }] },
    { version: 1, assets: [{ key: 'data', name: 'x', mime: 'text/plain', base64: 'AB==' }] },
  ];
  for (const recipe of invalid) {
    assert.throws(() => applyRecipe(base, recipe as AuthoringRecipe), error => error instanceof AuthoringError && error.diagnostics.length > 0, JSON.stringify(recipe));
    assert.deepEqual(base, before);
  }
});

test('new attachments reuse exact metadata and canonical bytes, including forward node references', () => {
  const base = newProject();
  base.assets.push({ id: 'asset-original', name: 'note.txt', mime: 'text/plain', base64: 'SGk=' });
  const result = applyRecipe(base, {
    version: 1,
    nodes: [{ key: 'n', canvas: base.entryCanvasId, kind: 'document', asset: 'again' }],
    assets: [
      { key: 'again', name: 'note.txt', mime: 'text/plain', base64: 'SGk=' },
      { key: 'also', name: 'note.txt', mime: 'text/plain', base64: 'SGk=' },
      { key: 'different-name', name: 'second.txt', mime: 'text/plain', base64: 'SGk=' },
    ],
  });
  assert.equal(result.project.assets.length, 2);
  assert.equal(result.aliases.assets.again, 'asset-original'); assert.equal(result.aliases.assets.also, 'asset-original');
  assert.notEqual(result.aliases.assets['different-name'], 'asset-original');
  assert.equal(result.project.nodes.find(n => n.id === result.aliases.nodes.n)!.assetId, 'asset-original');
  const update = applyRecipe(base, { version: 1, assets: [
    { key: 'keep-old', name: 'note.txt', mime: 'text/plain', base64: 'SGk=' },
    { key: 'change-original', existingId: 'asset-original', base64: 'TmV3' },
  ] });
  assert.equal(update.project.assets.find(a => a.id === 'asset-original')!.base64, 'TmV3');
  assert.equal(update.project.assets.find(a => a.id === update.aliases.assets['keep-old'])!.base64, 'SGk=');
  assert.notEqual(update.aliases.assets['keep-old'], 'asset-original');
});

test('multi-MiB canonical attachments validate without a regexp stack overflow and retain exact bytes', () => {
  const base = newProject('Large reference index'), before = structuredClone(base);
  const bytes = Buffer.alloc(3_750_001, 0xa5), base64 = bytes.toString('base64');
  assert.ok(base64.length > 5_000_000);
  const result = applyRecipe(base, { version: 1, assets: [{ key: 'index', name: 'reference-index.csv', mime: 'text/csv', base64 }] });
  assert.equal(result.project.assets[0].base64, base64);
  assert.deepEqual(Buffer.from(result.project.assets[0].base64, 'base64'), bytes);
  assert.deepEqual(base, before);
  assert.equal(result.aliases.assets.index, result.project.assets[0].id);
});

test('canonical attachment validation retains empty files, padding rules and strict unused trailing bits', () => {
  const base = newProject(), before = structuredClone(base);
  for (const base64 of ['', 'AA==', 'AAA=', 'AAAA', '+/8=', '////']) {
    const result = applyRecipe(base, { version: 1, assets: [{ key: 'file', name: 'file.bin', mime: 'application/octet-stream', base64 }] });
    assert.equal(result.project.assets[0].base64, base64);
  }
  const malformed = ['A', 'AA', 'AAA', '=', '====', 'AA=A', '=AAA', 'A===', 'AAAA=', 'AA==AAAA', 'AA==\n', 'A A=', 'AA-_', 'AA\u0000=', 'AB==', 'AAB='];
  // A long invalid suffix must also be rejected as an authoring diagnostic, not a native RangeError.
  const large = Buffer.alloc(3_750_001, 0xa5).toString('base64');
  malformed.push(`${large.slice(0, -4)}AA?=`, `${large.slice(0, -4)}AB==`, `${large}====`);
  for (const base64 of malformed) {
    assert.throws(() => applyRecipe(base, { version: 1, assets: [{ key: 'file', name: 'file.bin', mime: 'application/octet-stream', base64 }] }),
      error => error instanceof AuthoringError && error.diagnostics.some(diagnostic => diagnostic.path === 'assets.file.base64' && /canonical base64/.test(diagnostic.message)));
    assert.deepEqual(base, before);
  }
});

test('existing saved snapshot IDs remain valid reference targets', () => {
  const base = newProject();
  base.snapshots.external = { node: { id: 'external', canvasId: 'remote', parentId: 'remote-root', kind: 'text', title: 'Saved note', x: 0, y: 0, width: 100, height: 100 }, value: null, savedAt: '2026-10-04T00:00:00Z' };
  const result = applyRecipe(base, { version: 1, nodes: [{ key: 'ref', canvas: base.entryCanvasId, kind: 'reference', target: 'external' }] });
  assert.equal(result.project.nodes.find(n => n.id === result.aliases.nodes.ref)!.targetId, 'external');
  assert.equal(result.report.warnings.some(w => w.code === 'missing-reference'), false);
});

function layoutProject() {
  const base = newProject(), canvas = base.entryCanvasId;
  return applyRecipe(base, { version: 1, nodes: [
    { key: 'group', canvas, kind: 'container', x: 100, y: 100, width: 300, height: 200 },
    { key: 'a', canvas, parent: 'group', kind: 'text', x: 120, y: 140, width: 40, height: 40 },
    { key: 'b', canvas, parent: 'group', kind: 'text', x: 200, y: 140, width: 40, height: 40 },
    { key: 'peer', canvas, kind: 'text', x: 600, y: 100, width: 150, height: 100 },
  ], links: [
    { key: 'inside', source: 'a', target: 'b', points: [{ x: 180, y: 130 }] },
    { key: 'outside', source: 'b', target: 'peer', points: [{ x: 500, y: 130 }] },
  ] });
}

test('row layout moves a whole descendant group and only its internal route points', () => {
  const initial = layoutProject(), ids = initial.aliases;
  const result = applyRecipe(initial.project, { version: 1, layouts: [{ kind: 'row', nodes: [ids.nodes.group, ids.nodes.peer], x: 0, y: 0, gap: 30 }] });
  const get = (key: string) => result.project.nodes.find(n => n.id === ids.nodes[key])!;
  assert.deepEqual([get('group').x, get('group').y, get('a').x, get('a').y, get('peer').x, get('peer').y], [0, 0, 20, 40, 330, 0]);
  assert.deepEqual(result.project.links.find(l => l.id === ids.links.inside)!.points, [{ x: 80, y: 30 }]);
  assert.deepEqual(result.project.links.find(l => l.id === ids.links.outside)!.points, [{ x: 500, y: 130 }]);
  assert.equal(initial.project.nodes.find(n => n.id === ids.nodes.group)!.x, 100);
});

test('column and grid layouts use real variable group sizes, not fixed card dimensions', () => {
  const base = newProject(), nodes = [
    { key: 'a', canvas: base.entryCanvasId, kind: 'text' as const, width: 100, height: 60 },
    { key: 'b', canvas: base.entryCanvasId, kind: 'text' as const, width: 200, height: 80 },
    { key: 'c', canvas: base.entryCanvasId, kind: 'text' as const, width: 150, height: 100 },
  ];
  const column = applyRecipe(base, { version: 1, nodes, layouts: [{ kind: 'column', nodes: ['a', 'b', 'c'], x: -20, y: 10, gap: 20 }] });
  assert.deepEqual(column.project.nodes.filter(n => n.kind !== 'canvas').map(n => [n.x, n.y]), [[-20, 10], [-20, 90], [-20, 190]]);
  const grid = applyRecipe(base, { version: 1, nodes, layouts: [{ kind: 'grid', nodes: ['a', 'b', 'c'], columns: 2, x: 10, y: 20, gap: 30 }] });
  assert.deepEqual(grid.project.nodes.filter(n => n.kind !== 'canvas').map(n => [n.x, n.y]), [[10, 20], [190, 20], [10, 130]]);
});

test('radial layout centers differently sized items on a circle using degree angles', () => {
  const base = newProject();
  const result = applyRecipe(base, { version: 1, nodes: ['a', 'b', 'c', 'd'].map(key => ({ key, canvas: base.entryCanvasId, kind: 'text', width: 20, height: 20 })), layouts: [{ kind: 'radial', nodes: ['a', 'b', 'c', 'd'], center: { x: 400, y: 300 }, radius: 100 }] });
  const positions = result.project.nodes.filter(n => n.kind !== 'canvas').map(n => [n.x, n.y]);
  for (const [i, expected] of [[390, 190], [490, 290], [390, 390], [290, 290]].entries()) for (const axis of [0, 1]) assert.ok(Math.abs(positions[i][axis] - expected[axis]) < 1e-8);
});

test('layouts reject ancestor selections, duplicate nodes, roots and cross-canvas groups atomically', () => {
  const initial = layoutProject(), base = initial.project, before = structuredClone(base), ids = initial.aliases;
  for (const nodes of [[ids.nodes.group, ids.nodes.a], [ids.nodes.a, ids.nodes.a], [base.canvases[0].rootId]]) {
    assert.throws(() => applyRecipe(base, { version: 1, layouts: [{ kind: 'row', nodes }] }), AuthoringError);
    assert.deepEqual(base, before);
  }
  assert.throws(() => applyRecipe(base, { version: 1, canvases: [{ key: 'other' }], nodes: [{ key: 'far', canvas: 'other', kind: 'text' }], layouts: [{ kind: 'row', nodes: [ids.nodes.a, 'far'] }] }), /one Work/);
});

test('inspection reports geometry, media, typography and actionable warnings without confusing containment with overlap', () => {
  const base = newProject();
  const result = applyRecipe(base, { version: 1, nodes: [
    { key: 'group', canvas: base.entryCanvasId, kind: 'container', x: 0, y: 0, width: 300, height: 200 },
    { key: 'inside', canvas: base.entryCanvasId, parent: 'group', kind: 'text', title: 'Child', x: 20, y: 50, width: 200, height: 100, text: 'Short', style: { fontFamily: 'serif', fontSize: 24 } },
    { key: 'outside', canvas: base.entryCanvasId, parent: 'group', kind: 'text', title: 'Outside', x: 280, y: 180, width: 100, height: 100, text: 'Long text '.repeat(100), style: { contentSizing: 'fixed' } },
    { key: 'overlap', canvas: base.entryCanvasId, parent: 'group', kind: 'text', x: 25, y: 55, width: 100, height: 100, text: 'Another' },
    { key: 'image', canvas: base.entryCanvasId, kind: 'image', x: 800, y: 100, width: 200, height: 120 },
  ] });
  result.project.nodes.find(n => n.id === result.aliases.nodes.image)!.assetId = 'absent';
  const report = inspectProject(result.project), canvas = report.canvases[0];
  assert.equal(canvas.nodeCount, 5); assert.equal(canvas.imageCount, 1); assert.equal(canvas.containerCount, 1);
  assert.deepEqual(canvas.styles.fontSizes, [12, 24]); assert.deepEqual(canvas.styles.fontFamilies, ['sans', 'serif']);
  assert.ok(report.warnings.some(w => w.code === 'outside-container' && w.nodeId === result.aliases.nodes.outside));
  assert.ok(report.warnings.some(w => w.code === 'estimated-text-overflow' && w.message.includes('approximately')));
  delete result.project.nodes.find(n => n.id === result.aliases.nodes.outside)!.style!.contentSizing;
  assert.ok(!inspectProject(result.project).warnings.some(w => w.code === 'estimated-text-overflow'));
  assert.ok(report.warnings.some(w => w.code === 'missing-media'));
  const overlaps = report.warnings.filter(w => w.code === 'sibling-overlap');
  assert.equal(overlaps.length, 1); assert.ok(!overlaps[0].nodeIds!.includes(result.aliases.nodes.group));
  assert.equal(report.warnings.some(w => w.code === 'outside-container' && w.nodeId === result.aliases.nodes.image), false, 'Infinite canvas roots are not bounding frames');
});

test('inspection respects collapsed visibility, warns about uniform layouts and survives malformed input', () => {
  const base = newProject();
  const result = applyRecipe(base, { version: 1, nodes: [
    { key: 'group', canvas: base.entryCanvasId, kind: 'container', collapsed: true },
    ...Array.from({ length: 8 }, (_, i) => ({ key: `n-${i}`, canvas: base.entryCanvasId, parent: 'group', kind: 'text' as const, text: 'Plain', x: i * 220, y: 100 })),
  ] });
  assert.equal(result.report.canvases[0].visibleNodeCount, 1);
  assert.ok(result.report.warnings.some(w => w.code === 'uniform-presentation'));
  assert.ok(!result.report.warnings.some(w => w.code === 'sibling-overlap'));
  const malformed = inspectProject({ schemaVersion: 99 } as never);
  assert.equal(malformed.warnings[0].severity, 'error'); assert.equal(malformed.summary.warningCount, 1);
});

test('inspection shares manually authored collapsed bounds with the renderer while layouts retain stored geometry', () => {
  const base = newProject();
  const result = applyRecipe(base, { version: 1, nodes: [
    { key: 'group', canvas: base.entryCanvasId, kind: 'container', title: 'Compact', collapsed: true, text: 'A short visible summary.', x: 0, y: 0, width: 1200, height: 900 },
    { key: 'child', canvas: base.entryCanvasId, parent: 'group', kind: 'text', x: 800, y: 500, width: 200, height: 120 },
    { key: 'peer', canvas: base.entryCanvasId, kind: 'text', title: 'Not overlapped', x: 500, y: 0, width: 200, height: 180 },
    { key: 'image', canvas: base.entryCanvasId, kind: 'image', title: 'Own media stays', collapsed: true, x: 0, y: 250, width: 200, height: 240 },
    { key: 'image-peer', canvas: base.entryCanvasId, kind: 'text', title: 'Over the image', x: 100, y: 420, width: 100, height: 80 },
  ] });
  assert.deepEqual(result.report.canvases[0].bounds, { x: 0, y: 0, width: 1200, height: 900 });
  const overlaps = result.report.warnings.filter(warning => warning.code === 'sibling-overlap');
  assert.equal(overlaps.length, 4);
  assert.ok(overlaps.some(warning => warning.nodeIds?.includes(result.aliases.nodes.image) && warning.nodeIds?.includes(result.aliases.nodes['image-peer'])));
  for (const key of ['peer', 'image', 'image-peer']) assert.ok(overlaps.some(warning => warning.nodeIds?.includes(result.aliases.nodes.group) && warning.nodeIds?.includes(result.aliases.nodes[key])), `The manual closed frame still overlaps ${key}.`);
  const laidOut = applyRecipe(result.project, { version: 1, layouts: [{ kind: 'row', nodes: [result.aliases.nodes.group, result.aliases.nodes.peer], x: 0, y: 0, gap: 32 }] });
  assert.equal(laidOut.project.nodes.find(node => node.id === result.aliases.nodes.peer)?.x, 1232);
  assert.equal(laidOut.project.nodes.find(node => node.id === result.aliases.nodes.group)?.width, 1200);
});


test('module recipes resolve dependency aliases without turning visual relationships into requirements', () => {
  const base = newProject('Module recipe');
  const result = applyRecipe(base, { version: 1, canvases: [{ key: 'home', existingId: base.entryCanvasId }], nodes: [
    { key: 'assembly', canvas: 'home', kind: 'module', title: 'Decision tool' },
    { key: 'result', canvas: 'home', parent: 'assembly', kind: 'output', dependencies: ['condition', 'external-required-node'] },
    { key: 'condition', canvas: 'home', parent: 'assembly', kind: 'input', value: true },
  ], links: [{ key: 'meaning', source: 'assembly', target: 'condition', kind: 'visual', label: 'contains control' }] });
  const output = result.project.nodes.find(node => node.id === result.aliases.nodes.result)!;
  assert.deepEqual(output.dependencyIds, [result.aliases.nodes.condition, 'external-required-node']);
  assert.equal(result.project.nodes.find(node => node.id === result.aliases.nodes.assembly)!.dependencyIds, undefined);
  assert.equal(result.report.canvases[0].containerCount, 1);
  assert.deepEqual(validateProject(result.project), []);
  assert.throws(() => applyRecipe(base, { version: 1, canvases: [{ key: 'home', existingId: base.entryCanvasId }], nodes: [
    { key: 'self', canvas: 'home', kind: 'module', dependencies: ['self'] },
  ] }), /malformed|depend/i);
});
