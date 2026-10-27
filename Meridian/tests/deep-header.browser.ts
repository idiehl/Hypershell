import { test, expect } from '@playwright/test';
import { build } from 'esbuild';
import { readFile, writeFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';

test('deep expanded titles fit their allocated slot above original child frames at detail zoom', async ({ page }, info) => {
  const project = newProject('Twenty-four native nested title surfaces'), ids: string[] = [];
  let parentId = project.canvases[0].rootId;
  for (let depth = 0; depth < 24; depth++) {
    const node = addNode(project, project.entryCanvasId, 'container', `Level ${depth + 1} · vector workspace`);
    Object.assign(node, { parentId, x: 0, y: 0, width: 180, height: 120, sizeMode: 'fixed', collapsed: true, containerVariant: 'horizontal',
      style: { shape: 'rectangle', childPadding: 20, titleSize: 22 }, containerSizes: {
        expanded: { width: 700, height: 500, sizeMode: 'fixed' }, collapsed: { width: 180, height: 120, sizeMode: 'fixed' } } });
    ids.push(node.id); parentId = node.id;
  }
  const leaf = addNode(project, project.entryCanvasId, 'media', 'Original deepest detail');
  Object.assign(leaf, { parentId, sizeMode: 'fixed', width: 200, height: 120, collapsed: true, style: { shape: 'rectangle' } });
  const script = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';import {revealChildren} from './src/core/reveal.ts';import {navigationIndex} from './src/core/navigation.ts';import {boundedChildHeaderBounds} from './src/core/sizing.ts';window.deepHeader={renderScene,revealChildren,navigationIndex,boundedChildHeaderBounds};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
  await page.setContent('<style>html,body{margin:0}#scene{width:1450px;height:900px}</style><div id="scene"></div>');
  const css = (await Promise.all(['scene', 'grid', 'gallery-scene', 'window-scene', 'expansion-tray-scene'].map(name => readFile(`src/${name}.css`, 'utf8')))).join('\n');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  const observations = await page.evaluate(({ project, ids }) => {
    const h = (window as any).deepHeader;
    for (const id of ids) project = h.revealChildren(project, id, true);
    const unchanged = JSON.stringify(project), results = [];
    for (const depth of [7, 15, 23]) {
      const nav = h.navigationIndex(project), node = nav.nodes.get(ids[depth]), frame = nav.bounds(node), slot = h.boundedChildHeaderBounds(project, node, {}, frame);
      const scale = Math.min(1024, 600 / frame.width), view = { x: 300 - frame.x * scale, y: 100 - frame.y * scale, scale };
      h.renderScene(document.getElementById('scene'), project, project.entryCanvasId, { view });
      const card = document.querySelector<HTMLElement>(`.ms-node[data-node-id="${node.id}"]`)!, title = card.querySelector<HTMLElement>(':scope > .ms-node-header')!;
      const child = document.querySelector<HTMLElement>(`.ms-node[data-node-id="${nav.children.get(node.id)[0].id}"]`)!, a = title.getBoundingClientRect(), b = child.getBoundingClientRect(), c = card.getBoundingClientRect();
      const rect = (r: DOMRect) => ({ x: r.x, y: r.y, width: r.width, height: r.height, bottom: r.bottom, right: r.right });
      results.push({ depth: depth + 1, title: rect(a), glyphs: rect(title.querySelector('.ms-node-title')!.getBoundingClientRect()), child: rect(b), frame: rect(c), slot, scale, authoredFont: node.style.titleSize,
        font: getComputedStyle(title.querySelector('.ms-node-title')!).fontSize, fit: Number(card.dataset.contentFit), unchanged: JSON.stringify(project) === unchanged });
    }
    return results;
  }, { project, ids });
  for (const result of observations) {
    expect(result.title.height, `level${result.depth} title remains visible`).toBeGreaterThan(0);
    expect(result.title.bottom, `level${result.depth} title stays above children`).toBeLessThanOrEqual(result.child.y + .1);
    expect(result.glyphs.bottom, `level${result.depth} native text stays above children`).toBeLessThanOrEqual(result.child.y + .1);
    expect(result.title.x).toBeGreaterThanOrEqual(result.frame.x - .1); expect(result.title.right).toBeLessThanOrEqual(result.frame.right + .1);
    expect(result.child.height).toBeGreaterThan(result.frame.height * .74);
    expect(result.font).toBe(`${result.authoredFont}px`); expect(result.unchanged).toBe(true); expect(result.fit).toBeGreaterThan(0);
  }
  await writeFile(info.outputPath('deep-title-bounds.json'), JSON.stringify(observations, null, 2));
  await page.screenshot({ path: info.outputPath('level24-title-and-original-child.png') });
});
