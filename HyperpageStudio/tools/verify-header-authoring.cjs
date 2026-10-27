/* Focused real writer regression against a disposable generated header project. */
const fs = require('fs');
const path = require('path');
const net = require('net');
const { spawn, spawnSync } = require('child_process');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const [rootArgument, outputArgument, assetsArgument, sharedType = 'studio-header-row'] = process.argv.slice(2);
  if (!['studio-header-row', 'studio-body-row'].includes(sharedType)) throw new Error('Choose a shared Header or Body fixture.');
  const region = sharedType === 'studio-body-row' ? 'Body' : 'Header';
  const root = path.resolve(rootArgument), output = path.resolve(outputArgument);
  if (!root.toLowerCase().includes('qa') || !fs.existsSync(path.join(root, '.studio', 'hyperlib-work.json')))
    throw new Error('A disposable private QA project is required.');
  fs.mkdirSync(output, { recursive: true });
  const assets = path.resolve(assetsArgument || path.join(__dirname, '..', 'src', 'Hypershell', 'Assets', 'StudioHyperlibAuthoring'));
  const web = path.join(root, 'src', 'HyperLib.Host', 'wwwroot', 'studio-authoring'); fs.mkdirSync(web, { recursive: true });
  for (const file of ['index.html', 'app.js', 'element-editor.js', 'app.css', 'studio.css']) fs.copyFileSync(path.join(assets, file), path.join(web, file));
  const port = await new Promise(resolve => { const server = net.createServer(); server.listen(0, '127.0.0.1', () => { const port = server.address().port; server.close(() => resolve(port)); }); });
  const origin = `http://127.0.0.1:${port}`;
  const host = spawn('dotnet', ['run', '--configuration', 'Release', '--project', path.join(root, 'src', 'HyperLib.Host', 'HyperLib.Host.csproj'),
    '--no-launch-profile', '--', '--root', root, '--port', String(port)], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let logs = ''; host.stdout.on('data', b => logs = (logs + b).slice(-24000)); host.stderr.on('data', b => logs = (logs + b).slice(-24000));
  let browser;
  const report = { result: 'STUDIO_' + region.toUpperCase() + '_AUTHORING_OK', checks: [], screenshots: [] };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.checks.push(message); };
  const api = async route => { const response = await fetch(origin + route); if (!response.ok) throw new Error(await response.text()); return response.json(); };
  try {
    const deadline = Date.now() + 120000;
    while (true) {
      if (host.exitCode !== null) throw new Error('Owned QA gateway stopped: ' + logs);
      try { if ((await api('/api/health')).root.toLowerCase() === root.toLowerCase()) break; } catch { }
      if (Date.now() > deadline) throw new Error('QA gateway did not become ready: ' + logs);
      await new Promise(resolve => setTimeout(resolve, 180));
    }
    const pipeline = await api('/api/studio/pipeline');
    const before = await api('/api/state');
    const landing = before.pages[pipeline.landingPageId];
    const shared = landing.blocks.find(block => block.type === sharedType);
    check(!!shared && shared.values.layout.length > 0, 'The real saved landing Page contains a shared header row.');
    await api('/api/runtime/' + pipeline.workId);
    browser = await chromium.launch({ headless: true,
      executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
    const page = await browser.newPage({ viewport: { width: 1380, height: 950 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto(origin + '/studio-authoring/index.html', { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => window.__studioAuthoring?.state().ready);
    const frame = page.frameLocator('#page-frame');
    const row = frame.locator(`[data-block-id="${shared.id}"]`); await row.waitFor();
    await frame.locator('#studio-slot-hints').waitFor({ state: 'attached' });
    await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
    check(await row.locator('[data-header-element]').count() > 0, 'The actual writer renders header elements through the generated Work component.');
    await row.click({ position: { x: 5, y: 5 } });
    await page.locator('#panel-content').getByText('SHARED ' + region.toUpperCase(), { exact: true }).waitFor();
    check((await page.locator('#panel-content').textContent()).includes('Templates → ' + region)
      && await page.locator('#panel-content [data-field="layout"]').count() === 0,
      'The property inspector explains shared header editing without exposing serialized layout data.');
    check(await row.locator('[contenteditable="true"]').count() === 0,
      'The rendered shared header is protected from plain inline text editing.');
    const stable = await api('/api/state');
    await row.evaluate((row, blockId) => {
      const owner = window.frameElement;
      window.parent.postMessage({ source: 'hyperlib-renderer', kind: 'change', pageId: owner.dataset.pageId,
        generationId: owner.dataset.generationId, blockId, field: 'layout', value: 'Accidental inline replacement' }, location.origin);
    }, shared.id);
    await page.waitForTimeout(800);
    const after = await api('/api/state');
    check(after.revision === stable.revision && after.pages[landing.id].blocks.find(block => block.id === shared.id).values.layout === shared.values.layout,
      'A spurious renderer text message cannot replace the stored reusable header structure.');
    check(await page.evaluate(id => window.__studioAuthoring.state().nodeId === id && !window.__studioAuthoring.state().dirty, landing.id),
      'Header selection leaves the current Page and its saved state intact.');
    check(errors.length === 0, 'The real authoring surface reports no uncaught browser errors.');
    const screenshot = path.join(output, 'shared-' + region.toLowerCase() + '-inspector.png'); await page.screenshot({ path: screenshot, fullPage: true }); report.screenshots.push(screenshot);
    fs.writeFileSync(path.join(output, region.toLowerCase() + '-authoring-result.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ result: report.result, assertions: report.checks.length, screenshot }));
  } finally {
    if (browser) await browser.close();
    if (host.exitCode === null) spawnSync('taskkill.exe', ['/PID', String(host.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
  }
})().catch(error => { console.error(error.stack || String(error)); process.exitCode = 1; });
