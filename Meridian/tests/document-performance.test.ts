import test from 'node:test';
import assert from 'node:assert/strict';
import { createDocumentPreviewCache } from '../src/core/document-preview.ts';

const asset = (text: string, name = 'catalogue.csv', mime = 'text/csv') => ({ id: 'document', name, mime, base64: Buffer.from(text).toString('base64') });

test('prepared documents survive project clones and decode original bytes only once', () => {
  const prepare = createDocumentPreviewCache(), original = asset('\uFEFFType,Description\r\n"std::vector<T>","Owns elements, contiguously"\r\n');
  const decode = globalThis.atob; let calls = 0;
  globalThis.atob = value => { calls++; return decode(value); };
  try {
    const first = prepare(original), second = prepare(structuredClone(original));
    assert.equal(first, second); assert.equal(calls, 1);
    assert.deepEqual(Buffer.from(first.bytes), Buffer.from(original.base64, 'base64'));
    assert.deepEqual(second.table!.row(1), ['std::vector<T>', 'Owns elements, contiguously']);
    assert.equal(Object.isFrozen(first), true);
  } finally { globalThis.atob = decode; }
});

test('same attachment identity with new bytes, type or filename never reuses stale prepared content', () => {
  const prepare = createDocumentPreviewCache(), original = asset('Name,Value\nOriginal,1');
  const first = prepare(original), replacement = prepare({ ...original, base64: asset('Name,Value\nReplacement,2').base64 });
  assert.notEqual(first, replacement); assert.equal(replacement.table!.row(1)[0], 'Replacement');
  const text = prepare({ ...original, mime: 'application/octet-stream', name: 'catalogue.txt' });
  assert.notEqual(first, text); assert.equal(text.kind, 'text'); assert.equal(text.lines!.line(1), 'Original,1');
  const markdown = prepare({ ...original, name: 'catalogue.md', mime: 'application/octet-stream' });
  assert.equal(markdown.kind, 'markdown'); assert.notEqual(markdown, text);
});

test('prepared cache evicts least recently used entries and oversized items do not displace useful readers', () => {
  const prepare = createDocumentPreviewCache(1024, 2), a = asset('a,b'), b = asset('c,d'), c = asset('e,f');
  const first = prepare(a), second = prepare(b); assert.equal(prepare(a), first);
  prepare(c); assert.equal(prepare(a), first); assert.notEqual(prepare(b), second);
  const huge = asset('x'.repeat(1024)), before = prepare(a);
  assert.notEqual(prepare(huge), prepare(huge)); assert.equal(prepare(a), before);
});

test('Markdown preparation retains ordinary parsed blocks and preserves source identifiers', () => {
  const prepare = createDocumentPreviewCache(), original = asset('# Guide\n\nstd::basic_string_view and **ownership**.\n\n- one\n- two', 'guide.md', 'application/octet-stream');
  const first = prepare(original);
  assert.equal(first.blocks!.length, 3); assert.equal(first.blocks![1]!.text, 'std::basic_string_view and **ownership**.');
  assert.equal(Object.isFrozen(first.blocks), true); assert.equal(Object.isFrozen(first.blocks![0]), true);
  assert.equal(prepare({ ...original }).blocks, first.blocks);
  const many = prepare(asset(Array.from({ length: 8193 }, (_, i) => `# ${i}`).join('\n'), 'long.md', 'text/markdown'));
  assert.equal(many.blocks, undefined); assert.equal(many.error, undefined);
});

test('malformed document preparation keeps downloadable originals and caches the reader error', () => {
  const prepare = createDocumentPreviewCache(), invalid = asset('Title,Description\n"unclosed');
  const prepared = prepare(invalid); assert.match(prepared.error!, /Unclosed/); assert.equal(prepared.table, undefined);
  assert.deepEqual(Buffer.from(prepared.bytes), Buffer.from(invalid.base64, 'base64')); assert.equal(prepare({ ...invalid }), prepared);
  const invalidText = { ...asset('', 'source.txt', 'text/plain'), base64: Buffer.from([0xff, 0x01]).toString('base64') };
  assert.match(prepare(invalidText).error!, /decoded/); assert.deepEqual(Buffer.from(prepare(invalidText).bytes), Buffer.from([0xff, 0x01]));
});

test('PDF preparation caches bytes without DOM, object URLs or decoded document text', () => {
  const prepare = createDocumentPreviewCache(), pdf = asset('%PDF-1.7\noriginal bytes', 'paper.pdf', 'application/pdf'), first = prepare(pdf);
  assert.equal(first.kind, 'pdf'); assert.equal(first.source, ''); assert.equal(first.blocks, undefined); assert.equal(first.table, undefined);
  assert.equal(prepare({ ...pdf }), first); assert.deepEqual(Buffer.from(first.bytes), Buffer.from(pdf.base64, 'base64'));
});
