const fs = require('fs');
const path = require('path');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async () => {
  const [url, output] = process.argv.slice(2);
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  let page;
  try {
    page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto(url, { waitUntil: 'domcontentloaded' });
    const counters = page.locator('.hl-counter');
    await counters.first().waitFor();
    if (await counters.count() < 2) throw new Error('The saved complete original is missing its actual independent Counter components.');
    const initial = Number(await counters.first().locator('output').textContent());
    const other = await counters.nth(1).locator('output').textContent();
    // Wait for the actual imported module to mount without substituting a test implementation.
    await page.waitForFunction(expected => {
      const element = document.querySelector('.hl-counter');
      if (Number(element.querySelector('output').textContent) === expected) return true;
      element.querySelector('[data-action=add]').click(); return false;
    }, initial + 1);
    if (await counters.nth(1).locator('output').textContent() !== other) throw new Error('An imported saved-template Counter changed another instance.');
    if (errors.length) throw new Error(errors.join('; '));
    const screenshot = path.join(output, 'saved-complete-template-live.png');
    await page.screenshot({ path: screenshot });
    fs.writeFileSync(path.join(output, 'saved-complete-template-browser-result.json'), JSON.stringify({ result: 'STUDIO_SAVED_COMPLETE_TEMPLATE_BROWSER_OK', initial, actual: initial + 1, other, screenshot }, null, 2));
    console.log('STUDIO_SAVED_COMPLETE_TEMPLATE_BROWSER_OK|actual-imported-module|independent-instances');
  } catch (error) {
    if (page) await page.screenshot({ path: path.join(output, 'saved-complete-template-failure.png') }).catch(() => {});
    throw error;
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
