import test from 'node:test';
import assert from 'node:assert/strict';
import { applyTextFormat, clampTextOffset, isTextFormat, isTextRuns, normalizeTextRuns, rebaseTextRuns, replaceTextRange, textSegments, textStyleAt } from '../src/core/rich-text.ts';
import { FONT_FAMILIES } from '../src/core/fonts.ts';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { createModule, createModuleFromSelection, instantiateModule } from '../src/core/modules.ts';
import { applyRecipe } from '../src/core/authoring.ts';
import { createNodeFromSpec } from '../src/core/creation.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';
import type { TextFormat, TextRun } from '../src/core/types.ts';

test('sparse passages compose, split, coalesce and explicitly negate inherited styling', () => {
  const text = 'alpha beta gamma';
  let runs = applyTextFormat(text, [], 0, 10, { bold: true, fontFamily: 'garamond', color: '#AACCEE' });
  runs = applyTextFormat(text, runs, 6, 16, { italic: true, fontSize: 27.5 });
  assert.deepEqual(runs, [
    { start: 0, end: 6, style: { fontFamily: 'garamond', color: '#AACCEE', bold: true } },
    { start: 6, end: 10, style: { fontFamily: 'garamond', fontSize: 27.5, color: '#AACCEE', bold: true, italic: true } },
    { start: 10, end: 16, style: { fontSize: 27.5, italic: true } },
  ]);
  runs = applyTextFormat(text, runs, 0, 16, { bold: false, fontFamily: undefined, color: undefined });
  assert.deepEqual(runs, [
    { start: 0, end: 6, style: { bold: false } },
    { start: 6, end: 16, style: { fontSize: 27.5, bold: false, italic: true } },
  ]);
  assert.deepEqual(applyTextFormat(text, runs, 0, 16, { fontSize: undefined, bold: undefined, italic: undefined }), []);
  assert.deepEqual(textSegments(text, [{ start: 6, end: 10, style: { underline: true } }]), [
    { start: 0, end: 6, text: 'alpha ', style: {} }, { start: 6, end: 10, text: 'beta', style: { underline: true } },
    { start: 10, end: 16, text: ' gamma', style: {} },
  ]);
  assert.equal(textSegments(text, runs).map(segment => segment.text).join(''), text);
  assert.equal(isTextRuns(text, runs), true);
});

test('typing, replacing, removing and plain rebasing preserve only surviving canonical passages', () => {
  const source: TextRun[] = [{ start: 0, end: 5, style: { bold: true } }, { start: 6, end: 10, style: { color: '#FF0000' } }];
  assert.deepEqual(replaceTextRange('alpha beta', source, 2, 8, 'X', { italic: true }), {
    text: 'alXta', runs: [{ start: 0, end: 2, style: { bold: true } }, { start: 2, end: 3, style: { italic: true } }, { start: 3, end: 5, style: { color: '#FF0000' } }],
  });
  assert.deepEqual(replaceTextRange('alpha beta', source, 5, 5, '!', undefined).runs, [
    { start: 0, end: 6, style: { bold: true } }, { start: 7, end: 11, style: { color: '#FF0000' } },
  ]);
  assert.deepEqual(replaceTextRange('alpha beta', source, 0, 10, '').runs, []);
  assert.deepEqual(rebaseTextRuns('alpha beta', 'alpha NEW beta', source), [
    { start: 0, end: 5, style: { bold: true } }, { start: 10, end: 14, style: { color: '#FF0000' } },
  ]);
  assert.deepEqual(rebaseTextRuns('alpha beta', 'z', source), []);
  assert.deepEqual(source, [{ start: 0, end: 5, style: { bold: true } }, { start: 6, end: 10, style: { color: '#FF0000' } }]);
  assert.deepEqual(textStyleAt(source, 5), { bold: true });
  assert.deepEqual(textStyleAt(source, 6), { color: '#FF0000' });
});

test('UTF-16 selection boundaries preserve supplementary characters and handle arbitrary safe text edits', () => {
  const text = 'a😀b🚀c', runs: TextRun[] = [{ start: 1, end: 3, style: { fontFamily: 'georgia', underline: true } }];
  assert.equal(clampTextOffset(text, 2), 1); assert.equal(clampTextOffset(text, 2, 'forward'), 3);
  assert.deepEqual(applyTextFormat(text, [], 2, 2, { bold: true }), []);
  assert.deepEqual(applyTextFormat(text, [], 2, 3, { bold: true }), [{ start: 1, end: 3, style: { bold: true } }]);
  assert.equal(replaceTextRange(text, runs, 2, 2, 'X').text, 'aX😀b🚀c');
  assert.equal(replaceTextRange(text, runs, 2, 3, '').text, 'ab🚀c');
  assert.deepEqual(rebaseTextRuns(text, 'a😁b🚀c', runs), []);
  for (const oldText of ['', text, '𝄞one\ntwo', 'emoji 👩‍🚀 end']) for (const newText of ['', 'literal <img onerror=x>', '😀', '𝄞one\nnew two', `${oldText} end`]) {
    const oldRuns = oldText ? applyTextFormat(oldText, [], 0, oldText.length, { bold: true }) : [];
    assert.ok(isTextRuns(newText, rebaseTextRuns(oldText, newText, oldRuns)), `${oldText} → ${newText}`);
    for (let start = 0; start <= oldText.length; start++) {
      const inserted = replaceTextRange(oldText, oldRuns, start, start, '😃');
      assert.ok(isTextRuns(inserted.text, inserted.runs));
      assert.equal(textSegments(inserted.text, inserted.runs).map(segment => segment.text).join(''), inserted.text);
    }
  }
});

test('structured formatting rejects unsafe fields, invalid offsets, nonfinite size and injected CSS or HTML', () => {
  for (const font of FONT_FAMILIES) assert.equal(isTextFormat({ fontFamily: font.value }), true);
  for (const style of [{ fontSize: 1 }, { fontSize: 512 }, { fontSize: 19.25 }, { bold: false, italic: false, underline: false }]) assert.ok(isTextFormat(style));
  for (const style of [{}, { fontSize: 0 }, { fontSize: 512.01 }, { fontSize: NaN }, { fontSize: Infinity }, { fontSize: '12' }, { color: 'red' },
    { color: '#FFF' }, { color: '#000000;background:red' }, { fontFamily: 'Arial' }, { fontFamily: 'url(x)' }, { html: '<b>x</b>' }, { bold: 1 }, { italic: null }, { css: 'position:absolute' }]) {
    assert.equal(isTextFormat(style), false, JSON.stringify(style));
    if (Object.keys(style).length) assert.throws(() => applyTextFormat('word', [], 0, 4, style as TextFormat));
  }
  for (const invalid of [null, {}, [{ start: -1, end: 2, style: { bold: true } }], [{ start: 0, end: 9, style: { bold: true } }],
    [{ start: 0.5, end: 2, style: { bold: true } }], [{ start: 0, end: 0, style: { bold: true } }],
    [{ start: 2, end: 3, style: { bold: true } }], [{ start: 0, end: 2, style: {} }],
    [{ start: 0, end: 3, style: { bold: true } }, { start: 1, end: 4, style: { italic: true } }],
    [{ start: 0, end: 1, style: { bold: true }, html: 'x' }], Array.from({ length: 4097 }, () => ({ start: 0, end: 1, style: { bold: true } }))]) {
    assert.equal(isTextRuns('a😀b', invalid), false, JSON.stringify(invalid).slice(0, 200));
  }
  assert.deepEqual(normalizeTextRuns('word', [{ start: 0, end: 2, style: { bold: true } }, { start: 2, end: 4, style: { bold: true } }]), [{ start: 0, end: 4, style: { bold: true } }]);
  assert.throws(() => normalizeTextRuns('word', Array.from({ length: 4097 }, () => ({ start: 0, end: 1, style: { bold: true } }))));
});

test('formatted titles and text persist independently through snapshots, clipboard, fork, modules and portable publication', () => {
  const project = newProject('Portable passages'), canvasId = project.entryCanvasId;
  const node = addNode(project, canvasId, 'media', 'A formatted title');
  Object.assign(node, { x: 100, y: 100, text: 'Hello 😀 world\nsecond line', titleRuns: [{ start: 2, end: 11, style: { bold: false, fontFamily: 'garamond', fontSize: 44 } }],
    textRuns: [{ start: 0, end: 5, style: { bold: true, italic: true, color: '#123ABC' } }, { start: 6, end: 8, style: { underline: true, fontSize: 61.5 } }] });
  const second = addCanvas(project, 'Pinned passages'), reference = addNode(project, second.id, 'reference', 'Read the saved passages'); reference.targetId = node.id;
  const saved = commitSnapshots(project), before = JSON.stringify(saved), fork = forkProject(saved);
  const pasted = pasteNodes(saved, copyNodes(saved, [node.id]), canvasId);
  const module = createModuleFromSelection(saved, [node.id], 'Rich passages'), inserted = instantiateModule(saved, module, { canvasId, x: 1000, y: 1000 });
  const portable = readPublication(buildPublication(saved, '', ''));
  for (const copied of [fork.nodes.find(item => item.title === node.title), pasted.project.nodes.find(item => item.id === pasted.idMap[node.id]),
    module.project.nodes.find(item => item.title === node.title), inserted.project.nodes.find(item => item.title === node.title), portable.nodes.find(item => item.title === node.title)]) {
    assert.deepEqual(copied?.titleRuns, node.titleRuns); assert.deepEqual(copied?.textRuns, node.textRuns);
  }
  for (const copy of [fork, pasted.project, inserted.project, portable]) assert.ok(copy.nodes.some(item => item.title === node.title && item.id !== node.id));
  const pinned = portable.nodes.find(item => item.title === node.title)!;
  assert.deepEqual(portable.snapshots[pinned.id].node.textRuns, node.textRuns);
  for (const result of [saved, fork, pasted.project, inserted.project, portable]) assert.deepEqual(validateProject(result), []);
  assert.equal(JSON.stringify(saved), before);
  const dirty: any = structuredClone(saved); dirty.nodes.find((item: any) => item.id === node.id).textRuns[0].style.injected = 'x';
  assert.equal(isProjectShape(dirty), false);
  assert.equal((createPublicationPayload(dirty).project.nodes.find(item => item.id === node.id)!.textRuns![0].style as any).injected, undefined);
  dirty.snapshots[node.id].node.titleRuns[0].end = 999;
  assert.equal(isProjectShape(dirty), false);
});

test('declarative creation and recipe edits validate formatting atomically and rebase plain changes', () => {
  const base = newProject('Author passages'), canvasId = base.entryCanvasId, rootId = base.canvases[0].rootId;
  const created = createNodeFromSpec(base, { canvasId, parentId: rootId, type: 'media', title: '  Styled  ', titleRuns: [{ start: 2, end: 8, style: { italic: true } }],
    text: 'alpha beta', textRuns: [{ start: 6, end: 10, style: { color: '#00FF00' } }], x: 100, y: 100, width: 300, height: 120 });
  const node = created.project.nodes.find(item => item.id === created.nodeId)!;
  assert.deepEqual(node.titleRuns, [{ start: 0, end: 6, style: { italic: true } }]);
  const before = JSON.stringify(created.project);
  const revised = applyRecipe(created.project, { version: 1, nodes: [{ key: 'styled', existingId: node.id, text: 'alpha NEW beta' }] }).project;
  assert.deepEqual(revised.nodes.find(item => item.id === node.id)?.textRuns, [{ start: 10, end: 14, style: { color: '#00FF00' } }]);
  assert.equal(JSON.stringify(created.project), before);
  const explicit = applyRecipe(created.project, { version: 1, nodes: [{ key: 'styled', existingId: node.id, text: 'new text', textRuns: [{ start: 0, end: 3, style: { bold: true } }] }] }).project;
  assert.deepEqual(explicit.nodes.find(item => item.id === node.id)?.textRuns, [{ start: 0, end: 3, style: { bold: true } }]);
  assert.throws(() => applyRecipe(created.project, { version: 1, nodes: [{ key: 'styled', existingId: node.id, text: 'short', textRuns: node.textRuns }] }));
  assert.equal(JSON.stringify(created.project), before);
  const container = addNode(base, canvasId, 'container', 'An originally long container title');
  container.titleRuns = [{ start: 20, end: 34, style: { bold: true } }];
  const renamedModule = createModule(base, container.id, 'Short');
  assert.deepEqual(validateProject(renamedModule.project), []);
  assert.deepEqual(renamedModule.project.nodes.find(item => item.id === renamedModule.rootId)?.titleRuns, []);
});
