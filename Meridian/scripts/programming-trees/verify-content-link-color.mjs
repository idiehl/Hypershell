/** Stage only a shared CSS rule over exact installed bytes; render proof without host writes. */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { build } from 'esbuild';
import { chromium } from 'playwright';
import { addNode, newProject } from '../../src/core/model.ts';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const OUT = path.join(ROOT, 'output/programming-trees-20261021/link-color-fix');
const RUNTIME = path.join(ROOT, 'output/app/wwwroot');
const ORIGIN = 'http://127.0.0.1:5240';
const RULE = '.ms-node.ms-custom-text .ms-content-link{color:var(--ms-text-color)}';
const PATCH = `\n/* Match native content hyperlinks to explicitly authored node typography. */\n${RULE}\n`;
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const rgb = value => value.startsWith('#') ? [1, 3, 5].map(at => parseInt(value.slice(at, at + 2), 16)) : value.match(/[\d.]+/g).slice(0, 3).map(Number);
const luminance = value => rgb(value).map(component => { const s = component / 255; return s <= .04045 ? s / 12.92 : ((s + .055) / 1.055) ** 2.4; }).reduce((sum, component, at) => sum + component * [.2126, .7152, .0722][at], 0);
const contrast = (a, b) => { const x = luminance(a), y = luminance(b); return (Math.max(x, y) + .05) / (Math.min(x, y) + .05); };
await Promise.all(['original', 'staged'].map(folder => mkdir(path.join(OUT, folder), { recursive: true })));
assert.ok((await readFile(path.join(ROOT, 'src/scene.css'), 'utf8')).includes(RULE), 'Maintained source lacks the verified shared rule.');
const files = [];
for (const name of ['app.css', 'scene.css']) {
  const original = await readFile(path.join(RUNTIME, name));
  const response = await fetch(`${ORIGIN}/${name}`, { cache: 'no-store' });
  assert.equal(response.status, 200); const served = Buffer.from(await response.arrayBuffer());
  assert.deepEqual(served, original, `The served ${name} does not match the expected runtime file.`);
  assert.ok(!original.toString('utf8').includes(RULE), `Runtime ${name} already has this rule; do not restage against a different baseline.`);
  const staged = Buffer.concat([original, Buffer.from(PATCH, 'utf8')]);
  await writeFile(path.join(OUT, 'original', name), original); await writeFile(path.join(OUT, 'staged', name), staged);
  assert.deepEqual(staged.subarray(0, original.length), original);
  assert.equal(staged.subarray(original.length).toString('utf8'), PATCH);
  files.push({ name, runtimePath: path.join(RUNTIME, name), originalPath: path.join(OUT, 'original', name), stagedPath: path.join(OUT, 'staged', name),
    originalSHA256: sha(original), stagedSHA256: sha(staged), originalBytes: original.length, stagedBytes: staged.length, servedMatchesRuntime: true });
}
const script = (await build({ stdin: { contents: "import{renderScene}from'./src/scene.ts';window.linkColorRender=renderScene;", resolveDir: ROOT, loader: 'ts' }, bundle: true, write: false, platform: 'browser', format: 'iife', target: 'es2022' })).outputFiles[0].text;
const project = newProject('Authored hyperlink contrast');
const examples = [
  { key: 'light', x: 40, y: 70, fill: '#EEE9FF', textColor: '#171727', title: 'Light authored surface' },
  { key: 'dark', x: 530, y: 70, fill: '#1B2030', textColor: '#F5F4FA', title: 'Dark authored surface' },
  { key: 'default', x: 40, y: 330, fill: '#1B2030', title: 'Default link theme remains unchanged' },
];
for (const example of examples) {
  const node = addNode(project, project.entryCanvasId, 'media', example.title);
  Object.assign(node, { x: example.x, y: example.y, width: 440, height: 210, sizeMode: 'fixed', contentType: 'link',
    hyperlinks: [{ url: `http://127.0.0.1:5439/destination?surface=${example.key}`, label: `Open ${example.key} reference` }],
    style: { shape: 'rectangle', fill: example.fill, border: '#9C98B0', ...(example.textColor ? { textColor: example.textColor } : {}), titleSize: 24, fontSize: 20, contentSizing: 'fixed', padding: 16 } });
  example.nodeId = node.id;
}
const browser = await chromium.launch({ headless: true });
const results = [];
try {
  const context = await browser.newContext({ viewport: { width: 1180, height: 760 }, deviceScaleFactor: 1 });
  await context.route('**/*', route => {
    const url = new URL(route.request().url());
    if (url.origin === 'http://127.0.0.1:5439' && url.pathname === '/destination') return route.fulfill({ contentType: 'text/html', body: '<title>Verified reference destination</title><p>Navigation retained.</p>' });
    return route.abort();
  });
  for (const mode of ['app', 'scene']) {
    const page = await context.newPage();
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.setContent('<style>body{margin:0;background:#101720;color:#e5eef5}#chrome{position:absolute;top:12px;left:40px}main{width:1120px;height:700px;margin-top:35px}</style><a id="chrome" href="#chrome">Application link outside the Work</a><main></main>');
    await page.addStyleTag({ content: await readFile(path.join(OUT, 'original', `${mode}.css`), 'utf8') });
    await page.addScriptTag({ content: script });
    await page.evaluate(project => window.linkColorRender(document.querySelector('main'), project, project.entryCanvasId, { readonly: true, view: { x: 0, y: 0, scale: 1 } }), project);
    const inspect = () => page.evaluate(examples => ({ links: examples.map(example => {
      const anchor = document.querySelector(`[data-node-id="${example.nodeId}"] .ms-content-link`), style = getComputedStyle(anchor);
      return { key: example.key, color: style.color, decoration: style.textDecorationLine, cursor: style.cursor, pointerEvents: style.pointerEvents, href: anchor.href, target: anchor.target, rel: anchor.rel };
    }), chromeColor: getComputedStyle(document.querySelector('#chrome')).color }), examples);
    const before = await inspect();
    await page.screenshot({ path: path.join(OUT, `${mode}-before.png`), fullPage: true });
    await page.addStyleTag({ content: PATCH });
    const after = await inspect();
    for (const example of examples.filter(example => example.textColor)) {
      const first = before.links.find(link => link.key === example.key), second = after.links.find(link => link.key === example.key);
      assert.notEqual(first.color, second.color, `${mode}/${example.key} did not expose the preexisting mismatch.`);
      assert.deepEqual(rgb(second.color), rgb(example.textColor));
      assert.equal(second.decoration, first.decoration); assert.ok(second.decoration.includes('underline'));
      for (const field of ['cursor', 'pointerEvents', 'href', 'target', 'rel']) assert.equal(second[field], first[field]);
      assert.ok(contrast(second.color, example.fill) > 7, `${mode}/${example.key} contrast is insufficient.`);
    }
    assert.deepEqual(after.links.find(link => link.key === 'default'), before.links.find(link => link.key === 'default'));
    assert.equal(after.chromeColor, before.chromeColor, 'Rule changed an application link outside the Work.');
    const anchor = page.locator(`[data-node-id="${examples[0].nodeId}"] .ms-content-link`);
    await anchor.focus(); await page.keyboard.press('Tab'); await page.keyboard.press('Shift+Tab');
    const focus = await anchor.evaluate(element => { const style = getComputedStyle(element); return { focused: document.activeElement === element, outlineStyle: style.outlineStyle, outlineWidth: style.outlineWidth }; });
    assert.equal(focus.focused, true); assert.notEqual(focus.outlineStyle, 'none');
    await page.screenshot({ path: path.join(OUT, `${mode}-after.png`), fullPage: true });
    const popupPromise = page.waitForEvent('popup'); await anchor.click(); const popup = await popupPromise;
    await popup.waitForLoadState(); assert.equal(new URL(popup.url()).searchParams.get('surface'), 'light');
    await popup.close(); assert.deepEqual(errors, []);
    results.push({ mode: mode === 'app' ? 'installed editor CSS' : 'installed standalone viewer CSS', before, after, focus,
      contrasts: examples.filter(example => example.textColor).map(example => ({ key: example.key,
        before: contrast(before.links.find(link => link.key === example.key).color, example.fill),
        after: contrast(after.links.find(link => link.key === example.key).color, example.fill) })),
      originalNavigationRetained: true, defaultAndChromeLinksUnchanged: true, browserErrors: errors });
    await page.close();
  }
  await context.close();
} finally { await browser.close(); }
const manifest = { passed: true, origin: ORIGIN, runtimeRoot: RUNTIME, sourceFile: 'src/scene.css', rule: RULE,
  patchBytes: Buffer.byteLength(PATCH), files, results, stagedOnly: true, liveWrites: false, savedDataMutated: false,
  javascriptOrBinariesChanged: false, publicationOrDistChanged: false,
  deployment: 'Root must compare each runtime file with originalSHA256 immediately before copying its staged replacement, retain exact originals, copy only these two CSS files, then GET app.css and scene.css to prove stagedSHA256. No backend/native restart is needed. Existing pages need a stylesheet refresh before observing the rule.' };
await writeFile(path.join(OUT, 'verification.json'), JSON.stringify(manifest, null, 2) + '\n');
console.log(JSON.stringify({ passed: true, stagedOnly: true, out: OUT, files: files.map(({ name, originalSHA256, stagedSHA256 }) => ({ name, originalSHA256, stagedSHA256 })), contrasts: results.map(result => ({ mode: result.mode, contrasts: result.contrasts })) }));
