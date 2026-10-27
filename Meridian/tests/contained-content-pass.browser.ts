import { test, expect } from '@playwright/test';
import { build } from 'esbuild';
import { readFile } from 'node:fs/promises';
let script: string, css: string, appCss: string;
test.beforeAll(async () => {
  css = await readFile('output/document-performance-20261022/candidate/dist/scene.css', 'utf8');
  appCss = await readFile('output/scale-performance-20261022/installed-snapshot/app.css', 'utf8');
  const module = process.env.MERIDIAN_FIT_PASS_CANDIDATE === '1' ? './output/scale-performance-20261022/candidate/src/contained-content.ts' : './src/contained-content.ts';
  script = (await build({ stdin: { contents: `import {createContainedContentPass as createPass,fitContainedContent as fit} from '${module}';import {fitContainedContent as original} from './output/scale-performance-20261022/installed-baseline/src/contained-content.ts';window.cacheFit={fit,original,createPass};`, resolveDir: process.cwd(), loader: 'ts' }, bundle: true, write: false, format: 'iife', platform: 'browser', target: 'es2022' })).outputFiles[0]!.text;
});
test.beforeEach(async ({ page }) => {
  await page.route(/^https?:/, route => route.abort()); await page.setContent('<style>body{margin:0}.world{position:relative;width:1200px;height:900px}</style><main class="world"></main>'); await page.addStyleTag({ content: css }); await page.addScriptTag({ content: script });
  await page.evaluate(async () => {
    await document.fonts.ready;
    let reads = 0;
    for (const [prototype, name] of [[Element.prototype, 'scrollWidth'], [Element.prototype, 'scrollHeight'], [HTMLElement.prototype, 'offsetHeight']] as const) {
      const descriptor = Object.getOwnPropertyDescriptor(prototype, name)!; Object.defineProperty(prototype, name, { ...descriptor, get() { if ((this as HTMLElement).matches?.('.ms-node-header,.ms-node-body')) reads++; return descriptor.get!.call(this); } });
    }
    const project: any = { schemaVersion: 1, id: 'fit-project', title: 'Fit', revision: 1, entryCanvasId: 'work', canvases: [{ id: 'work', rootId: 'root', title: 'Fit' }], nodes: [{ id: 'root', kind: 'canvas', canvasId: 'work', title: '', x: 0, y: 0, width: 1000, height: 800 }], links: [], assets: [], snapshots: {}, dependencies: [] };
    let sequence = 0;
    const make = (changes: any = {}) => {
      const node: any = { id: 'text-card', parentId: 'root', canvasId: 'work', kind: 'media', title: 'A carefully fitted title', text: 'Variable scale keeps this ordinary text readable. '.repeat(3), x: 40, y: 35, width: 300, height: 180, sizeMode: 'fixed', contentType: 'text', style: { fontFamily: 'sans', titleSize: 22, fontSize: 16, shape: 'rectangle', padding: 5 }, ...changes };
      const card = document.createElement('article'); card.className = 'ms-node'; card.dataset.testId = String(++sequence);
      Object.assign(card.style, { left: `${node.x}px`, top: `${node.y}px`, width: `${node.width}px`, height: `${node.height}px`, fontFamily: changes.family ?? 'Arial, sans-serif' });
      card.style.setProperty('--ms-title-size', `${node.style.titleSize ?? 22}px`); card.style.setProperty('--ms-font-size', `${node.style.fontSize ?? 16}px`);
      const header = document.createElement('header'); header.className = 'ms-node-header'; const title = document.createElement('span'); title.className = 'ms-node-title'; title.textContent = node.title; header.append(title);
      const body = document.createElement('div'); body.className = 'ms-node-body'; const text = document.createElement('div'); text.className = 'ms-text'; text.textContent = node.text; body.append(text); card.append(header, body); document.querySelector('main')!.append(card);
      return { card, node, project: { ...project, nodes: [...project.nodes, node] } };
    };
    const fit = (record: any, original = false, width = record.node.width, height = record.node.height, usePass = false) => { Object.assign(record.card.style, { width: `${width}px`, height: `${height}px` }); reads = 0; if (usePass) record.card.removeAttribute('data-test-id'); const pass = usePass ? (window as any).cacheFit.createPass(record.card.parentElement) : undefined; const scale = (window as any).cacheFit[original ? 'original' : 'fit'](record.card, record.project, record.node, { x: record.node.x, y: record.node.y, width, height }, {}, pass); return { scale, reads, pass: !!pass, surfaces: [...record.card.querySelectorAll(':scope>.ms-node-header,:scope>.ms-node-body')].map((surface: any) => Object.fromEntries([...surface.style].sort().map((key: string) => [key, surface.style.getPropertyValue(key)]))) }; };
    (window as any).fitTest = { make, fit };
  });
});

test('fresh cards and reused cards reuse identical fit declarations without native remeasurement', async ({ page }) => {
  const result = await page.evaluate(() => {
    const h = (window as any).fitTest, cold = h.make(), first = h.fit(cold), fresh = h.make({ x: 700, y: 420 }), warm = h.fit(fresh), repeated = h.fit(fresh), original = h.fit(h.make(), true); return { first, warm, repeated, original };
  });
  expect(result.first.reads).toBeGreaterThan(0); expect(result.warm.reads).toBe(0); expect(result.repeated.reads).toBe(0);
  expect(result.warm.scale).toBe(result.original.scale); expect(result.warm.surfaces).toEqual(result.original.surfaces); expect(result.repeated).toEqual(result.warm);
});

test('text, typography, DOM formatting and frame sizes invalidate safely and match uncached fitting', async ({ page }) => {
  const results = await page.evaluate(() => {
    const h = (window as any).fitTest; h.fit(h.make());
    const changes = [{ text: 'A different passage '.repeat(30) }, { width: 170, height: 90 }, { style: { titleSize: 34, fontSize: 26, padding: 8 } }, { family: 'Georgia, serif' }];
    return changes.map(change => { const first = h.make(change), original = h.make(change); const fit = h.fit(first), expected = h.fit(original, true), warm = h.fit(h.make(change)); return { fit, expected, warm }; });
  });
  for (const result of results) { expect(result.fit.reads).toBeGreaterThan(0); expect(result.warm.reads).toBe(0); expect(result.fit.scale).toBe(result.expected.scale); expect(result.fit.surfaces).toEqual(result.expected.surfaces); expect(result.warm.surfaces).toEqual(result.expected.surfaces); }
  const formatting = await page.evaluate(() => { const h = (window as any).fitTest, first = h.make(), original = h.make(); for (const record of [first, original]) record.card.querySelector('.ms-text').innerHTML = '<strong>Formatted text</strong> with <span style="letter-spacing:2px">spacing</span>'; return { first: h.fit(first), original: h.fit(original, true) }; });
  expect(formatting.first.reads).toBeGreaterThan(0); expect(formatting.first.surfaces).toEqual(formatting.original.surfaces);
});

test('resize restoration stays idempotent and fixed sizing restores the original surface declarations', async ({ page }) => {
  const result = await page.evaluate(() => {
    const h = (window as any).fitTest, cached = h.make(), original = h.make(); const sizes = [[300, 180], [90, 50], [600, 400], [90, 50], [300, 180]];
    const steps = sizes.map(([width, height]) => ({ cached: h.fit(cached, false, width, height), original: h.fit(original, true, width, height) }));
    cached.node.style.contentSizing = 'fixed'; original.node.style.contentSizing = 'fixed'; return { steps, restored: h.fit(cached), expected: h.fit(original, true) };
  });
  for (const step of result.steps) { expect(step.cached.scale).toBe(step.original.scale); expect(step.cached.surfaces).toEqual(step.original.surfaces); }
  expect(result.steps[3]!.cached.reads).toBe(0); expect(result.steps[4]!.cached.reads).toBe(0); expect(result.restored.surfaces).toEqual(result.expected.surfaces); expect(result.restored.scale).toBe(1);
});

test('font loading invalidates measurements and custom fonts or active controls bypass the cache', async ({ page }) => {
  const result = await page.evaluate(() => {
    const h = (window as any).fitTest; h.fit(h.make()); const warm = h.fit(h.make()); document.fonts.dispatchEvent(new Event('loadingdone')); const cleared = h.fit(h.make());
    const face = new FontFace('QA Custom', 'local("Arial")'); document.fonts.add(face);
    const first = h.fit(h.make({ family: 'QA Custom, sans-serif' })), second = h.fit(h.make({ family: 'QA Custom, sans-serif' }));
    document.fonts.delete(face);
    const one = h.make(), two = h.make(); for (const record of [one, two]) { const button = document.createElement('button'); button.textContent = 'Control'; record.card.querySelector('.ms-node-body').append(button); }
    return { warm, cleared, first, second, control: [h.fit(one), h.fit(two)] };
  });
  expect(result.warm.reads).toBe(0); expect(result.cleared.reads).toBeGreaterThan(0); expect(result.first.reads).toBeGreaterThan(0); expect(result.second.reads).toBeGreaterThan(0); for (const control of result.control) expect(control.reads).toBeGreaterThan(0);
});

test('inherited typography invalidates fits and legacy spatial text tiles remain cacheable', async ({ page }) => {
  const result = await page.evaluate(() => {
    const h = (window as any).fitTest;
    const make = () => { const record = h.make({ containerVariant: 'contained', behavior: 'spatial' }); record.card.style.removeProperty('font-family'); return record; };
    const world = document.querySelector('main') as HTMLElement; world.style.fontFamily = 'Arial, sans-serif';
    const cold = h.fit(make()), warm = h.fit(make()); world.style.fontFamily = 'Georgia, serif';
    const changed = h.fit(make()), expected = h.fit(make(), true), repeated = h.fit(make()); return { cold, warm, changed, expected, repeated };
  });
  expect(result.cold.reads).toBeGreaterThan(0); expect(result.warm.reads).toBe(0); expect(result.changed.reads).toBeGreaterThan(0); expect(result.repeated.reads).toBe(0);
  expect(result.changed.scale).toBe(result.expected.scale); expect(result.repeated.surfaces).toEqual(result.expected.surfaces);
});

test('documents and embedded document surfaces bypass the simple text cache', async ({ page }) => {
  const results = await page.evaluate(() => {
    const h = (window as any).fitTest;
    return [{ kind: 'document' }, { assetId: 'document-asset' }, { contentType: 'document' }].map(changes => {
      const first = h.fit(h.make(changes)), repeated = h.fit(h.make(changes)), expected = h.fit(h.make(changes), true); return { first, repeated, expected };
    });
  });
  for (const result of results) { expect(result.first.reads).toBeGreaterThan(0); expect(result.repeated.reads).toBeGreaterThan(0); expect(result.repeated.surfaces).toEqual(result.expected.surfaces); }
});

test('static link text and URL invalidate fitting while native anchor activation remains intact', async ({ page }) => {
  const result = await page.evaluate(() => {
    const h = (window as any).fitTest;
    const make = (label = 'Read the type reference', url = '#reference') => {
      const record = h.make({ containerVariant: 'contained', contentType: 'link', hyperlinks: [{ label, url }] });
      const anchor = document.createElement('a'); anchor.className = 'ms-content-link'; anchor.href = url; anchor.textContent = label;
      record.card.querySelector('.ms-node-body').replaceChildren(anchor); return record;
    };
    const cold = h.fit(make()), warmRecord = make(), warm = h.fit(warmRecord);
    const changes = [['Longer changed reference text '.repeat(10), '#reference'], ['Read the type reference', '#changed']].map(([label, url]) => {
      const first = h.fit(make(label, url)), expected = h.fit(make(label, url), true), repeated = h.fit(make(label, url)); return { first, expected, repeated };
    });
    warmRecord.card.dataset.linkActivation = 'true'; return { cold, warm, changes };
  });
  expect(result.cold.reads).toBeGreaterThan(0); expect(result.warm.reads).toBe(0);
  for (const change of result.changes) { expect(change.first.reads).toBeGreaterThan(0); expect(change.repeated.reads).toBe(0); expect(change.repeated.surfaces).toEqual(change.expected.surfaces); }
  const anchor = page.locator('[data-link-activation] a'); await anchor.focus(); await page.keyboard.press('Enter'); await expect(page).toHaveURL(/#reference$/);
});

test('cached raw fit values preserve native glyph geometry at extreme dimensions and zoom', async ({ page }) => {
  const results = await page.evaluate(() => {
    const h = (window as any).fitTest;
    const bounds = (record: any) => [...record.card.querySelectorAll('.ms-node-header,.ms-node-body,.ms-node-title,.ms-text')].map((element: Element) => {
      const box = element.getBoundingClientRect(); return { x: box.x, y: box.y, width: box.width, height: box.height };
    });
    return [[40000.123456789, 36000.987654321, 1], [3.123456789, 4.987654321, 200], [.03123456789, .04987654321, 10000]].map(([width, height, zoom]) => {
      const first = h.make({ width, height }), fresh = h.make({ width, height }), original = h.make({ width, height });
      for (const record of [first, fresh, original]) record.card.style.transform = `scale(${zoom})`;
      h.fit(first); const warm = h.fit(fresh); h.fit(original, true); return { reads: warm.reads, actual: bounds(fresh), expected: bounds(original) };
    });
  });
  for (const result of results) { expect(result.reads).toBe(0); expect(result.actual).toEqual(result.expected); }
});

test('renderer pass reuses fits without measuring descendant styles and preserves exact geometry', async ({ page }) => {
  const result = await page.evaluate(() => {
    const h = (window as any).fitTest, fit = (record: any) => h.fit(record, false, record.node.width, record.node.height, true);
    let computed = 0; const originalStyle = window.getComputedStyle; window.getComputedStyle = (...args) => { if (args[0].closest('.ms-node')) computed++; return originalStyle(...args); };
    const cold = fit(h.make()); computed = 0; const fresh = h.make(), warm = fit(fresh), warmStyles = computed;
    const original = h.make(), expected = h.fit(original, true);
    const geometry = (record: any) => [...record.card.querySelectorAll('.ms-node-header,.ms-node-body,.ms-node-title,.ms-text')].map((element: Element) => element.getBoundingClientRect().toJSON());
    return { cold, warm, expected, warmStyles, actualGeometry: geometry(fresh), expectedGeometry: geometry(original) };
  });
  expect(result.cold.pass).toBe(true); expect(result.warm.pass).toBe(true); expect(result.warm.reads).toBe(0); expect(result.warmStyles).toBe(0);
  expect(result.warm.surfaces).toEqual(result.expected.surfaces); expect(result.actualGeometry).toEqual(result.expectedGeometry);
});

test('renderer pass invalidates inherited fonts, CSS variables and stylesheet CSSOM edits', async ({ page }) => {
  const results = await page.evaluate(() => {
    const h = (window as any).fitTest, world = document.querySelector('main') as HTMLElement;
    const make = () => { const record = h.make(); record.card.style.removeProperty('font-family'); return record; };
    const fit = (record: any) => h.fit(record, false, record.node.width, record.node.height, true);
    fit(make()); world.style.fontFamily = 'Georgia, serif';
    const compare = () => { const first = fit(make()), repeated = fit(make()), expected = h.fit(make(), true); return { first, repeated, expected }; };
    const results = [compare()]; world.style.setProperty('--ms-title-size', '35px'); world.style.letterSpacing = '2px'; results.push(compare());
    const style = document.createElement('style'); style.textContent = '.ms-node-title{line-height:1.8!important}'; document.head.append(style); results.push(compare());
    (style.sheet!.cssRules[0] as CSSStyleRule).style.lineHeight = '2.2'; results.push(compare());
    return results;
  });
  for (const result of results) { expect(result.first.pass).toBe(true); expect(result.first.reads).toBeGreaterThan(0); expect(result.repeated.reads).toBe(0); expect(result.repeated.surfaces).toEqual(result.expected.surfaces); }
});

test('dynamic embedding styles and custom fonts use the conservative fitting path', async ({ page }) => {
  const result = await page.evaluate(() => {
    const h = (window as any).fitTest, fit = () => h.fit(h.make(), false, 300, 180, true);
    const normal = fit(), style = document.createElement('style'); style.textContent = '.ms-node:hover .ms-text{font-size:31px}'; document.head.append(style);
    const hover = fit(); style.remove();
    const face = new FontFace('Pass Custom', 'local("Arial")'); document.fonts.add(face); const font = fit(); document.fonts.delete(face);
    return { normal, hover, font };
  });
  expect(result.normal.pass).toBe(true); expect(result.hover.pass).toBe(true); expect(result.hover.reads).toBeGreaterThan(0); expect(result.font.pass).toBe(false);
});


test('the installed editor stylesheet preserves scene caching despite unrelated dynamic UI rules', async ({ page }) => {
  await page.addStyleTag({content: appCss});
  const result = await page.evaluate(() => {
    const h = (window as any).fitTest, fit = (record: any) => h.fit(record, false, record.node.width, record.node.height, true);
    document.body.insertAdjacentHTML('beforeend', '<footer class="canvas-status"><span>Status</span><span>Work</span></footer><label class="font-picker-control"><span>Font</span></label>');
    let computed = 0; const originalStyle = window.getComputedStyle;
    window.getComputedStyle = (...args) => { if (args[0].closest('.ms-node')) computed++; return originalStyle(...args); };
    const cold = fit(h.make()); computed = 0; const warm = fit(h.make()), warmStyles = computed;
    return {cold, warm, warmStyles, expected: h.fit(h.make(), true)};
  });
  expect(result.cold.pass).toBe(true); expect(result.cold.reads).toBeGreaterThan(0);
  expect(result.warm.pass).toBe(true); expect(result.warm.reads).toBe(0); expect(result.warmStyles).toBe(0);
  expect(result.warm.surfaces).toEqual(result.expected.surfaces); expect(result.warm.scale).toBe(result.expected.scale);
});

test('transient, structural and relational scene selectors retain conservative descendant measurements', async ({ page }) => {
  const results = await page.evaluate(() => {
    const h = (window as any).fitTest, fit = (record: any) => h.fit(record, false, record.node.width, record.node.height, true);
    let computed = 0; const originalStyle = window.getComputedStyle;
    window.getComputedStyle = (...args) => { if (args[0].closest('.ms-node')) computed++; return originalStyle(...args); };
    return ['.ms-node:hover .ms-text', '.ms-node:is(:hover,:focus-visible) .ms-text', '.ms-node:not(:hover) .ms-text', '.ms-node:nth-child(2) .ms-text', '.ms-node:has(+ .ms-node) .ms-text', '.ms-node + .ms-node .ms-text', '.ms-node:hover'].map(selector => {
      document.querySelector('main')!.replaceChildren();
      const style = document.createElement('style'); style.textContent = selector === '.ms-node:hover' ? `${selector}{border:8px solid red}` : `${selector}{font-size:31px}`; document.head.append(style);
      fit(h.make()); computed = 0; const record = h.make(), measured = fit(record), styleReads = computed;
      const original = h.make(); record.card.replaceWith(original.card); const expected = h.fit(original, true); style.remove();
      return {selector, measured, styleReads, expected};
    });
  });
  for (const result of results) {
    expect(result.measured.pass, result.selector).toBe(true); expect(result.styleReads, result.selector).toBeGreaterThan(0);
    expect(result.measured.surfaces, result.selector).toEqual(result.expected.surfaces); expect(result.measured.scale, result.selector).toBe(result.expected.scale);
  }
});
