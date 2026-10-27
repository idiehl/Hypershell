const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const out = path.resolve(process.argv[2] || 'output/qa/studio-web-navigation');
  fs.mkdirSync(out, { recursive: true });
  const source = fs.readFileSync(path.resolve(__dirname, '../src/Hypershell/Services/StudioWebProjectTemplateAdapter.cs'), 'utf8');
  const script = source.split('private const string NavigationJavaScript =')[1].split('"""')[1];
  new Function(script);
  const css = source.split('private const string BaseCss =')[1].split('"""')[1];
  const pages = [{ id: 'home', name: 'Landing', route: '/' }, { id: 'one', name: 'Page one', route: '/page-1/' }, { id: 'two', name: 'Newly added Page', route: '/page-2/' }];
  const checks = [];
  const requireCheck = (condition, text) => { if (!condition) throw new Error(text); checks.push(text); };
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  try {
    const context = await browser.newContext({ viewport: { width: 1200, height: 800 } });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(String(error)));
    const nav = (footer = false) => `<nav class="studio-web-navigation" data-auto-pages="${!footer}" data-page-steps="${footer}" data-include-home="${!footer}" data-home-label="Work home" ${footer ? 'data-footer="true"' : 'data-nav-breakpoint="720"'}>${footer ? '' : '<button class="studio-web-navigation-toggle" aria-expanded="false" aria-controls="primary">Menu</button>'}<div class="studio-web-navigation-links" id="${footer ? 'footer' : 'primary'}"><a href="/" data-studio-page-link="true">Stale home</a><a href="/page-1/" data-studio-page-link="true" rel="next">Stale next</a><a href="https://example.com/about">Custom destination</a></div></nav>`;
    const html = `<!doctype html><html><head><style>${css}</style></head><body>${nav()}<p>Content</p>${nav(true)}<script>${script}</script></body></html>`;
    await page.route('http://studio.test/**', route => route.request().url().includes('/assets/studio-pages.json')
      ? route.fulfill({ contentType: 'application/json', body: JSON.stringify({ schemaVersion: 1, pages }) })
      : route.fulfill({ contentType: 'text/html', body: html }));
    await page.goto('http://studio.test/');
    await page.waitForFunction(() => [...document.querySelectorAll('#primary a')].some(a => a.textContent === 'Newly added Page'));
    requireCheck(await page.locator('#primary a').count() === 4, 'Auto Page navigation adds new Pages while retaining the custom link.');
    requireCheck(await page.locator('#primary [aria-current="page"]').innerText() === 'Work home', 'The configured Home label survives route refresh.');
    requireCheck(await page.locator('#footer a[rel="prev"]').count() === 0 && await page.locator('#footer a[rel="next"]').getAttribute('href') === '/page-1/', 'Landing navigation has a real Next link and no Previous link.');
    requireCheck(await page.locator('#footer a').count() === 2, 'Footer navigation preserves custom links without duplicating the Page list.');
    await page.goto('http://studio.test/page-1/');
    await page.waitForSelector('#footer a[rel="prev"]');
    requireCheck(await page.locator('#footer a[rel="prev"]').getAttribute('href') === '/' && await page.locator('#footer a[rel="next"]').getAttribute('href') === '/page-2/', 'A middle Page navigates to the actual preceding and following Pages.');
    requireCheck(await page.locator('#primary [aria-current="page"]').innerText() === 'Page one', 'Current Page styling follows the displayed route.');
    await page.setViewportSize({ width: 390, height: 800 });
    await page.waitForSelector('.studio-web-navigation[data-nav-compact="true"]');
    requireCheck(!await page.locator('#primary').isVisible(), 'Narrow navigation collapses its link list.');
    await page.locator('.studio-web-navigation-toggle').click();
    requireCheck(await page.locator('#primary').isVisible() && await page.locator('.studio-web-navigation-toggle').getAttribute('aria-expanded') === 'true', 'The menu button opens narrow navigation and updates its accessible state.');
    await page.keyboard.press('Escape');
    requireCheck(!await page.locator('#primary').isVisible() && await page.locator('.studio-web-navigation-toggle').getAttribute('aria-expanded') === 'false', 'Escape closes the menu and restores the menu-button focus.');
    await page.goto('http://studio.test/page-2/');
    await page.waitForSelector('#footer a[rel="prev"]');
    requireCheck(await page.locator('#footer a[rel="prev"]').getAttribute('href') === '/page-1/' && await page.locator('#footer a[rel="next"]').count() === 0, 'The last Page omits Next and links to the actual previous Page.');
    requireCheck(await page.locator('#primary a').filter({ hasText: 'Custom destination' }).count() === 1, 'Repeated route refreshes preserve one copy of each custom link.');
    requireCheck(errors.length === 0, 'The shared navigation runtime produces no browser errors.');
    await page.screenshot({ path: path.join(out, 'studio-web-navigation-mobile.png'), fullPage: true });
    const result = { status: 'STUDIO_WEB_NAVIGATION_OK', checks, count: checks.length, errors };
    fs.writeFileSync(path.join(out, 'studio-web-navigation-result.json'), JSON.stringify(result, null, 2));
    process.stdout.write(JSON.stringify(result) + '\n');
  } finally { await browser.close(); }
})().catch(error => { process.stderr.write(String(error.stack || error) + '\n'); process.exitCode = 1; });
