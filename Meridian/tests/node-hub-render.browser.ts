import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';
import type { MeridianProject, NodeContentItem } from '../src/core/types.ts';
import { nodeContentLayout } from '../src/core/sizing.ts';
import { documentNodeAsset } from '../src/core/document-node.ts';

let script: string, css: string;
test.beforeAll(async () => {
  css = (await Promise.all(['scene', 'grid', 'gallery-scene', 'window-scene', 'expansion-tray-scene', 'canvas-launcher', 'backgrounds', 'cluster-button'].map(name => readFile(`src/${name}.css`, 'utf8')))).join('\n');
  const result = await build({ stdin: { contents: `import {renderScene,getAsset,prepareSceneMediaForRemount} from './src/scene.ts';import {mountHubItemPreview,openHubItem,appendReadonlyNodeHub,hubContentAsset} from './src/hub-content-view.ts';import {revealDocumentNode} from './src/core/document-node.ts';window.hubRenderAPI={renderScene,getAsset,prepareSceneMediaForRemount,mountHubItemPreview,openHubItem,appendReadonlyNodeHub,hubContentAsset,revealDocumentNode};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' });
  script = result.outputFiles[0]!.text;
});
const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==';
function fixture() {
  const project = newProject('Hub rendering fixture'), node = addNode(project, project.entryCanvasId, 'media', 'Zeus');
  Object.assign(node, { x: 80, y: 70, width: 320, height: 180, sizeMode: 'fixed', text: 'Dormant legacy body', showTitle: false });
  project.assets.push({ id: 'portrait', name: 'Portrait.png', mime: 'image/png', base64: png }, { id: 'table', name: 'Names.csv', mime: 'text/csv', base64: Buffer.from('Name,Role\nZeus,King\nHera,Queen').toString('base64') });
  const items: NodeContentItem[] = [{ id: 'visible', type: 'text', text: 'Visible body' }, { id: 'portrait-item', type: 'asset', assetId: 'portrait' }, { id: 'table-item', type: 'asset', assetId: 'table' }, { id: 'link-item', type: 'link', url: 'https://example.test/story', label: 'Story' }];
  node.contentItems = items; return { project, node, items };
}
async function mount(page: Page, f: ReturnType<typeof fixture>) {
  const errors: string[] = []; page.on('pageerror', e => errors.push(e.message));
  await page.route(/^https?:/, route => route.abort());
  await page.setContent('<style>html,body{margin:0;background:#101b27;color:#e4eef5}#scene{width:1050px;height:700px}#hub{position:absolute;left:1070px;top:0;width:350px;max-height:850px;overflow:auto}</style><main id="scene"></main><aside id="hub"></aside>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(({ project, nodeId }) => {
    const api = (window as any).hubRenderAPI, h: any = { project, nodeId, hub: undefined, assets: [] };
    h.node = () => h.project.nodes.find((n: any) => n.id === h.nodeId);
    h.draw = () => api.renderScene(document.querySelector('#scene'), h.project, h.project.entryCanvasId, { view: { x: 0, y: 0, scale: 1 }, readonly: true, onAssetOpen: (id: string) => h.assets.push(id), onToggle: (id: string, _field: string, value: boolean) => { h.project = api.revealDocumentNode(h.project, id, !value); h.draw(); } });
    h.showHub = () => { h.hub?.dispose(); const host = document.querySelector('#hub'); host!.replaceChildren(); h.hub = api.appendReadonlyNodeHub(host, h.project, h.node()); };
    h.draw(); (window as any).hubRenderTest = h;
  }, { project: f.project, nodeId: f.node.id });
  return errors;
}

test('only first Hub item is drawn and hiding a title keeps its stored identity and content', async ({ page }) => {
  const f = fixture(), before = structuredClone(f.project), errors = await mount(page, f);
  const card = page.locator(`.ms-node[data-node-id="${f.node.id}"]`);
  await expect(card.locator('.ms-node-title')).toHaveCount(0); await expect(card.locator('.ms-text')).toHaveText('Visible body');
  await expect(card.locator('img,a,video,audio,.mdv-viewer')).toHaveCount(0); await expect(card).not.toContainText('Dormant legacy body');
  expect(await page.evaluate(() => (window as any).hubRenderTest.project)).toEqual(before);
  const hidden = nodeContentLayout(f.project, f.node); expect(hidden.headerHeight).toBe(0);
  const shown = nodeContentLayout(f.project, { ...f.node, showTitle: true }); expect(shown.headerHeight).toBeGreaterThan(0);
  await page.evaluate(() => { const h = (window as any).hubRenderTest; h.node().contentItems.unshift(h.node().contentItems.splice(1, 1)[0]); h.draw(); });
  await expect(card.locator('img')).toHaveCount(1); await expect(card.locator('.ms-text')).toHaveCount(0); await expect(card.locator('.ms-node-title')).toHaveCount(0); expect(errors).toEqual([]);
});

test('the first document attachment drives its reader instead of dormant legacy media', async ({ page }) => {
  const f = fixture(); f.node.assetId = 'portrait'; f.node.contentItems = [f.items[2]!, f.items[1]!];
  expect(documentNodeAsset(f.project, f.node)?.id).toBe('table'); await mount(page, f);
  const card = page.locator(`.ms-node[data-node-id="${f.node.id}"]`); await expect(card.locator('img')).toHaveCount(0);
  await card.locator('[data-document-action="read"]').click(); await expect(card.locator('.mdv-viewer')).toBeVisible(); await expect(card.locator('.mdv-viewer')).toContainText('Zeus');
  await expect(card.locator('.ms-node-title')).toHaveCount(0);
});

test('readonly Hub exposes secondary media, a usable table and larger views without modifying authored data', async ({ page }) => {
  const f = fixture(), before = structuredClone(f.project); await mount(page, f);
  await page.evaluate(() => (window as any).hubRenderTest.showHub());
  const hub = page.locator('#hub'); await expect(hub.locator('.nh-content-section')).toHaveCount(4); await expect(hub.locator('.nh-image-preview')).toHaveCount(1);
  await expect(hub.locator('.mdv-viewer')).toContainText('Hera'); await expect(hub.getByRole('link', { name: 'Story', exact: true })).toHaveAttribute('href', 'https://example.test/story');
  await hub.getByRole('button', { name: 'Open Names.csv in a larger view', exact: true }).click(); await expect(page.locator('dialog.nh-popout .mdv-viewer')).toContainText('Zeus');
  await page.getByRole('button', { name: 'Close content viewer', exact: true }).click(); await expect(page.locator('dialog.nh-popout')).toHaveCount(0);
  expect(await page.evaluate(() => (window as any).hubRenderTest.project)).toEqual(before);
});

test('saved secondary Hub assets keep pinned bytes and explicit Reference content overrides only its visual slot', async ({ page }) => {
  const f = fixture(), source = { ...structuredClone(f.node), id: 'external-original', canvasId: 'external-work', contentItems: [{ id: 'source-table', type: 'asset', assetId: 'pinned-table' }] as NodeContentItem[] };
  f.project.snapshots[source.id] = { node: source, value: null, savedAt: '2026-10-24T00:00:00Z', assets: [{ id: 'pinned-table', name: 'Pinned.csv', mime: 'text/csv', base64: Buffer.from('Name\nOld observation').toString('base64') }] };
  const reference = addNode(f.project, f.project.entryCanvasId, 'reference', 'Linked Zeus'); Object.assign(reference, { targetId: source.id, x: 500, y: 70, width: 300, height: 180, sizeMode: 'fixed' });
  f.project.assets.push({ id: 'pinned-table', name: 'Newer.csv', mime: 'text/csv', base64: Buffer.from('Name\nNew value').toString('base64') });
  await mount(page, f);
  const result = await page.evaluate(({ id, assetId }) => { const h = (window as any).hubRenderTest, api = (window as any).hubRenderAPI, n = h.project.nodes.find((n: any) => n.id === id); return api.hubContentAsset(h.project, n, assetId); }, { id: reference.id, assetId: 'pinned-table' });
  expect(result.name).toBe('Pinned.csv');
  await page.evaluate(id => { const h = (window as any).hubRenderTest, n = h.project.nodes.find((n: any) => n.id === id); n.contentItems = [{ id: 'own', type: 'text', text: 'Local commentary' }]; h.draw(); }, reference.id);
  const card = page.locator(`.ms-node[data-node-id="${reference.id}"]`); await expect(card.locator('.ms-text')).toHaveText('Local commentary');
  expect(await page.evaluate(id => (window as any).hubRenderTest.project.nodes.find((n: any) => n.id === id).targetId, reference.id)).toBe(source.id);
});

test('Audio in the display slot uses a native player in the scene and Hub', async ({ page }) => {
  const f = fixture(), wav = Buffer.alloc(46); wav.write('RIFF', 0); wav.writeUInt32LE(38, 4); wav.write('WAVEfmt ', 8); wav.writeUInt32LE(16, 16); wav.writeUInt16LE(1, 20); wav.writeUInt16LE(1, 22); wav.writeUInt32LE(8000, 24); wav.writeUInt32LE(16000, 28); wav.writeUInt16LE(2, 32); wav.writeUInt16LE(16, 34); wav.write('data', 36); wav.writeUInt32LE(2, 40);
  f.project.assets.push({ id: 'audio', name: 'Audio.wav', mime: 'audio/wav', base64: wav.toString('base64') }); f.node.contentItems = [{ id: 'audio-item', type: 'asset', assetId: 'audio' }];
  await mount(page, f); await expect(page.locator('.ms-node audio')).toHaveCount(1); await page.evaluate(() => (window as any).hubRenderTest.showHub()); await expect(page.locator('#hub audio')).toHaveCount(1);
});

test('a thousand-item published Hub loads nearby readers rather than decoding the full stack', async ({ page }) => {
  const f = fixture(); f.node.contentItems = Array.from({ length: 1000 }, (_, index) => ({ id: `table-${index}`, type: 'asset', assetId: 'table' }));
  await mount(page, f); await page.evaluate(() => (window as any).hubRenderTest.showHub());
  await expect(page.locator('#hub .nh-content-section')).toHaveCount(1000);
  await expect(page.locator('#hub .mdv-viewer').first()).toBeVisible();
  expect(await page.locator('#hub .mdv-viewer').count()).toBeLessThan(20);
  await page.locator('#hub').evaluate(host => { host.scrollTop = host.scrollHeight; });
  await expect(page.locator('#hub .nh-content-section').last().locator('.mdv-viewer')).toBeVisible();
  expect(await page.locator('#hub .mdv-viewer').count()).toBeLessThan(30);
});

test('unsupported attachments stay inert and download their exact original bytes', async ({ page }) => {
  const f = fixture(), original = '<script>window.UNSAFE_HUB_ATTACHMENT=true</script>';
  f.project.assets.push({ id: 'html-source', name: 'Original.custom', mime: 'application/x-example', base64: Buffer.from(original).toString('base64') });
  f.node.contentItems = [{ id: 'html-item', type: 'asset', assetId: 'html-source' }];
  await mount(page, f); await page.evaluate(() => (window as any).hubRenderTest.showHub());
  const download = page.locator('#hub .nh-download'); await expect(download).toBeVisible();
  expect(await page.evaluate(() => (window as any).UNSAFE_HUB_ATTACHMENT)).toBeUndefined();
  const contents = await download.evaluate(async anchor => { const response = await fetch((anchor as HTMLAnchorElement).href); return { mime: response.headers.get('content-type'), text: await response.text() }; });
  expect(contents).toEqual({ mime: 'application/octet-stream', text: original });
});

test('an unchanged display player retains identity, playback and controls through secondary Hub edits', async ({ page }) => {
  const f = fixture(), length = 8 * 8000 * 2, wav = Buffer.alloc(44 + length);
  wav.write('RIFF', 0); wav.writeUInt32LE(wav.length - 8, 4); wav.write('WAVEfmt ', 8); wav.writeUInt32LE(16, 16); wav.writeUInt16LE(1, 20); wav.writeUInt16LE(1, 22); wav.writeUInt32LE(8000, 24); wav.writeUInt32LE(16000, 28); wav.writeUInt16LE(2, 32); wav.writeUInt16LE(16, 34); wav.write('data', 36); wav.writeUInt32LE(length, 40);
  f.project.assets.push({ id: 'long-audio', name: 'Long.wav', mime: 'audio/wav', base64: wav.toString('base64') }, { id: 'other-audio', name: 'Other.wav', mime: 'audio/wav', base64: wav.toString('base64') });
  f.node.contentItems = [{ id: 'player', type: 'asset', assetId: 'long-audio' }];
  await mount(page, f); const player = page.locator('.ms-node audio');
  await expect.poll(() => player.evaluate((element: HTMLAudioElement) => element.readyState)).toBeGreaterThanOrEqual(1);
  await player.evaluate(async (element: HTMLAudioElement) => { element.currentTime = 1.25; element.volume = .35; element.muted = true; await element.play(); });
  const before = await player.evaluate((element: HTMLAudioElement) => { (window as any).previousHubPlayer = element; return element.currentTime; });
  await page.evaluate(() => { const h = (window as any).hubRenderTest; h.node().contentItems.push({ id: 'secondary', type: 'text', text: 'Off-canvas information' }); h.node().showTitle = true; h.draw(); });
  expect(await player.evaluate(element => element === (window as any).previousHubPlayer)).toBe(true);
  expect(await player.evaluate((element: HTMLAudioElement) => element.currentTime)).toBeGreaterThanOrEqual(before);
  expect(await player.evaluate((element: HTMLAudioElement) => ({ volume: element.volume, muted: element.muted }))).toEqual({ volume: .35, muted: true });
  await expect.poll(() => player.evaluate((element: HTMLAudioElement) => element.paused)).toBe(false);
  await page.waitForTimeout(150); expect(await player.evaluate((element: HTMLAudioElement) => element.paused)).toBe(false);
  // The editor replaces its surrounding shell. Reuse the prepared scene host
  // through that move, then verify the same native player is still advancing.
  await page.evaluate(() => { const h = (window as any).hubRenderTest, scene = document.querySelector('#scene')!; (window as any).hubRenderAPI.prepareSceneMediaForRemount(scene); scene.remove(); document.body.prepend(scene); h.node().contentItems.push({ id: 'another', type: 'text', text: 'Another note' }); h.draw(); });
  expect(await player.evaluate(element => element === (window as any).previousHubPlayer)).toBe(true);
  expect(await player.evaluate((element: HTMLAudioElement) => element.currentTime)).toBeGreaterThanOrEqual(before);
  await page.waitForTimeout(150); expect(await player.evaluate((element: HTMLAudioElement) => element.paused)).toBe(false);
  await page.evaluate(() => { const h = (window as any).hubRenderTest; h.node().contentItems[0] = { id: 'replacement', type: 'asset', assetId: 'other-audio' }; h.draw(); });
  expect(await player.evaluate(element => element === (window as any).previousHubPlayer)).toBe(false);
  expect(await player.evaluate((element: HTMLAudioElement) => ({ time: element.currentTime, volume: element.volume, muted: element.muted, paused: element.paused }))).toEqual({ time: 0, volume: 1, muted: false, paused: true });
  expect(await page.evaluate(() => (window as any).previousHubPlayer.getAttribute('src'))).toBeNull();
  await player.evaluate(element => { (window as any).previousHubPlayer = element; });
  await page.evaluate(() => { const h = (window as any).hubRenderTest; h.project.assets.find((asset: any) => asset.id === 'other-audio').base64 = h.project.assets.find((asset: any) => asset.id === 'other-audio').base64.slice(0, -4) + 'AAAA'; h.draw(); });
  expect(await player.evaluate(element => element === (window as any).previousHubPlayer)).toBe(false);
});

test('the real published navigation CSS keeps Hub labels horizontal and its popout icon compact at 340px', async ({ page }) => {
  const f = fixture(); f.project.assets.find(asset => asset.id === 'table')!.name = 'Family.csv'; f.node.contentItems = [f.items[2]!];
  await mount(page, f);
  await page.evaluate(() => {
    const h = (window as any).hubRenderTest, host = document.querySelector('#hub') as HTMLElement;
    host.className = 'mv-navigation'; Object.assign(host.style, { width: '340px', padding: '0' });
    const context = document.createElement('div'); context.className = 'mv-node-context'; context.style.maxHeight = 'none'; host.replaceChildren(context);
    h.hub = (window as any).hubRenderAPI.appendReadonlyNodeHub(context, h.project, h.node());
  });
  const heading = page.locator('#hub .nh-content-heading'), label = heading.locator('strong'), open = heading.getByRole('button');
  await expect(label).toHaveText('Family.csv'); await expect(open).toBeVisible();
  const geometry = await heading.evaluate(header => { const label = header.querySelector('strong')!.getBoundingClientRect(), button = header.querySelector('button')!.getBoundingClientRect(); return { labelWidth: label.width, labelHeight: label.height, buttonWidth: button.width }; });
  expect(geometry.labelWidth).toBeGreaterThan(100); expect(geometry.labelHeight).toBeLessThan(30); expect(geometry.buttonWidth).toBe(30);
  await open.click(); await expect(page.locator('dialog.nh-popout .mdv-viewer')).toContainText('Zeus');
  const dialog = await page.locator('dialog.nh-popout').boundingBox(); expect(dialog!.width).toBeLessThanOrEqual(1500 * .94); expect(dialog!.height).toBeLessThanOrEqual(1000 * .92);
  await page.getByRole('button', { name: 'Close content viewer', exact: true }).click();
});
