import { workspaceAction } from './ui-helpers.ts';
import { selectEditorNode } from './ui-helpers.ts';
import { test, expect, type APIRequestContext, type Locator, type Page, type TestInfo } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { readPublication } from '../src/publication.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';

type Settings = { stickyEdges: boolean; snapToGrid: boolean; smartAlign: boolean };
type Point = { x: number; y: number };
const OFF: Settings = { stickyEdges: false, snapToGrid: false, smartAlign: false };
const labels: Record<keyof Settings, string> = {
  stickyEdges: 'Sticky edges', snapToGrid: 'Snap to grid', smartAlign: 'Smart align',
};

// Every scenario creates a new project in playwright.config.ts's isolated 5291 host.
// Never open or modify the user's MyNet/default store or mythology project.
async function seed(page: Page, request: APIRequestContext, name: string, settings: Settings = OFF) {
  const project = newProject(`Snapping QA: ${name}`);
  project.canvases[0].snapping = settings;
  const moving = addNode(project, project.entryCanvasId, 'text', 'Moving card');
  const peer = addNode(project, project.entryCanvasId, 'text', 'Inherited peer');
  const anchor = addNode(project, project.entryCanvasId, 'text', 'Alignment anchor');
  // These authored rectangles exercise exact edge contacts, independent of
  // automatic content sizing, whose width can vary with each card's text.
  Object.assign(moving, { sizeMode: 'fixed', x: 90, y: 80, width: 150, height: 100, text: 'Drag this card' });
  Object.assign(peer, { sizeMode: 'fixed', x: 605, y: 80, width: 150, height: 100, text: 'Independent inheritance' });
  Object.assign(anchor, { sizeMode: 'fixed', x: 420, y: 250, width: 180, height: 130, text: 'Stationary comparison' });
  const response = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(response.ok()).toBeTruthy();
  const saved = await response.json() as MeridianProject;
  await page.goto(`/?project=${saved.id}`);
  await expect(page.locator('.save-state')).toHaveText('Saved · r1');
  await expect(card(page, moving.id)).toBeVisible();
  return { project: saved, moving, peer, anchor };
}

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const snapping = (entity: object | undefined): Settings | undefined =>
  (entity as { snapping?: Settings } | undefined)?.snapping;

async function canvasDialog(page: Page) {
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('canvas');
  await page.locator('.inspector-tree-title').first().click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('properties');
  return page.locator('.inspector-panel-properties');
}

async function nodeDialog(page: Page, node: MeridianNode) {
  await selectEditorNode(page,node.id,node.title);
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('properties');
  return page.locator('.inspector-panel-properties');
}

async function fillSettings(dialog: Locator, settings: Settings) {
  await dialog.getByRole('combobox', { name: 'Alignment behavior', exact: true }).selectOption(settings.stickyEdges ? 'sticky' : settings.smartAlign ? 'align' : 'free');
}

async function expectSettings(dialog: Locator, settings: Settings, disabled = false) {
  await expect(dialog.getByRole('combobox', { name: 'Alignment behavior', exact: true })).toHaveValue(disabled ? 'inherit' : settings.stickyEdges ? 'sticky' : settings.smartAlign || settings.snapToGrid ? 'align' : 'free');
}

async function apply(dialog: Locator) {
  await expect(dialog.getByRole('combobox', { name: 'Alignment behavior', exact: true })).toBeEnabled();
}

async function setCanvas(page: Page, settings: Settings) {
  const dialog = await canvasDialog(page);
  await fillSettings(dialog, settings);
  await apply(dialog);
}

async function worldPoint(page: Page, id: string): Promise<Point> {
  // Bounding boxes include the temporary CSS drag transform. Merely reading the
  // authored left/top here would miss a broken or incorrectly snapped preview.
  // Automatic saving can retire the resolved element before evaluation starts.
  let actual: Point | null = null;
  await expect.poll(async () => {
    actual = await card(page, id).evaluate(element => {
      if (!element.isConnected) return null;
      const viewport = element.closest('.ms-viewport')!.getBoundingClientRect();
      const world = element.closest('.ms-world')!;
      const matrix = new DOMMatrix(getComputedStyle(world).transform);
      const bounds = element.getBoundingClientRect();
      return { x: (bounds.x - viewport.x - matrix.e) / matrix.a,
        y: (bounds.y - viewport.y - matrix.f) / matrix.d };
    });
    return actual !== null;
  }).toBe(true);
  return actual!;
}

async function expectPoint(page: Page, id: string, point: Point, tolerance = .15) {
  await expect.poll(async () => {
    const actual = await worldPoint(page, id);
    return Math.max(Math.abs(actual.x - point.x), Math.abs(actual.y - point.y));
  }).toBeLessThanOrEqual(tolerance);
}

async function beginDrag(page: Page, id: string, rawDestination: Point, alt = false) {
  const tile = card(page, id), before = await tile.locator('.ms-node-header').boundingBox(), viewport = await page.locator('.ms-viewport').boundingBox();
  // Inspecting another node may center that node and move this card offscreen.
  // Bring the real drag target into view before measuring its camera and pointer
  // origin; a mouse-down outside the canvas cannot exercise snapping behavior.
  if (!before || !viewport || before.x < viewport.x || before.y < viewport.y ||
    before.x + before.width > viewport.x + viewport.width || before.y + before.height > viewport.y + viewport.height) {
    await selectEditorNode(page, id, (await tile.locator('.ms-node-title').textContent())?.trim() ?? id);
  }
  const original = await worldPoint(page, id);
  const scale = await page.locator('.ms-world').evaluate(element => new DOMMatrix(getComputedStyle(element).transform).a);
  const header = await card(page, id).locator('.ms-node-header').boundingBox();
  expect(header).not.toBeNull();
  const start = { x: header!.x + Math.min(40 * scale, header!.width / 2), y: header!.y + header!.height / 2 };
  if (alt) await page.keyboard.down('Alt');
  await page.mouse.move(start.x, start.y);
  await page.mouse.down();
  await page.mouse.move(start.x + (rawDestination.x - original.x) * scale,
    start.y + (rawDestination.y - original.y) * scale, { steps: 7 });
  return { start, scale };
}

async function endDrag(page: Page, id: string, expected: Point, alt = false, tolerance = .15) {
  await page.mouse.up();
  if (alt) await page.keyboard.up('Alt');
  await expectPoint(page, id, expected, tolerance);
  // Screen transforms round fractional CSS pixels; compare stored inputs to authored geometry exactly.
  const actual = await card(page, id).evaluate(element => ({ x: Number.parseFloat((element as HTMLElement).style.left), y: Number.parseFloat((element as HTMLElement).style.top) }));
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption('appearance');
  expect(Number(await page.getByLabel('X', { exact: true }).inputValue())).toBe(actual.x);
  expect(Number(await page.getByLabel('Y', { exact: true }).inputValue())).toBe(actual.y);
  await expect(page.locator('.ms-snap-guide')).toHaveCount(0);
}

async function drag(page: Page, id: string, raw: Point, expected: Point) {
  await beginDrag(page, id, raw);
  await expectPoint(page, id, expected);
  await endDrag(page, id, expected);
}

test('Properties alignment changes immediately, undo once, and preserve independent node inheritance', async ({ page, request }, testInfo) => {
  const { moving, peer, anchor } = await seed(page, request, 'properties and inheritance', { ...OFF, stickyEdges: true });
  let panel = await canvasDialog(page); await expectSettings(panel, { ...OFF, stickyEdges: true });
  await expect(page.getByRole('button', {name:'Canvas settings',exact:true})).toHaveCount(0);
  await expect(page.getByRole('button', {name:'Node settings',exact:true})).toHaveCount(0);
  await fillSettings(panel, OFF);
  await page.getByRole('button', {name:'Undo',exact:true}).click(); panel=await canvasDialog(page); await expectSettings(panel,{...OFF,stickyEdges:true});
  await page.getByRole('button', {name:'Redo',exact:true}).click(); panel=await canvasDialog(page); await expectSettings(panel,OFF);
  await fillSettings(panel,{...OFF,stickyEdges:true});
  panel=await nodeDialog(page,moving); await expectSettings(panel,{...OFF,stickyEdges:true},true); await fillSettings(panel,OFF);
  panel=await nodeDialog(page,peer); await expectSettings(panel,{...OFF,stickyEdges:true},true);
  await drag(page,moving.id,{x:264,y:259},{x:264,y:259});
  await drag(page,peer.id,{x:604,y:260},{x:600,y:260});
  await expectPoint(page,anchor.id,{x:420,y:250});
  panel=await nodeDialog(page,moving);await expectSettings(panel,OFF);
  await panel.getByRole('combobox',{name:'Alignment behavior',exact:true}).selectOption('inherit');
  await drag(page,moving.id,{x:274,y:101},{x:274,y:101});
  await page.setViewportSize({width:390,height:800});await page.screenshot({path:testInfo.outputPath('properties-390.png')});
  await page.setViewportSize({width:1500,height:950});
});

test('all snapping off preserves free pointer preview and committed coordinates', async ({ page, request }) => {
  const { moving, anchor } = await seed(page, request, 'free movement');
  // This ends near an opposing edge and away from the 24-unit grid. Either
  // accidentally enabled snapping mode would visibly change the answer.
  const raw = { x: 264, y: 259 };
  await beginDrag(page, moving.id, raw);
  await expectPoint(page, moving.id, raw);
  await expect(page.locator('.ms-snap-guide')).toHaveCount(0);
  await endDrag(page, moving.id, raw);
  await expectPoint(page, anchor.id, { x: 420, y: 250 });
});

test('grid snapping follows world coordinates after pan and zoom; Alt bypasses it', async ({ page, request }, testInfo) => {
  const { moving } = await seed(page, request, 'grid and camera', { ...OFF, snapToGrid: true });
  const viewport = await page.locator('.ms-viewport').boundingBox();
  expect(viewport).not.toBeNull();
  // Start in empty lower-left canvas space, away from cards and camera controls.
  await page.mouse.move(viewport!.x + 40, viewport!.y + viewport!.height - 95);
  await page.mouse.down();
  await page.mouse.move(viewport!.x + 113, viewport!.y + viewport!.height - 54, { steps: 5 });
  await page.mouse.up();
  await page.getByRole('button', { name: 'Zoom out', exact: true }).click();
  const camera = await page.locator('.ms-viewport').evaluate(element => {
    const matrix = new DOMMatrix(getComputedStyle(element.querySelector('.ms-world')!).transform);
    const style = getComputedStyle(element);
    return { scale: matrix.a, x: matrix.e, y: matrix.f,
      gridSize: style.backgroundSize.split(' ').map(parseFloat),
      gridPosition: style.backgroundPosition.split(' ').map(parseFloat) };
  });
  expect(camera.scale).toBeCloseTo(.8, 5);
  expect(camera.gridSize[0]).toBeCloseTo(24 * camera.scale, 4);
  expect(camera.gridSize[1]).toBeCloseTo(24 * camera.scale, 4);
  // The background lattice must share the world origin, not remain fixed to the
  // screen. A whole-cell equivalent offset is also a correct repeating pattern.
  for (const [actual, origin] of [[camera.gridPosition[0], camera.x], [camera.gridPosition[1], camera.y]]) {
    // CSS radial-gradient dots sit at each tile's center, not its corner.
    const cells = (actual + 12 * camera.scale - origin) / (24 * camera.scale);
    expect(Math.abs(cells - Math.round(cells))).toBeLessThan(.01);
  }
  await beginDrag(page, moving.id, { x: 149, y: 159 });
  await expectPoint(page, moving.id, { x: 144, y: 168 });
  await page.screenshot({ path: testInfo.outputPath('grid-pan-zoom-preview.png') });
  await endDrag(page, moving.id, { x: 144, y: 168 });
  const raw = { x: 177, y: 199 };
  await beginDrag(page, moving.id, raw, true);
  await expectPoint(page, moving.id, raw, 1.3);
  await expect(page.locator('.ms-snap-guide')).toHaveCount(0);
  await endDrag(page, moving.id, raw, true, 1.3);
  const final = await worldPoint(page, moving.id);
  expect(Math.abs(final.x / 24 - Math.round(final.x / 24))).toBeGreaterThan(.15);
});

for (const scenario of [
  { mode: 'stickyEdges' as const, title: 'sticky edges align opposing borders', raw: { x: 274, y: 260 }, snapped: { x: 270, y: 260 } },
  { mode: 'smartAlign' as const, title: 'smart align matches same-side edges', raw: { x: 424, y: 101 }, snapped: { x: 420, y: 101 } },
]) {
  test(`${scenario.title} during preview and commit`, async ({ page, request }, testInfo) => {
    const { moving, anchor } = await seed(page, request, scenario.mode);
    await setCanvas(page, { ...OFF, [scenario.mode]: true });
    await beginDrag(page, moving.id, scenario.raw);
    await expectPoint(page, moving.id, scenario.snapped);
    const guides = page.locator(`.ms-snap-guide-${scenario.mode === 'stickyEdges' ? 'sticky' : 'align'}[data-axis="x"]`);
    expect(await guides.count()).toBeGreaterThan(0);
    expect(await guides.first().evaluate(element => getComputedStyle(element).stroke)).not.toBe('none');
    await expectPoint(page, anchor.id, { x: 420, y: 250 });
    await page.screenshot({ path: testInfo.outputPath(`${scenario.mode}-preview.png`) });
    await endDrag(page, moving.id, scenario.snapped);
    await page.getByRole('button', { name: 'Undo', exact: true }).click();
    await expectPoint(page, moving.id, { x: 90, y: 80 });
    await page.getByRole('button', { name: 'Redo', exact: true }).click();
    await expectPoint(page, moving.id, scenario.snapped);
  });
}

test('snapping survives draft recovery, save, package round-trip and fresh publication import', async ({ page, request }, testInfo: TestInfo) => {
  const { project, moving, peer } = await seed(page, request, 'persistence');
  const canvasSettings = { stickyEdges: true, snapToGrid: false, smartAlign: false };
  const nodeSettings = { stickyEdges: false, snapToGrid: false, smartAlign: true };
  await page.route(`**/api/projects/${project.id}`,route=>route.request().method()==='PUT'?route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({error:'Pause commits to exercise snapping recovery'})}):route.continue());
  await setCanvas(page, canvasSettings);
  const dialog = await nodeDialog(page, moving);  await fillSettings(dialog, nodeSettings);
  await apply(dialog);
  const summary = (p: MeridianProject) => ({
    canvas: snapping(p.canvases.find(c => c.id === p.entryCanvasId)),
    moving: snapping(p.nodes.find(n => n.title === moving.title)),
    peer: snapping(p.nodes.find(n => n.title === peer.title)),
  });
  const expected = { canvas: canvasSettings, moving: nodeSettings, peer: undefined };
  await expect.poll(async () => {
    const response = await request.get(`/api/projects/${project.id}/recovery`);
    return response.ok() ? summary((await response.json()).project) : null;
  }).toEqual(expected);
  page.on('dialog', dialog => void dialog.accept());
  await page.reload();
  await page.getByRole('button', { name: 'Restore draft', exact: true }).click();
  let settingsDialog = await nodeDialog(page, moving);
  await expectSettings(settingsDialog, nodeSettings);  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('content');
  await expect(page.locator('.save-state')).toHaveText('Not saved · retry');
  await page.unroute(`**/api/projects/${project.id}`);
  await workspaceAction(page,'Save');
  await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  const saved = await (await request.get(`/api/projects/${project.id}`)).json() as MeridianProject;
  expect(summary(saved)).toEqual(expected);
  await page.reload();
  await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  settingsDialog = await canvasDialog(page);
  await expectSettings(settingsDialog, canvasSettings);
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('content');

  const packageEvent = page.waitForEvent('download');
  await workspaceAction(page,'Export Work');
  await page.getByRole('button', { name: 'Download Work', exact: true }).click();
  const archive = await packageEvent;
  expect(archive.suggestedFilename()).toMatch(/\.meridian$/);
  const archivePath = testInfo.outputPath('snapping-roundtrip.meridian');
  await archive.saveAs(archivePath);
  const importedPackage = await request.post('/api/import', { multipart: {
    file: { name: archive.suggestedFilename(), mimeType: 'application/octet-stream', buffer: await readFile(archivePath) },
  } });
  expect(importedPackage.ok()).toBeTruthy();
  expect(summary(await importedPackage.json())).toEqual(expected);

  const publishedEvent = page.waitForEvent('download');
  await workspaceAction(page,'Publish');
  await page.getByRole('button', { name: 'Publish HTML', exact: true }).click();
  const published = await publishedEvent;
  const htmlPath = testInfo.outputPath('snapping-publication.html');
  await published.saveAs(htmlPath);
  const portable = readPublication(await readFile(htmlPath, 'utf8'));
  expect(portable.id).not.toBe(project.id);
  expect(summary(portable)).toEqual(expected);
  await page.getByRole('button', { name: 'Done', exact: true }).click();
  const chooser = page.waitForEvent('filechooser');
  await workspaceAction(page,'Import');
  await (await chooser).setFiles(htmlPath);
  await expect.poll(()=>new URL(page.url()).searchParams.get('project')).not.toBe(project.id);
  await expect(page.locator('.save-state')).toHaveText('Saved · r1');
  const importedId = new URL(page.url()).searchParams.get('project');
  expect(importedId).toBeTruthy();
  expect(importedId).not.toBe(project.id);
  const imported = await (await request.get(`/api/projects/${importedId}`)).json() as MeridianProject;
  expect(imported.title).toContain('imported');
  await expect(page.locator('[data-current-canvas]')).toContainText(imported.canvases.find(canvas=>canvas.id===imported.entryCanvasId)!.title);
  expect(summary(imported)).toEqual(expected);
  expect((await (await request.get(`/api/projects/${project.id}`)).json()).revision).toBe(2);
});

test('click, return and cancel preserve a container; a committed drag moves its children and route together', async ({ page, request }, testInfo) => {
  const project = newProject('Snapping QA: container gestures');
  project.canvases[0].snapping = { snapToGrid: true };
  const container = addNode(project, project.entryCanvasId, 'container', 'Moving group');
  const first = addNode(project, project.entryCanvasId, 'text', 'First child');
  const second = addNode(project, project.entryCanvasId, 'text', 'Second child');
  // Fixed geometry makes route anchoring and whole-branch translation exact.
  Object.assign(container, { sizeMode: 'fixed', x: 97, y: 97, width: 360, height: 240 });
  Object.assign(first, { sizeMode: 'fixed', parentId: container.id, x: 121, y: 147, width: 120, height: 90 });
  Object.assign(second, { sizeMode: 'fixed', parentId: container.id, x: 301, y: 147, width: 120, height: 90 });
  const linkId = crypto.randomUUID();
  project.links.push({ id: linkId, canvasId: project.entryCanvasId, sourceId: first.id, targetId: second.id,
    kind: 'visual', relation: 'causal', sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: .5 },
    points: [{ x: 270, y: 210 }] });
  const created = await request.post('/api/projects', { data: commitSnapshots(project) });
  expect(created.ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`);
  const path = page.locator(`.ms-link-line[data-link-id="${linkId}"]`);
  const originalPath = 'M241,192 L270,210 L301,192';
  const movedPath = 'M288,263 L317,281 L348,263';
  const unchanged = async () => {
    await expectPoint(page, container.id, { x: 97, y: 97 });
    await expectPoint(page, first.id, { x: 121, y: 147 });
    await expectPoint(page, second.id, { x: 301, y: 147 });
    await expect(path).toHaveAttribute('d', originalPath);
    await expect(page.locator('.ms-snap-guide')).toHaveCount(0);
    await expect(page.locator('.save-state')).toHaveText('Saved · r1');
    await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  };
  // An ordinary click on an off-grid group must never become an implicit move.
  await card(page, container.id).locator('.ms-node-header').click({ position: { x: 45, y: 15 } });
  await unchanged();
  const firstDrag = await beginDrag(page, container.id, { x: 149, y: 159 });
  await expectPoint(page, first.id, { x: 168, y: 218 });
  await expect(path).toHaveAttribute('d', movedPath);
  await page.mouse.move(firstDrag.start.x, firstDrag.start.y, { steps: 5 });
  await expectPoint(page, container.id, { x: 97, y: 97 });
  await expectPoint(page, first.id, { x: 121, y: 147 });
  await expect(path).toHaveAttribute('d', originalPath);
  await page.mouse.up();
  await unchanged();

  // Capture the browser's actual pointer ID before delivering its cancellation.
  await card(page, container.id).evaluate(element => element.addEventListener('pointerdown', event => {
    (element as HTMLElement).dataset.qaPointer = String((event as PointerEvent).pointerId);
  }, { once: true }));
  await beginDrag(page, container.id, { x: 149, y: 159 });
  await expectPoint(page, second.id, { x: 348, y: 218 });
  await expect(path).toHaveAttribute('d', movedPath);
  await card(page, container.id).evaluate(element => element.dispatchEvent(new PointerEvent('pointercancel', {
    pointerId: Number((element as HTMLElement).dataset.qaPointer), bubbles: true,
  })));
  await page.mouse.up();
  await unchanged();

  await beginDrag(page, container.id, { x: 149, y: 159 });
  await expectPoint(page, container.id, { x: 144, y: 168 });
  await expectPoint(page, first.id, { x: 168, y: 218 });
  await expectPoint(page, second.id, { x: 348, y: 218 });
  await expect(path).toHaveAttribute('d', movedPath);
  await page.screenshot({ path: testInfo.outputPath('container-children-route-preview.png') });
  await endDrag(page, container.id, { x: 144, y: 168 });
  await expectPoint(page, first.id, { x: 168, y: 218 });
  await expect(path).toHaveAttribute('d', movedPath);
  await workspaceAction(page,'Save');
  await expect(page.locator('.save-state')).toHaveText('Saved · r2');
  const saved = await (await request.get(`/api/projects/${project.id}`)).json() as MeridianProject;
  expect(saved.links.find(link => link.id === linkId)?.points).toEqual([{ x: 317, y: 281 }]);
  expect(saved.nodes.find(node => node.id === first.id)).toMatchObject({ x: 168, y: 218, parentId: container.id });
});

test('imported partial settings survive inspection, and choosing alignment creates an explicit mode', async ({ page, request }, testInfo) => {
  const project=newProject('Partial imported alignment');project.canvases[0].snapping={stickyEdges:true,snapToGrid:true,smartAlign:true};
  const node=addNode(project,project.entryCanvasId,'text','Partially overridden');node.snapping={stickyEdges:false};
  const exported=await request.post('/api/export',{data:commitSnapshots(project)});expect(exported.ok()).toBeTruthy();
  const file=testInfo.outputPath('partial-settings.meridian');await writeFile(file,await exported.body());
  await page.goto('/');const windowCount=page.context().pages().length,chooser=page.waitForEvent('filechooser');await workspaceAction(page,'Import');await(await chooser).setFiles(file);
  await page.waitForURL(url=>!!url.searchParams.get('project'));expect(page.context().pages()).toHaveLength(windowCount);
  await expect(page.locator('.save-state')).toHaveText('Saved · r1');const id=new URL(page.url()).searchParams.get('project')!;
  let panel=await nodeDialog(page,node);await expectSettings(panel,{stickyEdges:false,smartAlign:true,snapToGrid:true});
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('content');
  expect((await(await request.get('/api/projects/'+id)).json()).nodes.find((entry:any)=>entry.id===node.id).snapping).toEqual({stickyEdges:false});
  panel=await nodeDialog(page,node);await fillSettings(panel,{...OFF,stickyEdges:true});await workspaceAction(page,'Save');
  await expect(page.locator('.save-state')).toHaveText('Saved · r2');let saved=await(await request.get('/api/projects/'+id)).json();
  expect(saved.nodes.find((entry:any)=>entry.id===node.id).snapping).toEqual({stickyEdges:true,smartAlign:false,snapToGrid:false});
  await setCanvas(page,OFF);panel=await nodeDialog(page,node);await expectSettings(panel,{...OFF,stickyEdges:true});await workspaceAction(page,'Save');
  await expect(page.locator('.save-state')).toHaveText('Saved · r3');saved=await(await request.get('/api/projects/'+id)).json();
  expect(saved.nodes.find((entry:any)=>entry.id===node.id).snapping).toEqual({stickyEdges:true,smartAlign:false,snapToGrid:false});
});
