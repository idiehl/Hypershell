/* Render the actual application-pattern catalog in a generated, private QA Work. */
const fs = require('fs'), path = require('path'), net = require('net');
const { spawn, spawnSync } = require('child_process');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const [rootArgument, outputArgument, assetsArgument, inventoryArgument] = process.argv.slice(2);
  if (!rootArgument || !outputArgument || !assetsArgument)
    throw new Error('Usage: node verify-application-templates.cjs PRIVATE_QA_LIBRARY OUTPUT STUDIO_ASSETS [INVENTORY_JSON]');
  const inputRoot = path.resolve(rootArgument), output = path.resolve(outputArgument), assets = path.resolve(assetsArgument);
  const inventoryPath = inventoryArgument ? path.resolve(inventoryArgument)
    : path.join(inputRoot, 'studio-application-template-result.json');
  const inventory = JSON.parse(fs.readFileSync(inventoryPath, 'utf8').replace(/^\uFEFF/, ''));
  const root = path.resolve(inventory.library || inventory.projectRoot || inputRoot);
  if (!root.toLowerCase().includes('qa') || !fs.existsSync(path.join(root, '.studio', 'hyperlib-work.json')))
    throw new Error('Only a generated private QA Work with its pipeline identity can be rendered by this workflow.');
  if (inventory.projectRoot && (!path.resolve(inventory.projectRoot).toLowerCase().startsWith(inputRoot.toLowerCase())
    || !root.toLowerCase().startsWith(inputRoot.toLowerCase())))
    throw new Error('The inventory project is outside the supplied private QA root.');
  if (!Array.isArray(inventory.pages) || inventory.pages.length !== 10
    || !Array.isArray(inventory.components) || inventory.components.length !== 80 || !inventory.canvasPageId)
    throw new Error('The backend inventory must identify exactly ten new Pages and eighty section/module blocks.');
  fs.mkdirSync(output, { recursive: true });
  const web = path.join(root, 'src', 'HyperLib.Host', 'wwwroot', 'studio-authoring');
  fs.mkdirSync(web, { recursive: true });
  for (const file of ['index.html', 'app.js', 'element-editor.js', 'app.css', 'studio.css'])
    fs.copyFileSync(path.join(assets, file), path.join(web, file));
  const port = await new Promise((resolve, reject) => {
    const server = net.createServer(); server.once('error', reject);
    server.listen(0, '127.0.0.1', () => { const value = server.address().port; server.close(() => resolve(value)); });
  });
  const origin = 'http://127.0.0.1:' + port;
  const host = spawn('dotnet', ['run', '--no-build', '--configuration', 'Release', '--project',
    path.join(root, 'src', 'HyperLib.Host', 'HyperLib.Host.csproj'), '--no-launch-profile', '--', '--root', root, '--port', String(port)],
  { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env, DOTNET_PROCESSOR_COUNT: '2', DOTNET_NOLOGO: '1' } });
  let logs = '', browser, page;
  host.stdout.on('data', chunk => logs = (logs + chunk).slice(-50000));
  host.stderr.on('data', chunk => logs = (logs + chunk).slice(-50000));
  const report = { result: 'STUDIO_APPLICATION_TEMPLATES_BROWSER_OK', projectRoot: root, inventoryPath,
    checks: [], pages: [], components: [], screenshots: [], errors: [] };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.checks.push(message); };
  const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
  const wait = async (predicate, description, timeout = 90000) => {
    const until = Date.now() + timeout;
    while (Date.now() < until) {
      if (host.exitCode !== null) throw new Error('The owned QA gateway exited: ' + logs);
      if (await predicate()) return;
      await pause(120);
    }
    throw new Error('Timed out: ' + description);
  };
  const api = async route => {
    const response = await fetch(origin + route);
    if (!response.ok) throw new Error(route + ': ' + response.status + ' ' + await response.text());
    return response.json();
  };
  const normalized = value => String(value || '').replace(/\s+/g, ' ').trim().toLowerCase();
  try {
    await wait(async () => {
      try { return (await api('/api/health')).root.toLowerCase() === root.toLowerCase(); }
      catch { return false; }
    }, 'the owned private gateway', 120000);
    const pipeline = await api('/api/studio/pipeline'), state = await api('/api/state');
    check(!inventory.workId || pipeline.workId === inventory.workId, 'The running gateway matches the inventory Work identity.');
    const runtime = await api('/api/runtime/' + pipeline.workId);
    check(typeof runtime.proxyPrefix === 'string' && runtime.proxyPrefix.startsWith('/'),
      'The real imported Work builds and exposes its owned renderer proxy.');
    check(inventory.pages.every(item => state.pages[item.pageId] && state.pages[item.pageId].workId === pipeline.workId)
      && state.pages[inventory.canvasPageId]?.workId === pipeline.workId,
      'All ten imported Pages and the section/module canvas belong to this private Work.');
    browser = await chromium.launch({ headless: true,
      executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
    page = await browser.newPage({ viewport: { width: 1500, height: 1000 } });
    page.on('pageerror', error => report.errors.push(error.stack || error.message));
    const readerUrl = id => origin + runtime.proxyPrefix + 'page/' + encodeURIComponent(id) + '?edit=0';
    const uniqueShots = [];
    for (const role of [/analytics|dashboard|report/i, /media|audio|video/i, /record|knowledge|directory/i]) {
      const sample = inventory.pages.find(item => role.test(item.templateId + ' ' + item.title) && !uniqueShots.includes(item.pageId));
      if (sample) uniqueShots.push(sample.pageId);
    }
    for (const sample of inventory.pages) if (uniqueShots.length < 3 && !uniqueShots.includes(sample.pageId)) uniqueShots.push(sample.pageId);
    for (const item of inventory.pages) {
      for (const width of [1500, 390]) {
        await page.setViewportSize({ width, height: width === 390 ? 844 : 1000 });
        const response = await page.goto(readerUrl(item.pageId), { waitUntil: 'networkidle' });
        check(response?.ok(), item.title + ' returns a successful real rendered Page at ' + width + 'px.');
        await page.locator('.hl-document').waitFor();
        await page.evaluate(() => document.fonts.ready);
        await page.evaluate(() => document.querySelectorAll('.hl-document img').forEach(image => image.loading = 'eager'));
        await wait(() => page.evaluate(() => [...document.querySelectorAll('.hl-document img')]
          .every(image => image.complete && image.naturalWidth > 0)), 'all local images on ' + item.title);
        const metrics = await page.evaluate(() => {
          const documentSurface = document.querySelector('.hl-document');
          const images = [...documentSurface.querySelectorAll('img')];
          return { viewport: innerWidth, scrollWidth: document.documentElement.scrollWidth,
            bodyText: documentSurface.innerText.replace(/\s+/g, ' ').trim(),
            blockIds: [...documentSurface.querySelectorAll('.hl-region > .hl-block[data-block-id]')].map(block => block.dataset.blockId),
            navigation: documentSurface.querySelectorAll('nav a[href]').length,
            headings: documentSurface.querySelectorAll('h1,h2,h3').length,
            images: images.map(image => ({ src: image.getAttribute('src'), naturalWidth: image.naturalWidth,
              alt: image.getAttribute('alt'), width: image.getBoundingClientRect().width })),
            overflowing: [...documentSurface.querySelectorAll('*')].filter(element => {
              const box = element.getBoundingClientRect();
              if (!box.width || getComputedStyle(element).position === 'fixed') return false;
              if (element.closest('[style*="overflow-x: auto"], [style*="overflow-x:auto"]')) return false;
              return box.right > innerWidth + 2 || box.left < -2;
            }).slice(0, 12).map(element => ({ tag: element.tagName, name: element.getAttribute('data-vp-id'),
              width: element.getBoundingClientRect().width, left: element.getBoundingClientRect().left, text: element.textContent.slice(0, 100) })) };
        });
        check(metrics.scrollWidth <= width + 2, item.title + ' fits the ' + width + 'px viewport without horizontal document overflow. ' + JSON.stringify(metrics.overflowing));
        check(metrics.headings >= 2 && metrics.navigation > 0, item.title + ' renders its composed headings and built-in navigation at ' + width + 'px.');
        check(Array.isArray(item.blockIds) && item.blockIds.length > 0 && item.blockIds.every(id => metrics.blockIds.includes(id)),
          item.title + ' renders every imported saved block at ' + width + 'px.');
        const expectedTexts = (item.expectedTexts || []).map(normalized).filter(Boolean);
        check(expectedTexts.length > 0 && expectedTexts.every(text => normalized(metrics.bodyText).includes(text)),
          item.title + ' displays its expected authored sample content at ' + width + 'px.');
        check(metrics.images.every(image => image.naturalWidth > 0 && image.width > 0 && image.alt),
          item.title + ' uses loaded, labeled local images at ' + width + 'px.');
        report.pages.push({ templateId: item.templateId, pageId: item.pageId, title: item.title, width, metrics });
        if (uniqueShots.includes(item.pageId)) {
          const slug = item.templateId.replace(/[^a-zA-Z0-9_-]/g, '-'), shot = path.join(output, slug + '-' + width + '.png');
          await page.screenshot({ path: shot, fullPage: true }); report.screenshots.push(shot);
        }
      }
    }
    await page.setViewportSize({ width: 1500, height: 1000 });
    await page.goto(readerUrl(inventory.canvasPageId), { waitUntil: 'networkidle' });
    await page.locator('.hl-document').waitFor();
    await page.evaluate(() => document.querySelectorAll('.hl-document img').forEach(image => image.loading = 'eager'));
    await wait(() => page.evaluate(() => [...document.querySelectorAll('.hl-document img')].every(image => image.complete && image.naturalWidth > 0)),
      'all section and module media');
    const componentMetrics = await page.evaluate(components => components.map(item => {
      const block = [...document.querySelectorAll('.hl-region > .hl-block[data-block-id]')].find(element => element.dataset.blockId === item.blockId);
      if (!block) return { templateId: item.templateId, missing: true };
      const box = block.getBoundingClientRect();
      return { templateId: item.templateId, scope: item.scope, title: item.title, width: box.width, height: box.height,
        text: block.innerText.replace(/\s+/g, ' ').trim(), imageCount: block.querySelectorAll('img').length,
        loadedImages: [...block.querySelectorAll('img')].every(image => image.complete && image.naturalWidth > 0),
        descendantCount: block.querySelectorAll('[data-vp-id]').length };
    }), inventory.components);
    for (const [index, item] of inventory.components.entries()) {
      const actual = componentMetrics[index], expectedTexts = (item.expectedTexts || []).map(normalized).filter(Boolean);
      check(!actual.missing && actual.width > 0 && actual.height > 0 && actual.loadedImages
        && expectedTexts.length > 0 && expectedTexts.every(text => normalized(actual.text).includes(text)),
        'The actual renderer displays the independently inserted ' + item.scope + ' “' + item.title + '” with its expected content and local media.');
      report.components.push(actual);
    }
    check(componentMetrics.filter(item => item.scope === 'section').length === 30
      && componentMetrics.filter(item => item.scope === 'module').length === 50,
      'The actual section/module canvas contains all thirty new Sections and fifty new Modules.');
    const graph = inventory.components.find(item => /horizontal-bars|bar-graph|composition-graph/i.test(item.templateId));
    if (graph) {
      const block = page.locator('[data-block-id="' + graph.blockId + '"]').first();
      await block.scrollIntoViewIfNeeded();
      const shot = path.join(output, 'application-section-graph.png');
      await block.screenshot({ path: shot }); report.screenshots.push(shot);
    }

    // Exercise the current authoring controller against one of the new imported Page templates.
    // A minimal native bridge supplies authoritative panel choices; all content still uses the real save gateway.
    const imagePage = report.pages.find(item => item.width === 1500 && item.metrics.images.length > 0);
    check(!!imagePage, 'At least one new application Page supplies a real nested image for authoring regression coverage.');
    await page.goto(origin + '/studio-authoring/index.html#' + encodeURIComponent(imagePage.pageId), { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => window.__studioAuthoring?.state().currentGenerationReady === true);
    const frame = page.frameLocator('#page-frame');
    await frame.locator('#studio-canvas-tools').waitFor({ state: 'attached' });
    const image = frame.locator('.hl-region > .hl-block img').first();
    await image.click();
    await page.waitForFunction(() => !!window.__studioAuthoring.state().selectedElement?.path);
    const imageSelection = await page.evaluate(() => window.__studioAuthoring.state().selectedElement);
    check((await page.evaluate(() => window.__studioAuthoring.state().nodeId)) === imagePage.pageId && imageSelection.path !== '',
      'Clicking an image inside a new complete Page selects the image independently from its template wrapper.');
    await page.locator('[data-left-panel="inspector"]').click();
    check(await page.locator('#properties-content [data-element-attribute="alt"]').isVisible(),
      'The new imported Page image exposes its own editable Properties fields.');
    async function chooseCode() {
      return page.evaluate(() => {
        window.chrome ||= {};
        window.chrome.webview ||= { postMessage() {}, addEventListener() {} };
        return window.__studioAuthoring.configureWorkspaceChrome({ tabs: [{ id: 'application-template-qa', title: 'Authoring' }],
          activeId: 'application-template-qa', canSave: true,
          leftPanel: { selectedId: 'code', isOpen: true, options: [{ id: 'contents', title: 'Contents' },
            { id: 'properties', title: 'Properties' }, { id: 'appearance', title: 'Appearance' }, { id: 'code', title: 'Code' }] },
          rightPanel: { selectedId: 'templates', isOpen: false, options: [{ id: 'templates', title: 'Templates' }] } });
      });
    }
    check(await chooseCode(), 'The existing coordinated Code panel accepts the new imported template selection.');
    await page.locator('#element-code').waitFor({ state: 'visible' });
    check(/^<img\b/i.test((await page.locator('#element-code').inputValue()).trim()),
      'Code shows the selected image markup rather than the complete imported Page.');
    const ancestors = page.locator('#properties-content button[data-element-path]');
    const chain = await ancestors.evaluateAll(buttons => buttons.map(button => ({ path: button.dataset.elementPath, tag: button.textContent.trim() })));
    const container = [...chain].reverse().find(item => item.path !== imageSelection.path && ['figure', 'div', 'section', 'article'].includes(item.tag));
    check(!!container, 'A new imported template provides an explicit containing-box breadcrumb.');
    await page.locator('#properties-content button[data-element-path]').evaluateAll((buttons, chosen) => {
      const button = buttons.find(item => item.dataset.elementPath === chosen); if (!button) throw new Error('Containing breadcrumb disappeared.'); button.click();
    }, container.path);
    const boxCode = await page.locator('#element-code').inputValue();
    check(/^<(figure|div|section|article)\b/i.test(boxCode.trim()) && boxCode.includes('<img')
      && (await page.evaluate(() => window.__studioAuthoring.state().selectedElement.path)) === container.path,
      'Selecting the containing box shows its nested image HTML in the Code editor.');
    const authoringShot = path.join(output, 'application-template-selected-code.png');
    await page.screenshot({ path: authoringShot, fullPage: true }); report.screenshots.push(authoringShot);
    check(report.errors.length === 0, 'All new templates and the selection/Code workflow produce no uncaught browser errors. ' + report.errors.join('; '));
    report.passed = true;
    const receipt = path.join(output, 'studio-application-template-browser-result.json');
    fs.writeFileSync(receipt, JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ result: report.result, assertions: report.checks.length, pages: 10, sections: 30, modules: 50, receipt }));
  } catch (error) {
    report.passed = false; report.failure = error.stack || String(error);
    fs.writeFileSync(path.join(output, 'application-template-host.log'), logs);
    fs.writeFileSync(path.join(output, 'studio-application-template-browser-failure.json'), JSON.stringify(report, null, 2));
    if (page) await page.screenshot({ path: path.join(output, 'application-template-failure.png'), fullPage: true }).catch(() => {});
    throw error;
  } finally {
    if (browser) await browser.close();
    if (host.exitCode === null) spawnSync('taskkill.exe', ['/PID', String(host.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
  }
})().catch(error => { console.error(error.stack || String(error)); process.exitCode = 1; });
