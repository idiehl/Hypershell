const fs = require('node:fs');
const path = require('node:path');
const { spawn, spawnSync } = require('node:child_process');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const fixture = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
  const out = path.resolve(process.argv[3]);
  if (!out.toLowerCase().includes('qa')) throw new Error('Use isolated QA output.');
  const checks = [];
  const screenshotGeometry = [];
  const verify = (value, message) => { if (!value) throw new Error(message); checks.push(message); };
  const environment = { ...process.env, DOTNET_PROCESSOR_COUNT: '2', MSBUILDDISABLENODEREUSE: '1', ASPNETCORE_ENVIRONMENT: 'Development' };
  const build = (host, label) => {
    const result = spawnSync('dotnet', ['build', 'Host.csproj', '-c', 'Release', '--nologo', '-v:minimal', '-m:1', '-nr:false'], { cwd: host, windowsHide: true, env: environment, encoding: 'utf8', timeout: 240000, maxBuffer: 4 * 1024 * 1024 });
    fs.writeFileSync(path.join(out, label + '-build.txt'), (result.stdout || '') + (result.stderr || '') + (result.error ? '\n' + result.error : ''));
    verify(result.status === 0, label + ' compiles as a real generated ASP.NET Host.');
  };
  const start = async host => {
    const binary = path.join(host, 'bin', 'Release', 'net10.0', 'Host.dll');
    if (!fs.existsSync(binary)) throw new Error('Missing generated Host DLL: ' + binary);
    const child = spawn('dotnet', [binary, '--urls', 'http://127.0.0.1:0'], { cwd: host, windowsHide: true, env: environment, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '', errorOutput = '';
    const origin = await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('Generated Host did not listen: ' + output + errorOutput)), 45000);
      child.stdout.on('data', data => { output += data; const match = output.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/); if (match) { clearTimeout(timeout); resolve(match[1]); } });
      child.stderr.on('data', data => { errorOutput += data; });
      child.on('error', error => { clearTimeout(timeout); reject(error); });
      child.once('exit', code => { clearTimeout(timeout); reject(new Error('Generated Host exited ' + code + ': ' + output + errorOutput)); });
    }).catch(error => { child.kill(); throw error; });
    return { origin, stop: () => new Promise(resolve => { if (child.exitCode !== null) return resolve(); child.once('exit', resolve); child.kill(); }) };
  };
  build(fixture.allRazorHost, 'all-complete-templates');
  const allHost = await start(fixture.allRazorHost);
  try {
    for (const entry of fixture.completePages) {
      const response = await fetch(allHost.origin + entry.route);
      const html = await response.text();
      verify(response.status === 200 && html.includes('<title>' + entry.title + '</title>') && !html.includes('An unhandled exception'), 'Compiled complete template serves ' + entry.route + '.');
    }
  } finally { await allHost.stop(); }
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  try {
    for (const project of fixture.projects) {
      build(project.host, project.kind + '-scratch');
      const host = await start(project.host);
      try {
        const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
        const errors = [], badRequests = [];
        page.on('pageerror', error => errors.push(String(error)));
        page.on('response', response => { if (response.status() >= 400 && response.url().startsWith(host.origin)) badRequests.push(response.status() + ' ' + response.url()); });
        await page.goto(host.origin + '/', { waitUntil: 'networkidle' });
        await page.waitForFunction(() => [...document.querySelectorAll('.studio-web-navigation-links a')].some(a => a.textContent === 'Renamed article'));
        await page.waitForFunction(() => [...document.images].every(image => image.complete));
        verify(await page.locator('img').evaluateAll(images => images.every(image => image.getAttribute('src')?.trim() && image.naturalWidth > 0)), project.kind + ' has no blank-source or broken images, including its unfilled Header logo.');
        verify(await page.locator('[data-header-element="web-empty-image-slot"], [data-header-element="web-empty-video-slot"]').evaluateAll(slots => slots.length === 2 && slots.every(slot => slot.children.length === 0 && !slot.textContent.trim())), project.kind + ' publishes empty image/video content slots as blank layout regions.');
        verify(await page.locator('body').innerText().then(text => text.includes('Authored landing content')), project.kind + ' renders content authored through the real visual document.');
        verify(await page.locator('[data-header-element="web-nested-box"] [data-header-element="web-image"] img').evaluate(img => img.complete && img.naturalWidth > 0), project.kind + ' serves the nested, independently imported image.');
        const geometry = await page.locator('[data-header-element="web-nested-box"] [data-header-element="web-image"]').evaluate(element => ({ child: element.getBoundingClientRect().toJSON(), parent: element.parentElement.getBoundingClientRect().toJSON() }));
        verify(Math.abs(geometry.child.y - geometry.parent.y) <= 1.1 && geometry.child.x >= geometry.parent.x - 1, project.kind + ' zero-coordinate nested media aligns with the Box content origin.');
        verify(await page.locator('.studio-web-navigation-links a').filter({ hasText: 'Custom reference' }).count() >= 2, project.kind + ' preserves configured custom references while refreshing Page links.');
        const footer = page.locator('footer .studio-web-navigation');
        verify(await footer.locator('[rel="prev"]').count() === 0 && await footer.locator('[rel="next"]').getAttribute('href').then(href => href.startsWith('/page-1')), project.kind + ' landing footer has the correct Next link and no Previous link.');
        const hrefs = await page.locator('.studio-web-navigation [data-studio-page-link]').evaluateAll(elements => [...new Set(elements.map(element => element.getAttribute('href')))]);
        for (const href of hrefs) { const response = await fetch(host.origin + href); verify(response.ok, project.kind + ' generated navigation reaches ' + href + '.'); }
        const foundation = await page.locator('[data-studio-page-template]').evaluate(element => ({ background: getComputedStyle(element).backgroundImage, border: getComputedStyle(element).borderTopWidth }));
        verify(foundation.background.includes('data:image/svg+xml') && foundation.border === '2px', project.kind + ' preserves the actual dense-star background and Page border in shared CSS.');
        await page.goto(host.origin + '/page-1/', { waitUntil: 'networkidle' });
        await page.waitForSelector('footer a[rel="next"]');
        verify(await page.locator('footer a[rel="prev"]').getAttribute('href') === '/' && await page.locator('footer a[rel="next"]').getAttribute('href').then(href => href.startsWith('/renamed-article')), project.kind + ' middle-Page footer follows the updated route index.');
        await page.goto(host.origin + '/renamed-article/', { waitUntil: 'networkidle' });
        await page.waitForSelector('footer a[rel="prev"]');
        const authoredSurface = await page.locator('[data-studio-page-template]').evaluate(element => {
          const style = getComputedStyle(element);
          return { color: style.backgroundColor, image: style.backgroundImage, borderWidth: style.borderTopWidth, borderColor: style.borderTopColor, borderStyle: style.borderTopStyle };
        });
        verify(authoredSurface.color === 'rgb(34, 51, 68)' && authoredSurface.image === 'none'
          && authoredSurface.borderWidth === '5px' && authoredSurface.borderColor === 'rgb(242, 184, 75)' && authoredSurface.borderStyle === 'solid',
          project.kind + ' saved and reopened Page background and border override its shared foundation in the real browser.');
        verify(await page.locator('footer a[rel="next"]').count() === 0 && await page.locator('footer a[rel="prev"]').getAttribute('href').then(href => href.startsWith('/page-1')), project.kind + ' newly added Page uses the saved layout, shared navigation runtime, and correct last-Page links.');
        await page.setViewportSize({ width: 390, height: 844 });
        const primary = page.locator('.studio-web-navigation[data-nav-breakpoint]').first();
        await primary.waitFor();
        await page.waitForFunction(() => document.querySelector('.studio-web-navigation[data-nav-breakpoint]').dataset.navCompact === 'true');
        verify(!await primary.locator('.studio-web-navigation-links').isVisible(), project.kind + ' mobile navigation collapses in the actual generated project.');
        await primary.locator('button').click();
        verify(await primary.locator('.studio-web-navigation-links').isVisible() && await primary.locator('button').getAttribute('aria-expanded') === 'true', project.kind + ' actual generated Menu button opens the Page links.');
        await page.keyboard.press('Escape');
        verify(!await primary.locator('.studio-web-navigation-links').isVisible(), project.kind + ' Escape closes the actual generated mobile menu.');
        await page.evaluate(async () => {
          await document.fonts.ready;
          await Promise.all([...document.images].map(image => image.decode().catch(() => {})));
          scrollTo(0, 0);
          await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        });
        await page.waitForFunction(() => innerWidth === 390 && innerHeight === 844
          && document.querySelector('[data-studio-page-template]').getBoundingClientRect().height > 600
          && document.documentElement.scrollHeight >= 844);
        verify(await page.locator('img').evaluateAll(images => images.every(image => image.getAttribute('src')?.trim() && image.complete && image.naturalWidth > 0)), project.kind + ' added Page has no broken or blank-source images after viewport resizing.');
        verify(errors.length === 0 && badRequests.length === 0, project.kind + ' actual generated pages, media, styles, scripts, and route index have no browser errors or failed local assets: ' + errors.join(';') + badRequests.join(';'));
        const geometryBeforeScreenshot = await page.evaluate(() => ({ innerWidth, innerHeight, scrollHeight: document.documentElement.scrollHeight, rootHeight: document.querySelector('[data-studio-page-template]').getBoundingClientRect().height }));
        const png = await page.screenshot({ path: path.join(out, project.kind.toLowerCase() + '-scratch-mobile.png'), fullPage: true, animations: 'disabled' });
        const pngWidth = png.readUInt32BE(16), pngHeight = png.readUInt32BE(20);
        screenshotGeometry.push({ kind: project.kind, ...geometryBeforeScreenshot, pngWidth, pngHeight });
        verify(pngWidth === 390 && pngHeight >= 844 && pngHeight >= geometryBeforeScreenshot.rootHeight - 2,
          project.kind + ' mobile evidence captures the stable full Page rather than a transient clipped strip.');
        // Preserve the generated Host origin so the actual Canvas renderer's CSS links load normally.
        await page.setViewportSize({ width: 1280, height: 900 });
        await page.setContent(fs.readFileSync(project.canvasPath, 'utf8'), { waitUntil: 'networkidle' });
        const canvasRoot = page.locator('[data-vp-id="' + project.canvasRootId + '"]');
        await canvasRoot.waitFor();
        const canvasSurface = await canvasRoot.evaluate(element => {
          const style = getComputedStyle(element);
          return { color: style.backgroundColor, image: style.backgroundImage, borderWidth: style.borderTopWidth, borderColor: style.borderTopColor, borderStyle: style.borderTopStyle };
        });
        verify(canvasSurface.color === 'rgb(34, 51, 68)' && canvasSurface.image === 'none'
          && canvasSurface.borderWidth === '5px' && canvasSurface.borderColor === 'rgb(242, 184, 75)' && canvasSurface.borderStyle === 'solid',
          project.kind + ' actual Canvas renderer preserves the saved Page background and border above the shared foundation.');
        verify(errors.length === 0 && badRequests.length === 0,
          project.kind + ' actual Canvas loads its foundation and Page CSS from the generated Host without browser or asset errors: ' + errors.join(';') + badRequests.join(';'));
        await page.screenshot({ path: path.join(out, project.kind.toLowerCase() + '-authored-canvas.png'), animations: 'disabled' });
        await page.close();
      } finally { await host.stop(); }
    }
  } finally { await browser.close(); }
  const result = { status: 'STUDIO_WEB_PROJECT_BROWSER_OK', checks, count: checks.length, screenshotGeometry };
  fs.writeFileSync(path.join(out, 'studio-web-project-browser-result.json'), JSON.stringify(result, null, 2));
  process.stdout.write(JSON.stringify(result) + '\n');
})().catch(error => { process.stderr.write(String(error.stack || error) + '\n'); process.exitCode = 1; });
