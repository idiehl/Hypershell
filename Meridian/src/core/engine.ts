import type { Evaluation, LogicValue, MeridianProject as Project } from './types.ts';
import { isProjectShape, validateProject } from './rules.ts';
import { applyOperation, inputPorts, isComputedLink, isValueOfType } from './operations.ts';
import { resolveReferenceTarget } from './cells.ts';

export function evaluate(project: Project): Evaluation {
  const values: Record<string, LogicValue> = Object.create(null);
  const errors: Record<string, string> = Object.create(null);
  if (!isProjectShape(project)) {
    // Keep failed evaluation explicit for callers already displaying a partially repaired graph.
    const partial = project as unknown as { nodes?: Array<{ id?: unknown } | null> } | null;
    if (Array.isArray(partial?.nodes)) for (const node of partial.nodes) {
      if (node && typeof node.id === 'string') values[node.id] = null;
    }
    errors.project = 'Project data is malformed or has an unsupported schema.';
    return { values, errors };
  }
  const nodes = new Map(project.nodes.map(node => [node.id, node]));
  const incoming = new Map<string, typeof project.links>();
  for (const link of project.links) if (isComputedLink(link.kind)) {
    const list = incoming.get(link.targetId) ?? [];
    list.push(link); incoming.set(link.targetId, list);
  }
  for (const issue of validateProject(project)) {
    if (issue.nodeId) errors[issue.nodeId] ??= issue.message;
    if (issue.linkId) {
      const link = project.links.find(item => item.id === issue.linkId);
      if (link) errors[link.targetId] ??= issue.message;
    }
  }
  const visiting = new Set<string>();
  const read = (nodeId: string): LogicValue => {
    if (Object.hasOwn(values, nodeId)) return values[nodeId]!;
    const node = nodes.get(nodeId);
    if (!node) return null;
    if (visiting.has(nodeId)) { errors[nodeId] = 'Functional feedback cycles are not supported yet.'; return null; }
    if (errors[nodeId]) { values[nodeId] = null; return null; }
    visiting.add(nodeId);
    let value: LogicValue = null;
    if (node.kind === 'input') {
      if (typeof node.value === 'boolean') value = node.value;
      else errors[nodeId] = 'Input has no Boolean value.';
    } else if (node.kind === 'number') {
      if (typeof node.value === 'number' && Number.isFinite(node.value)) value = node.value;
      else errors[nodeId] = 'Input has no finite numeric value.';
    } else if (node.kind === 'reference') {
      const target = resolveReferenceTarget(project, node);
      if (target && !target.saved) value = read(target.node.id);
      else if (target?.snapshot) value = typeof target.snapshot.value === 'boolean' || typeof target.snapshot.value === 'number' && Number.isFinite(target.snapshot.value) ? target.snapshot.value : null;
      else errors[nodeId] = node.targetCell ? 'Referenced cell is empty or has no available saved snapshot.' : 'Referenced node has no available saved snapshot.';
    } else if (['switch', 'joint', 'output'].includes(node.kind) || node.kind === 'junction' && node.valueType) {
      if (node.kind === 'switch' && node.closed !== true) value = null;
      else {
        const ports = inputPorts(node);
        const links = incoming.get(nodeId) ?? [];
        const operands = ports.map((_port, index) => {
          const link = links.find(item => (item.targetPort ?? 0) === index);
          return link ? read(link.sourceId) : null;
        });
        if (!ports.length) errors[nodeId] = 'Choose a supported joint operation.';
        else if (operands.some(operand => operand === null)) errors[nodeId] = 'One or more inputs are undefined.';
        else if (operands.some((operand, index) => !isValueOfType(operand, ports[index]!.type))) errors[nodeId] = 'Input value does not match the required type.';
        else if (node.kind !== 'joint') value = operands[0]!;
        else if (node.operation) {
          value = applyOperation(node.operation, operands, node.valueType);
          if (value === null) errors[nodeId] = (node.operation === 'DIVIDE' || node.operation === 'MODULO') && operands[1] === 0 ? 'Division by zero is undefined.' : 'Operation has no finite result.';
        }
      }
    }
    visiting.delete(nodeId);
    values[nodeId] = value;
    return value;
  };
  for (const node of project.nodes) read(node.id);
  return { values, errors };
}
