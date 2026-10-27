const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const [configurationFile, output] = process.argv.slice(2);
  const configuration = JSON.parse(fs.readFileSync(configurationFile, 'utf8'));
  const browser = await chromium.launch({ headless: true,
    executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  const report = { result: 'HYPERLIB_HEADER_BROWSER_OK', checks: [], screenshots: [] };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.checks.push(message); };
  const esc = value => String(value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const errors = [];
  try {
    const page = await browser.newPage({ viewport: { width: 960, height: 700 } });
    page.on('pageerror', error => errors.push(error.message));
    const thumbnailDirectory = path.join(output, 'thumbnails'); fs.mkdirSync(thumbnailDirectory, { recursive: true });
    const hashes = [];
    for (const preset of configuration.pages) {
      await page.goto(preset.url, { waitUntil: 'networkidle' });
      const geometry = await page.locator('[data-region="header"]').evaluate(node => ({
        width: node.clientWidth - parseFloat(getComputedStyle(node).paddingLeft) - parseFloat(getComputedStyle(node).paddingRight),
        rows: node.querySelectorAll('[data-header-row]').length,
        children: [...node.querySelectorAll('[data-header-row]')].map(row => row.getBoundingClientRect().width),
        overflow: document.documentElement.scrollWidth > innerWidth + 1
      }));
      if (!(geometry.rows === preset.rows && geometry.children.every(width => width >= geometry.width - 4) && !geometry.overflow))
        console.error('HEADER_GEOMETRY', preset.id, JSON.stringify(geometry));
      check(geometry.rows === preset.rows && geometry.children.every(width => width >= geometry.width - 4) && !geometry.overflow,
        `${preset.title}: actual header rows fill the padded region without column squeezing or horizontal overflow.`);
      const file = path.join(thumbnailDirectory, preset.id + '.png');
      await page.locator('[data-region="header"]').screenshot({ path: file });
      hashes.push(crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex'));
      await page.setViewportSize({ width: 390, height: 800 });
      const narrow = await page.locator('[data-region="header"]').evaluate(header => {
        const texts = [...header.querySelectorAll('.studio-header-text')];
        const outside = texts.some(node => { const text = node.getBoundingClientRect(), parent = node.parentElement.parentElement.getBoundingClientRect();
          return text.left < parent.left - 1 || text.right > parent.right + 1 || text.bottom > parent.bottom + 1; });
        let overlap = false;
        for (let i = 0; i < texts.length; i++) for (let j = i + 1; j < texts.length; j++) {
          if (texts[i].parentElement.parentElement !== texts[j].parentElement.parentElement) continue;
          const a = texts[i].getBoundingClientRect(), b = texts[j].getBoundingClientRect();
          if (Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1) overlap = true;
        }
        const placeholderOverflow = [...header.querySelectorAll('.studio-header-media-placeholder')].some(node => node.scrollHeight > node.clientHeight + 1 || node.scrollWidth > node.clientWidth + 1);
        return { outside, overlap, placeholderOverflow, documentOverflow: document.documentElement.scrollWidth > innerWidth + 1 };
      });
      check(!narrow.outside && !narrow.overlap && !narrow.placeholderOverflow && !narrow.documentOverflow,
        `${preset.title}: the default header text and media slots remain contained at 390px. ${JSON.stringify(narrow)}`);
      await page.locator('[data-region="header"]').screenshot({ path: path.join(thumbnailDirectory, preset.id + '-390.png') });
      await page.setViewportSize({ width: 960, height: 700 });
    }
    check(new Set(hashes).size === 10, 'Ten header foundations have distinct actual rendered structures.');
    const cards = configuration.pages.map(preset => `<article><img alt="${esc(preset.title)}" src="data:image/png;base64,${fs.readFileSync(path.join(thumbnailDirectory, preset.id + '.png')).toString('base64')}"><strong>${esc(preset.title)}</strong></article>`).join('');
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.setContent(`<!doctype html><html><head><style>*{box-sizing:border-box}body{margin:0;padding:28px;background:#0f1921;color:#edf3f2;font:15px Segoe UI,system-ui}h1{font-size:25px;margin:0 0 8px;font-weight:600}p{color:#aabfb8;margin:0 0 24px}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:18px}article{border:1px solid #30463f;border-radius:10px;overflow:hidden;background:#17272e}img{display:block;width:100%;height:210px;object-fit:contain;background:#101d28}strong{display:block;padding:14px}</style></head><body><h1>Hyperpage Studio · editable header foundations</h1><p>Each design is built from the same editable rows, text, boxes, images, and video used in the page builder.</p><section class="grid">${cards}</section></body></html>`, { waitUntil: 'networkidle' });
    const sheet = path.join(output, 'header-contact-sheet.png'); await page.screenshot({ path: sheet, fullPage: true }); report.screenshots.push(sheet);

    await page.setViewportSize({ width: 1100, height: 850 });
    await page.goto(configuration.composedUrl, { waitUntil: 'networkidle' });
    check(await page.locator('[data-header-row]').count() === 2, 'The custom header contains independent announcement and identity rows.');
    check(await page.locator('[data-header-element="identity-box"] [data-header-element="nested-title"]').count() === 1,
      'A text element renders inside its reusable box, with positioning relative to that box.');
    const image = page.locator('[data-header-element="header-picture"] img');
    await image.evaluate(img => img.decode());
    check(await image.evaluate(img => img.naturalWidth === 240 && img.naturalHeight === 140), 'The imported header picture loads from the owned media endpoint.');
    const video = page.locator('[data-header-element="header-video"] video');
    await video.evaluate(video => video.readyState >= 1 ? undefined : new Promise((resolve, reject) => {
      video.addEventListener('loadedmetadata', resolve, { once: true }); video.addEventListener('error', () => reject(new Error('Header video failed to load')), { once: true });
    }));
    check(await video.evaluate(node => node.videoWidth > 0 && node.videoHeight > 0 && node.controls && !node.autoplay),
      'A valid imported video loads its metadata with reader controls and no forced autoplay.');
    const videoUrl = await video.getAttribute('src');
    const range = await page.request.get(new URL(videoUrl, page.url()).href, { headers: { Range: 'bytes=0-15' } });
    check(range.status() === 206 && (await range.body()).length === 16, 'The private media endpoint supports byte-range video playback.');
    await page.evaluate(() => {
      window.__headerDocument = document;
      window.__headerBody = document.querySelector('[data-region="main"]');
      window.__headerVideo = document.querySelector('[data-header-element="header-video"] video');
      window.__nestedTitle = document.querySelector('[data-header-element="nested-title"]');
      window.__headerBody.dataset.testContent = 'Authored body is preserved';
    });
    const originalUrl = page.url();
    for (const update of configuration.updates) {
      check(await page.evaluate(update.script), 'The native immediate header update finds the current rendered header.');
      const actual = await page.locator('[data-header-element="nested-title"]').evaluate(element => ({
        text: element.textContent, size: parseFloat(getComputedStyle(element).fontSize), weight: getComputedStyle(element).fontWeight,
        font: getComputedStyle(element).fontFamily, x: element.style.left, y: element.style.top, width: element.style.width,
        parent: element.parentElement.closest('[data-header-element]')?.dataset.headerElement
      }));
      check(actual.text === 'A living header <safe>' && actual.size === update.size && actual.weight === '500' && actual.font.includes('Georgia')
        && actual.x === update.x + '%' && actual.y === update.y + '%' && actual.width === update.width + '%' && actual.parent === 'identity-box',
        `Immediate edit applies text, font, nested coordinates, and width at ${update.size}px.`);
      const row = await page.locator('[data-header-row="main-header"]').evaluate(node => ({ height: parseFloat(getComputedStyle(node).height), border: getComputedStyle(node).borderTopStyle }));
      check(row.height === update.height && row.border === 'dashed', `Immediate edit applies row height ${update.height}px and border styling.`);
      check(page.url() === originalUrl && await page.evaluate(() => document === window.__headerDocument
        && document.querySelector('[data-region="main"]') === window.__headerBody
        && window.__headerBody.dataset.testContent === 'Authored body is preserved'
        && document.querySelector('[data-header-element="header-video"] video') === window.__headerVideo
        && document.querySelector('[data-header-element="nested-title"]') === window.__nestedTitle),
        'Live header edits preserve the document, body, video player, and stable nested element.');
    }
    check(await page.evaluate(configuration.structureScript) && await page.locator('[data-header-row]').count() === 3,
      'Adding a third independent header row updates immediately in the existing document.');
    check(await page.evaluate(() => window.__headerBody === document.querySelector('[data-region="main"]')),
      'Adding rows leaves the current Page body intact.');
    const wide = path.join(output, 'custom-header-wide.png'); await page.screenshot({ path: wide, fullPage: true }); report.screenshots.push(wide);
    await page.setViewportSize({ width: 390, height: 820 });
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'The custom nested header fits a narrow viewport.');
    const narrow = path.join(output, 'custom-header-narrow.png'); await page.screenshot({ path: narrow, fullPage: true }); report.screenshots.push(narrow);
    await page.setViewportSize({ width: 1100, height: 850 });
    await page.goto(configuration.reopenedUrl, { waitUntil: 'networkidle' });
    await page.locator('[data-header-element="header-picture"] img').evaluate(img => img.decode());
    check(await page.locator('[data-header-row]').count() === 3
      && await page.locator('[data-header-element="nested-title"]').textContent() === 'A living header <safe>'
      && await page.locator('[data-header-element="header-picture"] img').evaluate(img => img.naturalWidth === 240),
      'Portable project reopen retains rows, safely encoded text, nesting, and imported image without the original catalogue.');
    const reopened = path.join(output, 'owned-header-reopened.png'); await page.screenshot({ path: reopened, fullPage: true }); report.screenshots.push(reopened);
    check(errors.length === 0, 'Actual header render and live updates produce no uncaught browser errors.');
    fs.writeFileSync(path.join(output, 'header-browser-result.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ result: report.result, assertions: report.checks.length, contactSheet: sheet }));
  } finally { await browser.close(); }
})().catch(error => { console.error(error.stack || String(error)); process.exitCode = 1; });
