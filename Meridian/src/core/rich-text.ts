import type { TextFormat, TextRun } from './types.ts';
import { isFontFamily } from './fonts.ts';

export type { TextFormat, TextRun } from './types.ts';
export const TEXT_FORMAT_KEYS = ['fontFamily', 'fontSize', 'color', 'bold', 'italic', 'underline'] as const;
export const TEXT_RUN_LIMIT = 4096;
export const TEXT_FONT_SIZE_LIMITS = { min: 1, max: 512 } as const;
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const own = (value: object, key: PropertyKey): boolean => Object.hasOwn(value, key);
const validSetting = (key: string, value: unknown): boolean => {
  if (key === 'fontFamily') return isFontFamily(value);
  if (key === 'fontSize') return typeof value === 'number' && Number.isFinite(value) && value >= 1 && value <= 512;
  if (key === 'color') return typeof value === 'string' && /^#[0-9a-f]{6}$/i.test(value);
  return ['bold', 'italic', 'underline'].includes(key) && typeof value === 'boolean';
};
export function isTextFormat(value: unknown): value is TextFormat {
  return object(value) && Object.keys(value).length > 0
    && Object.entries(value).every(([key, setting]) => TEXT_FORMAT_KEYS.includes(key as typeof TEXT_FORMAT_KEYS[number]) && validSetting(key, setting));
}
export function isSafeTextBoundary(text: string, offset: number): boolean {
  if (!Number.isInteger(offset) || offset < 0 || offset > text.length) return false;
  return offset === 0 || offset === text.length || !(text.charCodeAt(offset - 1) >= 0xD800 && text.charCodeAt(offset - 1) <= 0xDBFF
    && text.charCodeAt(offset) >= 0xDC00 && text.charCodeAt(offset) <= 0xDFFF);
}
/** Clamp DOM selection offsets without tearing a Unicode supplementary character in half. */
export function clampTextOffset(text: string, offset: number, affinity: 'backward' | 'forward' = 'backward'): number {
  let bounded = Math.max(0, Math.min(text.length, Number.isFinite(offset) ? Math.trunc(offset) : 0));
  if (!isSafeTextBoundary(text, bounded)) bounded += affinity === 'forward' ? 1 : -1;
  return bounded;
}
function textRange(text: string, start: number, end: number): [number, number] {
  if (start === end) { const caret = clampTextOffset(text, start); return [caret, caret]; }
  return [clampTextOffset(text, Math.min(start, end)), clampTextOffset(text, Math.max(start, end), 'forward')];
}
export function isTextRuns(text: string, value: unknown): value is TextRun[] {
  if (!Array.isArray(value) || value.length > TEXT_RUN_LIMIT) return false;
  let previousEnd = 0;
  return value.every(run => {
    if (!object(run) || Object.keys(run).some(key => !['start', 'end', 'style'].includes(key))
      || typeof run.start !== 'number' || typeof run.end !== 'number' || run.start >= run.end || run.start < previousEnd
      || !isSafeTextBoundary(text, run.start) || !isSafeTextBoundary(text, run.end) || !isTextFormat(run.style)) return false;
    previousEnd = run.end; return true;
  });
}
function cleanFormat(value: unknown): TextFormat {
  if (!object(value)) return {};
  const result: Record<string, unknown> = {};
  for (const key of TEXT_FORMAT_KEYS) if (own(value, key) && validSetting(key, value[key])) result[key] = value[key];
  return result as TextFormat;
}
function sameFormat(a: TextFormat, b: TextFormat): boolean { return TEXT_FORMAT_KEYS.every(key => a[key] === b[key]); }
function append(result: TextRun[], start: number, end: number, style: TextFormat): void {
  if (start >= end || !Object.keys(style).length) return;
  const previous = result.at(-1);
  if (previous?.end === start && sameFormat(previous.style, style)) previous.end = end;
  else result.push({ start, end, style: { ...style } });
  if (result.length > TEXT_RUN_LIMIT) throw new RangeError(`Text supports at most ${TEXT_RUN_LIMIT} formatted passages.`);
}
/** Canonical sparse intervals, safe to render/save. Overlapping input is clipped in sorted order. */
export function normalizeTextRuns(text: string, runs: readonly TextRun[] | undefined): TextRun[] {
  if (!runs?.length || !text.length) return [];
  if (runs.length > TEXT_RUN_LIMIT) throw new RangeError(`Text supports at most ${TEXT_RUN_LIMIT} formatted passages.`);
  const sorted = runs.filter(run => run && typeof run.start === 'number' && Number.isFinite(run.start) && typeof run.end === 'number' && Number.isFinite(run.end))
    .map(run => ({ start: clampTextOffset(text, run.start), end: clampTextOffset(text, run.end, 'forward'), style: cleanFormat(run.style) }))
    .sort((a, b) => a.start - b.start || a.end - b.end);
  const result: TextRun[] = [];
  for (const run of sorted) append(result, Math.max(result.at(-1)?.end ?? 0, run.start), run.end, run.style);
  return result;
}
export interface TextSegment { start: number; end: number; text: string; style: TextFormat }
/** Include unformatted gaps so every canonical character renders once, with no generated HTML. */
export function textSegments(text: string, runs?: readonly TextRun[]): TextSegment[] {
  const result: TextSegment[] = []; let cursor = 0;
  for (const run of normalizeTextRuns(text, runs)) {
    if (cursor < run.start) result.push({ start: cursor, end: run.start, text: text.slice(cursor, run.start), style: {} });
    result.push({ ...run, text: text.slice(run.start, run.end), style: { ...run.style } }); cursor = run.end;
  }
  if (cursor < text.length) result.push({ start: cursor, end: text.length, text: text.slice(cursor), style: {} });
  return result;
}
/** Return sparse inherited overrides at a caret; the end of a passage keeps its typing style. */
export function textStyleAt(runs: readonly TextRun[] | undefined, offset: number): TextFormat {
  const inside = runs?.find(run => run.start <= offset && offset < run.end);
  const previous = inside ?? [...runs ?? []].reverse().find(run => run.end === offset);
  return previous ? { ...previous.style } : {};
}
/** Undefined patch values remove an override; explicit false negates inherited Boolean styling. */
export function applyTextFormat(text: string, runs: readonly TextRun[] | undefined, start: number, end: number, patch: Partial<TextFormat>): TextRun[] {
  if (!object(patch) || Object.entries(patch).some(([key, value]) => !TEXT_FORMAT_KEYS.includes(key as typeof TEXT_FORMAT_KEYS[number]) || value !== undefined && !validSetting(key, value)))
    throw new Error('Text formatting accepts only supported fonts, bounded sizes, hexadecimal colors and Boolean emphasis.');
  const [a, b] = textRange(text, start, end);
  const normalized = normalizeTextRuns(text, runs); if (a === b || !Object.keys(patch).length) return normalized;
  const result: TextRun[] = [];
  for (const segment of textSegments(text, normalized)) {
    if (segment.end <= a || segment.start >= b) { append(result, segment.start, segment.end, segment.style); continue; }
    append(result, segment.start, Math.min(segment.end, a), segment.style);
    const style = { ...segment.style };
    for (const key of TEXT_FORMAT_KEYS) if (own(patch, key)) {
      if (patch[key] === undefined) delete style[key]; else Object.assign(style, { [key]: patch[key] });
    }
    append(result, Math.max(segment.start, a), Math.min(segment.end, b), style);
    append(result, Math.max(segment.start, b), segment.end, segment.style);
  }
  return result;
}
/** Apply one replacement, shifting surviving passages and preserving the active typing overrides. */
export function replaceTextRange(text: string, runs: readonly TextRun[] | undefined, start: number, end: number, insert: string, typingStyle?: TextFormat): { text: string; runs: TextRun[] } {
  if (typeof insert !== 'string') throw new TypeError('Inserted text must be plain text.');
  if (typingStyle && Object.keys(typingStyle).length && !isTextFormat(typingStyle)) throw new Error('Typing formatting is invalid.');
  const [a, b] = textRange(text, start, end);
  const normalized = normalizeTextRuns(text, runs), result: TextRun[] = [], delta = insert.length - (b - a);
  for (const run of normalized) append(result, run.start, Math.min(run.end, a), run.style);
  append(result, a, a + insert.length, typingStyle ?? textStyleAt(normalized, a));
  for (const run of normalized) if (run.end > b) append(result, Math.max(run.start, b) + delta, run.end + delta, run.style);
  const changed = text.slice(0, a) + insert + text.slice(b);
  return { text: changed, runs: normalizeTextRuns(changed, result) };
}
/** Plain edits preserve unchanged prefixes/suffixes and strip formatting only from replaced characters. */
export function rebaseTextRuns(oldText: string, newText: string, runs: readonly TextRun[] | undefined): TextRun[] {
  if (oldText === newText) return normalizeTextRuns(newText, runs);
  let start = 0;
  while (start < oldText.length && start < newText.length && oldText[start] === newText[start]) start++;
  start = clampTextOffset(oldText, start);
  let oldEnd = oldText.length, newEnd = newText.length;
  while (oldEnd > start && newEnd > start && oldText[oldEnd - 1] === newText[newEnd - 1]) { oldEnd--; newEnd--; }
  if (!isSafeTextBoundary(oldText, oldEnd)) { oldEnd++; newEnd++; }
  return replaceTextRange(oldText, runs, start, oldEnd, newText.slice(start, newEnd), {}).runs;
}
