import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);
test('hover adds no dashed inner outline while authored border patterns and selected-node outlines remain intact', async ({ page }, info) => {
  const project = newProject('Uncluttered hover outlines');
  const ordinary = addNode(project, project.entryCanvasId, 'media', 'A regular node');
  Object.assign(ordinary, { x: 60, y: 80, width: 260, height: 160, sizeMode: 'fixed', style: { shape: 'rectangle', borderStyle: 'dotted', borderWidth: 3 } });
  const label = addNode(project, project.entryCanvasId, 'text', 'A floating label');
  Object.assign(label, { x: 360, y: 80, width: 260, height: 160, sizeMode: 'fixed', style: { shape: 'none', variant: 'label' } });
  const gallery = addNode(project, project.entryCanvasId, 'media', 'Gallery');
  Object.assign(gallery, { x: 60, y: 320, width: 260, height: 160, sizeMode: 'fixed', collapsed: false, containerVariant: 'gallery', childLayout: 'spatial' });
  const child = addNode(project, project.entryCanvasId, 'media', 'Visible Gallery child');
  Object.assign(child, { parentId: gallery.id, x: 400, y: 320, width: 260, height: 160, sizeMode: 'fixed', style: { shape: 'rectangle' } });
  const css = (await Promise.all(['scene', 'gallery-scene', 'expansion-tray-scene', 'window-scene', 'grid'].map(name => readFile(`src/${name}.css`, 'utf8')))).join('\n');
  const script = (await build({ stdin: { contents: "import {renderScene} from './src/scene.ts';window.outlineScene=renderScene;", resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', target: 'es2022' })).outputFiles[0]!.text;
  await page.setContent('<style>html,body{margin:0}#scene{width:1450px;height:850px}</style><main id="scene"></main>');
  await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate((project: MeridianProject) => {
    const state = { project, before: JSON.stringify(project), selected: [] as string[] };
    (window as any).outlineState = state;
    (window as any).outlineDraw = () => (window as any).outlineScene(document.querySelector('#scene'), project, project.entryCanvasId,
      { readonly: true, fit: false, view: { x: 0, y: 0, scale: 1 }, selections: state.selected });
    (window as any).outlineDraw();
  }, project);
  for (const node of [ordinary, label, child]) {
    await card(page, node.id).hover();
    await expect(card(page, node.id).locator(':scope > .ms-node-outline')).toHaveCSS('display', 'none');
    expect(await card(page, node.id).evaluate(element => getComputedStyle(element).outlineStyle)).toBe('none');
  }
  const border = card(page, ordinary.id).locator(':scope > .ms-node-border path').first();
  await expect(border).toHaveAttribute('stroke-linecap', 'round'); expect(await border.getAttribute('stroke-dasharray')).not.toBe('none');
  await page.evaluate(id => { (window as any).outlineState.selected = [id]; (window as any).outlineDraw(); }, child.id);
  await card(page, child.id).hover();
  await expect(card(page, child.id)).toHaveAttribute('aria-selected', 'true');
  await expect(card(page, child.id).locator(':scope > .ms-node-outline')).toHaveCSS('display', 'block');
  await expect(card(page, child.id).locator(':scope > .ms-node-outline path')).toHaveCSS('stroke-dasharray', 'none');
  expect(await page.evaluate(() => JSON.stringify((window as any).outlineState.project))).toBe(JSON.stringify(project));
  await page.screenshot({ path: info.outputPath('hover-clean-gallery-child-selected.png') });
});
