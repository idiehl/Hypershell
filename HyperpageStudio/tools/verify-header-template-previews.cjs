const fs = require('fs');
const path = require('path');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

async function verifyNavigation(page, check) {
  const before = page.url(); let popups = 0; const popup = () => popups++; page.on('popup', popup);
  check(await page.locator('[data-block-type="studio-navigation"]').count() === 0,
    'The interactive builder omits synthetic Page title and Another Page navigation.');
  await page.evaluate(() => {
    window.__navigationDocument = document; window.__navigationRoot = document.querySelector('[data-guided-page-preview]');
    const controls = document.createElement('section'); controls.id = 'preview-navigation-checks';
    controls.innerHTML = '<a id="preview-root-link" href="/">Host root</a><a id="preview-missing-link" href="/page/unavailable">Unavailable Page</a><a id="preview-empty-link" href="#">Empty placeholder</a><a id="preview-window-link" href="/" target="_blank">Another window</a><a id="preview-anchor-link" href="#preview-real-anchor">Real anchor</a><div id="preview-real-anchor" style="margin-top:1400px;height:40px">Actual target</div>';
    document.body.append(controls);
  });
  for (const id of ['preview-root-link', 'preview-missing-link', 'preview-empty-link', 'preview-window-link']) {
    await page.locator('#' + id).click();
    check(page.url() === before && await page.evaluate(() => document === window.__navigationDocument),
      `${id}: clicking a preview link preserves the current document and route.`);
  }
  await page.locator('#preview-anchor-link').click();
  check(await page.evaluate(() => scrollY > 500 && location.hash === '#preview-real-anchor' && document === window.__navigationDocument
    && document.querySelector('[data-guided-page-preview]') === window.__navigationRoot), 'A real in-page anchor scrolls inside the current interactive preview.');
  check(popups === 0, 'Preview links cannot open a second browser window.');
  await page.evaluate(before => { history.replaceState(null, '', before); document.getElementById('preview-navigation-checks').remove(); scrollTo(0, 0); }, before);
  page.off('popup', popup);
}

async function verifyBoundedImageTemplate(page, check, label, followingText) {
  const bounds = await page.locator('[data-header-element="library-image"]').evaluate((element, followingText) => {
    const caption = element.querySelector('figcaption');
    const previousTop = element.scrollTop;
    element.scrollTop = element.scrollHeight;
    const box = element.getBoundingClientRect(), text = caption.getBoundingClientRect();
    const following = document.querySelector('[data-header-element="library-text"]').getBoundingClientRect();
    const result = { width: box.width, height: box.height, clientHeight: element.clientHeight, scrollHeight: element.scrollHeight,
      overflow: getComputedStyle(element).overflowY, moved: element.scrollTop > 0,
      captionWithin: text.top >= box.top - 1 && text.bottom <= box.bottom + 1,
      followingBelow: !followingText || following.top >= box.bottom - 1 };
    element.scrollTop = previousTop;
    return result;
  }, followingText);
  check(Math.abs(bounds.width - 128) < 1 && Math.abs(bounds.height - 96) < 1
    && bounds.scrollHeight > bounds.clientHeight && ['auto', 'scroll'].includes(bounds.overflow)
    && bounds.moved && bounds.captionWithin && bounds.followingBelow,
    label + ': the explicit-height image template keeps its caption in a working 128 by 96 pixel scroller without overlapping following content. ' + JSON.stringify(bounds));
}

(async () => {
  const [configurationFile, output] = process.argv.slice(2);
  const configuration = JSON.parse(fs.readFileSync(configurationFile, 'utf8'));
  const browser = await chromium.launch({ headless: true,
    executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  const report = { result: 'HYPERLIB_HEADER_TEMPLATE_BROWSER_OK', checks: [], screenshots: [] };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.checks.push(message); };
  let page;
  try {
    page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    const failedNavigations = []; page.on('response', response => {
      if (response.request().isNavigationRequest() && response.request().frame() === page.mainFrame() && response.status() === 404) failedNavigations.push(response.url());
    });
    await page.goto(configuration.firstUrl, { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => document.documentElement.dataset.guidedInteractiveReady === 'true');
    const first = page.locator('[data-header-element="counter-one"] .hl-counter');
    const second = page.locator('[data-header-element="counter-two"] .hl-counter');
    await first.locator('[data-action="add"]').click();
    await first.locator('output').filter({ hasText: /^8$/ }).waitFor();
    check(await second.locator('output').textContent() === '9', 'Two instances of the same real JavaScript library tool keep independent state.');
    const wave = page.locator('[data-header-element="wave-tool"] .hl-wave');
    await wave.getByRole('button', { name: 'Shift phase' }).click();
    await wave.locator('output').filter({ hasText: '1 shifts' }).waitFor();
    check(true, 'The actual C# wave tool handles its Blazor button inside the reusable header.');
    check(await page.locator('[data-header-element="nested-box"] [data-header-element="counter-two"] .hl-counter').count() === 1,
      'A real library object renders inside a nested header Box.');
    check((await page.locator('[data-header-element="library-text"]').textContent()).includes('Real library text, placed inside the reusable header.'),
      'The selected text template renders its actual configured content.');
    const image = page.locator('[data-header-element="library-image"] img'); await image.evaluate(image => image.decode());
    check(await image.evaluate(image => image.naturalWidth === 220 && image.alt === 'Owned mountain illustration'),
      'A template media field renders the actual imported local image.');
    await page.evaluate(() => {
      window.__templateDocument = document;
      window.__templateMain = document.querySelector('[data-region="main"]');
      window.__firstTool = document.querySelector('[data-header-element="counter-one"] .hl-counter');
      window.__secondTool = document.querySelector('[data-header-element="counter-two"] .hl-counter');
      window.__waveTool = document.querySelector('[data-header-element="wave-tool"] .hl-wave');
    });
    await verifyNavigation(page, check);
    check(await first.locator('output').textContent() === '8' && await second.locator('output').textContent() === '9'
      && (await wave.locator('output').textContent()).includes('1 shifts'), 'Blocked links and working in-page anchors preserve JavaScript and C# tool state.');
    const originalUrl = page.url();
    check(await page.evaluate(id => window.hyperlibGuidedPreview.update(id, 1), configuration.geometryRequestId), 'The existing circuit accepts a new header geometry draft.');
    await page.waitForFunction(() => document.documentElement.dataset.guidedUpdateVersion === '1');
    check(page.url() === originalUrl && await page.evaluate(() => document === window.__templateDocument && document.querySelector('[data-region="main"]') === window.__templateMain
      && document.querySelector('[data-header-element="counter-one"] .hl-counter') === window.__firstTool
      && document.querySelector('[data-header-element="counter-two"] .hl-counter') === window.__secondTool
      && document.querySelector('[data-header-element="wave-tool"] .hl-wave') === window.__waveTool),
      'Geometry edits preserve the document, current Page body, and existing real tool instances.');
    check(await first.locator('output').textContent() === '8' && await second.locator('output').textContent() === '9'
      && (await wave.locator('output').textContent()).includes('1 shifts'), 'Header geometry changes preserve both JavaScript and C# tool state.');
    check(await page.evaluate(id => window.hyperlibGuidedPreview.update(id, 2), configuration.fieldsRequestId), 'The existing circuit accepts edited template field values.');
    await first.locator('[data-field="label"]').filter({ hasText: 'Updated actual template' }).waitFor();
    await first.locator('output').filter({ hasText: /^20$/ }).waitFor();
    check(await second.locator('output').textContent() === '9', 'Editing one template instance updates its real content while retaining the other instance.');
    await page.evaluate(({ stale, latest }) => Promise.all([window.hyperlibGuidedPreview.update(stale, 3), window.hyperlibGuidedPreview.update(latest, 4)]),
      { stale: configuration.staleRequestId, latest: configuration.latestRequestId });
    await first.locator('[data-field="label"]').filter({ hasText: 'Latest field update' }).waitFor();
    check(await page.evaluate(() => document.documentElement.dataset.guidedUpdateVersion === '4'), 'Only the latest queued template draft reaches the current component tree.');
    // Change runtime state away from the configured starts. A remounted tool would otherwise reset
    // to the same value and make a broken keyed-reorder implementation appear to preserve state.
    await first.locator('[data-action="add"]').click(); await first.locator('output').filter({ hasText: /^21$/ }).waitFor();
    await second.locator('[data-action="add"]').click(); await second.locator('output').filter({ hasText: /^10$/ }).waitFor();
    await page.evaluate(() => {
      window.__reorderFirstTool = document.querySelector('[data-header-element="counter-one"] .hl-counter');
      window.__reorderSecondTool = document.querySelector('[data-header-element="counter-two"] .hl-counter');
    });
    await page.evaluate(id => window.hyperlibGuidedPreview.update(id, 5), configuration.flowRequestId);
    const flowGeometry = await page.locator('[data-header-row="template-identity-row"]').evaluate(row => {
      const one = row.querySelector('[data-header-element="counter-one"]').getBoundingClientRect();
      const box = row.querySelector('[data-header-element="nested-box"]').getBoundingClientRect();
      return { firstLeft: one.left, firstRight: one.right, boxLeft: box.left, boxRight: box.right, width: row.getBoundingClientRect().width,
        rowDisplay: getComputedStyle(row.querySelector('.studio-header-row-content')).display,
        rowDirection: getComputedStyle(row.querySelector('.studio-header-row-content')).flexDirection,
        boxDirection: getComputedStyle(row.querySelector('.studio-header-box-content')).flexDirection };
    });
    check(flowGeometry.rowDisplay === 'flex' && flowGeometry.rowDirection === 'row' && flowGeometry.boxDirection === 'column'
      && flowGeometry.firstRight <= flowGeometry.boxLeft + 1 && flowGeometry.boxRight - flowGeometry.firstLeft <= flowGeometry.width + 1,
      'Horizontal rows and vertical nested Boxes arrange real templates without overlapping their sibling bounds.');
    check(await page.locator('[data-header-element="library-image"]').evaluate(element => {
      const box = element.getBoundingClientRect(); return Math.abs(box.width - 128) < 1 && Math.abs(box.height - 96) < 1;
    }), 'A template media slot retains its explicit 128 by 96 pixel dimensions.');
    const growth = await page.locator('[data-header-row="flow-media-row"]').evaluate(row => {
      const next = document.querySelector('[data-header-row="after-flow-media-row"]');
      const bounds = row.getBoundingClientRect(), following = next.getBoundingClientRect();
      const second = row.querySelector('[data-header-element="flow-picture-two"]').getBoundingClientRect();
      return { height: bounds.height, bottom: bounds.bottom, followingTop: following.top, contentBottom: second.bottom };
    });
    check(growth.height >= 192 && growth.contentBottom <= growth.bottom + 1 && growth.followingTop >= growth.bottom - 1,
      'A flow row grows beyond its minimum height to contain stacked media and keeps the following row below it.');
    const scrollingBox = await page.locator('[data-header-element="nested-box"]').evaluate(box => {
      const content = box.querySelector(':scope > .studio-header-box-content');
      const describe = element => ({ scrollHeight: element.scrollHeight, height: element.clientHeight, overflow: getComputedStyle(element).overflowY });
      const previousTop = box.scrollTop; box.scrollTop = 60;
      const result = { outer: describe(box), inner: describe(content), boxHeight: box.getBoundingClientRect().height, moved: box.scrollTop > 0 };
      box.scrollTop = previousTop; return result;
    });
    check(scrollingBox.outer.scrollHeight > scrollingBox.outer.height + 20 && ['auto', 'scroll'].includes(scrollingBox.outer.overflow)
      && Math.abs(scrollingBox.boxHeight - 220) < 1 && scrollingBox.moved,
      'Additional flow content scrolls inside its explicit-height Box instead of overlapping another header row. ' + JSON.stringify(scrollingBox));
    const counterBeforeReorder = await first.locator('output').textContent();
    await page.evaluate(id => window.hyperlibGuidedPreview.update(id, 6), configuration.reorderedRequestId);
    check(await page.locator('[data-header-row="template-identity-row"] > .studio-header-row-content').evaluate(container =>
      [...container.children].filter(element => element.hasAttribute('data-header-element')).map(element => element.dataset.headerElement).join(',') === 'nested-box,counter-one'),
      'Reordering header elements changes the real component order.');
    check(await page.locator('[data-header-row="template-tools-row"] > .studio-header-row-content').evaluate(container =>
      [...container.children].filter(element => element.hasAttribute('data-header-element')).map(element => element.dataset.headerElement).join(',') === 'library-image,library-text,wave-tool'),
      'Reordering also moves the real C# tool across sibling positions.');
    await verifyBoundedImageTemplate(page, check, 'Vertical flow', true);
    check(await first.locator('output').textContent() === counterBeforeReorder && counterBeforeReorder === '21' && await second.locator('output').textContent() === '10'
      && (await wave.locator('output').textContent()).includes('1 shifts') && await page.evaluate(() =>
        document.querySelector('[data-header-element="counter-one"] .hl-counter') === window.__reorderFirstTool
        && document.querySelector('[data-header-element="counter-two"] .hl-counter') === window.__reorderSecondTool
        && document.querySelector('[data-header-element="wave-tool"] .hl-wave') === window.__waveTool),
      'Moving actual counters, their ancestor Box, and the C# tool preserves live DOM instances and runtime state beyond their configured defaults.');
    await page.evaluate(id => window.hyperlibGuidedPreview.update(id, 7), configuration.positionedRequestId);
    check(await first.evaluate(element => getComputedStyle(element.closest('[data-header-element]')).position === 'absolute'),
      'Switching back to positioned mode restores manual placement.');
    await verifyBoundedImageTemplate(page, check, 'Positioned mode', false);
    await page.evaluate(id => window.hyperlibGuidedPreview.update(id, 8), configuration.restoredFlowRequestId);
    check(await first.evaluate(element => getComputedStyle(element.closest('[data-header-element]')).position === 'relative')
      && (await wave.locator('output').textContent()).includes('1 shifts'), 'Returning to flow mode preserves the current interactive tools.');
    const wide = path.join(output, 'header-library-tools-wide.png'); await page.screenshot({ path: wide, fullPage: true }); report.screenshots.push(wide);
    await page.setViewportSize({ width: 600, height: 900 });
    check(await page.locator('[data-header-template]').count() === 5, 'Nested template objects remain present at a narrow builder viewport.');
    await verifyBoundedImageTemplate(page, check, 'Narrow flow', true);
    const narrow = path.join(output, 'header-library-tools-narrow.png'); await page.screenshot({ path: narrow, fullPage: true }); report.screenshots.push(narrow);
    await page.setViewportSize({ width: 1280, height: 1000 });
    await page.goto(configuration.reopenedUrl, { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => document.documentElement.dataset.guidedInteractiveReady === 'true');
    await page.locator('[data-header-element="library-image"] img').evaluate(image => image.decode());
    const reopenedCounter = page.locator('[data-header-element="counter-one"] .hl-counter');
    await reopenedCounter.locator('[data-action="add"]').click(); await reopenedCounter.locator('output').filter({ hasText: /^21$/ }).waitFor();
    check((await reopenedCounter.textContent()).includes('Latest field update'), 'A reopened portable project retains working library tools and edited fields without the source collection.');
    check(await page.locator('[data-header-element="library-image"] img').evaluate(image => image.naturalWidth === 220),
      'The reopened template retains its project-owned image bytes.');
    await verifyBoundedImageTemplate(page, check, 'Reopened portable project', true);
    const reopened = path.join(output, 'header-library-tools-reopened.png'); await page.screenshot({ path: reopened, fullPage: true }); report.screenshots.push(reopened);
    check(errors.length === 0, 'Real template rendering, JavaScript imports, Blazor events, and live updates have no uncaught browser errors. ' + errors.join('; '));
    check(failedNavigations.length === 0, 'Preview links never request a missing document or reach a 404 page. ' + failedNavigations.join('; '));
    fs.writeFileSync(path.join(output, 'header-template-browser-result.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ result: report.result, assertions: report.checks.length }));
  } catch (error) {
    if (page) await page.screenshot({ path: path.join(output, 'header-library-tools-failure.png'), fullPage: true }).catch(() => {});
    throw error;
  } finally { await browser.close(); }
})().catch(error => { console.error(error.stack || String(error)); process.exitCode = 1; });
