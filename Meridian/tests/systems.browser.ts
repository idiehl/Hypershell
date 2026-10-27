import { workspaceAction } from './ui-helpers.ts';
import { test, expect, type Page } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createModule, instantiateModule } from '../src/core/modules.ts';
import { buildPublication, readPublication } from '../src/publication.ts';
import type { LinkKind, MeridianNode, MeridianProject } from '../src/core/types.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const state = (page: Page, id: string) => card(page, id).locator('.ms-state');

function numericSystem() {
  const project = newProject('Numbers, operations and explicit joins'), canvas = project.entryCanvasId;
  const system = addNode(project, canvas, 'container', 'Price calculation');
  Object.assign(system, { x: 40, y: 60, width: 800, height: 540, text: 'Quantity and unit price flow into an explicit multiplication joint. The switch controls whether a result is available. '.repeat(8) });
  const amount = addNode(project, canvas, 'number', 'Quantity'); Object.assign(amount, { x: 70, y: 190, width: 170, height: 135, parentId: system.id, value: 12.5 });
  const factor = addNode(project, canvas, 'number', 'Unit price'); Object.assign(factor, { x: 70, y: 405, width: 170, height: 135, parentId: system.id, value: 2 });
  const multiply = addNode(project, canvas, 'joint', 'Multiply'); Object.assign(multiply, { x: 310, y: 290, width: 190, height: 145, parentId: system.id, operation: 'MULTIPLY' });
  const enabled = addNode(project, canvas, 'switch', 'Calculation enabled'); Object.assign(enabled, { x: 570, y: 292, width: 210, height: 145, parentId: system.id, valueType: 'number' });
  const total = addNode(project, canvas, 'output', 'Total'); Object.assign(total, { x: 920, y: 155, width: 220, height: 145, valueType: 'number' });
  const threshold = addNode(project, canvas, 'number', 'Threshold'); Object.assign(threshold, { x: 920, y: 435, width: 220, height: 135, value: 20, exposed: false });
  const greater = addNode(project, canvas, 'joint', 'Above threshold'); Object.assign(greater, { x: 1220, y: 290, width: 210, height: 145, operation: 'GREATER' });
  const decision = addNode(project, canvas, 'output', 'Condition'); Object.assign(decision, { x: 1510, y: 295, width: 195, height: 135 });
  // Keep this deliberately bounded composition and its expandable description.
  for (const node of project.nodes) node.sizeMode = 'fixed';
  const wire = (source: MeridianNode, target: MeridianNode, kind: LinkKind, port = 0, id = crypto.randomUUID()) => {
    project.links.push({ id, canvasId: canvas, sourceId: source.id, targetId: target.id, kind, relation: 'causal', targetPort: port }); return id;
  };
  wire(amount, multiply, 'numeric'); wire(factor, multiply, 'numeric', 1); wire(multiply, enabled, 'numeric');
  const outputLink = wire(enabled, total, 'numeric'); wire(total, greater, 'numeric'); wire(threshold, greater, 'numeric', 1); wire(greater, decision, 'functional');
  return { project, system, amount, factor, multiply, enabled, total, threshold, greater, decision, outputLink };
}

test('numeric controls, comparisons, explicit junction insertion and hidden calculations work in the editor', async ({ page, request }, info) => {
  await page.setViewportSize({ width: 1760, height: 980 });
  const fixture = numericSystem(), { project, system, amount, multiply, enabled, total, decision, outputLink } = fixture;
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok()).toBeTruthy();
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.goto(`/?project=${project.id}`); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await expect(state(page, total.id)).toHaveText('25'); await expect(state(page, decision.id)).toHaveText('On');
  await expect(card(page, multiply.id).locator('.ms-operation-mark')).toHaveText('×');
  await expect(card(page, multiply.id).locator('.ms-port-label')).toHaveText(['A', 'B']);
  await expect(page.locator('.ms-link-numeric')).toHaveCount(6);
  const summary = card(page, system.id).locator('.ms-contained-summary');
  const firstChild = card(page, amount.id);
  const bounds = await summary.boundingBox(), childBounds = await firstChild.boundingBox();
  expect(bounds!.y + bounds!.height).toBeLessThan(childBounds!.y);
  await page.getByRole('button', { name: 'Read description of Price calculation', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Price calculation description' })).toContainText(system.text!);
  await page.getByRole('button', { name: 'Close description', exact: true }).click();
  await card(page, amount.id).getByRole('spinbutton', { name: 'Quantity: value', exact: true }).fill('6');
  await card(page, amount.id).getByRole('spinbutton', { name: 'Quantity: value', exact: true }).press('Enter');
  await expect(state(page, total.id)).toHaveText('12'); await expect(state(page, decision.id)).toHaveText('Off');
  await page.locator(`.ms-link-hit[data-link-id="${outputLink}"]`).press('Enter');
  await page.locator('.inspector summary').filter({hasText:'Attachment points and route'}).click();
  await page.getByRole('button', { name: 'Insert joint', exact: true }).click();
  await expect(page.locator('.ms-node-junction.ms-utility')).toHaveCount(1); await expect(state(page, total.id)).toHaveText('12');
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await page.getByRole('switch', { name: 'Calculation enabled: toggle switch', exact: true }).click();
  await expect(state(page, total.id)).toHaveText('Unknown'); await expect(state(page, decision.id)).toHaveText('Unknown');
  await page.getByRole('switch', { name: 'Calculation enabled: toggle switch', exact: true }).click();
  await card(page, system.id).locator('.ms-node-title').dblclick();
  await expect(card(page, enabled.id)).toHaveCount(0); await expect(state(page, total.id)).toHaveText('12');
  await expect(state(page, decision.id)).toHaveText('Off');
  await workspaceAction(page,'Save'); await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  const saved = await (await request.get(`/api/projects/${project.id}`)).json() as MeridianProject;
  expect(saved.revision).toBeGreaterThan(1);
  expect(saved.nodes.find(node => node.id === amount.id)?.value).toBe(6);
  expect(saved.nodes.filter(node => node.kind === 'junction')).toHaveLength(1);
  expect(saved.nodes.find(node => node.kind === 'junction')?.valueType).toBe('number');
  await page.reload(); await expect(state(page, total.id)).toHaveText('12'); expect(errors).toEqual([]);
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await page.screenshot({ path: info.outputPath('numeric-editor-collapsed.png'), fullPage: true });
  for (const width of [1500, 1280]) {
    await page.setViewportSize({ width, height: 950 }); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
    const toolbar = page.locator('.canvas-toolbar');
    const overflow = await toolbar.evaluate(element => {
      const rect = element.getBoundingClientRect();
      return [...element.querySelectorAll('button,select')].filter(node => {
        const child = node.getBoundingClientRect(); return child.width > 0 && (child.left < rect.left - 1 || child.right > rect.right + 1 || child.top < rect.top - 1 || child.bottom > rect.bottom + 1);
      }).map(node => node.getAttribute('aria-label') || node.textContent);
    });
    expect(overflow).toEqual([]);
    for(const name of ['New Node','Delete objects','Single select'])await expect(page.getByRole('button',{name,exact:true})).toBeVisible();
    await page.getByRole('button',{name:'New Node',exact:true}).click();await expect(page.getByRole('menuitem',{name:'Link',exact:true})).toBeVisible();await page.keyboard.press('Escape');
    await page.screenshot({ path: info.outputPath(`numeric-editor-${width}.png`), fullPage: true });
  }
});

test('standalone numeric controls stay transient, finite and stopped until run, with offline module parity', async ({ browser }, info) => {
  const fixture = numericSystem(), { project, amount, factor, multiply, total, threshold, decision } = fixture;
  // The same stored-module path used by the editor retains numeric fields and operations.
  const module = createModule(project, fixture.system.id, 'Numeric calculation');
  const destination = newProject('Module copy');
  const inserted = instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 80, y: 80 });
  expect(inserted.project.nodes.find(node => node.title === 'Quantity')).toMatchObject({ kind: 'number', value: 12.5 });
  expect(inserted.project.nodes.find(node => node.title === 'Multiply')?.operation).toBe('MULTIPLY');
  multiply.operation = 'DIVIDE';
  const html = buildPublication(commitSnapshots(project), await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8'));
  const file = info.outputPath('numeric-offline.html'); await writeFile(file, html);
  const context = await browser.newContext({ viewport: { width: 1760, height: 980 }, offline: true, acceptDownloads: true });
  try {
    const page = await context.newPage(), errors: string[] = [], remote: string[] = [];
    page.on('pageerror', error => errors.push(error.message)); page.on('request', request => { if (/^https?:/.test(request.url())) remote.push(request.url()); });
    await page.goto(pathToFileURL(file).href);
    await expect(page.getByRole('status')).toHaveText('Stopped'); await expect(state(page, total.id)).toHaveText('Stopped');
    await expect(card(page, threshold.id).getByRole('spinbutton')).toBeDisabled();
    await page.getByRole('button', { name: 'Run simulation', exact: true }).click(); await expect(state(page, total.id)).toHaveText('6.25');
    const authored = await page.locator('#meridian-data').textContent();
    await card(page, factor.id).getByRole('spinbutton').fill('0'); await card(page, factor.id).getByRole('spinbutton').press('Enter');
    await expect(state(page, total.id)).toHaveText('Unknown'); await expect(state(page, decision.id)).toHaveText('Unknown');
    await card(page, factor.id).getByRole('spinbutton').fill('2'); await card(page, factor.id).getByRole('spinbutton').press('Enter');
    await card(page, amount.id).getByRole('spinbutton').fill('50'); await card(page, amount.id).getByRole('spinbutton').press('Enter');
    await expect(state(page, total.id)).toHaveText('25'); await expect(state(page, decision.id)).toHaveText('On');
    expect(await page.locator('#meridian-data').textContent()).toBe(authored);
    const downloading = page.waitForEvent('download'); await page.getByRole('button', { name: 'Download this published canvas for viewing or importing into the Meridian editor' }).click();
    const download = await downloading, copy = info.outputPath('numeric-viewer-copy.html'); await download.saveAs(copy);
    const imported = readPublication(await readFile(copy, 'utf8'));
    expect(imported.nodes.find(node => node.title === 'Quantity')?.value).toBe(12.5);
    expect(imported.nodes.find(node => node.title === 'Total')?.valueType).toBe('number');
    await page.getByRole('button', { name: 'Reset to authored state', exact: true }).click();
    await expect(page.getByRole('status')).toHaveText('Stopped'); await expect(card(page, amount.id).getByRole('spinbutton')).toHaveValue('12.5');
    expect(errors).toEqual([]); expect(remote).toEqual([]);
    await page.screenshot({ path: info.outputPath('numeric-offline.png'), fullPage: true });
  } finally { await context.close(); }
});

test('cross-canvas numeric references show their saved value and keep the source read only', async ({ browser }, info) => {
  const project = newProject('Pinned numeric values'), remote = addCanvas(project, 'Original context');
  const number = addNode(project, remote.id, 'number', 'Saved measurement'); number.value = 7.25;
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Pinned measurement'); reference.targetId = number.id;
  const output = addNode(project, project.entryCanvasId, 'output', 'Observed measurement'); output.valueType = 'number'; output.x = 440;
  project.links.push({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: reference.id, targetId: output.id, kind: 'numeric', relation: 'causal' });
  const saved = commitSnapshots(project); saved.nodes.find(node => node.id === number.id)!.value = 99;
  const file = info.outputPath('numeric-reference.html'); await writeFile(file, buildPublication(saved, await readFile('dist/viewer.js', 'utf8'), await readFile('dist/scene.css', 'utf8')));
  const context = await browser.newContext({ offline: true });
  try {
    const page = await context.newPage(); await page.goto(pathToFileURL(file).href); await page.getByRole('button', { name: 'Run simulation', exact: true }).click();
    await expect(card(page, reference.id).locator('.ms-reference-value')).toHaveText('7.25');
    await expect(card(page, reference.id).getByRole('spinbutton')).toHaveCount(0);
    await expect(state(page, output.id)).toHaveText('7.25');
  } finally { await context.close(); }
});

test('crossed visual routes stay separate until a source is explicitly joined to an existing segment', async ({ page, request }, info) => {
  const project = newProject('Explicit route joining'), canvas = project.entryCanvasId;
  const named = (title: string, x: number, y: number) => { const node = addNode(project, canvas, 'text', title); Object.assign(node, { x, y, width: 180, height: 100, text: title }); return node; };
  const a = named('Left endpoint', 80, 150), b = named('Right endpoint', 820, 150);
  const c = named('Upper endpoint', 460, 0), d = named('Lower endpoint', 460, 430), source = named('Joining source', 80, 430);
  const routeId = crypto.randomUUID(), crossingId = crypto.randomUUID();
  project.links.push({ id: routeId, canvasId: canvas, sourceId: a.id, targetId: b.id, kind: 'visual', relation: 'causal' }, { id: crossingId, canvasId: canvas, sourceId: c.id, targetId: d.id, kind: 'visual', relation: 'causal' });
  expect((await request.post('/api/projects', { data: commitSnapshots(project) })).ok()).toBeTruthy();
  await page.goto(`/?project=${project.id}`); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await expect(page.locator('.ms-node-junction')).toHaveCount(0);
  const target = await page.locator(`.ms-link-line[data-link-id="${routeId}"]`).evaluate(path => {
    const curve = path as SVGPathElement, point = curve.getPointAtLength(curve.getTotalLength() * 0.3), screen = new DOMPoint(point.x, point.y).matrixTransform(curve.getScreenCTM()!);
    return { x: screen.x, y: screen.y };
  });
  await page.mouse.click(target.x, target.y,{button:'right'});await page.getByRole('menuitem',{name:'Create Link',exact:true}).click();
  const linkForm=page.getByRole('dialog',{name:'New Link',exact:true});
  await linkForm.getByLabel('Source',{exact:true}).selectOption(source.id);await expect(linkForm.getByLabel('Destination',{exact:true})).not.toHaveValue('');
  await linkForm.getByRole('button',{name:'Cancel',exact:true}).click();
  await expect(page.locator('.ms-node-junction')).toHaveCount(0);await expect(page.locator('.ms-link-hit')).toHaveCount(2);
  expect((await (await request.get(`/api/projects/${project.id}`)).json()).links).toHaveLength(2);
  await page.mouse.click(target.x, target.y,{button:'right'});await page.getByRole('menuitem',{name:'Create Link',exact:true}).click();
  await linkForm.getByLabel('Source',{exact:true}).selectOption(source.id);await linkForm.getByRole('button',{name:'Create link',exact:true}).click();
  await expect(page.locator('.ms-node-junction.ms-utility')).toHaveCount(1);
  await expect(page.getByRole('button',{name: 'Single select',exact:true})).toHaveAttribute('aria-pressed','true');
  await workspaceAction(page,'Save'); await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  const saved = await (await request.get(`/api/projects/${project.id}`)).json() as MeridianProject, junction = saved.nodes.find(node => node.kind === 'junction')!;
  expect(junction.valueType).toBeUndefined(); expect(saved.links).toHaveLength(4);
  expect(saved.links.find(link => link.id === crossingId)).toMatchObject({ sourceId: c.id, targetId: d.id });
  expect(saved.links.filter(link => link.sourceId === junction.id || link.targetId === junction.id)).toHaveLength(3);
  expect(saved.links.some(link => link.sourceId === source.id && link.targetId === junction.id)).toBeTruthy();
  const insertion = await page.locator(`.ms-link-line[data-link-id="${routeId}"]`).evaluate(path => {
    const curve = path as SVGPathElement, world = curve.getPointAtLength(curve.getTotalLength() * 0.8);
    const screen = new DOMPoint(world.x, world.y).matrixTransform(curve.getScreenCTM()!);
    return { world: { x: world.x, y: world.y }, screen: { x: screen.x, y: screen.y } };
  });
  await page.mouse.click(insertion.screen.x, insertion.screen.y);
  await page.locator('.inspector summary').filter({hasText:'Attachment points and route'}).click();
  await page.getByRole('button', { name: 'Insert joint', exact: true }).click();
  await expect(page.locator('.ms-node-junction')).toHaveCount(2);
  await workspaceAction(page,'Save'); await expect(page.locator('.save-state')).toHaveText(/^Saved · r\d+$/);
  const inserted = await (await request.get(`/api/projects/${project.id}`)).json() as MeridianProject;
  expect(inserted.revision).toBeGreaterThan(saved.revision);
  const point = inserted.nodes.find(node => node.kind === 'junction' && node.id !== junction.id)!;
  expect(Math.abs(point.x + point.width / 2 - insertion.world.x)).toBeLessThan(2);
  expect(Math.abs(point.y + point.height / 2 - insertion.world.y)).toBeLessThan(2);
  await page.screenshot({ path: info.outputPath('explicit-visual-junction.png'), fullPage: true });
});


