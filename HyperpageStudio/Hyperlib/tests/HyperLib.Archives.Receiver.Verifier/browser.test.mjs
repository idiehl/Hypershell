import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';

const [url, output, modulePath] = process.argv.slice(2);
if (!url || !output || !modulePath) throw new Error('Provide the local public page URL, output folder, and installed Playwright module path.');
assert.equal(new URL(url).hostname, '127.0.0.1', 'Browser verifier only opens its owned loopback fixture');
const { chromium } = createRequire(import.meta.url)(modulePath);
const executablePath = process.env.ARCHIVES_BROWSER_PATH || [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
].find(file => fs.existsSync(file));
const browser = await chromium.launch({ headless: true, ...(executablePath ? { executablePath } : {}) });
const checks = [], errors = [];
try {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1100 }, reducedMotion: 'reduce' });
  const page = await context.newPage();
  page.setDefaultTimeout(30000);
  page.on('pageerror', error => errors.push(error.message));
  const helperLoaded = page.waitForResponse(response => response.url().endsWith('/blocks/counter-helper.js') && response.ok());
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await page.locator('#page').waitFor({ state: 'visible' });
  await helperLoaded;
  const frame = page.frameLocator('#page');
  await frame.getByRole('button', { name: 'Increase', exact: true }).click();
  const deadline = Date.now() + 10000;
  while (await frame.locator('.hl-counter output').textContent() !== '1') {
    if (Date.now() > deadline) throw new Error('Published interactive counter did not increment');
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  checks.push('Published nested JavaScript counter increments after interactive Blazor activation');
  assert.equal(await frame.locator('[contenteditable="true"]').count(), 0);
  assert.equal(await frame.locator('.archives-fixture').getAttribute('data-readonly'), 'True');
  checks.push('Public Page remains read only in the active browser circuit');
  assert.equal(await frame.locator('img').first().evaluate(image => image.complete && image.naturalWidth > 0), true);
  checks.push('Immutable edition media visibly loads inside the public reader');
  assert.equal(await frame.locator('.archives-fixture').evaluate(element => getComputedStyle(element).color), 'rgb(18, 52, 86)');
  checks.push('Custom Razor scoped CSS applies to the visible component');
  assert.equal(await page.locator('body').evaluate(element => element.scrollWidth <= innerWidth), true);
  await page.screenshot({ path: path.join(output, 'archives-desktop.png'), fullPage: true });
  checks.push('Wide public reader has no horizontal overflow');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: path.join(output, 'archives-compact.png'), fullPage: true });
  assert.equal(await page.locator('body').evaluate(element => element.scrollWidth <= innerWidth), true);
  checks.push('Compact public reader has no horizontal overflow');
  assert.deepEqual(errors, []);
  checks.push('Public reader and renderer produced no browser JavaScript errors');
  await fs.promises.writeFile(path.join(output, 'archives-browser-verification.json'), JSON.stringify({ status: 'HYPERLIB_ARCHIVES_BROWSER_OK', checks }, null, 2));
  console.log('HYPERLIB_ARCHIVES_BROWSER_OK|' + checks.length + ' checks');
} finally {
  await browser.close();
}
