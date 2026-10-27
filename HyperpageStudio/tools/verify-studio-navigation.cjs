const fs = require('fs');
const path = require('path');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const [configFile, output] = process.argv.slice(2);
  const config = JSON.parse(fs.readFileSync(configFile, 'utf8'));
  const report = { result: 'STUDIO_NAVIGATION_BROWSER_OK', checks: [], geometry: [] };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.checks.push(message); };
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  let page;
  try {
    page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto(config.url, { waitUntil: 'networkidle' });
    const nav = page.locator('.studio-navigation-links').first();
    await nav.waitFor();
    check(await page.locator('[data-region="navigation"] .studio-navigation-layout[data-sticky="true"]').count() === 1,
      'The generated Work renders a dedicated sticky Navigation region below its Header.');
    check(await nav.locator('a[href="page/nav-later-page"]').count() > 0,
      'The actual generated renderer includes a Page added after project creation.');
    check(await nav.locator('a[href="page/nav-reference-page"]').count() >= 1,
      'Custom Page targets resolve to actual reader routes.');
    const active = nav.locator('.studio-nav-items a[aria-current="page"]');
    check(await active.count() === 1 && await active.textContent() === 'Home', 'The current landing Page has an explicit active link.');
    const normal = nav.locator('.studio-nav-items a[href="page/nav-first-page"]').first();
    const normalStyle = await normal.evaluate(element => ({ fill: getComputedStyle(element).backgroundColor, font: getComputedStyle(element).fontFamily, size: getComputedStyle(element).fontSize }));
    const activeFill = await active.evaluate(element => getComputedStyle(element).backgroundColor);
    await normal.hover();
    const hoverFill = await normal.evaluate(element => getComputedStyle(element).backgroundColor);
    check(activeFill !== normalStyle.fill && hoverFill !== normalStyle.fill && activeFill !== hoverFill,
      'Actual active and hovered links have distinct configured colors.');
    check(normalStyle.font.includes('system-ui') && normalStyle.size === '16px', 'Navigation links use the configured font family and size.');
    await page.mouse.move(0, 0);
    const initial = await page.locator('[data-region="navigation"]').evaluate(element => ({ top: element.getBoundingClientRect().top, height: element.getBoundingClientRect().height, body: document.querySelector('[data-region="main"]').getBoundingClientRect().top, maxScroll: document.documentElement.scrollHeight-innerHeight }));
    check(initial.maxScroll > 700, 'The test reader has a real scrollable content body.');
    await page.evaluate(() => scrollTo(0, 700));
    await page.waitForTimeout(100);
    const scrolled = await page.locator('[data-region="navigation"]').evaluate(element => ({ top: element.getBoundingClientRect().top, height: element.getBoundingClientRect().height, body: document.querySelector('[data-region="main"]').getBoundingClientRect().top, scrollY }));
    report.geometry.push({ initial, scrolled });
    check(scrolled.scrollY > 500 && Math.abs(scrolled.top) <= 2 && scrolled.body < initial.body - 500,
      'Navigation remains at the viewport edge while the actual Page body scrolls beneath it. ' + JSON.stringify(scrolled));
    await page.screenshot({ path: path.join(output, 'navigation-sticky.png'), fullPage: false });
    await page.evaluate(() => scrollTo(0, 0));
    await page.screenshot({ path: path.join(output, 'navigation-wide.png'), fullPage: false });
    await page.setViewportSize({ width: 480, height: 850 });
    const menu = nav.locator('.studio-nav-menu');
    await menu.waitFor({ state: 'visible' });
    check(await menu.getAttribute('aria-expanded') === 'false' && !(await normal.isVisible()), 'The narrow-screen layout starts with its Page links collapsed behind Menu.');
    await menu.click();
    await page.waitForFunction(() => document.querySelector('.studio-nav-menu')?.getAttribute('aria-expanded') === 'true');
    check(await normal.isVisible(), 'The real C# Menu button opens the responsive Page links.');
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'The mobile reader and Navigation have no horizontal viewport overflow.');
    await page.screenshot({ path: path.join(output, 'navigation-mobile.png'), fullPage: false });
    const liveState = await (await page.request.get(config.stateUrl)).json();
    const added = await page.request.post(config.operationsUrl, { headers: { 'X-HyperLib-Client': 'editor' }, data: {
      expectedRevision: liveState.revision, description: 'Verify a large real responsive Work outline',
      operations: Array.from({ length: 40 }, (_, index) => ({ kind: 'createNode', id: 'mobile-nav-page-' + index,
        nodeType: 'page', parentId: config.workId, title: 'Additional content Page ' + (index + 1) })) } });
    check(added.ok(), 'The reader fixture creates forty additional real Pages for responsive Navigation.' + (added.ok() ? '' : ' ' + await added.text()));
    await page.reload({ waitUntil: 'networkidle' });
    await menu.click();
    await page.waitForFunction(() => document.querySelector('.studio-nav-menu')?.getAttribute('aria-expanded') === 'true');
    const scroll = await nav.locator('.studio-nav-items').evaluate(element => {
      const bounds = element.getBoundingClientRect(); element.scrollTop = element.scrollHeight;
      return { height: bounds.height, maximum: innerHeight - 96, client: element.clientHeight, content: element.scrollHeight,
        moved: element.scrollTop > 0, overflow: getComputedStyle(element).overflowY };
    });
    check(scroll.height <= scroll.maximum + 1 && scroll.content > scroll.client + 100 && scroll.moved && scroll.overflow === 'auto',
      'A large mobile Page list has a bounded working inner scroller rather than extending beyond the viewport. ' + JSON.stringify(scroll));
    await page.screenshot({ path: path.join(output, 'navigation-mobile-many-pages.png'), fullPage: false });
    await normal.click();
    await page.waitForURL('**/page/nav-first-page');
    await page.waitForLoadState('networkidle');
    const currentPageLink = page.locator('.studio-nav-items a[href="page/nav-first-page"][aria-current="page"]');
    await currentPageLink.waitFor({ state: 'attached' });
    check(await currentPageLink.count() === 1,
      'Clicking a generated Page link opens the actual Page and updates active navigation.');
    check(errors.length === 0, 'Actual Navigation runs without browser JavaScript errors: ' + errors.join('; '));
    fs.writeFileSync(path.join(output, 'studio-navigation-browser-result.json'), JSON.stringify(report, null, 2));
    process.stdout.write(report.result + '|checks=' + report.checks.length + '\n');
  } catch (error) {
    if (page) await page.screenshot({ path: path.join(output, 'navigation-failure.png'), fullPage: false }).catch(() => {});
    fs.writeFileSync(path.join(output, 'studio-navigation-browser-failure.json'), JSON.stringify({ ...report, error: String(error.stack || error) }, null, 2));
    throw error;
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
