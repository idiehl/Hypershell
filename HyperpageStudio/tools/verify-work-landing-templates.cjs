const fs = require('fs');
const path = require('path');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const config = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
  const report = { passed: false, checks: [], screenshots: [], errors: [] };
  const check = (value, message) => { if (!value) throw new Error(message); report.checks.push(message); };
  const browser = await chromium.launch({ headless: true,
    executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  let page;
  try {
    page = await browser.newPage({ viewport: { width: 1500, height: 1000 } });
    page.on('pageerror', error => report.errors.push(error.message));
    await page.addInitScript(() => {
      window.__workLandingQaReady = [];
      window.addEventListener('message', event => {
        if (event.source === window && event.data?.source === 'hyperlib-renderer' && event.data.kind === 'ready')
          window.__workLandingQaReady.push(event.data);
      });
    });
    const waitForReader = async expectedCount => {
      await page.waitForFunction(({ title, count, landingId, expectedCount }) =>
        window.__workLandingQaReady?.some(message => message.pageId === landingId && message.generation)
        && document.querySelectorAll('[data-work-landing=true]').length === count
        && [...document.querySelectorAll('[data-work-landing=true]')].every(root => root.querySelector('h1')?.textContent === title
          && root.querySelectorAll('[data-work-page]').length === expectedCount),
      { title: config.workTitle, count: config.designs.length, landingId: config.landingId, expectedCount });
      // Server rendering can be followed by interactive hydration. Inspect a quiet, connected reader DOM.
      await page.evaluate(() => new Promise(resolve => {
        let quiet;
        const finish = () => { observer.disconnect(); clearTimeout(quiet); resolve(); };
        const touched = () => { clearTimeout(quiet); quiet = setTimeout(finish, 150); };
        const observer = new MutationObserver(touched);
        observer.observe(document.documentElement, { childList: true, subtree: true, attributes: true, characterData: true });
        touched();
        setTimeout(finish, 10000);
      }));
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    };
    await page.goto(config.url, { waitUntil: 'domcontentloaded' });
    await waitForReader(config.expected.length);
    check(await page.locator('[role=alert]').count() === 0, 'The actual compiled reader renders without a component error boundary.');
    const testContents = async (expected, phase) => {
      for (const design of config.designs) {
        const root = page.locator(`[data-work-landing=true].${design.id}`);
        check(await root.count() === 1, `${phase}: ${design.title} is one independent real component.`);
        const actual = await root.locator('[data-work-page]').evaluateAll(elements => elements.map(element => ({
          id: element.dataset.workPage, title: element.querySelector('.wle-page-title').textContent,
          group: element.querySelector('.wle-page-group')?.textContent || '',
          target: element.dataset.hyperlibNavigate, href: element.href
        })));
        check(actual.length === expected.length && actual.every((entry, index) => entry.id === expected[index].id
          && entry.title === expected[index].title && entry.group === expected[index].group),
          `${phase}: ${design.title} lists actual Pages in hierarchical reading order with their actual current titles.`);
        check(actual.every(entry => entry.id !== config.landingId && entry.target === entry.id
          && new URL(entry.href).pathname.endsWith('/page/' + encodeURIComponent(entry.id))),
          `${phase}: ${design.title} excludes the special landing and uses stable real Page links plus the native navigation bridge.`);
        check(await root.locator('.wle-wordmark').getAttribute('data-hyperlib-navigate') === config.landingId,
          `${phase}: ${design.title} links its brand to the authoritative Work landing.`);
        const first = expected[0].id;
        check(await root.locator('.wle-start').getAttribute('data-hyperlib-navigate') === first
          && await root.locator('.wle-menu [data-hyperlib-navigate]').first().getAttribute('data-hyperlib-navigate') === first,
          `${phase}: ${design.title} starts reading from the first actual content Page.`);
        const internal = await root.locator('a[href^="#"]').evaluateAll(elements => elements.map(element => ({
          href: element.getAttribute('href'), resolved: element.href,
          present: !!document.getElementById(element.getAttribute('href').slice(1)),
          ids: [...element.closest('[data-work-landing=true]').querySelectorAll('[id]')].map(target => target.id)
        })));
        check(internal.length >= 2 && internal.every(item => item.present), `${phase}: ${design.title} has working local Contents and return links. ${JSON.stringify(internal)}`);
        const library = root.locator('.wle-menu a').last();
        check((await library.getAttribute('data-hyperlib-navigate')).length > 0
          && new URL(await library.getAttribute('href')).origin === new URL(config.url).origin,
          `${phase}: ${design.title} returns to the actual gateway/library context.`);
      }
    };
    await testContents(config.expected, 'Initial');
    for (const entry of config.expected) {
      // The renderer declares a generation-scoped document <base>; use the browser's real resolution.
      const address = await page.locator(`[data-work-page="${entry.id}"]`).first().evaluate(anchor => anchor.href);
      const response = await page.request.get(address);
      const body = await response.text();
      check(response.ok() && !body.includes('This component could not render:'),
        `The actual reader destination for ${entry.title} responds successfully: ${response.status()} ${address}.`
          + (response.ok() ? '' : ` Response: ${body.slice(0, 1200)}`));
    }
    for (const viewport of [{ width: 1500, height: 1000, suffix: 'wide' }, { width: 390, height: 900, suffix: 'narrow' }]) {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      for (const design of config.designs) {
        const root = page.locator(`[data-work-landing=true].${design.id}`);
        const metrics = await root.evaluate(element => {
          const rect = element.getBoundingClientRect();
          const css = getComputedStyle(element);
          return { width: rect.width, viewport: innerWidth, scrollWidth: document.documentElement.scrollWidth,
            rootScroll: element.scrollWidth, background: css.backgroundColor,
            cards: getComputedStyle(element.querySelector('.wle-page-list')).gridTemplateColumns };
        });
        check(metrics.width <= viewport.width + 1 && metrics.rootScroll <= metrics.width + 1 && metrics.scrollWidth <= viewport.width + 1,
          `${design.title} fits the ${viewport.width}px viewport without horizontal cropping: ${JSON.stringify(metrics)}.`);
        const hex = design.background.slice(1);
        const rgb = `rgb(${parseInt(hex.slice(0, 2), 16)}, ${parseInt(hex.slice(2, 4), 16)}, ${parseInt(hex.slice(4, 6), 16)})`;
        check(metrics.background === rgb, `${design.title} retains its own authored background in the ${viewport.suffix} actual renderer.`);
        if (design.project === 'Atlas') check(await root.locator('.wle-wordmark').textContent().then(text => text.includes('ATLAS UNIVERSALIS')),
          `${design.title} carries the Atlas site identity while its catalog name stays generic.`);
        const screenshot = path.join(config.output, design.id + '-' + viewport.suffix + '.png');
        await root.screenshot({ path: screenshot }); report.screenshots.push(screenshot);
      }
    }
    fs.writeFileSync(path.join(config.fixture, 'browser-ready.json'), JSON.stringify({ passed: true, checks: report.checks.length }));
    const update = path.join(config.fixture, 'state-updated.json');
    const start = Date.now();
    while (!fs.existsSync(update)) {
      if (Date.now() - start > 90000) throw new Error('The owned live-content update did not arrive.');
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    const changed = JSON.parse(fs.readFileSync(update, 'utf8'));
    await page.setViewportSize({ width: 1500, height: 1000 });
    await page.reload({ waitUntil: 'domcontentloaded' });
    await waitForReader(changed.expected.length);
    await testContents(changed.expected, 'After live update');
    const first = page.locator(`[data-work-landing=true].${config.designs[0].id}`);
    check(await first.locator('.wle-wordmark').textContent() === 'A personally edited brand', 'An independently edited brand survives a live content reload.');
    check(await first.locator('[data-field=text]').textContent() === 'An introduction edited after the renderer was already running.',
      'The real component binds the declared inline-editable introduction field.');
    await first.locator('.wle-cover img').waitFor();
    await first.locator('.wle-cover img').evaluate(async image => { if (!image.complete) await new Promise((resolve, reject) => {
      image.addEventListener('load', resolve, { once: true }); image.addEventListener('error', reject, { once: true });
    }); });
    check(await first.locator('.wle-cover img').evaluate(image => image.complete && image.naturalWidth === 640
      && image.alt === 'Locally owned gold circle cover' && new URL(image.src).pathname === '/media/landing-cover.svg'),
      'The Page loads its own selected cover image at its real dimensions with the edited description.');
    check(await page.locator('[role=alert]').count() === 0 && report.errors.length === 0,
      'All ten generated components and the live Page refresh complete without browser or component errors.');
    const screenshot = path.join(config.output, 'work-landing-edited-cover-wide.png');
    await first.screenshot({ path: screenshot }); report.screenshots.push(screenshot);
    report.passed = true;
    fs.writeFileSync(path.join(config.output, 'work-landing-templates-browser-result.json'), JSON.stringify(report, null, 2));
    console.log('STUDIO_WORK_LANDING_TEMPLATES_BROWSER_OK|checks=' + report.checks.length + '|renders=' + report.screenshots.length);
  } catch (error) {
    report.error = String(error.stack || error);
    if (page) {
      report.readerReady = await page.evaluate(() => window.__workLandingQaReady || []).catch(() => []);
      fs.writeFileSync(path.join(config.output, 'work-landing-templates-failure.html'), await page.content().catch(() => ''));
      report.anchorDiagnostics = await page.locator('[data-work-landing=true]').evaluateAll(roots => roots.map(root => ({
        classes: root.className, ids: [...root.querySelectorAll('[id]')].map(element => element.id),
        anchors: [...root.querySelectorAll('a')].map(anchor => ({ href: anchor.getAttribute('href'), resolved: anchor.href,
          target: anchor.dataset.hyperlibNavigate, text: anchor.textContent }))
      }))).catch(() => []);
    }
    fs.writeFileSync(path.join(config.output, 'work-landing-templates-browser-failure.json'), JSON.stringify(report, null, 2));
    if (page) await page.screenshot({ path: path.join(config.output, 'work-landing-templates-failure.png') }).catch(() => {});
    throw error;
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
