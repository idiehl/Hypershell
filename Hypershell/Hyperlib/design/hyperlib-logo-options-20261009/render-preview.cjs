const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const { pathToFileURL } = require('node:url');
const path = require('node:path');

(async () => {
  const browser = await chromium.launch({ headless: true, channel: 'msedge' });
  try {
    const page = await browser.newPage({ viewport: { width: 1600, height: 1000 }, deviceScaleFactor: 1 });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(pathToFileURL(path.join(__dirname, 'index.html')).href);
    if (await page.locator('.concept').count() !== 10) throw new Error('Expected ten concepts');
    for (let index = 0; index < 10; index++) {
      await page.locator('.concept').nth(index).click();
      if (await page.locator('.concept[aria-pressed="true"]').count() !== 1) throw new Error('Invalid selected state');
      if (!(await page.locator('#preview-title').innerText()).startsWith(String(index + 1).padStart(2, '0'))) throw new Error('Wrong toolbar preview');
    }
    await page.locator('.concept').nth(2).click();
    await page.screenshot({ path: path.join(__dirname, 'board-preview.png'), fullPage: true });
    await page.setViewportSize({ width: 420, height: 1000 });
    if (await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)) throw new Error('Horizontal overflow at 420px');
    if (errors.length) throw new Error(errors.join('\n'));
    await page.setViewportSize({ width: 1600, height: 930 });
    await page.goto(pathToFileURL(path.join(__dirname, 'contact-sheet.svg')).href);
    await page.screenshot({ path: path.join(__dirname, 'contact-sheet.png') });
    console.log('LOGO_BOARD_OK concepts=10 selection=10 mobile-width=420 overflow=false runtime-errors=0');
    console.log('Rendered board-preview.png and contact-sheet.png directly from vectors.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
