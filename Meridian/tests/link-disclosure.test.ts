import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, forkProject, newProject } from '../src/core/model.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { validateProject } from '../src/core/rules.ts';
import { applyRecipe } from '../src/core/authoring.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';
import { splitLinkWithJunction } from '../src/core/connections.ts';
import { createPublicationPayload, readPublication, buildPublication } from '../src/publication.ts';
import { evaluate } from '../src/core/engine.ts';
import type { MeridianLink } from '../src/core/types.ts';

function fixture() {
  const project = newProject('Owned connections'), canvasId = project.entryCanvasId;
  const ancestor = addNode(project, canvasId, 'container', 'Ancestor');
  const parent = addNode(project, canvasId, 'text', 'Parent'); parent.parentId = ancestor.id; parent.childLayout = 'spatial';
  const child = addNode(project, canvasId, 'text', 'Child'); child.parentId = parent.id;
  const sibling = addNode(project, canvasId, 'text', 'Sibling'); sibling.parentId = parent.id;
  const outside = addNode(project, canvasId, 'text', 'Outside');
  const owner = addNode(project, canvasId, 'text', 'Connections only'); owner.childLayout = 'spatial';
  const link = (sourceId: string, targetId: string, parentId?: string): MeridianLink => {
    const connection: MeridianLink = { id: crypto.randomUUID(), canvasId, sourceId, targetId, kind: 'visual', relation: 'causal', ...(parentId ? { parentId } : {}) };
    project.links.push(connection); return connection;
  };
  return { project, ancestor, parent, child, sibling, outside, owner, link };
}

test('legacy connections infer their nearest shared containment including endpoint parents', () => {
  const { project, parent, child, sibling, outside, link } = fixture();
  const inward = link(parent.id, child.id), peers = link(child.id, sibling.id), cross = link(child.id, outside.id);
  const nav = navigationIndex(project);
  assert.equal(nav.linkParent(inward)?.id, parent.id);
  assert.equal(nav.linkParent(peers)?.id, parent.id);
  assert.equal(nav.linkParent(cross)?.id, project.canvases[0].rootId);
  assert.equal(nav.contents(parent.id).links.length, 2);
  assert.equal(nav.isLinkVisible(inward), true);
  parent.collapsed = true;
  assert.equal(nav.isLinkVisible(inward), false);
  assert.equal(nav.isLinkVisible(peers), false);
  assert.equal(nav.isLinkVisible(cross), false, 'a hidden endpoint never proxies to its visible parent');
});

test('a link-only owner controls visible endpoints independently and hidden ancestors suppress links', () => {
  const { project, ancestor, parent, outside, owner, link } = fixture();
  const connection = link(parent.id, outside.id, owner.id), nav = navigationIndex(project);
  assert.deepEqual(nav.contents(owner.id), { nodes: [], links: [connection] });
  assert.equal(nav.isLinkVisible(connection), true);
  owner.collapsed = true; assert.equal(nav.isLinkVisible(connection), false);
  owner.collapsed = false; owner.parentId = ancestor.id; ancestor.collapsed = true;
  assert.equal(nav.isLinkVisible(connection), false);
  ancestor.collapsed = false; assert.equal(nav.isLinkVisible(connection), true);
  parent.collapsed = true; assert.equal(nav.isLinkVisible(connection), true, 'an endpoint tile remains visible when its own contents collapse');
});

test('nested collapse hides descendant-owned links even when their endpoints remain outside', () => {
  const { project, ancestor, parent, outside, owner, link } = fixture();
  owner.parentId = parent.id;
  const connection = link(ancestor.id, outside.id, owner.id), nav = navigationIndex(project);
  parent.collapsed = true; assert.equal(nav.isLinkVisible(connection), false);
  parent.collapsed = false; assert.equal(nav.isLinkVisible(connection), true);
  project.nodes[0].collapsed = true; assert.equal(nav.isLinkVisible(connection), false);
});

test('validation rejects missing, wrong-type and cross-canvas ownership', () => {
  const { project, parent, child, link } = fixture(); const connection = link(parent.id, child.id, parent.id);
  assert.deepEqual(validateProject(project), []);
  for (const value of ['', 'missing', null, true, {}]) {
    (connection as any).parentId = value; assert.ok(validateProject(project).length, JSON.stringify(value));
  }
  connection.parentId = addCanvas(project, 'Other').rootId;
  assert.match(validateProject(project).map(issue => issue.message).join(' '), /owner/);
});

test('recipes resolve forward owners, keep omitted ownership and explicitly clear it', () => {
  const base = newProject('Recipe');
  const made = applyRecipe(base, { version: 1, nodes: [
    { key: 'first', canvas: base.entryCanvasId, kind: 'text' },
    { key: 'last', canvas: base.entryCanvasId, kind: 'text' },
    { key: 'owner', canvas: base.entryCanvasId, kind: 'text', collapsed: true },
  ], links: [{ key: 'edge', source: 'first', target: 'last', parent: 'owner' }] });
  const edge = made.project.links[0]; assert.equal(edge.parentId, made.aliases.nodes.owner);
  const kept = applyRecipe(made.project, { version: 1, links: [{ key: 'edge', existingId: edge.id, label: 'Still owned' }] });
  assert.equal(kept.project.links[0].parentId, edge.parentId);
  const cleared = applyRecipe(kept.project, { version: 1, links: [{ key: 'edge', existingId: edge.id, parent: null }] });
  assert.equal(cleared.project.links[0].parentId, undefined);
  assert.throws(() => applyRecipe(base, { version: 1, links: [{ key: 'edge', source: 'missing', target: 'missing', parent: 'missing' }] }));
  assert.equal(base.links.length, 0);
});

test('forks and offline publication preserve or independently remap connection owners', () => {
  const { project, parent, child, owner, link } = fixture(); link(parent.id, child.id, owner.id);
  const payload = createPublicationPayload(project); assert.equal(payload.project.links[0].parentId, owner.id);
  for (const copy of [forkProject(project), readPublication(buildPublication(project, '', ''))]) {
    assert.notEqual(copy.links[0].parentId, owner.id);
    assert.equal(copy.nodes.find(node => node.id === copy.links[0].parentId)?.title, owner.title);
    assert.deepEqual(validateProject(copy), []);
  }
});

test('module extraction preserves internal owners, adopts external owners, and insertion remaps them', () => {
  const { project, parent, child, sibling, owner, link } = fixture();
  link(parent.id, child.id, parent.id); link(child.id, sibling.id, owner.id);
  const module = createModule(project, parent.id, 'Branch'), copy = module.project;
  assert.equal(copy.nodes.find(node => node.id === copy.links[0].parentId)?.title, parent.title);
  assert.equal(copy.links[1].parentId, module.rootId, 'external owner becomes the packing node');
  const destination = newProject('Destination'), inserted = instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 0, y: 0 });
  assert.deepEqual(validateProject(inserted.project), []);
  for (const edge of inserted.project.links) assert.ok(inserted.project.nodes.some(node => node.id === edge.parentId));
});

test('junction splitting retains the same disclosure owner on both resulting segments', () => {
  for (const explicit of [false, true]) {
    const { project, parent, child, owner, link } = fixture(); const edge = link(parent.id, child.id, explicit ? owner.id : undefined);
    const originalOwner = navigationIndex(project).linkParent(edge)!.id;
    const result = splitLinkWithJunction(project, edge.id, { x: 500, y: 400 });
    assert.equal(result.project.nodes.find(node => node.id === result.nodeId)!.parentId, originalOwner);
    assert.ok(result.project.links.every(connection => connection.parentId === originalOwner));
    result.project.nodes.find(node => node.id === originalOwner)!.collapsed = true;
    assert.ok(result.project.links.every(connection => !navigationIndex(result.project).isLinkVisible(connection)));
  }
});

test('disclosure never changes computation or removes a hidden functional connection', () => {
  const project = newProject('Signals'), input = addNode(project, project.entryCanvasId, 'input', 'Input'), output = addNode(project, project.entryCanvasId, 'output', 'Output');
  const owner = addNode(project, project.entryCanvasId, 'text', 'Hide connection'); input.value = true; owner.collapsed = true;
  project.links.push({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: input.id, targetId: output.id, parentId: owner.id, kind: 'functional', relation: 'causal' });
  assert.equal(navigationIndex(project).isLinkVisible(project.links[0]), false);
  assert.equal(evaluate(project).values[output.id], true);
  assert.equal(project.links.length, 1);
});

test('visible outlines prune large collapsed branches while the complete hierarchy remains available', () => {
  const project = newProject('Large hierarchy'), chapter = addNode(project, project.entryCanvasId, 'text', 'Chapter'); chapter.collapsed = true;
  for (let i = 0; i < 5800; i++) { const child = addNode(project, project.entryCanvasId, 'text', `Detail ${i}`); child.parentId = chapter.id; }
  const index = navigationIndex(project);
  assert.deepEqual(index.outline(project.entryCanvasId, true).map(row => row.node.id), [chapter.id]);
  assert.equal(index.outline(project.entryCanvasId).length, 5801);
  chapter.collapsed = false; assert.equal(index.outline(project.entryCanvasId, true).length, 5801);
});
