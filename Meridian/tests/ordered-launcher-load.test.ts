import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { gridGeometry, normalizeContainerLayouts, reflowContainedChildren } from '../src/core/grid.ts';
import { boundedChildInterior, sizingBounds } from '../src/core/sizing.ts';
import { validateProject } from '../src/core/rules.ts';

const parentId = 'a3d00829-1577-4dec-8591-24495b6c885d';
const titles = ['Greek Mythology · A living atlas', 'Greek Mythology · Visual atlas', 'Greek Mythology · Gods, Epics and Roman Worlds', 'Meridian · First connections'];
const close = (actual: number, expected: number) => assert.ok(Math.abs(actual - expected) < 1e-7, `${actual} must equal ${expected}`);

// Synthetic reproduction only: no saved user library or project is read here.
function oldLauncherRow(sizeMode: 'auto' | 'fixed', pattern: 'horizontal' | 'vertical') {
  const project = newProject('Legacy short ordered launcher row');
  const parent = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(parent, { id: parentId, tag: 'Canvas Block', x: 80, y: 90, width: 840, height: 530, sizeMode,
    collapsed: false, containerVariant: pattern, childLayout: 'contained', childArrangement: pattern === 'horizontal' ? 'row' : 'column',
    grid: { columns: pattern === 'horizontal' ? 4 : 1, rows: pattern === 'vertical' ? 4 : 1, cellWidth: 336, cellHeight: 100, gap: 0, showHeaders: false },
    style: { shape: 'rectangle', borderWidth: 1, padding: 5, childPadding: 5 },
    containerSizes: { expanded: { width: 840, height: 530, sizeMode }, collapsed: { width: 230, height: 140, sizeMode: 'fixed' } },
  });
  const children = titles.map((title, at) => {
    const child = addNode(project, project.entryCanvasId, 'canvas-link', title);
    Object.assign(child, { parentId, x: 86 + at * 207, y: 96, width: 207, height: 100, sizeMode: 'fixed', collapsed: true,
      cell: { column: pattern === 'horizontal' ? at + 1 : 1, row: pattern === 'vertical' ? at + 1 : 1 },
      canvasTarget: { projectId: `synthetic-destination-${at}` }, style: { shape: 'rectangle' },
    });
    return child;
  });
  return { project, parent, children };
}

test('loading old short Canvas launcher cells fills both axes without renaming or resizing their ordered parent', () => {
  for (const sizeMode of ['auto', 'fixed'] as const) for (const pattern of ['horizontal', 'vertical'] as const) {
    const { project, parent, children } = oldLauncherRow(sizeMode, pattern), before = structuredClone(project);
    assert.deepEqual(validateProject(project), []);
    const normalized = normalizeContainerLayouts(project), current = normalized.nodes.find(node => node.id === parentId)!;
    const inner = boundedChildInterior(normalized, current), grid = gridGeometry(current, normalized);
    assert.deepEqual(project, before, 'Load normalization works on an independent draft.');
    assert.deepEqual([current.id, current.tag, current.title, current.width, current.height], [parentId, 'Canvas Block', '', 840, 530]);
    assert.deepEqual(current.containerSizes, parent.containerSizes, 'Both authored disclosure frames remain intact.');
    close(grid.cellWidth * grid.visibleColumns, inner.width);
    close(grid.cellHeight * grid.visibleRows, inner.height);
    for (const [at, original] of children.entries()) {
      const child = normalized.nodes.find(node => node.id === original.id)!;
      const box = sizingBounds(normalized, child);
      close(box.x, inner.x + (pattern === 'horizontal' ? at * grid.cellWidth : 0));
      close(box.y, inner.y + (pattern === 'vertical' ? at * grid.cellHeight : 0));
      close(box.width, grid.cellWidth); close(box.height, grid.cellHeight);
      if (pattern === 'horizontal') close(box.y + box.height, inner.y + inner.height);
      else close(box.x + box.width, inner.x + inner.width);
      assert.equal(child.title, original.title); assert.deepEqual(child.canvasTarget, original.canvasTarget);
    }
    assert.deepEqual(normalizeContainerLayouts(normalized), normalized, 'Reopening cannot accumulate geometric changes.');
    assert.deepEqual(validateProject(normalized), []);
  }
});

test('ordered Canvas launchers keep filling as parent height or width grows past stale cell requests and shrinks again', () => {
  const sweep = [70, 140, 250, 530, 1200, 530, 250, 140, 70];
  for (const sizeMode of ['auto', 'fixed'] as const) for (const pattern of ['horizontal', 'vertical'] as const) {
    const fixture = oldLauncherRow(sizeMode, pattern);
    let current = normalizeContainerLayouts(fixture.project);
    for (const dimension of sweep) {
      const candidate = structuredClone(current), parent = candidate.nodes.find(node => node.id === parentId)!;
      if (pattern === 'horizontal') parent.height = dimension;
      else parent.width = dimension;
      const expectedWidth = pattern === 'horizontal' ? 840 : dimension;
      const expectedHeight = pattern === 'horizontal' ? dimension : 530;
      const before = structuredClone(candidate);
      current = reflowContainedChildren(candidate, parentId);
      assert.deepEqual(candidate, before, 'Resizing fits an independent draft.');
      const fittedParent = current.nodes.find(node => node.id === parentId)!;
      const inner = boundedChildInterior(current, fittedParent), grid = gridGeometry(fittedParent, current);
      close(fittedParent.width, expectedWidth); close(fittedParent.height, expectedHeight);
      close(grid.cellWidth * grid.visibleColumns, inner.width);
      close(grid.cellHeight * grid.visibleRows, inner.height);
      assert.equal(fittedParent.grid!.cellWidth, 336, 'The request remains portable metadata, not a rendered-width ceiling.');
      assert.equal(fittedParent.grid!.cellHeight, 100, 'The request remains portable metadata, not a rendered-height ceiling.');
      for (const [at, original] of fixture.children.entries()) {
        const child = current.nodes.find(node => node.id === original.id)!;
        const box = sizingBounds(current, child);
        close(box.x, inner.x + (pattern === 'horizontal' ? at * grid.cellWidth : 0));
        close(box.y, inner.y + (pattern === 'vertical' ? at * grid.cellHeight : 0));
        close(box.width, grid.cellWidth); close(box.height, grid.cellHeight);
        if (pattern === 'horizontal') { close(box.height, inner.height); close(box.y + box.height, inner.y + inner.height); }
        else { close(box.width, inner.width); close(box.x + box.width, inner.x + inner.width); }
        assert.equal(child.title, original.title); assert.deepEqual(child.canvasTarget, original.canvasTarget);
      }
      assert.deepEqual(normalizeContainerLayouts(current), current);
      assert.deepEqual(validateProject(current), []);
    }
  }
});
