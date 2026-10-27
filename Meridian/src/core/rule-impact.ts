import type { MeridianNode, MeridianProject, NodeKind, Rules, ValidationIssue } from './types.ts';
import { OPERATION_DEFINITIONS } from './operations.ts';
import { isProjectShape, validateProject } from './rules.ts';
import { nodeTag } from './node-tag.ts';

export const RULE_KEYS = ['nodeKinds', 'linkKinds', 'operations'] as const;
export type RuleKey = typeof RULE_KEYS[number];
export type EffectiveRules = { [Key in RuleKey]: NonNullable<Rules[Key]> };

// A record makes a future NodeKind addition a compile-time reminder to update this vocabulary.
const nodeKinds: Record<NodeKind, true> = {
  canvas: true, container: true, spreadsheet: true, module: true, media: true, block: true, text: true, image: true, document: true,
  input: true, number: true, switch: true, output: true, joint: true, junction: true,
  reference: true, 'canvas-link': true,
};
export const RULE_VOCABULARY: { readonly [Key in RuleKey]: Readonly<NonNullable<Rules[Key]>> } = Object.freeze({
  nodeKinds: Object.freeze((Object.keys(nodeKinds) as NodeKind[]).filter(kind => kind !== 'block')),
  linkKinds: Object.freeze(['visual', 'functional', 'numeric'] as const),
  operations: Object.freeze(OPERATION_DEFINITIONS.map(definition => definition.operation)),
});

export interface RulePermissionChange { key: RuleKey; removed: string[]; added: string[] }
export interface AffectedRuleNode {
  nodeId: string; title: string; kind: NodeKind; hidden: boolean;
  before: EffectiveRules; after: EffectiveRules; changes: RulePermissionChange[];
}
export interface RuleImpactReport {
  nodeId: string; descendantCount: number; affected: AffectedRuleNode[]; unchangedCount: number;
  conflicts: ValidationIssue[]; canApply: boolean; changed: boolean;
}

function fullPermissions(): EffectiveRules {
  return { nodeKinds: [...RULE_VOCABULARY.nodeKinds], linkKinds: [...RULE_VOCABULARY.linkKinds], operations: [...RULE_VOCABULARY.operations] };
}

function clonePermissions(rules: EffectiveRules): EffectiveRules {
  return { nodeKinds: [...rules.nodeKinds], linkKinds: [...rules.linkKinds], operations: [...rules.operations] };
}

/** Empty declarations permit nothing. An omitted declaration inherits the parent's complete set. */
function restrict(inherited: EffectiveRules, own: Rules | undefined): EffectiveRules {
  const result = clonePermissions(inherited);
  for (const key of RULE_KEYS) {
    const values = own?.[key];
    if (Array.isArray(values)) {
      const allowed = new Set<string>(values);
      (result as Record<RuleKey, string[]>)[key] = inherited[key].filter(value => allowed.has(value));
    }
  }
  return result;
}

function requireProject(project: MeridianProject): Map<string, MeridianNode> {
  if (!isProjectShape(project)) throw new Error('Cannot preview rules for malformed Work data.');
  return new Map(project.nodes.map(node => [node.id, node]));
}

function ancestorsOf(nodes: Map<string, MeridianNode>, nodeId: string): MeridianNode[] {
  const node = nodes.get(nodeId);
  if (!node) throw new Error('The node for this rule preview does not exist.');
  const seen = new Set([nodeId]), ancestors: MeridianNode[] = [];
  let parentId = node.parentId;
  while (parentId) {
    if (seen.has(parentId)) throw new Error('Cannot preview inherited rules through a containment cycle.');
    seen.add(parentId);
    const parent = nodes.get(parentId);
    if (!parent) throw new Error('Cannot preview inherited rules because an ancestor is missing.');
    ancestors.push(parent); parentId = parent.parentId;
  }
  return ancestors;
}

/** The permissions applying to this node come from its ancestors, never its own descendant rules. */
export function effectiveInheritedRules(project: MeridianProject, nodeId: string): EffectiveRules {
  const nodes = requireProject(project);
  return ancestorsOf(nodes, nodeId).reduce((permissions, ancestor) => restrict(permissions, ancestor.rules), fullPermissions());
}

function sameDeclarations(before: Rules | undefined, after: Rules | undefined): boolean {
  return RULE_KEYS.every(key => {
    const a = before?.[key], b = after?.[key];
    if (a === undefined || b === undefined) return a === b;
    if (!Array.isArray(a) || !Array.isArray(b)) return false;
    const first = new Set(a), second = new Set(b);
    return first.size === second.size && [...first].every(value => second.has(value));
  });
}

function proposalShapeIssues(nodeId: string, proposed: Rules | undefined): ValidationIssue[] {
  if (proposed === undefined) return [];
  if (!proposed || typeof proposed !== 'object' || Array.isArray(proposed)) return [{ nodeId, message: 'Rules must be a set of permission lists or omitted to inherit.' }];
  const issues: ValidationIssue[] = [];
  for (const key of Object.keys(proposed)) if (!RULE_KEYS.includes(key as RuleKey)) issues.push({ nodeId, message: `Unknown rule category: ${key}.` });
  for (const key of RULE_KEYS) {
    const list = proposed[key];
    if (list !== undefined && (!Array.isArray(list) || !list.every(value => typeof value === 'string'))) issues.push({ nodeId, message: `The ${key} rule must be a list of supported names.` });
  }
  return issues;
}

/**
 * Preview a complete replacement of one node's own rules. Does not edit the project or proposal.
 * A single iterative walk propagates old/new effective permissions through hidden and visible descendants.
 * The ordinary graph validator supplies authoritative conflicts, including links crossing this branch.
 */
export function previewRuleChange(project: MeridianProject, nodeId: string, proposedRules?: Rules): RuleImpactReport {
  const nodes = requireProject(project), target = nodes.get(nodeId);
  if (!target) throw new Error('The node for this rule preview does not exist.');
  const ancestors = ancestorsOf(nodes, nodeId);
  const inherited = ancestors.reduce((permissions, ancestor) => restrict(permissions, ancestor.rules), fullPermissions());
  const proposalIssues = proposalShapeIssues(nodeId, proposedRules);
  const changed = proposalIssues.length > 0 || !sameDeclarations(target.rules, proposedRules);
  const replacement = { ...target };
  if (proposedRules === undefined) delete replacement.rules;
  else replacement.rules = proposedRules;
  // Validation is read-only; sharing unchanged media/snapshots avoids cloning large embedded assets per preview.
  const candidate = { ...project, nodes: project.nodes.map(node => node.id === nodeId ? replacement : node) };
  const candidateIssues = validateProject(candidate);
  const conflicts = proposalIssues.length ? [...proposalIssues, ...candidateIssues.filter(issue => !/malformed.*schema/i.test(issue.message))] : candidateIssues;
  const children = new Map<string, MeridianNode[]>();
  for (const node of project.nodes) if (node.parentId) {
    const list = children.get(node.parentId) ?? []; list.push(node); children.set(node.parentId, list);
  }
  const beforeChildren = restrict(inherited, target.rules), afterChildren = restrict(inherited, proposedRules);
  const hiddenChildren = !!target.collapsed || ancestors.some(node => node.collapsed);
  const pending = (children.get(nodeId) ?? []).map(node => ({ node, before: beforeChildren, after: afterChildren, hidden: hiddenChildren }));
  const visited = new Set([nodeId]), affected: AffectedRuleNode[] = [];
  let descendantCount = 0;
  for (let cursor = 0; cursor < pending.length; cursor++) {
    const { node, before, after, hidden } = pending[cursor];
    if (visited.has(node.id)) continue;
    visited.add(node.id); descendantCount++;
    const changes: RulePermissionChange[] = [];
    for (const key of RULE_KEYS) {
      const previous = new Set<string>(before[key]), next = new Set<string>(after[key]);
      const removed = before[key].filter(value => !next.has(value)), added = after[key].filter(value => !previous.has(value));
      if (removed.length || added.length) changes.push({ key, removed, added });
    }
    if (changes.length) affected.push({ nodeId: node.id, title: nodeTag(node), kind: node.kind, hidden,
      before: clonePermissions(before), after: clonePermissions(after), changes });
    const nestedBefore = restrict(before, node.rules), nestedAfter = restrict(after, node.rules);
    pending.push(...(children.get(node.id) ?? []).map(child => ({ node: child, before: nestedBefore, after: nestedAfter, hidden: hidden || !!node.collapsed })));
  }
  return { nodeId, descendantCount, affected, unchangedCount: descendantCount - affected.length, conflicts, canApply: changed && conflicts.length === 0, changed };
}
