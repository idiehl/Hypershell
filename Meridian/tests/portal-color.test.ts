import assert from 'node:assert/strict';
import test from 'node:test';
import { isCanvasStyle, isNodeStyle, PORTAL_STYLES } from '../src/core/appearance.ts';
import { effectivePortalColor, portalTintColor } from '../src/core/portal-color.ts';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { createModuleFromSelection, instantiateModule } from '../src/core/modules.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';

const invalid = ['', 'red', '#123', '#12345678', '123456', '#12GG34', 'url(x)', null, 12, false, {}, []];
function fixture() {
  const project = newProject('Portal colors'); project.canvases[0]!.style = { portalStyle: 'passage', portalColor: '#deaf65' };
  const pinned = addCanvas(project, 'Pinned choices'); pinned.style = { portalColor: '#79aee1' };
  const colors = ['#cc3366', '#3377cc', '#000000', '#ffffff'];
  const launchers = PORTAL_STYLES.map((style, index) => {
    const node = addNode(project, project.entryCanvasId, 'canvas-link', style.label);
    Object.assign(node, { x: 80 + index * 280, y: 100, width: 240, height: 180,
      canvasTarget: { projectId: crypto.randomUUID() }, style: { portalStyle: style.value, portalColor: colors[index] } });
    const reference = addNode(project, pinned.id, 'reference', `Pinned ${style.label}`); reference.targetId = node.id;
    return node;
  });
  const inherited = addNode(project, project.entryCanvasId, 'canvas-link', 'Inherited color'); inherited.canvasTarget = { projectId: crypto.randomUUID() };
  return { project: commitSnapshots(project), launchers, inherited };
}

test('portal colors accept only optional six-digit hex on nodes, canvas defaults and snapshots', () => {
  for (const color of ['#000000', '#ffffff', '#aBcD12']) {
    assert.ok(isNodeStyle({ portalColor: color })); assert.ok(isCanvasStyle({ portalColor: color }));
  }
  const { project, launchers } = fixture(); assert.deepEqual(validateProject(project), []);
  for (const portalColor of invalid) {
    assert.equal(isNodeStyle({ portalColor }), false); assert.equal(isCanvasStyle({ portalColor }), false);
    for (const surface of ['node', 'snapshot', 'canvas']) {
      const bad = structuredClone(project);
      const owner = surface === 'canvas' ? bad.canvases[0]! : surface === 'snapshot' ? bad.snapshots[launchers[0]!.id]!.node : bad.nodes.find(node => node.id === launchers[0]!.id)!;
      owner.style = { ...owner.style, portalColor } as typeof owner.style;
      assert.equal(isProjectShape(bad), false, `${surface}: ${JSON.stringify(portalColor)}`);
    }
  }
});

test('per-button colors, snapshots and workspace defaults survive publication, import and forks without mutation', () => {
  const { project, launchers, inherited } = fixture(), before = JSON.stringify(project);
  for (const result of [createPublicationPayload(project).project, forkProject(project), readPublication(buildPublication(project, '', ''))]) {
    assert.deepEqual(result.canvases.map(canvas => canvas.style), project.canvases.map(canvas => canvas.style));
    for (const source of launchers) {
      const copy = result.nodes.find(node => node.tag === source.tag)!;
      assert.deepEqual(copy.style, source.style); assert.equal(result.snapshots[copy.id]!.node.style!.portalColor, source.style!.portalColor);
    }
    assert.equal(result.nodes.find(node => node.tag === inherited.tag)!.style?.portalColor, undefined);
    assert.deepEqual(validateProject(result), []);
  }
  assert.equal(JSON.stringify(project), before);
});

test('clipboard and Module templates retain explicit color while inherited buttons follow their destination workspace', () => {
  const { project, launchers, inherited } = fixture(), destination = newProject('Destination');
  destination.canvases[0]!.style = { portalColor: '#64aadd' }; const ids = [...launchers, inherited].map(node => node.id);
  const copied = pasteNodes(destination, copyNodes(project, ids), destination.entryCanvasId);
  const module = createModuleFromSelection(project, ids, 'Colored portals'), inserted = instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 200, y: 200 });
  for (const source of [...launchers, inherited]) {
    assert.equal(copied.project.nodes.find(node => node.id === copied.idMap[source.id])!.style?.portalColor, source.style?.portalColor);
    const stored = module.project.nodes.find(node => node.tag === source.tag)!;
    assert.equal(stored.style?.portalColor, source.style?.portalColor);
    assert.equal(inserted.project.nodes.find(node => node.id === inserted.idMap[stored.id])!.style?.portalColor, source.style?.portalColor);
  }
  for (const portable of [copied.project, inserted.project]) assert.equal(portable.canvases[0]!.style!.portalColor, '#64aadd');
});

test('reference color inheritance is local to its own button and workspace, never its source', () => {
  const { project, launchers } = fixture(), source = launchers[0]!, reference = addNode(project, project.entryCanvasId, 'reference', 'Own cover'); reference.targetId = source.id;
  assert.equal(effectivePortalColor(reference, source, '#4488cc'), '#4488cc');
  assert.equal(effectivePortalColor(reference, source), undefined);
  reference.style = { portalColor: '#aa6655' }; assert.equal(effectivePortalColor(reference, source, '#4488cc'), '#aa6655');
  reference.style.portalColor = 'invalid'; assert.equal(effectivePortalColor(reference, source, '#4488cc'), '#4488cc');
});

test('tinted art preserves a bounded tonal range and supports black, white and saturated choices', () => {
  for (const tint of ['#000000', '#ffffff', '#ff0000', '#0055ff', '#72d9ac']) {
    const paints = ['#080c20', '#37725a', '#8fb1ef', '#f2fff5'].map(source => portalTintColor(source, tint));
    assert.ok(paints.every(paint => /^#[0-9a-f]{6}$/.test(paint))); assert.equal(new Set(paints).size, 4);
    const lightness = paints.map(paint => { const channels = [1, 3, 5].map(offset => parseInt(paint.slice(offset, offset + 2), 16)); return (Math.min(...channels) + Math.max(...channels)) / 2; });
    assert.ok(lightness[0]! < lightness[1]! && lightness[1]! < lightness[2]! && lightness[2]! < lightness[3]!);
  }
  assert.equal(portalTintColor('none', '#ff0000'), 'none'); assert.equal(portalTintColor('#ffffff', 'invalid'), '#ffffff');
});
