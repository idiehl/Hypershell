import type { ExpansionTray } from './types.ts';
import { EXPANSION_TRAY_HEADER, EXPANSION_TRAY_LEGACY_HEADER, EXPANSION_TRAY_LEGACY_PADDING, EXPANSION_TRAY_PADDING } from './expansion-tray-frame.ts';

const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);

export function isExpansionTraySize(value: unknown, layoutVersion?: 2 | 3): value is NonNullable<ExpansionTray['size']> {
  const padding = layoutVersion === 3 ? EXPANSION_TRAY_PADDING : EXPANSION_TRAY_LEGACY_PADDING;
  return record(value) && Object.keys(value).every(key => key === 'width' || key === 'height')
    && ['width', 'height'].every(key => typeof value[key] === 'number' && Number.isFinite(value[key]) && (value[key] as number) <= 1e9)
    && (value.width as number) > 2 * padding
    && (value.height as number) > 2 * padding + (layoutVersion ? EXPANSION_TRAY_HEADER : EXPANSION_TRAY_LEGACY_HEADER);
}

/** Inert source-relative presentation state. It cannot introduce graph identities or executable settings. */
export function isExpansionTray(value: unknown): value is ExpansionTray {
  if (!record(value) || Object.keys(value).some(key => !['offset', 'size', 'layoutVersion'].includes(key)) || !record(value.offset)
    || value.layoutVersion !== undefined && value.layoutVersion !== 2 && value.layoutVersion !== 3) return false;
  const offset = value.offset;
  return Object.keys(offset).every(key => key === 'x' || key === 'y')
    && ['x', 'y'].every(key => typeof offset[key] === 'number' && Number.isFinite(offset[key]) && Math.abs(offset[key] as number) <= 1e9)
    && (value.size === undefined || isExpansionTraySize(value.size, value.layoutVersion as 2 | 3 | undefined));
}
