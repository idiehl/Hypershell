import test from 'node:test';
import assert from 'node:assert/strict';
import { applyRecipe, inventoryProject, AuthoringError, type AuthoringRecipe } from '../src/core/authoring.ts';
import { addCanvas, addNode, newProject } from '../src/core/model.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { evaluate } from '../src/core/engine.ts';
import { validateProject } from '../src/core/rules.ts';
import { normalizeContainerLayouts } from '../src/core/grid.ts';

test('saved alias manifests update the same IDs and preserve all unmentioned entities and input data', () => {
  const first = applyRecipe(newProject('Stable revision'), { version: 1, canvases: [{ key: 'home' }], nodes: [
    { key: 'hero', canvas: 'home', kind: 'media', tag: 'Focal image', title: '', text: 'Keep original content', x: 20, y: 50, width: 400, height: 300 },
    { key: 'peer', canvas: 'home', kind: 'media', text: 'Unrelated content', x: 800 },
  ], links: [{ key: 'relation', source: 'hero', target: 'peer', label: 'Compare' }] });
  const before = structuredClone(first.project), bindings = structuredClone(first.aliases);
  const result = applyRecipe(first.project, { version: 1, bindings, nodes: [{ key: 'hero', style: { backgroundDisplay: 'hover', portalColor: '#4C8079' } }], links: [{ key: 'relation', label: 'An explicit comparison' }] });
  assert.equal(result.aliases.nodes.hero, first.aliases.nodes.hero);
  assert.equal(result.project.nodes.length, before.nodes.length);
  assert.equal(result.project.nodes.find(node => node.id === result.aliases.nodes.hero)?.text, 'Keep original content');
  assert.deepEqual(result.project.nodes.find(node => node.id === result.aliases.nodes.peer), before.nodes.find(node => node.id === first.aliases.nodes.peer));
  assert.equal(result.project.links[0].id, before.links[0].id);
  assert.deepEqual(first.project, before); assert.equal(JSON.stringify(first.aliases), JSON.stringify(bindings));
  assert.equal(result.project.revision, before.revision);
  assert.throws(() => applyRecipe(first.project, { version: 1, bindings, nodes: [{ key: 'hero', existingId: bindings.nodes.peer }] }), /conflicts/);
  assert.throws(() => applyRecipe(first.project, { version: 1, bindings: { nodes: { hero: 'missing' } } }), /unavailable/);
  assert.throws(() => applyRecipe(first.project, { version: 1, bindings: { canvases: { home: first.project.entryCanvasId }, roots: { home: first.aliases.roots.home } } }), /root binding/);
});

test('explicit optional resets remove old subfields while preserving neighboring settings and validate the final composition', () => {
  const base = newProject(), node = addNode(base, base.entryCanvasId, 'media', 'Styled');
  node.text = 'A title'; node.textRuns = [{ start: 0, end: 1, style: { bold: true } }];
  node.style = { fill: '#112233', radius: 24, childPadding: 35, fontSize: 20, backgroundPattern: 'dots', backgroundTint: '#123456' };
  node.containerSizes = { expanded: { width: 800, height: 600, sizeMode: 'fixed' }, collapsed: { width: 150, height: 90, sizeMode: 'fixed' } };
  const result = applyRecipe(base, { version: 1, nodes: [{ key: 'styled', existingId: node.id, unset: ['style.radius', 'style.childPadding', 'textRuns', 'containerSizes'], text: 'Changed', style: { backgroundDisplay: 'hover' } }] });
  const changed = result.project.nodes.find(item => item.id === node.id)!;
  assert.equal(changed.style?.radius, undefined); assert.equal(changed.style?.childPadding, undefined);
  assert.equal(changed.style?.fontSize, 20); assert.equal(changed.style?.backgroundPattern, 'dots');
  assert.equal(changed.textRuns, undefined); assert.equal(changed.containerSizes, undefined);
  const reset = applyRecipe(result.project, { version: 1, nodes: [{ key: 'styled', existingId: node.id, unset: ['style'], style: { shape: 'none' } }] });
  assert.deepEqual(reset.project.nodes.find(item => item.id === node.id)!.style, { shape: 'none' });
  for (const field of ['id', 'kind', 'parentId', 'width', 'style.__proto__', 'style.notAStyle']) assert.throws(() => applyRecipe(base, { version: 1, nodes: [{ key: 'n', existingId: node.id, unset: [field] }] }), AuthoringError);
  assert.equal(node.style.radius, 24);
});

test('removals are branch scoped, clean owned and endpoint connections, and refuse dangling survivors atomically', () => {
  const base = newProject(), canvas = base.entryCanvasId, other = addCanvas(base, 'Unrelated canvas');
  const group = addNode(base, canvas, 'container', 'Old group'), child = addNode(base, canvas, 'media', 'Old detail'), peer = addNode(base, canvas, 'media', 'Keep');
  child.parentId = group.id;
  const elsewhere = addNode(base, other.id, 'media', 'Other canvas');
  base.links.push({ id: crypto.randomUUID(), canvasId: canvas, sourceId: child.id, targetId: peer.id, kind: 'visual', relation: 'generic' });
  const reference = addNode(base, canvas, 'reference', 'Dependent'); reference.targetId = child.id;
  const before = structuredClone(base);
  assert.throws(() => applyRecipe(base, { version: 1, remove: { nodes: [group.id] } }), /referring to a removed node/);
  assert.deepEqual(base, before);
  const result = applyRecipe(base, { version: 1, remove: { nodes: [group.id] }, nodes: [{ key: 'survivor', existingId: reference.id, unset: ['target'] }] });
  assert.ok(!result.project.nodes.some(node => [group.id, child.id].includes(node.id)));
  assert.equal(result.project.links.length, 0);
  assert.deepEqual(result.project.nodes.find(node => node.id === elsewhere.id), elsewhere);
  assert.equal(result.project.nodes.find(node => node.id === peer.id)?.title, 'Keep');
  assert.deepEqual(validateProject(result.project), []);
  assert.throws(() => applyRecipe(base, { version: 1, remove: { nodes: [base.canvases[0].rootId] } }), /roots cannot/);
  assert.throws(() => applyRecipe(base, { version: 1, remove: { nodes: [group.id] }, nodes: [{ key: 'old', existingId: child.id }] }), /does not exist/);
});

test('declarative routes create real typed utilities with stable aliases, labels and disclosure ownership', () => {
  const base = newProject(), canvas = base.entryCanvasId;
  const result = applyRecipe(base, { version: 1, nodes: [
    { key: 'group', canvas, kind: 'container', x: 0, y: 0, width: 1000, height: 600 },
    { key: 'a', canvas, parent: 'group', kind: 'media', x: 20, y: 50 },
    { key: 'b', canvas, parent: 'group', kind: 'media', x: 600, y: 350 },
  ], routes: [{ key: 'descent', source: 'a', target: 'b', label: 'descends from', direction: 'forward', style: { color: '#D7AD69', width: 2 }, sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: .5 }, via: [
    { key: 'elbow', x: 400, y: 110, variant: 'pivot' }, { key: 'lineage-joint', x: 400, y: 410, variant: 'joint' },
  ] }] });
  const { project, aliases } = result, utilities = project.nodes.filter(node => node.kind === 'junction');
  assert.equal(utilities.length, 2); assert.equal(project.links.length, 3);
  assert.equal(utilities[0].parentId, aliases.nodes.group); assert.equal(utilities[0].x + utilities[0].width / 2, 400);
  assert.equal(utilities[0].utilityVariant, 'pivot'); assert.equal(utilities[1].utilityVariant, 'joint');
  const terminal = project.links.find(link => link.id === aliases.links.descent)!;
  assert.equal(terminal.label, 'descends from'); assert.deepEqual(terminal.targetAnchor, { x: 0, y: .5 });
  assert.ok(project.links.every(link => link.parentId === aliases.nodes.group && link.style?.color === '#D7AD69'));
  assert.equal(project.links.filter(link => link.label).length, 1);
  assert.equal(navigationIndex(project).linkParent(terminal)?.id, aliases.nodes.group);
  const edited = applyRecipe(project, { version: 1, bindings: aliases, routes: [{ key: 'descent', source: 'a', target: 'b', label: 'follows', via: [{ key: 'elbow', x: 420, y: 100 }, { key: 'lineage-joint', x: 420, y: 430, variant: 'joint' }] }] });
  assert.equal(edited.project.nodes.length, project.nodes.length); assert.equal(edited.project.links.length, project.links.length);
  assert.equal(edited.aliases.links.descent, aliases.links.descent); assert.equal(edited.aliases.nodes.elbow, aliases.nodes.elbow);
  assert.equal(edited.project.links.find(link => link.id === terminal.id)?.label, 'follows');
  assert.deepEqual(validateProject(edited.project), []);
});

test('numeric routes preserve destination ports and execute through passive waypoints; late route failures leave input untouched', () => {
  const base = newProject(), canvas = base.entryCanvasId;
  const recipe: AuthoringRecipe = { version: 1, nodes: [{ key: 'a', canvas, kind: 'number', value: 7 }, { key: 'b', canvas, kind: 'output', valueType: 'number' }], routes: [{ key: 'signal', source: 'a', target: 'b', kind: 'numeric', via: [{ key: 'pivot', x: 400, y: 300 }] }] };
  const result = applyRecipe(base, recipe);
  assert.equal(evaluate(result.project).values[result.aliases.nodes.b], 7);
  assert.equal(result.project.nodes.find(node => node.id === result.aliases.nodes.pivot)?.valueType, 'number');
  const moved = applyRecipe(result.project, { version: 1, bindings: result.aliases, routes: [{ key: 'signal', source: 'a', target: 'b', via: [{ key: 'pivot', x: 450, y: 350 }] }] });
  assert.equal(evaluate(moved.project).values[moved.aliases.nodes.b], 7);
  assert.ok(moved.project.links.every(link => link.kind === 'numeric'));
  const before = structuredClone(base);
  for (const bad of [ { ...recipe, routes: [{ ...recipe.routes![0], target: 'missing' }] }, { ...recipe, routes: [{ ...recipe.routes![0], via: [{ key: 'a', x: 0, y: 0 }] }] } ]) assert.throws(() => applyRecipe(base, bad), AuthoringError);
  assert.deepEqual(base, before);
});

test('route updates preserve custom waypoint settings and require explicit removal of obsolete segments', () => {
  const base = newProject(), canvas = base.entryCanvasId;
  const first = applyRecipe(base, { version: 1, nodes: [{ key: 'a', canvas, kind: 'media' }, { key: 'b', canvas, kind: 'media' }], routes: [{ key: 'route', source: 'a', target: 'b', relation: 'mutual', direction: 'both', label: 'Two accounts', style: { color: '#446688' }, via: [
    { key: 'p', x: 350, y: 200, variant: 'joint', width: 24, height: 20 }, { key: 'q', x: 450, y: 200 },
  ] }] });
  const shorter: AuthoringRecipe = { version: 1, bindings: first.aliases, routes: [{ key: 'route', source: 'a', target: 'b', via: [{ key: 'p', x: 360, y: 220 }] }] };
  assert.throws(() => applyRecipe(first.project, shorter), /Obsolete route segment/);
  const result = applyRecipe(first.project, { ...shorter, remove: { links: ['route/segment/2'], nodes: ['q'] } });
  const p = result.project.nodes.find(node => node.id === first.aliases.nodes.p)!;
  assert.equal(p.utilityVariant, 'joint'); assert.equal(p.width, 24); assert.equal(p.height, 20);
  assert.equal(result.project.links.length, 2);
  assert.ok(result.project.links.every(link => link.relation === 'mutual' && link.direction === 'both' && link.style?.color === '#446688'));
  assert.equal(result.project.links.find(link => link.id === first.aliases.links.route)?.label, 'Two accounts');
  assert.equal(result.aliases.nodes.q, undefined); assert.equal(result.aliases.links['route/segment/2'], undefined);
  assert.throws(() => applyRecipe(first.project, { version: 1, bindings: { ...first.aliases, nodes: { ...first.aliases.nodes, other: first.aliases.nodes.p } }, nodes: [{ key: 'other', title: 'Edited twice' }], routes: shorter.routes }), /cannot be declared twice/);
  assert.throws(() => applyRecipe(first.project, { version: 1, bindings: { ...first.aliases, links: { ...first.aliases.links, other: first.aliases.links.route } }, links: [{ key: 'other', label: 'Edited twice' }], routes: shorter.routes }), /cannot be declared twice/);
});

test('inventory exports reusable aliases, real effective bounds and media metadata without attachment payloads', () => {
  const base = newProject(), node = addNode(base, base.entryCanvasId, 'media', 'Visible');
  node.tag = 'Search handle'; node.text = 'Original knowledge'; node.style = { shape: 'none' };
  base.assets.push({ id: 'asset-1', name: 'Credit.txt', mime: 'text/plain', base64: 'SGVsbG8=' });
  const result = inventoryProject(base);
  assert.equal(result.nodes[0].tag, 'Search handle'); assert.equal(result.nodes[0].text, 'Original knowledge');
  assert.deepEqual(result.nodes[0].bounds, navigationIndex(base).bounds(node));
  assert.equal(result.assets[0].bytes, 5); assert.ok(!JSON.stringify(result).includes('SGVsbG8='));
  const edited = applyRecipe(base, { version: 1, bindings: result.aliases, nodes: [{ key: result.nodes[0].alias, title: 'Updated' }] });
  assert.equal(edited.project.nodes.find(item => item.id === node.id)?.title, 'Updated');
});

test('explicit Freeform arrangement retains a serpentine reading path through recipe updates and viewer normalization', () => {
  const base = newProject('A chronological epic'), canvas = base.entryCanvasId;
  const placements = Array.from({ length: 24 }, (_, index) => {
    const row = Math.floor(index / 6), column = row % 2 ? 5 - index % 6 : index % 6;
    return { key: `book-${index + 1}`, canvas, parent: 'epic', kind: 'media' as const, title: `Book ${index + 1}`, x: 140 + column * 220, y: 160 + row * 150, width: 180, height: 100, childPosition: 'manual' as const };
  });
  const first = applyRecipe(base, { version: 1, nodes: [{ key: 'epic', canvas, kind: 'container', title: '', tag: 'Epic sequence', containerVariant: 'contained', childArrangement: 'free', x: 100, y: 100, width: 1420, height: 720 }, ...placements], links: placements.slice(1).map((book, index) => ({ key: `next-${index + 1}`, source: placements[index].key, target: book.key, relation: 'chronological' as const })) });
  assert.equal(first.project.nodes.find(node => node.id === first.aliases.nodes.epic)?.childArrangement, 'free');
  const revised = applyRecipe(first.project, { version: 1, bindings: first.aliases, nodes: [{ key: 'epic', containerVariant: 'contained', style: { childPadding: 5 } }] });
  assert.equal(revised.project.nodes.find(node => node.id === first.aliases.nodes.epic)?.childArrangement, 'free');
  for (const candidate of [first.project, revised.project, normalizeContainerLayouts(revised.project)]) for (const placement of placements) {
    const node = candidate.nodes.find(item => item.id === first.aliases.nodes[placement.key])!;
    assert.deepEqual([node.x, node.y], [placement.x, placement.y], placement.key);
  }
  assert.throws(() => applyRecipe(base, { version: 1, nodes: [{ key: 'bad', canvas, kind: 'container', containerVariant: 'horizontal', childArrangement: 'free' }] }), /requires 'row'/);
  assert.deepEqual(validateProject(revised.project), []);
});
