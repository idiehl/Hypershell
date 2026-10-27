import test from 'node:test';
import assert from 'node:assert/strict';
import { CANVAS_STYLE_PRESETS, NODE_SHAPES, NODE_STYLE_PRESETS, isCanvasStyle, isHexColor, isLinkLabel, isLinkStyle, isNodeStyle } from '../src/core/appearance.ts';
import { addCanvas, addNode, cloneProject, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';
import type { MeridianProject, NodeStyle } from '../src/core/types.ts';

test('appearance is optional and all shipped presets satisfy the portable vocabulary', () => {
  assert.ok(isProjectShape(newProject()));
  assert.ok(isNodeStyle(undefined)); assert.ok(isCanvasStyle(undefined)); assert.ok(isLinkStyle(undefined));
  for (const preset of NODE_STYLE_PRESETS) assert.ok(isNodeStyle(preset.style), preset.id);
  for (const preset of CANVAS_STYLE_PRESETS) assert.ok(isCanvasStyle(preset.style), preset.id);
  assert.ok(isHexColor('#aBcD09')); assert.ok(!isHexColor('red')); assert.ok(!isHexColor('#abcd'));
});

test('content fitting and fixed typography survive snapshots, publication and copying', () => {
  const project = newProject('Scalable content');
  const savedCanvas = addCanvas(project, 'Saved content');
  for (const contentSizing of ['fit', 'fixed'] as const) {
    const node = addNode(project, project.entryCanvasId, 'media', contentSizing);
    node.text = 'A small tile can contain a complete reading composition.';
    node.style = { contentSizing, fontSize: 16, titleSize: 24 };
    const reference = addNode(project, savedCanvas.id, 'reference', `Saved ${contentSizing}`);
    reference.targetId = node.id;
  }
  const committed = commitSnapshots(project), imported = readPublication(buildPublication(committed, '', ''));
  for (const contentSizing of ['fit', 'fixed'] as const) {
    const node = imported.nodes.find(node => node.title === contentSizing)!;
    assert.equal(node.style?.contentSizing, contentSizing);
    assert.equal(imported.snapshots[node.id].node.style?.contentSizing, contentSizing);
  }
  assert.deepEqual(validateProject(imported), []);
  assert.ok(forkProject(committed).nodes.some(node => node.style?.contentSizing === 'fixed'));
  for (const contentSizing of ['stretch', true, 1, null]) assert.equal(isNodeStyle({ contentSizing }), false);
});

test('node styles reject injected CSS, unsupported families, wrong types and non-finite or excessive dimensions', () => {
  for (const style of [null, [], 'paper', { fill: 'url(https://example.test/a)' }, { border: '#123' },
    { textColor: '#ffffff;display:none' }, { accent: 'transparent' }, { fontFamily: 'untrusted font' },
    { fontSize: 9 }, { fontSize: 73 }, { titleSize: 97 }, { titleSize: '24' }, { fontSize: NaN },
    { borderWidth: -1 }, { borderWidth: 9 }, { radius: Infinity }, { radius: 49 },
    { textAlign: 'justify' }, { imageFit: 'stretch' }, { variant: 'html' }, { shape: 'url(external.svg)' }, { shape: 'star' }, { shape: 4 }, { cssText: 'display:none' }]) {
    assert.equal(isNodeStyle(style), false, JSON.stringify(style));
  }
  assert.ok(isNodeStyle({ fontSize: 10, titleSize: 96, borderWidth: 0, radius: 48, imageFit: 'cover', variant: 'label' }));
});

test('canvas and link styles enforce exact keys and bounded safe values', () => {
  for (const style of [null, { background: 'url(x)' }, { gridVisible: 1 }, { opacity: 0 }]) assert.equal(isCanvasStyle(style), false);
  for (const style of [null, { color: 'red' }, { width: 0 }, { width: 33 }, { width: NaN }, { dashed: 'yes' }, { marker: 'external.svg' }]) assert.equal(isLinkStyle(style), false);
  assert.ok(isCanvasStyle({ background: '#FFF0C0', grid: '#123456', gridVisible: false }));
  assert.ok(isLinkStyle({ color: '#abcdef', width: 1.5, dashed: false }));
  assert.ok(isLinkLabel('A < B & C — parentage'));
  assert.ok(isLinkLabel('a'.repeat(160))); assert.ok(!isLinkLabel('a'.repeat(161)));
  assert.ok(!isLinkLabel(null)); assert.ok(!isLinkLabel(23)); assert.ok(!isLinkLabel('bad\u0000label')); assert.ok(!isLinkLabel('bad\u007flabel'));
});

function styledProject() {
  const project = newProject('Styled collection');
  project.canvases[0].style = { background: '#EFE7D1', grid: '#B6AB8F', gridVisible: false };
  const source = addNode(project, project.entryCanvasId, 'text', 'A title');
  source.style = { fill: '#FFF8E1', border: '#BDA977', textColor: '#362916', accent: '#A36B27', fontFamily: 'serif', fontSize: 16, titleSize: 30, textAlign: 'center', variant: 'card', borderWidth: 2, radius: 8, shape: 'hexagon' };
  const target = addNode(project, project.entryCanvasId, 'text', 'A free label');
  target.style = { variant: 'label', titleSize: 42, fontSize: 22, textColor: '#24333D' };
  project.links.push({ id: 'styled-link', canvasId: project.entryCanvasId, sourceId: source.id, targetId: target.id, kind: 'visual', relation: 'causal', label: 'Selected descent', style: { color: '#AA7033', width: 3, dashed: false } });
  const second = addCanvas(project, 'Saved appearance');
  const reference = addNode(project, second.id, 'reference', 'An independent appearance');
  reference.targetId = source.id;
  reference.style = { fill: '#172C40', textColor: '#DFEAF4', fontFamily: 'mono', fontSize: 13, shape: 'none' };
  return { project: commitSnapshots(project), sourceId: source.id, referenceId: reference.id };
}

test('project shape validates current nodes, canvases, links and saved snapshot appearance', () => {
  const { project, sourceId } = styledProject();
  assert.deepEqual(validateProject(project), []);
  const invalid = (mutate: (copy: MeridianProject) => void) => {
    const copy = cloneProject(project); mutate(copy); assert.equal(isProjectShape(copy), false); assert.ok(validateProject(copy).length);
  };
  invalid(copy => { copy.nodes[1].style = { fontSize: '20' } as unknown as NodeStyle; });
  invalid(copy => { copy.canvases[0].style = { background: 'transparent' }; });
  invalid(copy => { copy.links[0].style = { width: 0 }; });
  invalid(copy => { copy.links[0].label = 'x'.repeat(161); });
  invalid(copy => { copy.snapshots[sourceId].node.style = { fontFamily: 'external' } as unknown as NodeStyle; });
});

test('forks and saved snapshots preserve independent authored appearance without sharing objects', () => {
  const { project, sourceId, referenceId } = styledProject();
  const fork = forkProject(project);
  assert.notEqual(fork.id, project.id);
  assert.deepEqual(fork.nodes.find(node => node.title === 'A title')?.style, project.nodes.find(node => node.id === sourceId)?.style);
  const reference = project.nodes.find(node => node.id === referenceId)!;
  assert.notDeepEqual(reference.style, project.snapshots[sourceId].node.style);
  fork.nodes.find(node => node.title === 'A title')!.style!.fontSize = 33;
  assert.equal(project.snapshots[sourceId].node.style!.fontSize, 16);
  assert.equal(project.nodes.find(node => node.id === sourceId)!.style!.fontSize, 16);
});

test('standalone publication and fresh import retain node, canvas, link and pinned styles', () => {
  const { project, sourceId, referenceId } = styledProject();
  const payload = createPublicationPayload(project, { includeExternalReferences: false });
  assert.deepEqual(payload.project.canvases[0].style, project.canvases[0].style);
  assert.deepEqual(payload.project.links[0].style, project.links[0].style);
  assert.equal(payload.project.links[0].label, 'Selected descent');
  assert.deepEqual(payload.project.snapshots[sourceId].node.style, project.snapshots[sourceId].node.style);
  assert.deepEqual(payload.project.nodes.find(node => node.id === referenceId)?.style, project.nodes.find(node => node.id === referenceId)?.style);
  const imported = readPublication(buildPublication(project, '', ''));
  assert.notEqual(imported.id, project.id);
  assert.deepEqual(imported.canvases[0].style, project.canvases[0].style);
  assert.deepEqual(imported.links[0].style, project.links[0].style);
  assert.equal(imported.links[0].label, 'Selected descent');
  assert.deepEqual(validateProject(imported), []);
});

test('publication strips unknown appearance metadata and keeps labels inert', () => {
  const { project, sourceId } = styledProject();
  Object.assign(project.nodes[1].style!, { sourcePath: 'C:/private/secret.css' });
  Object.assign(project.canvases[0].style!, { stylesheet: 'C:/private/theme.css' });
  Object.assign(project.links[0].style!, { marker: 'C:/private/arrow.svg' });
  Object.assign(project.snapshots[sourceId].node.style!, { sourcePath: 'C:/private/source.css' });
  project.links[0].label = '</script><img src=x onerror=alert(1)>';
  const html = buildPublication(project, '', '');
  assert.ok(!html.includes('C:/private'));
  assert.ok(!html.includes('<img src=x'));
  const imported = readPublication(html);
  assert.equal(imported.links[0].label, project.links[0].label);
  assert.deepEqual(validateProject(imported), []);
});

test('every basic backdrop shape survives saved snapshots, publication and import for all node surfaces', () => {
  const project = newProject('Backdrop shapes');
  const consumer = addCanvas(project, 'Pinned shapes');
  for (const shape of NODE_SHAPES) {
    const node = addNode(project, project.entryCanvasId, 'container', shape.label);
    node.style = { shape: shape.value };
    const reference = addNode(project, consumer.id, 'reference', `Pinned ${shape.label}`); reference.targetId = node.id;
    assert.ok(isNodeStyle(node.style), shape.value);
  }
  const committed = commitSnapshots(project);
  const imported = readPublication(buildPublication(committed, '', ''));
  for (const shape of NODE_SHAPES) {
    const node = imported.nodes.find(node => node.title === shape.label)!;
    assert.equal(node.style?.shape, shape.value);
    assert.equal(imported.snapshots[node.id].node.style?.shape, shape.value);
  }
  assert.deepEqual(validateProject(imported), []);
});

