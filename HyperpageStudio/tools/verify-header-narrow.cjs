/* Reuses immutable renderer output and retained payloads; no build or live-library writes. */
const fs = require('fs');
const path = require('path');
const net = require('net');
const { spawn, spawnSync } = require('child_process');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const [previewArgument, outputArgument] = process.argv.slice(2);
  const preview = path.resolve(previewArgument), output = path.resolve(outputArgument);
  if (!preview.toLowerCase().includes('qa')) throw new Error('Only a disposable QA preview is supported.');
  const config = JSON.parse(fs.readFileSync(path.join(preview, 'header-browser.json'), 'utf8'));
  const firstId = new URL(config.pages[0].url).pathname.split('/').at(-1);
  const cache = path.join(preview, 'preview-cache');
  const session = fs.readdirSync(path.join(cache, 'sessions')).map(name => path.join(cache, 'sessions', name)).find(directory => fs.existsSync(path.join(directory, firstId + '.json')));
  const generation = fs.readdirSync(path.join(cache, 'generations')).map(name => path.join(cache, 'generations', name)).find(directory => fs.existsSync(path.join(directory, 'bin', 'Host.dll')));
  const csproj = fs.readFileSync(path.join(generation, 'project', 'Pages', 'Pages.csproj'), 'utf8');
  const assembly = csproj.match(/<AssemblyName>([^<]+)<\/AssemblyName>/)?.[1] || 'Pages';
  const port = await new Promise(resolve => { const server = net.createServer(); server.listen(0, '127.0.0.1', () => { const port = server.address().port; server.close(() => resolve(port)); }); });
  const origin = `http://127.0.0.1:${port}`;
  const host = spawn('dotnet', [path.join(generation, 'bin', 'Host.dll'), '--urls', origin], { cwd: path.join(generation, 'bin'),
    windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env, HYPERLIB_GUIDED_PREVIEW_CONTENT: session, HYPERLIB_GUIDED_PREVIEW_ASSEMBLY: assembly } });
  let log = ''; host.stdout.on('data', b => log = (log + b).slice(-12000)); host.stderr.on('data', b => log = (log + b).slice(-12000));
  let browser;
  const results = [];
  try {
    const deadline = Date.now() + 20000;
    while (true) {
      if (host.exitCode !== null) throw new Error(log);
      try { if ((await fetch(origin + '/healthz')).ok) break; } catch { }
      if (Date.now() > deadline) throw new Error('Owned cached preview did not start: ' + log);
      await new Promise(resolve => setTimeout(resolve, 120));
    }
    fs.mkdirSync(output, { recursive: true });
    browser = await chromium.launch({ headless: true,
      executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
    const page = await browser.newPage({ viewport: { width: 390, height: 800 } });
    for (const preset of config.pages) {
      await page.goto(origin + new URL(preset.url).pathname, { waitUntil: 'networkidle' });
      const geometry = await page.locator('[data-region="header"]').evaluate(header => {
        const rect = node => { const r = node.getBoundingClientRect(); return { left: r.left, top: r.top, right: r.right, bottom: r.bottom, width: r.width, height: r.height }; };
        const texts = [...header.querySelectorAll('.studio-header-text')].map(node => ({ node, element: node.parentElement, box: rect(node), parent: rect(node.parentElement.parentElement) }));
        const outside = texts.filter(({ box, parent }) => box.left < parent.left - 1 || box.right > parent.right + 1 || box.top < parent.top - 1 || box.bottom > parent.bottom + 1)
          .map(({ node, box, parent }) => ({ text: node.textContent, box, parent }));
        const overlaps = [];
        for (let i = 0; i < texts.length; i++) for (let j = i + 1; j < texts.length; j++) {
          const a = texts[i], b = texts[j];
          if (a.element.parentElement !== b.element.parentElement) continue;
          const width = Math.min(a.box.right, b.box.right) - Math.max(a.box.left, b.box.left);
          const height = Math.min(a.box.bottom, b.box.bottom) - Math.max(a.box.top, b.box.top);
          if (width > 1 && height > 1) overlaps.push({ text: a.node.textContent, other: b.node.textContent, width, height });
        }
        return { outside, overlaps, horizontalOverflow: document.documentElement.scrollWidth > innerWidth + 1 };
      });
      const screenshot = path.join(output, preset.id + '-390.png'); await page.screenshot({ path: screenshot, fullPage: true });
      results.push({ id: preset.id, title: preset.title, ...geometry, screenshot });
    }
    fs.writeFileSync(path.join(output, 'header-narrow-result.json'), JSON.stringify({ result: 'HYPERLIB_HEADER_NARROW_DIAGNOSTIC', results }, null, 2));
    console.log(JSON.stringify(results.map(({ id, outside, overlaps, horizontalOverflow }) => ({ id, outside: outside.length, overlaps: overlaps.length, horizontalOverflow }))));
  } finally {
    if (browser) await browser.close();
    if (host.exitCode === null) spawnSync('taskkill.exe', ['/PID', String(host.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
  }
})().catch(error => { console.error(error.stack || String(error)); process.exitCode = 1; });
