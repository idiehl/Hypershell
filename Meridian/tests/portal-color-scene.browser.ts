import { test, expect } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addCanvas, addNode, commitSnapshots, newProject } from '../src/core/model.ts';
let script: string, css: string;
test.beforeAll(async () => {
  script = (await build({ stdin: { contents: "import {canvasLauncher} from './src/canvas-launcher.ts';import {renderScene} from './src/scene.ts';import {nodeAppearance,canvasAppearance} from './src/authoring-ui.ts';window.colorHarness={canvasLauncher,renderScene,nodeAppearance,canvasAppearance};", resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
  css = (await Promise.all(['scene', 'grid', 'gallery-scene', 'backgrounds', 'window-scene', 'canvas-launcher', 'expansion-tray-scene', 'editor', 'inspector-panels'].map(file => readFile(`src/${file}.css`, 'utf8')))).join('\n');
});

test('all four designs tint their complete vector palette without changing geometry, opacity, motion or accessible navigation', async ({ page }, info) => {
  await page.setContent('<style>body{background:#101d29;color:#fff}.meridian-scene{display:grid;grid-template-columns:repeat(4,230px);gap:20px}.sample{height:230px;display:grid;place-items:center;background:#172c33;border:1px solid #56736d}.sample:nth-child(n+5){background:#fff9eb}</style><main class="meridian-scene"></main>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  const result = await page.evaluate(() => {
    const { canvasLauncher } = (window as any).colorHarness, results = [];
    (window as any).colorActivations = 0;
    for (const color of ['#d15377', '#397acd', '#000000', '#ffffff']) for (const style of ['doorway', 'passage', 'wormhole', 'vortex']) {
      const original = canvasLauncher('Open Original', () => {}, { style });
      const button = canvasLauncher('Open Destination', () => (window as any).colorActivations++, { style, color, destinationName: `A ${style} destination` });
      const shape = (target: HTMLElement) => [...target.querySelectorAll('svg *')].map(element => [...element.attributes].filter(attribute => !['id', 'fill', 'stroke', 'stop-color', 'clip-path'].includes(attribute.name)).map(attribute => [attribute.name, attribute.value]));
      const colors = (target: HTMLElement) => [...target.querySelectorAll('svg [fill],svg [stroke],svg [stop-color]')].flatMap(element => ['fill', 'stroke', 'stop-color'].map(name => element.getAttribute(name)).filter(value => value && /^#[0-9a-f]{6}$/i.test(value)));
      results.push({ style, color, unchangedGeometry: JSON.stringify(shape(button)) === JSON.stringify(shape(original)), colorsChanged: JSON.stringify(colors(button)) !== JSON.stringify(colors(original)), tonalColors: new Set(colors(button)).size });
      const section = document.createElement('section'); section.className = 'sample'; section.append(button); button.style.width = '180px'; button.style.height = '180px'; document.querySelector('main')!.append(section);
    }
    return results;
  });
  expect(result).toHaveLength(16); for (const check of result) { expect(check.unchangedGeometry, JSON.stringify(check)).toBe(true); expect(check.colorsChanged).toBe(true); expect(check.tonalColors).toBeGreaterThan(5); }
  const first = page.locator('.ms-canvas-open').first(); await first.hover(); await expect(first).toHaveAttribute('data-portal-rate', '4.0000');
  await expect(page.locator('.ms-canvas-destination')).toHaveText('A doorway destination'); await first.click(); expect(await page.evaluate(() => (window as any).colorActivations)).toBe(1);
  await page.screenshot({ path: info.outputPath('four-designs-four-colors.png'), fullPage: true });
});

test('Appearance swatches use actual inherited colors and resetting a button or workspace preserves unrelated appearance', async ({ page }, info) => {
  const project = newProject('Appearance source QA'), node = addNode(project, project.entryCanvasId, 'canvas-link', 'Own portal'); node.style = { fill: '#18352b', portalStyle: 'vortex' }; project.canvases[0]!.style = { portalColor: '#cc9944', portalStyle: 'wormhole', background: '#101d29' };
  // Appearance uses native UUIDs for accessible field identities. A loopback
  // origin supplies the same secure-context capability as the installed app.
  await page.route('http://127.0.0.1:5438/color-fixture', route => route.fulfill({ contentType: 'text/html', body: '<style>body{background:#182330;color:#e4edf3}main{width:390px}</style><main></main>' }));
  await page.goto('http://127.0.0.1:5438/color-fixture'); await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, id }) => {
    const state: any = { project, id, changes: [], mode: 'node' };
    state.draw = () => {
      const node = state.project.nodes.find((node: any) => node.id === state.id), canvas = state.project.canvases[0], callback = (style: any) => { state.changes.push(structuredClone(style)); if (state.mode === 'node') node.style = style; else canvas.style = style; state.draw(); };
      const panel = state.mode === 'node' ? (window as any).colorHarness.nodeAppearance(node, callback, false, undefined, true, canvas.style) : (window as any).colorHarness.canvasAppearance(canvas, callback);
      panel.open = true; document.querySelector('main')!.replaceChildren(panel);
    };
    (window as any).colorState = state; state.draw();
  }, { project, id: node.id });
  const picker = page.locator('input[type=color][aria-label="Work portal color"]'); await expect(picker).toHaveValue('#cc9944'); await expect(page.getByRole('button', { name: 'Use Work color', exact: true })).toBeDisabled();
  await picker.fill('#a94b87'); await picker.dispatchEvent('change'); await expect(picker).toHaveValue('#a94b87');
  expect(await page.evaluate(() => (window as any).colorState.project.nodes.find((n: any) => n.id === (window as any).colorState.id).style)).toEqual({ fill: '#18352b', portalStyle: 'vortex', portalColor: '#a94b87' });
  await page.getByRole('button', { name: 'Use Work color', exact: true }).click(); await expect(picker).toHaveValue('#cc9944');
  expect(await page.evaluate(() => (window as any).colorState.project.nodes.find((n: any) => n.id === (window as any).colorState.id).style)).toEqual({ fill: '#18352b', portalStyle: 'vortex' });
  await page.evaluate(() => { const state = (window as any).colorState; state.mode = 'canvas'; state.draw(); }); await picker.fill('#487abc'); await picker.dispatchEvent('change');
  await page.getByRole('button', { name: 'Use original colors', exact: true }).click();
  expect(await page.evaluate(() => (window as any).colorState.project.canvases[0].style)).toEqual({ portalStyle: 'wormhole', background: '#101d29' });
  await page.screenshot({ path: info.outputPath('portal-color-appearance.png') });
});

test('ordinary, reference and Gallery launchers render workspace inheritance and independent overrides through the shared scene', async ({ page }) => {
  const project = newProject('Scene colors'), home = project.canvases[0]!, target = addCanvas(project, 'Destination'); home.style = { portalColor: '#cc9944' };
  const source = addNode(project, home.id, 'canvas-link', 'Own red'); Object.assign(source, { x: 60, y: 80, width: 260, height: 210, sizeMode: 'fixed', targetId: target.rootId, style: { portalStyle: 'vortex', portalColor: '#ca4862' } });
  const reference = addNode(project, home.id, 'reference', 'Inherited gold'); Object.assign(reference, { x: 370, y: 80, width: 260, height: 210, sizeMode: 'fixed', targetId: source.id });
  const gallery = addNode(project, home.id, 'container', 'Gallery'); Object.assign(gallery, { x: 690, y: 80, width: 320, height: 330, sizeMode: 'fixed', containerVariant: 'gallery', childLayout: 'contained', collapsed: false });
  const item = addNode(project, home.id, 'canvas-link', 'Own blue'); Object.assign(item, { parentId: gallery.id, targetId: target.rootId, style: { portalColor: '#4582c9' } });
  await page.setContent('<style>body{margin:0}main{width:1450px;height:900px}</style><main></main>'); await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(project => { (window as any).colorSceneProject = project; (window as any).colorHarness.renderScene(document.querySelector('main'), project, project.entryCanvasId, { readonly: true, view: { x: 20, y: 20, scale: 1 } }); }, commitSnapshots(project));
  await expect(page.locator(`[data-node-id="${source.id}"] .ms-canvas-open`)).toHaveAttribute('data-portal-color', '#ca4862');
  await expect(page.locator(`[data-node-id="${reference.id}"] .ms-canvas-open`)).toHaveAttribute('data-portal-color', '#cc9944');
  await expect(page.locator(`[data-gallery-id="${gallery.id}"] .ms-canvas-open`)).toHaveAttribute('data-portal-color', '#4582c9');
  await page.evaluate(({ id, galleryItem }) => { const project = (window as any).colorSceneProject; project.nodes.find((n: any) => n.id === id).style = { portalColor: '#91b65c' }; delete project.nodes.find((n: any) => n.id === galleryItem).style.portalColor; (window as any).colorHarness.renderScene(document.querySelector('main'), project, project.entryCanvasId, { readonly: true, view: { x: 20, y: 20, scale: 1 } }); }, { id: reference.id, galleryItem: item.id });
  await expect(page.locator(`[data-node-id="${reference.id}"] .ms-canvas-open`)).toHaveAttribute('data-portal-color', '#91b65c');
  await expect(page.locator(`[data-gallery-id="${gallery.id}"] .ms-canvas-open`)).toHaveAttribute('data-portal-color', '#cc9944');
});
