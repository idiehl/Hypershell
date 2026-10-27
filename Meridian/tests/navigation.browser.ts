import { workspaceAction } from './ui-helpers.ts';
import { test, expect } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { buildPublication, readPublication } from '../src/publication.ts';

function fixture() {
  const project = newProject('Navigation and density QA');
  const home = project.entryCanvasId;
  const branch = addNode(project, home, 'container', 'A compact branch');
  Object.assign(branch, { x: 60, y: 80, width: 1200, height: 900, sizeMode: 'fixed', text: 'One short summary remains visible while the branch is closed.', collapsed: true });
  const child = addNode(project, home, 'text', 'Hidden detail'); Object.assign(child, { parentId: branch.id, x: 100, y: 300, text: 'A deeper explanation.' });
  const image = addNode(project, home, 'image', 'Parent image'); Object.assign(image, { x: 580, y: 90, width: 260, height: 270, sizeMode: 'fixed', collapsed: true, assetId: 'image-asset' });
  project.assets.push({ id: 'image-asset', name: 'One pixel.png', mime: 'image/png', base64: 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==' });
  const imageChild = addNode(project, home, 'text', 'Image child'); Object.assign(imageChild, { parentId: image.id, x: 600, y: 400 });
  const output = addNode(project, home, 'output', 'Sibling output'); Object.assign(output, { x: 550, y: 420 });
  project.links.push({ id: 'proxy-link', canvasId: home, sourceId: child.id, targetId: output.id, kind: 'visual', relation: 'causal', label: 'explains' });
  const library = addCanvas(project, 'Source library');
  const books = addNode(project, library.id, 'container', 'Closed source shelf'); Object.assign(books, { x: 40, y: 70, width: 950, height: 750, text: 'Open this branch to read the entire source text.', collapsed: true });
  const book = addNode(project, library.id, 'document', 'Complete source'); Object.assign(book, { parentId: books.id, x: 130, y: 300, width: 400, height: 330, assetId: 'full-text' });
  const text = 'A preserved source line.\n'.repeat(210) + '\nAFTER_PREVIEW_BOUNDARY <script>window.shouldNeverRun=true</script>\nThe final passage is available in full.';
  project.assets.push({ id: 'full-text', name: 'Complete source.txt', mime: 'text/plain', base64: Buffer.from(text).toString('base64') });
  const reference = addNode(project, home, 'reference', 'Source elsewhere'); Object.assign(reference, { x: 70, y: 430, width: 340, height: 210, targetId: book.id });
  const toLibrary=addNode(project,home,'canvas-link','Open the source library');Object.assign(toLibrary,{x:920,y:70,width:270,height:130,targetId:library.rootId});
  const toWorkspace=addNode(project,library.id,'canvas-link','Return to the workspace');Object.assign(toWorkspace,{x:420,y:70,width:270,height:130,targetId:project.canvases[0]!.rootId});
  return { project: commitSnapshots(project), home, branch, child, image, imageChild, books, book, reference, library, text };
}

test('editor opens complete Markdown by MIME or extension as inert text', async ({ page, request }, info) => {
  const project = newProject('Markdown attachment QA');
  const text = '# Literal heading\n\n[Literal link](https://example.invalid/)\n' + 'A preserved Markdown line.\n'.repeat(210) + '\nAFTER_MARKDOWN_PREVIEW <script>window.markdownExecuted=true</script>\n<img src=x onerror="window.markdownExecuted=true">\nThe complete ending.';
  const assets = [
    { id: 'markdown-mime', name: 'Source.data', mime: 'text/markdown' },
    { id: 'markdown-extension', name: 'Journal.MD', mime: 'application/octet-stream' },
    { id: 'plain-text-regression', name: 'Plain.txt', mime: 'text/plain' },
  ];
  const nodes = assets.map((asset, index) => {
    project.assets.push({ ...asset, base64: Buffer.from(text).toString('base64') });
    const node = addNode(project, project.entryCanvasId, 'document', asset.name);
    Object.assign(node, { x: 60 + index * 340, y: 70, width: 300, height: 280, assetId: asset.id });
    return node;
  });
  await request.post('/api/projects', { data: commitSnapshots(project) }); await page.goto(`/?project=${project.id}`);
  for (const [index, asset] of assets.entries()) {
    await page.locator(`.ms-node[data-node-id="${nodes[index]!.id}"]`).getByRole('button', { name: `Open ${asset.name}`, exact: true }).click();
    const dialog = page.getByRole('dialog', { name: asset.name, exact: true });
    await expect(dialog.locator('.document-text')).toBeVisible();
    expect(await dialog.locator('.document-text').textContent()).toBe(text);
    await expect(dialog.locator('.document-text script, .document-text img, .document-text h1, .document-text a')).toHaveCount(0);
    expect(await page.evaluate(() => (window as Window & { markdownExecuted?: boolean }).markdownExecuted)).toBeUndefined();
    await expect(dialog.getByRole('link', { name: 'Download original', exact: true })).toHaveAttribute('download', asset.name);
    if (index === 1) await page.screenshot({ path: info.outputPath('full-inert-markdown-editor.png') });
    await dialog.getByRole('button', { name: 'Close', exact: true }).click();
  }
});

test('authored headings wrap while default cards and compact summaries retain their intended size', async ({ page, request }, info) => {
  const project = newProject('Long authored headings');
  const heading = addNode(project, project.entryCanvasId, 'text', 'Iliad XIX · Reconciliation and the cost of returning to battle');
  Object.assign(heading, { x: 60, y: 60, width: 340, height: 260, text: 'A concise explanation remains below the complete wrapped title.', style: { titleSize: 28, fontSize: 16, fontFamily: 'serif' } });
  const ordinary = addNode(project, project.entryCanvasId, 'text', heading.title);
  Object.assign(ordinary, { x: 470, y: 60, width: 300, height: 180, sizeMode: 'fixed', text: 'An ordinary compact app card keeps its default header.' });
  const compact = addNode(project, project.entryCanvasId, 'container', 'Selected genealogy · descendants and variant parentages');
  Object.assign(compact, { x: 60, y: 400, width: 1000, height: 750, sizeMode: 'fixed', text: 'A short summary remains readable before expanding this family branch.', collapsed: true, style: { titleSize: 24, fontSize: 14, fill: '#FFFCF4', textColor: '#243744' } });
  const child = addNode(project, project.entryCanvasId, 'text', 'Lineage'); Object.assign(child, { parentId: compact.id, x: 100, y: 540 });
  const target = addCanvas(project, 'A long target canvas title that belongs in the accessible name, not the visible launch button');
  const launch = addNode(project, project.entryCanvasId, 'canvas-link', 'Continue reading');
  Object.assign(launch, { x: 470, y: 400, width: 310, height: 230, text: 'Open a related reading canvas.', targetId: target.rootId, style: { titleSize: 24, fontSize: 26 } });
  const document = addNode(project, project.entryCanvasId, 'document', 'Readable source preview');
  Object.assign(document, { x: 830, y: 60, width: 280, height: 230, assetId: 'source-preview', style: { fill: '#FFFCF4', textColor: '#243744' } });
  project.assets.push({ id: 'source-preview', name: 'Source.txt', mime: 'text/plain', base64: Buffer.from('An attachment preview follows its authored text color.').toString('base64') });
  const saved = commitSnapshots(project); await request.post('/api/projects', { data: saved }); await page.goto(`/?project=${project.id}`);
  let compactDimensions: {width:number;height:number}|undefined;
  const verify = async () => {
    const title = page.locator(`.ms-node[data-node-id="${heading.id}"] .ms-node-title`);
    await expect(title).toHaveCSS('white-space', 'pre-wrap');
    const metrics = await title.evaluate(node => ({ height: node.clientHeight, scroll: node.scrollHeight, line: Number.parseFloat(getComputedStyle(node).lineHeight) }));
    expect(metrics.height).toBeGreaterThan(metrics.line * 2); expect(metrics.scroll).toBeLessThanOrEqual(metrics.height + 1);
    await expect(page.locator(`.ms-node[data-node-id="${ordinary.id}"] .ms-node-title`)).toHaveCSS('white-space', 'nowrap');
    const card = page.locator(`.ms-node[data-node-id="${compact.id}"]`);
    const dimensions = await card.evaluate(node => ({width:node.clientWidth,height:node.clientHeight}));
    expect(dimensions.height).toBeGreaterThan(0); expect(dimensions.height).toBeLessThan(180);
    if(compactDimensions)expect(dimensions).toEqual(compactDimensions);else compactDimensions=dimensions;
    await expect(card.locator('.ms-text')).toHaveText(compact.text!);
    const compactTitle = await card.locator('.ms-node-title').evaluate(node=>({height:node.clientHeight,scroll:node.scrollHeight}));
    expect(compactTitle.scroll).toBeLessThanOrEqual(compactTitle.height+1);
    const summary = await card.locator('.ms-text').evaluate(node => ({ height: node.clientHeight, scroll: node.scrollHeight }));
    expect(summary.height).toBeGreaterThan(25); expect(summary.scroll).toBeLessThanOrEqual(summary.height + 1);
    await expect(card.locator('.ms-node-header button')).toHaveCount(0);
    await expect(card.locator('.ms-node-title')).toHaveCSS('color', 'rgb(36, 55, 68)');
    await expect(page.locator(`.ms-node[data-node-id="${document.id}"] .ms-text-preview`)).toHaveCSS('color', 'rgb(36, 55, 68)');
    const open = page.locator(`.ms-node[data-node-id="${launch.id}"]`).getByRole('button', { name: `Open ${target.title}`, exact: true });
    await expect(open).toHaveText(''); await expect(open).toHaveAttribute('title', `Open ${target.title}`);
    await expect(open.locator('svg')).toHaveAttribute('aria-hidden','true');
    const fit=await open.evaluate(button=>{const box=button.getBoundingClientRect(),parent=button.parentElement!.getBoundingClientRect(),style=getComputedStyle(button);return{width:parseFloat(style.width),height:parseFloat(style.height),left:box.left-parent.left,right:parent.right-box.right};});
    expect(fit.width).toBeLessThanOrEqual(28.5);expect(fit.height).toBeLessThanOrEqual(28.5);expect(fit.left).toBeGreaterThanOrEqual(-1);expect(fit.right).toBeGreaterThanOrEqual(-1);
  };
  await verify(); await page.screenshot({ path: info.outputPath('authored-title-wrap-editor.png') });
  const html = buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8'));
  const path = info.outputPath('wrapped-headings.html'); await writeFile(path, html); await page.goto(pathToFileURL(path).href);
  await verify(); await page.screenshot({ path: info.outputPath('authored-title-wrap-viewer.png') });
});

test('collapse keeps parent media and summaries, hides child connections and restores authored dimensions', async ({ page, request }, info) => {
  const f = fixture(); await request.post('/api/projects', { data: f.project }); await page.goto(`/?project=${f.project.id}`);
  const branch = page.locator(`.ms-node[data-node-id="${f.branch.id}"]`), image = page.locator(`.ms-node[data-node-id="${f.image.id}"]`);
  const dimensions = () => branch.evaluate(node => ({width:node.clientWidth,height:node.clientHeight}));
  const collapsedDimensions = await dimensions();
  expect(collapsedDimensions.width).toBeGreaterThan(0); expect(collapsedDimensions.width).toBeLessThanOrEqual(400);
  expect(collapsedDimensions.height).toBeGreaterThan(0); expect(collapsedDimensions.height).toBeLessThan(180);
  await expect(branch.locator('.ms-text')).toHaveText(f.branch.text!); await expect(branch.locator('.ms-hidden-count')).toHaveCount(0);
  await expect(page.locator(`.ms-node[data-node-id="${f.child.id}"]`)).toHaveCount(0);
  await expect(image).toHaveCSS('height', '270px'); await expect(image.locator('img')).toBeVisible();
  await expect(image.getByRole('button', { name: 'Open One pixel.png' })).toHaveCount(0);
  await expect(page.locator(`.ms-node[data-node-id="${f.imageChild.id}"]`)).toHaveCount(0);
  await expect(page.locator('.ms-link-line[data-link-id="proxy-link"]')).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('compact-summary-and-media.png') });
  await branch.locator('.ms-node-title').dblclick();
  await expect(branch).toHaveCSS('width', '1200px'); await expect(branch).toHaveCSS('height', '900px');
  await expect(page.locator(`.ms-node[data-node-id="${f.child.id}"]`)).toBeVisible();
  await expect(page.locator('.ms-link-line[data-link-id="proxy-link"]')).toHaveCount(1);
  await branch.locator('.ms-node-title').dblclick();
  expect(await dimensions()).toEqual(collapsedDimensions);
  await workspaceAction(page,'Save'); await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  const saved = await (await request.get(`/api/projects/${f.project.id}`)).json();
  expect(saved.nodes.find((node: { id: string }) => node.id === f.branch.id)).toMatchObject({ width: 1200, height: 900, collapsed: true });
});

test('canvas GUID search reveals a hidden document in one undo and same-window history preserves canvas cameras', async ({ page, request }, info) => {
  const f = fixture(), response = await request.post('/api/projects', { data: f.project }); expect(response.ok()).toBeTruthy(); const baseline = await response.json(); await page.goto(`/?project=${f.project.id}&canvas=${f.home}`);
  await page.keyboard.press('Control+f');
  const homeSearch=page.getByRole('searchbox',{name:'Search Work nodes',exact:true});
  await homeSearch.fill(f.book.id); await expect(page.locator('.inspector-canvas-tree')).toContainText('No matching nodes on this canvas.'); await homeSearch.fill('');
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
  const homeCamera = await page.locator('.ms-world').getAttribute('style');
  const windowCount = page.context().pages().length;
  await page.getByRole('button',{name:`Open ${f.library.title}`,exact:true}).click();
  await page.waitForURL(url=>url.searchParams.get('canvas')===f.library.id);await expect(page.locator('.canvas-stage')).toBeVisible();
  expect(page.context().pages()).toHaveLength(windowCount);
  await page.getByRole('button', { name: 'Zoom out', exact: true }).click(); const libraryCamera = await page.locator('.ms-world').getAttribute('style');
  expect(libraryCamera).not.toBe(homeCamera);
  await page.goBack(); await page.waitForURL(url=>url.searchParams.get('canvas')===f.home);
  expect(await page.locator('.ms-world').getAttribute('style')).toBe(homeCamera);
  await page.goForward(); await page.waitForURL(url=>url.searchParams.get('canvas')===f.library.id);
  expect(await page.locator('.ms-world').getAttribute('style')).toBe(libraryCamera);
  expect(await (await request.get(`/api/projects/${f.project.id}`)).json()).toEqual(baseline);await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeDisabled();
  await page.keyboard.press('Control+f');const search=page.getByRole('searchbox',{name:'Search Work nodes',exact:true});
  await search.fill(f.book.id); await page.getByRole('button',{name:'Jump to Node: Complete source',exact:true}).click();
  await expect(page.locator(`.ms-node[data-node-id="${f.book.id}"]`)).toBeVisible();
  await expect(page.getByRole('button',{name:'Undo',exact:true})).toBeEnabled();
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(page.locator(`.ms-node[data-node-id="${f.book.id}"]`)).toHaveCount(0);
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await expect(page.locator(`.ms-node[data-node-id="${f.book.id}"]`)).toBeVisible();
  await page.screenshot({ path: info.outputPath('canvas-search-hidden-result.png') });
  await workspaceAction(page,'Save'); await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  const saved = await (await request.get(`/api/projects/${f.project.id}`)).json();
  expect(saved.nodes.find((node: { id: string }) => node.id === f.books.id).collapsed).toBe(false);
  expect(saved.assets).toEqual(baseline.assets);
  await search.fill('source');
  const shelf = page.getByRole('button', { name: 'Node: Closed source shelf', exact: true });
  const book = page.getByRole('button', { name: 'Node: Complete source', exact: true });
  await shelf.focus(); await page.keyboard.press('ArrowDown'); await expect(book).toBeFocused();
  await page.keyboard.press('ArrowUp'); await expect(shelf).toBeFocused();
  const revealedCamera = await page.locator('.ms-world').getAttribute('style');
  await page.goBack(); await page.waitForURL(url=>url.searchParams.get('canvas')===f.home);
  expect(await page.locator('.ms-world').getAttribute('style')).toBe(homeCamera);
  await page.goForward(); await page.waitForURL(url=>url.searchParams.get('canvas')===f.library.id);
  expect(await page.locator('.ms-world').getAttribute('style')).toBe(revealedCamera);
  await expect(page.locator(`.ms-node[data-node-id="${f.book.id}"]`)).toBeVisible();
  expect(page.context().pages()).toHaveLength(windowCount);
});

test('offline viewer searches full text, navigates originals and backlinks, and never rewrites the publication', async ({ page }, info) => {
  const f = fixture();
  const html = buildPublication(f.project, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8'));
  const path = info.outputPath('navigation-offline.html'); await writeFile(path, html);
  await page.route(/^https?:/, route => route.abort()); await page.goto(pathToFileURL(path).href);
  const original = await page.locator('#meridian-data').textContent();
  await page.getByRole('button', { name: 'Explore project', exact: true }).click();
  await page.getByRole('searchbox', { name: 'Search project' }).fill('AFTER_PREVIEW_BOUNDARY');
  await page.getByRole('button', { name: 'Go to Complete source in Source library', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Choose Work' })).toHaveValue(f.library.id);
  await expect(page.locator(`.ms-node[data-node-id="${f.book.id}"]`)).toBeVisible();
  await page.getByRole('button', { name: 'Close project navigation', exact: true }).click();
  await page.locator(`.ms-node[data-node-id="${f.book.id}"]`).getByRole('button', { name: 'Open Complete source.txt', exact: true }).click();
  await expect(page.locator('.ms-dialog-text')).toHaveText(f.text);
  expect(await page.evaluate(() => (window as Window & { shouldNeverRun?: boolean }).shouldNeverRun)).toBeUndefined();
  await page.screenshot({ path: info.outputPath('full-source-offline.png') });
  await page.getByRole('button', { name: 'Close attachment', exact: true }).click();
  await page.getByRole('button', { name: 'Explore project', exact: true }).click();
  await page.getByRole('button', { name: 'Open reference Source elsewhere', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Choose Work' })).toHaveValue(f.home);
  await page.getByRole('button', { name: 'Open original context', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Choose Work' })).toHaveValue(f.library.id);
  await page.getByRole('button', { name: 'Back', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Choose Work' })).toHaveValue(f.home);
  await page.getByRole('button', { name: 'Forward', exact: true }).click();
  await expect(page.getByRole('combobox', { name: 'Choose Work' })).toHaveValue(f.library.id);
  await page.screenshot({ path: info.outputPath('offline-navigation.png') });
  expect(await page.locator('#meridian-data').textContent()).toBe(original);
  const downloadEvent = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download this published canvas for viewing or importing into the Meridian editor', exact: true }).click();
  const download = await downloadEvent, copyPath = info.outputPath('unchanged-copy.html'); await download.saveAs(copyPath);
  const copy = readPublication(await readFile(copyPath, 'utf8'));
  expect(copy.nodes.find(node => node.title === f.books.title)?.collapsed).toBe(true);
  await page.getByRole('searchbox', { name: 'Search project' }).fill('');
  const shelf = page.getByRole('button', { name: 'Go to Closed source shelf', exact: true });
  const book = page.getByRole('button', { name: 'Go to Complete source', exact: true });
  await shelf.focus(); await page.keyboard.press('ArrowDown'); await expect(book).toBeFocused();
  await page.keyboard.press('Enter'); await expect(book).toBeFocused();
  await page.keyboard.press('ArrowUp'); await expect(shelf).toBeFocused();
  await page.setViewportSize({ width: 390, height: 800 });
  await expect(page.getByRole('searchbox', { name: 'Search project' })).toBeVisible();
  await page.screenshot({ path: info.outputPath('offline-navigation-mobile.png') });
});
