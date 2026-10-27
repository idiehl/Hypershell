import type { RetiredBlockContent } from './types.ts';
import { isTextRuns } from './rich-text.ts';

/** A lossless legacy archive is canonical inert text, never executable markup or a second active content item. */
export function isRetiredBlockContent(value: unknown): value is RetiredBlockContent {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const archive = value as Record<string, unknown>;
  return Object.keys(archive).every(key => ['title', 'text', 'titleRuns', 'textRuns', 'contentType'].includes(key))
    && typeof archive.title === 'string' && (archive.text === undefined || typeof archive.text === 'string')
    && (archive.contentType === undefined || ['text', 'asset', 'link'].includes(archive.contentType as string))
    && (archive.titleRuns === undefined || isTextRuns(archive.title, archive.titleRuns))
    && (archive.textRuns === undefined || isTextRuns(typeof archive.text === 'string' ? archive.text : '', archive.textRuns));
}
