import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, addCanvas, commitSnapshots, newProject } from '../src/core/model.ts';
import { galleryObjectLayout, longestRichTextLine, nodeContentLayout, richTextAdvance, textAdvance, wrappedRichTextHeight, wrappedTextHeight } from '../src/core/sizing.ts';
import type { TextRun } from '../src/core/types.ts';

const metrics = { fontSize: 16, family: 'calibri' as const };
test('plain text and color/underline overrides preserve the previous wrapping geometry', () => {
  const text = 'A familiar reading line.\nAnother short paragraph.';
  const decorative: TextRun[] = [{ start: 2, end: 10, style: { color: '#AACC22', underline: true } }];
  for (const width of [16, 110, 320]) {
    assert.equal(wrappedRichTextHeight(text, undefined, width, metrics), wrappedTextHeight(text, width, metrics));
    assert.equal(wrappedRichTextHeight(text, decorative, width, metrics), wrappedTextHeight(text, width, metrics));
  }
  assert.equal(richTextAdvance(text, decorative, metrics), textAdvance(text, metrics));
});
test('mixed native font sizes reserve only the affected line and measure words across formatting boundaries', () => {
  const text = 'Small\nENLARGED\nSmall';
  const runs: TextRun[] = [{ start: 6, end: 14, style: { fontSize: 64, fontFamily: 'garamond', bold: true, italic: true } }];
  assert.equal(wrappedRichTextHeight(text, runs, 900, metrics), (16 + 64 + 16) * 1.5);
  assert.equal(longestRichTextLine(text, runs, metrics), richTextAdvance('ENLARGED', [{ start: 0, end: 8, style: runs[0]!.style }], metrics));
  const word = 'Connected', split: TextRun[] = [{ start: 0, end: 4, style: { fontSize: 32 } }];
  const width = richTextAdvance(word, split, metrics);
  assert.equal(wrappedRichTextHeight(word, split, width + .1, metrics), 32 * 1.5);
  assert.ok(wrappedRichTextHeight(word, split, width / 2, metrics) > 32 * 1.5);
});
test('canonical Unicode and CRLF offsets retain formatting on the intended supplementary glyph', () => {
  const text = 'A\r\n😀 mixed\nZ', runs: TextRun[] = [{ start: 3, end: 5, style: { fontSize: 48 } }];
  assert.equal(wrappedRichTextHeight(text, runs, 900, metrics), (16 + 48 + 16) * 1.5);
  assert.ok(longestRichTextLine(text, runs, metrics) > textAdvance('😀 mixed', metrics));
});
test('node title/body formatting enlarges automatic envelopes and preserves diagonal rotation sizing', () => {
  const project = newProject('Rich text'), node = addNode(project, project.entryCanvasId, 'media', 'A formatted title');
  Object.assign(node, { sizeMode: 'auto', contentType: 'text', text: 'A larger word lives here.', style: { fontSize: 16, titleSize: 22 } });
  const plain = nodeContentLayout(project, node);
  node.titleRuns = [{ start: 2, end: 11, style: { fontSize: 52, fontFamily: 'cambria', bold: false } }];
  node.textRuns = [{ start: 2, end: 8, style: { fontSize: 60, bold: true, italic: true } }];
  const rich = nodeContentLayout(project, node); assert.ok(rich.headerHeight > plain.headerHeight); assert.ok(rich.bodyHeight > plain.bodyHeight);
  Object.assign(node.style!, { titleRotation: -45, textRotation: 90 });
  const rotated = nodeContentLayout(project, node); assert.ok(rotated.headerHeight > rich.headerHeight); assert.ok(rotated.bodyHeight > plain.bodyHeight);
  assert.notEqual(rotated.bodyHeight, rich.bodyHeight);
  assert.equal(node.style?.fontSize, 16); assert.equal(node.text, 'A larger word lives here.');
});
test('Gallery and live/saved references use the source body runs rather than unrelated reference text ranges', () => {
  const project = newProject('Formatted references'), source = addNode(project, project.entryCanvasId, 'media', 'Source');
  Object.assign(source, { contentType: 'text', text: 'Large reading word', textRuns: [{ start: 0, end: 5, style: { fontSize: 56 } }] });
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Mirror'); reference.targetId = source.id;
  const live = nodeContentLayout(project, reference);
  source.textRuns = undefined; const plain = nodeContentLayout(project, reference); assert.ok(live.bodyHeight > plain.bodyHeight);
  source.textRuns = [{ start: 0, end: 5, style: { fontSize: 56 } }];
  const gallery = addNode(project, project.entryCanvasId, 'container', 'Collection'); gallery.containerVariant = 'gallery';
  const object = galleryObjectLayout(project, gallery, reference); source.textRuns = undefined;
  assert.ok(object.bodyHeight > galleryObjectLayout(project, gallery, reference).bodyHeight);
  source.textRuns = [{ start: 0, end: 5, style: { fontSize: 56 } }];
  source.canvasId = addCanvas(project, 'Remote').id; const saved = commitSnapshots(project);
  assert.ok(nodeContentLayout(saved, reference).bodyHeight > plain.bodyHeight);
});
