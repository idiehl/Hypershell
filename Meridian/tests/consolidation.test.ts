import { test } from 'node:test';
import assert from 'node:assert/strict';
import { newProject, addCanvas, addNode } from '../src/core/model.ts';
import { consolidateCanvases } from '../src/core/consolidation.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { revealChildren } from '../src/core/reveal.ts';

test('consolidation preserves identities and turns page navigation into local references', () => {
  const p = newProject('Atlas'), first = p.canvases[0]!, second = addCanvas(p, 'Family');
  const parent = addNode(p, second.id, 'text', 'Parent'), child = addNode(p, second.id, 'text', 'Child');
  parent.collapsed = true; child.parentId = parent.id; child.text = 'Exact attributed text';
  const reference = addNode(p, first.id, 'canvas-link', 'Family'); reference.targetId = second.rootId;
  p.links.push({ id: crypto.randomUUID(), canvasId: second.id, sourceId: parent.id, targetId: child.id, kind: 'visual', relation: 'causal', parentId: parent.id });
  const original = structuredClone(p), { project: flat, branches } = consolidateCanvases(p);
  assert.deepEqual(p, original);
  assert.equal(flat.canvases.length, 1); assert.equal(branches[second.id], second.rootId);
  assert.deepEqual(flat.nodes.map(n => n.id), p.nodes.map(n => n.id));
  assert.equal(flat.nodes.find(n => n.id === reference.id)?.kind, 'reference');
  assert.equal(flat.nodes.find(n => n.id === child.id)?.text, child.text);
  assert.equal(navigationIndex(flat).isLinkVisible(flat.links[0]!), false);
  let opened = revealChildren(flat, second.rootId, true);
  opened = revealChildren(opened, parent.id, true);
  assert.ok(opened.nodes.find(node => node.id === parent.id)?.expansionTray, 'The consolidated unbounded branch opens in its own tray.');
  assert.equal(opened.links[0]!.id, p.links[0]!.id, 'Consolidation and tray disclosure preserve the authored relationship.');
  assert.equal(navigationIndex(opened).isLinkRevealed(opened.links[0]!), true);
  assert.equal(navigationIndex(opened).isLinkVisible(opened.links[0]!), false, 'The single tray tether replaces the source-to-child shaft.');
});

test('consolidation preserves unresolved external canvas references and rejects absent destination', () => {
  const p = newProject(), external = addNode(p, p.entryCanvasId, 'canvas-link');
  external.targetId = crypto.randomUUID();
  assert.equal(consolidateCanvases(p).project.nodes.find(n => n.id === external.id)?.kind, 'canvas-link');
  assert.throws(() => consolidateCanvases(p, 'missing'), /destination Work/);
});
