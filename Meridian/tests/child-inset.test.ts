import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { gridGeometry, reflowContainedChildren } from '../src/core/grid.ts';
import { boundedChildInterior, nodeSpacing } from '../src/core/sizing.ts';
import { DEFAULT_CHILD_INSET } from '../src/core/spacing.ts';

test('default child inset leaves a parent backdrop while ordered children remain contiguous and fill the interior', () => {
  for (const variant of ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'] as const) {
    const project = newProject('Child inset'), parent = addNode(project, project.entryCanvasId, 'container', '');
    Object.assign(parent, { x: 100, y: 80, width: 800, height: 500, sizeMode: 'fixed', collapsed: false, containerVariant: variant,
      grid: { columns: 2, rows: 2, cellWidth: 300, cellHeight: 180, gap: 0, showHeaders: false, windowColumns: 2, windowRows: 2 }, style: { shape: 'rectangle', borderWidth: 1 } });
    const ids = Array.from({ length: 4 }, (_, at) => {
      const child = addNode(project, parent.canvasId, 'media', `Child ${at}`);
      Object.assign(child, { parentId: parent.id, sizeMode: 'fixed', width: 200, height: 120, style: { shape: 'rectangle' } }); return child.id;
    });
    const current = reflowContainedChildren(project, parent.id), updated = current.nodes.find(node => node.id === parent.id)!;
    const inner = boundedChildInterior(current, updated), grid = gridGeometry(updated, current);
    assert.deepEqual([inner.x, inner.y, inner.width, inner.height], [106, 86, 788, 488]);
    assert.equal(grid.gap, 0); assert.equal(grid.cellWidth * grid.visibleColumns, inner.width); assert.equal(grid.cellHeight * grid.visibleRows, inner.height);
    const children = current.nodes.filter(node => ids.includes(node.id));
    assert.equal(Math.min(...children.map(node => node.x)), inner.x); assert.equal(Math.min(...children.map(node => node.y)), inner.y);
    assert.equal(Math.max(...children.map(node => node.x + node.width)), inner.x + inner.width);
    assert.equal(Math.max(...children.map(node => node.y + node.height)), inner.y + inner.height);
    assert.equal(updated.style?.childPadding, undefined, 'Rendering the default does not rewrite authored style.');
  }
});

test('explicit child insets remain authored values and do not change content padding or gaps', () => {
  assert.equal(DEFAULT_CHILD_INSET, 5);
  assert.deepEqual(nodeSpacing(undefined), { padding: 5, contentGap: 5, textMediaGap: 5, childPadding: 5 });
  for (const childPadding of [0, 5, 13, 40]) assert.deepEqual(nodeSpacing({ childPadding }), { padding: 5, contentGap: 5, textMediaGap: 5, childPadding });
});

test('every bounded display pattern shares the five-pixel inner inset while explicit spacing remains independent', () => {
  for (const containerVariant of ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'gallery', 'window'] as const) {
    const project = newProject('Universal bounded inset'), node = addNode(project, project.entryCanvasId, 'container', '');
    Object.assign(node, { x: 10, y: 20, width: 500, height: 300, sizeMode: 'fixed', collapsed: false, containerVariant, style: { shape: 'rectangle' } });
    assert.deepEqual(boundedChildInterior(project, node), { x: 16, y: 26, width: 488, height: 288 });
    node.style = { ...node.style, childPadding: 20 };
    assert.deepEqual(boundedChildInterior(project, node), { x: 31, y: 41, width: 458, height: 258 });
  }
});
