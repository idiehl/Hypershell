import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { build } from 'esbuild';
import { chromium } from '@playwright/test';

const output = 'output/workspace-tools-qa';
await mkdir(output, { recursive: true });
const bundle = await build({ bundle: true, write: false, format: 'iife', platform: 'browser', stdin: {
  resolveDir: process.cwd(), sourcefile: 'scene-author-review.ts', loader: 'ts', contents: `
    import { renderScene } from './src/scene.ts';
    import { newProject, addNode } from './src/core/model.ts';
    import { NODE_SHAPES } from './src/core/appearance.ts';
    const p = newProject('Scene author tools');
    const parent = addNode(p, p.entryCanvasId, 'container', 'Parent');
    Object.assign(parent, { x: 40, y: 40, width: 200, height: 150, childLayout: 'spatial', text: 'Parent content' });
    const child = addNode(p, p.entryCanvasId, 'text', 'Child', parent.id);
    Object.assign(child, { x: 320, y: 40, width: 200, height: 150, text: 'Child content' });
    p.links.push({ id: 'scene-link', canvasId: p.entryCanvasId, parentId: p.nodes[0].id, sourceId: parent.id, targetId: child.id, kind: 'visual', relation: 'generic', direction: 'none' });
    let mode = 'hand', resetView = false; const host = document.getElementById('scene');
    window.events = []; window.project = p; window.ids = { parent: parent.id, child: child.id };
    const note = (type, data) => window.events.push({ type, data });
    window.draw = (next = mode) => { mode = next; renderScene(host, p, p.entryCanvasId, { authoringMode: mode, view: resetView ? { x: 44, y: 44, scale: 1 } : undefined,
      onAuthorClick: target => note('author', target), onContextMenu: target => note('menu', target),
      onSelection: id => note('selection', id), onMove: (...data) => note('move', data),
      onToggle: (...data) => note('toggle', data), onLinkSelect: id => note('link', id),
      onCanvasNodeOpen: id => note('canvas', id),
      onViewChange: view => window.view = view,
    }); };
    window.shapes = () => {
      p.nodes.splice(1); p.links = [];
      NODE_SHAPES.forEach((shape, i) => {
        const node = addNode(p, p.entryCanvasId, 'text', shape.label);
        Object.assign(node, { x: 35 + (i % 4) * 270, y: 35 + Math.floor(i / 4) * 240, width: 230, height: 190,
          text: 'Content stays readable.\\nThe backdrop has its own shape.', style: { shape: shape.value, fill: '#1D3E43', border: '#79C6B5', fontSize: 14, titleSize: 18 } });
      });
      resetView = true; window.draw('hand');
    };
    window.shapeLinks = (explicit = false, styled = true) => {
      p.nodes.splice(1); p.links = [];
      const circle = addNode(p, p.entryCanvasId, 'text', 'Circle endpoint');
      Object.assign(circle, { x: 50, y: 50, width: 200, height: 200, style: styled ? { shape: 'circle' } : undefined });
      const diamond = addNode(p, p.entryCanvasId, 'text', 'Diamond endpoint');
      Object.assign(diamond, { x: 420, y: 330, width: 240, height: 210, style: styled ? { shape: 'diamond' } : undefined });
      p.links.push({ id: 'shape-edge', canvasId: p.entryCanvasId, sourceId: circle.id, targetId: diamond.id, kind: 'visual', relation: 'generic', direction: 'none', ...(explicit ? { sourceAnchor: { x: 1, y: .2 }, targetAnchor: { x: 0, y: .3 } } : {}) });
      resetView = true; window.draw('hand');
    };
    window.externalLauncher = () => {
      p.nodes.splice(1); p.links = [];
      const launcher = addNode(p, p.entryCanvasId, 'canvas-link', 'Saved external canvas');
      Object.assign(launcher, { x: 50, y: 50, width: 260, height: 170, canvasTarget: { projectId: 'saved-project', canvasId: 'saved-canvas' } });
      window.events = []; resetView = true; window.draw('node'); return launcher.id;
    };
    window.draw();
  `,
} });
const css = await readFile('src/scene.css', 'utf8');
const server = createServer((req, res) => {
  if (req.url === '/scene.js') { res.setHeader('Content-Type', 'text/javascript'); res.end(bundle.outputFiles[0].text); }
  else if (req.url === '/scene.css') { res.setHeader('Content-Type', 'text/css'); res.end(css); }
  else { res.setHeader('Content-Type', 'text/html'); res.end('<!doctype html><html><head><link rel="stylesheet" href="/scene.css"></head><body style="margin:0"><div id="scene" style="height:850px"></div><script src="/scene.js"></script></body></html>'); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1250, height: 850 } });
const errors = []; page.on('pageerror', error => errors.push(String(error)));
const checks = [];
try {
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  const ids = await page.evaluate(() => window.ids);
  const parent = page.locator(`[data-node-id="${ids.parent}"]`);
  const title = parent.locator('.ms-node-title');
  await title.click();
  assert.equal((await page.evaluate(() => window.events)).at(-1).type, 'selection');
  await page.evaluate(() => { window.events = []; window.draw('node'); });
  await title.click();
  let events = await page.evaluate(() => window.events);
  assert.equal(events.length, 1); assert.equal(events[0].type, 'author'); assert.equal(events[0].data.nodeId, ids.parent);
  assert.equal(await page.locator('.ms-viewport').getAttribute('data-authoring-mode'), 'node');
  checks.push('Node mode acts on release and remains active');
  for (const mode of ['node', 'link', 'delete']) {
    await page.evaluate(mode => { window.events = []; window.draw(mode); }, mode);
    const box = await title.boundingBox(), start = { x: box.x + 25, y: box.y + box.height / 2 };
    const original = await page.evaluate(() => window.view);
    await page.mouse.move(start.x, start.y); await page.mouse.down(); await page.mouse.move(start.x + 50, start.y + 70, { steps: 5 }); await page.mouse.up();
    assert.deepEqual(await page.evaluate(() => window.events), []);
    const next = await page.evaluate(() => window.view);
    assert.equal(next.x, original.x + 50); assert.equal(next.y, original.y + 70);
    await page.mouse.move(1100, 790); await page.mouse.down(); await page.waitForTimeout(400); await page.mouse.up();
    assert.deepEqual(await page.evaluate(() => window.events), []);
    checks.push(mode + ' mode pans from nodes and long press never acts');
  }
  await page.evaluate(() => { window.events = []; window.draw('delete'); });
  const link = page.locator('.ms-link-hit'); const linkBox = await link.boundingBox();
  await page.mouse.click(linkBox.x + linkBox.width / 2, linkBox.y + linkBox.height / 2);
  events = await page.evaluate(() => window.events);
  assert.equal(events.length, 1); assert.equal(events[0].data.linkId, 'scene-link');
  checks.push('Delete mode targets a link segment without selecting it');
  await page.evaluate(() => { window.events = []; window.draw('hand'); });
  await title.click({ button: 'right' });
  events = await page.evaluate(() => window.events);
  assert.equal(events.length, 1); assert.equal(events[0].type, 'menu'); assert.equal(events[0].data.nodeId, ids.parent);
  await page.mouse.click(1100, 790, { button: 'right' });
  events = await page.evaluate(() => window.events);
  assert.equal(events.at(-1).type, 'menu'); assert.equal(events.at(-1).data.nodeId, undefined);
  checks.push('Context menu target includes node identity or background world point');
  await page.evaluate(() => { window.events = []; window.draw('node'); });
  await parent.focus(); await parent.press('Enter');
  events = await page.evaluate(() => window.events);
  assert.equal(events.at(-1).type, 'author'); assert.equal(events.at(-1).data.nodeId, ids.parent);
  await parent.press('Shift+F10');
  events = await page.evaluate(() => window.events);
  assert.equal(events.at(-1).type, 'menu'); assert.equal(events.at(-1).data.nodeId, ids.parent);
  checks.push('Keyboard Enter uses active tool and Shift F10 opens node context menu');
  await page.evaluate(() => { window.events = []; window.draw('delete'); });
  await link.focus(); await link.press('Enter');
  events = await page.evaluate(() => window.events);
  assert.equal(events.at(-1).type, 'author'); assert.equal(events.at(-1).data.linkId, 'scene-link');
  checks.push('Keyboard Enter uses delete on a focused link');
  await page.evaluate(() => {
    window.events = []; window.draw('node');
    const input = document.createElement('input'); document.body.append(input); input.focus();
    input.onblur = () => { const previous = document.getElementById('scene'); const next = previous.cloneNode(false); previous.replaceWith(next); };
  });
  await page.mouse.click(1100, 790);
  assert.deepEqual(await page.evaluate(() => window.events), []);
  assert.deepEqual(errors, []);
  checks.push('An input commit replacing the scene suppresses stale gestures without pointer capture errors');
  await page.reload();
  await page.evaluate(() => window.shapes());
  assert.equal(await page.locator('.ms-node[data-shape]').count(), 11);
  for (const shape of ['triangle', 'diamond', 'hexagon', 'none']) {
    const style = await page.locator(`[data-shape="${shape}"]`).evaluate(el => ({ cardClip: getComputedStyle(el).clipPath, backdrop: getComputedStyle(el, '::before').display, backdropClip: getComputedStyle(el, '::before').clipPath }));
    assert.equal(style.cardClip, 'none');
    if (shape === 'none') assert.equal(style.backdrop, 'none'); else assert.match(style.backdropClip, /^polygon/);
  }
  checks.push('All 11 shapes render; only backdrop is clipped and no-backdrop is transparent');
  await page.screenshot({ path: output + '/node-shapes.png' });
  const route = () => page.locator('.ms-link-line[data-link-id="shape-edge"]').evaluate(el => {
    const path = el, start = path.getPointAtLength(0), end = path.getPointAtLength(path.getTotalLength());
    return { start: { x: start.x, y: start.y }, end: { x: end.x, y: end.y } };
  });
  const close = (a, b) => Math.abs(a - b) < .01;
  for (const explicit of [false, true]) {
    await page.evaluate(explicit => window.shapeLinks(explicit), explicit);
    const points = await route();
    assert.ok(close(Math.hypot(points.start.x - 150, points.start.y - 150), 100));
    assert.ok(close(Math.abs((points.end.x - 540) / 120) + Math.abs((points.end.y - 435) / 105), 1));
  }
  checks.push('Automatic and explicit link endpoints meet circle and diamond backdrop edges');
  await page.evaluate(() => window.shapeLinks(true, false));
  const defaultRoute = await route();
  assert.ok(close(defaultRoute.start.x, 250) && close(defaultRoute.start.y, 90));
  assert.ok(close(defaultRoute.end.x, 420) && close(defaultRoute.end.y, 393));
  checks.push('Historical rectangular explicit anchors retain their exact coordinates');
  const launcherId = await page.evaluate(() => window.externalLauncher());
  await page.getByRole('button', { name: 'Open Saved external canvas', exact: true }).click();
  assert.deepEqual(await page.evaluate(() => window.events), [{ type: 'canvas', data: launcherId }]);
  checks.push('Ordinary external canvas launcher opens without library-specific metadata and keeps its native action in Node mode');
  assert.deepEqual(errors, []);
  await writeFile(output + '/scene-review.json', JSON.stringify({ passed: checks, errors }, null, 2));
  process.stdout.write(JSON.stringify({ passed: checks, screenshot: output + '/node-shapes.png' }, null, 2));
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
