import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { createModule, createModuleFromSelection, instantiateModule, validateModule } from '../src/core/modules.ts';
import { normalizeContainerLayouts } from '../src/core/grid.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { expansionTrayBounds } from '../src/core/expansion-tray.ts';
import { validateProject } from '../src/core/rules.ts';
import type { MeridianProject } from '../src/core/types.ts';

function fixture() {
  const project = newProject('Module tray capture'), owner = addNode(project, project.entryCanvasId, 'container', 'Owner');
  Object.assign(owner, { x: 40, y: 60, width: 600, height: 300, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal', childArrangement: 'row' });
  const source = addNode(project, project.entryCanvasId, 'media', 'Source');
  Object.assign(source, { parentId: owner.id, width: 190, height: 120, sizeMode: 'fixed', collapsed: true, containerVariant: 'horizontal', childArrangement: 'row' });
  const children = Array.from({ length: 2 }, (_, at) => {
    const child = addNode(project, project.entryCanvasId, 'media', `Original ${at}`);
    Object.assign(child, { parentId: source.id, x: 800 + at * 220, y: 120, width: 190, height: 120, sizeMode: 'fixed', collapsed: true }); return child;
  });
  project.links.push({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: children[0]!.id, targetId: children[1]!.id, kind: 'visual', relation: 'generic', points: [{ x: 1000, y: 100 }] });
  return { project: revealChildren(normalizeContainerLayouts(project), source.id, true), ownerId: owner.id, sourceId: source.id };
}
const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
function outsideAncestors(project: MeridianProject, sourceId: string) {
  const nav = navigationIndex(project), frame = expansionTrayBounds(project, sourceId)!; assert.ok(frame);
  for (const ancestor of nav.ancestors(sourceId).filter(node => ['container', 'spreadsheet', 'module'].includes(node.kind))) {
    const bounds = nav.bounds(ancestor);
    assert.ok(frame.x >= bounds.x + bounds.width || frame.y >= bounds.y + bounds.height || frame.x + frame.width <= bounds.x || frame.y + frame.height <= bounds.y, `${ancestor.title}: tray stays outside each bounded ancestor`);
  }
}

test('Module wrappers measure source covers while keeping original tray nodes and their remembered presentation', () => {
  const f = fixture(), source = get(f.project, f.sourceId), before = structuredClone(f.project), nav = navigationIndex(f.project);
  const ids = [f.ownerId, ...nav.descendants(f.ownerId).map(node => node.id)];
  const selected = createModuleFromSelection(f.project, ids, 'Selection tray'), savedSource = selected.project.nodes.find(node => node.tag === source.tag)!;
  const wrapper = get(selected.project, selected.rootId), owner = get(f.project, f.ownerId);
  assert.equal(wrapper.width, owner.width + 12, 'A distant tray cannot inflate the new enclosure.');
  assert.deepEqual(savedSource.expansionTray, source.expansionTray); assert.equal(savedSource.collapsed, false);
  outsideAncestors(selected.project, savedSource.id); assert.deepEqual(validateModule(selected), []);
  const branch = createModule(f.project, f.sourceId, 'Single source tray'), branchSource = branch.project.nodes.find(node => node.tag === source.tag)!;
  assert.equal(get(branch.project, branch.rootId).width, Math.max(480, source.width + 48));
  assert.deepEqual(branchSource.expansionTray, source.expansionTray); outsideAncestors(branch.project, branchSource.id);
  const destination = newProject('Independent insertion'), inserted = instantiateModule(destination, selected, { canvasId: destination.entryCanvasId, x: 2000, y: 1400 });
  const copiedSource = get(inserted.project, inserted.idMap[savedSource.id]);
  assert.deepEqual(copiedSource.expansionTray, savedSource.expansionTray); assert.equal(copiedSource.collapsed, false); outsideAncestors(inserted.project, copiedSource.id);
  assert.deepEqual(validateProject(inserted.project), []); assert.deepEqual(f.project, before);
});

test('inserting an older roomy Module repairs a contained tray without moving its source or changing the stored template', () => {
  const f = fixture(), module = createModuleFromSelection(f.project, [f.ownerId, ...navigationIndex(f.project).descendants(f.ownerId).map(node => node.id)], 'Legacy roomy wrapper');
  const wrapper = get(module.project, module.rootId), source = module.project.nodes.find(node => node.tag === 'Source')!;
  Object.assign(wrapper, { x: 0, y: 0, width: 2400, height: 1600 });
  const before = structuredClone(module), destination = newProject('Legacy insertion');
  const inserted = instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 3000, y: 2200 });
  const copiedSource = get(inserted.project, inserted.idMap[source.id]), copiedRoot = get(inserted.project, inserted.rootId);
  assert.equal(copiedSource.x, source.x + copiedRoot.x - wrapper.x); assert.equal(copiedSource.y, source.y + copiedRoot.y - wrapper.y);
  assert.notDeepEqual(copiedSource.expansionTray, source.expansionTray, 'A newly conflicting enclosure is repaired without discarding the tray.');
  outsideAncestors(inserted.project, copiedSource.id); assert.deepEqual(module, before);
  assert.equal(inserted.project.nodes.length, module.project.nodes.length); assert.deepEqual(validateProject(inserted.project), []);
});
