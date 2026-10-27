import test from 'node:test';
import assert from 'node:assert/strict';
import type { MeridianProject, SnappingSettings } from '../src/core/types.ts';
import { GRID_SIZE, resolveSnapping, snapPosition, alignmentGuides, type SnapRect } from '../src/core/snapping.ts';
import { addCanvas, addNode, cloneProject, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';

const rect = (x: number, y: number, width = 20, height = 20, id?: string): SnapRect => ({ x, y, width, height, ...(id ? { id } : {}) });

test('settings default to sticky movement with smart guides and inherit each field independently with explicit false overrides', () => {
  assert.deepEqual(resolveSnapping(), { stickyEdges: true, snapToGrid: false, smartAlign: true });
  assert.deepEqual(resolveSnapping({ snapping: { stickyEdges: true, snapToGrid: true } }, { snapping: { stickyEdges: false, smartAlign: true } }), { stickyEdges: false, snapToGrid: true, smartAlign: true });
  assert.equal(resolveSnapping({ snapping: { smartAlign: false } }).smartAlign, false);
  assert.equal(resolveSnapping({ snapping: { smartAlign: true } }, { snapping: { smartAlign: false } }).smartAlign, false);
  assert.equal(resolveSnapping({ snapping: { smartAlign: false } }, { snapping: { smartAlign: true } }).smartAlign, true);
  const project = newProject();
  assert.equal(project.canvases[0].snapping, undefined);
  assert.equal(addNode(project, project.entryCanvasId, 'text').snapping, undefined);
});

test('all modes off retain exact fractional and negative coordinates without guides or mutation', () => {
  const moving = Object.freeze(rect(-23.75, 106.125, 50, 40));
  const neighbors = Object.freeze([Object.freeze(rect(-25, 150, 50, 40))]);
  assert.deepEqual(snapPosition(moving, neighbors, resolveSnapping({ snapping: { stickyEdges: false, smartAlign: false } })), { x: -23.75, y: 106.125, guides: [] });
});

test('grid uses world origin and 24-unit top-left phase, including negative coordinates', () => {
  assert.equal(GRID_SIZE, 24);
  assert.deepEqual(snapPosition(rect(37, -37, 107, 39), [], { snapToGrid: true }), { x: 48, y: -48, guides: [] });
  assert.deepEqual(snapPosition(rect(-12, 12), [], { snapToGrid: true }), { x: 0, y: 24, guides: [] });
  assert.deepEqual(snapPosition(rect(71, -49), [], { snapToGrid: true }, { scale: 0.3 }), { x: 72, y: -48, guides: [] });
});

test('sticky edges join either horizontal side while orthogonal spans overlap', () => {
  const target = rect(128, 45, 80, 30);
  const right = snapPosition(rect(83, 40, 40, 40), [target], { stickyEdges: true });
  assert.deepEqual(right, { x: 88, y: 40, guides: [{ axis: 'x', position: 128, from: 40, to: 80, kind: 'sticky' }] });
  assert.equal(snapPosition(rect(211, 50, 40, 40), [target], { stickyEdges: true }).x, 208);
});

test('sticky edges join either vertical side and do not join diagonally separated spans', () => {
  const target = rect(30, 100, 60, 50);
  assert.equal(snapPosition(rect(40, 76, 30, 20), [target], { stickyEdges: true }).y, 80);
  assert.equal(snapPosition(rect(40, 154, 30, 20), [target], { stickyEdges: true }).y, 150);
  assert.deepEqual(snapPosition(rect(83, 0, 40, 10), [rect(128, 25, 80, 30)], { stickyEdges: true }), { x: 83, y: 0, guides: [] });
});

test('sticky capture tolerance stays eight screen pixels across zoom levels', () => {
  const moving = rect(82, 40, 40, 40), target = rect(128, 45, 80, 30);
  assert.equal(snapPosition(moving, [target], { stickyEdges: true }, { scale: 1 }).x, 88);
  assert.equal(snapPosition(moving, [target], { stickyEdges: true }, { scale: 2 }).x, 82);
  assert.equal(snapPosition(rect(75, 40, 40, 40), [target], { stickyEdges: true }, { scale: 0.5 }).x, 88);
  assert.equal(snapPosition(moving, [target], { stickyEdges: true }, { tolerancePx: 5 }).x, 82);
});

test('smart alignment captures matching left, right and center lines of differently sized nodes', () => {
  const target = rect(100, 160, 100, 40);
  for (const [proposed, expected, line] of [[105, 100, 100], [174, 180, 200], [139, 140, 150]]) {
    const result = snapPosition(rect(proposed, 100), [target], { smartAlign: true });
    assert.equal(result.x, expected);
    assert.deepEqual(result.guides, [{ axis: 'x', position: line, from: 100, to: 200, kind: 'align' }]);
  }
});

test('smart alignment captures matching top, bottom and vertical-center lines', () => {
  const target = rect(160, 100, 40, 100);
  for (const [proposed, expected, line] of [[105, 100, 100], [174, 180, 200], [139, 140, 150]]) {
    const result = snapPosition(rect(100, proposed), [target], { smartAlign: true });
    assert.equal(result.y, expected);
    assert.deepEqual(result.guides, [{ axis: 'y', position: line, from: 100, to: 200, kind: 'align' }]);
  }
});

test('smart alignment ignores distant rows and columns and respects screen-space proximity', () => {
  const moving = rect(105, 0);
  assert.equal(snapPosition(moving, [rect(100, 350, 100, 30)], { smartAlign: true }).x, 105);
  assert.equal(snapPosition(moving, [rect(100, 350, 100, 30)], { smartAlign: true }, { scale: 0.5 }).x, 100);
  assert.equal(snapPosition(moving, [rect(100, 100, 100, 30)], { smartAlign: true }, { alignmentRangePx: 60 }).x, 105);
});

test('sticky or smart captures override grid only on their captured axis', () => {
  const sticky = snapPosition(rect(83, 37, 40, 40), [rect(128, 25, 80, 90)], { stickyEdges: true, snapToGrid: true });
  assert.deepEqual(sticky, { x: 88, y: 48, guides: [{ axis: 'x', position: 128, from: 25, to: 115, kind: 'sticky' }] });
  const smart = snapPosition(rect(105, 37), [rect(100, 100, 100, 20)], { smartAlign: true, snapToGrid: true });
  assert.deepEqual(smart, { x: 100, y: 48, guides: [{ axis: 'x', position: 100, from: 48, to: 120, kind: 'align' }] });
});

test('axes independently capture different neighbors, including negative world coordinates', () => {
  const result = snapPosition(rect(-53, -53), [rect(-30, -60, 20, 60), rect(-60, -30, 60, 20)], { stickyEdges: true });
  assert.deepEqual(result, { x: -50, y: -50, guides: [
    { axis: 'x', position: -30, from: -60, to: 0, kind: 'sticky' },
    { axis: 'y', position: -30, from: -60, to: 0, kind: 'sticky' },
  ] });
});

test('grid correction on the other axis cannot leave sticky contact or alignment outside its eligible span', () => {
  assert.deepEqual(snapPosition(rect(83, 11, 40, 10), [rect(128, 20, 80, 5)], { stickyEdges: true, snapToGrid: true }), { x: 72, y: 0, guides: [] });
  assert.deepEqual(snapPosition(rect(105, 11, 20, 10), [rect(100, 340, 100, 20)], { smartAlign: true, snapToGrid: true }), { x: 96, y: 0, guides: [] });
});

test('nearest capture wins and exact sticky/alignment ties favor touching edges', () => {
  const moving = rect(90, 50);
  assert.equal(snapPosition(moving, [rect(115, 50), rect(107, 50)], { stickyEdges: true }).x, 87);
  const result = snapPosition(moving, [rect(85, 100), rect(115, 50)], { stickyEdges: true, smartAlign: true, snapToGrid: true });
  assert.equal(result.x, 95);
  assert.equal(result.guides.find(guide => guide.axis === 'x')?.kind, 'sticky');
  // A closer alignment still wins over a farther touching-edge target.
  assert.equal(snapPosition(moving, [rect(91, 100), rect(115, 50)], { stickyEdges: true, smartAlign: true }).x, 91);
});

test('equal-distance ties do not depend on candidate order, and self/invalid candidates are ignored', () => {
  const moving = rect(90, 40, 20, 20, 'moving');
  const targets = [rect(113, 40, 20, 20, 'a'), rect(107, 40, 20, 20, 'b')];
  const result = snapPosition(moving, targets, { stickyEdges: true });
  assert.equal(result.x, 87);
  assert.deepEqual(snapPosition(moving, [...targets].reverse(), { stickyEdges: true }), result);
  assert.deepEqual(snapPosition(moving, [rect(111, 40, 20, 20, 'moving'), rect(Infinity, 40)], { stickyEdges: true }), { x: 90, y: 40, guides: [] });
});

function settingsProject(): MeridianProject {
  let project = newProject('Snapping persistence');
  project.canvases[0].snapping = { stickyEdges: true, snapToGrid: true, smartAlign: false };
  const note = addNode(project, project.entryCanvasId, 'text', 'Snap note');
  note.snapping = { stickyEdges: false, smartAlign: true };
  const second = addCanvas(project, 'Snapshot consumer');
  const reference = addNode(project, second.id, 'reference'); reference.targetId = note.id;
  project = commitSnapshots(project);
  return project;
}

test('settings survive JSON persistence, clone, saved snapshots and forks without sharing mutable objects', () => {
  const project = settingsProject(), note = project.nodes.find(n => n.title === 'Snap note')!;
  for (const copy of [JSON.parse(JSON.stringify(project)), cloneProject(project), forkProject(project)] as MeridianProject[]) {
    assert.deepEqual(copy.canvases[0].snapping, project.canvases[0].snapping);
    const copiedNode = copy.nodes.find(n => n.title === note.title)!;
    assert.deepEqual(copiedNode.snapping, note.snapping);
    assert.deepEqual(copy.snapshots[copiedNode.id].node.snapping, note.snapping);
    copy.canvases[0].snapping!.stickyEdges = false; copiedNode.snapping!.smartAlign = false;
    assert.equal(project.canvases[0].snapping!.stickyEdges, true); assert.equal(note.snapping!.smartAlign, true);
  }
});

test('publication retains only the three snapping fields for canvases, nodes and pinned snapshots', () => {
  const project = settingsProject(), note = project.nodes.find(n => n.title === 'Snap note')!;
  for (const settings of [project.canvases[0].snapping, note.snapping, project.snapshots[note.id].node.snapping]) Object.assign(settings!, { privatePath: 'C:/private/snapping' });
  const payload = createPublicationPayload(project);
  assert.ok(!JSON.stringify(payload).includes('private'));
  assert.deepEqual(payload.project.canvases[0].snapping, { stickyEdges: true, snapToGrid: true, smartAlign: false });
  assert.deepEqual(payload.project.snapshots[note.id].node.snapping, { stickyEdges: false, smartAlign: true });
  const imported = readPublication(buildPublication(project, '', ''));
  assert.notEqual(imported.id, project.id);
  const importedNote = imported.nodes.find(n => n.title === note.title)!;
  assert.notEqual(importedNote.id, note.id);
  assert.deepEqual(importedNote.snapping, { stickyEdges: false, smartAlign: true });
  assert.deepEqual(imported.snapshots[importedNote.id].node.snapping, importedNote.snapping);
  assert.deepEqual(imported.canvases[0].snapping, payload.project.canvases[0].snapping);
  assert.deepEqual(validateProject(imported), []);
});

test('old documents remain unchanged and valid when no snapping settings are supplied', () => {
  const project = newProject();
  assert.equal(isProjectShape(project), true);
  const imported = readPublication(buildPublication(project, '', ''));
  assert.equal(imported.canvases[0].snapping, undefined); assert.equal(imported.nodes[0].snapping, undefined);
  assert.deepEqual(resolveSnapping(imported.canvases[0], imported.nodes[0]), { stickyEdges: true, snapToGrid: false, smartAlign: true });
});

test('smart guides capture all nine edge/center combinations on either axis without requiring matching node sizes', () => {
  for (const axis of ['x', 'y'] as const) for (const sourceAt of [0, .5, 1]) for (const targetAt of [0, .5, 1]) {
    const target = axis === 'x' ? rect(200, 300, 180, 120, 'reference') : rect(300, 200, 120, 180, 'reference');
    const sourceMainSize = 40, targetMainSize = 180;
    const expected = 200 + targetMainSize * targetAt - sourceMainSize * sourceAt;
    const moving = axis === 'x' ? rect(expected + 3, 80, sourceMainSize, 60, 'moving') : rect(80, expected + 3, 60, sourceMainSize, 'moving');
    const result = snapPosition(moving, [target], { smartAlign: true });
    assert.equal(result[axis], expected, `${axis}: source ${sourceAt} to reference ${targetAt}`);
    const guide = result.guides.find(guide => guide.axis === axis)!;
    assert.equal(guide.position, 200 + targetMainSize * targetAt);
    assert.equal(guide.from, 80); assert.equal(guide.to, 420);
  }
});

test('smart alignment tolerance and reference search remain screen-based at deep and shallow zoom', () => {
  for (const scale of [.05, .5, 1, 4, 100]) {
    const target = rect(200 / scale, 250 / scale, 180 / scale, 120 / scale);
    assert.equal(snapPosition(rect(207 / scale, 80 / scale, 40 / scale, 60 / scale), [target], { smartAlign: true }, { scale }).x, 200 / scale);
    assert.equal(snapPosition(rect(209 / scale, 80 / scale, 40 / scale, 60 / scale), [target], { smartAlign: true }, { scale }).x, 209 / scale);
    assert.equal(snapPosition(rect(207 / scale, -140 / scale, 40 / scale, 60 / scale), [target], { smartAlign: true }, { scale }).x, 207 / scale);
  }
});

test('an equally aligned nearby reference wins over a distant line, independently of candidate order', () => {
  const moving = rect(103, 100, 40, 20);
  const near = rect(100, 160, 80, 50, 'near'), far = rect(100, 400, 80, 50, 'far');
  const result = snapPosition(moving, [far, near], { smartAlign: true });
  assert.deepEqual(result, snapPosition(moving, [near, far], { smartAlign: true }));
  assert.deepEqual(result.guides.find(guide => guide.axis === 'x'), { axis: 'x', position: 100, from: 100, to: 210, kind: 'align' });
});

test('shape-aware guides align visible circle edges rather than invisible frame corners', () => {
  const moving = { ...rect(103, 80, 120, 40), shape: 'circle' as const };
  const target = { ...rect(170, 200, 60, 60), shape: 'circle' as const };
  // Moving physical left is 143; its center is 163, which captures target left 170.
  const result = snapPosition(moving, [target], { smartAlign: true });
  assert.equal(result.x, 110);
  assert.deepEqual(result.guides, [{ axis: 'x', position: 170, from: 80, to: 260, kind: 'align' }]);
});

test('post-collision guides describe only exact final edges and retain an unaffected aligned axis', () => {
  const candidates = [rect(100, 300, 80, 60, 'below'), rect(300, 100, 80, 60, 'right')];
  const proposed = snapPosition(rect(104, 104, 40, 40, 'moving'), candidates, { smartAlign: true });
  assert.deepEqual([proposed.x, proposed.y], [100, 100]); assert.equal(proposed.guides.length, 2);
  // An obstacle moved the actual x coordinate after capture; its old x line
  // must disappear, but the still-correct y alignment remains useful.
  const final = Object.freeze(rect(119, 100, 40, 40, 'moving'));
  const guides = alignmentGuides(final, candidates, { smartAlign: true, snapToGrid: true });
  assert.deepEqual(guides, [{ axis: 'y', position: 100, from: 119, to: 380, kind: 'align' }]);
  assert.deepEqual(final, rect(119, 100, 40, 40, 'moving'));
  assert.deepEqual(alignmentGuides(rect(119, 119, 40, 40), candidates, { smartAlign: true }), []);
  assert.deepEqual(alignmentGuides(rect(100, 100, 40, 40), candidates, { snapToGrid: true }), []);
});

test('post-collision guides use physical sticky contact and never show a nearby gap as a connection', () => {
  const reference = rect(200, 100, 80, 100);
  assert.equal(alignmentGuides(rect(160, 100, 40, 100), [reference], { stickyEdges: true }).length, 1);
  assert.deepEqual(alignmentGuides(rect(159.5, 100, 40, 100), [reference], { stickyEdges: true }), []);
  assert.deepEqual(alignmentGuides(rect(160, 100, 40, 100), [reference], { stickyEdges: false, smartAlign: false }), []);
});

test('shape validation rejects non-Boolean settings and malformed setting objects on canvas, node and snapshot', () => {
  for (const location of ['canvas', 'node', 'snapshot']) {
    for (const key of ['stickyEdges', 'snapToGrid', 'smartAlign'] as const) {
      for (const invalid of [null, 'true', 'false', 0, 1, [], {}]) {
        const project = settingsProject(), note = project.nodes.find(n => n.title === 'Snap note')!;
        const owner = location === 'canvas' ? project.canvases[0] : location === 'node' ? note : project.snapshots[note.id].node;
        owner.snapping = { [key]: invalid } as SnappingSettings;
        assert.equal(isProjectShape(project), false, `${location}.${key} rejects ${JSON.stringify(invalid)}`);
        assert.ok(validateProject(project).length);
      }
    }
    for (const invalid of [null, true, 'on', []]) {
      const project = settingsProject(), note = project.nodes.find(n => n.title === 'Snap note')!;
      const owner = location === 'canvas' ? project.canvases[0] : location === 'node' ? note : project.snapshots[note.id].node;
      owner.snapping = invalid as unknown as SnappingSettings;
      assert.equal(isProjectShape(project), false, `${location} rejects ${JSON.stringify(invalid)}`);
    }
  }
});
