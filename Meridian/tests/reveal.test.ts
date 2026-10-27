import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { displayBounds, linkDirection, navigationIndex } from '../src/core/navigation.ts';
import { isEdgeAttached, revealChildren } from '../src/core/reveal.ts';
import { applyRecipe, inspectProject } from '../src/core/authoring.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { splitLinkWithJunction } from '../src/core/connections.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';
import { evaluate } from '../src/core/engine.ts';
import type { MeridianLink, MeridianNode, MeridianProject } from '../src/core/types.ts';

const overlaps = (a: MeridianNode, b: MeridianNode) => a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
function setup() {
  const project = newProject('Spatial disclosure');
  const parent = addNode(project, project.entryCanvasId, 'text', 'Text and related ideas');
  Object.assign(parent, { x: 0, y: 0, width: 320, height: 240, sizeMode: 'fixed', childLayout: 'spatial', collapsed: true, text: 'The main idea stays in place.' });
  return { project, parent };
}
function child(project: MeridianProject, parent: MeridianNode, placement: MeridianNode['placement'] = 'near') {
  const node = addNode(project, parent.canvasId, 'text', 'Related idea');
  Object.assign(node, { parentId: parent.id, placement, x: parent.x, y: parent.y, width: 180, height: 120, sizeMode: 'fixed' });
  return node;
}
const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;

test('spatial disclosure preserves parent tile, canvas, identities, content and nested collapsed states', () => {
  const { project, parent } = setup(), first = child(project, parent), nested = child(project, first);
  first.collapsed = true; nested.text = 'Kept content'; const original = structuredClone(project);
  const expanded = revealChildren(project, parent.id, true), shown = navigationIndex(expanded);
  assert.deepEqual(project, original);
  assert.equal(expanded.entryCanvasId, project.entryCanvasId);
  assert.equal(expanded.canvases.length, 1);
  assert.deepEqual(expanded.nodes.map(node => node.id), project.nodes.map(node => node.id));
  assert.deepEqual(shown.bounds(get(expanded, parent.id)), displayBounds(parent));
  assert.equal(shown.visibleNode(first.id)?.id, first.id);
  assert.equal(shown.visibleNode(nested.id)?.id, first.id);
  assert.equal(get(expanded, nested.id).text, 'Kept content');
  const collapsed = revealChildren(expanded, parent.id, false);
  assert.equal(navigationIndex(collapsed).visibleNode(nested.id)?.id, parent.id);
  assert.deepEqual(collapsed.nodes.map(node => [node.x, node.y]), expanded.nodes.map(node => [node.x, node.y]));
  assert.deepEqual(revealChildren(collapsed, parent.id, true), expanded);
});

test('automatic reveal finds nearby unoccupied spaces without moving unrelated or freely placed nodes', () => {
  const { project, parent } = setup();
  const fixed = addNode(project, parent.canvasId, 'image', 'Already here');
  Object.assign(fixed, { x: 320, y: -80, width: 260, height: 420, sizeMode: 'fixed' });
  const free = child(project, parent, 'free'); Object.assign(free, { x: -230, y: -60 });
  const items = Array.from({ length: 8 }, () => child(project, parent));
  const expanded = revealChildren(project, parent.id, true), shown = [get(expanded, parent.id), get(expanded, fixed.id), get(expanded, free.id), ...items.map(node => get(expanded, node.id))];
  assert.deepEqual(get(expanded, fixed.id), fixed); assert.deepEqual(get(expanded, free.id), free);
  for (const item of items) for (const obstacle of shown) if (item.id !== obstacle.id) assert.equal(overlaps(get(expanded, item.id), obstacle), false);
  assert.ok(items.every(item => Math.abs(get(expanded, item.id).x) < 1400 && Math.abs(get(expanded, item.id).y) < 1400));
  assert.deepEqual(revealChildren(project, parent.id, true), expanded);
  assert.deepEqual(revealChildren(expanded, parent.id, true), expanded);
});

test('edge children form an adjacent collage and choose another side when the requested edge is blocked', () => {
  const { project, parent } = setup();
  const blocked = addNode(project, parent.canvasId, 'image', 'Occupied right edge');
  Object.assign(blocked, { x: 320, y: -400, width: 300, height: 1000, sizeMode: 'fixed' });
  const items = Array.from({ length: 4 }, () => child(project, parent, 'edge'));
  items.forEach(node => { node.attachSide = 'right'; });
  const expanded = revealChildren(project, parent.id, true), revealed = items.map(node => get(expanded, node.id));
  for (const node of revealed) {
    assert.equal(isEdgeAttached(parent, node), true);
    assert.equal(overlaps(node, blocked), false);
    assert.equal(overlaps(node, parent), false);
    for (const other of revealed) if (node.id !== other.id) assert.equal(overlaps(node, other), false);
  }
  assert.deepEqual(get(expanded, blocked.id), blocked);
});

test('completely blocked edges fall back to a free nearby space without displacing an existing tile', () => {
  const { project, parent } = setup(), node = child(project, parent, 'edge');
  const blockers = [
    { x: -250, y: -250, width: 820, height: 250 },
    { x: -250, y: 240, width: 820, height: 250 },
    { x: -250, y: 0, width: 250, height: 240 },
    { x: 320, y: 0, width: 250, height: 240 },
  ].map((bounds, index) => Object.assign(addNode(project, parent.canvasId, 'image', `Blocked edge ${index}`), bounds, { sizeMode: 'fixed' }));
  const expanded = revealChildren(project, parent.id, true), revealed = get(expanded, node.id);
  assert.ok(blockers.every(blocker => !overlaps(revealed, blocker)));
  assert.equal(isEdgeAttached(parent, revealed), false);
  for (const blocker of blockers) assert.deepEqual(get(expanded, blocker.id), blocker);
});

test('a moved branch keeps descendants and internal route bends together, while outside routes stay authored', () => {
  const { project, parent } = setup(), branch = child(project, parent), hidden = child(project, branch, 'free');
  branch.collapsed = true; hidden.x = 75; hidden.y = 100;
  project.links.push(
    { id: 'inside', canvasId: parent.canvasId, sourceId: branch.id, targetId: hidden.id, kind: 'visual', relation: 'generic', points: [{ x: 50, y: 50 }] },
    { id: 'outside', canvasId: parent.canvasId, sourceId: parent.id, targetId: branch.id, kind: 'visual', relation: 'causal', points: [{ x: 300, y: 400 }] },
  );
  const expanded = revealChildren(project, parent.id, true), moved = get(expanded, branch.id), dx = moved.x - branch.x, dy = moved.y - branch.y;
  assert.notDeepEqual([dx, dy], [0, 0]);
  assert.deepEqual([get(expanded, hidden.id).x, get(expanded, hidden.id).y], [hidden.x + dx, hidden.y + dy]);
  assert.deepEqual(expanded.links[0].points, [{ x: 50 + dx, y: 50 + dy }]);
  assert.deepEqual(expanded.links[1].points, project.links[1].points);
  assert.equal(get(expanded, branch.id).collapsed, true);
});

test('free children and legacy parents keep their geometry; migration can keep distant valid near positions', () => {
  const { project, parent } = setup(), free = child(project, parent, 'free'), distant = child(project, parent);
  Object.assign(free, { x: 350, y: 80 }); // Free placement preserves valid spatial positions, never parent overlap.
  Object.assign(distant, { x: 3000, y: 2200 });
  const expanded = revealChildren(project, parent.id, true, { preserveValidPositions: true });
  assert.deepEqual(get(expanded, free.id), free);
  assert.deepEqual(get(expanded, distant.id), distant);
  const near = revealChildren(project, parent.id, true);
  assert.ok(Math.abs(get(near, distant.id).x) < 1000 && Math.abs(get(near, distant.id).y) < 1000);
  delete parent.childLayout; parent.kind = 'container'; parent.width = 1000; parent.height = 900;
  assert.equal(displayBounds(parent).width, 1000);
  const enclosed = revealChildren(project, parent.id, true), expandedParent = enclosed.nodes.find(node => node.id === parent.id)!;
  assert.equal(expandedParent.childLayout, 'contained'); assert.ok(displayBounds(expandedParent).width >= 1000);
});

test('expanded descendant footprints avoid unrelated tiles even when a child contains another visible branch', () => {
  const { project, parent } = setup(), branch = child(project, parent), grandchild = child(project, branch, 'free');
  branch.childLayout = 'spatial'; branch.collapsed = false; grandchild.x = 190; grandchild.y = 60;
  const obstacle = addNode(project, parent.canvasId, 'image', 'Nearby image');
  Object.assign(obstacle, { x: 350, y: 0, width: 200, height: 500, sizeMode: 'fixed' });
  const expanded = revealChildren(project, parent.id, true);
  for (const node of [get(expanded, branch.id), get(expanded, grandchild.id)]) {
    assert.equal(overlaps(node, obstacle), false); assert.equal(overlaps(node, parent), false);
  }
  assert.deepEqual([get(expanded, grandchild.id).x - get(expanded, branch.id).x, get(expanded, grandchild.id).y - get(expanded, branch.id).y], [190, 60]);
});

test('spatial fields and relationship direction round-trip through recipes, publication and reusable modules', () => {
  const base = newProject('Portable spatial composition');
  const authored = applyRecipe(base, { version: 1, nodes: [
    { key: 'parent', canvas: base.entryCanvasId, kind: 'text', childLayout: 'spatial', collapsed: true },
    { key: 'picture', canvas: base.entryCanvasId, parent: 'parent', kind: 'image', placement: 'edge', attachSide: 'left', x: -500 },
    { key: 'next', canvas: base.entryCanvasId, parent: 'parent', kind: 'text', placement: 'near', x: 600 },
  ], links: [{ key: 'sequence', source: 'parent', target: 'next', relation: 'chronological', direction: 'reverse' }] });
  assert.ok(!inspectProject(authored.project).warnings.some(issue => issue.code === 'outside-container'));
  const payload = createPublicationPayload(authored.project).project;
  assert.equal(get(payload, authored.aliases.nodes.parent).childLayout, 'spatial');
  assert.equal(get(payload, authored.aliases.nodes.picture).placement, 'edge');
  assert.equal(get(payload, authored.aliases.nodes.picture).attachSide, 'left');
  assert.equal(payload.links[0].direction, 'reverse');
  const reopened = readPublication(buildPublication(authored.project, '', ''));
  assert.equal(reopened.links[0].relation, 'chronological'); assert.equal(reopened.links[0].direction, 'reverse');
  const module = createModule(authored.project, authored.aliases.nodes.parent, 'Expandable essay');
  const destination = newProject('Destination'), inserted = instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 200, y: 300 }).project;
  assert.ok(inserted.nodes.some(node => node.childLayout === 'spatial'));
  assert.ok(inserted.nodes.some(node => node.placement === 'edge' && node.attachSide === 'left'));
  assert.equal(inserted.links[0].direction, 'reverse'); assert.deepEqual(validateProject(inserted), []);
});

test('untrusted spatial enums reject atomically, including saved snapshots and inherited restrictions', () => {
  const { project, parent } = setup(), node = child(project, parent), original = structuredClone(project);
  for (const [field, value] of [['childLayout', 'window'], ['placement', 'absolute-css'], ['attachSide', 'diagonal']] as const) {
    const invalid = structuredClone(project); (get(invalid, parent.id) as unknown as Record<string, unknown>)[field] = value;
    assert.equal(isProjectShape(invalid), false);
    assert.throws(() => applyRecipe(project, { version: 1, nodes: [{ key: 'parent', existingId: parent.id, [field]: value }] } as never));
    const snapshot = structuredClone(project); snapshot.snapshots[parent.id] = { node: get(invalid, parent.id), value: null, savedAt: '2026-10-05T00:00:00Z' };
    assert.equal(isProjectShape(snapshot), false);
  }
  parent.rules = { nodeKinds: ['image'] };
  assert.throws(() => revealChildren(project, parent.id, true), /inherited/);
  assert.equal(node.x, original.nodes.find(item => item.id === node.id)?.x);
  assert.equal(parent.collapsed, true);
  assert.throws(() => revealChildren(project, 'absent', true), /existing/);
  assert.throws(() => revealChildren(project, project.canvases[0].rootId, true), /always visible/);
});

test('explicit visual arrow choices preserve legacy defaults and survive inserted routing junctions', () => {
  const { project, parent } = setup(), node = child(project, parent);
  const link: MeridianLink = { id: 'relationship', canvasId: parent.canvasId, sourceId: parent.id, targetId: node.id, kind: 'visual', relation: 'generic' };
  assert.equal(linkDirection(link), 'none');
  for (const [relation, expected] of [['causal', 'forward'], ['chronological', 'forward'], ['mutual', 'both'], ['cyclical', 'both']] as const) assert.equal(linkDirection({ ...link, relation }), expected);
  for (const direction of ['forward', 'reverse', 'both', 'none'] as const) {
    project.links = [{ ...link, direction }];
    assert.equal(linkDirection(project.links[0]), direction);
    const split = splitLinkWithJunction(project, link.id, { x: 500, y: 200 });
    assert.ok(split.project.links.every(item => item.direction === direction));
    assert.equal(linkDirection({ ...link, kind: 'numeric', direction }), 'forward');
  }
  const invalid = structuredClone(project); (invalid.links[0] as unknown as Record<string, unknown>).direction = 'sideways';
  assert.equal(isProjectShape(invalid), false);
});

test('revealing or hiding numeric systems changes presentation without changing their values', () => {
  const { project, parent } = setup(), input = addNode(project, parent.canvasId, 'number'), output = addNode(project, parent.canvasId, 'output');
  input.parentId = parent.id; input.placement = 'near'; input.value = 17; output.valueType = 'number'; output.x = 800;
  project.links.push({ id: 'signal', canvasId: parent.canvasId, sourceId: input.id, targetId: output.id, kind: 'numeric', relation: 'causal' });
  assert.equal(evaluate(project).values[output.id], 17);
  const expanded = revealChildren(project, parent.id, true), collapsed = revealChildren(expanded, parent.id, false);
  assert.equal(evaluate(expanded).values[output.id], 17); assert.equal(evaluate(collapsed).values[output.id], 17);
});
