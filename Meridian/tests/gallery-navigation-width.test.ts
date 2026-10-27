import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { normalizeContainerLayouts } from '../src/core/grid.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { expansionTrayBounds, resizeExpansionTray } from '../src/core/expansion-tray.ts';
import { externalGalleryNavigationBounds } from '../src/core/sizing.ts';

test('narrow and tall exterior Gallery pages and navigation share exact outer edges without a minimum-width expansion', () => {
  for (const width of [.01, 24, 55, 109, 110, 360]) for (const height of [30, 300]) {
    const project = newProject('Exact Gallery navigation width'), source = addNode(project, project.entryCanvasId, 'media', 'Gallery');
    Object.assign(source, { x: 20, y: 30, width: 200, height: 120, collapsed: false, sizeMode: 'fixed', containerVariant: 'gallery' });
    const item = addNode(project, source.canvasId, 'media', 'Original tall page');
    Object.assign(item, { parentId: source.id, x: 300, y: 50, width, height, sizeMode: 'auto', collapsed: true, text: 'Content fits this common page. '.repeat(20), style: { shape: 'rounded' } });
    const before = structuredClone(project), tile = navigationIndex(project).bounds(item), nav = externalGalleryNavigationBounds(project, source, item);
    assert.equal(nav.x, tile.x); assert.equal(nav.width, tile.width); assert.ok(nav.height > 0 && nav.scale > 0 && nav.scale <= 1);
    assert.equal(nav.height / nav.scale, 38); assert.deepEqual(project, before);
  }
});

test('automatic and resized Gallery trays keep identical page/navigation widths through paging, normalization and reopen', () => {
  for (const width of [30, 75, 240]) {
    const project = newProject('Exact tray Gallery edges'), owner = addNode(project, project.entryCanvasId, 'container', '');
    Object.assign(owner, { x: 20, y: 20, width: 500, height: 400, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal' });
    const source = addNode(project, owner.canvasId, 'media', 'Gallery');
    Object.assign(source, { parentId: owner.id, sizeMode: 'fixed', collapsed: true, containerVariant: 'gallery', width: 200, height: 120 });
    const items = Array.from({ length: 2 }, (_, at) => {
      const node = addNode(project, owner.canvasId, 'media', `Original ${at}`);
      Object.assign(node, { parentId: source.id, width, height: 300, sizeMode: 'auto', collapsed: true, text: 'Page content '.repeat(at ? 100 : 1), style: { shape: 'rounded' } }); return node;
    });
    let current = revealChildren(normalizeContainerLayouts(project), source.id, true);
    for (const resized of [false, true]) {
      const frame = expansionTrayBounds(current, source.id)!;
      if (resized) current = resizeExpansionTray(current, source.id, { ...frame, width: width + 10, height: 440 });
      for (const item of items) {
        const parent = current.nodes.find(node => node.id === source.id)!, child = current.nodes.find(node => node.id === item.id)!, context = { galleryItems: { [source.id]: item.id } };
        const tile = navigationIndex(current, context).bounds(child), nav = externalGalleryNavigationBounds(current, parent, child, { context });
        assert.equal(nav.x, tile.x); assert.equal(nav.width, tile.width); assert.equal(child.id, item.id); assert.deepEqual(child.style, item.style);
      }
      assert.deepEqual(normalizeContainerLayouts(current), current);
      assert.deepEqual(revealChildren(revealChildren(current, source.id, false), source.id, true), current);
    }
  }
});

test('a height-constrained exterior navigation scales its controls without narrowing or shifting the page-aligned strip', () => {
  const project = newProject('Height-constrained Gallery strip'), owner = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(owner, { x: 0, y: 0, width: 300, height: 200, sizeMode: 'fixed', collapsed: false });
  const source = addNode(project, owner.canvasId, 'media', 'Gallery');
  Object.assign(source, { parentId: owner.id, collapsed: false, containerVariant: 'gallery', sizeMode: 'fixed' });
  const item = addNode(project, owner.canvasId, 'media', 'Page');
  Object.assign(item, { parentId: source.id, x: 60, y: 120, width: 55, height: 60, sizeMode: 'fixed', collapsed: true });
  const before = structuredClone(project), tile = navigationIndex(project).bounds(item), nav = externalGalleryNavigationBounds(project, source, item);
  assert.equal(nav.x, tile.x); assert.equal(nav.width, tile.width); assert.ok(nav.height < 19 && nav.height > 0); assert.ok(nav.scale < .5);
  assert.deepEqual(project, before);
});
