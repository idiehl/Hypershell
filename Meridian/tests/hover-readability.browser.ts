import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { newProject, addNode } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

let source = '', css = '';
test.beforeAll(async () => {
  source = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';import {mountNodeReadability} from './src/hover-readability.ts';window.readabilityAPI={renderScene,mountNodeReadability};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
  css = (await Promise.all(['scene', 'grid', 'gallery-scene', 'backgrounds', 'window-scene', 'canvas-launcher', 'expansion-tray-scene', 'cluster-button', 'hover-readability'].map(name => readFile(`src/${name}.css`, 'utf8')))).join('\n');
});
function fixture() {
  const project = newProject('Hover readability'), node = addNode(project, project.entryCanvasId, 'media', 'Namespace');
  Object.assign(node, { x: 300, y: 200, width: 220, height: 90, sizeMode: 'fixed', style: { titleSize: 20, fontSize: 20, padding: 8, fill: '#193a49', textColor: '#eef6f6' } });
  return { project, node };
}
async function mount(page: Page, project: MeridianProject, scale: number, readonly = true) {
  await page.setContent('<style>html,body{margin:0}#scene{height:700px;width:1000px}</style><main id="scene"></main>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: source });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.evaluate(({ project, scale, readonly }) => {
    const api = (window as any).readabilityAPI, host = document.querySelector('#scene'), state: any = { project, before: JSON.stringify(project), moves: [], selected: [], scale };
    api.renderScene(host, project, project.entryCanvasId, { view: { x: 20, y: 20, scale }, readonly, onMove: (id: string, x: number, y: number) => state.moves.push({ id, x, y }), onSelection: (id: string) => state.selected.push(id) });
    const viewport = host!.querySelector('.ms-viewport'), cards = new Map([...host!.querySelectorAll<HTMLElement>('.ms-node')].map(card => [card.dataset.nodeId, card]));
    // Model a large scene index without constructing offscreen DOM. Pointer
    // movement must use only get(id), never scan these 50,000 other entries.
    const oneCard = cards.values().next().value!; for (let index = 0; index < 50000; index++) cards.set('offscreen-' + index, oneCard);
    let scans = 0;
    const indexed = new Proxy(cards, { get(target, property) { if (['entries', 'values', Symbol.iterator].includes(property as any)) { scans++; throw new Error('Hover must not scan the card collection.'); } const result = Reflect.get(target, property, target); return typeof result === 'function' ? result.bind(target) : result; } });
    state.mount = api.mountNodeReadability({ host, viewport, cards: indexed, getScale: () => state.scale, enabled: () => !state.disabled }); state.scans = () => scans; state.indexSize = cards.size;
    state.fontPx = (id: string) => { const card = cards.get(id)!, text = card.querySelector<HTMLElement>('.ms-node-title')!; let factor = 1; for (let e: HTMLElement | null = text; e && e !== viewport; e = e.parentElement) { const style = getComputedStyle(e); if (style.transform !== 'none') { const m = new DOMMatrixReadOnly(style.transform); factor *= Math.min(Math.hypot(m.a, m.b), Math.hypot(m.c, m.d)); } if (style.scale !== 'none') factor *= Number(style.scale); } return parseFloat(getComputedStyle(text).fontSize) * factor; };
    (window as any).readabilityState = state;
  }, { project, scale, readonly });
}

test('tiny rendered text reaches the same16px target on the genuine card, leaves geometry and media unchanged, and restores on exit', async ({ page }, info) => {
  const f = fixture(), errors: string[] = []; page.on('pageerror', e => errors.push(e.message)); await mount(page, f.project, .1);
  const card = page.locator(`[data-node-id="${f.node.id}"]`); const original = (await card.boundingBox())!;
  await card.hover(); await expect(card).toHaveClass(/ms-readability-expanded/);
  const enlarged = (await card.boundingBox())!; expect(enlarged.width).toBeGreaterThan(original.width);
  expect(await page.evaluate(id => (window as any).readabilityState.fontPx(id), f.node.id)).toBeCloseTo(16, 1);
  expect(await page.locator('.ms-node').count()).toBe(1);
  expect(await page.evaluate(() => (window as any).readabilityState.scans())).toBe(0);
  expect(await page.evaluate(() => (window as any).readabilityState.indexSize)).toBe(50001);
  expect(await page.evaluate(() => JSON.stringify((window as any).readabilityState.project) === (window as any).readabilityState.before)).toBe(true);
  await page.screenshot({ path: info.outputPath('hover-readable-node.png') });
  await page.mouse.move(960, 660); await expect(card).not.toHaveClass(/ms-readability-expanded/);
  expect((await card.boundingBox())!.width).toBeCloseTo(original.width, 3); expect(errors).toEqual([]);
});

test('readable cards are unchanged and keyboard focus can inspect a tiny card without a mouse', async ({ page }) => {
  const f = fixture(); f.node.style!.titleSize = 40; f.node.style!.fontSize = 40; await mount(page, f.project, 1);
  const card = page.locator(`[data-node-id="${f.node.id}"]`); await card.hover(); await expect(card).not.toHaveClass(/ms-readability-expanded/);
  await mount(page, f.project, .08); await card.focus(); await expect(card).toHaveClass(/ms-readability-expanded/);
  await page.keyboard.press('Escape'); await expect(card).not.toHaveClass(/ms-readability-expanded/);
});

test('real hyperlink controls remain clickable at their enlarged location and plain drag restores the magnification', async ({ page }) => {
  const f = fixture(); f.node.contentItems = [{ id: 'destination', type: 'link', url: 'https://example.test', label: 'Record' }];
  await mount(page, f.project, .12, false);
  const card = page.locator(`[data-node-id="${f.node.id}"]`); await card.hover(); await expect(card).toHaveClass(/ms-readability-expanded/);
  const link = card.locator('a'); await link.evaluate(a => a.addEventListener('click', e => { e.preventDefault(); (window as any).readabilityState.opened = ((window as any).readabilityState.opened ?? 0) + 1; }));
  await link.click(); expect(await page.evaluate(() => (window as any).readabilityState.opened)).toBe(1);
  await page.mouse.move(950, 660); await card.hover(); const box = (await card.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + Math.min(8, box.height / 4)); await page.mouse.down();
  await expect(card).not.toHaveClass(/ms-readability-expanded/);
  await page.mouse.move(box.x + box.width / 2 + 40, box.y + Math.min(8, box.height / 4) + 25, { steps: 3 }); await page.mouse.up();
  expect(await page.evaluate(() => (window as any).readabilityState.moves.length)).toBe(1);
});

test('viewport-edge enlargement fits inside the scene and text/region/active gesture states suppress it', async ({ page }) => {
  const f = fixture(); f.node.x = 7800; f.node.y = 50; await mount(page, f.project, .12);
  const card = page.locator(`[data-node-id="${f.node.id}"]`); await card.hover(); await expect(card).toHaveClass(/ms-readability-expanded/);
  const box = (await card.boundingBox())!; expect(box.x).toBeGreaterThanOrEqual(11); expect(box.y).toBeGreaterThanOrEqual(11); expect(box.x + box.width).toBeLessThanOrEqual(989); expect(box.y + box.height).toBeLessThanOrEqual(689);
  await page.evaluate(() => { const state = (window as any).readabilityState; state.disabled = true; state.mount.refresh(); }); await expect(card).not.toHaveClass(/ms-readability-expanded/);
  await page.evaluate(() => { (window as any).readabilityState.disabled = false; document.querySelector<HTMLElement>('.ms-viewport')!.dataset.authoringMode = 'text'; });
  await card.hover(); await expect(card).not.toHaveClass(/ms-readability-expanded/);
  await page.evaluate(() => { const viewport = document.querySelector<HTMLElement>('.ms-viewport')!; viewport.dataset.authoringMode = 'select'; viewport.classList.add('ms-region-mode'); });
  await card.hover(); await expect(card).not.toHaveClass(/ms-readability-expanded/);
});

test('expanded container workspaces stay fixed and the same native audio player keeps advancing across hover', async ({ page }) => {
  const f = fixture(), length = 8 * 8000 * 2, wav = Buffer.alloc(44 + length);
  wav.write('RIFF', 0); wav.writeUInt32LE(wav.length - 8, 4); wav.write('WAVEfmt ', 8); wav.writeUInt32LE(16, 16); wav.writeUInt16LE(1, 20); wav.writeUInt16LE(1, 22); wav.writeUInt32LE(8000, 24); wav.writeUInt32LE(16000, 28); wav.writeUInt16LE(2, 32); wav.writeUInt16LE(16, 34); wav.write('data', 36); wav.writeUInt32LE(length, 40);
  f.project.assets.push({ id: 'playing-audio', name: 'Audio.wav', mime: 'audio/wav', base64: wav.toString('base64') }); f.node.contentItems = [{ id: 'audio', type: 'asset', assetId: 'playing-audio' }];
  const container = addNode(f.project, f.project.entryCanvasId, 'container', 'Expanded workspace'); Object.assign(container, { x: 2500, y: 200, width: 300, height: 180, collapsed: false, sizeMode: 'fixed' });
  await mount(page, f.project, .1);
  const frame = page.locator(`[data-node-id="${container.id}"]`); await frame.hover(); await expect(frame).not.toHaveClass(/ms-readability-expanded/);
  const player = page.locator(`[data-node-id="${f.node.id}"] audio`); await player.evaluate(async (element: HTMLAudioElement) => { element.currentTime = 1.25; element.volume = .35; element.muted = true; (window as any).hoverPlayer = element; await element.play(); });
  const before = await player.evaluate((element: HTMLAudioElement) => element.currentTime);
  const card = page.locator(`[data-node-id="${f.node.id}"]`); await card.hover(); await expect(card).toHaveClass(/ms-readability-expanded/);
  await page.waitForTimeout(100); await page.mouse.move(950, 600); await expect(card).not.toHaveClass(/ms-readability-expanded/);
  expect(await player.evaluate(element => element === (window as any).hoverPlayer)).toBe(true);
  expect(await player.evaluate((element: HTMLAudioElement) => element.currentTime)).toBeGreaterThan(before);
  expect(await player.evaluate((element: HTMLAudioElement) => ({ volume: element.volume, muted: element.muted, paused: element.paused }))).toEqual({ volume: .35, muted: true, paused: false });
});
