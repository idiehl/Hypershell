import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { evaluate } from '../src/core/engine.ts';
import { validateProject } from '../src/core/rules.ts';
import type { ContainerVariant, LinkKind, LinkRelation, MeridianLink, MeridianNode, MeridianProject, NodeKind } from '../src/core/types.ts';

const variants: ContainerVariant[] = ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'window', 'gallery', 'spatial'];
function fixture(kind: NodeKind = 'container', variant: ContainerVariant = 'contained') {
  const project = newProject('Embedded relationships'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, kind, 'Collection');
  Object.assign(parent, { childLayout: 'contained', containerVariant: variant, collapsed: false });
  const child = addNode(project, canvasId, 'text', 'Child'); child.parentId = parent.id;
  const nested = addNode(project, canvasId, 'container', 'Nested collection'); nested.parentId = parent.id;
  const descendant = addNode(project, canvasId, 'text', 'Descendant'); descendant.parentId = nested.id;
  const external = addNode(project, canvasId, 'text', 'External'); external.childLayout = 'spatial';
  return { project, canvasId, parent, child, nested, descendant, external };
}
function edge(project: MeridianProject, source: MeridianNode, target: MeridianNode, kind: LinkKind = 'visual', relation: LinkRelation = 'causal', parentId?: string): MeridianLink {
  const link: MeridianLink = { id: crypto.randomUUID(), canvasId: source.canvasId, sourceId: source.id, targetId: target.id, kind, relation, label: 'Stored relationship',
    direction: 'both', sourceAnchor: { x: 0.25, y: 0.75 }, targetAnchor: { x: 0.5, y: 0 }, points: [{ x: 330, y: 280 }], style: { color: '#aabbcc', width: 2, dashed: true }, ...(parentId ? { parentId } : {}) };
  project.links.push(link); return link;
}

test('every container subtype suppresses parent-child relationship lines in either direction without deleting them', () => {
  for (const variant of variants) for (const kind of ['container', 'module', 'spreadsheet'] as const) {
    const f = fixture(kind, variant), inward = edge(f.project, f.parent, f.child), outward = edge(f.project, f.child, f.parent), before = structuredClone(f.project);
    const index = navigationIndex(f.project);
    for (const link of [inward, outward]) {
      assert.equal(index.isEmbeddedLink(link), true, `${kind}/${variant}/${link.sourceId === f.parent.id ? 'forward' : 'reverse'}`);
      assert.equal(index.isLinkVisible(link), false);
      assert.ok(index.contents(f.parent.id).links.includes(link), 'the relationship remains in its owner contents');
    }
    assert.deepEqual(f.project, before, 'classification only changes presentation');
  }
});

test('embedding suppresses causal, chronological, mutual, cyclical and plain relationships and every signal channel', () => {
  const f = fixture();
  for (const kind of ['visual', 'functional', 'numeric'] as const) for (const relation of ['causal', 'chronological', 'mutual', 'cyclical', 'generic'] as const) {
    const link = edge(f.project, f.parent, f.child, kind, relation, f.external.id), index = navigationIndex(f.project);
    assert.equal(index.isEmbeddedLink(link), true, `${kind}/${relation}`);
    assert.equal(index.isLinkRevealed(link), true, 'a separate expanded disclosure owner does not make embedding hidden graph data');
    assert.equal(index.isLinkVisible(link), false);
    assert.ok(index.contents(f.external.id).links.includes(link));
  }
});

test('an enclosing ancestor needs no connector to its descendants but sibling and external relationships remain visible', () => {
  const f = fixture(), ancestor = edge(f.project, f.parent, f.descendant), reversed = edge(f.project, f.descendant, f.parent);
  const peers = edge(f.project, f.child, f.nested), across = edge(f.project, f.descendant, f.external), index = navigationIndex(f.project);
  for (const link of [ancestor, reversed]) { assert.equal(index.isEmbeddedLink(link), true); assert.equal(index.isLinkVisible(link), false); }
  for (const link of [peers, across]) { assert.equal(index.isEmbeddedLink(link), false); assert.equal(index.isLinkVisible(link), true); }
  f.parent.collapsed = true;
  for (const link of [ancestor, reversed, peers, across]) assert.equal(index.isLinkRevealed(link), false, 'disclosure still hides concealed endpoints');
  f.parent.collapsed = false;
  assert.equal(index.isLinkVisible(peers), true); assert.equal(index.isLinkVisible(across), true);
  assert.equal(index.isLinkVisible(ancestor), false);
});

test('ordinary spatial parents and canvas roots keep their meaningful relationship lines', () => {
  const f = fixture('text'); f.parent.childLayout = 'spatial'; delete f.parent.containerVariant;
  const spatial = edge(f.project, f.parent, f.child), root = f.project.nodes.find(node => node.kind === 'canvas')!, rootLink = edge(f.project, root, f.parent), index = navigationIndex(f.project);
  for (const link of [spatial, rootLink]) { assert.equal(index.isEmbeddedLink(link), false); assert.equal(index.isLinkRevealed(link), true); assert.equal(index.isLinkVisible(link), true); }
  f.parent.childLayout = 'contained';
  assert.equal(index.isEmbeddedLink(spatial), false, 'a legacy layout flag cannot turn an ordinary content node into a bounded frame');
  assert.equal(index.isLinkVisible(spatial), true); assert.equal(index.isLinkVisible(rootLink), true);
  f.parent.kind = 'container';
  assert.equal(index.isEmbeddedLink(spatial), true, 'the explicit bounded Container type supplies the visible containment relationship');
  assert.equal(index.isLinkVisible(spatial), false); assert.equal(index.isLinkVisible(rootLink), true);
});

test('computed parents remain unbounded despite legacy containment flags and preserve signal evaluation', () => {
  for (const channel of ['functional', 'numeric'] as const) {
    const project = newProject('Contained computation'), canvasId = project.entryCanvasId, parent = addNode(project, canvasId, 'output', 'Result'), child = addNode(project, canvasId, channel === 'numeric' ? 'number' : 'input', 'Operand');
    Object.assign(parent, { childLayout: 'contained', collapsed: false, valueType: channel === 'numeric' ? 'number' : 'boolean' });
    child.parentId = parent.id; child.value = channel === 'numeric' ? 37 : true;
    const link = edge(project, child, parent, channel); delete link.direction;
    assert.deepEqual(validateProject(project), []);
    const before = structuredClone(project), evaluation = evaluate(project), index = navigationIndex(project);
    assert.equal(index.isLinkRevealed(link), true); assert.equal(index.isEmbeddedLink(link), false); assert.equal(index.isLinkVisible(link), true);
    assert.equal(evaluation.values[parent.id], child.value); assert.deepEqual(evaluation.errors, Object.create(null));
    parent.collapsed = true;
    assert.equal(index.isLinkRevealed(link), false); assert.equal(evaluate(project).values[parent.id], child.value);
    parent.collapsed = false; assert.deepEqual(project, before);
    assert.deepEqual(evaluate(project), evaluation);
  }
});

test('malformed containment cycles and unresolved endpoints terminate safely without inventing an embedded relation', () => {
  const f = fixture(), self = edge(f.project, f.parent, f.parent), missing = edge(f.project, f.parent, f.child); missing.targetId = 'unresolved-child';
  f.parent.parentId = f.nested.id;
  const index = navigationIndex(f.project);
  assert.equal(index.isEmbeddedLink(self), false); assert.equal(index.isEmbeddedLink(missing), false); assert.equal(index.isLinkVisible(missing), false);
});
