import type { LinkKind, LogicValue, MeridianNode, MeridianProject, Operation, ValueType } from './types.ts';
import { resolveReferenceTarget } from './cells.ts';

export interface InputPort { name: string; type: ValueType }
export interface OperationDefinition {
  operation: Operation; label: string; symbol: string; family: 'Boolean' | 'Arithmetic' | 'Comparison' | 'Routing';
  inputs: readonly InputPort[]; output: ValueType;
}
const boolPair = [{ name: 'A', type: 'boolean' }, { name: 'B', type: 'boolean' }] as const;
const numberPair = [{ name: 'A', type: 'number' }, { name: 'B', type: 'number' }] as const;
export const OPERATION_DEFINITIONS: readonly OperationDefinition[] = [
  ...(['AND', 'OR', 'NAND', 'NOR', 'XOR', 'XNOR'] as const).map(operation => ({ operation, label: operation, symbol: operation, family: 'Boolean' as const, inputs: boolPair, output: 'boolean' as const })),
  { operation: 'NOT', label: 'NOT', symbol: '¬', family: 'Boolean', inputs: [{ name: 'A', type: 'boolean' }], output: 'boolean' },
  { operation: 'IDENTITY', label: 'Pass through', symbol: '→', family: 'Routing', inputs: [{ name: 'In', type: 'boolean' }], output: 'boolean' },
  ...([
    ['ADD', 'Add', '+'], ['SUBTRACT', 'Subtract', '−'], ['MULTIPLY', 'Multiply', '×'], ['DIVIDE', 'Divide', '÷'],
    ['MODULO', 'Remainder', '%'], ['POWER', 'Power', '^'], ['MIN', 'Minimum', 'min'], ['MAX', 'Maximum', 'max'],
  ] as const).map(([operation, label, symbol]) => ({ operation, label, symbol, family: 'Arithmetic' as const, inputs: numberPair, output: 'number' as const })),
  ...([['ABS', 'Absolute value', '|x|'], ['NEGATE', 'Negate', '−x']] as const).map(([operation, label, symbol]) => ({ operation, label, symbol, family: 'Arithmetic' as const, inputs: [{ name: 'A', type: 'number' as const }], output: 'number' as const })),
  ...([['EQUAL', 'Equal', '='], ['GREATER', 'Greater than', '>'], ['LESS', 'Less than', '<']] as const).map(([operation, label, symbol]) => ({ operation, label, symbol, family: 'Comparison' as const, inputs: numberPair, output: 'boolean' as const })),
];
const registry = new Map(OPERATION_DEFINITIONS.map(definition => [definition.operation, definition]));

export function operationDefinition(operation: Operation | undefined, valueType: ValueType = 'boolean'): OperationDefinition | undefined {
  const definition = operation && registry.get(operation);
  if (!definition) return undefined;
  return operation === 'IDENTITY' ? { ...definition, inputs: [{ name: 'In', type: valueType }], output: valueType } : definition;
}
export function inputPorts(node: MeridianNode): readonly InputPort[] {
  if (node.kind === 'joint') return operationDefinition(node.operation, node.valueType)?.inputs ?? [];
  if (node.kind === 'switch' || node.kind === 'output') return [{ name: 'In', type: node.valueType ?? 'boolean' }];
  if (node.kind === 'junction' && node.valueType) return [{ name: 'In', type: node.valueType }];
  return [];
}
export function isComputedLink(kind: LinkKind): boolean { return kind === 'functional' || kind === 'numeric'; }
export function valueTypeForLink(kind: LinkKind): ValueType | undefined { return kind === 'functional' ? 'boolean' : kind === 'numeric' ? 'number' : undefined; }
export function isValueOfType(value: LogicValue, type: ValueType): boolean { return typeof value === type && (typeof value !== 'number' || Number.isFinite(value)); }

/** References observe local live types or the saved type from another canvas. */
export function outputValueType(project: MeridianProject, node: MeridianNode, seen = new Set<string>()): ValueType | undefined {
  if (seen.has(node.id)) return undefined;
  seen.add(node.id);
  if (node.kind === 'input') return 'boolean';
  if (node.kind === 'number') return 'number';
  if (node.kind === 'switch' || node.kind === 'output') return node.valueType ?? 'boolean';
  if (node.kind === 'junction') return node.valueType;
  if (node.kind === 'joint') return operationDefinition(node.operation, node.valueType)?.output;
  if (node.kind === 'reference' && node.targetId) {
    const target = resolveReferenceTarget(project, node);
    if (target && !target.saved) return outputValueType(project, target.node, seen);
    const snapshot = target?.snapshot;
    if (snapshot) {
      if (typeof snapshot.value === 'number') return 'number';
      if (typeof snapshot.value === 'boolean') return 'boolean';
      // A saved reference's null result carries no reliable source type unless explicit.
      return snapshot.node.kind === 'reference' ? snapshot.node.valueType : outputValueType(project, snapshot.node, seen);
    }
  }
  return undefined;
}

/** Strict operands and finite results: no implicit Boolean/number coercion. */
export function applyOperation(operation: Operation, operands: readonly LogicValue[], valueType?: ValueType): LogicValue {
  const definition = operationDefinition(operation, valueType);
  if (!definition || definition.inputs.length !== operands.length || operands.some((value, index) => !isValueOfType(value, definition.inputs[index]!.type))) return null;
  const [a, b] = operands;
  let result: LogicValue;
  switch (operation) {
    case 'AND': result = a === true && b === true; break;
    case 'OR': result = a === true || b === true; break;
    case 'NOT': result = a !== true; break;
    case 'NAND': result = !(a === true && b === true); break;
    case 'NOR': result = !(a === true || b === true); break;
    case 'XOR': result = a !== b; break;
    case 'XNOR': result = a === b; break;
    case 'IDENTITY': result = a!; break;
    case 'ADD': result = (a as number) + (b as number); break;
    case 'SUBTRACT': result = (a as number) - (b as number); break;
    case 'MULTIPLY': result = (a as number) * (b as number); break;
    case 'DIVIDE': result = b === 0 ? null : (a as number) / (b as number); break;
    case 'MODULO': result = b === 0 ? null : (a as number) % (b as number); break;
    case 'POWER': result = (a as number) ** (b as number); break;
    case 'MIN': result = Math.min(a as number, b as number); break;
    case 'MAX': result = Math.max(a as number, b as number); break;
    case 'ABS': result = Math.abs(a as number); break;
    case 'NEGATE': result = -(a as number); break;
    case 'EQUAL': result = a === b; break;
    case 'GREATER': result = (a as number) > (b as number); break;
    case 'LESS': result = (a as number) < (b as number); break;
  }
  return typeof result === 'number' && !Number.isFinite(result) ? null : result;
}
