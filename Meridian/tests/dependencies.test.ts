import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { summarizeDependencies } from '../src/core/dependencies.ts';
import { validateProject } from '../src/core/rules.ts';

test('dependency inspection follows transitive declarations, references and functional inputs cycle safely', () => {
  const project = newProject(), a = addNode(project, project.entryCanvasId, 'input', 'A'), b = addNode(project, project.entryCanvasId, 'text', 'B');
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Alias'); reference.targetId = a.id;
  const out = addNode(project, project.entryCanvasId, 'output', 'Out');
  project.links.push({ id: 'wire', canvasId: project.entryCanvasId, sourceId: reference.id, targetId: out.id, kind: 'functional', relation: 'causal' });
  a.dependencyIds = [b.id]; b.dependencyIds = [a.id, 'unavailable'];
  const before = structuredClone(project), summary = summarizeDependencies(project, { rootIds: [out.id] });
  assert.deepEqual(project, before); assert.deepEqual(validateProject(project), []);
  assert.deepEqual(new Set(summary.local.map(item => item.id)), new Set([a.id, b.id, reference.id]));
  assert.equal(summary.missing[0].id, 'unavailable'); assert.equal(summary.entries.length, 4);
  assert.deepEqual(summary.entries.find(item => item.id === a.id)?.reasons.sort(), ['declared', 'reference']);
});

test('a snapshot is source context, not its entire required canvas', () => {
  const project = newProject(), reference = addNode(project, project.entryCanvasId, 'reference', 'Remote'); reference.targetId = 'external';
  project.snapshots.external = { node: { id: 'external', canvasId: 'outside', parentId: 'outside-root', kind: 'input', title: 'Saved input', x: 0, y: 0, width: 100, height: 100, dependencyIds: ['unavailable'] }, value: true, savedAt: new Date().toISOString() };
  project.dependencies.push({ canvasId: 'outside', title: 'Required external canvas', required: true });
  const report = summarizeDependencies(project);
  assert.equal(report.snapshots[0].id, 'external');
  assert.deepEqual(new Set(report.missing.map(item => item.id)), new Set(['unavailable', 'outside']));
  assert.match(report.missing.find(item => item.id === 'outside')!.note!, /do not include/);
  const excluded = summarizeDependencies(project, { includeExternalReferences: false });
  assert.equal(excluded.excluded[0].id, 'external'); assert.equal(excluded.snapshots.length, 0);
});

test('cross-canvas references disclose missing saves and preserve included saved context', () => {
  const project = newProject(), other = addCanvas(project, 'Source'), source = addNode(project, other.id, 'input', 'Source input');
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Pinned'); reference.targetId = source.id;
  assert.equal(summarizeDependencies(project).missing[0].id, source.id);
  const saved = commitSnapshots(project), report = summarizeDependencies(saved, { includeExternalReferences: false });
  assert.equal(report.missing.length, 0); assert.equal(report.excluded.length, 0); assert.equal(report.local[0].id, source.id);
  assert.match(report.local[0].note!, /saved snapshot/);
});

test('branch dependency inspection includes descendants but not unrelated branches or decorative links', () => {
  const project = newProject(), branch = addNode(project, project.entryCanvasId, 'container');
  const child = addNode(project, project.entryCanvasId, 'text'); child.parentId = branch.id; child.dependencyIds = ['required'];
  const unrelated = addNode(project, project.entryCanvasId, 'text'); unrelated.dependencyIds = ['irrelevant'];
  project.links.push({ id: 'decoration', canvasId: project.entryCanvasId, sourceId: unrelated.id, targetId: child.id, kind: 'visual', relation: 'causal' });
  assert.deepEqual(summarizeDependencies(project, { rootIds: [branch.id] }).entries.map(item => item.id), ['required']);
});

test('declarations reject duplicates, self-dependencies, overlong and malformed identity arrays', () => {
  const project = newProject(), node = addNode(project, project.entryCanvasId, 'text');
  for (const value of [['same', 'same'], [node.id], [' bad '], ['x'.repeat(97)], [1], Array.from({ length: 1001 }, (_, index) => `id${index}`)]) {
    node.dependencyIds = value as string[];
    assert.ok(validateProject(project).length);
  }
});
