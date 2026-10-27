import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['scene.css', 'grid.css', 'gallery-scene.css', 'window-scene.css', 'canvas-launcher.css'].map(name => readFile(`src/${name}`, 'utf8')))).join('\n');
  script = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';import {setSnapLock,lockedClusterNodeIds} from './src/core/clusters.ts';window.clusterHarness={renderScene,setSnapLock,lockedClusterNodeIds};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

async function mount(page: Page) {
  const project = newProject('Clusters and reusable selection'), canvasId = project.entryCanvasId;
  const a = addNode(project, canvasId, 'media', 'A'), b = addNode(project, canvasId, 'media', 'B'), c = addNode(project, canvasId, 'media', 'C');
  [a, b, c].forEach((node, index) => Object.assign(node, { x: 70 + index * 200, y: 100, width: 200, height: 110, sizeMode: 'fixed', collapsed: false, childLayout: 'spatial', placement: 'manual', style: { shape: 'rectangle', titleSize: 24 }, text: `Node ${node.title}`, contentType: 'text' }));
  const child = addNode(project, canvasId, 'media', 'Child of A'); Object.assign(child, { parentId: a.id, x: 100, y: 330, width: 140, height: 65, sizeMode: 'fixed', placement: 'manual', style: { shape: 'circle' }, text: 'A child', contentType: 'text' });
  project.links.push({ id: 'snap-ab', canvasId, sourceId: a.id, targetId: b.id, kind: 'visual', relation: 'generic', direction: 'none', snap: { locked: false } },
    { id: 'snap-bc', canvasId, sourceId: b.id, targetId: c.id, kind: 'visual', relation: 'generic', direction: 'none', snap: { locked: false } },
    { id: 'concept-ac', canvasId, sourceId: a.id, targetId: c.id, kind: 'visual', relation: 'generic', direction: 'forward', points: [{ x: 270, y: 260 }, { x: 470, y: 260 }], style: { color: '#CC5566', width: 3 } });
  await page.setContent('<style>html,body{margin:0}#scene{width:1400px;height:850px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(project => {
    const state: any = { project, view: { x: 20, y: 20, scale: 1 }, selections: [], mode: 'select', locks: [], moves: [], moduleToggles: [], toggles: [], linkClicks: [] }, harness = (window as any).clusterHarness;
    const render = () => harness.renderScene(document.getElementById('scene'), state.project, state.project.entryCanvasId, { view: state.view, selections: state.selections, authoringMode: state.mode, readonly: state.readonly,
      onViewChange(view: any) { state.view = view; }, onSelections(ids: string[]) { state.selections = ids; render(); },
      onSnapLock(sourceId: string, targetId: string, locked: boolean, linkId?: string) { state.locks.push({ sourceId, targetId, locked, linkId }); state.project = harness.setSnapLock(state.project, sourceId, targetId, locked, linkId); render(); },
      onMoveSelection(ids: string[], dx: number, dy: number) { state.moves.push({ ids, dx, dy }); for (const id of harness.lockedClusterNodeIds(state.project, state.project.entryCanvasId, ids)) { const node = state.project.nodes.find((node: any) => node.id === id); node.x += dx; node.y += dy; } render(); },
      onModuleSelect(id: string) { state.moduleToggles.push(id); const chosen = new Set(state.selections); if (chosen.has(id)) chosen.delete(id); else { chosen.add(id); const pending = [id]; while (pending.length) { const parent = pending.pop(); for (const node of state.project.nodes) if (node.parentId === parent) { chosen.add(node.id); pending.push(node.id); } } } state.selections = [...chosen]; render(); },
      onToggle(id: string) { state.toggles.push(id); }, onLinkSelect(id: string) { state.linkClicks.push(id); }, onResize() { throw new Error('Module gestures must not resize'); },
    }); (window as any).clusterTest = { state, render }; render();
  }, project);
  return { a, b, c, child, project };
}
const readState = (page: Page) => page.evaluate(() => structuredClone((window as any).clusterTest.state));
const tile = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);

test('contact links have no redundant shafts; the edge lock stays screen-sized and unlocks in place', async ({ page }, info) => {
  const { a, b } = await mount(page);
  await expect(page.locator('.ms-link-line[data-link-id^="snap-"]')).toHaveCount(0);
  await page.mouse.move(290, 168);
  const lock = page.getByRole('button', { name: 'Lock nodes together' }); await expect(lock).toBeVisible();
  expect(await lock.evaluate(node => ({ width: node.getBoundingClientRect().width, height: node.getBoundingClientRect().height }))).toEqual({ width: 28, height: 28 });
  await lock.click(); expect((await readState(page)).project.links.find((link: any) => link.id === 'snap-ab').snap.locked).toBe(true);
  await page.mouse.move(290, 168); await expect(page.getByRole('button', { name: 'Unlock cluster edge' })).toBeVisible();
  await page.screenshot({ path: info.outputPath('contact-lock.png') });
  await page.getByRole('button', { name: 'Unlock cluster edge' }).click();
  expect((await readState(page)).locks.map((entry: any) => entry.locked)).toEqual([true, false]);
  await page.evaluate(() => { const test = (window as any).clusterTest; test.state.view.scale = .25; test.render(); });
  await page.mouse.move(87.5, 57); await expect(page.getByRole('button', { name: 'Lock nodes together' })).toBeVisible();
  expect(await page.locator('.ms-snap-lock').evaluate(node => node.getBoundingClientRect().width)).toBe(28);
  expect((await readState(page)).project.nodes.find((node: any) => node.id === a.id).x).toBe(70);
  expect((await readState(page)).project.nodes.find((node: any) => node.id === b.id).x).toBe(270);
});

test('a transitive locked cluster and descendant branch preview and commit as one rigid movement', async ({ page }) => {
  const { a, b, c, child } = await mount(page);
  await page.evaluate(() => { const test = (window as any).clusterTest; for (const link of test.state.project.links) if (link.snap) link.snap.locked = true; test.render(); });
  await page.mouse.move(170, 165); await page.mouse.down(); await page.mouse.move(220, 205, { steps: 5 });
  for (const node of [a, b, c, child]) expect(await tile(page, node.id).evaluate(node => (node as HTMLElement).style.transform)).toBe('translate(50px, 40px)');
  await page.mouse.up(); let state = await readState(page);
  expect(state.moves).toHaveLength(1); expect(new Set(state.moves[0].ids)).toEqual(new Set([a.id, b.id, c.id]));
  for (const node of [a, b, c, child]) expect(state.project.nodes.find((entry: any) => entry.id === node.id)).toMatchObject({ x: node.x + 50, y: node.y + 40 });
  await tile(page, b.id).focus(); await page.keyboard.press('ArrowRight'); state = await readState(page);
  for (const node of [a, b, c, child]) expect(state.project.nodes.find((entry: any) => entry.id === node.id)).toMatchObject({ x: node.x + 55, y: node.y + 40 });
  expect(state.project.links.filter((link: any) => link.snap).map((link: any) => link.snap.locked)).toEqual([true, true]);
});

test('cluster collisions constrain the whole branch while a bounded ancestor remains stationary', async ({ page }) => {
  const { a, b, c, child } = await mount(page);
  await page.evaluate(({ memberIds }) => {
    const test = (window as any).clusterTest, project = test.state.project;
    project.nodes.push({ id: 'workspace', canvasId: project.entryCanvasId, parentId: project.canvases[0].rootId, kind: 'container', title: 'Freeform workspace',
      x: 10, y: 50, width: 900, height: 450, sizeMode: 'fixed', containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free', collapsed: false, placement: 'free', style: { shape: 'rectangle' } });
    for (const id of memberIds) project.nodes.find((node: any) => node.id === id).parentId = 'workspace';
    project.nodes.push({ id: 'obstacle', canvasId: project.entryCanvasId, parentId: 'workspace', kind: 'media', title: 'Obstacle', x: 700, y: 100, width: 100, height: 110,
      sizeMode: 'fixed', contentType: 'text', text: 'Keep clear', childLayout: 'spatial', placement: 'manual', style: { shape: 'rectangle' } });
    for (const link of project.links) if (link.snap) link.snap.locked = true; test.render();
  }, { memberIds: [a.id, b.id, c.id] });
  await page.mouse.move(170, 165); await page.mouse.down(); await page.mouse.move(220, 165, { steps: 5 });
  for (const node of [a, b, c, child]) expect(await tile(page, node.id).evaluate(node => (node as HTMLElement).style.transform)).toBe('translate(30px, 0px)');
  expect(await tile(page, 'workspace').evaluate(node => (node as HTMLElement).style.transform)).toBe('');
  await page.mouse.up(); const state = await readState(page);
  expect(state.moves).toHaveLength(1); expect(state.moves[0]).toMatchObject({ dx: 30, dy: 0 });
  for (const node of [a, b, c, child]) expect(state.project.nodes.find((entry: any) => entry.id === node.id)).toMatchObject({ x: node.x + 30, y: node.y });
  expect(state.project.nodes.find((node: any) => node.id === 'workspace')).toMatchObject({ x: 10, y: 50 });
  await tile(page, b.id).focus(); await page.keyboard.press('ArrowRight'); expect((await readState(page)).moves).toHaveLength(1);
});

test('module clicks accumulate and toggle branches, pulse real outlines and select internal links without disclosing or dragging', async ({ page }, info) => {
  const { a, c, child, project } = await mount(page);
  await page.evaluate(() => { const test = (window as any).clusterTest; test.state.mode = 'module'; test.render(); });
  await tile(page, a.id).locator('.ms-node-title').click(); await tile(page, c.id).locator('.ms-node-title').click();
  expect(new Set((await readState(page)).selections)).toEqual(new Set([a.id, child.id, c.id]));
  await expect(tile(page, a.id)).toHaveClass(/ms-module-selected/); await expect(tile(page, child.id)).toHaveClass(/ms-module-selected/);
  await expect(page.locator('.ms-link[data-link-id="concept-ac"]')).toHaveClass(/ms-module-link-selected/);
  const arrow = (await page.locator('.ms-link-line[data-link-id="concept-ac"]').getAttribute('marker-end'))!.slice(5, -1);
  await expect(page.locator(`marker[id="${arrow}"] path`)).toHaveAttribute('fill', '#8EF1D6');
  expect(await tile(page, child.id).locator('.ms-node-outline path').evaluate(node => ({ d: node.getAttribute('d'), animation: getComputedStyle(node).animationName }))).toMatchObject({ animation: 'ms-module-selection-pulse' });
  await page.screenshot({ path: info.outputPath('module-selection.png') });
  await page.mouse.click(1100, 650); expect((await readState(page)).selections).toHaveLength(3);
  await tile(page, child.id).click(); expect(new Set((await readState(page)).selections)).toEqual(new Set([a.id, c.id]));
  await tile(page, a.id).dblclick(); expect((await readState(page)).toggles).toEqual([]);
  await page.mouse.move(170, 165); await page.mouse.down(); await page.mouse.move(220, 205, { steps: 5 }); await page.mouse.up();
  const state = await readState(page); expect(state.moves).toEqual([]); expect(state.project).toEqual(project); expect(state.view).toMatchObject({ x: 70, y: 60 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  expect(await tile(page, a.id).locator('.ms-node-outline path').evaluate(node => getComputedStyle(node).animationName)).toBe('none');
});

test('module outlines paint native, thick and curved outer edges without unclipping contents and stay screen-sized', async ({ page }, info) => {
  const { a, b, c, child } = await mount(page);
  await page.evaluate(ids => {
    const test = (window as any).clusterTest;
    test.state.project.nodes.find((node: any) => node.id === ids[0]).style = { borderWidth: 20, border: '#445588' };
    test.state.project.nodes.find((node: any) => node.id === ids[1]).style = undefined;
    test.state.project.nodes.find((node: any) => node.id === ids[2]).style = { shape: 'circle' };
    test.state.mode = 'module'; test.state.selections = ids; test.render();
  }, [a.id, b.id, c.id, child.id]);
  await expect(page.locator('.ms-module-outline')).toHaveCount(4);
  for (const node of [a, b, c]) {
    expect(await tile(page, node.id).evaluate(card => getComputedStyle(card).overflow)).toBe('hidden');
    const paint = page.locator(`.ms-module-outline[data-node-id="${node.id}"] path`);
    expect(await paint.getAttribute('d')).toBe(await tile(page, node.id).locator('.ms-node-outline path').getAttribute('d'));
    expect(await paint.evaluate(path => ({ stroke: getComputedStyle(path).stroke, width: getComputedStyle(path).strokeWidth, animation: getComputedStyle(path).animationName })))
      .toEqual({ stroke: 'rgb(142, 241, 214)', width: '2.5px', animation: 'ms-module-selection-pulse' });
  }
  await page.emulateMedia({ reducedMotion: 'reduce' });
  expect(await page.locator('.ms-module-outline path').first().evaluate(path => getComputedStyle(path).animationName)).toBe('none');
  // Inspect actual painted pixels at each outer top edge. Computed SVG styles
  // alone pass when the card clips the entire stroke, which was the regression.
  const regions = await Promise.all([a,b,c].map(node => tile(page, node.id).evaluate(card => { const box = card.getBoundingClientRect(); return { x: Math.round(box.x + box.width / 2), y: Math.round(box.y) }; })));
  const screenshot = await page.screenshot({ path: info.outputPath('module-outer-edge-outlines.png') });
  const counts = await page.evaluate(async ({ image, regions }) => {
    const bitmap = new Image(); bitmap.src = `data:image/png;base64,${image}`; await bitmap.decode();
    const canvas = document.createElement('canvas'); canvas.width = bitmap.width; canvas.height = bitmap.height; const context = canvas.getContext('2d')!; context.drawImage(bitmap, 0, 0);
    return regions.map(({ x,y }) => { const pixels = context.getImageData(x-7,y-2,15,5).data; let green = 0; for(let at=0;at<pixels.length;at+=4) if(pixels[at+1]>160 && pixels[at+1]>pixels[at]+40 && pixels[at+1]>pixels[at+2]+10) green++; return green; });
  }, { image: screenshot.toString('base64'), regions });
  for (const count of counts) expect(count).toBeGreaterThanOrEqual(25);
  const before = await page.locator(`.ms-module-outline[data-node-id="${a.id}"] path`).boundingBox();
  await page.mouse.move(1100,700); await page.mouse.down(); await page.mouse.move(1150,740,{steps:4}); await page.mouse.up();
  const after = await page.locator(`.ms-module-outline[data-node-id="${a.id}"] path`).boundingBox();
  expect(after!.x-before!.x).toBeCloseTo(50); expect(after!.y-before!.y).toBeCloseTo(40);
  await page.evaluate(() => { const test=(window as any).clusterTest; test.state.view.scale=.25; test.render(); });
  expect(await page.locator('.ms-module-outline path').first().evaluate(path => getComputedStyle(path).strokeWidth)).toBe('2.5px');
  expect(await page.locator(`.ms-module-outline[data-node-id="${a.id}"] path`).boundingBox()).toMatchObject({ width:52.5, height:30 }); // 50 × 27.5 plus the constant screen stroke.
  await page.locator('#scene').evaluate(scene => { scene.style.width='1000px'; });
  await expect.poll(()=>page.locator('.ms-module-selection-layer clipPath rect').first().getAttribute('width')).toBe('1000');
  await tile(page,b.id).locator('.ms-node-title').click(); await expect(page.locator(`.ms-module-outline[data-node-id="${b.id}"]`)).toHaveCount(0);
});

test('module outlines retain Window clipping and follow its scroll aperture', async ({ page }) => {
  const { a,b,c,child } = await mount(page);
  await page.evaluate(ids => {
    const test = (window as any).clusterTest, project=test.state.project, parent=project.nodes.find((node:any)=>node.id===ids[0]);
    Object.assign(parent,{kind:'container',x:70,y:100,width:220,height:150,containerVariant:'window',childLayout:'contained',collapsed:false,grid:{columns:1,rows:3,cellWidth:200,cellHeight:110,windowColumns:1,windowRows:1,scrollDirection:'vertical',gap:0,showHeaders:false}});
    project.nodes=project.nodes.filter((node:any)=>node.id!==ids[3]);
    [ids[1],ids[2]].forEach((id,index)=>Object.assign(project.nodes.find((node:any)=>node.id===id),{parentId:parent.id,cell:{column:1,row:index+1},style:undefined}));
    test.state.mode='module';test.state.selections=ids.slice(0,3);test.render();
  },[a.id,b.id,c.id,child.id]);
  const window=page.locator('.ms-window-viewport'); await expect(window).toHaveCount(1);
  const clipBounds=async()=>page.locator(`.ms-module-outline[data-node-id="${b.id}"]`).evaluate(group=>{
    const id=group.getAttribute('clip-path')!.slice(5,-1),rect=document.querySelector(`#${id} rect`)!;return ['x','y','width','height'].map(name=>Number(rect.getAttribute(name)));
  });
  const before=await clipBounds(); const viewport=await page.locator('.ms-viewport').boundingBox(), aperture=await window.boundingBox();
  expect(before).toEqual([aperture!.x-viewport!.x,aperture!.y-viewport!.y,aperture!.width,aperture!.height]);
  const paintedBefore=await page.locator(`.ms-module-outline[data-node-id="${b.id}"] path`).boundingBox();
  await window.evaluate(element=>{element.scrollTop=80;});
  await expect.poll(async()=>{const box=await page.locator(`.ms-module-outline[data-node-id="${b.id}"] path`).boundingBox();return paintedBefore!.y-box!.y;}).toBeCloseTo(80);
  expect(await clipBounds()).toEqual(before);
});

test('shape contact ignores invisible circle corners and readonly publications cannot lock edges', async ({ page }) => {
  const { a, b } = await mount(page);
  await page.evaluate(({ aId, bId }) => { const test = (window as any).clusterTest; test.state.project.nodes.find((node: any) => node.id === aId).style.shape = 'circle'; test.state.project.nodes.find((node: any) => node.id === bId).style.shape = 'circle'; test.render(); }, { aId: a.id, bId: b.id });
  await page.mouse.move(290, 168); await expect(page.locator('.ms-snap-lock')).toBeHidden();
  await page.evaluate(({ aId, bId }) => { const test = (window as any).clusterTest; for (const id of [aId, bId]) test.state.project.nodes.find((node: any) => node.id === id).style.shape = 'rectangle'; test.state.readonly = true; test.render(); }, { aId: a.id, bId: b.id });
  await page.mouse.move(290, 168); await expect(page.locator('.ms-snap-lock')).toBeHidden(); expect((await readState(page)).locks).toEqual([]);
});
