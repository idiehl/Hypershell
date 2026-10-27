import { workspaceAction } from './ui-helpers.ts';
import { test, expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { normalizeContainerLayouts } from '../src/core/grid.ts';
import { OPERATION_DEFINITIONS } from '../src/core/operations.ts';
import { validateProject } from '../src/core/rules.ts';
import type { MeridianNode, MeridianProject, NodeKind } from '../src/core/types.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
const group = (dialog: Locator, name: string) => dialog.getByRole('group', { name, exact: true });
const checkbox = (container: Locator, name: string) => container.getByRole('checkbox', { name, exact: true });
async function read(request: APIRequestContext, id: string): Promise<MeridianProject> { return (await request.get(`/api/projects/${id}`)).json(); }
async function save(page: Page, request: APIRequestContext, id: string) {
  const before = await read(request, id); await workspaceAction(page,'Save');
  await expect(page.locator('.save-state')).toHaveText(`Saved · r${before.revision + 1}`); return read(request, id);
}
async function openRules(page: Page, id?: string) {
  if (id) await card(page, id).locator('.ms-node-title').click();
  await page.getByRole('combobox',{name:'Side panel',exact:true}).selectOption('relations');const inspector = page.locator('.inspector');
  const disclosure = inspector.locator('details').filter({ has: page.locator('summary').filter({ hasText: /^Inherited rules$/ }) });
  if (await disclosure.count() && await disclosure.getAttribute('open') === null) await disclosure.locator('summary').click();
  await inspector.getByRole('button', { name: 'Edit inherited rules', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Edit inherited rules', exact: true });
  await expect(dialog).toBeVisible(); return dialog;
}
async function seed(page: Page, request: APIRequestContext, project: MeridianProject) {
  expect(validateProject(project)).toEqual([]);
  const response = await request.post('/api/projects', { data: commitSnapshots(project) }); expect(response.ok()).toBeTruthy();
  const saved = await response.json() as MeridianProject; await page.goto(`/?project=${saved.id}`); return saved;
}
function base(title: string) {
  const project = newProject(title), canvasId = project.entryCanvasId;
  const parent = addNode(project, canvasId, 'text', 'Rule-bearing branch');
  Object.assign(parent, { x: 140, y: 160, width: 300, height: 185, collapsed: true, childLayout: 'spatial', text: 'Editing descendant permissions must preserve this complete branch.' });
  const child = (kind: NodeKind, title: string, owner: MeridianNode = parent) => {
    const node = addNode(project, canvasId, kind, title);
    Object.assign(node, { parentId: owner.id, x: 500, y: 200 + project.nodes.length * 150, childLayout: 'spatial' }); return node;
  };
  return { project, parent, child };
}

test('rule preview distinguishes affected hidden descendants from explicitly masked descendants and applies in one undo step', async ({ page, request }, info) => {
  const { project, parent, child } = base('Preview permissions before applying');
  const note = child('text', 'Hidden account'); note.text = 'This original content must survive a rule change.';
  const direct = child('joint', 'Inherited AND'); direct.operation = 'AND';
  const narrowed = child('container', 'Already limited branch'); narrowed.rules = { operations: ['AND'], linkKinds: ['visual'] };
  const grandchild = child('joint', 'Already limited AND', narrowed); grandchild.operation = 'AND';
  project.links.push({ id: crypto.randomUUID(), canvasId: project.entryCanvasId, sourceId: note.id, targetId: direct.id, parentId: parent.id, kind: 'visual', relation: 'generic', label: 'Retained visual account' });
  const baseline = await seed(page, request, project);
  // Loading upgrades legacy formal containers on the independent editing draft.
  // A rule edit must preserve that displayed graph, while Cancel still leaves
  // the original stored baseline untouched in the separate cancellation tests.
  const displayedBaseline = normalizeContainerLayouts(baseline);
  const initialCamera = await page.locator('.ms-world').evaluate(node => (node as HTMLElement).style.transform);
  const dialog = await openRules(page, parent.id);
  for (const label of ['Inherit node types from parent', 'Inherit connection types from parent', 'Inherit operations from parent']) await expect(checkbox(dialog, label)).toBeChecked();
  await expect(dialog.getByRole('heading', { name: 'Affected descendants', exact: true })).toBeVisible();
  await expect(dialog.getByRole('heading', { name: 'Conflicts', exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Apply rules', exact: true })).toBeDisabled();
  await checkbox(dialog, 'Inherit operations from parent').uncheck(); await checkbox(group(dialog, 'Allowed operations'), 'OR').uncheck();
  await checkbox(dialog, 'Inherit connection types from parent').uncheck();
  const links = group(dialog, 'Allowed connection types'); await checkbox(links, 'Boolean').uncheck(); await checkbox(links, 'Numeric').uncheck();
  const directRow = dialog.locator(`[data-rule-node-id="${direct.id}"]`);
  await expect(directRow).toContainText('Inherited AND'); await expect(directRow).toContainText(/hidden/i); await expect(directRow).toContainText('OR');
  await expect(dialog.locator(`[data-rule-node-id="${grandchild.id}"]`)).toHaveCount(0);
  await expect(dialog.getByRole('status')).toContainText(/changed permissions/); await expect(dialog.getByRole('status')).toContainText(/unchanged/);
  await expect(dialog.getByRole('button', { name: 'Apply rules', exact: true })).toBeEnabled();
  await page.screenshot({ path: info.outputPath('rule-preview-hidden-descendants.png') });
  await dialog.getByRole('button', { name: 'Apply rules', exact: true }).click(); await expect(dialog).toHaveCount(0);
  await expect(card(page, note.id)).toHaveCount(0); await expect(card(page, parent.id)).toHaveAttribute('aria-expanded', 'false');
  expect(await page.locator('.ms-world').evaluate(node => (node as HTMLElement).style.transform)).toBe(initialCamera);
  const applied = await save(page, request, project.id);
  const expectedRules = { linkKinds: ['visual'], operations: OPERATION_DEFINITIONS.map(definition => definition.operation).filter(operation => operation !== 'OR') };
  expect(applied.nodes.find(node => node.id === parent.id)?.rules).toEqual(expectedRules);
  for (const node of [note, direct, narrowed, grandchild]) expect(applied.nodes.find(candidate => candidate.id === node.id)).toEqual(displayedBaseline.nodes.find(candidate => candidate.id === node.id));
  expect(applied.links).toEqual(baseline.links); expect(applied.assets).toEqual(baseline.assets);
  await page.getByRole('button', { name: 'Undo', exact: true }).click(); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  const undoneDialog = await openRules(page); await expect(checkbox(undoneDialog, 'Inherit operations from parent')).toBeChecked();
  await undoneDialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  const redone = await save(page, request, project.id); expect(redone.nodes.find(node => node.id === parent.id)?.rules).toEqual(expectedRules);
  await page.reload(); const reopened = await openRules(page, parent.id);
  await expect(checkbox(reopened, 'Inherit operations from parent')).not.toBeChecked(); await expect(checkbox(group(reopened, 'Allowed operations'), 'OR')).not.toBeChecked();
  await expect(checkbox(group(reopened, 'Allowed operations'), 'AND')).toBeChecked();
  await expect(dialog.getByRole('button', { name: 'Apply rules', exact: true })).toBeDisabled();
});

test('conflicts identify existing hidden content, a joint operation and a connection crossing the edited branch', async ({ page, request }, info) => {
  const { project, parent, child } = base('Conflicting rules cannot erase existing work');
  const image = child('image', 'Evidence photograph'), text = child('text', 'Witness transcript');
  const joint = child('joint', 'Existing OR joint'); joint.operation = 'OR';
  const input = child('input', 'Inside Boolean input'); input.value = true;
  const outside = addNode(project, project.entryCanvasId, 'output', 'Outside Boolean result'); Object.assign(outside, { x: 660, y: 170, width: 240, height: 140 });
  const crossing = { id: crypto.randomUUID(), canvasId: project.entryCanvasId, parentId: project.canvases[0]!.rootId, sourceId: input.id, targetId: outside.id, targetPort: 0, kind: 'functional' as const, relation: 'causal' as const, label: 'Cross-branch signal' };
  project.links.push(crossing);
  const baseline = await seed(page, request, project), dialog = await openRules(page, parent.id);
  await checkbox(dialog, 'Inherit node types from parent').uncheck();
  const nodes = group(dialog, 'Allowed node types'); for (const name of ['Image', 'Text', 'Operation joint']) await checkbox(nodes, name).uncheck();
  await checkbox(dialog, 'Inherit operations from parent').uncheck(); await checkbox(group(dialog, 'Allowed operations'), 'OR').uncheck();
  await checkbox(dialog, 'Inherit connection types from parent').uncheck(); await checkbox(group(dialog, 'Allowed connection types'), 'Boolean').uncheck();
  for (const node of [image, text, joint]) {
    const rows = dialog.locator(`[data-rule-node-id="${node.id}"]`); expect(await rows.count()).toBeGreaterThan(0); await expect(rows.first()).toContainText(node.title);
  }
  const connectionRows = dialog.locator(`[data-rule-link-id="${crossing.id}"]`);
  expect(await connectionRows.count()).toBeGreaterThan(0); await expect(connectionRows.first()).toContainText(/Cross-branch signal|Inside Boolean input/);
  await expect(dialog).toContainText('Existing OR joint'); await expect(dialog.getByRole('button', { name: 'Apply rules', exact: true })).toBeDisabled();
  const conflictSearch = dialog.getByRole('searchbox', { name: 'Find conflicts', exact: true });
  await conflictSearch.fill(outside.title);
  await expect(connectionRows).toHaveCount(1); await expect(connectionRows).toContainText(outside.title);
  await expect(dialog.locator('.rule-conflicts').locator(`[data-rule-node-id="${image.id}"]`)).toHaveCount(0);
  await expect(dialog.getByRole('button', { name: 'Apply rules', exact: true })).toBeDisabled();
  await conflictSearch.fill('No matching account'); await expect(dialog.locator('.rule-conflicts')).toContainText('No matching conflicts.');
  await conflictSearch.clear(); await expect(dialog.locator('.rule-conflicts').locator(`[data-rule-node-id="${image.id}"]`)).toHaveCount(1);
  await page.screenshot({ path: info.outputPath('rule-conflicts-crossing-link.png') });
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  expect(await read(request, project.id)).toEqual(baseline);
  await expect(page.locator('.save-state')).toHaveText('Saved · r1'); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  await expect(card(page, image.id)).toHaveCount(0); await expect(card(page, outside.id)).toBeVisible();
});

test('ancestor limits stay locked and existing local choices are preserved before explicitly returning to inheritance', async ({ page, request }) => {
  const { project, parent, child } = base('Respect ancestor permissions');
  const root = project.nodes.find(node => node.id === project.canvases[0]!.rootId)!;
  root.rules = { nodeKinds: ['text', 'container'], linkKinds: ['visual'], operations: ['AND'] };
  parent.rules = { nodeKinds: ['text'], linkKinds: [], operations: [] };
  const note = child('text', 'Allowed child text');
  const baseline = await seed(page, request, project), dialog = await openRules(page, parent.id);
  await expect(checkbox(dialog, 'Inherit node types from parent')).not.toBeChecked();
  const nodes = group(dialog, 'Allowed node types');
  await expect(checkbox(nodes, 'Text')).toBeChecked(); await expect(checkbox(nodes, 'Container')).not.toBeChecked();
  await expect(checkbox(nodes, 'Container')).toBeEnabled(); await expect(checkbox(nodes, 'Image')).toBeDisabled();
  const connections = group(dialog, 'Allowed connection types'); await expect(checkbox(connections, 'Visual')).not.toBeChecked();
  await expect(checkbox(connections, 'Boolean')).toBeDisabled(); await expect(checkbox(connections, 'Numeric')).toBeDisabled();
  const operations = group(dialog, 'Allowed operations'); await expect(checkbox(operations, 'AND')).toBeEnabled(); await expect(checkbox(operations, 'AND')).not.toBeChecked(); await expect(checkbox(operations, 'OR')).toBeDisabled();
  await expect(dialog.getByRole('button', { name: 'Apply rules', exact: true })).toBeDisabled();
  await checkbox(dialog, 'Inherit node types from parent').check();
  await expect(dialog.locator(`[data-rule-node-id="${note.id}"]`)).toContainText('Container');
  await expect(dialog.getByRole('button', { name: 'Apply rules', exact: true })).toBeEnabled();
  await dialog.getByRole('button', { name: 'Apply rules', exact: true }).click();
  const saved = await save(page, request, project.id);
  expect(saved.nodes.find(node => node.id === parent.id)?.rules).toEqual({ linkKinds: [], operations: [] });
  expect(saved.nodes.find(node => node.id === root.id)?.rules).toEqual(baseline.nodes.find(node => node.id === root.id)?.rules);
  expect(saved.nodes.find(node => node.id === note.id)).toEqual(baseline.nodes.find(node => node.id === note.id));
});

test('canceling a valid draft leaves source and undo history untouched and rule controls remain usable on a narrow screen', async ({ page, request }, info) => {
  const { project, parent, child } = base('Accessible rule editing'); child('text', 'Hidden note');
  const baseline = await seed(page, request, project), dialog = await openRules(page, parent.id);
  for (const name of ['Allowed node types', 'Allowed connection types', 'Allowed operations']) await expect(group(dialog, name)).toBeVisible();
  await checkbox(dialog, 'Inherit node types from parent').uncheck();
  const image = checkbox(group(dialog, 'Allowed node types'), 'Image'); await image.focus(); await page.keyboard.press('Space');
  await expect(image).not.toBeChecked(); await expect(dialog.getByRole('button', { name: 'Apply rules', exact: true })).toBeEnabled();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(dialog).toBeVisible();
  const bounds = await dialog.boundingBox(); expect(bounds).not.toBeNull();
  expect(bounds!.x).toBeGreaterThanOrEqual(0); expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(391);
  const overflow = await dialog.evaluate(element => element.scrollWidth > element.clientWidth + 1); expect(overflow).toBe(false);
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: info.outputPath('rules-narrow-screen.png') });
  await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0);
  await page.setViewportSize({ width: 1500, height: 950 });
  expect(await read(request, project.id)).toEqual(baseline);
  await expect(page.locator('.save-state')).toHaveText('Saved · r1'); await expect(page.getByRole('button', { name: 'Undo', exact: true })).toBeDisabled();
  const reopened = await openRules(page); await expect(checkbox(reopened, 'Inherit node types from parent')).toBeChecked();
  await expect(reopened.getByRole('button', { name: 'Apply rules', exact: true })).toBeDisabled();
  await reopened.getByRole('button', { name: 'Cancel', exact: true }).click();
});
