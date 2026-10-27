import { test, expect } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';

let script = '', css = '';
test.beforeAll(async () => {
  script = (await build({ stdin: { contents: `
    import {newProject,addNode} from './src/core/model.ts';
    import {renderInspectorPanels,loadInspectorPanel,saveInspectorPanel} from './src/inspector-panels.ts';
    import {createNodeDialog} from './src/creation-ui.ts';
    window.hubShell={newProject,addNode,renderInspectorPanels,loadInspectorPanel,saveInspectorPanel,createNodeDialog};
  `, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
  css = (await Promise.all(['src/scene.css', 'src/editor.css', 'src/right-panel.css', 'src/workspace-tools.css', 'src/creation.css', 'src/inspector-panels.css', 'src/inspector-workspace.css', 'src/application-chrome.css', 'src/node-hub-layout.css'].map(file => readFile(file, 'utf8')))).join('\n');
});

test.beforeEach(async ({ page }) => {
  // Keep a same-origin document for the real optional panel preference, without
  // booting the editor or reading/writing any project in this isolated host.
  await page.goto('/?window=waiting');
  await page.setContent('<main id="fixture"></main>');
  await page.addStyleTag({ content: css });
  await page.addScriptTag({ content: script });
});

test('retired Content preference opens Properties and keeps structural controls and selected Module identity', async ({ page }) => {
  await page.evaluate(() => {
    const app = (window as any).hubShell;
    localStorage.setItem('meridian.inspector-panel', 'content');
    const project = app.newProject('Node Hub controls');
    const node = app.addNode(project, project.entryCanvasId, 'media', 'Zeus');
    const host = document.createElement('aside'); host.className = 'inspector'; document.querySelector('#fixture')!.append(host);
    const render = (selected: any) => {
      host.replaceChildren();
      app.renderInspectorPanels(host, { project, canvasId: project.entryCanvasId, nodeId: selected.id, panel: app.loadInspectorPanel('canvas'), onPanelChange() {}, onJumpToNode() {}, onDeleteNode() {}, onInspectLink() {}, onChangeNodeType() {}, renderPanel(panel: string, body: HTMLElement) {
        if (panel === 'properties') { const input = document.createElement('input'); input.setAttribute('aria-label', 'Role-specific value'); input.value = 'retained'; body.append(input); }
      } });
    };
    (window as any).renderExistingModule = () => render(app.addNode(project, project.entryCanvasId, 'module', 'Existing assembly'));
    render(node);
  });
  const panels = page.getByRole('combobox', { name: 'Side panel', exact: true });
  await expect(panels).toHaveValue('properties');
  expect(await panels.locator('option').allTextContents()).not.toContain('Content');
  expect(await page.getByLabel('Type', { exact: true }).locator('option').allTextContents()).not.toContain('Module');
  await expect(page.getByLabel('Role-specific value')).toHaveValue('retained');
  expect(await page.evaluate(() => localStorage.getItem('meridian.inspector-panel'))).toBe('properties');
  await page.evaluate(() => (window as any).renderExistingModule());
  await expect(page.getByLabel('Type', { exact: true })).toHaveValue('module');
  await expect(page.getByLabel('Type', { exact: true })).toBeDisabled();
});

test('ordinary creation still chooses structural Type and excludes Module without altering a cancelled source', async ({ page }) => {
  await page.evaluate(() => {
    const app = (window as any).hubShell, project = app.newProject('Hub creation');
    (window as any).sourceBefore = JSON.stringify(project);
    (window as any).creationProject = project;
    void app.createNodeDialog(project, { canvasId: project.entryCanvasId, parentId: project.canvases[0].rootId, x: 100, y: 100 }).then((result: unknown) => { (window as any).creationResult = result; });
  });
  const dialog = page.getByRole('dialog', { name: 'New Node', exact: true });
  await expect(dialog).toBeVisible();
  const type = dialog.getByLabel('Type', { exact: true });
  const options = await type.locator('option').allTextContents();
  expect(options).toContain('Media'); expect(options).toContain('Container'); expect(options).not.toContain('Module');
  await type.selectOption('container'); await expect(dialog.getByLabel('Display Pattern', { exact: true })).toBeVisible();
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(await page.evaluate(() => JSON.stringify((window as any).creationProject) === (window as any).sourceBefore)).toBe(true);
  await expect.poll(() => page.evaluate(() => (window as any).creationResult)).toBeNull();
});

test('inspector stays left, toolbar starts after it, and selected Hub occupies the right without narrowing the header', async ({ page }, info) => {
  await page.setViewportSize({ width: 1500, height: 950 });
  await page.evaluate(() => {
    document.querySelector('#fixture')!.innerHTML = '<div class="workspace is-node-hub-layout has-node-hub" style="--inspector-width:310px"><header class="app-header">Atlas Meridian</header><main class="canvas-column"><div class="canvas-toolbar"><button>+</button></div><div class="canvas-stage"></div></main><aside class="inspector is-open"><button aria-label="Close side panel">←</button></aside><div class="inspector-divider"></div><aside class="node-hub"><button aria-label="Close Node Hub">×</button></aside></div>';
  });
  const inspector = page.locator('.inspector'), center = page.locator('.canvas-column'), hub = page.locator('.node-hub');
  const a = (await inspector.boundingBox())!, b = (await center.boundingBox())!, c = (await hub.boundingBox())!, toolbar = (await page.locator('.canvas-toolbar').boundingBox())!;
  expect(a.x).toBeCloseTo(0, 0); expect(b.x).toBeGreaterThanOrEqual(a.x + a.width - 1); expect(toolbar.x).toBeCloseTo(b.x, 0);
  expect(c.x).toBeGreaterThanOrEqual(b.x + b.width - 1); expect(c.x + c.width).toBeCloseTo(1500, 0);
  expect((await page.locator('.app-header').boundingBox())!.width).toBeCloseTo(1500, 0);
  await page.screenshot({ path: info.outputPath('node-hub-desktop-layout.png') });
  await page.locator('.workspace').evaluate(el => el.classList.add('is-inspector-closed'));
  await expect(inspector).toBeHidden(); expect((await center.boundingBox())!.x).toBeCloseTo(0, 0); expect((await hub.boundingBox())!.x + (await hub.boundingBox())!.width).toBeCloseTo(1500, 0);
  await page.setViewportSize({ width: 760, height: 900 });
  await page.locator('.workspace').evaluate(el => el.classList.remove('is-inspector-closed'));
  await expect(inspector).toBeVisible(); await expect(hub).toBeVisible();
  expect((await inspector.boundingBox())!.x).toBeCloseTo(0, 0); expect((await hub.boundingBox())!.x + (await hub.boundingBox())!.width).toBeCloseTo(760, 0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(760);
  await expect(page.getByRole('button', { name: 'Close side panel', exact: true })).toBeInViewport();
  await expect(page.getByRole('button', { name: 'Close Node Hub', exact: true })).toBeInViewport();
  await page.screenshot({ path: info.outputPath('node-hub-narrow-layout.png') });
});
