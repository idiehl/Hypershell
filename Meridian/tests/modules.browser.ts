import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createModule } from '../src/core/modules.ts';
import type { MeridianNode, MeridianProject, StoredModule } from '../src/core/types.ts';
import { selectEditorNode, toolsAction } from './ui-helpers.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const readProject = async (request: APIRequestContext, id: string) => await (await request.get(`/api/projects/${id}`)).json() as MeridianProject;

// All project/module writes use playwright.config.ts's isolated data-root host.
// No test opens the installed Meridian host or modifies the user's saved modules.
function assembly() {
  const project = newProject('Module browser QA');
  const module = addNode(project, project.entryCanvasId, 'module', 'Boolean assembly');
  Object.assign(module, { x: 60, y: 60, width: 1040, height: 600, text: 'A reusable assembly with ordinary named controls.' });
  const calculation = addNode(project, project.entryCanvasId, 'container', 'Calculation');
  Object.assign(calculation, { parentId: module.id, x: 90, y: 190, width: 690, height: 360, text: 'Collapse these internals while Result remains visible.' });
  const input = addNode(project, project.entryCanvasId, 'input', 'Enable');
  Object.assign(input, { parentId: calculation.id, x: 115, y: 300, width: 165, height: 120, value: true });
  const reference = addNode(project, project.entryCanvasId, 'reference', 'Internal reading');
  Object.assign(reference, { parentId: calculation.id, x: 310, y: 300, width: 220, height: 135, targetId: input.id });
  const gate = addNode(project, project.entryCanvasId, 'joint', 'Invert');
  Object.assign(gate, { parentId: calculation.id, x: 585, y: 305, width: 145, height: 135, operation: 'NOT', dependencyIds: [reference.id] });
  const output = addNode(project, project.entryCanvasId, 'output', 'Result');
  Object.assign(output, { parentId: module.id, x: 830, y: 300, width: 210, height: 150, dependencyIds: [gate.id] });
  project.links.push(
    { id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: reference.id, targetId: gate.id, kind: 'functional', relation: 'causal', targetPort: 0 },
    { id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: gate.id, targetId: output.id, kind: 'functional', relation: 'causal', targetPort: 0, points: [{ x: 810, y: 360 }] },
  );
  return { project: commitSnapshots(project), module, calculation, input, reference, gate, output };
}

async function create(request: APIRequestContext, project: MeridianProject) {
  const response = await request.post('/api/projects', { data: project });
  expect(response.ok()).toBeTruthy();
  return await response.json() as MeridianProject;
}

async function selectNode(page: Page, id: string) {
  await selectEditorNode(page,id,id);await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('relations');
  await expect(card(page, id)).toHaveAttribute('aria-selected', 'true');
}

async function save(page: Page, request: APIRequestContext, id: string) {
  const previous = await page.$('.toast');
  await page.keyboard.press('Control+s');
  await page.waitForFunction(prior => {
    const current = document.querySelector('.toast');
    return current !== prior && /^All changes are saved · r\d+\.$/.test(current?.textContent ?? '');
  }, previous);
  await previous?.dispose();
  return await readProject(request, id);
}

async function shelf(page: Page, module: StoredModule, query = module.id) {
  await toolsAction(page,'Modules');
  const dialog = page.getByRole('dialog', { name: 'Module library', exact: true });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Search modules', { exact: true }).fill(query);
  const item = dialog.locator(`[data-module-id="${module.id}"]`);
  await expect(item).toBeVisible();
  await item.click();
  await expect(dialog.getByRole('button', { name: 'Insert module', exact: true })).toBeEnabled();
  return { dialog, item };
}

async function storeDefinition(request: APIRequestContext, source: ReturnType<typeof assembly>) {
  const module = createModule(source.project, source.module.id, `Boolean toolkit ${source.project.id.slice(0, 8)}`);
  const response = await request.post('/api/modules', { data: { module } });
  expect(response.status()).toBe(201);
  return module;
}

function branch(project: MeridianProject, rootId: string): MeridianNode[] {
  const ids = new Set([rootId]);
  for (let changed = true; changed;) {
    changed = false;
    for (const node of project.nodes) if (node.parentId && ids.has(node.parentId) && !ids.has(node.id)) { ids.add(node.id); changed = true; }
  }
  return project.nodes.filter(node => ids.has(node.id));
}

test('save a reusable node module, insert independent assemblies, undo and redo, and keep sibling outputs live', async ({ page, request }, info) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  const source = assembly(), sourceSaved = await create(request, source.project);
  await page.goto(`/?project=${sourceSaved.id}`);
  await selectNode(page, source.module.id);
  await page.getByRole('button', { name: 'Save as module', exact: true }).click();
  const saveDialog = page.getByRole('dialog', { name: 'Save reusable module', exact: true });
  const moduleName = `Independent Boolean toolkit ${sourceSaved.id.slice(0, 8)}`;
  await saveDialog.getByLabel('Name', { exact: true }).fill(moduleName);
  await saveDialog.getByRole('button', { name: 'Save module', exact: true }).click();
  await expect(saveDialog).toBeHidden();
  await expect.poll(async () => (await (await request.get('/api/modules')).json() as { id: string; name: string }[]).some(item => item.name === moduleName)).toBe(true);
  const catalog = await (await request.get('/api/modules')).json() as { id: string; name: string; nodeCount: number; linkCount: number }[];
  const summary = catalog.find(item => item.name === moduleName)!;
  expect(summary.nodeCount).toBe(6); expect(summary.linkCount).toBe(2);
  const definition = await (await request.get(`/api/modules/${summary.id}`)).json() as StoredModule;
  expect(await readProject(request, sourceSaved.id)).toEqual(sourceSaved);

  const destination = await create(request, newProject('Independent module instances'));
  await page.goto(`/?project=${destination.id}`);
  let library = await shelf(page, definition, moduleName);
  await expect(library.dialog.locator('.module-id')).toHaveText(definition.id);
  await page.screenshot({ path: info.outputPath('module-library.png') });
  await library.dialog.getByRole('button', { name: 'Insert module', exact: true }).click();
  const once = await save(page, request, destination.id);
  expect(once.nodes.filter(node => node.kind === 'module')).toHaveLength(1);
  const firstRoot = once.nodes.find(node => node.kind === 'module')!;
  expect(branch(once, firstRoot.id)).toHaveLength(6);

  await page.locator('#scene').click({ position: { x: 6, y: 6 } });
  library = await shelf(page, definition);
  await library.dialog.getByRole('button', { name: 'Insert module', exact: true }).click();
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  const undone = await save(page, request, destination.id);
  expect(undone.nodes).toEqual(once.nodes); expect(undone.links).toEqual(once.links);
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  const twice = await save(page, request, destination.id);
  const roots = twice.nodes.filter(node => node.kind === 'module');
  expect(roots).toHaveLength(2);
  expect(roots.every(root => root.parentId === twice.canvases[0].rootId)).toBe(true);
  const secondRoot = roots.find(root => root.id !== firstRoot.id)!;
  const first = branch(twice, firstRoot.id), second = branch(twice, secondRoot.id);
  const originals = new Set([...sourceSaved.nodes, ...definition.project.nodes].map(node => node.id));
  expect([...first, ...second].every(node => !originals.has(node.id))).toBe(true);
  expect(new Set([...first, ...second].map(node => node.id)).size).toBe(12);
  for (const nodes of [first, second]) {
    const input = nodes.find(node => node.kind === 'input')!, reference = nodes.find(node => node.kind === 'reference')!;
    const gate = nodes.find(node => node.kind === 'joint')!, output = nodes.find(node => node.kind === 'output')!;
    expect(reference.targetId).toBe(input.id);
    expect(gate.dependencyIds).toEqual([reference.id]); expect(output.dependencyIds).toEqual([gate.id]);
    const ids = new Set(nodes.map(node => node.id));
    const links = twice.links.filter(link => ids.has(link.sourceId));
    expect(links).toHaveLength(2); expect(links.every(link => ids.has(link.targetId))).toBe(true);
    const packing = nodes.find(node => node.kind === 'module')!;
    const savedPacking = definition.project.nodes.find(node => node.id === definition.rootId)!;
    for (const node of nodes) {
      const original = definition.project.nodes.find(item => item.title === node.title && item.kind === node.kind)!;
      expect(node.x - packing.x).toBeCloseTo(original.x - savedPacking.x, 5);
      expect(node.y - packing.y).toBeCloseTo(original.y - savedPacking.y, 5);
    }
  }
  const firstInput = first.find(node => node.kind === 'input')!, firstCalculation = first.find(node => node.kind === 'container')!;
  const firstOutput = first.find(node => node.kind === 'output')!, secondOutput = second.find(node => node.kind === 'output')!;
  await selectNode(page, firstInput.id);
  await card(page, firstInput.id).getByRole('switch', { name: 'Enable: toggle input', exact: true }).click();
  await expect(card(page, firstOutput.id).locator('.ms-state')).toHaveText('On');
  await expect(card(page, secondOutput.id).locator('.ms-state')).toHaveText('Off');
  await selectNode(page, firstCalculation.id);
  // Node titles are inline text-editing surfaces. Disclosure has its own
  // ordinary Relations control, which leaves the title and focus untouched.
  await page.getByRole('button', { name: 'Toggle contents from relations', exact: true }).click();
  await expect(card(page, firstInput.id)).toHaveCount(0);
  await expect(card(page, firstOutput.id).locator('.ms-state')).toHaveText('On');
  await expect(card(page, secondOutput.id).locator('.ms-state')).toHaveText('Off');
  const committed = await save(page, request, destination.id);
  await page.reload();
  await expect(card(page, firstInput.id)).toHaveCount(0);
  await expect(card(page, firstOutput.id).locator('.ms-state')).toHaveText('On');
  await expect(card(page, secondOutput.id).locator('.ms-state')).toHaveText('Off');
  expect(await readProject(request, destination.id)).toEqual(committed);
  expect(await (await request.get(`/api/modules/${definition.id}`)).json()).toEqual(definition);
  expect(await readProject(request, sourceSaved.id)).toEqual(sourceSaved);
  expect(errors).toEqual([]);
  await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
  await page.screenshot({ path: info.outputPath('independent-modules-sibling-output.png') });
});

test('opening a module definition copy creates an editable canvas without changing the stored definition', async ({ page, request }, info) => {
  const source = assembly(), definition = await storeDefinition(request, source);
  const destination = await create(request, newProject('Definition-copy entry'));
  await page.goto(`/?project=${destination.id}`);
  const library = await shelf(page, definition);
  await library.dialog.getByRole('button', { name: 'Open definition copy', exact: true }).click();
  await expect(page.locator('[data-current-canvas]')).toContainText(definition.name);
  const copyId = new URL(page.url()).searchParams.get('project')!;
  expect(copyId).not.toBe(destination.id); expect(copyId).not.toBe(definition.project.id);
  const copy = await readProject(request, copyId);
  expect(copy.nodes).toHaveLength(definition.project.nodes.length);
  const originalIds = new Set(definition.project.nodes.map(node => node.id));
  expect(copy.nodes.every(node => !originalIds.has(node.id))).toBe(true);
  const input = copy.nodes.find(node => node.kind === 'input')!;
  await selectNode(page, input.id);
  await card(page, input.id).getByRole('switch', { name: 'Enable: toggle input', exact: true }).click();
  const changed = await save(page, request, copy.id);
  expect(changed.nodes.find(node => node.id === input.id)?.value).toBe(false);
  expect(await (await request.get(`/api/modules/${definition.id}`)).json()).toEqual(definition);
  expect((await readProject(request, destination.id)).nodes).toEqual(destination.nodes);
  await page.screenshot({ path: info.outputPath('editable-module-definition-copy.png') });
});

test('node dependencies expose transitive missing requirements without treating visual neighbors as dependencies', async ({ page, request }, info) => {
  const project = newProject('Dependency declarations browser QA');
  const note = addNode(project, project.entryCanvasId, 'text', 'Dependent explanation');
  const context = addNode(project, project.entryCanvasId, 'text', 'Required operating context');
  const neighbor = addNode(project, project.entryCanvasId, 'text', 'Visual neighbor only');
  Object.assign(note, { x: 60, y: 80, width: 300, height: 180, text: 'This note names the context needed for reuse.' });
  Object.assign(context, { x: 420, y: 80, width: 300, height: 180, text: 'The operating context has an unavailable requirement of its own.' });
  Object.assign(neighbor, { x: 780, y: 80, width: 300, height: 180, text: 'A visual relation alone does not declare a dependency.' });
  project.links.push({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: note.id, targetId: neighbor.id, kind: 'visual', relation: 'mutual' });
  const saved = await create(request, commitSnapshots(project));
  const missingId = crypto.randomUUID();
  await page.goto(`/?project=${saved.id}`);
  await selectNode(page, context.id);
  await page.getByRole('button', { name: 'Edit dependencies', exact: true }).click();
  let dialog = page.getByRole('dialog', { name: 'Node dependencies', exact: true });
  await dialog.getByLabel('Dependency GUID', { exact: true }).fill(missingId);
  await dialog.getByRole('button', { name: 'Add dependency', exact: true }).click();
  await expect(dialog.locator('.dependency-row').filter({ hasText: missingId })).toContainText('Not included');
  await dialog.getByRole('button', { name: 'Apply dependencies', exact: true }).click();

  await selectNode(page, note.id);
  await page.getByRole('button', { name: 'Edit dependencies', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Node dependencies', exact: true });
  await dialog.getByLabel('Available node dependency', { exact: true }).selectOption(context.id);
  await dialog.getByRole('button', { name: 'Add dependency', exact: true }).click();
  await expect(dialog.locator('.dependency-row').filter({ hasText: context.id })).toContainText('Included');
  await expect(dialog.locator('.dependency-row').filter({ hasText: missingId })).toContainText('Not included');
  await expect(dialog.locator('.dependency-row').filter({ hasText: neighbor.id })).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('transitive-node-declarations.png') });
  await dialog.getByRole('button', { name: 'Apply dependencies', exact: true }).click();
  const committed = await save(page, request, saved.id);
  expect(committed.nodes.find(node => node.id === note.id)?.dependencyIds).toEqual([context.id]);
  expect(committed.nodes.find(node => node.id === context.id)?.dependencyIds).toEqual([missingId]);
  expect(committed.nodes.find(node => node.id === neighbor.id)?.dependencyIds).toBeUndefined();
  expect(committed.links).toEqual(saved.links);
  await page.reload();
  await selectNode(page, note.id);
  await page.getByRole('button', { name: 'Edit dependencies', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Node dependencies', exact: true });
  await expect(dialog.locator('.declared-dependency')).toContainText(context.id);
  await expect(dialog.locator('.dependency-row').filter({ hasText: missingId })).toContainText('Not included');
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();

  await page.getByRole('button', { name: 'Save as module', exact: true }).click();
  const preflight = page.getByRole('dialog', { name: 'Save reusable module', exact: true });
  await expect(preflight.locator('.module-stats')).toContainText('2 nodes');
  await expect(preflight.locator('.dependency-row').filter({ hasText: context.id })).toContainText('Not included');
  await expect(preflight.locator('.dependency-row').filter({ hasText: missingId })).toContainText('Not included');
  await expect(preflight.locator('.dependency-row').filter({ hasText: neighbor.id })).toHaveCount(0);
  await page.screenshot({ path: info.outputPath('module-inclusion-and-missing-context.png') });
  await preflight.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect(await readProject(request, saved.id)).toEqual(committed);
});

test('module insertion rejects inherited-rule conflicts without partially editing the destination', async ({ page, request }) => {
  const definition = await storeDefinition(request, assembly());
  const project = newProject('Restricted module destination');
  const parent = addNode(project, project.entryCanvasId, 'container', 'Text-only collection');
  Object.assign(parent, { x: 100, y: 100, width: 550, height: 300, rules: { nodeKinds: ['container', 'text'], linkKinds: ['visual'] } });
  const saved = await create(request, commitSnapshots(project));
  await page.goto(`/?project=${saved.id}`);
  await selectNode(page, parent.id);
  const library = await shelf(page, definition);
  await library.dialog.getByRole('button', { name: 'Insert module', exact: true }).click();
  await expect(library.dialog.getByRole('alert')).toContainText(/inherited|excluded/i);
  await expect(library.dialog).toBeVisible();
  await library.dialog.getByRole('button', { name: 'Close', exact: true }).click();
  await expect(page.locator('.ms-node')).toHaveCount(1);
  expect(await readProject(request, saved.id)).toEqual(saved);
});

test('a pending definition copy keeps its shelf modal and source draft protected until the copy is persisted', async ({ page, request }) => {
  const definition = await storeDefinition(request, assembly());
  const project = newProject('Protected definition-copy source');
  const note = addNode(project, project.entryCanvasId, 'text', 'Protected note');
  Object.assign(note, { x: 100, y: 100, width: 320, height: 180, text: 'Saved source text' });
  const saved = await create(request, commitSnapshots(project));
  await page.goto(`/?project=${saved.id}`);
  await selectNode(page, note.id);
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('content');const text = page.getByLabel('Text body', { exact: true });
  await page.route(`**/api/projects/${saved.id}`,route=>route.request().method()==='PUT'?route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({error:'Pause source saving to exercise protected recovery'})}):route.continue());
  await text.fill('The unsaved source draft must remain protected');
  await text.press('Tab');
  await expect(page.getByRole('alert')).toContainText('Automatic saving paused: Pause source saving to exercise protected recovery');
  const library = await shelf(page, definition);
  let release!: () => void, postedId = '';
  const hold = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/projects', async route => {
    if (route.request().method() !== 'POST') { await route.continue(); return; }
    postedId = (route.request().postDataJSON() as MeridianProject).id;
    await hold;
    await route.continue();
  });
  try {
    const starting = page.waitForRequest(request => request.method() === 'POST' && new URL(request.url()).pathname === '/api/projects');
    await library.dialog.getByRole('button', { name: 'Open definition copy', exact: true }).click();
    await page.getByRole('button', { name: 'Keep draft and leave', exact: true }).click();
    await starting;
    await expect.poll(() => postedId).not.toBe('');
    await expect(library.dialog.getByRole('button', { name: 'Close', exact: true })).toBeDisabled();
    await expect(library.dialog.getByRole('button', { name: 'Close dialog', exact: true })).toBeDisabled();
    await expect(library.item).toBeDisabled();
    await page.keyboard.press('Escape');
    await expect(library.dialog).toBeVisible();
    await library.dialog.getByRole('button', { name: 'Close', exact: true }).click({ force: true });
    await expect(library.dialog).toBeVisible();
    await library.dialog.getByRole('button', { name: 'Close dialog', exact: true }).click({ force: true });
    await expect(library.dialog).toBeVisible();
    await expect(text.click({ timeout: 350 })).rejects.toThrow(/Timeout|intercepts pointer events/i);
    await expect(text).toHaveText('The unsaved source draft must remain protected');
    expect(new URL(page.url()).searchParams.get('project')).toBe(saved.id);
    expect((await request.get(`/api/projects/${postedId}`)).status()).toBe(404);

    release();
    await expect(library.dialog).toBeHidden();
    await expect.poll(() => new URL(page.url()).searchParams.get('project')).toBe(postedId);
    expect(new URL(page.url()).searchParams.get('project')).toBe(postedId);
    const copy = await readProject(request, postedId);
    expect(copy.revision).toBe(1);
    expect(copy.nodes.filter(node => node.kind === 'module')).toHaveLength(1);
    expect(copy.nodes).toHaveLength(definition.project.nodes.length);
    expect(await readProject(request, saved.id)).toEqual(saved);
    const recovery = await (await request.get(`/api/projects/${saved.id}/recovery`)).json() as { project: MeridianProject };
    expect(recovery.project.nodes.find(node => node.id === note.id)?.text).toBe('The unsaved source draft must remain protected');
    expect(await (await request.get(`/api/modules/${definition.id}`)).json()).toEqual(definition);
  } finally {
    release();
    await page.unroute('**/api/projects');
  }
});

test('saving a node with a long title starts with a valid editable module name without truncating its content', async ({ page, request }) => {
  const project = newProject('Long module name preflight');
  const longTitle = 'A detailed module description '.repeat(8).trim();
  expect(longTitle.length).toBeGreaterThan(160);
  const node = addNode(project, project.entryCanvasId, 'text', longTitle);
  Object.assign(node, { x: 100, y: 100, width: 420, height: 220, text: 'The original node title and content are preserved inside the packing node.' });
  const saved = await create(request, commitSnapshots(project));
  await page.goto(`/?project=${saved.id}`);
  await selectNode(page, node.id);
  await page.getByRole('button', { name: 'Save as module', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Save reusable module', exact: true });
  const name = dialog.getByLabel('Name', { exact: true });
  await expect(name).toHaveValue(longTitle.slice(0, 160));
  await expect(name).toHaveAttribute('maxlength', '160');
  await expect(dialog.getByRole('button', { name: 'Save module', exact: true })).toBeEnabled();
  await expect(dialog.locator('.module-stats')).toContainText('2 nodes');
  const revisedName = `Concise reusable note ${saved.id.slice(0, 8)}`;
  await name.fill(revisedName);
  await dialog.getByRole('button', { name: 'Save module', exact: true }).click();
  await expect(dialog).toBeHidden();
  const catalog = await (await request.get('/api/modules')).json() as { id: string; name: string }[];
  const entry = catalog.find(item => item.name === revisedName);
  expect(entry).toBeDefined();
  const definition = await (await request.get(`/api/modules/${entry!.id}`)).json() as StoredModule;
  expect(definition.name).toBe(revisedName);
  expect(definition.project.nodes.find(item => item.kind === 'text')?.title).toBe(longTitle);
  expect(definition.project.nodes.find(item => item.kind === 'text')?.text).toBe(node.text);
  expect(await readProject(request, saved.id)).toEqual(saved);
});
