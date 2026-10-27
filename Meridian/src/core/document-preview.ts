import type { MeridianAsset } from './types.ts';

export type DocumentPreviewKind = 'csv' | 'markdown' | 'text' | 'pdf';
export const DOCUMENT_PREVIEW_MAX_BYTES = 16 * 1024 * 1024;
type Attachment = Pick<MeridianAsset, 'name' | 'mime' | 'base64'>;
const textExtensions = /\.(?:txt|text|log|json|jsonl|ndjson|xml|xsd|xsl|html?|css|[cm]?js|jsx|tsx?|cs|[ch]|[ch]pp|[ch]xx|cc|py|rb|rs|go|java|kt|swift|sql|sh|bash|ps1|ini|ya?ml|toml|conf|config|svg)$/i;

/** Generic upload MIME types fall back to the original filename. Active HTML,
 * XML and source files are previewed as inert text, never embedded documents. */
export function documentPreviewKind(asset: Pick<MeridianAsset, 'name' | 'mime'>): DocumentPreviewKind | null {
  const mime = asset.mime.split(';', 1)[0]!.trim().toLowerCase();
  if (mime === 'application/pdf') return 'pdf';
  if (['text/csv', 'application/csv', 'text/tab-separated-values'].includes(mime)) return 'csv';
  if (['text/markdown', 'text/x-markdown'].includes(mime)) return 'markdown';
  if (mime === 'text/plain' || mime === 'application/vnd.ms-excel') {
    if (/\.(?:csv|tsv)$/i.test(asset.name)) return 'csv';
    if (mime === 'text/plain' && /\.(?:md|markdown|mdown)$/i.test(asset.name)) return 'markdown';
  }
  if (mime.startsWith('text/') || /^(?:application\/(?:[\w.+-]*\+)?(?:json|xml)|application\/(?:javascript|x-javascript|typescript|x-yaml|yaml|toml))$/.test(mime)) return 'text';
  if (mime && !['application/octet-stream', 'binary/octet-stream', 'application/x-binary', 'application/unknown'].includes(mime)) return null;
  if (/\.pdf$/i.test(asset.name)) return 'pdf';
  if (/\.(?:csv|tsv)$/i.test(asset.name)) return 'csv';
  if (/\.(?:md|markdown|mdown)$/i.test(asset.name)) return 'markdown';
  return textExtensions.test(asset.name) ? 'text' : null;
}

/** Decode once, bounded before atob allocates, without modifying original data. */
export function decodeDocumentBytes(asset: Pick<MeridianAsset, 'base64'>): Uint8Array<ArrayBuffer> {
  const value = asset.base64, maximum = Math.ceil(DOCUMENT_PREVIEW_MAX_BYTES / 3) * 4;
  if (value.length > maximum) throw new Error('Document previews support attachments up to 16 MiB.');
  const padding = value.endsWith('==') ? 2 : value.endsWith('=') ? 1 : 0, body = value.slice(0, value.length - padding);
  if (value.length % 4 || /[^A-Za-z0-9+/]/.test(body)) throw new Error('The attachment has invalid base64 data.');
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
  if (padding && (alphabet.indexOf(body.at(-1) ?? '') & (padding === 2 ? 15 : 3))) throw new Error('The attachment has noncanonical base64 data.');
  const byteLength = value.length / 4 * 3 - padding;
  if (byteLength < 0 || byteLength > DOCUMENT_PREVIEW_MAX_BYTES) throw new Error('Document previews support attachments up to 16 MiB.');
  let raw: string;
  try { raw = atob(value); } catch { throw new Error('The attachment has invalid base64 data.'); }
  const bytes = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  return bytes;
}

/** UTF-8 and UTF-16 byte-order marks take precedence over MIME declarations.
 * Legacy single-byte text is accepted only when its charset is declared. */
export function decodeDocumentText(asset: Attachment): string {
  return decodeDocumentTextBytes(asset, decodeDocumentBytes(asset));
}

function decodeDocumentTextBytes(asset: Attachment, bytes: Uint8Array): string {
  let encoding = /charset\s*=\s*["']?([^\s;"']+)/i.exec(asset.mime)?.[1]?.toLowerCase() ?? 'utf-8';
  if (bytes[0] === 0xff && bytes[1] === 0xfe) encoding = 'utf-16le';
  else if (bytes[0] === 0xfe && bytes[1] === 0xff) encoding = 'utf-16be';
  else if (bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf) encoding = 'utf-8';
  try { return new TextDecoder(encoding, { fatal: true }).decode(bytes).replace(/^\uFEFF/, ''); }
  catch { throw new Error(`The document could not be decoded as ${encoding}. Save text as UTF-8 or UTF-16 with a byte-order mark, or supply its charset.`); }
}

export function documentDelimiter(asset: Pick<MeridianAsset, 'name' | 'mime'>): ',' | '\t' {
  return /\.tsv$/i.test(asset.name) || /^text\/tab-separated-values(?:;|$)/i.test(asset.mime) ? '\t' : ',';
}

/** Check the signature before handing embedded bytes to the browser PDF reader. */
export function hasDocumentPdfSignature(bytes: Uint8Array): boolean {
  return bytes.length >= 5 && bytes[0] === 0x25 && bytes[1] === 0x50 && bytes[2] === 0x44 && bytes[3] === 0x46 && bytes[4] === 0x2d;
}

export function documentTextLines(text: string): { lineCount: number; line: (index: number) => string } {
  let count = text ? 1 : 0;
  for (let i = 0; i < text.length; i++) if (text[i] === '\n' && i + 1 < text.length) count++;
  const starts = new Uint32Array(count + 1); let at = 1;
  for (let i = 0; i < text.length; i++) if (text[i] === '\n' && i + 1 < text.length) starts[at++] = i + 1;
  starts[count] = text.length;
  return { lineCount: count, line: index => Number.isInteger(index) && index >= 0 && index < count ? text.slice(starts[index], starts[index + 1]) : '' };
}

export interface DocumentTable {
  readonly rowCount: number;
  readonly columnCount: number;
  /** Zero-based source row, optional column window; omitted fields remain empty. */
  row(index: number, fromColumn?: number, columnCount?: number): string[];
  /** Lazily search decoded cells, including quoted text and multiline fields. */
  matchingRows(query: string, fromRow?: number): Iterable<number>;
}

/** Full-file CSV/TSV index. Row offsets use at most four bytes per source
 * character. Pages decode only their visible rows/columns; no grid-size limit,
 * 24-row truncation or full cell matrix is imposed on the original document. */
export function parseDocumentTable(source: string, delimiter: ',' | '\t' = ','): DocumentTable {
  const text = source.replace(/^\uFEFF/, '');
  if (text.length > DOCUMENT_PREVIEW_MAX_BYTES) throw new Error('Document text exceeds the 16 MiB preview limit.');
  const scan = (visit?: (start: number) => void) => {
    let count = 0, columns = 0, width = 1, start = 0, fieldStart = true, quoted = false, afterQuote = false;
    const emit = () => { visit?.(start); count++; columns = Math.max(columns, width); width = 1; fieldStart = true; afterQuote = false; };
    for (let i = 0; i < text.length; i++) {
      const c = text[i];
      if (quoted) { if (c === '"') { if (text[i + 1] === '"') i++; else { quoted = false; afterQuote = true; } } continue; }
      if (c === delimiter) { width++; fieldStart = true; afterQuote = false; }
      else if (c === '\n' || c === '\r') { emit(); if (c === '\r' && text[i + 1] === '\n') i++; start = i + 1; }
      else if (fieldStart && c === '"') { quoted = true; fieldStart = false; }
      else { if (afterQuote && c !== ' ' && c !== '\t') throw new Error(`Unexpected text after a closing quote in row ${count + 1}.`); fieldStart = false; }
    }
    if (quoted) throw new Error(`Unclosed quoted field in row ${count + 1}.`);
    if (start < text.length) emit();
    return { count, columns };
  };
  const { count, columns } = scan(), offsets = new Uint32Array(count + 1); let at = 0;
  scan(start => { offsets[at++] = start; }); offsets[count] = text.length;
  function* fields(index: number): Generator<string> {
    if (!Number.isInteger(index) || index < 0 || index >= count) return;
    const start = offsets[index]!, next = offsets[index + 1]!;
    let end = next;
    if (text[end - 1] === '\n') end--;
    if (text[end - 1] === '\r') end--;
    let i = start;
    while (i <= end) {
      let value: string;
      if (text[i] === '"') {
        const from = ++i;
        while (i < end) { if (text[i] === '"') { if (text[i + 1] === '"') i += 2; else break; } else i++; }
        value = text.slice(from, i).replace(/""/g, '"'); i++;
        while (i < end && text[i] !== delimiter) i++;
      } else { const from = i; while (i < end && text[i] !== delimiter) i++; value = text.slice(from, i); }
      yield value;
      if (i >= end) break;
      i++;
    }
  }
  return {
    rowCount: count, columnCount: columns,
    row(index, fromColumn = 0, columnCount = Infinity) {
      if (!Number.isInteger(fromColumn) || fromColumn < 0 || columnCount < 0) throw new Error('Invalid document column range.');
      const result: string[] = []; let column = 0;
      for (const value of fields(index)) { if (column >= fromColumn + columnCount) break; if (column >= fromColumn) result.push(value); column++; }
      return result;
    },
    *matchingRows(query, fromRow = 0) {
      const needle = query.toLocaleLowerCase();
      for (let row = Math.max(0, fromRow); row < count; row++) {
        if (!needle) { yield row; continue; }
        for (const value of fields(row)) if (value.toLocaleLowerCase().includes(needle)) { yield row; break; }
      }
    },
  };
}

export type MarkdownBlock = { type: 'heading' | 'paragraph' | 'code' | 'quote' | 'list' | 'rule'; text: string; level?: number; ordered?: boolean; language?: string };
/** Small inert Markdown block reader. It yields blocks incrementally instead of
 * constructing an unbounded DOM or interpreting authored HTML. */
export function* documentMarkdownBlocks(text: string): Generator<MarkdownBlock> {
  let offset = 0, pending: { line: string; start: number } | undefined;
  const next = (): { line: string; start: number } | undefined => {
    if (pending) { const value = pending; pending = undefined; return value; }
    if (offset >= text.length) return;
    const start = offset, newline = text.indexOf('\n', offset); offset = newline < 0 ? text.length : newline + 1;
    return { line: text.slice(start, newline < 0 ? text.length : newline).replace(/\r$/, ''), start };
  };
  let entry: ReturnType<typeof next>;
  while ((entry = next())) {
    const line = entry.line; if (!line.trim()) continue;
    const heading = /^ {0,3}(#{1,6})\s+(.*)$/.exec(line), fence = /^ {0,3}(`{3,}|~{3,})(.*)$/.exec(line), item = /^\s*(?:([-+*])|(\d+)[.)])\s+(.*)$/.exec(line);
    if (heading) { yield { type: 'heading', level: heading[1]!.length, text: heading[2]!.replace(/\s+#+\s*$/, '') }; continue; }
    if (/^\s*(?:(?:\*\s*){3,}|(?:-\s*){3,}|(?:_\s*){3,})$/.test(line)) { yield { type: 'rule', text: '' }; continue; }
    if (fence) {
      const marker = fence[1]!, lines: string[] = []; let current: ReturnType<typeof next>;
      while ((current = next())) { const trimmed = current.line.trim(); if (trimmed[0] === marker[0] && trimmed.length >= marker.length && [...trimmed].every(c => c === marker[0])) break; lines.push(current.line); }
      yield { type: 'code', text: lines.join('\n'), language: fence[2]!.trim().slice(0, 80) }; continue;
    }
    if (item) {
      const lines = [item[3]!], ordered = !!item[2]; let current: ReturnType<typeof next>;
      while ((current = next())) { const match = /^\s*(?:([-+*])|(\d+)[.)])\s+(.*)$/.exec(current.line); if (!match || !!match[2] !== ordered) { pending = current; break; } lines.push(match[3]!); }
      yield { type: 'list', text: lines.join('\n'), ordered }; continue;
    }
    if (/^\s*>/.test(line)) {
      const lines = [line.replace(/^\s*> ?/, '')]; let current: ReturnType<typeof next>;
      while ((current = next())) { if (!/^\s*>/.test(current.line)) { pending = current; break; } lines.push(current.line.replace(/^\s*> ?/, '')); }
      yield { type: 'quote', text: lines.join('\n') }; continue;
    }
    const lines = [line]; let current: ReturnType<typeof next>;
    while ((current = next())) { if (!current.line.trim() || /^ {0,3}(?:#{1,6}\s|`{3,}|~{3,}|>|[-+*]\s|\d+[.)]\s)/.test(current.line)) { pending = current; break; } lines.push(current.line); }
    yield { type: 'paragraph', text: lines.join('\n') };
  }
}

export function safeDocumentLink(value: string): string | null {
  if (/\s|[\u0000-\u001f\u007f]/.test(value) || value.length > 4096) return null;
  try { const url = new URL(value); return ['https:', 'http:', 'mailto:'].includes(url.protocol) ? url.href : null; } catch { return null; }
}

export interface PreparedDocumentPreview {
  readonly kind: DocumentPreviewKind | null;
  readonly bytes: Uint8Array<ArrayBuffer>;
  readonly source: string;
  readonly table?: DocumentTable;
  readonly blocks?: readonly MarkdownBlock[];
  readonly lines?: ReturnType<typeof documentTextLines>;
  readonly error?: string;
}

/** Reuse immutable attachment preparation across scene replacement and cloned
 * projects. Exact content/type/name equality prevents stale previews when an
 * attachment is replaced in place. No object URLs or DOM are retained here. */
export function createDocumentPreviewCache(maximumBytes = 32 * 1024 * 1024, maximumEntries = 8): (asset: Attachment) => PreparedDocumentPreview {
  const entries: { asset: Attachment; document: PreparedDocumentPreview; cost: number }[] = [];
  let retainedBytes = 0;
  return asset => {
    const index = entries.findIndex(entry => entry.asset.name === asset.name && entry.asset.mime === asset.mime && entry.asset.base64 === asset.base64);
    if (index >= 0) { const entry = entries.splice(index, 1)[0]!; entries.push(entry); return entry.document; }
    const bytes = decodeDocumentBytes(asset), kind = documentPreviewKind(asset);
    let source = '', table: DocumentTable | undefined, blocks: readonly MarkdownBlock[] | undefined, lines: ReturnType<typeof documentTextLines> | undefined, error: string | undefined;
    try {
      if (kind && kind !== 'pdf') {
        source = decodeDocumentTextBytes(asset, bytes);
        if (kind === 'csv') table = parseDocumentTable(source, documentDelimiter(asset));
        else if (kind === 'markdown') {
          // Long pathological files keep their incremental reader instead of
          // retaining an object per line. Normal guides are parsed only once.
          const parsed: MarkdownBlock[] = [];
          for (const block of documentMarkdownBlocks(source)) { parsed.push(Object.freeze(block)); if (parsed.length > 8192) break; }
          if (parsed.length <= 8192) blocks = Object.freeze(parsed);
        } else lines = documentTextLines(source);
      }
    } catch (failure) { error = failure instanceof Error ? failure.message : 'This attachment could not be previewed.'; }
    const document = Object.freeze({ kind, bytes, source, table, blocks, lines, error });
    // Count the held base64, decoded bytes, UTF-16 text and a conservative bound
    // for indexes/Markdown objects. Oversized documents work without retention.
    const cost = asset.base64.length * 2 + bytes.byteLength + source.length * 2
      + (table || lines ? source.length * 4 + 4 : 0)
      + (blocks ? blocks.reduce((total, block) => total + block.text.length * 2 + 128, 0) : 0);
    if (cost <= maximumBytes && maximumEntries > 0) {
      while (entries.length && (entries.length >= maximumEntries || retainedBytes + cost > maximumBytes)) retainedBytes -= entries.shift()!.cost;
      entries.push({ asset: { name: asset.name, mime: asset.mime, base64: asset.base64 }, document, cost }); retainedBytes += cost;
    }
    return document;
  };
}

/** Shared bounded cache: prepared content only, never reader or project state. */
export const prepareDocumentPreview = createDocumentPreviewCache();
