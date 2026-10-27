import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { gridGeometry, normalizeContainerLayouts, reflowContainedChildren } from '../src/core/grid.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { rememberContainerSize } from '../src/core/container-size.ts';
import { boundedChildInterior } from '../src/core/sizing.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { createNodeFromSpec } from '../src/core/creation.ts';
import { nestNodeBranches } from '../src/core/nesting.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';
import { expansionTrayBounds } from '../src/core/expansion-tray.ts';
import type { ContainerVariant, MeridianNode, MeridianProject } from '../src/core/types.ts';

const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
function fixture(pattern: ContainerVariant = 'horizontal', kind: 'container' | 'media' = 'container') {
  const project = newProject('Nested disclosure boundaries'), owner = addNode(project, project.entryCanvasId, 'container', '');
  Object.assign(owner, { x: 100, y: 100, width: 840, height: 500, sizeMode: 'fixed', collapsed: false, containerVariant: pattern });
  owner.grid = { columns: 2, rows: 2, cellWidth: 420, cellHeight: 250, windowColumns: 2, windowRows: 2 };
  const branch = addNode(project, owner.canvasId, kind, 'Nested branch');
  Object.assign(branch, { tag: 'Nested branch handle', parentId: owner.id, x: 130, y: 150, width: 180, height: 80, sizeMode: 'fixed', collapsed: true });
  if (kind === 'container') branch.containerSizes = { expanded: { width: 500, height: 400, sizeMode: 'fixed' }, collapsed: { width: 180, height: 80, sizeMode: 'fixed' } };
  const leaves: MeridianNode[] = [];
  for (let at = 0; at < 2; at++) {
    const leaf = addNode(project, owner.canvasId, 'media', `Detail ${at}`);
    Object.assign(leaf, { parentId: branch.id, x: 2000 + at * 230, y: 1400, width: 200, height: 130, sizeMode: 'fixed', childPosition: 'auto', text: `Original detail ${at}`, collapsed: true });
    leaves.push(leaf);
  }
  for (let at = 0; at < 3; at++) {
    const peer = addNode(project, owner.canvasId, 'media', `Peer ${at}`);
    Object.assign(peer, { parentId: owner.id, x: 120, y: 120, width: 180, height: 80, sizeMode: 'fixed', collapsed: true });
  }
  return { project, owner, branch, leaves };
}

test('bounded nested disclosure retains authored mode frames while its ordered owner allocates a stable full cell', () => {
  for (const pattern of ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'] as const) {
    const f = fixture(pattern), frames = structuredClone(f.branch.containerSizes), identities = f.project.nodes.map(node => [node.id, node.parentId, node.title, node.text]);
    let current = reflowContainedChildren(f.project, f.owner.id);
    assert.deepEqual(get(current, f.branch.id).containerSizes, frames, `${pattern}: initial packing preserves authored frames`);
    for (let cycle = 0; cycle < 6; cycle++) {
      for (const expanded of [true, false]) {
        current = revealChildren(current, f.branch.id, expanded);
        current = reflowContainedChildren(current, f.owner.id);
        const owner = get(current, f.owner.id), branch = get(current, f.branch.id), cell = gridGeometry(owner, current);
        assert.deepEqual([owner.width, owner.height], [840, 500]);
        assert.deepEqual([branch.width, branch.height], [cell.cellWidth, cell.cellHeight]);
        assert.deepEqual(branch.containerSizes, frames, `${pattern}: ${expanded ? 'open' : 'closed'} cycle ${cycle}`);
        assert.equal(branch.collapsed, !expanded);
        if (expanded) {
          const inner = boundedChildInterior(current, branch), index = navigationIndex(current);
          for (const original of f.leaves) {
            const leaf = get(current, original.id), box = index.bounds(leaf);
            assert.ok(box.x >= inner.x - 1e-7 && box.y >= inner.y - 1e-7);
            assert.ok(box.x + box.width <= inner.x + inner.width + 1e-7 && box.y + box.height <= inner.y + inner.height + 1e-7);
          }
        }
        assert.deepEqual(current.nodes.map(node => [node.id, node.parentId, node.title, node.text]), identities);
        assert.deepEqual(validateProject(current), []);
        assert.deepEqual(normalizeContainerLayouts(current), current);
      }
    }
    const saved = JSON.parse(JSON.stringify(commitSnapshots(current))) as MeridianProject;
    assert.equal(isProjectShape(saved), true); assert.deepEqual(get(saved, f.branch.id).containerSizes, frames);
    assert.deepEqual(saved.snapshots[f.branch.id]!.node.containerSizes, frames);
  }
});

test('manual frame edits replace only the current mode record and automatic parent fitting retains the edit', () => {
  const f = fixture(), initial = reflowContainedChildren(f.project, f.owner.id), branch = get(initial, f.branch.id);
  Object.assign(branch, { width: 220, height: 95 }); rememberContainerSize(branch);
  const frames = structuredClone(branch.containerSizes), fitted = reflowContainedChildren(initial, f.owner.id);
  assert.deepEqual(get(fitted, f.branch.id).containerSizes, frames);
  assert.deepEqual(frames?.collapsed, { width: 220, height: 95, sizeMode: 'fixed' });
  assert.deepEqual(frames?.expanded, f.branch.containerSizes?.expanded);
  const opened = reflowContainedChildren(revealChildren(fitted, branch.id, true), f.owner.id);
  const closed = reflowContainedChildren(revealChildren(opened, branch.id, false), f.owner.id);
  assert.deepEqual(get(opened, branch.id).containerSizes, frames); assert.deepEqual(get(closed, branch.id).containerSizes, frames);
});

test('exterior descendants expand into a tray while their ordered allocation and original graph remain stable', () => {
  const f = fixture('horizontal', 'media'), packed = reflowContainedChildren(f.project, f.owner.id);
  const before = structuredClone(packed), exposed = revealChildren(packed, f.branch.id, true), arranged = reflowContainedChildren(exposed, f.owner.id);
  const frame = expansionTrayBounds(arranged, f.branch.id)!, owner = navigationIndex(arranged).bounds(get(arranged, f.owner.id));
  assert.ok(frame); assert.ok(frame.x >= owner.x + owner.width || frame.y >= owner.y + owner.height || frame.x + frame.width <= owner.x || frame.y + frame.height <= owner.y);
  assert.deepEqual(arranged.nodes.map(node => [node.id, node.parentId]), before.nodes.map(node => [node.id, node.parentId]));
  assert.deepEqual([get(arranged, f.owner.id).width, get(arranged, f.owner.id).height], [840, 500]);
  assert.deepEqual(get(arranged, f.branch.id).expansionTray, get(exposed, f.branch.id).expansionTray);
  assert.deepEqual(packed, before); assert.deepEqual(validateProject(arranged), []);
});

test('exterior Horizontal and Vertical descendants use usable tray cells without a subpixel cap at the parent wall', () => {
  for (const pattern of ['horizontal', 'vertical'] as const) {
    const f = fixture('horizontal', 'media'); f.branch.containerVariant = pattern;
    const packed = reflowContainedChildren(f.project, f.owner.id), exposed = revealChildren(packed, f.branch.id, true);
    const branch = get(exposed, f.branch.id), viewport = gridGeometry(branch, exposed), before = structuredClone(exposed), arranged = reflowContainedChildren(exposed, f.owner.id);
    assert.ok(viewport.cellHeight > 1 && viewport.cellWidth > 1, 'The tray is independent of the parent wall.');
    assert.ok(branch.expansionTray); assert.equal(branch.collapsed, false);
    for (const leaf of f.leaves) assert.ok(get(arranged, leaf.id).width > 1 && get(arranged, leaf.id).height > 1);
    assert.deepEqual(arranged.nodes.map(node => [node.id, node.parentId]), packed.nodes.map(node => [node.id, node.parentId]));
    assert.deepEqual(exposed, before); assert.equal(get(packed, f.branch.id).collapsed, true);
  }
});

test('creating, dropping or inserting a Module into a closed allocated container preserves both authored mode records', () => {
  for (const action of ['create', 'drop', 'module'] as const) {
    const f = fixture(), frames = structuredClone(f.branch.containerSizes), packed = reflowContainedChildren(f.project, f.owner.id);
    let current: MeridianProject;
    if (action === 'create') {
      current = createNodeFromSpec(packed, { type: 'media', canvasId: f.owner.canvasId, parentId: f.branch.id, title: '', tag: 'Created detail', x: 3000, y: 2000, width: 200, height: 120, sizeMode: 'fixed' }).project;
    } else {
      const source = addNode(packed, f.owner.canvasId, 'media', 'Outside detail');
      Object.assign(source, { x: 3000, y: 2000, width: 200, height: 120, sizeMode: 'fixed', collapsed: true });
      current = action === 'drop' ? nestNodeBranches(packed, [source.id], f.branch.id)
        : instantiateModule(packed, createModule(packed, source.id, 'Detail template'), { canvasId: f.owner.canvasId, parentId: f.branch.id, x: 3500, y: 2200 }).project;
    }
    assert.equal(get(current, f.branch.id).collapsed, false);
    assert.deepEqual(get(current, f.branch.id).containerSizes, frames, action);
    current = reflowContainedChildren(current, f.owner.id);
    assert.deepEqual(get(current, f.branch.id).containerSizes, frames, action);
    assert.deepEqual(validateProject(current), []);
  }
});
