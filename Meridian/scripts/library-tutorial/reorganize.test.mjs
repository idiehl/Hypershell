/** Preservation-contract checks against the checkpoint and staged candidate.
 * This suite is local-only and never reads or writes the live Meridian store. */
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { applyRecipe } from '../../src/core/authoring.ts';
import { libraryDocumentFromProject } from '../../src/core/library.ts';
import { DEFAULT_DIRECTORY, IDS, auditPreservation, hash, makeRecipe } from './reorganize.mjs';

const json = async file => JSON.parse((await readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const metadata = await json(path.join(DEFAULT_DIRECTORY, 'library-composition.json'));
const [baseline, candidate, catalog, tutorial, aliases] = await Promise.all([
  json(path.join(DEFAULT_DIRECTORY, 'library-baseline.json')),
  json(path.join(DEFAULT_DIRECTORY, 'library-candidate.json')),
  json(path.join(DEFAULT_DIRECTORY, 'catalog.json')),
  json(metadata.tutorialFile),
  json(path.join(DEFAULT_DIRECTORY, 'library.aliases.json')),
]);
const audit = value => auditPreservation(baseline, value, catalog, tutorial, aliases);
test('the saved user checkpoint is unchanged by repeated local composition', () => {
  const before = hash(baseline), recipe = makeRecipe(baseline, catalog, tutorial, aliases);
  const composed = applyRecipe(candidate.project, recipe);
  const repeated = libraryDocumentFromProject(baseline, composed.project);
  assert.equal(hash(baseline), before);
  assert.equal(hash(repeated), hash(candidate), 'Recomposition must retain new and original identities and avoid accumulating branch translations.');
  assert.equal(audit(repeated).passed, true);
});
test('changing an original external Work destination is rejected', () => {
  const changed = structuredClone(candidate), id = Object.keys(baseline.canvasTargets)[0];
  changed.canvasTargets[id] = { ...changed.canvasTargets[id], canvasId: tutorial.entryCanvasId };
  changed.project.nodes.find(node => node.id === id).canvasTarget = structuredClone(changed.canvasTargets[id]);
  assert.throws(() => audit(changed), /changed/);
});
test('removing discovery tombstones is rejected', () => {
  const changed = structuredClone(candidate), actual = new Set(Object.values(baseline.canvasTargets).map(target => `${target.projectId}:${target.canvasId ?? '*'}`));
  const deleted = baseline.discoveredTargets.find(key => !actual.has(key)); assert.ok(deleted);
  changed.discoveredTargets = changed.discoveredTargets.filter(key => key !== deleted);
  assert.throws(() => audit(changed), /discovery history/);
});
test('rewriting a saved observation or attachment is rejected', () => {
  const observation = structuredClone(candidate), first = Object.keys(observation.project.snapshots)[0]; assert.ok(first);
  observation.project.snapshots[first].savedAt = '2026-10-21T00:00:00Z';
  assert.throws(() => audit(observation), /snapshots changed/);
  const attachment = structuredClone(candidate); attachment.project.assets[0].name += ' altered';
  assert.throws(() => audit(attachment), /assets changed/);
});
test('hiding an existing branch through deletion or reparenting is rejected', () => {
  const changed = structuredClone(candidate);
  const original = baseline.project.nodes.find(node => node.kind === 'canvas-link' && baseline.project.nodes.find(parent => parent.id === node.parentId)?.kind === 'canvas-link');
  assert.ok(original);
  changed.project.nodes.find(node => node.id === original.id).parentId = IDS.mythology;
  assert.throws(() => audit(changed), /parentId changed/);
});
test('user-authored text and a second local Work remain protected', () => {
  const changed = structuredClone(candidate), original = baseline.project.nodes.find(node => node.kind === 'canvas-link');
  changed.project.nodes.find(node => node.id === original.id).text = 'A convenient replacement summary';
  assert.throws(() => audit(changed), /text changed/);
  const second = structuredClone(candidate), other = baseline.project.canvases.find(work => work.id !== baseline.project.entryCanvasId);
  assert.ok(other); second.project.nodes.find(node => node.id === other.rootId).title = 'Replaced';
  assert.throws(() => audit(second), /title changed/);
});
