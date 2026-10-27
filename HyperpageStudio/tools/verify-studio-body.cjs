const fs = require('fs');
const path = require('path');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const [configFile, output] = process.argv.slice(2);
  const config = JSON.parse(fs.readFileSync(configFile, 'utf8'));
  const report = { result: 'STUDIO_BODY_BROWSER_OK', checks: [], bounds: [] };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.checks.push(message); };
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  let page;
  try {
    page = await browser.newPage({ viewport: { width: 1400, height: 1050 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto(config.url, { waitUntil: 'networkidle' });
    const body = page.locator('[data-region="main"]');
    await body.locator('[data-studio-region="body"]').first().waitFor();
    check(await body.locator('[data-header-row]').count() === 2, 'The actual reader renders each Body Block in its main region.');
    check(await body.getByText('Original authored Body content survives enabling the builder.', { exact: true }).count() === 1,
      'Existing authored content remains beside the newly enabled Body foundation.');
    check(await body.getByText('Legacy Body chrome', { exact: true }).count() === 0, 'Legacy Body chrome does not duplicate the new builder.');
    const first = body.locator('[data-header-element="body-counter-one"] .hl-counter');
    const second = body.locator('[data-header-element="body-counter-two"] .hl-counter');
    await first.locator('[data-action="add"]').click();
    await first.locator('output').filter({ hasText: /^6$/ }).waitFor();
    check(await second.locator('output').textContent() === '9', 'Two real Body library tools have independent interactive state.');
    const nativeImage = body.locator('[data-header-element="body-native-image"] img');
    const libraryImage = body.locator('[data-header-element="body-library-image"] img');
    await page.waitForFunction(() => [...document.querySelectorAll('[data-header-element="body-native-image"] img,[data-header-element="body-library-image"] img')].every(image => image.complete && image.naturalWidth > 0));
    check((await nativeImage.getAttribute('src')).startsWith('/media/') && (await libraryImage.getAttribute('src')).startsWith('/media/'),
      'Native and nested Template media load through portable project-owned URLs.');
    const bounds = await body.locator('[data-header-element="body-box"]').evaluate(box => {
      const container = box.querySelector(':scope > .studio-header-box-content').getBoundingClientRect();
      const media = box.querySelector('[data-header-element="body-native-image"]').getBoundingClientRect();
      const outer = box.getBoundingClientRect();
      return { container: container.toJSON(), media: media.toJSON(), outer: outer.toJSON() };
    });
    report.bounds.push(bounds);
    check(Math.abs(bounds.media.x - bounds.container.x) < 1 && Math.abs(bounds.media.y - bounds.container.y) < 1
      && bounds.media.right <= bounds.container.right + 1 && bounds.media.bottom <= bounds.container.bottom + 1
      && Math.abs(bounds.media.width - 128) < 1 && Math.abs(bounds.media.height - 96) < 1,
      'Nested Body media preserves fixed dimensions and starts at its Box padding edge. ' + JSON.stringify(bounds));
    const regions = await page.locator('[data-region="header"],[data-region="navigation"],[data-region="main"]').evaluateAll(elements => elements.map(element => ({ id: element.dataset.region, top: element.getBoundingClientRect().top, bottom: element.getBoundingClientRect().bottom })));
    const header = regions.find(region => region.id === 'header'), nav = regions.find(region => region.id === 'navigation'), main = regions.find(region => region.id === 'main');
    check(header.bottom <= nav.top + 1 && nav.bottom <= main.top + 1, 'Header, Navigation, and Body occupy separate nonoverlapping regions.');
    await page.screenshot({ path: path.join(output, 'body-reader-wide.png'), fullPage: true });
    await page.setViewportSize({ width: 540, height: 950 });
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'The generated Body remains inside the narrow reader viewport.');
    await page.screenshot({ path: path.join(output, 'body-reader-narrow.png'), fullPage: true });
    check(errors.length === 0, 'Actual Body components run without browser JavaScript errors: ' + errors.join('; '));
    fs.writeFileSync(path.join(output, 'studio-body-browser-result.json'), JSON.stringify(report, null, 2));
    process.stdout.write(report.result + '|checks=' + report.checks.length + '\n');
  } catch (error) {
    if (page) await page.screenshot({ path: path.join(output, 'body-reader-failure.png'), fullPage: true }).catch(() => {});
    fs.writeFileSync(path.join(output, 'studio-body-browser-failure.json'), JSON.stringify({ ...report, error: String(error.stack || error) }, null, 2));
    throw error;
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
