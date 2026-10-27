import assert from 'node:assert/strict';
import test from 'node:test';
import { existsSync, readFileSync } from 'node:fs';
import { computeTreeLayout, normalizeTreeForest, PROGRAMMING_TREE_PRESETS, inspectTreeLayout, segmentIntersectsRectangle, spatialTreeNodeFields } from './tree-layout.mjs';
import { applyRecipe } from '../../src/core/authoring.ts';
import { newProject } from '../../src/core/model.ts';
import { revealChildren } from '../../src/core/reveal.ts';
import { navigationIndex } from '../../src/core/navigation.ts';

const forest = [
  { key: 'System', width: 460, height: 160 },
  { key: 'System.Text', parent: 'System' },
  { key: 'System.Text.Json', parent: 'System.Text', width: 360, height: 150 },
  { key: 'System.Text.Json.Nodes', parent: 'System.Text.Json' },
  { key: 'System.Text.RegularExpressions', parent: 'System.Text', height: 180 },
  { key: 'System.IO', parent: 'System', width: 440 },
  { key: 'System.IO.Compression', parent: 'System.IO' },
  { key: 'System.IO.Pipes', parent: 'System.IO' },
  { key: 'Microsoft', width: 400 },
  { key: 'Microsoft.CSharp', parent: 'Microsoft' },
  { key: 'Microsoft.CSharp.RuntimeBinder', parent: 'Microsoft.CSharp' },
];

for (const orientation of ['down', 'right']) {
  test(`${orientation}: branches advance through generations with disjoint reserved footprints`, () => {
    const original = structuredClone(forest);
    const layout = computeTreeLayout(forest, orientation, { origin: { x: 80, y: 500 } });
    assert.deepEqual(forest, original);
    assert.deepEqual(computeTreeLayout(forest, orientation, { origin: { x: 80, y: 500 } }), layout);
    assert.deepEqual(inspectTreeLayout(layout), { overlaps: [], routeCollisions: [] });
    assert.equal(layout.routes.length, forest.length - 2);
    for (const node of layout.nodes.filter(node => node.parent)) {
      const parent = layout.byKey[node.parent];
      assert.ok(orientation === 'down' ? node.y > parent.y + parent.height : node.x > parent.x + parent.width);
      assert.ok(node.allocation.crossStart >= parent.allocation.crossStart);
      assert.ok(node.allocation.crossEnd <= parent.allocation.crossEnd);
    }
    const siblings = layout.nodes.filter(node => node.parent === 'System');
    assert.ok(orientation === 'down' ? siblings[0].x !== siblings[1].x : siblings[0].y !== siblings[1].y);
  });

  test(`${orientation}: local native hierarchy and saved link bends survive nested collapse/reopen without movement`, () => {
    const layout = computeTreeLayout(forest, orientation, { localGenerations: true });
    const base = newProject('Tree disclosure test');
    const composed = applyRecipe(base, {
      version: 1,
      canvases: [{ key: 'work', existingId: base.entryCanvasId, title: 'Namespace tree' }],
      nodes: layout.nodes.map(node => ({ key: node.key, canvas: 'work', ...(node.parent ? { parent: node.parent } : {}),
        ...spatialTreeNodeFields(node), title: node.key, text: 'Short searchable description.', collapsed: false,
        style: { shape: 'rectangle', fontFamily: 'sans', titleSize: 22, fontSize: 16, contentSizing: 'fixed' } })),
      links: layout.routes.map((route, at) => ({ key: `relation/${at}`, canvas: 'work', parent: route.parent,
        source: route.source, target: route.target, kind: 'visual', relation: 'generic', direction: 'forward',
        sourceAnchor: route.sourceAnchor, targetAnchor: route.targetAnchor, points: route.points,
        style: { lineStyle: 'solid' } })),
    });
    const initialGeometry = project => project.nodes.map(node => [node.id, node.x, node.y, node.width, node.height]);
    const initial = initialGeometry(composed.project);
    const bends = composed.project.links.map(link => link.points);
    let project = composed.project;
    for (const key of ['System.Text.Json', 'System.Text', 'System', 'Microsoft']) project = revealChildren(project, composed.aliases.nodes[key], false);
    for (const key of ['System', 'Microsoft', 'System.Text', 'System.Text.Json']) project = revealChildren(project, composed.aliases.nodes[key], true);
    assert.deepEqual(initialGeometry(project), initial);
    assert.deepEqual(project.links.map(link => link.points), bends);
    const nav = navigationIndex(project);
    assert.ok(project.nodes.every(node => nav.isNodeVisible(node.id)));
    assert.ok(project.links.every(link => nav.isLinkVisible(link)));
    assert.equal(project.nodes.filter(node => node.expansionTray).length, 0);
    assert.deepEqual(JSON.parse(JSON.stringify(project)), project);
  });
}

test('deep uneven forest with mixed fixed dimensions has no rectangle or route collisions', () => {
  const records = [{ key: 'root' }];
  for (let branch = 0; branch < 40; branch++) {
    const parent = `root/n${branch}`; records.push({ key: parent, parent: 'root', width: 220 + branch % 7 * 30, height: 90 + branch % 5 * 25 });
    for (let leaf = 0; leaf <= branch % 9; leaf++) {
      const key = `${parent}/n${leaf}`; records.push({ key, parent, width: 110 + leaf * 19, height: 90 + leaf * 12 });
      if (leaf % 2) records.push({ key: `${key}/detail`, parent: key, width: 190, height: 100 });
    }
  }
  for (const direction of ['down', 'right']) {
    const layout = computeTreeLayout(records, direction, { siblingGap: 55, levelGap: 180 });
    assert.deepEqual(inspectTreeLayout(layout), { overlaps: [], routeCollisions: [] });
  }
});

test('invalid forests and nonfinite geometry reject before layout', () => {
  for (const records of [[{ key: 'x' }, { key: 'x' }], [{ key: 'x', parent: 'missing' }], [{ key: 'x', parent: 'y' }, { key: 'y', parent: 'x' }], [{ key: 'x', width: NaN }]]) {
    assert.throws(() => computeTreeLayout(records), TypeError);
  }
  assert.throws(() => computeTreeLayout([], 'radial'), TypeError);
  assert.throws(() => computeTreeLayout([], 'down', { levelGap: 0 }), TypeError);
  assert.deepEqual(computeTreeLayout([]).bounds, { x: 0, y: 0, width: 0, height: 0 });
});

test('route collision probe detects an unrelated box and permits endpoint boundary contact', () => {
  const box = { x: 10, y: 10, width: 20, height: 20 };
  assert.equal(segmentIntersectsRectangle({ x: 0, y: 20 }, { x: 40, y: 20 }, box), true);
  assert.equal(segmentIntersectsRectangle({ x: 0, y: 20 }, { x: 10, y: 20 }, box), false);
  assert.equal(segmentIntersectsRectangle({ x: 0, y: 0 }, { x: 40, y: 0 }, box), false);
});

test('736 namespace roots retain a readable overview while deep frames fit within available focus zoom', () => {
  const records = [];
  const addFamily = (root, count, branches) => {
    records.push({ key: root });
    for (let at = 0; at < branches; at++) records.push({ key: `${root}/b${at}`, parent: root });
    for (let at = branches + 1; at < count; at++) records.push({ key: `${root}/n${at}`, parent: `${root}/b${at % branches}` });
  };
  addFamily('System', 463, 42); addFamily('Microsoft', 270, 28);
  records.push({ key: 'Internal' }, { key: 'Accessibility' }, { key: 'Windows' });
  assert.equal(records.length, 736);
  const raw = computeTreeLayout(records, 'down');
  const before = structuredClone(raw);
  const fit = normalizeTreeForest(raw, PROGRAMMING_TREE_PRESETS.namespaces);
  assert.deepEqual(raw, before);
  assert.ok(fit.bounds.width <= 10000 + 1e-6);
  assert.ok(fit.normalization.factor < 1);
  assert.ok(fit.normalization.detailFramesReachable);
  assert.ok(fit.normalization.largestRequiredZoom < 1024);
  assert.deepEqual(inspectTreeLayout(fit), { overlaps: [], routeCollisions: [] });
  for (const node of fit.nodes) {
    if (fit.roots.includes(node.key)) assert.deepEqual([node.width, node.height], [1200, 650]);
    else assert.ok(Math.abs(node.width / node.height - raw.byKey[node.key].width / raw.byKey[node.key].height) < 1e-9);
  }
  assert.ok(fit.roots.every(key => Math.abs(fit.byKey[key].y - fit.byKey.System.y) < 1e-9));
});

test('large rightward class tree normalizes height and root promotion recomputes safe lanes', () => {
  const records = [{ key: 'C++' }];
  for (let category = 0; category < 20; category++) {
    const key = `category${category}`; records.push({ key, parent: 'C++' });
    for (let leaf = 0; leaf < 46; leaf++) records.push({ key: `${key}/class${leaf}`, parent: key });
  }
  const raw = computeTreeLayout(records, 'right');
  const fit = normalizeTreeForest(raw, PROGRAMMING_TREE_PRESETS.classes);
  assert.ok(fit.bounds.height <= 10000 + 1e-6);
  assert.equal(fit.byKey['C++'].width, 2000);
  assert.equal(fit.byKey['C++'].height, 1800);
  assert.ok(fit.byKey.category0.x >= fit.byKey['C++'].x + 2120);
  assert.deepEqual(inspectTreeLayout(fit), { overlaps: [], routeCollisions: [] });
  assert.ok(fit.normalization.detailFramesReachable);
  assert.ok(fit.nodes.filter(node => node.parent === 'C++').some(node => node.y !== fit.byKey.category0.y));
});

test('impossible promoted root overview rejects rather than overlap roots', () => {
  const layout = computeTreeLayout([{ key: 'a' }, { key: 'b' }]);
  assert.throws(() => normalizeTreeForest(layout, { maxCrossSpan: 1000, rootWidth: 1000 }), RangeError);
});

test('optional branch headers expand uniformly into their own reserved bays without extra overview span', () => {
  const raw = computeTreeLayout(forest, 'down');
  const settings = { maxCrossSpan: 5000, rootWidth: 900, rootHeight: 450 };
  const plain = normalizeTreeForest(raw, settings);
  const filled = normalizeTreeForest(raw, { ...settings, branchFill: .75 });
  assert.ok(Math.abs(plain.bounds.width - filled.bounds.width) < 1e-7);
  assert.ok(filled.byKey['System.Text'].width > plain.byKey['System.Text'].width);
  for (const node of filled.nodes.filter(node => node.parent)) {
    assert.ok(node.width <= node.allocation.crossEnd - node.allocation.crossStart + 1e-7);
    assert.ok(Math.abs(node.width / node.height - raw.byKey[node.key].width / raw.byKey[node.key].height) < 1e-9);
  }
  assert.deepEqual(inspectTreeLayout(filled), { overlaps: [], routeCollisions: [] });
});

const stagedNamespaces = new URL('../../content/programming-trees-20261021/csharp-namespaces.json', import.meta.url);
test('actual staged namespace census fits promoted roots with collision-free readable focus frames', { skip: !existsSync(stagedNamespaces) }, () => {
  const catalog = JSON.parse(readFileSync(stagedNamespaces, 'utf8'));
  const records = catalog.namespaceEntries.map(entry => ({ key: entry.name, parent: entry.parent }));
  assert.equal(records.length, catalog.statistics.treeNodeCount);
  const raw = computeTreeLayout(records, 'down', { width: 720, height: 280, depthScale: .82, minScale: .38, siblingGap: 110, levelGap: 160 });
  const fit = normalizeTreeForest(raw, PROGRAMMING_TREE_PRESETS.namespaces);
  assert.ok(fit.bounds.width <= 10000 + 1e-6);
  assert.ok(fit.normalization.largestRequiredZoom <= 1024);
  assert.equal(fit.normalization.detailFramesReachable, true);
  assert.deepEqual(inspectTreeLayout(fit), { overlaps: [], routeCollisions: [] });
  const unpromoted = normalizeTreeForest(raw, { ...PROGRAMMING_TREE_PRESETS.namespaces, promoteBranches: 0 });
  for (const node of fit.nodes.filter(node => node.parent)) {
    assert.ok(Math.abs(node.width / node.height - raw.byKey[node.key].width / raw.byKey[node.key].height) < 1e-8);
    assert.ok(node.width / unpromoted.byKey[node.key].width <= 25 + 1e-7);
  }
  assert.ok(fit.byKey['System.Text'].width > unpromoted.byKey['System.Text'].width);
  const text = fit.byKey['System.Text'], json = fit.byKey['System.Text.Json'];
  assert.ok(json.y > text.y + text.height);
  const textChildren = fit.nodes.filter(node => node.parent === text.key);
  const expectedGap = Math.min(120, Math.max(text.height * .35, ...textChildren.map(child => child.height * .35), 1e-6));
  assert.ok(Math.abs(json.y - text.y - text.height - expectedGap) < 1e-7);
  assert.ok(textChildren.every(child => Math.abs(child.y - json.y) < 1e-7));
  assert.equal(fit.normalization.localGenerations, true);
});

test('local generations retain small families near their parent despite a huge unrelated cousin', () => {
  for (const orientation of ['down', 'right']) {
    const records = [{ key: 'root' }, { key: 'small', parent: 'root', width: 20, height: 10 },
      { key: 'small/child', parent: 'small', width: 8, height: 4 },
      { key: 'huge', parent: 'root', width: 1500, height: 900 },
      { key: 'huge/child', parent: 'huge', width: 100, height: 200 }];
    const layout = computeTreeLayout(records, orientation, { localGenerations: true });
    const parent = layout.byKey.small, child = layout.byKey['small/child'];
    const gap = orientation === 'down' ? child.y - parent.y - parent.height : child.x - parent.x - parent.width;
    assert.ok(Math.abs(gap - (orientation === 'down' ? 3.5 : 7)) < 1e-8);
    assert.deepEqual(inspectTreeLayout(layout), { overlaps: [], routeCollisions: [] });
  }
});

const stagedClasses = new URL('../../content/programming-trees-20261021/cpp-types.json', import.meta.url);
test('actual staged C++ categories, families and all type leaves use collision-free local rightward generations', { skip: !existsSync(stagedClasses) }, () => {
  const catalog = JSON.parse(readFileSync(stagedClasses, 'utf8'));
  const records = [{ key: 'std' }];
  for (const category of catalog.categories) {
    const key = `category/${category.id}`; records.push({ key, parent: 'std' });
    for (const group of category.children) {
      const groupKey = `group/${group.id}`; records.push({ key: groupKey, parent: key });
      for (const id of group.typeIds) records.push({ key: `type/${id}`, parent: groupKey });
    }
  }
  assert.equal(records.filter(record => record.key.startsWith('type/')).length, catalog.types.length);
  const raw = computeTreeLayout(records, 'right', { width: 720, height: 280, depthScale: .82, minScale: .38, siblingGap: 110, levelGap: 160 });
  const fit = normalizeTreeForest(raw, PROGRAMMING_TREE_PRESETS.classes);
  assert.ok(fit.bounds.height <= 10000 + 1e-6);
  assert.ok(fit.normalization.largestRequiredZoom <= 1024);
  assert.equal(fit.normalization.localGenerations, true);
  assert.deepEqual(inspectTreeLayout(fit), { overlaps: [], routeCollisions: [] });
  for (const parent of fit.nodes) {
    const children = fit.nodes.filter(node => node.parent === parent.key);
    if (!children.length) continue;
    assert.ok(children.every(child => Math.abs(child.x - children[0].x) < 1e-7));
    const gap = Math.min(120, Math.max(parent.width * .35, ...children.map(child => child.width * .35), 1e-6));
    assert.ok(children.every(child => Math.abs(child.x - parent.x - parent.width - gap) < 1e-7));
  }
});
