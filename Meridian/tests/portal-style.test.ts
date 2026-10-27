import assert from 'node:assert/strict';
import test from 'node:test';
import { PORTAL_STYLES, isCanvasStyle, isNodeStyle } from '../src/core/appearance.ts';
import { applyRecipe, type AuthoringRecipe } from '../src/core/authoring.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { createModuleFromSelection, instantiateModule, validateModule } from '../src/core/modules.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { buildPublication, createPublicationPayload, readPublication } from '../src/publication.ts';

const malformed = ['', 'door', 'DOORWAY', 'spiral', 'url(https://example.test/portal.svg)', 1, false, null, [], {}];

function fixture() {
  const project = newProject('Portable portals'), canvas = project.canvases[0];
  canvas.style = { background: '#102030', portalStyle: 'passage' };
  const launchers = PORTAL_STYLES.map((style, index) => {
    const node = addNode(project, canvas.id, 'canvas-link', style.label);
    Object.assign(node, { x: 60 + index * 300, y: 80, width: 240, height: 180, sizeMode: 'fixed',
      canvasTarget: { projectId: crypto.randomUUID(), canvasId: crypto.randomUUID() }, style: { portalStyle: style.value } });
    return node;
  });
  const inherited = addNode(project, canvas.id, 'canvas-link', 'Canvas default');
  Object.assign(inherited, { x: 60, y: 360, width: 240, height: 180, sizeMode: 'fixed', canvasTarget: { projectId: crypto.randomUUID() } });
  const pinnedCanvas = addCanvas(project, 'Pinned portals'); pinnedCanvas.style = { portalStyle: 'vortex' };
  for (const source of launchers) {
    const reference = addNode(project, pinnedCanvas.id, 'reference', `Pinned ${source.title}`); reference.targetId = source.id;
  }
  return { project: commitSnapshots(project), launchers, inherited };
}

test('the four portal choices are an optional bounded vocabulary on nodes and workspaces', () => {
  assert.deepEqual(PORTAL_STYLES, [
    { value: 'doorway', label: 'Endless doorway' }, { value: 'passage', label: 'Luminous passage' },
    { value: 'wormhole', label: 'Curved wormhole' }, { value: 'vortex', label: 'Spiral vortex' },
  ]);
  assert.ok(isNodeStyle(undefined)); assert.ok(isCanvasStyle(undefined));
  assert.ok(isNodeStyle({})); assert.ok(isCanvasStyle({}));
  for (const { value } of PORTAL_STYLES) { assert.ok(isNodeStyle({ portalStyle: value })); assert.ok(isCanvasStyle({ portalStyle: value })); }
  for (const value of malformed) { assert.equal(isNodeStyle({ portalStyle: value }), false); assert.equal(isCanvasStyle({ portalStyle: value }), false); }
  assert.equal(isNodeStyle({ portalStyle: 'doorway', portalSpeed: 2 }), false);
  assert.equal(isCanvasStyle({ portalStyle: 'doorway', portalScript: 'alert(1)' }), false);
  assert.equal(newProject().canvases[0].style?.portalStyle, undefined, 'Old documents retain the implicit default without migration.');
});

test('malformed portal styles reject on live nodes, pinned snapshots and canvas defaults', () => {
  const { project, launchers } = fixture(); assert.deepEqual(validateProject(project), []);
  for (const portalStyle of malformed) for (const surface of ['node', 'snapshot', 'canvas']) {
    const bad = structuredClone(project);
    const owner = surface === 'canvas' ? bad.canvases[0] : surface === 'snapshot'
      ? bad.snapshots[launchers[0].id].node : bad.nodes.find(node => node.id === launchers[0].id)!;
    owner.style = { ...owner.style, portalStyle } as typeof owner.style;
    assert.equal(isProjectShape(bad), false, `${surface}: ${JSON.stringify(portalStyle)}`);
    assert.ok(validateProject(bad).length);
  }
});

test('snapshots, forks and standalone publication preserve defaults and explicit overrides without mutation', () => {
  const { project, launchers } = fixture(), before = JSON.stringify(project);
  const payload = createPublicationPayload(project), fork = forkProject(project), imported = readPublication(buildPublication(project, '', ''));
  for (const portable of [payload.project, fork, imported]) {
    assert.deepEqual(portable.canvases.map(canvas => canvas.style), project.canvases.map(canvas => canvas.style));
    for (const source of launchers) {
      const node = portable.nodes.find(node => node.tag === source.tag)!;
      assert.deepEqual(node.style, source.style);
      assert.equal(portable.snapshots[node.id].node.style?.portalStyle, source.style?.portalStyle);
    }
    assert.equal(portable.nodes.find(node => node.tag === 'Canvas default')!.style?.portalStyle, undefined);
    assert.deepEqual(validateProject(portable), []);
  }
  assert.equal(JSON.stringify(project), before);
  imported.canvases[0].style!.portalStyle = 'wormhole';
  assert.equal(project.canvases[0].style!.portalStyle, 'passage');
});

test('clipboard and Module insertion preserve explicit portal choices and destination workspace defaults', () => {
  const { project, launchers, inherited } = fixture(), before = JSON.stringify(project);
  const destination = newProject('Destination'); destination.canvases[0].style = { portalStyle: 'wormhole' };
  const ids = [...launchers, inherited].map(node => node.id), pasted = pasteNodes(destination, copyNodes(project, ids), destination.entryCanvasId);
  for (const source of [...launchers, inherited]) assert.equal(pasted.project.nodes.find(node => node.id === pasted.idMap[source.id])!.style?.portalStyle, source.style?.portalStyle);
  assert.equal(pasted.project.canvases[0].style!.portalStyle, 'wormhole');
  const module = createModuleFromSelection(project, ids, 'Portal collection'); assert.deepEqual(validateModule(module), []);
  const inserted = instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 100, y: 100 });
  for (const source of [...launchers, inherited]) {
    const stored = module.project.nodes.find(node => node.tag === source.tag)!;
    assert.equal(stored.style?.portalStyle, source.style?.portalStyle);
    assert.equal(inserted.project.nodes.find(node => node.id === inserted.idMap[stored.id])!.style?.portalStyle, source.style?.portalStyle);
  }
  assert.equal(inserted.project.canvases[0].style!.portalStyle, 'wormhole');
  assert.equal(JSON.stringify(project), before);
});

test('declarative appearance edits accept portal choices and reject malformed values atomically', () => {
  const { project, launchers } = fixture(), before = JSON.stringify(project);
  const recipe: AuthoringRecipe = { version: 1,
    canvases: [{ key: 'workspace', existingId: project.entryCanvasId, style: { portalStyle: 'vortex' } }],
    nodes: [{ key: 'launcher', existingId: launchers[0].id, style: { portalStyle: 'wormhole' } }],
  };
  const edited = applyRecipe(project, recipe).project;
  assert.equal(edited.canvases[0].style!.portalStyle, 'vortex');
  assert.equal(edited.nodes.find(node => node.id === launchers[0].id)!.style!.portalStyle, 'wormhole');
  for (const portalStyle of malformed) for (const surface of ['canvases', 'nodes'] as const) {
    const bad = structuredClone(recipe);
    (bad[surface]![0].style as unknown as Record<string, unknown>).portalStyle = portalStyle;
    assert.throws(() => applyRecipe(project, bad), /appearance/);
  }
  assert.equal(JSON.stringify(project), before);
});
