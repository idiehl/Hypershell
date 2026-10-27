import { test, expect, type Page } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { addNode, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

let sceneScript: string, sceneCss: string;
test.beforeAll(async () => {
  sceneCss = await readFile('src/scene.css', 'utf8');
  sceneScript = (await build({ stdin: { contents: `import {renderScene} from './src/scene.ts';window.routeIndexHarness={renderScene};`, resolveDir: process.cwd(), loader: 'ts' },
    bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});

const line = (page: Page, id: string) => page.locator(`.ms-link-line[data-link-id="${id}"]`);
const card = (page: Page, id: string) => page.locator(`.ms-node[data-node-id="${id}"]`);

function projectFixture() {
  const project = newProject('Indexed link routes'), canvasId = project.entryCanvasId;
  project.canvases[0]!.snapping = { stickyEdges: false, smartAlign: false, snapToGrid: false };
  const make = (title: string, x: number, y: number) => {
    const node = addNode(project, canvasId, 'media', title);
    Object.assign(node, { x, y, width: 180, height: 120, sizeMode: 'fixed', childLayout: 'spatial', text: title }); return node;
  };
  const source = make('Source branch', 100, 100), target = make('Destination', 500, 100);
  const otherSource = make('Unrelated source', 100, 500), otherTarget = make('Unrelated destination', 800, 500);
  project.links.push(
    { id: 'moving-route', canvasId, sourceId: source.id, targetId: target.id, kind: 'visual', relation: 'generic', direction: 'none', points: [{ x: 350, y: 60 }], style: { lineStyle: 'solid' } },
    { id: 'unrelated-route', canvasId, sourceId: otherSource.id, targetId: otherTarget.id, kind: 'visual', relation: 'generic', direction: 'none', style: { lineStyle: 'solid' } },
  );
  return { project, source, target };
}

async function mount(page: Page, project: MeridianProject, options: { selections?: string[]; selectedLink?: string; countReads?: boolean } = {}) {
  await page.setContent('<style>html,body{margin:0}#scene{height:850px;width:1300px}</style><div id="scene"></div>');
  await page.addStyleTag({ content: sceneCss }); await page.addScriptTag({ content: sceneScript });
  return page.evaluate(({ project, options }) => {
    const state: any = { sourceReads: 0, targetReads: 0, moves: [], pathWrites: {}, view: { x: 20, y: 20, scale: 1 }, ...options };
    if (options.countReads) for (const link of project.links) for (const name of ['sourceId', 'targetId'] as const) {
      const value = link[name]; Object.defineProperty(link, name, { configurable: true, enumerable: true, get() { state[name === 'sourceId' ? 'sourceReads' : 'targetReads']++; return value; } });
    }
    const originalSetAttribute = Element.prototype.setAttribute;
    Element.prototype.setAttribute = function (name: string, value: string) {
      if (name === 'd' && this.classList.contains('ms-link-line')) {
        const id = (this as SVGElement).dataset.linkId!; state.pathWrites[id] = (state.pathWrites[id] ?? 0) + 1;
      }
      return originalSetAttribute.call(this, name, value);
    };
    const render = () => (window as any).routeIndexHarness.renderScene(document.getElementById('scene'), project, project.entryCanvasId, {
      view: state.view, selections: state.selections ?? [], selection: state.selectedLink,
      onSelections(ids: string[]) { state.selections = ids; },
      onMoveSelection(ids: string[], dx: number, dy: number) { state.moves.push({ ids, dx, dy }); },
      onViewChange(view: any) { state.view = view; }, onRouteEdit() {},
    });
    (window as any).routeIndexTest = { state, project, render }; render();
    return { sourceReads: state.sourceReads, targetReads: state.targetReads };
  }, { project, options });
}

test('indexed loop ordinals retain exact path order, authored styles, and rebuild after removal', async ({ page }) => {
  const { project, source } = projectFixture(), canvasId = project.entryCanvasId;
  const loop = (id: string) => ({ id, canvasId, sourceId: source.id, targetId: source.id, kind: 'visual' as const, relation: 'causal' as const,
    sourceAnchor: { x: 0, y: .5 }, targetAnchor: { x: 0, y: .5 }, style: { width: 4, color: '#CC3366', lineStyle: 'dotted' as const } });
  project.links.splice(0, 0, loop('loop-1')); project.links.splice(2, 0, loop('loop-2')); project.links.push(loop('loop-3'));
  await mount(page, project);
  const expected = (ordinal: number) => { const size = 65 + ordinal * 24; return `M100,160 C${100 - size},${160 - size} ${100 - size},${160 + size} 100,160`; };
  for (let at = 0; at < 3; at++) expect(await line(page, `loop-${at + 1}`).getAttribute('d')).toBe(expected(at));
  expect(await line(page, 'loop-2').evaluate(path => ({ color: getComputedStyle(path).stroke, width: getComputedStyle(path).strokeWidth, dash: getComputedStyle(path).strokeDasharray })))
    .toEqual({ color: 'rgb(204, 51, 102)', width: '4px', dash: '0px, 12px' });
  await page.evaluate(() => { const fixture = (window as any).routeIndexTest; fixture.project.links = fixture.project.links.filter((link: any) => link.id !== 'loop-1'); fixture.render(); });
  expect(await line(page, 'loop-2').getAttribute('d')).toBe(expected(0)); expect(await line(page, 'loop-3').getAttribute('d')).toBe(expected(1));
});

test('link-property reads grow linearly when a canvas has many repeated routes', async ({ page }, info) => {
  const counts: Array<{ links: number; sourceReads: number; targetReads: number }> = [];
  for (const size of [48, 192]) {
    const { project, source } = projectFixture(); project.links = [];
    for (let at = 0; at < size; at++) project.links.push({ id: `loop-${at}`, canvasId: project.entryCanvasId, sourceId: source.id, targetId: source.id,
      kind: 'visual', relation: 'generic', direction: 'none', style: { lineStyle: 'solid' } });
    const reads = await mount(page, project, { countReads: true }); counts.push({ links: size, ...reads });
    await expect(page.locator('.ms-link-line')).toHaveCount(size);
  }
  await info.attach('route-index-operation-counts', { body: JSON.stringify(counts), contentType: 'application/json' });
  expect(counts[1]!.sourceReads + counts[1]!.targetReads).toBeLessThan((counts[0]!.sourceReads + counts[0]!.targetReads) * 5);
});

test('drag previews redraw connected routes and leave unrelated paths untouched', async ({ page }, info) => {
  const { project, source } = projectFixture(); await mount(page, project, { selections: [source.id] });
  const before = await line(page, 'moving-route').getAttribute('d'), unrelated = await line(page, 'unrelated-route').getAttribute('d');
  const box = (await card(page, source.id).locator('.ms-node-title').boundingBox())!;
  await page.mouse.move(box.x + 30, box.y + 12); await page.mouse.down();
  await page.evaluate(() => { (window as any).routeIndexTest.state.pathWrites = {}; });
  await page.mouse.move(box.x + 70, box.y + 42, { steps: 5 });
  expect(await line(page, 'moving-route').getAttribute('d')).not.toBe(before); expect(await line(page, 'unrelated-route').getAttribute('d')).toBe(unrelated);
  const writes = await page.evaluate(() => (window as any).routeIndexTest.state.pathWrites);
  expect(writes['moving-route']).toBeGreaterThan(0); expect(writes['unrelated-route']).toBeUndefined();
  await info.attach('drag-route-operation-counts', { body: JSON.stringify(writes), contentType: 'application/json' });
  await page.keyboard.press('Escape'); await page.mouse.up();
  expect(await line(page, 'moving-route').getAttribute('d')).toBe(before);
  expect(await page.evaluate(() => (window as any).routeIndexTest.state.moves)).toEqual([]);
});

test('whole-selection drag updates internal bends and selected route handles exactly once', async ({ page }) => {
  const { project, source, target } = projectFixture(); await mount(page, project, { selections: [source.id, target.id], selectedLink: 'moving-route' });
  const before = await line(page, 'moving-route').getAttribute('d'), handle = page.locator('.ms-route-handle');
  const box = (await card(page, source.id).locator('.ms-node-title').boundingBox())!;
  await page.mouse.move(box.x + 30, box.y + 12); await page.mouse.down(); await page.mouse.move(box.x + 70, box.y + 42, { steps: 4 });
  expect(await line(page, 'moving-route').getAttribute('d')).toContain('L390,90');
  await expect(handle).toHaveAttribute('cx', '390'); await expect(handle).toHaveAttribute('cy', '90');
  await page.mouse.up();
  expect(await page.evaluate(() => (window as any).routeIndexTest.state.moves)).toEqual([{ ids: [source.id, target.id], dx: 40, dy: 30 }]);
  expect(await line(page, 'moving-route').getAttribute('d')).toBe(before);
  await expect(handle).toHaveAttribute('cx', '350'); await expect(handle).toHaveAttribute('cy', '60');
});
