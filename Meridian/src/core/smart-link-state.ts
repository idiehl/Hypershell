import type { MeridianNode, SmartLinkSettings } from './types.ts';

export const SMART_LINK_DIRECTIONS = ['auto', 'right', 'down', 'left', 'up'] as const;
export const DEFAULT_SMART_LINK_GAP = 48;
export function isSmartLinkSettings(value: unknown): value is SmartLinkSettings {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const settings = value as Record<string, unknown>;
  return Object.keys(settings).every(key => ['direction', 'gap', 'resolvedDirection'].includes(key))
    && SMART_LINK_DIRECTIONS.includes(settings.direction as never)
    && typeof settings.gap === 'number' && Number.isFinite(settings.gap) && settings.gap >= 8 && settings.gap <= 10000
    && (settings.resolvedDirection === undefined || ['right', 'down', 'left', 'up'].includes(settings.resolvedDirection as string));
}
export function isSmartLink(node: MeridianNode | undefined): node is MeridianNode & { smartLink: SmartLinkSettings } {
  return !!node && node.kind === 'junction' && !!node.smartLink;
}
