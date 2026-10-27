const fs = require('fs');
const path = require('path');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const [configurationFile, output] = process.argv.slice(2);
  const configuration = JSON.parse(fs.readFileSync(configurationFile, 'utf8'));
  const browser = await chromium.launch({ headless: true,
    executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  const report = { result: 'HYPERLIB_HEADER_PREVIEW_DRAG_BROWSER_OK', checks: [], screenshots: [] };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.checks.push(message); };
  const injection = `window.__dragMessages=[];window.chrome??={};window.chrome.webview={postMessage:message=>window.__dragMessages.push(JSON.parse(JSON.stringify(message)))};\n`
    + fs.readFileSync(configuration.dragScript, 'utf8');
  let page;
  try {
    page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
    await page.addInitScript({ content: injection });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    let generation = 0;
    const element = id => page.locator(`[data-header-element="${id}"]`);
    const commits = () => page.evaluate(() => window.__dragMessages.filter(message => message.type === 'studio-header-drag'));
    async function configure() {
      const config = { token: 'qa-token-' + (++generation), generation, documentId: 'qa-document' };
      check(await page.evaluate(config => window.hyperlibGuidedHeaderDrag.configure(config), config), 'The current native-issued drag capability becomes ready.');
      const ready = await page.evaluate(() => window.__dragMessages.filter(message => message.type === 'studio-header-drag-ready').at(-1));
      check(ready.token === config.token && ready.generation === config.generation && ready.documentId === config.documentId,
        'The readiness acknowledgment carries the exact native capability.');
      return config;
    }
    async function bounds(id) {
      return element(id).evaluate(node => {
        const b = node.getBoundingClientRect(), parent = node.parentElement, p = parent.getBoundingClientRect();
        return { x: b.x, y: b.y, width: b.width, height: b.height, left: b.left, top: b.top, right: b.right, bottom: b.bottom,
          parentLeft: p.left + parent.clientLeft, parentTop: p.top + parent.clientTop, parentWidth: parent.clientWidth, parentHeight: parent.clientHeight,
          style: node.getAttribute('style'), parentStyle: parent.getAttribute('style') };
      });
    }
    async function pointerDrag(id, target, grip = false) {
      await element(id).scrollIntoViewIfNeeded();
      const b = await bounds(id);
      const start = { x: grip ? b.right - 12 : b.left + Math.min(20, b.width / 2), y: b.top + Math.min(12, b.height / 2) };
      await page.mouse.move(start.x, start.y); await page.mouse.down();
      const end = typeof target === 'function' ? target(b, start) : target;
      await page.mouse.move(end.x, end.y, { steps: 8 }); await page.mouse.up();
      return b;
    }
    async function dragToPercent(id, x, y, grip = false) {
      return pointerDrag(id, (b, start) => ({ x: b.parentLeft + b.parentWidth * x / 100 + start.x - b.left,
        y: b.parentTop + b.parentHeight * y / 100 + start.y - b.top }), grip);
    }
    await page.goto(configuration.staticUrl, { waitUntil: 'networkidle' });
    await page.locator('[data-guided-page-preview]').waitFor();
    check(await page.evaluate(() => Promise.race([window.hyperlibGuidedPreview.whenReady().then(() => true),
      new Promise(resolve => setTimeout(() => resolve(false), 1000))])),
      'A static header acknowledges readiness without waiting for a nonexistent interactive circuit.');
    await element('padded-image').locator('img').evaluate(image => image.decode());
    const initialUrl = page.url();
    await configure();
    const initial = await bounds('drag-text');
    await pointerDrag('drag-text', (b, start) => ({ x: start.x + 1, y: start.y + 1 }));
    check((await commits()).length === 0 && (await bounds('drag-text')).style === initial.style,
      'A click or sub-threshold pointer movement does not change an element or create a model commit.');
    const token = await configure();
    await dragToPercent('drag-text', 35, 30);
    const moved = (await commits()).at(-1);
    check(moved.token === token.token && moved.generation === token.generation && moved.documentId === token.documentId
      && moved.elementId === 'drag-text' && moved.parentId === 'drag-row' && moved.previousX === 8 && moved.previousY === 10
      && Math.abs(moved.x - 35) < .15 && Math.abs(moved.y - 30) < .15 && Number.isSafeInteger(moved.sequence),
      'A real pointer drag commits one exact-token message with stable IDs, original model coordinates and padded-parent percentages.');
    const count = (await commits()).length;
    await pointerDrag('drag-text', (b, start) => ({ x: start.x + 12, y: start.y + 10 }));
    check((await commits()).length === count, 'A consumed one-shot capability cannot commit another gesture.');
    await configure();
    await pointerDrag('drag-text', b => ({ x: b.parentLeft + b.parentWidth + 80, y: b.parentTop + b.parentHeight + 80 }));
    const clamped = await bounds('drag-text');
    check(clamped.left >= clamped.parentLeft - 1 && clamped.top >= clamped.parentTop - 1
      && clamped.right <= clamped.parentLeft + clamped.parentWidth + 1 && clamped.bottom <= clamped.parentTop + clamped.parentHeight + 1,
      'Dragging beyond the padded row clamps the whole visible element inside its actual content plane.');
    await configure();
    await element('nested-text').scrollIntoViewIfNeeded();
    const nestedBefore = await bounds('nested-text'), boxBefore = await bounds('drag-box');
    const nestedCommits = (await commits()).length;
    await pointerDrag('nested-text', (b, start) => ({ x: start.x + 35, y: start.y + 18 }));
    const nestedAfter = await bounds('nested-text'), boxAfter = await bounds('drag-box');
    check((await commits()).length === nestedCommits + 1 && (await commits()).at(-1).parentId === 'drag-box'
      && Math.abs(boxBefore.x - boxAfter.x) < .1 && Math.abs(boxBefore.y - boxAfter.y) < .1
      && nestedAfter.x > nestedBefore.x + 20 && nestedAfter.y > nestedBefore.y + 10,
      'Dragging a nested child changes only that child and identifies its immediate Box parent.');
    await configure();
    await element('drag-box').scrollIntoViewIfNeeded();
    const parentBefore = await bounds('drag-box'), childBefore = await bounds('nested-text');
    await pointerDrag('drag-box', (b, start) => ({ x: start.x + 30, y: start.y + 12 }), true);
    const parentAfter = await bounds('drag-box'), childAfter = await bounds('nested-text');
    check((await commits()).at(-1).elementId === 'drag-box'
      && Math.abs((parentAfter.x - parentBefore.x) - (childAfter.x - childBefore.x)) < .2
      && Math.abs((parentAfter.y - parentBefore.y) - (childAfter.y - childBefore.y)) < .2,
      'Dragging a Box by its hover grip moves its children with it without changing their local coordinates.');
    await configure();
    const canceledBefore = await bounds('nested-text'), canceledCount = (await commits()).length;
    await page.mouse.move(canceledBefore.left + 12, canceledBefore.top + 10); await page.mouse.down();
    await page.mouse.move(canceledBefore.left + 60, canceledBefore.top + 30, { steps: 5 }); await page.keyboard.press('Escape'); await page.mouse.up();
    check((await bounds('nested-text')).style === canceledBefore.style && (await commits()).length === canceledCount,
      'Escape restores the exact pre-drag style and sends no model commit.');
    const invalidated = await configure();
    const beforeInvalidate = await bounds('nested-text');
    await page.mouse.move(beforeInvalidate.left + 10, beforeInvalidate.top + 10); await page.mouse.down();
    await page.mouse.move(beforeInvalidate.left + 45, beforeInvalidate.top + 30, { steps: 4 });
    await page.evaluate(() => window.hyperlibGuidedHeaderDrag.configure(null)); await page.mouse.up();
    check((await bounds('nested-text')).style === beforeInvalidate.style && (await commits()).length === canceledCount,
      'A native draft invalidation cancels an active gesture and rolls it back without a stale commit.');
    await configure();
    check(!await page.evaluate(config => window.hyperlibGuidedHeaderDrag.configure(config), { ...invalidated, generation: invalidated.generation - 1 }),
      'An older generation cannot replace the current drag capability.');
    await pointerDrag('padded-image', (b, start) => ({ x: b.parentLeft + b.parentWidth + 20, y: b.parentTop + b.parentHeight + 20 }));
    const padded = await bounds('padded-image');
    const mediaGeometry = await element('padded-image').evaluate(node => {
      const outer = node.parentElement.closest('[data-header-element]').getBoundingClientRect(), parent = node.parentElement.getBoundingClientRect();
      return { dx: parent.left - outer.left, dy: parent.top - outer.top, width: parent.width, height: parent.height,
        objectPosition: getComputedStyle(node.querySelector('img')).objectPosition };
    });
    check(Math.abs(mediaGeometry.dx - 11) < .1 && Math.abs(mediaGeometry.dy - 11) < .1 && Math.abs(mediaGeometry.height - 78) < .1
      && Math.abs(padded.height - 78) < .1 && padded.right <= padded.parentLeft + padded.parentWidth + 1
      && padded.bottom <= padded.parentTop + padded.parentHeight + 1 && mediaGeometry.objectPosition === '0% 0%',
      'An image in a 100px Box respects its 10px padding and 1px border, uses the 78px content height, and stays aligned at the content origin. ' + JSON.stringify(mediaGeometry));
    // The drop consumes its capability, so arm again when checking browser-native image dragging.
    await configure();
    check(await element('padded-image').locator('img').evaluate(node => !node.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true }))),
      'Free-placement media prevents browser-native image dragging.');
    for (const id of ['horizontal-text', 'vertical-text']) {
      await configure(); const before = await bounds(id), beforeCount = (await commits()).length;
      await pointerDrag(id, (b, start) => ({ x: start.x + 50, y: start.y + 30 }));
      check((await commits()).length === beforeCount && (await bounds(id)).style === before.style
        && !await element(id).evaluate(node => node.hasAttribute('data-preview-header-draggable')),
        id + ': side-by-side and stacked flow elements do not drag or expose a movement grip.');
    }
    await configure();
    await pointerDrag('aligned-text', (b, start) => ({ x: start.x - 90, y: start.y + 18 }));
    const alignedCommit = (await commits()).at(-1);
    check(alignedCommit.elementId === 'aligned-text' && alignedCommit.previousX === 1 && alignedCommit.previousY === 5
      && await element('aligned-text').evaluate(node => node.style.transform === 'none' && node.style.right === 'auto'),
      'Dragging an aligned element begins at its visible location and supplies manual coordinates while retaining the original model baseline.');
    check(page.url() === initialUrl, 'Static dragging never navigates or reloads the document.');
    const staticShot = path.join(output, 'header-preview-drag-static.png'); await page.screenshot({ path: staticShot, fullPage: true }); report.screenshots.push(staticShot);

    await page.goto(configuration.interactiveUrl, { waitUntil: 'domcontentloaded' }); generation = 0;
    await page.waitForFunction(() => document.documentElement.dataset.guidedInteractiveReady === 'true');
    await page.evaluate(() => window.hyperlibGuidedPreview.whenReady());
    const counter = element('drag-counter').locator('.hl-counter'), wave = element('drag-wave').locator('.hl-wave');
    await configure();
    await counter.locator('[data-action="add"]').click(); await counter.locator('output').filter({ hasText: /^8$/ }).waitFor();
    await wave.getByRole('button', { name: 'Shift phase' }).click(); await wave.locator('output').filter({ hasText: '1 shifts' }).waitFor();
    check((await commits()).length === 0, 'Normal JavaScript Counter and C# Wave buttons work without creating drag commits.');
    await page.evaluate(() => { window.__dragCounter = document.querySelector('[data-header-element="drag-counter"] .hl-counter');
      window.__dragWave = document.querySelector('[data-header-element="drag-wave"] .hl-wave'); window.__dragDocument = document; });
    await configure();
    const toolBefore = await bounds('drag-counter');
    await pointerDrag('drag-counter', (b, start) => ({ x: start.x + 40, y: start.y + 25 }), true);
    check((await commits()).at(-1)?.elementId === 'drag-counter' && (await bounds('drag-counter')).x > toolBefore.x + 25
      && await counter.locator('output').textContent() === '8' && (await wave.locator('output').textContent()).includes('1 shifts'),
      'A real interactive library tool moves by its preview grip while its JavaScript and C# runtime state stays intact.');
    await configure();
    const protectedCount = (await commits()).length;
    const toolBody = counter.locator('output'); await toolBody.scrollIntoViewIfNeeded(); const toolBodyBox = await toolBody.boundingBox();
    await page.mouse.move(toolBodyBox.x + 4, toolBodyBox.y + 4); await page.mouse.down();
    await page.mouse.move(toolBodyBox.x + 60, toolBodyBox.y + 30, { steps: 5 }); await page.mouse.up();
    check((await commits()).length === protectedCount, 'Dragging a tool output or control surface does not move its parent Template.');
    await configure();
    await dragToPercent('drag-text', 35, 30);
    const interactiveDrop = (await commits()).at(-1);
    check(interactiveDrop.elementId === 'drag-text' && Math.abs(interactiveDrop.x - 35) < .15 && Math.abs(interactiveDrop.y - 30) < .15,
      'Interactive headers use the same pointer-to-model coordinate contract as static headers.');
    await page.evaluate(() => window.hyperlibGuidedHeaderDrag.configure(null));
    check(await page.evaluate(id => window.hyperlibGuidedPreview.update(id, 100), configuration.committedRequestId),
      'The actual interactive circuit acknowledges a committed geometry render.');
    check(await element('drag-text').evaluate(node => Number(node.dataset.headerX) === 35 && Number(node.dataset.headerY) === 30)
      && await counter.locator('output').textContent() === '8' && (await wave.locator('output').textContent()).includes('1 shifts')
      && await page.evaluate(() => document === window.__dragDocument
        && document.querySelector('[data-header-element="drag-counter"] .hl-counter') === window.__dragCounter
        && document.querySelector('[data-header-element="drag-wave"] .hl-wave') === window.__dragWave),
      'The render acknowledgment follows the new model coordinates and preserves the current document and real tool instances.');
    await configure();
    await element('nested-text').scrollIntoViewIfNeeded();
    const wheelStart = await bounds('nested-text');
    await page.mouse.move(wheelStart.left + 12, wheelStart.top + 10); await page.mouse.down();
    await page.mouse.move(wheelStart.left + 22, wheelStart.top + 18, { steps: 3 });
    const scrollBefore = await page.evaluate(() => scrollY); await page.mouse.wheel(0, 90);
    await page.waitForFunction(before => scrollY > before, scrollBefore);
    await page.mouse.move(wheelStart.left + 42, wheelStart.top + 25, { steps: 3 }); await page.mouse.up();
    const afterWheel = await bounds('nested-text');
    check((await commits()).at(-1).elementId === 'nested-text' && afterWheel.left >= afterWheel.parentLeft - 1
      && afterWheel.right <= afterWheel.parentLeft + afterWheel.parentWidth + 1
      && afterWheel.top >= afterWheel.parentTop - 1 && afterWheel.bottom <= afterWheel.parentTop + afterWheel.parentHeight + 1,
      'The mouse wheel continues to scroll during a pointer drag, and the drop uses the current scrolled parent bounds.');
    const interactiveShot = path.join(output, 'header-preview-drag-interactive.png'); await page.screenshot({ path: interactiveShot, fullPage: true }); report.screenshots.push(interactiveShot);
    check(errors.length === 0, 'Static and interactive pointer dragging have no uncaught browser errors. ' + errors.join('; '));
    fs.writeFileSync(path.join(output, 'header-preview-drag-browser-result.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ result: report.result, assertions: report.checks.length }));
  } catch (error) {
    if (page) await page.screenshot({ path: path.join(output, 'header-preview-drag-failure.png'), fullPage: true }).catch(() => {});
    throw error;
  } finally { await browser.close(); }
})().catch(error => { console.error(error.stack || String(error)); process.exitCode = 1; });
