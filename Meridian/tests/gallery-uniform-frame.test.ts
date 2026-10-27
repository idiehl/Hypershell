import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { normalizeContainerLayouts, reflowContainedChildren } from '../src/core/grid.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { expansionTrayBounds, resizeExpansionTray, migrateExpansionTrayMetrics } from '../src/core/expansion-tray.ts';
import { boundedChildInterior, exteriorGalleryItemFrame, externalGalleryNavigationBounds } from '../src/core/sizing.ts';
import { validateProject } from '../src/core/rules.ts';
import type { MeridianProject } from '../src/core/types.ts';

const get = (p: MeridianProject, id: string) => p.nodes.find(node => node.id === id)!;
const rect = ({ x, y, width, height }: { x: number; y: number; width: number; height: number }) => ({ x, y, width, height });

function fixture(contained: boolean, embedded = false) {
  const project = newProject('Uniform authored Gallery slots'), owner = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(owner, { x: 40, y: 50, width: 600, height: 400, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal',
    containerSizes: { expanded: { width: 600, height: 400, sizeMode: 'fixed' }, collapsed: { width: 180, height: 120, sizeMode: 'fixed' } } });
  const source = addNode(project, project.entryCanvasId, embedded ? 'container' : 'media', 'Gallery');
  Object.assign(source, { ...(contained ? { parentId: owner.id } : {}), x: 80, y: 80, width: 320, height: 240, sizeMode: 'fixed', collapsed: true, containerVariant: 'gallery' });
  const children = Array.from({ length: 3 }, (_, i) => {
    const node = addNode(project, project.entryCanvasId, 'media', i ? 'Many words in a longer title' : 'Short');
    Object.assign(node, { parentId: source.id, x: 1000 + i * 100, y: 200, width: i ? 260 : 180, height: i === 2 ? 190 : 120, sizeMode: 'auto', collapsed: true,
      text: i ? 'A much longer text passage to fit in the common page. '.repeat(i * 80) : 'Brief', style: { fontSize: 18, shape: i === 1 ? 'circle' : i === 2 ? 'diamond' : 'rectangle' } });
    return node;
  });
  if (!contained) project.nodes = project.nodes.filter(node => node.id !== owner.id);
  const current = contained ? reflowContainedChildren(project, owner.id) : project;
  return { project: current, source: get(current, source.id), owner: get(current, owner.id) ?? owner, children: children.map(node => get(current, node.id)) };
}

test('read-only exterior Gallery measurement gives heterogeneous automatic objects one authored slot independent of content and active page', () => {
  const f = fixture(false); f.source.collapsed = false;
  const before = structuredClone(f.project), common = exteriorGalleryItemFrame(f.project, f.source)!;
  assert.deepEqual([common.width, common.height], [260, 190]);
  for (const active of f.children) {
    const index = navigationIndex(f.project, { galleryItems: new Map([[f.source.id, active.id]]) });
    for (const child of f.children) assert.deepEqual(rect(index.bounds(child)), common);
    assert.deepEqual(externalGalleryNavigationBounds(f.project, f.source, active), externalGalleryNavigationBounds(f.project, f.source, f.children[0]));
  }
  assert.deepEqual(f.project, before);
});

test('exterior Gallery layout uses equal original-node frames while retaining shapes, typography, identities and saved hidden content', () => {
  for (const contained of [false, true]) {
    const f = fixture(contained), child = f.children[0], hidden = addNode(f.project, child.canvasId, 'media', 'Original hidden note');
    Object.assign(hidden, { parentId: child.id, x: child.x + 12, y: child.y + 20, width: 40, height: 25, collapsed: true, sizeMode: 'fixed' });
    const styles = f.children.map(node => structuredClone(node.style));
    const current = revealChildren(f.project, f.source.id, true), source = get(current, f.source.id), frame = exteriorGalleryItemFrame(current, source)!;
    assert.deepEqual([frame.width, frame.height], [260, 190]);
    for (const [i, original] of f.children.entries()) {
      const node = get(current, original.id); assert.deepEqual(rect(node), frame); assert.deepEqual(node.style, styles[i]);
      assert.equal(node.text, original.text); assert.equal(node.parentId, source.id);
    }
    assert.equal(get(current, hidden.id).parentId, child.id); assert.ok(get(current, hidden.id).width > 0);
    const tray = expansionTrayBounds(current, source.id);
    for (const active of f.children) assert.deepEqual(expansionTrayBounds(current, source.id, { galleryItems: { [source.id]: active.id } }), tray);
    assert.deepEqual(normalizeContainerLayouts(current), current);
    assert.deepEqual(revealChildren(revealChildren(current, source.id, false), source.id, true), current);
    assert.deepEqual(validateProject(current), []);
  }
});

test('embedded Gallery browsing keeps its authored frame and all stored original geometry while every page uses the same inner viewport', () => {
  const f = fixture(true, true), before = structuredClone(f.project), current = revealChildren(f.project, f.source.id, true), source = get(current, f.source.id);
  const frame = rect(navigationIndex(current).bounds(source)), inner = boundedChildInterior(current, source);
  for (const active of f.children) {
    assert.deepEqual(rect(navigationIndex(current, { galleryItems: { [source.id]: active.id } }).bounds(source)), frame);
    assert.deepEqual(boundedChildInterior(current, source, { context: { galleryItems: { [source.id]: active.id } } }), inner);
  }
  assert.deepEqual(current.nodes.filter(node => node.parentId === source.id), before.nodes.filter(node => node.parentId === source.id));
});

test('fixed Gallery trays keep their exact authored outer frame after paging, v2 migration, resizing and ancestor reopen', () => {
  const f = fixture(true), opened = revealChildren(f.project, f.source.id, true), initial = expansionTrayBounds(opened, f.source.id)!;
  const fixed = resizeExpansionTray(opened, f.source.id, { ...rect(initial), width: 430, height: 280 });
  const source = get(fixed, f.source.id), tray = expansionTrayBounds(fixed, source.id)!, styles = f.children.map(node => structuredClone(get(fixed, node.id).style));
  for (const active of f.children) assert.deepEqual(expansionTrayBounds(fixed, source.id, { galleryItems: { [source.id]: active.id } }), tray);
  const reopened = revealChildren(normalizeContainerLayouts(revealChildren(fixed, f.owner.id, false)), f.owner.id, true);
  assert.deepEqual(reopened, fixed); assert.deepEqual(f.children.map(node => get(reopened, node.id).style), styles);
  // Construct a saved v2 frame with its old20px inset, then migrate once.
  source.expansionTray = { ...source.expansionTray!, layoutVersion: 2, offset: { x: source.expansionTray!.offset.x + 15, y: source.expansionTray!.offset.y + 15 } };
  const legacyOuter = expansionTrayBounds(fixed, source.id)!;
  migrateExpansionTrayMetrics(fixed);
  assert.deepEqual(rect(expansionTrayBounds(fixed, source.id)!), rect(legacyOuter));
  assert.equal(source.expansionTray.layoutVersion, 3); assert.deepEqual(migrateExpansionTrayMetrics(fixed), []);
});

test('nested exterior disclosures have their own trays and never change an automatic Gallery page frame while browsing', () => {
  const f = fixture(true), item = f.children[0], hidden = addNode(f.project, item.canvasId, 'media', 'Original child branch');
  Object.assign(item, { collapsed: true, containerVariant: 'horizontal' });
  Object.assign(hidden, { parentId: item.id, x: 2100, y: 1300, width: 320, height: 200, sizeMode: 'fixed', collapsed: true, text: 'A distant original branch' });
  const openGallery = revealChildren(f.project, f.source.id, true), frame = expansionTrayBounds(openGallery, f.source.id)!;
  const openBranch = revealChildren(openGallery, item.id, true), common = expansionTrayBounds(openBranch, f.source.id)!;
  assert.deepEqual(common, frame); assert.ok(expansionTrayBounds(openBranch, item.id));
  for (const active of f.children) assert.deepEqual(expansionTrayBounds(openBranch, f.source.id, { galleryItems: { [f.source.id]: active.id } }), common);
  assert.deepEqual(normalizeContainerLayouts(openBranch), openBranch);
  assert.equal(get(openBranch, hidden.id).parentId, item.id); assert.deepEqual(validateProject(openBranch), []);
});
