const fs = require('fs');
const path = require('path');
const http = require('http');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const [configPath, output] = process.argv.slice(2);
  const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
  const report = { result: 'STUDIO_COMPLETE_TEMPLATE_PREVIEW_BROWSER_OK', name: config.name, checks: [], screenshots: [] };
  const check = (value, message) => { if (!value) throw new Error(message); report.checks.push(message); };
  const browser = await chromium.launch({ headless: true,
    executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  let page, server;
  try {
    server = http.createServer((request, response) => {
      response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8',
        'Content-Security-Policy': "default-src 'none'; style-src 'unsafe-inline'; frame-src http://127.0.0.1:*" });
      response.end('<!doctype html><style>html,body{margin:0;height:100%;overflow:hidden}iframe{display:block;border:0;width:100%;height:100vh}</style><iframe id="preview" sandbox="allow-scripts allow-same-origin" src="' + config.url.replaceAll('&', '&amp;').replaceAll('"', '&quot;') + '"></iframe>');
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const parentUrl = `http://127.0.0.1:${server.address().port}/`;
    check(new URL(parentUrl).origin !== new URL(config.url).origin, 'The real preview is embedded from a different local port, as it is in Studio.');
    page = await browser.newPage({ viewport: { width: 1280, height: 1000 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto(parentUrl, { waitUntil: 'domcontentloaded' });
    const frame = await (await page.locator('#preview').elementHandle()).contentFrame();
    await frame.waitForFunction(() => document.documentElement.dataset.guidedInteractiveReady === 'true');
    check(frame.url() === config.url, 'The browser loads the requested interactive Page instead of a blocked-frame error document.');
    const root = frame.locator('[data-guided-page-preview]');
    check(await root.count() === 1, 'The actual complete template renders one live Page canvas.');
    check(await root.evaluate(element => !element.classList.contains('studio-staged-layout') && !element.classList.contains('studio-guided-layout')),
      'Complete originals omit staged layout classes rather than inheriting a wizard grid.');
    check(await frame.locator('.studio-empty-region,.studio-slot-placeholder,.studio-region-chrome,[data-studio-slot-placeholder]').count() === 0,
      'Complete original content has no synthetic wizard chrome or empty-slot scaffolding.');
    const text = await root.textContent();
    check(config.expectedTexts.every(value => text.includes(value)), 'Original authored titles, text, and configured object labels are retained.');
    const wide = await root.evaluate(element => {
      const style = getComputedStyle(element); return { top: parseFloat(style.paddingTop), left: parseFloat(style.paddingLeft),
        background: style.backgroundColor, width: element.getBoundingClientRect().width };
    });
    check(Math.abs(wide.top - 64) < 1 && Math.abs(wide.left - 51.2) < 1,
      'Wide complete preview uses the original Runtime canvas spacing, without the wizard 28px override: ' + JSON.stringify(wide));
    const hex = config.expectedBackground.slice(1);
    const expectedColor = `rgb(${parseInt(hex.slice(0, 2), 16)}, ${parseInt(hex.slice(2, 4), 16)}, ${parseInt(hex.slice(4, 6), 16)})`;
    check(wide.background === expectedColor, 'The outer complete canvas retains the original template background instead of a blank cream frame. ' + wide.background);
    if (config.counter) {
      const counters = frame.locator('.hl-counter');
      check(await counters.count() >= 2, 'The complete dashboard contains its real independent JavaScript Counter components.');
      const first = counters.first();
      const initial = Number(await first.locator('output').textContent());
      const other = await counters.nth(1).locator('output').textContent();
      await first.locator('[data-action=add]').click();
      await frame.waitForFunction(value => Number(document.querySelector('.hl-counter output').textContent) === value, initial + 1);
      check(await counters.nth(1).locator('output').textContent() === other, 'Clicking the real imported tool changes only its own live instance.');
      await first.locator('[data-action=subtract]').click();
      await frame.waitForFunction(value => Number(document.querySelector('.hl-counter output').textContent) === value, initial);
    }
    const widePath = path.join(output, config.name + '-wide.png');
    await page.screenshot({ path: widePath }); report.screenshots.push(widePath);
    await page.setViewportSize({ width: 480, height: 900 });
    const narrow = await root.evaluate(element => {
      const style = getComputedStyle(element); return { top: parseFloat(style.paddingTop), left: parseFloat(style.paddingLeft),
        rootWidth: element.getBoundingClientRect().width, viewport: innerWidth, documentWidth: document.documentElement.scrollWidth };
    });
    check(Math.abs(narrow.top - 26) < 1 && Math.abs(narrow.left - 20) < 1,
      'Narrow complete preview keeps the original Runtime responsive spacing. ' + JSON.stringify(narrow));
    check(narrow.rootWidth <= narrow.viewport + 1 && narrow.documentWidth <= narrow.viewport + 1,
      'The complete original Page fits its narrow viewport without outer horizontal overflow. ' + JSON.stringify(narrow));
    const narrowPath = path.join(output, config.name + '-narrow.png');
    await page.screenshot({ path: narrowPath }); report.screenshots.push(narrowPath);
    check(errors.length === 0, 'Actual imported original components run without browser errors: ' + errors.join('; '));
    fs.writeFileSync(path.join(output, config.name + '-preview-browser-result.json'), JSON.stringify(report, null, 2));
    console.log(report.result + '|checks=' + report.checks.length);
  } catch (error) {
    if (page) await page.screenshot({ path: path.join(output, config.name + '-preview-failure.png') }).catch(() => {});
    fs.writeFileSync(path.join(output, config.name + '-preview-browser-failure.json'), JSON.stringify({ ...report, error: String(error.stack || error) }, null, 2));
    throw error;
  } finally { await browser.close(); if (server) await new Promise(resolve => server.close(resolve)); }
})().catch(error => { console.error(error); process.exitCode = 1; });
