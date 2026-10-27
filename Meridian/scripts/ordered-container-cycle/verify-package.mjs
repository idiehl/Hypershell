import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, writeFile, readdir, lstat } from 'node:fs/promises';
import { chromium, expect } from '@playwright/test';
import { addCanvas, addNode, commitSnapshots, newProject } from '../../src/core/model.ts';
import { galleryItems } from '../../src/core/gallery.ts';
import { reflowContainedChildren } from '../../src/core/grid.ts';
import { validateProject } from '../../src/core/rules.ts';
import { buildPublication, readPublication } from '../../src/publication.ts';

// Every write is confined to one fresh fixture on the specifically owned copied-
// data host. Existing projects, the home envelope, recovery drafts and checkpoints
// are read-only. Their review screenshots use the shared offline renderer.
const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
const { origin, qaRoot, dataRoot, packageRoot, sourceRoot } = config;
const workspaceRoot = path.resolve(import.meta.dirname, '../..');
assert.equal(workspaceRoot.toLowerCase(), path.resolve('I:/dev/Meridian').toLowerCase());
assert.equal(new URL(origin).origin, 'http://127.0.0.1:5294');
assert.equal(path.resolve(packageRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/ordered-container-package-20261008').toLowerCase());
assert.equal(path.resolve(qaRoot).toLowerCase(), path.resolve(workspaceRoot, 'output/ordered-container-qa-20261008').toLowerCase());
assert.ok(path.resolve(dataRoot).toLowerCase().startsWith(path.resolve(workspaceRoot, '.test-data').toLowerCase() + path.sep));
assert.notEqual(path.resolve(dataRoot).toLowerCase(), path.resolve(sourceRoot).toLowerCase());
assert.ok(config.ownedHostId > 0, 'A specifically owned isolated host is required.');
await mkdir(qaRoot, { recursive: true });
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const checks = [], screenshots = [], browserErrors = [], remoteRequests = [], blockedWrites = [], fixtureWrites = [];
const record = message => { checks.push(message); process.stdout.write(message + '\n'); };

async function fileInventory(root, omitHostLock = false) {
  const absoluteRoot = path.resolve(root), rows = [];
  async function walk(directory) {
    for (const item of (await readdir(directory, { withFileTypes: true })).sort((a, b) => a.name.localeCompare(b.name))) {
      const filename = path.resolve(directory, item.name); assert.ok(filename.startsWith(absoluteRoot + path.sep));
      const info = await lstat(filename); assert.equal(info.isSymbolicLink(), false, `Inventory links are not permitted: ${filename}`);
      if (info.isDirectory()) await walk(filename);
      else if (info.isFile() && !(omitHostLock && item.name === '.host.lock')) rows.push({ path: filename, relativePath: path.relative(absoluteRoot, filename), sha256: digest(await readFile(filename)) });
    }
  }
  assert.equal((await lstat(absoluteRoot)).isSymbolicLink(), false); await walk(absoluteRoot);
  return rows.sort((a, b) => a.relativePath.localeCompare(b.relativePath));
}
const protectedSourceFiles = await fileInventory(sourceRoot, true), protectedCopiedFiles = await fileInventory(dataRoot, true);
assert.equal(protectedSourceFiles.length, config.copiedFiles);
assert.deepEqual(protectedCopiedFiles.map(({ relativePath, sha256 }) => ({ relativePath, sha256 })), protectedSourceFiles.map(({ relativePath, sha256 }) => ({ relativePath, sha256 })));
const fixtureId = randomUUID();
const fixtureRoute = route => route === `/api/projects/${fixtureId}` || route === `/api/projects/${fixtureId}/recovery`;
async function json(route, method = 'GET', data, allow404 = false) {
  assert.ok(route.startsWith('/api/') || route === '/__meridian_status');
  if (!['GET', 'HEAD'].includes(method)) {
    assert.ok(route === '/api/projects' && method === 'POST' && data?.id === fixtureId || fixtureRoute(route), 'Only the fresh fixture may be written.');
    fixtureWrites.push(`${method} ${route}`);
  }
  const url = new URL(route, origin); assert.equal(url.origin, 'http://127.0.0.1:5294');
  const response = await fetch(url, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
  if (allow404 && response.status === 404) return null;
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`);
  return response.status === 204 ? null : response.json();
}
const stored = () => json(`/api/projects/${fixtureId}`);
const tile = (page, id) => page.locator(`.ms-node[data-node-id="${id}"]`);
async function saved(page) { await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/); }
async function screenshot(page, name) { const filename = path.join(qaRoot, name); await page.screenshot({ path: filename }); screenshots.push(filename); }
async function panel(page, name) {
  if (!(await page.locator('.inspector').isVisible())) await page.getByRole('button', { name: 'Show side panel', exact: true }).click();
  await page.getByRole('combobox', { name: 'Side panel', exact: true }).selectOption(name);
}
async function selectNode(page, id) {
  await panel(page, 'canvas'); const search = page.getByRole('searchbox', { name: 'Search canvas nodes', exact: true });
  await search.fill(id); await page.locator(`button[data-jump-to-node="${id}"]`).click();
  if (await search.isVisible()) await search.fill('');
}
async function geometry(page, id) {
  return tile(page, id).evaluate(element => ({ x: parseFloat(element.style.left), y: parseFloat(element.style.top), width: parseFloat(element.style.width), height: parseFloat(element.style.height) }));
}
async function withinParent(page, parentId, childIds) {
  const frame = await geometry(page, parentId);
  for (const id of childIds) {
    const child = await geometry(page, id);
    assert.ok(child.x >= frame.x - 1 && child.y >= frame.y - 1 && child.x + child.width <= frame.x + frame.width + 1 && child.y + child.height <= frame.y + frame.height + 1, `${id} stays inside ${parentId}`);
  }
}
const nodeIn = (document, id) => document.nodes.find(node => node.id === id);
assert.equal((await json('/__meridian_status')).application, 'atlas-meridian');
const copiedCatalog = await json('/api/projects'), copiedProjects = [];
for (const entry of copiedCatalog) copiedProjects.push({ entry, document: await json(`/api/projects/${entry.id}`), recovery: await json(`/api/projects/${entry.id}/recovery`, 'GET', undefined, true) });
const copiedLibrary = await json('/api/library'), copiedLibraryRecovery = await json('/api/library/recovery', 'GET', undefined, true);
const zeusId = '005fe68a-bc30-4b11-af1d-4dadec799b1c', zeusProject = copiedProjects.find(row => nodeIn(row.document, zeusId));
assert.ok(zeusProject, 'Copied saved collection contains the requested Zeus node.');
const zeus = nodeIn(zeusProject.document, zeusId); assert.equal(zeus.containerVariant, 'gallery'); assert.equal(galleryItems(zeusProject.document, zeus).length, 5);
const protectedEnvelopes = { catalog: copiedCatalog, projects: copiedProjects, library: copiedLibrary, libraryRecovery: copiedLibraryRecovery };
await writeFile(path.join(qaRoot, 'copied-content-baselines.json'), JSON.stringify({ envelopes: protectedEnvelopes, sourceFiles: protectedSourceFiles, copiedFiles: protectedCopiedFiles }, null, 2));
record('All copied projects, recoveries, the home envelope, original file hashes, and the existing five-object Zeus Gallery are recorded without writing existing content.');

const viewerJS = await readFile(path.join(packageRoot, 'app/wwwroot/viewer.js'), 'utf8'), sceneCSS = await readFile(path.join(packageRoot, 'app/wwwroot/scene.css'), 'utf8');
const browser = await chromium.launch(), contexts = [];
async function context(options = {}) {
  const current = await browser.newContext({ viewport: { width: 1600, height: 1000 }, ...options }); contexts.push(current);
  current.setDefaultTimeout(15000); current.setDefaultNavigationTimeout(18000);
  current.on('page', page => page.on('pageerror', error => browserErrors.push(error.message)));
  await current.route('**/*', async route => {
    const request = route.request(), url = request.url();
    if (/^https?:/i.test(url) && new URL(url).origin !== origin) { remoteRequests.push(url); await route.abort(); }
    else if (/^https?:/i.test(url) && !['GET', 'HEAD'].includes(request.method())) {
      if (!fixtureRoute(new URL(url).pathname)) { blockedWrites.push(`${request.method()} ${url}`); await route.abort(); }
      else { fixtureWrites.push(`${request.method()} ${new URL(url).pathname}`); await route.continue(); }
    } else await route.continue();
  }); return current;
}
const editorContext = await context(), page = await editorContext.newPage();
let fixture, finalProject, parent, first, second, hidden, manual, gallery, launcher, image, galleryText, mainCanvas, galleryCanvas, windowCanvas, windowParent, windowChildren, squareParent, squareChildren, rasterBytes;
const measured = {};
async function cleanGallery(page, id) {
  await expect(tile(page,id).locator('> .ms-node-header')).toHaveCSS('box-shadow','none');
  await expect(tile(page,id).locator('.ms-cell-address')).toHaveCount(0);
  await expect(tile(page,id).getByRole('button',{name:/Inspect current object/})).toHaveCount(0);
  const bottom = await tile(page,id).evaluate(element=>{const frame=element.getBoundingClientRect(), navigation=element.querySelector('.ms-gallery-navigation').getBoundingClientRect(),scale=frame.width/parseFloat(element.style.width);return(frame.bottom-navigation.bottom)/scale;});
  assert.ok(bottom>=4&&bottom<=8,`Gallery navigation fits the 5px bottom inset: ${bottom}`);
  return bottom;
}
const windowView = (page,id)=>page.locator(`.ms-window-viewport[data-window-id="${id}"]`);
async function windowDimensions(page,id) { return windowView(page,id).evaluate(element=>({width:element.clientWidth,height:element.clientHeight,fullWidth:element.scrollWidth,fullHeight:element.scrollHeight})); }
try {
  // Original saved content uses an offline read-only viewer, so disclosure cannot
  // run editor migrations, create recovery drafts or automatically save a layout.
  const sourceContext = await context({ offline: true }), sourceView = await sourceContext.newPage();
  const homePresentation = structuredClone(copiedLibrary.project), homeGroup = nodeIn(homePresentation, 'a3d00829-1577-4dec-8591-24495b6c885d');
  assert.ok(homeGroup); homeGroup.collapsed = true;
  const homePath = path.join(qaRoot, 'copied-library-default-collapsed.html');
  await writeFile(homePath, buildPublication(homePresentation, viewerJS, sceneCSS)); await sourceView.goto(pathToFileURL(homePath).href);
  await expect(tile(sourceView, homeGroup.id)).toHaveCount(1); assert.ok((await geometry(sourceView, homeGroup.id)).height < 180);
  await screenshot(sourceView, 'package-copied-library-default-collapsed.png');
  const zeusPresentation = structuredClone(zeusProject.document); zeusPresentation.entryCanvasId = zeus.canvasId;
  const zeusPath = path.join(qaRoot, 'copied-zeus-gallery-readonly.html');
  await writeFile(zeusPath, buildPublication(zeusPresentation, viewerJS, sceneCSS)); await sourceView.goto(pathToFileURL(zeusPath).href);
  await expect(sourceView.locator(`.ms-gallery[data-gallery-id="${zeusId}"] .ms-gallery-position`)).toHaveText('1 of 5');
  await screenshot(sourceView, 'package-copied-zeus-gallery-readonly.png');
  const sourcePayload = await sourceView.locator('#meridian-data').textContent();
  await tile(sourceView, zeusId).press('Enter'); await sourceView.getByRole('button', { name: 'Fit selected branch', exact: true }).click();
  await expect.poll(() => sourceView.locator(`.ms-gallery[data-gallery-id="${zeusId}"] img`).evaluate(image => image.naturalWidth)).toBeGreaterThan(0);
  assert.equal(await sourceView.locator('#meridian-data').textContent(), sourcePayload);
  await sourceView.getByRole('button', { name: 'Close project navigation', exact: true }).click();
  measured.copiedZeusBottomInset = await cleanGallery(sourceView,zeusId);
  const zeusView=sourceView.locator(`.ms-gallery[data-gallery-id="${zeusId}"]`),zeusNext=zeusView.getByRole('button',{name:`Next object in ${zeus.title}`,exact:true});
  await zeusNext.click();await zeusNext.click();await expect(zeusView.locator('.ms-gallery-position')).toHaveText('3 of 5');
  await expect.poll(()=>zeusView.locator('img').evaluate(image=>image.naturalWidth)).toBeGreaterThan(0);
  measured.copiedZeusThirdBottomInset=await cleanGallery(sourceView,zeusId);
  assert.equal(await sourceView.locator('#meridian-data').textContent(),sourcePayload);
  await screenshot(sourceView, 'package-copied-zeus-gallery-readonly-detail.png');
  record('Read-only offline screenshots confirm a compact collapsed home node and the unchanged five-object Zeus Gallery using packaged renderer bytes. No existing editor document was expanded or edited.');

  await page.goto(`${origin}/__meridian_status`);
  const rasterBase64 = await page.evaluate(() => {
    const canvas = document.createElement('canvas'); canvas.width = 320; canvas.height = 180;
    const drawing = canvas.getContext('2d'); drawing.fillStyle = '#20494d'; drawing.fillRect(0, 0, 320, 180);
    drawing.fillStyle = '#8cd9b9'; drawing.fillRect(30, 24, 260, 132); drawing.fillStyle = '#d9bb7b'; drawing.fillRect(58, 47, 68, 84);
    return canvas.toDataURL('image/png').split(',')[1];
  });
  rasterBytes = Buffer.from(rasterBase64, 'base64'); await writeFile(path.join(qaRoot, 'container-review-image.png'), rasterBytes);
  fixture = newProject('Container disclosure · isolated package review'); fixture.id = fixtureId;
  mainCanvas = fixture.canvases[0]; mainCanvas.title = 'Container behavior';
  parent = addNode(fixture, mainCanvas.id, 'container', 'My canvases');
  Object.assign(parent, { x: 100, y: 100, width: 1074, height: 892, collapsed: true, containerVariant: 'spatial', childLayout: 'spatial', grid: { columns: 3, rows: 3, cellWidth: 240, cellHeight: 180, gap: 12, showHeaders: true }, style: { fill: '#1c302d', border: '#426258', textColor: '#dae9e4', titleSize: 18, fontSize: 13 } });
  delete parent.sizeMode; // A genuine legacy structural tile with oversized stored dimensions.
  first = addNode(fixture, mainCanvas.id, 'text', 'First collection');
  Object.assign(first, { parentId: parent.id, x: -360, y: 300, collapsed: true, cell: { column: 1, row: 1, name: 'first' }, text: 'The supporting branch remains independently collapsed.' });
  second = addNode(fixture, mainCanvas.id, 'text', 'Second collection');
  Object.assign(second, { parentId: parent.id, x: 1400, y: 340, cell: { column: 2, row: 1, name: 'second' }, text: 'Separate content keeps its identity and named address.' });
  hidden = addNode(fixture, mainCanvas.id, 'text', 'Hidden supporting detail');
  Object.assign(hidden, { parentId: first.id, x: -320, y: 500, text: 'Only the first collection controls this disclosure.' });
  fixture.links.push({ id: randomUUID(), canvasId: mainCanvas.id, parentId: parent.id, sourceId: parent.id, targetId: first.id, kind: 'visual', relation: 'generic', direction: 'none' });
  manual = addNode(fixture, mainCanvas.id, 'text', 'Automatic and fixed dimensions');
  Object.assign(manual, { x: 1800, y: 100, width: 300, height: 40, text: 'The entire passage and title must fit inside the visible surface. '.repeat(5), style: { fontSize: 22, titleSize: 30, fill: '#263346', textColor: '#edf4fb' } });
  galleryCanvas = addCanvas(fixture, 'One object at a time');
  const destination = addCanvas(fixture, 'Launcher destination');
  gallery = addNode(fixture, galleryCanvas.id, 'container', 'A gallery of relevant objects');
  Object.assign(gallery, { x: 100, y: 100, width: 1074, height: 892, collapsed: false, containerVariant: 'gallery', childLayout: 'contained', style: { fill: '#153a37', border: '#398c7d', textColor: '#d2f3e9', titleSize: 20, fontSize: 14 } });
  launcher = addNode(fixture, galleryCanvas.id, 'canvas-link', 'A canvas'); Object.assign(launcher, { parentId: gallery.id, targetId: destination.rootId });
  image = addNode(fixture, galleryCanvas.id, 'image', 'A landscape image');
  Object.assign(image, { parentId: gallery.id, assetId: 'container-review-image', width: 350, text: 'The image, caption and navigation fit together.' });
  galleryText = addNode(fixture, galleryCanvas.id, 'text', 'An explanatory entry');
  Object.assign(galleryText, { parentId: gallery.id, width: 300, text: 'Text entries share the collection without inheriting an empty image-sized frame. '.repeat(3) });
  fixture.assets.push({ id: 'container-review-image', name: 'container-review-image.png', mime: 'image/png', base64: rasterBase64 });
  windowCanvas=addCanvas(fixture,'Ordered window and square grid');
  windowParent=addNode(fixture,windowCanvas.id,'container','A scrolling collection');
  Object.assign(windowParent,{x:100,y:100,collapsed:false,containerVariant:'window',childLayout:'contained',text:'Browse the full collection through a small window.',grid:{columns:6,rows:8,cellWidth:120,cellHeight:80,fillOrder:'row',windowColumns:3,windowRows:2,scrollDirection:'vertical'}});
  windowChildren=Array.from({length:48},(_,at)=>{const child=addNode(fixture,windowCanvas.id,'text',`Window object ${at+1}`);Object.assign(child,{parentId:windowParent.id,text:`Content ${at+1}`,cell:{column:at%6+1,row:Math.floor(at/6)+1,name:`window_object_${at+1}`},style:{shape:at?'diamond':'rounded'}});return child;});
  squareParent=addNode(fixture,windowCanvas.id,'container','Ten objects in a square');
  Object.assign(squareParent,{x:1100,y:100,collapsed:false,containerVariant:'grid',childLayout:'contained',grid:{columns:1,rows:1,cellWidth:120,cellHeight:80,fillOrder:'column'}});
  squareChildren=Array.from({length:10},(_,at)=>{const child=addNode(fixture,windowCanvas.id,'text',`Square object ${at+1}`);Object.assign(child,{parentId:squareParent.id,text:`Entry ${at+1}`,style:{shape:at?'circle':'hexagon'}});return child;});
  fixture=reflowContainedChildren(fixture,windowParent.id);fixture=reflowContainedChildren(fixture,squareParent.id);
  assert.deepEqual(validateProject(fixture), []);
  const created = await json('/api/projects', 'POST', commitSnapshots(fixture)); assert.equal(created.id, fixtureId);
  const originalIds = created.nodes.map(node => node.id).sort();
  await page.goto(`${origin}/?project=${fixtureId}&canvas=${mainCanvas.id}`); await expect(page.locator('.workspace')).toBeVisible(); await saved(page);
  measured.compactLegacy = await geometry(page, parent.id); assert.ok(measured.compactLegacy.height < 180 && measured.compactLegacy.width < 400);
  await expect(tile(page, first.id)).toHaveCount(0); await expect(tile(page, second.id)).toHaveCount(0);
  await selectNode(page, parent.id); await panel(page, 'relations');
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  await expect(tile(page, first.id)).toHaveCount(1); await expect(tile(page, second.id)).toHaveCount(1);
  assert.ok((await geometry(page, parent.id)).height >= parent.height); await withinParent(page,parent.id,[first.id,second.id]);await expect(tile(page, hidden.id)).toHaveCount(0);
  await saved(page); await screenshot(page, 'package-legacy-enclosed-working-space.png');
  for (const variant of ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'window', 'gallery', 'contained']) {
    const variantBaseline=nodeIn(await stored(),parent.id);
    await panel(page, 'properties'); await page.getByRole('combobox', { name: 'Sub-type', exact: true }).selectOption(variant);
    await expect.poll(async () => nodeIn(await stored(), parent.id).containerVariant).toBe(variant); await saved(page);
    if (variant === 'gallery') {
      await expect(page.locator(`.ms-gallery[data-gallery-id="${parent.id}"] .ms-gallery-item`)).toHaveCount(1);
      await expect(tile(page, first.id)).toHaveCount(0); await expect(tile(page, second.id)).toHaveCount(0);
      assert.ok((await geometry(page, parent.id)).height < 350);
    } else {
      await expect(tile(page, first.id)).toHaveCount(1); await expect(tile(page, second.id)).toHaveCount(1);
      if(variant!=='window')await withinParent(page, parent.id, [first.id, second.id]);
      if(variant==='contained')assert.ok((await geometry(page,parent.id)).width>=variantBaseline.width,'Enclosed retains its current authored working width.');
      if(['horizontal','vertical','grid','spreadsheet','window'].includes(variant)) {
        const a=await geometry(page,first.id),b=await geometry(page,second.id);assert.equal(a.width,b.width);assert.equal(a.height,b.height);
        await expect(tile(page,first.id)).toHaveAttribute('data-shape',await tile(page,second.id).getAttribute('data-shape'));
        if(variant==='horizontal'){assert.equal(a.y,b.y);assert.ok(Math.abs(a.x+a.width-b.x)<.01);}
        if(variant==='vertical'){assert.equal(a.x,b.x);assert.ok(Math.abs(a.y+a.height-b.y)<.01);}
        if(variant==='grid'){const g=nodeIn(await stored(),parent.id).grid;assert.equal(g.columns,2);assert.equal(g.rows,2);}
      }
      if (variant === 'grid') await expect(tile(page, parent.id).locator('.ms-empty-cell')).toHaveCount(0);
      if (variant === 'spreadsheet') {
        await panel(page, 'relations'); const rows = page.getByLabel('Rows', { exact: true }); await rows.fill('3'); await rows.press('Tab');
        await expect.poll(async () => nodeIn(await stored(), parent.id).grid.rows).toBe(3); await saved(page);
        assert.ok(await tile(page, parent.id).locator('.ms-empty-cell').count() > 0);
      }
    }
    await expect(tile(page, hidden.id)).toHaveCount(0);
    const current = await stored(); assert.deepEqual(current.nodes.map(node => node.id).sort(), originalIds);
    assert.equal(nodeIn(current, first.id).cell.name,first.cell.name);assert.equal(nodeIn(current,second.id).cell.name,second.cell.name);
    assert.equal(nodeIn(current, first.id).collapsed, true);
    measured[`variant-${variant}`] = await geometry(page, parent.id);
    await expect(page.locator('.ms-cell-address')).toHaveCount(0);await screenshot(page, `package-layout-${variant}.png`);
  }
  await panel(page, 'relations'); await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  await expect(tile(page, first.id)).toHaveCount(0); assert.ok((await geometry(page, parent.id)).height < 180);
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  await withinParent(page, parent.id, [first.id, second.id]); await saved(page);
  record('A legacy 1074 × 892 parent migrates to roomy Enclosed, collapses to own content and converts through all seven current variants. GUIDs, named aliases and independent collapse states persist; Horizontal/Vertical are flush equal-size tiles, Grid uses square capacity and Spreadsheet preserves explicit capacity.');

  await selectNode(page, manual.id); await panel(page, 'appearance');
  await expect(page.getByRole('combobox', { name: 'Sizing', exact: true })).toHaveValue('auto');
  assert.ok((await geometry(page, manual.id)).height > 200);
  for (const [label, value] of [['Width', '640'], ['Height', '500']]) {
    const input = page.locator('.inspector').getByLabel(label, { exact: true }); await input.fill(value); await input.press('Tab');
  }
  await expect(page.getByRole('combobox', { name: 'Sizing', exact: true })).toHaveValue('fixed');
  await expect.poll(async () => { const current = nodeIn(await stored(), manual.id); return { width: current.width, height: current.height, mode: current.sizeMode }; }).toEqual({ width: 640, height: 500, mode: 'fixed' });
  await saved(page); measured.fixedBeforeReload = await geometry(page, manual.id); await screenshot(page, 'package-fixed-dimensions.png');
  await page.reload(); await expect(tile(page, manual.id)).toHaveCount(1); assert.deepEqual(await geometry(page, manual.id), measured.fixedBeforeReload);
  record('Appearance width/height edits explicitly switch Automatic to Fixed, save 640 × 500 dimensions, and reopen with identical world-space geometry.');

  await page.goto(`${origin}/?project=${fixtureId}&canvas=${windowCanvas.id}`);await saved(page);
  const squareStored=nodeIn(await stored(),squareParent.id);assert.equal(squareStored.grid.columns,4);assert.equal(squareStored.grid.rows,4);
  const squareEntries=(await stored()).nodes.filter(node=>node.parentId===squareParent.id);
  assert.deepEqual(squareEntries.slice(0,5).map(node=>node.cell),[{column:1,row:1},{column:1,row:2},{column:1,row:3},{column:1,row:4},{column:2,row:1}]);
  for(const child of squareEntries){assert.equal(child.width,squareStored.grid.cellWidth);assert.equal(child.height,squareStored.grid.cellHeight);await expect(tile(page,child.id)).toHaveAttribute('data-shape','hexagon');}
  await selectNode(page,windowParent.id);await panel(page,'relations');
  await page.getByRole('combobox',{name:'Scroll direction',exact:true}).selectOption('horizontal');
  await expect.poll(async()=>nodeIn(await stored(),windowParent.id).grid.scrollDirection).toBe('horizontal');await saved(page);
  const viewport=windowView(page,windowParent.id);await expect(viewport).toHaveCSS('overflow-x','auto');await expect(viewport).toHaveCSS('overflow-y','auto');
  measured.windowFrame=await geometry(page,windowParent.id);measured.windowSurface=await windowDimensions(page,windowParent.id);
  assert.deepEqual(measured.windowSurface,{width:360,height:160,fullWidth:720,fullHeight:640});
  const windowStored=await stored(),windowRecovery=await json(`/api/projects/${fixtureId}/recovery`,'GET',undefined,true),windowWrites=fixtureWrites.length,windowCamera=await page.locator('.ms-world').evaluate(element=>element.style.transform);
  await viewport.hover();await page.mouse.wheel(0,130);await expect.poll(()=>viewport.evaluate(element=>element.scrollLeft)).toBeGreaterThan(0);
  await viewport.focus();await viewport.press('ArrowDown');await expect.poll(()=>viewport.evaluate(element=>element.scrollTop)).toBe(80);
  assert.equal(await page.locator('.ms-world').evaluate(element=>element.style.transform),windowCamera);
  await screenshot(page,'package-window-horizontal-and-vertical-scroll.png');await page.waitForTimeout(2200);
  assert.equal(fixtureWrites.length,windowWrites);assert.deepEqual(await stored(),windowStored);assert.deepEqual(await json(`/api/projects/${fixtureId}/recovery`,'GET',undefined,true),windowRecovery);
  await page.reload();await expect(viewport).toHaveCount(1);await saved(page);assert.deepEqual(await geometry(page,windowParent.id),measured.windowFrame);assert.deepEqual(await windowDimensions(page,windowParent.id),measured.windowSurface);
  assert.deepEqual(nodeIn(await stored(),windowParent.id).grid,nodeIn(windowStored,windowParent.id).grid);
  await expect(page.locator('.ms-cell-address')).toHaveCount(0);await screenshot(page,'package-window-and-square-grid-reopened.png');
  record('Ten objects use a 4 × 4 Grid with column-fill coordinates and identical hexagon tiles. A saved 6 × 8 Window exposes a 3 × 2 viewport of 120 × 80px cells, scrolls both axes without camera movement, persistence/recovery requests or document changes, and reopens with identical geometry.');

  await page.goto(`${origin}/?project=${fixtureId}&canvas=${galleryCanvas.id}`); await saved(page);
  const galleryView = page.locator(`.ms-gallery[data-gallery-id="${gallery.id}"]`);
  await expect(galleryView.locator('.ms-gallery-item')).toHaveAttribute('data-gallery-item-id', launcher.id);
  const gallerySavedBefore = await stored(), writesBeforePaging = fixtureWrites.length;
  const camera = await page.locator('.ms-world').evaluate(element => element.style.transform);
  measured.galleryLauncher = await geometry(page, gallery.id); assert.ok(measured.galleryLauncher.height < 240);
  measured.galleryLauncherBottomInset=await cleanGallery(page,gallery.id);
  await screenshot(page, 'package-gallery-canvas-launcher.png');
  await galleryView.getByRole('button', { name: `Next object in ${gallery.title}`, exact: true }).click();
  await expect(galleryView.locator('img')).toHaveJSProperty('naturalWidth', 320);
  measured.galleryImage = await geometry(page, gallery.id);
  measured.galleryImageBottomInset=await cleanGallery(page,gallery.id);
  assert.ok(measured.galleryImage.height > measured.galleryLauncher.height + 60 && measured.galleryImage.height < 500);
  assert.equal(await page.locator('.ms-world').evaluate(element => element.style.transform), camera);
  await expect(tile(page, image.id)).toHaveCount(0); await screenshot(page, 'package-gallery-image.png');
  await galleryView.getByRole('button', { name: `Next object in ${gallery.title}`, exact: true }).click();
  await expect(galleryView.locator('.ms-gallery-item')).toHaveAttribute('data-gallery-item-id', galleryText.id);
  measured.galleryText = await geometry(page, gallery.id); assert.ok(measured.galleryText.height < 500);
  measured.galleryTextBottomInset=await cleanGallery(page,gallery.id);
  assert.equal(await page.locator('.ms-world').evaluate(element => element.style.transform), camera);
  await screenshot(page, 'package-gallery-text.png');
  await galleryView.getByRole('button', { name: `Previous object in ${gallery.title}`, exact: true }).click();
  await galleryView.getByRole('button', { name: `Previous object in ${gallery.title}`, exact: true }).click();
  assert.deepEqual(await geometry(page, gallery.id), measured.galleryLauncher);
  // Allow both bounded autosave and recovery timers to expose any accidental edit.
  await page.waitForTimeout(2200); assert.equal(fixtureWrites.length, writesBeforePaging);
  assert.deepEqual(await stored(), gallerySavedBefore);
  finalProject = await stored(); assert.equal(finalProject.assets.find(asset => asset.id === image.assetId).base64, rasterBase64);
  record('Gallery fits a launcher, image with caption, and text entry one at a time. Paging changes neither the camera transform, saved document/revision, recovery data, nor persistence requests; embedded objects stay off the canvas.');

  const offlineProject = structuredClone(finalProject); offlineProject.entryCanvasId = galleryCanvas.id;
  const html = buildPublication(offlineProject, viewerJS, sceneCSS), offlinePath = path.join(qaRoot, 'package-container-sizing-offline.html'); await writeFile(offlinePath, html);
  const imported = readPublication(html), importedGallery = imported.nodes.find(node => node.title === gallery.title);
  assert.equal(importedGallery.containerVariant, 'gallery'); assert.equal(imported.nodes.find(node => node.title === manual.title).sizeMode, 'fixed');
  assert.equal(imported.assets.find(asset => asset.name === 'container-review-image.png').base64, rasterBase64);
  const offlineContext = await context({ offline: true }), offline = await offlineContext.newPage(); await offline.goto(pathToFileURL(offlinePath).href);
  const offlineGallery = offline.locator(`.ms-gallery[data-gallery-id="${gallery.id}"]`), payload = await offline.locator('#meridian-data').textContent();
  assert.deepEqual(await geometry(offline, gallery.id), measured.galleryLauncher);
  await offlineGallery.getByRole('button', { name: `Next object in ${gallery.title}`, exact: true }).click();
  await expect(offlineGallery.locator('img')).toHaveJSProperty('naturalWidth', 320); assert.deepEqual(await geometry(offline, gallery.id), measured.galleryImage);
  await screenshot(offline, 'package-gallery-offline-image.png');
  await offlineGallery.getByRole('button', { name: `Next object in ${gallery.title}`, exact: true }).click();
  assert.deepEqual(await geometry(offline, gallery.id), measured.galleryText); assert.equal(await offline.locator('#meridian-data').textContent(), payload);
  record('Offline HTML matches packaged editor Gallery geometry for launcher, image and text objects exactly, preserves media bytes and fixed sizing on import, and keeps its authored payload immutable.');

  const offlineWindowProject=structuredClone(finalProject);offlineWindowProject.entryCanvasId=windowCanvas.id;
  const offlineWindowPath=path.join(qaRoot,'package-ordered-window-offline.html'),windowHTML=buildPublication(offlineWindowProject,viewerJS,sceneCSS);await writeFile(offlineWindowPath,windowHTML);
  const importedWindow=readPublication(windowHTML).nodes.find(node=>node.title===windowParent.title);assert.deepEqual(importedWindow.grid,nodeIn(finalProject,windowParent.id).grid);
  await offline.goto(pathToFileURL(offlineWindowPath).href);const offlineViewport=windowView(offline,windowParent.id),windowPayload=await offline.locator('#meridian-data').textContent(),offlineCamera=await offline.locator('.ms-world').evaluate(element=>element.style.transform);
  assert.deepEqual(await geometry(offline,windowParent.id),measured.windowFrame);assert.deepEqual(await windowDimensions(offline,windowParent.id),measured.windowSurface);
  await offlineViewport.hover();await offline.mouse.wheel(0,130);await expect.poll(()=>offlineViewport.evaluate(element=>element.scrollLeft)).toBeGreaterThan(0);
  await offlineViewport.focus();await offlineViewport.press('ArrowDown');await expect.poll(()=>offlineViewport.evaluate(element=>element.scrollTop)).toBe(80);
  assert.equal(await offline.locator('.ms-world').evaluate(element=>element.style.transform),offlineCamera);assert.equal(await offline.locator('#meridian-data').textContent(),windowPayload);
  await expect(offline.locator('.ms-cell-address')).toHaveCount(0);await screenshot(offline,'package-window-offline-scrolling.png');
  record('Offline Window matches editor frame and viewport geometry exactly, preserves full-grid/viewport metadata through import, scrolls horizontally and vertically without camera movement, and leaves its authored payload immutable.');

  for (const copied of copiedProjects) {
    assert.deepEqual(await json(`/api/projects/${copied.entry.id}`), copied.document, 'An existing copied project changed.');
    assert.deepEqual(await json(`/api/projects/${copied.entry.id}/recovery`, 'GET', undefined, true), copied.recovery, 'An existing copied recovery changed.');
  }
  assert.deepEqual(await json('/api/library'), copiedLibrary); assert.deepEqual(await json('/api/library/recovery', 'GET', undefined, true), copiedLibraryRecovery);
  const copiedAfter = await fileInventory(dataRoot, true), previousNames = new Set(protectedCopiedFiles.map(file => file.relativePath));
  for (const file of protectedCopiedFiles) assert.equal(copiedAfter.find(current => current.relativePath === file.relativePath)?.sha256, file.sha256, `A protected copied file changed: ${file.relativePath}`);
  const fixturePrefix = path.join('projects', fixtureId) + path.sep;
  for (const file of copiedAfter.filter(current => !previousNames.has(current.relativePath))) assert.ok(file.relativePath.startsWith(fixturePrefix), `An unexpected copied-data file appeared: ${file.relativePath}`);
  assert.deepEqual(await fileInventory(sourceRoot, true), protectedSourceFiles, 'Live saved bytes changed.');
  assert.deepEqual(browserErrors, []); assert.deepEqual(remoteRequests, []); assert.deepEqual(blockedWrites, []);
  const packageHashes = (await fileInventory(path.join(packageRoot, 'app'))).map(({ path, sha256 }) => ({ path, sha256 }));
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify({ passed: true, origin, isolatedDataRoot: dataRoot, copiedSourceFiles: config.copiedFiles, fixtureProjectId: fixtureId, fixtureCanvasId: mainCanvas.id,
    sourceCollection: { id: zeusProject.document.id, revision: zeusProject.document.revision, nodeId: zeusId, galleryObjects: 5 }, savedDataUnchanged: true, copiedDataUnchanged: true,
    protectedSourceFiles, protectedCopiedFiles, protectedEnvelopeSha256: digest(JSON.stringify(protectedEnvelopes)), rasterSha256: digest(rasterBytes), measured, checks, screenshots, packageHashes, browserErrors, remoteRequests, blockedWrites, fixtureWrites }, null, 2));
  process.stdout.write(JSON.stringify({ checks: checks.length, screenshots, packageFiles: packageHashes.length, browserErrors, remoteRequests, blockedWrites }, null, 2) + '\n');
} catch (error) {
  if (!page.isClosed()) await screenshot(page, 'package-failure.png').catch(() => {});
  const failure = { passed: false, message: error.message, stack: error.stack, checks, screenshots, measured, browserErrors, remoteRequests, blockedWrites, fixtureWrites };
  await writeFile(path.join(qaRoot, 'package-review.json'), JSON.stringify(failure, null, 2)); await writeFile(path.join(qaRoot, 'package-failure.json'), JSON.stringify(failure, null, 2)); throw error;
} finally { for (const current of contexts.reverse()) await current.close(); await browser.close(); }
