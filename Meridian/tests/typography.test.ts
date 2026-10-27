import test from 'node:test';
import assert from 'node:assert/strict';
import { FONT_FAMILIES, FONT_CATEGORIES, fontFamily, isFontFamily } from '../src/core/fonts.ts';
import { FONT_FAMILIES as legacyFontExport, isNodeStyle } from '../src/core/appearance.ts';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { createModuleFromSelection, instantiateModule } from '../src/core/modules.ts';
import { validateProject } from '../src/core/rules.ts';
import { buildPublication, readPublication } from '../src/publication.ts';

test('expanded local font catalog retains old styles and supplies safe category fallbacks', () => {
  assert.ok(FONT_FAMILIES.length >= 100);
  assert.equal(legacyFontExport, FONT_FAMILIES);
  assert.equal(new Set(FONT_FAMILIES.map(font => font.value)).size, FONT_FAMILIES.length);
  for (const category of FONT_CATEGORIES) assert.ok(FONT_FAMILIES.some(font => font.category === category.value));
  for (const font of FONT_FAMILIES) {
    assert.ok(isFontFamily(font.value), font.value);
    assert.ok(isNodeStyle({ fontFamily: font.value }), font.value);
    assert.ok(!/[;{}<>]|url\(|https?:|@font-face/i.test(font.css), font.value);
    assert.equal(font.monospace, font.category === 'monospace', font.value);
  }
  for (const alias of ['sans', 'serif', 'mono']) assert.ok(isFontFamily(alias));
  assert.equal(fontFamily(undefined).value, 'sans');
  assert.equal(fontFamily('untrusted, font').value, 'sans');
  assert.equal(fontFamily('times-new-roman').label, 'Times New Roman');
  assert.equal(fontFamily('cascadia-code').monospace, true);
  assert.equal(isFontFamily('Arial'), false);
  assert.equal(isNodeStyle({ fontFamily: 'Arial; color:red' }), false);
});

test('independent text and title rotation accept fractional signed angles and reject invalid document data', () => {
  for (const field of ['textRotation', 'titleRotation']) {
    for (const angle of [-180, -90, -12.5, 0, 32.75, 90, 180]) assert.ok(isNodeStyle({ [field]: angle }), `${field}: ${angle}`);
    for (const angle of [-180.001, 180.001, NaN, Infinity, -Infinity, '90', null, true]) assert.equal(isNodeStyle({ [field]: angle }), false, `${field}: ${angle}`);
  }
  assert.ok(isNodeStyle({ fontFamily: 'garamond', textRotation: 90, titleRotation: -15 }));
});

test('chosen fonts and separate text angles persist through snapshots, clipboard, fork, Module copies and portable publication', () => {
  const project = newProject('Portable typography'), canvasId = project.entryCanvasId;
  const a = addNode(project, canvasId, 'media', 'Rotated title');
  Object.assign(a, { x: 100, y: 100, width: 300, height: 300, sizeMode: 'fixed', contentType: 'text', text: 'A separate rotated body.', style: { fontFamily: 'garamond', titleRotation: -18.5, textRotation: 90 } });
  const b = addNode(project, canvasId, 'media', 'Code');
  Object.assign(b, { x: 500, y: 100, width: 300, height: 300, sizeMode: 'fixed', contentType: 'text', text: 'value = 42;', style: { fontFamily: 'cascadia-code', textRotation: -90, titleRotation: 180 } });
  const pinned = addCanvas(project, 'Saved typography'), reference = addNode(project, pinned.id, 'reference', 'Pinned title');
  reference.targetId = a.id;
  const committed = commitSnapshots(project), sourceBefore = JSON.stringify(committed);
  assert.deepEqual(committed.snapshots[a.id].node.style, a.style);
  const fork = forkProject(committed);
  const clipboard = pasteNodes(committed, copyNodes(committed, [a.id, b.id]), canvasId);
  const stored = createModuleFromSelection(committed, [a.id, b.id], 'Typography module');
  const inserted = instantiateModule(committed, stored, { canvasId, x: 1000, y: 1000 });
  const imported = readPublication(buildPublication(committed, '', ''));
  for (const source of [a, b]) {
    assert.deepEqual(fork.nodes.find(node => node.title === source.title)?.style, source.style);
    assert.deepEqual(clipboard.project.nodes.find(node => node.id === clipboard.idMap[source.id])?.style, source.style);
    assert.deepEqual(stored.project.nodes.find(node => node.title === source.title)?.style, source.style);
    assert.deepEqual(inserted.project.nodes.find(node => node.title === source.title)?.style, source.style);
    assert.deepEqual(imported.nodes.find(node => node.title === source.title)?.style, source.style);
  }
  const importedA = imported.nodes.find(node => node.title === a.title)!;
  assert.deepEqual(imported.snapshots[importedA.id].node.style, a.style);
  for (const result of [fork, clipboard.project, inserted.project, imported]) assert.deepEqual(validateProject(result), []);
  assert.equal(JSON.stringify(committed), sourceBefore);
});
