import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, newProject } from '../src/core/model.ts';
import { OPERATION_DEFINITIONS } from '../src/core/operations.ts';
import { RULE_KEYS, RULE_VOCABULARY, effectiveInheritedRules, previewRuleChange } from '../src/core/rule-impact.ts';
import { validateProject } from '../src/core/rules.ts';
import type { MeridianProject, Rules } from '../src/core/types.ts';

function fixture() {
  const project = newProject('Inherited context'), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'container', 'Parent'); parent.childLayout = 'spatial';
  const child = addNode(project, canvasId, 'text', 'Child'); child.parentId = parent.id;
  const grandchild = addNode(project, canvasId, 'text', 'Grandchild'); grandchild.parentId = child.id;
  const sibling = addNode(project, canvasId, 'text', 'Sibling'); sibling.parentId = parent.id;
  const unrelated = addNode(project, canvasId, 'text', 'Unrelated');
  return { project, parent, child, grandchild, sibling, unrelated };
}

function freeze<T>(value: T): T {
  if (value && typeof value === 'object') {
    Object.freeze(value);
    for (const child of Object.values(value)) freeze(child);
  }
  return value;
}

test('rule vocabulary includes each supported operation and is immutable while effective rules are independent arrays', () => {
  assert.deepEqual(RULE_KEYS, ['nodeKinds', 'linkKinds', 'operations']);
  assert.deepEqual(RULE_VOCABULARY.operations, OPERATION_DEFINITIONS.map(item => item.operation));
  assert.equal(new Set(RULE_VOCABULARY.nodeKinds).size, 16);
  assert.ok(!RULE_VOCABULARY.nodeKinds.includes('block'));
  assert.ok(RULE_VOCABULARY.nodeKinds.includes('media'));
  assert.ok(RULE_VOCABULARY.nodeKinds.includes('spreadsheet'));
  assert.deepEqual(RULE_VOCABULARY.linkKinds, ['visual', 'functional', 'numeric']);
  assert.throws(() => (RULE_VOCABULARY.operations as string[]).push('FAKE'));
  const f = fixture(), rules = effectiveInheritedRules(f.project, f.parent.id); rules.operations.length = 0;
  assert.equal(effectiveInheritedRules(f.project, f.parent.id).operations.length, OPERATION_DEFINITIONS.length);
});

test('effective inherited rules intersect ancestors and exclude the node own rules', () => {
  const f = fixture(); f.project.nodes[0].rules = { operations: ['AND', 'OR'], linkKinds: ['visual', 'functional'] };
  f.parent.rules = { operations: ['AND'], linkKinds: ['visual'] }; f.child.rules = { operations: [] };
  assert.deepEqual(effectiveInheritedRules(f.project, f.parent.id).operations, ['AND', 'OR']);
  assert.deepEqual(effectiveInheritedRules(f.project, f.child.id).operations, ['AND']);
  assert.deepEqual(effectiveInheritedRules(f.project, f.grandchild.id).operations, []);
  assert.deepEqual(effectiveInheritedRules(f.project, f.child.id).linkKinds, ['visual']);
  assert.deepEqual(effectiveInheritedRules(f.project, f.project.canvases[0].rootId).nodeKinds, RULE_VOCABULARY.nodeKinds);
});

test('preview identifies hidden descendants and exact permission differences without changing nodes or the proposal', () => {
  const f = fixture(); f.parent.collapsed = true;
  const proposal: Rules = { nodeKinds: ['text'], linkKinds: ['visual'], operations: ['AND'] };
  const before = structuredClone(f.project), proposedBefore = structuredClone(proposal);
  freeze(f.project); freeze(proposal);
  const report = previewRuleChange(f.project, f.parent.id, proposal);
  assert.equal(report.descendantCount, 3); assert.equal(report.affected.length, 3); assert.equal(report.unchangedCount, 0);
  assert.equal(report.changed, true); assert.equal(report.canApply, true); assert.deepEqual(report.conflicts, []);
  assert.ok(report.affected.every(item => item.hidden));
  assert.ok(report.affected.every(item => item.nodeId !== f.parent.id && item.nodeId !== f.unrelated.id));
  const row = report.affected.find(item => item.nodeId === f.grandchild.id)!;
  assert.equal(row.title, 'Grandchild'); assert.equal(row.kind, 'text'); assert.deepEqual(row.after.nodeKinds, ['text']);
  assert.deepEqual(row.changes.find(item => item.key === 'linkKinds'), { key: 'linkKinds', removed: ['functional', 'numeric'], added: [] });
  assert.deepEqual(f.project, before); assert.deepEqual(proposal, proposedBefore);
  row.after.nodeKinds.length = 0;
  assert.deepEqual(report.affected.find(item => item.nodeId === f.child.id)!.after.nodeKinds, ['text']);
});

test('nested restrictions mask ancestor changes only below their own node and unchanged descendants are counted', () => {
  const f = fixture(); f.parent.rules = { operations: ['AND', 'OR'] }; f.child.rules = { operations: ['AND'] };
  const report = previewRuleChange(f.project, f.parent.id, { operations: ['AND'] });
  assert.equal(report.descendantCount, 3); assert.equal(report.affected.length, 2); assert.equal(report.unchangedCount, 1);
  assert.ok(report.affected.some(item => item.nodeId === f.child.id));
  assert.ok(report.affected.some(item => item.nodeId === f.sibling.id));
  assert.equal(report.affected.some(item => item.nodeId === f.grandchild.id), false);
  assert.ok(report.affected.every(item => item.changes[0].removed.join() === 'OR'));
});

test('a collapsed descendant is visible itself but hides descendants, including through a collapsed ancestor outside the preview branch', () => {
  const f = fixture(); f.child.collapsed = true;
  let report = previewRuleChange(f.project, f.parent.id, { operations: ['AND'] });
  assert.equal(report.affected.find(item => item.nodeId === f.child.id)?.hidden, false);
  assert.equal(report.affected.find(item => item.nodeId === f.grandchild.id)?.hidden, true);
  assert.equal(report.affected.find(item => item.nodeId === f.sibling.id)?.hidden, false);
  f.project.nodes[0].collapsed = true;
  report = previewRuleChange(f.project, f.parent.id, { operations: ['AND'] });
  assert.ok(report.affected.every(item => item.hidden));
});

test('no-op declarations do not enable Apply and semantic order or duplicate changes are ignored', () => {
  const f = fixture();
  for (const proposal of [undefined, {}, { operations: undefined }]) {
    const report = previewRuleChange(f.project, f.parent.id, proposal);
    assert.equal(report.changed, false); assert.equal(report.canApply, false); assert.equal(report.affected.length, 0); assert.equal(report.unchangedCount, 3);
  }
  f.parent.rules = { operations: ['AND', 'OR'] };
  const same = previewRuleChange(f.project, f.parent.id, { operations: ['OR', 'AND', 'AND'] });
  assert.equal(same.changed, false); assert.equal(same.canApply, false); assert.equal(same.affected.length, 0);
});

test('explicit full permissions remain a meaningful declaration even when current effective permissions do not change', () => {
  const f = fixture();
  const report = previewRuleChange(f.project, f.parent.id, { operations: [...RULE_VOCABULARY.operations] });
  assert.equal(report.changed, true); assert.equal(report.canApply, true); assert.equal(report.affected.length, 0); assert.equal(report.unchangedCount, 3);
});

test('a descendant cannot widen ancestor permissions even if the attempted additions have no effective impact', () => {
  const f = fixture(); f.project.nodes[0].rules = { operations: ['AND'] }; f.parent.rules = { operations: ['AND'] };
  const report = previewRuleChange(f.project, f.parent.id, { operations: ['AND', 'OR'] });
  assert.equal(report.changed, true); assert.equal(report.canApply, false); assert.equal(report.affected.length, 0);
  assert.ok(report.conflicts.some(issue => issue.nodeId === f.parent.id && /cannot widen/.test(issue.message)));
});

test('removing own restrictions broadens descendants only within ancestor permissions and retains nested masks', () => {
  const f = fixture(); f.project.nodes[0].rules = { operations: ['AND', 'OR'] }; f.parent.rules = { operations: ['AND'] }; f.child.rules = { operations: ['AND'] };
  const report = previewRuleChange(f.project, f.parent.id);
  assert.equal(report.canApply, true); assert.equal(report.affected.length, 2); assert.equal(report.unchangedCount, 1);
  assert.ok(report.affected.every(item => item.changes[0].added.join() === 'OR' && item.changes[0].removed.length === 0));
  f.project.nodes[0].rules = { operations: ['AND'] };
  const sameEffective = previewRuleChange(f.project, f.parent.id);
  assert.equal(sameEffective.changed, true); assert.equal(sameEffective.canApply, true); assert.equal(sameEffective.affected.length, 0);
});

test('narrowing the parent reports existing descendant rule declarations that would widen the new inheritance', () => {
  const f = fixture(); f.parent.rules = { operations: ['AND', 'OR'] }; f.child.rules = { operations: ['AND', 'OR'] };
  const report = previewRuleChange(f.project, f.parent.id, { operations: ['AND'] });
  assert.equal(report.canApply, false);
  assert.ok(report.conflicts.some(issue => issue.nodeId === f.child.id && /cannot widen/.test(issue.message)));
  assert.ok(report.affected.some(item => item.nodeId === f.grandchild.id && item.after.operations.join() === 'AND'));
});

test('empty permission lists mean no permissions and report existing node, joint, and crossing-link conflicts', () => {
  const f = fixture(); const joint = addNode(f.project, f.project.entryCanvasId, 'joint', 'AND joint'); joint.parentId = f.parent.id;
  const cross = { id: crypto.randomUUID(), canvasId: f.project.entryCanvasId, sourceId: f.child.id, targetId: f.unrelated.id, kind: 'visual' as const, relation: 'generic' as const };
  f.project.links.push(cross);
  const report = previewRuleChange(f.project, f.parent.id, { nodeKinds: [], linkKinds: [], operations: [] });
  assert.equal(report.canApply, false); assert.equal(report.affected.length, 4);
  assert.ok(report.affected.every(item => RULE_KEYS.every(key => item.after[key].length === 0)));
  assert.ok(report.conflicts.some(issue => issue.nodeId === f.child.id && /Node kind/.test(issue.message)));
  assert.ok(report.conflicts.some(issue => issue.nodeId === joint.id && /Joint operation/.test(issue.message)));
  assert.ok(report.conflicts.some(issue => issue.nodeId === f.child.id && issue.linkId === cross.id && /Link kind/.test(issue.message)));
  const leaf = previewRuleChange(f.project, f.unrelated.id, { nodeKinds: [], operations: [], linkKinds: [] });
  assert.equal(leaf.canApply, true); assert.equal(leaf.descendantCount, 0); assert.equal(leaf.affected.length, 0);
});

test('connection validation considers endpoints across the branch boundary rather than the disclosure owner alone', () => {
  const f = fixture();
  const inside = addNode(f.project, f.project.entryCanvasId, 'number', 'Inside source'); inside.parentId = f.parent.id;
  const outside = addNode(f.project, f.project.entryCanvasId, 'output', 'Outside output'); outside.valueType = 'number';
  const link = { id: crypto.randomUUID(), canvasId: f.project.entryCanvasId, sourceId: inside.id, targetId: outside.id, parentId: f.unrelated.id, kind: 'numeric' as const, relation: 'causal' as const };
  f.project.links.push(link);
  const report = previewRuleChange(f.project, f.parent.id, { linkKinds: ['visual'] });
  assert.equal(report.canApply, false);
  assert.ok(report.conflicts.some(issue => issue.linkId === link.id && issue.nodeId === inside.id));
  assert.equal(report.affected.some(item => item.nodeId === outside.id), false);
});

test('own rule changes do not apply to the selected joint itself but do apply to joints below it', () => {
  const project = newProject('Joint parent'), parent = addNode(project, project.entryCanvasId, 'joint', 'Parent AND');
  const leaf = previewRuleChange(project, parent.id, { operations: [] }); assert.equal(leaf.canApply, true);
  const child = addNode(project, project.entryCanvasId, 'joint', 'Child OR'); child.parentId = parent.id; child.operation = 'OR';
  const report = previewRuleChange(project, parent.id, { operations: ['AND'] });
  assert.equal(report.canApply, false); assert.equal(report.descendantCount, 1);
  assert.ok(report.conflicts.some(issue => issue.nodeId === child.id && /Joint operation/.test(issue.message)));
  assert.equal(report.conflicts.some(issue => issue.nodeId === parent.id), false);
});

test('preview rejects malformed proposals and unsupported values with the selected node identity and never mutates source data', () => {
  const f = fixture(), before = structuredClone(f.project);
  for (const proposed of [null, [], { operations: 'AND' }, { operations: [42] }, { unexpected: ['text'] }, { operations: ['UNSUPPORTED'] }]) {
    const report = previewRuleChange(f.project, f.parent.id, proposed as unknown as Rules);
    assert.equal(report.canApply, false); assert.equal(report.changed, true);
    assert.ok(report.conflicts.some(issue => issue.nodeId === f.parent.id));
  }
  assert.deepEqual(f.project, before);
});

test('missing nodes, missing ancestors and containment cycles fail promptly instead of hanging', () => {
  const f = fixture();
  assert.throws(() => previewRuleChange(f.project, 'missing'), /does not exist/);
  assert.throws(() => effectiveInheritedRules(f.project, 'missing'), /does not exist/);
  const malformed = { ...f.project, nodes: null } as unknown as MeridianProject;
  assert.throws(() => previewRuleChange(malformed, f.parent.id), /malformed/);
  f.parent.parentId = 'missing'; assert.throws(() => previewRuleChange(f.project, f.parent.id), /ancestor is missing/);
  f.parent.parentId = f.grandchild.id;
  assert.throws(() => previewRuleChange(f.project, f.parent.id), /containment cycle/);
  assert.throws(() => effectiveInheritedRules(f.project, f.child.id), /containment cycle/);
});

test('candidate conflicts from other canvases remain visible and cannot enable an invalid whole-project edit', () => {
  const f = fixture(), other = addCanvas(f.project, 'Other canvas'), root = f.project.nodes.find(node => node.id === other.rootId)!;
  root.rules = { nodeKinds: ['text'] }; const invalid = addNode(f.project, other.id, 'number', 'Invalid existing node');
  const report = previewRuleChange(f.project, f.parent.id, { operations: ['AND'] });
  assert.equal(report.canApply, false); assert.equal(report.descendantCount, 3);
  assert.ok(report.conflicts.some(issue => issue.nodeId === invalid.id));
  assert.equal(report.affected.some(item => item.nodeId === invalid.id), false);
});

test('effective permission previews match the existing validator on valid and conflicting proposed changes', () => {
  const f = fixture(); const joint = addNode(f.project, f.project.entryCanvasId, 'joint', 'Operation'); joint.parentId = f.child.id;
  const proposals: (Rules | undefined)[] = [undefined, { operations: ['AND'] }, { operations: ['OR'] }, { nodeKinds: ['text', 'joint'] }, { nodeKinds: ['text'] }, { linkKinds: [] }];
  for (const proposed of proposals) {
    const report = previewRuleChange(f.project, f.parent.id, proposed), candidate = structuredClone(f.project);
    const node = candidate.nodes.find(node => node.id === f.parent.id)!;
    if (proposed) node.rules = structuredClone(proposed); else delete node.rules;
    assert.deepEqual(report.conflicts, validateProject(candidate));
    for (const row of report.affected) {
      assert.deepEqual(row.before, effectiveInheritedRules(f.project, row.nodeId));
      assert.deepEqual(row.after, effectiveInheritedRules(candidate, row.nodeId));
    }
  }
});

test('wide trees are visited once in stable hierarchy order and report all hidden descendants', () => {
  const project = newProject('Wide graph'), parent = addNode(project, project.entryCanvasId, 'container', 'Parent'); parent.collapsed = true;
  for (let i = 0; i < 1000; i++) { const node = addNode(project, project.entryCanvasId, 'text', `Child ${i}`); node.parentId = parent.id; }
  const report = previewRuleChange(project, parent.id, { nodeKinds: ['text'] });
  assert.equal(report.descendantCount, 1000); assert.equal(report.affected.length, 1000); assert.equal(new Set(report.affected.map(item => item.nodeId)).size, 1000);
  assert.equal(report.affected[0].title, 'Child 0'); assert.equal(report.affected[999].title, 'Child 999'); assert.ok(report.affected.every(item => item.hidden));
  assert.equal(report.canApply, true);
});
