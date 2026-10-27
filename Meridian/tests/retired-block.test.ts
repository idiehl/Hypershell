import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { normalizeRetiredBlocks } from '../src/core/retired-block.ts';
import { creationTypes } from '../src/core/creation.ts';
import { activeNodeContent } from '../src/core/content.ts';
import { RULE_VOCABULARY } from '../src/core/rule-impact.ts';
import { validateProject } from '../src/core/rules.ts';
import { createPublicationPayload } from '../src/publication.ts';

test('retiring Block preserves blank appearance, identity, dormant content, rules and pinned observations', () => {
  const project = newProject('Legacy shapes'), root = project.nodes[0]!;
  root.rules = { nodeKinds: ['block'] };
  const node = addNode(project, project.entryCanvasId, 'block', 'Dormant heading');
  Object.assign(node, { tag: 'Shape handle', text: 'Dormant passage', titleRuns: [{ start: 0, end: 7, style: { bold: true } }], style: { shape: 'hexagon', fill: '#123456' } });
  const saved = commitSnapshots(project), before = JSON.stringify(saved), migrated = normalizeRetiredBlocks(saved);
  const result = migrated.nodes.find(item => item.id === node.id)!;
  assert.equal(result.kind, 'media'); assert.equal(result.tag, 'Shape handle');
  assert.equal(result.title, ''); assert.equal(activeNodeContent(result).text, '');
  assert.deepEqual(result.style, node.style); assert.equal(result.width, node.width); assert.equal(result.height, node.height);
  assert.deepEqual(result.retiredBlockContent, { title: node.title, text: node.text, titleRuns: node.titleRuns });
  assert.deepEqual(migrated.nodes[0]!.rules?.nodeKinds, ['media']);
  assert.equal(migrated.snapshots[node.id]!.node.kind, 'media');
  assert.equal(migrated.snapshots[node.id]!.node.retiredBlockContent?.text, node.text);
  assert.equal(validateProject(migrated).length, 0);
  const portable = createPublicationPayload(migrated).project;
  assert.deepEqual(portable.nodes.find(item => item.id === node.id)!.retiredBlockContent, result.retiredBlockContent);
  assert.equal(normalizeRetiredBlocks(migrated), migrated); assert.equal(JSON.stringify(saved), before);
});

test('Block is absent from new-node choices and rule choices while blank Media remains available', () => {
  const project = newProject();
  assert.ok(!creationTypes(project, project.nodes[0]!.id).some(type => type.value === 'block'));
  assert.ok(creationTypes(project, project.nodes[0]!.id).some(type => type.value === 'media'));
  assert.ok(!RULE_VOCABULARY.nodeKinds.includes('block'));
});
