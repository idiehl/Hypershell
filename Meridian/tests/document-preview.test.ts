import test from 'node:test';
import assert from 'node:assert/strict';
import { DOCUMENT_PREVIEW_MAX_BYTES, decodeDocumentBytes, decodeDocumentText, documentDelimiter, documentMarkdownBlocks, documentPreviewKind, documentTextLines, hasDocumentPdfSignature, parseDocumentTable, safeDocumentLink } from '../src/core/document-preview.ts';

const attachment = (text = '', name = 'document.txt', mime = 'text/plain') => ({ id: 'document', name, mime, base64: Buffer.from(text).toString('base64') });

test('known document MIME types and generic filename fallbacks are case insensitive', () => {
  for (const [name, mime, expected] of [
    ['catalog.CSV', 'application/octet-stream', 'csv'], ['catalog.tsv', '', 'csv'],
    ['catalog.csv', 'text/plain', 'csv'], ['catalog.tsv', 'text/plain', 'csv'],
    ['catalog.md', 'text/plain;charset=utf-8', 'markdown'], ['catalog.csv', 'application/vnd.ms-excel', 'csv'],
    ['catalog.xls', 'application/vnd.ms-excel', null],
    ['catalog.MD', 'application/octet-stream', 'markdown'], ['notes.markdown', 'binary/octet-stream', 'markdown'],
    ['report.PDF', 'application/octet-stream', 'pdf'], ['file', 'application/pdf; name=report.pdf', 'pdf'],
    ['file', 'text/csv; charset=utf-8', 'csv'], ['file', 'text/tab-separated-values', 'csv'],
    ['file', 'text/markdown', 'markdown'], ['file', 'application/ld+json', 'text'],
    ['file', 'application/rss+xml', 'text'], ['source.CS', '', 'text'], ['file.HTML', '', 'text'],
    ['data.json', 'application/octet-stream', 'text'], ['file.svg', '', 'text'],
    ['file.xlsx', 'application/octet-stream', null], ['file.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', null],
    ['misleading.md', 'image/png', null], ['file.zip', 'application/zip', null],
  ] as const) assert.equal(documentPreviewKind({ name, mime }), expected, `${name} / ${mime}`);
});

test('TSV delimiter follows filename or MIME, never guesses from comma-containing fields', () => {
  assert.equal(documentDelimiter({ name: 'file.TSV', mime: 'application/octet-stream' }), '\t');
  assert.equal(documentDelimiter({ name: 'file', mime: 'text/tab-separated-values;charset=utf-8' }), '\t');
  assert.equal(documentDelimiter({ name: 'file.csv', mime: 'text/csv' }), ',');
});

test('PDF requires an actual header before embedding, independently of MIME or filename', () => {
  assert.equal(hasDocumentPdfSignature(Buffer.from('%PDF-1.7\n')), true);
  for (const text of ['', '%PDF', 'fake.pdf', '<script>bad()</script>', ' %PDF-1.7']) assert.equal(hasDocumentPdfSignature(Buffer.from(text)), false);
});

test('source line index reaches the end, retains original line breaks and has no row limit', () => {
  const text = Array.from({ length: 2500 }, (_, i) => `line ${i + 1}\r\n`).join(''), lines = documentTextLines(text);
  assert.equal(lines.lineCount, 2500); assert.equal(lines.line(2499), 'line 2500\r\n');
  assert.equal(documentTextLines('').lineCount, 0); assert.equal(documentTextLines('one').line(0), 'one');
  assert.equal(lines.line(-1), ''); assert.equal(lines.line(2500), '');
});

test('UTF-8 BOM and Unicode text decode without changing source bytes', () => {
  const bytes = Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), Buffer.from('Greek: Ἀθηνᾶ — 日本語\n')]), asset = { ...attachment(), base64: bytes.toString('base64') };
  assert.equal(decodeDocumentText(asset), 'Greek: Ἀθηνᾶ — 日本語\n');
  assert.deepEqual(Buffer.from(decodeDocumentBytes(asset)), bytes);
});

test('UTF-16 little-endian and big-endian BOMs override MIME charset', () => {
  const text = 'Namespace,Description\r\nSystem.Text,Unicode Ω 😀', le = Buffer.from(text, 'utf16le'), be = Buffer.from(le); be.swap16();
  for (const bytes of [Buffer.concat([Buffer.from([0xff, 0xfe]), le]), Buffer.concat([Buffer.from([0xfe, 0xff]), be])]) {
    const asset = { ...attachment('', 'data.csv', 'text/csv;charset=utf-8'), base64: bytes.toString('base64') };
    assert.equal(decodeDocumentText(asset), text); assert.deepEqual(Buffer.from(decodeDocumentBytes(asset)), bytes);
  }
});

test('declared Windows-1252 is decoded, malformed or undeclared binary text is explicit', () => {
  const asset = { ...attachment('', 'data.txt', 'text/plain; charset="windows-1252"'), base64: Buffer.from([0x80, 0x20, 0xe9]).toString('base64') };
  assert.equal(decodeDocumentText(asset), '€ é');
  assert.throws(() => decodeDocumentText({ ...asset, mime: 'text/plain' }), /could not be decoded as utf-8/);
  assert.throws(() => decodeDocumentText({ ...asset, mime: 'text/plain;charset=no-such-encoding' }), /could not be decoded/);
});

test('canonical base64 validation rejects invalid alphabet, padding and trailing bits', () => {
  for (const value of ['AAAA=', 'AA=A', 'A===', '====', 'AA$A', 'ZE==', 'Zm9=', 'AA\nA', 'A']) assert.throws(() => decodeDocumentBytes({ base64: value }), /base64|16 MiB/);
  for (const text of ['', 'a', 'ab', 'abc', 'abcd', 'Ω']) assert.deepEqual(Buffer.from(decodeDocumentBytes(attachment(text))), Buffer.from(text));
});

test('16 MiB boundary is accepted and one additional byte is rejected before decoding', () => {
  const bytes = Buffer.alloc(DOCUMENT_PREVIEW_MAX_BYTES, 65), asset = { base64: bytes.toString('base64') };
  assert.equal(decodeDocumentBytes(asset).length, DOCUMENT_PREVIEW_MAX_BYTES);
  assert.throws(() => decodeDocumentBytes({ base64: Buffer.alloc(DOCUMENT_PREVIEW_MAX_BYTES + 1).toString('base64') }), /16 MiB/);
});

test('CSV fields preserve escaped quotes, commas, embedded newlines and empty trailing cells', () => {
  const table = parseDocumentTable('\uFEFFName,Description,Empty\r\n"System.Text","quotes ""and"", commas\nand lines",\r\n"a"  ,b,c\r\n');
  assert.equal(table.rowCount, 3); assert.equal(table.columnCount, 3);
  assert.deepEqual(table.row(0), ['Name', 'Description', 'Empty']);
  assert.deepEqual(table.row(1), ['System.Text', 'quotes "and", commas\nand lines', '']);
  assert.deepEqual(table.row(2), ['a', 'b', 'c']);
  assert.deepEqual([...table.matchingRows('QUOTES "AND"')], [1]);
  assert.deepEqual([...table.matchingRows('lines')], [1]);
});

test('CSV row endings support LF, CRLF, CR and no final newline without inventing records', () => {
  for (const separator of ['\n', '\r\n', '\r']) {
    for (const suffix of ['', separator]) { const table = parseDocumentTable(`a,b${separator}c,d${suffix}`); assert.equal(table.rowCount, 2); assert.deepEqual(table.row(1), ['c', 'd']); }
  }
  assert.equal(parseDocumentTable('').rowCount, 0);
  assert.deepEqual(parseDocumentTable('\n\n').row(1), ['']);
  assert.deepEqual(parseDocumentTable('a,,').row(0), ['a', '', '']);
});

test('TSV retains commas and quoted multiline/tab fields', () => {
  const table = parseDocumentTable('Name\tNotes\nType\t"comma, tab\tand\nline"', '\t');
  assert.equal(table.rowCount, 2); assert.equal(table.columnCount, 2);
  assert.deepEqual(table.row(1), ['Type', 'comma, tab\tand\nline']);
});

test('full 963-type catalogue and header remain accessible past the old 512-row limit', () => {
  const text = ['Type,Header,Description', ...Array.from({ length: 963 }, (_, i) => `Type${i + 1},header${i + 1},description ${i + 1}`)].join('\r\n');
  const table = parseDocumentTable(text);
  assert.equal(table.rowCount, 964); assert.equal(table.columnCount, 3);
  assert.deepEqual(table.row(963), ['Type963', 'header963', 'description 963']);
  assert.deepEqual([...table.matchingRows('type963', 1)], [963]);
  assert.deepEqual([...table.matchingRows('', 1)].slice(950), Array.from({ length: 13 }, (_, i) => i + 951));
});

test('more than 1024 rows and columns are indexed without constructing a grid', () => {
  const table = parseDocumentTable(Array.from({ length: 1100 }, (_, row) => Array.from({ length: 1100 }, (_, col) => row === 1099 && col === 1099 ? 'LAST' : '').join(',')).join('\n'));
  assert.equal(table.rowCount, 1100); assert.equal(table.columnCount, 1100);
  assert.deepEqual(table.row(1099, 1098, 2), ['', 'LAST']);
  assert.deepEqual([...table.matchingRows('last')], [1099]);
});

test('narrow column windows avoid allocating all fields of a very wide record', () => {
  const table = parseDocumentTable('name,' + ','.repeat(20000) + 'end');
  assert.equal(table.columnCount, 20002);
  assert.deepEqual(table.row(0, 0, 2), ['name', '']);
  assert.deepEqual(table.row(0, 20000, 2), ['', 'end']);
  assert.deepEqual(table.row(-1), []); assert.deepEqual(table.row(1), []);
  assert.throws(() => table.row(0, -1, 2), /column range/);
});

test('malformed quoted records produce explicit errors rather than corrupting later rows', () => {
  assert.throws(() => parseDocumentTable('header\n"unfinished'), /Unclosed quoted field in row 2/);
  assert.throws(() => parseDocumentTable('"closed"x,value'), /Unexpected text after a closing quote in row 1/);
  assert.deepEqual(parseDocumentTable('unquoted"literal,b').row(0), ['unquoted"literal', 'b']);
});

test('Markdown block reader handles hierarchy, prose, lists, quotes, code and dividers', () => {
  const blocks = [...documentMarkdownBlocks('# Title\r\n\r\nParagraph **bold**\ncontinued.\n\n1. First\n2. Second\n\n> Quoted\n> continued\n\n```cs\nSystem.Console.WriteLine("Hello");\n```\n\n---\n')];
  assert.deepEqual(blocks.map(block => block.type), ['heading', 'paragraph', 'list', 'quote', 'code', 'rule']);
  assert.equal(blocks[0]!.level, 1); assert.equal(blocks[1]!.text, 'Paragraph **bold**\ncontinued.');
  assert.equal(blocks[2]!.ordered, true); assert.equal(blocks[2]!.text, 'First\nSecond');
  assert.equal(blocks[3]!.text, 'Quoted\ncontinued'); assert.equal(blocks[4]!.language, 'cs');
});

test('Markdown HTML and image syntax remain inert source data for the DOM renderer', () => {
  const source = '<script>alert(1)</script>\n\n![remote image](https://example.com/track.png)\n\n[bad](javascript:alert(1))';
  assert.deepEqual([...documentMarkdownBlocks(source)].map(block => block.text), source.split('\n\n'));
  assert.equal(safeDocumentLink('javascript:alert(1)'), null);
  assert.equal(safeDocumentLink('data:text/html,<script>alert(1)</script>'), null);
  assert.equal(safeDocumentLink('file:///C:/secret.txt'), null);
  assert.equal(safeDocumentLink('//example.com'), null);
  assert.equal(safeDocumentLink('https://example.com/\nfoo'), null);
  assert.equal(safeDocumentLink('https://example.com/reference#one'), 'https://example.com/reference#one');
  assert.equal(safeDocumentLink('mailto:author@example.com'), 'mailto:author@example.com');
});

test('large generic-MIME Markdown is classified and read without the old 3000-character cutoff', () => {
  const text = '# C# namespaces\n\n' + 'Detailed source reference.\n\n'.repeat(8000) + '## Final source marker';
  const asset = attachment(text, 'CSharp-namespaces.md', 'application/octet-stream');
  assert(text.length > 194876); assert.equal(documentPreviewKind(asset), 'markdown');
  assert.equal(decodeDocumentText(asset), text);
  assert.equal([...documentMarkdownBlocks(text)].at(-1)!.text, 'Final source marker');
});
