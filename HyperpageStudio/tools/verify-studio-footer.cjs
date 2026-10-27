const fs = require('fs');
const path = require('path');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const [configFile, output] = process.argv.slice(2);
  const config = JSON.parse(fs.readFileSync(configFile, 'utf8'));
  const report = { result: 'STUDIO_FOOTER_BROWSER_OK', checks: [] };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.checks.push(message); };
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  let page;
  try {
    page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    const footer = () => page.locator('[data-region="footer"]');
    const nav = () => footer().locator('[data-header-element="footer-page-navigation"] .studio-navigation-links');
    await page.goto(config.firstUrl, { waitUntil: 'networkidle' });
    await nav().waitFor();
    check(await nav().locator('a[rel="prev"]').count() === 0 && await nav().locator('a[rel="next"]').getAttribute('href') === 'page/slot-other-page',
      'The first real Page omits Previous and links Next to the first item in the actual Work outline.');
    check(await nav().locator('.studio-nav-items a').count() === 1 && await nav().locator('.studio-nav-items a').getAttribute('href') === 'page/' + config.landingId,
      'Footer navigation provides Work home without repeating the full Page list.');
    check(await page.locator('[data-studio-nav-preview-steps],[data-studio-slot-placeholder]').count() === 0,
      'The public reader has no private layout labels or synthetic navigation placeholders.');
    check(await page.locator('[data-header-element="article-text-slot"]').textContent() === '' && await page.locator('[data-header-element="article-image-slot"] img').count() === 0,
      'Unfilled primitive slots remain empty in the reader rather than showing design labels as finished content.');
    await page.goto(config.middleUrl, { waitUntil: 'networkidle' });
    await page.locator('[data-header-element="article-text-slot"]').getByText('First independent article text', { exact: true }).waitFor();
    check(await nav().locator('a[rel="prev"]').getAttribute('href') === 'page/slot-other-page' && await nav().locator('a[rel="next"]').getAttribute('href') === 'page/slot-last-page',
      'A middle Page has Previous and Next targets from its actual neighboring Pages.');
    check(await page.locator('[data-header-element="article-tool-slot"] [data-field="label"]').textContent() === 'First independent tool label',
      'The actual library component renders the Page-specific hydrated content field.');
    const counter = page.locator('[data-header-element="article-tool-slot"] .hl-counter');
    const initial = Number(await counter.locator('output').textContent());
    await counter.locator('[data-action="add"]').click();
    await page.waitForFunction(value => Number(document.querySelector('[data-header-element="article-tool-slot"] output')?.textContent) === value + 1, initial);
    check(true, 'A real library tool remains interactive when used as a Body content slot.');
    await page.waitForFunction(() => [...document.querySelectorAll('[data-header-element="article-image-slot"] img,[data-header-element="footer-image"] img')].every(image => image.complete && image.naturalWidth > 0));
    check(await page.locator('[data-header-element="article-image-slot"] img').count() === 1,
      'The first Page renders its individually authored picture.');
    check(await footer().getByText('Independently authored footer note', { exact: true }).count() === 1,
      'The independently authored Footer note survives the shared template revision.');
    const bounds = await page.evaluate(() => ({ main: document.querySelector('[data-region="main"]').getBoundingClientRect().toJSON(), footer: document.querySelector('[data-region="footer"]').getBoundingClientRect().toJSON() }));
    check(bounds.footer.top >= bounds.main.bottom - 1, 'The actual Footer occupies the Page bottom after Body content. ' + JSON.stringify(bounds));
    await page.screenshot({ path: path.join(output, 'footer-slots-reader-wide.png'), fullPage: true });
    await nav().locator('a[rel="next"]').click();
    await page.waitForURL('**/page/slot-last-page');
    await page.locator('[data-header-element="article-text-slot"]').getByText('Last independent article text', { exact: true }).waitFor();
    check(await nav().locator('a[rel="prev"]').getAttribute('href') === 'page/slot-first-page' && await nav().locator('a[rel="next"]').count() === 0,
      'Clicking Next opens the actual last Page, which omits Next and points Previous to its neighbor.');
    check(await nav().locator('.studio-nav-items a,.studio-nav-empty,.studio-nav-menu').count() === 0,
      'A previous/next-only Footer has no empty Page-list hint or unusable mobile Menu.');
    check(await page.locator('[data-header-element="article-tool-slot"] [data-field="label"]').textContent() === 'Last independent tool label'
      && await page.locator('[data-header-element="article-image-slot"] img').count() === 0,
      'The last Page retains its own content and does not inherit the first Page picture.');
    check(await footer().getByText('Revised shared footer note', { exact: true }).count() === 1,
      'Untouched Footer content follows the updated reusable foundation.');
    await page.setViewportSize({ width: 540, height: 950 });
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'Body slots and Footer stay inside the narrow reader viewport.');
    await page.screenshot({ path: path.join(output, 'footer-slots-reader-narrow.png'), fullPage: true });
    await nav().locator('a[rel="prev"]').click();
    await page.waitForURL('**/page/slot-first-page');
    await page.locator('[data-header-element="article-text-slot"]').getByText('First independent article text', { exact: true }).waitFor();
    await nav().locator('.studio-nav-items a').click();
    await page.waitForURL('**/page/' + config.landingId);
    await nav().locator('.studio-nav-items a[aria-current="page"]').waitFor();
    check(true, 'Footer Previous and Work home controls navigate through the actual reader routes.');
    check(errors.length === 0, 'Actual Footer and content-slot components run without browser JavaScript errors: ' + errors.join('; '));
    fs.writeFileSync(path.join(output, 'studio-footer-browser-result.json'), JSON.stringify(report, null, 2));
    process.stdout.write(report.result + '|checks=' + report.checks.length + '\n');
  } catch (error) {
    if (page) await page.screenshot({ path: path.join(output, 'footer-slots-reader-failure.png'), fullPage: true }).catch(() => {});
    fs.writeFileSync(path.join(output, 'studio-footer-browser-failure.json'), JSON.stringify({ ...report, error: String(error.stack || error) }, null, 2));
    throw error;
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
