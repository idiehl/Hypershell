const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const [configurationFile, output] = process.argv.slice(2);
  const configuration = JSON.parse(fs.readFileSync(configurationFile, 'utf8'));
  const browser = await chromium.launch({ headless: true,
    executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  const report = { result: 'HYPERLIB_BACKGROUND_BROWSER_OK', checks: [], screenshots: [] };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.checks.push(message); };
  const esc = value => String(value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  try {
    const page = await browser.newPage({ viewport: { width: 480, height: 270 } });
    const thumbnails = path.join(output, 'thumbnails'); fs.mkdirSync(thumbnails, { recursive: true });
    const hashes = [];
    for (const design of configuration.designs) {
      await page.setContent(fs.readFileSync(design.htmlPath, 'utf8'), { waitUntil: 'networkidle' });
      const style = await page.locator('.surface').evaluate(node => ({ image: getComputedStyle(node).backgroundImage,
        color: getComputedStyle(node).backgroundColor, width: node.clientWidth, height: node.clientHeight }));
      check(style.image !== 'none' && style.width === 480 && style.height === 270, `${design.title}: actual surface fills its thumbnail.`);
      const file = path.join(thumbnails, design.id + '.png'); await page.screenshot({ path: file });
      hashes.push(crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex'));
    }
    check(new Set(hashes).size === 10, 'All ten actual rendered thumbnails are distinct.');
    await page.setViewportSize({ width: 1120, height: 900 });
    const cards = configuration.designs.map(design => `<article><img alt="${esc(design.title)}" src="data:image/png;base64,${fs.readFileSync(path.join(thumbnails, design.id + '.png')).toString('base64')}"><div><strong>${esc(design.title)}</strong><span>${esc(design.id)}</span></div></article>`).join('');
    await page.setContent(`<!doctype html><html><head><style>*{box-sizing:border-box}body{margin:0;padding:28px;background:#101820;color:#edf3f2;font:15px Segoe UI,system-ui}h1{font-size:25px;font-weight:600;margin:0 0 8px}p{color:#acbcb8;margin:0 0 24px}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:20px}article{overflow:hidden;border:1px solid #30413f;border-radius:10px;background:#17232b}img{display:block;width:100%;aspect-ratio:16/9;object-fit:cover}article>div{padding:14px;display:flex;gap:12px;justify-content:space-between}span{font-size:11px;color:#9faeaf}</style></head><body><h1>Hyperpage Studio - ten background designs</h1><p>Actual portable CSS and SVG surfaces. Atlas night retains the original Atlas Universalis stars and deep-blue backdrop.</p><section class="grid">${cards}</section></body></html>`, { waitUntil: 'networkidle' });
    const sheet = path.join(output, 'background-contact-sheet.png'); await page.screenshot({ path: sheet, fullPage: true }); report.screenshots.push(sheet);
    await page.setViewportSize({ width: 960, height: 760 });
    for (const design of configuration.pages) {
      await page.goto(design.url, { waitUntil: 'networkidle' });
      const actual = await page.locator('[data-guided-page-preview]').evaluate(node => ({ image: getComputedStyle(node).backgroundImage,
        width: node.clientWidth, height: node.clientHeight, border: getComputedStyle(node).borderTopWidth }));
      check(actual.image !== 'none' && actual.width > 800 && actual.height > 600, `${design.title}: the real Page renderer shows the selected background.`);
      const screenshot = path.join(output, 'actual-' + design.id + '.png'); await page.screenshot({ path: screenshot, fullPage: true }); report.screenshots.push(screenshot);
    }
    await page.goto(configuration.imageUrl, { waitUntil: 'networkidle' });
    const loadedImage = await page.locator('[data-guided-page-preview]').evaluate(async node => {
      const url = getComputedStyle(node).backgroundImage.match(/url\("?([^\")]+)"?\)/)?.[1];
      if (!url) return false;
      return await new Promise(resolve => { const image = new Image(); image.onload = () => resolve(image.naturalWidth === 192 && image.naturalHeight === 112); image.onerror = () => resolve(false); image.src = url; });
    });
    check(loadedImage, 'A chosen local background image loads from the owned preview media endpoint.');
    const originalUrl = page.url();
    for (const update of configuration.liveUpdates) {
      const actual = await page.locator('[data-guided-page-preview]').evaluate((root, update) => {
        const surface = document.createElement('div').style; surface.cssText = update.css;
        root.style.setProperty('--hl-paper', update.color);
        for (let i = 0; i < surface.length; i++) { const key = surface.item(i); root.style.setProperty(key, surface.getPropertyValue(key)); }
        const style = getComputedStyle(root);
        return { image: style.backgroundImage, size: style.backgroundSize, repeat: style.backgroundRepeat,
          border: style.borderTopWidth, borderStyle: style.borderTopStyle, radius: style.borderTopLeftRadius, color: style.backgroundColor };
      }, update);
      if (['cover', 'contain', 'tile'].includes(update.mode)) {
        check(actual.size === (update.mode === 'tile' ? 'auto' : update.mode)
          && actual.repeat === (update.mode === 'tile' ? 'repeat' : 'no-repeat')
          && actual.border === '6px' && actual.radius === '24px', `Immediate image update applies ${update.mode}, border, and corners.`);
      } else if (update.mode === 'tint') {
        check(actual.image.includes('data:image/svg+xml') && !actual.image.includes('/media/')
          && actual.border === '3px' && actual.borderStyle === 'dotted', `Immediate live tint ${update.color} replaces the prior uploaded image.`);
      } else {
        check(actual.image === 'none' && actual.border === '0px' && actual.radius === '0px' && actual.color === 'rgb(171, 200, 211)',
          'Color-only mode clears every prior pattern, image, border, and corner setting.');
      }
      check(page.url() === originalUrl, `${update.mode}: live background changes do not navigate or replace the Page.`);
    }
    const liveShot = path.join(output, 'live-color-border-reset.png'); await page.screenshot({ path: liveShot, fullPage: true }); report.screenshots.push(liveShot);
    await page.goto(configuration.reopenedUrl, { waitUntil: 'networkidle' });
    const reopened = await page.locator('[data-guided-page-preview]').evaluate(node => {
      const style = getComputedStyle(node); return { image: style.backgroundImage, size: style.backgroundSize, border: style.borderTopWidth, radius: style.borderTopLeftRadius };
    });
    check(reopened.image.includes('/media/') && reopened.size === 'contain' && reopened.border === '6px' && reopened.radius === '24px',
      'A saved project reopens its owned image, contain fit, border, and rounded corners.');
    const ownedShot = path.join(output, 'owned-image-background-reopened.png'); await page.screenshot({ path: ownedShot, fullPage: true }); report.screenshots.push(ownedShot);
    await page.setViewportSize({ width: 390, height: 800 });
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'The background canvas fits a narrow Page viewport.');
    fs.writeFileSync(path.join(output, 'background-browser-result.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ result: report.result, assertions: report.checks.length, thumbnails, contactSheet: sheet }));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
