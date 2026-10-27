import { test, expect } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';
import { normalizeContainerLayouts } from '../src/core/grid.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { expansionTrayBounds, resizeExpansionTray } from '../src/core/expansion-tray.ts';

test('ordinary, automatic-tray and resized-tray Gallery navigation paints the exact page width while paging in editor and read-only scenes', async ({ page }, info) => {
  const script = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';window.widthHarness={renderScene};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
  const css = (await Promise.all(['scene', 'grid', 'gallery-scene', 'window-scene', 'expansion-tray-scene', 'canvas-launcher'].map(name => readFile(`src/${name}.css`, 'utf8')))).join('\n');
  await page.setContent('<style>html,body{margin:0}#scene{width:1450px;height:900px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  for (const kind of ['ordinary', 'automatic-tray', 'resized-tray'] as const) for (const width of [30, 75, 180]) {
    let project = newProject('Narrow Gallery frame'), owner = addNode(project, project.entryCanvasId, 'container', '');
    Object.assign(owner, { x: 20, y: 20, width: 420, height: 400, sizeMode: 'fixed', collapsed: false, containerVariant: 'horizontal' });
    const source = addNode(project, owner.canvasId, 'media', 'Gallery');
    Object.assign(source, { ...(kind === 'ordinary' ? {} : { parentId: owner.id }), x: 50, y: 50, width: 180, height: 120, sizeMode: 'fixed', collapsed: true, containerVariant: 'gallery' });
    const children = Array.from({ length: 2 }, (_, at) => {
      const node = addNode(project, owner.canvasId, 'media', `Page ${at}`);
      Object.assign(node, { parentId: source.id, width, height: 280, collapsed: true, sizeMode: 'auto', text: 'A tall common page. '.repeat(at ? 40 : 1), style: { shape: 'rounded' } }); return node;
    });
    if (kind === 'ordinary') project.nodes = project.nodes.filter(node => node.id !== owner.id);
    project = revealChildren(normalizeContainerLayouts(project), source.id, true);
    if (kind === 'resized-tray') { const frame = expansionTrayBounds(project, source.id)!; project = resizeExpansionTray(project, source.id, { ...frame, width: width + 10, height: 420 }); }
    for (const readonly of [false, true]) for (const child of children) {
      const result = await page.evaluate(({ project, sourceId, childId, readonly }) => {
        const before = JSON.stringify(project);
        (window as any).widthHarness.renderScene(document.getElementById('scene'), project, project.entryCanvasId, { readonly, galleryItems: { [sourceId]: childId }, view: { x: 30, y: 30, scale: 1.5 } });
        const card = document.querySelector<HTMLElement>(`.ms-node[data-node-id="${childId}"]`)!, nav = document.querySelector<HTMLElement>(`[data-external-gallery="${sourceId}"]`)!;
        const a = card.getBoundingClientRect(), b = nav.getBoundingClientRect();
        return { card: { x: a.x, width: a.width }, nav: { x: b.x, width: b.width }, buttons: nav.querySelectorAll('button').length, unchanged: before === JSON.stringify(project) };
      }, { project, sourceId: source.id, childId: child.id, readonly });
      expect(result.nav.x, `${kind}, width${width}`).toBeCloseTo(result.card.x, 4);
      expect(result.nav.width, `${kind}, width${width}`).toBeCloseTo(result.card.width, 4);
      expect(result.buttons).toBe(2); expect(result.unchanged).toBe(true);
    }
    if (kind === 'automatic-tray' && width === 30) await page.screenshot({ path: info.outputPath('narrow-automatic-gallery-equal-width.png') });
  }
  await page.screenshot({ path: info.outputPath('gallery-page-navigation-equal-width.png') });
});
