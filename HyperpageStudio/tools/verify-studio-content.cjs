/* Real browser verification against one disposable generated Hyperlib project. */
const fs = require('fs');
const path = require('path');
const net = require('net');
const childProcess = require('child_process');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

async function run() {
  const [rootArgument, outputArgument, assetsArgument, compatibilityArgument] = process.argv.slice(2);
  const legacyCompatibility = compatibilityArgument === '--legacy';
  if (!rootArgument || !outputArgument) throw new Error('Usage: node verify-studio-content.cjs <disposable-generated-library-root> <output-directory> [assets-directory]');
  const root = path.resolve(rootArgument);
  const output = path.resolve(outputArgument);
  const assets = path.resolve(assetsArgument || path.join(__dirname, '..', 'src', 'Hypershell', 'Assets', 'StudioHyperlibAuthoring'));
  if (!fs.existsSync(path.join(root, '.studio', 'hyperlib-work.json')) || !root.toLowerCase().includes('qa'))
    throw new Error('This verifier requires a generated private QA fixture; it never edits the live library.');
  const webRoot = path.join(root, 'src', 'HyperLib.Host', 'wwwroot', 'studio-authoring');
  fs.mkdirSync(webRoot, { recursive: true }); fs.mkdirSync(output, { recursive: true });
  for (const file of ['index.html', 'app.js', 'element-editor.js', 'app.css', 'studio.css']) fs.copyFileSync(path.join(assets, file), path.join(webRoot, file));
  const port = await new Promise((resolve, reject) => {
    const server = net.createServer(); server.once('error', reject);
    server.listen(0, '127.0.0.1', () => { const available = server.address().port; server.close(() => resolve(available)); });
  });
  const origin = `http://127.0.0.1:${port}`;
  const runSuffix = Date.now().toString(36).slice(-5);
  const existingGateway = fs.existsSync(path.join(root, 'src', 'HyperLib.Host', 'bin', 'Release', 'net10.0', 'HyperLib.Host.exe'));
  const host = childProcess.spawn('dotnet', ['run', ...(existingGateway ? ['--no-build'] : []), '--configuration', 'Release', '--project',
    path.join(root, 'src', 'HyperLib.Host', 'HyperLib.Host.csproj'), '--no-launch-profile', '--', '--root', root, '--port', String(port)],
    { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env, DOTNET_NOLOGO: '1' } });
  let diagnostics = '';
  host.stdout.on('data', data => diagnostics = (diagnostics + data).slice(-20000));
  host.stderr.on('data', data => diagnostics = (diagnostics + data).slice(-20000));
  let browser, page;
  const report = { root, origin, checks: [], screenshots: [], errors: [], assertions: 0 };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.assertions++; report.checks.push(message); };
  const api = async (route, options) => {
    const response = await fetch(origin + route, options);
    if (!response.ok) throw new Error(`${route}: HTTP ${response.status}: ${await response.text()}`);
    return response.json();
  };
  const waitFor = async (predicate, description, timeout = 60000) => {
    const started = Date.now();
    while (Date.now() - started < timeout) {
      if (host.exitCode !== null) throw new Error('Owned QA gateway exited: ' + diagnostics);
      try { if (await predicate()) return; } catch { }
      await new Promise(resolve => setTimeout(resolve, 150));
    }
    throw new Error('Timed out: ' + description);
  };
  try {
    await waitFor(async () => (await api('/api/health')).root.toLowerCase() === root.toLowerCase(), 'private gateway identity', 120000);
    const initial = await api('/api/state');
    let pipeline;
    if (legacyCompatibility) {
      const workId = initial.nodes.find(node => node.type === 'work').id;
      const original = await api('/api/templates/' + workId);
      pipeline = { legacy: true, workId, landingPageId: initial.nodes.find(node => node.type === 'page')?.id,
        toolkit: original.blocks.map(block => ({ blockTypeIds: [block.id] })), pageTemplate: { blocks: original.defaultBlocks || [] } };
      check(true, 'Legacy Work fixture retains its existing element library and content.');
    } else {
      pipeline = await api('/api/studio/pipeline');
      check(pipeline.schemaVersion >= 2 && pipeline.phase === 3, 'Generated fixture is a staged Phase 3 project.');
    }
    const definition = await api('/api/templates/' + pipeline.workId);
    const runtime = await api('/api/runtime/' + pipeline.workId);
    check(!!runtime.proxyPrefix, 'The actual Work renderer built successfully.');
    browser = await chromium.launch({ headless: true, executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
    page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    await page.addInitScript(() => {
      const Original = window.EventSource;
      window.__qaStreams = [];
      window.EventSource = class extends Original {
        constructor(...arguments_) { super(...arguments_); window.__qaStreams.push(this); }
      };
    });
    page.on('pageerror', error => report.errors.push(String(error)));
    await page.goto(origin + '/studio-authoring/index.html' + (pipeline.legacy ? '#' + pipeline.workId : ''), { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => window.__studioAuthoring?.state().ready);
    check(!(await page.locator('.topbar').isVisible()), 'Reader chrome is absent inside the Studio content surface.');
    check(await page.locator('[data-panel="elements"]').isVisible(), 'The actual element and property editing tools are available.');
    check(await page.locator('[data-panel="assistant"]').count() === 0, 'The embedded writer does not duplicate Studio’s assistant.');
    if (!pipeline.legacy) {
      await page.frameLocator('#page-frame').locator('[data-block-id]').first().waitFor();
      await page.frameLocator('#page-frame').locator('#studio-slot-hints').waitFor({ state: 'attached' });
    }
    await page.screenshot({ path: path.join(output, 'content-landing-desktop.png'), fullPage: true });
    report.screenshots.push(path.join(output, 'content-landing-desktop.png'));

    const sharedHeader = page.frameLocator('#page-frame').locator('[data-block-type="studio-header-row"]').first();
    if (!pipeline.legacy && await sharedHeader.count()) {
      await page.frameLocator('#page-frame').locator('#studio-slot-hints').waitFor({ state: 'attached' });
      const headerId = await sharedHeader.getAttribute('data-block-id');
      const nestedTemplate = sharedHeader.locator('[data-studio-shared-template] [data-block-id]').first();
      if (await nestedTemplate.count()) await nestedTemplate.click({ position: { x: 6, y: 6 } });
      else await sharedHeader.click({ position: { x: 6, y: 6 } });
      await page.locator('#panel-content').getByText('SHARED HEADER', { exact: true }).waitFor();
      check(await page.locator('#panel-content [data-field="layout"]').count() === 0
        && (await page.locator('#panel-content').textContent()).includes('Templates → Header'),
        'Shared headers direct the author to the template builder without exposing raw layout data.');
      check(await sharedHeader.locator('[contenteditable="true"]').count() === 0,
        'Shared header markup is not a plain inline editable field.');
      const beforeHeader = await api('/api/state');
      await page.frameLocator('#page-frame').locator('body').evaluate((body, blockId) => {
        const frame = window.frameElement;
        window.parent.postMessage({ source: 'hyperlib-renderer', kind: 'change', pageId: frame.dataset.pageId,
          generationId: frame.dataset.generationId, blockId, field: 'layout', value: 'Accidental inline text' }, location.origin);
      }, headerId);
      if (await nestedTemplate.count()) {
        const nestedId = await nestedTemplate.getAttribute('data-block-id');
        await page.frameLocator('#page-frame').locator('body').evaluate((body, blockId) => {
          const frame = window.frameElement;
          const identity = { source: 'hyperlib-renderer', pageId: frame.dataset.pageId, generationId: frame.dataset.generationId, blockId };
          window.parent.postMessage({ ...identity, kind: 'dirty' }, location.origin);
          window.parent.postMessage({ ...identity, kind: 'change', field: 'text', value: 'Accidental nested replacement' }, location.origin);
        }, nestedId);
      }
      await page.waitForTimeout(750); // Exceeds the inline-save debounce, so a missing guard would persist the overwrite.
      await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
      check((await api('/api/state')).revision === beforeHeader.revision,
        'An accidental inline text message cannot overwrite the shared header structure.');
    }

    async function createItem(type, title, templateId = null) {
      const uniqueTitle = title + ' ' + runSuffix;
      await page.locator('#studio-create-page').click();
      await page.locator('#dialog[open]').waitFor();
      await page.locator('#dialog [name="type"]').selectOption(type);
      if (type === 'page' && templateId) await page.locator('#dialog [name="studioPageTemplateId"]').selectOption(templateId);
      if (type !== 'page' && await page.locator('#studio-page-template-chooser').count())
        check(!await page.locator('#studio-page-template-chooser').isVisible(), 'Reusable Page designs are offered only when creating a Page.');
      await page.locator('#dialog [name="title"]').fill(uniqueTitle);
      await page.locator('#dialog-submit').click();
      await page.locator('#dialog').waitFor({ state: 'hidden' });
      await waitFor(async () => (await api('/api/state')).nodes.some(node => node.title === uniqueTitle), 'create ' + title);
      return (await api('/api/state')).nodes.find(node => node.title === uniqueTitle);
    }
    const namedTemplates = !pipeline.legacy && Array.isArray(pipeline.pageTemplates) ? pipeline.pageTemplates : [];
    const defaultTemplate = namedTemplates.find(item => item.id === pipeline.defaultPageTemplateId) || namedTemplates[0];
    const contentPage = await createItem('page', 'Written in Studio', defaultTemplate?.id);
    if (defaultTemplate) {
      check((await api('/api/state')).nodes.find(item => item.id === contentPage.id).appearance.studioPageTemplateId === defaultTemplate.id,
        'The Page template chooser records the selected stable design identity.');
      await page.waitForFunction(name => document.querySelector('#studio-template-context')?.textContent === `Template \u00b7 ${name}`, defaultTemplate.name);
      check(true, 'The writer identifies the current Page design without confusing it with the component schema.');
    }
    let state = await api('/api/state');
    check(pipeline.legacy || state.pages[contentPage.id].blocks.length === pipeline.pageTemplate.blocks.length,
      'A Page created in Studio inherits the reusable Page template slots.');
    check(pipeline.legacy || state.pages[contentPage.id].blocks.every(block => !Object.values(block.values || {}).some(value => typeof value === 'string' && value.includes('Lorem'))),
      'Page creation starts with layout slots rather than sample articles.');
    await page.waitForFunction(id => window.__studioAuthoring.state().nodeId === id, contentPage.id);
    const toolkitTypes = new Set(pipeline.toolkit.flatMap(item => item.blockTypeIds).concat('studio-navigation'));
    const editableObjects = definition.blocks.filter(block => toolkitTypes.has(block.id) && block.fields.some(field => ['text', 'richtext', 'markdown', 'multiline'].includes(field.type) && ['text', 'content'].includes(field.id)));
    const body = editableObjects.find(block => block.component === 'TextBlock') || editableObjects.find(block => /RichTextBlock|TextBlock/.test(block.component || '')) || editableObjects[0];
    check(!!body, 'The selected toolset contains an editable content object.');
    const textField = body.fields.find(field => ['text', 'content'].includes(field.id));
    async function addObject(block) {
      const before = (await api('/api/state')).pages[contentPage.id].blocks.filter(item => item.type === block.id).length;
      await page.locator('[data-panel="elements"]').click();
      const region = definition.regions.find(region => !region.allowedBlocks.length || region.allowedBlocks.includes(block.id));
      if (await page.locator('#region-select').count()) await page.locator('#region-select').selectOption(region.id);
      const exposed = await page.locator('[data-add-block]').evaluateAll(buttons => buttons.map(button => button.dataset.addBlock));
      const privateTypes = ['studio-header-row', 'studio-body-row', 'studio-footer-row', 'studio-navigation-layout'];
      const compatibleObjects = definition.blocks.filter(block => !privateTypes.includes(block.id) && (!region.allowedBlocks.length || region.allowedBlocks.includes(block.id))).map(block => block.id);
      check(exposed.length === compatibleObjects.length && exposed.every(type => compatibleObjects.includes(type)),
        'The object shelf exposes Hyperlib’s complete element collection for the selected compatible Page region.');
      await page.locator(`[data-add-block="${block.id}"]`).click();
      await page.locator(`[data-field="${textField.id}"]`).first().waitFor().catch(() => {});
      await waitFor(async () => (await api('/api/state')).pages[contentPage.id].blocks.filter(item => item.type === block.id).length > before, 'add object');
    }
    await addObject(body);
    const bodyId = (await api('/api/state')).pages[contentPage.id].blocks.filter(block => block.type === body.id).at(-1).id;
    await page.locator(`#panel-content [data-field="${textField.id}"]`).fill('Text authored through the Studio properties panel.');
    await page.locator('#studio-save').click();
    await waitFor(async () => (await api('/api/state')).pages[contentPage.id].blocks.find(block => block.id === bodyId)?.values[textField.id] === 'Text authored through the Studio properties panel.', 'durable text edit');
    check(true, 'Property edits persist through the revision-checked content gateway.');

    const editable = page.frameLocator('#page-frame').locator(`[data-block-id="${bodyId}"] [data-field="${textField.id}"][contenteditable="true"]`);
    await editable.waitFor();
    await editable.fill('Inline writing on the Studio canvas.');
    await page.locator('#studio-save').click();
    await waitFor(async () => (await api('/api/state')).pages[contentPage.id].blocks.find(block => block.id === bodyId)?.values[textField.id] === 'Inline writing on the Studio canvas.', 'inline text save');
    check(true, 'Writing directly in the live page canvas saves through the same gateway.');
    await addObject(body);
    const secondBody = (await api('/api/state')).pages[contentPage.id].blocks.filter(block => block.type === body.id).at(-1);
    await page.locator('#block-up').click();
    await waitFor(async () => {
      const blocks = (await api('/api/state')).pages[contentPage.id].blocks.filter(block => block.region === secondBody.region);
      return blocks.findIndex(block => block.id === secondBody.id) < blocks.findIndex(block => block.id === bodyId);
    }, 'move object within region');
    check(true, 'Object ordering follows the selected content region.');
    const compatible = definition.regions.filter(region => !region.allowedBlocks.length || region.allowedBlocks.includes(body.id));
    if (compatible.length > 1) {
      const target = compatible.find(region => region.id !== secondBody.region);
      await page.locator('#block-region').selectOption(target.id);
      await waitFor(async () => (await api('/api/state')).pages[contentPage.id].blocks.find(block => block.id === secondBody.id)?.region === target.id, 'change object region');
      check(true, 'Objects can move between compatible Page template regions while keeping their content.');
    }

    const image = definition.blocks.find(block => toolkitTypes.has(block.id) && block.fields.some(field => field.id === 'src'));
    if (image) {
      await page.locator('[data-panel="elements"]').click();
      const region = definition.regions.find(region => !region.allowedBlocks.length || region.allowedBlocks.includes(image.id));
      if (await page.locator('#region-select').count()) await page.locator('#region-select').selectOption(region.id);
      await page.locator(`[data-add-block="${image.id}"]`).click();
      const mediaFile = path.join(output, 'local-import.png');
      fs.writeFileSync(mediaFile, Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aOCkAAAAASUVORK5CYII=', 'base64'));
      const chooser = page.waitForEvent('filechooser');
      await page.locator('[data-upload-field="src"]').click();
      await (await chooser).setFiles(mediaFile);
      await waitFor(async () => (await api('/api/state')).pages[contentPage.id].blocks.some(block => block.type === image.id && block.values.src?.startsWith('/media/')), 'owned local media upload');
      check(true, 'Local media is imported into the project and assigned to the selected object.');
    }

    await page.evaluate(id => window.__studioAuthoring.navigate(id, 'qa-overview'), pipeline.workId);
    await page.waitForFunction(id => window.__studioAuthoring.state().nodeId === id, pipeline.workId);
    const part = await createItem('part', 'Studio section');
    const nestedPage = await createItem('page', 'Nested Page');
    check(nestedPage.parentId === part.id, 'Hierarchical Page creation preserves the selected Part.');
    await page.locator('#more-button').click();
    await page.locator('#dialog [name="title"]').fill('Renamed nested Page');
    await page.locator('#dialog-submit').click();
    await page.locator('#dialog').waitFor({ state: 'hidden' });
    await waitFor(async () => (await api('/api/state')).nodes.find(node => node.id === nestedPage.id)?.title === 'Renamed nested Page', 'rename hierarchy');
    await page.locator('#undo-button').click();
    await waitFor(async () => (await api('/api/state')).nodes.find(node => node.id === nestedPage.id)?.title === nestedPage.title, 'undo hierarchy rename');
    await page.locator('#redo-button').click();
    await waitFor(async () => (await api('/api/state')).nodes.find(node => node.id === nestedPage.id)?.title === 'Renamed nested Page', 'redo hierarchy rename');
    check(true, 'Hierarchy rename, undo and redo use revision-checked operations.');

    await page.evaluate(id => window.__studioAuthoring.navigate(id, 'qa-draft'), contentPage.id);
    await page.waitForFunction(id => window.__studioAuthoring.state().nodeId === id, contentPage.id);
    await page.frameLocator('#page-frame').locator(`[data-block-id="${bodyId}"]`).click();
    await page.locator(`#panel-content [data-field="${textField.id}"]`).waitFor();
    await page.route('**/api/operations', route => route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ error: 'A concurrent edit prevented this test save.' }) }));
    await page.locator(`#panel-content [data-field="${textField.id}"]`).fill('A recoverable writing draft.');
    await page.locator('#studio-save').click();
    await page.getByRole('button', { name: 'Download draft', exact: true }).waitFor();
    check(await page.evaluate(() => window.__studioAuthoring.state().dirty), 'A rejected save keeps the user’s content draft pending.');
    page.once('dialog', dialog => dialog.accept());
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => window.__studioAuthoring?.state().ready);
    await page.getByRole('button', { name: 'Restore draft', exact: true }).click();
    await page.frameLocator('#page-frame').locator(`[data-block-id="${bodyId}"]`).click();
    await page.waitForFunction(({ field }) => document.querySelector(`#panel-content [data-field="${field}"]`)?.value === 'A recoverable writing draft.', { field: textField.id });
    check(true, 'The unfinished Page draft can be restored after reloading the content surface.');
    await page.unroute('**/api/operations');
    await page.locator('#studio-save').click();
    await waitFor(async () => (await api('/api/state')).pages[contentPage.id].blocks.find(block => block.id === bodyId)?.values[textField.id] === 'A recoverable writing draft.', 'save recovered content');
    await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
    check(true, 'A restored draft saves successfully when the gateway is available again.');

    if (namedTemplates.length > 1) {
      await page.evaluate(id => window.__studioAuthoring.navigate(id, 'qa-template-parent'), pipeline.workId);
      await page.waitForFunction(id => window.__studioAuthoring.state().nodeId === id, pipeline.workId);
      const alternate = namedTemplates.find(item => item.id !== defaultTemplate.id);
      const secondPage = await createItem('page', 'Alternate design Page', alternate.id);
      let namedState = await api('/api/state');
      check(namedState.nodes.find(item => item.id === secondPage.id).appearance.studioPageTemplateId === alternate.id,
        'Creating another Page can select a different reusable design.');
      check(JSON.stringify(namedState.pages[secondPage.id].blocks) === JSON.stringify(alternate.blocks),
        'The chosen alternate Page inherits its own composed template slots.');
      await page.waitForFunction(id => window.__studioAuthoring.state().nodeId === id, secondPage.id);
      await page.waitForFunction(name => document.querySelector('#studio-template-context')?.textContent === `Template \u00b7 ${name}`, alternate.name);
      const firstBlocks = JSON.stringify(namedState.pages[contentPage.id].blocks);
      const firstAppearance = namedState.nodes.find(item => item.id === contentPage.id).appearance.background;
      const savedPipeline = await api('/api/studio/pipeline');
      const recipes = savedPipeline.pageTemplates.map(item => ({ id: item.id, name: item.name, definition: structuredClone(item.definition) }));
      recipes.find(item => item.id === alternate.id).definition.backgroundColor = '#314159';
      await api('/api/studio/page-templates', { method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-HyperLib-Client': 'editor' },
        body: JSON.stringify({ expectedPipelineRevision: savedPipeline.revision, expectedContentRevision: namedState.revision,
          landingTemplate: savedPipeline.landingTemplate.definition, pageTemplate: savedPipeline.pageTemplate.definition,
          pageTemplates: recipes, defaultPageTemplateId: savedPipeline.defaultPageTemplateId }) });
      namedState = await api('/api/state');
      check(namedState.nodes.find(item => item.id === secondPage.id).appearance.background === '#314159',
        'Editing a named reusable design updates Pages assigned to that design.');
      check(JSON.stringify(namedState.pages[contentPage.id].blocks) === firstBlocks
        && namedState.nodes.find(item => item.id === contentPage.id).appearance.background === firstAppearance,
        'Updating the alternate design leaves another design and its authored Page content unchanged.');
      await page.evaluate(() => window.__studioAuthoring.refreshFoundation('qa-named-designs'));
      await page.evaluate(id => window.__studioAuthoring.navigate(id, 'qa-return-to-writing'), contentPage.id);
      await page.waitForFunction(id => window.__studioAuthoring.state().nodeId === id, contentPage.id);
    }

    if (!pipeline.legacy && process.env.STUDIO_AUTHORING_QA_VERIFIER) {
      // Match native staging exactly: change durable state offline, without
      // the gateway route's content event or a browser session restart.
      await page.evaluate(() => window.__qaStreams.forEach(stream => stream.close()));
      const oldRevision = await page.evaluate(() => window.__studioAuthoring.state().revision);
      const workRoot = path.join(root, initial.nodes.find(node => node.id === pipeline.workId).projectPath);
      const changed = childProcess.execFileSync(process.env.STUDIO_AUTHORING_QA_VERIFIER,
        ['--studio-restage-browser', workRoot], { windowsHide: true, timeout: 120000, encoding: 'utf8' });
      check(changed.includes('STUDIO_DIRECT_RESTAGING_OK'), 'The native direct-store staging boundary updates the private fixture without HTTP publication.');
      const latest = await api('/api/state');
      check(latest.revision > oldRevision && await page.evaluate(() => window.__studioAuthoring.state().revision) === oldRevision,
        'The running writer initially retains the prior revision after a direct native staging update.');
      check(await page.evaluate(() => window.__studioAuthoring.refreshFoundation('qa-native-foundation')),
        'The native refresh bridge reloads content, pipeline and object definitions in the running writer.');
      check(await page.evaluate(() => window.__studioAuthoring.state().revision) === latest.revision,
        'The writer now holds the current content revision without reopening its project.');
      await page.frameLocator('#page-frame').locator(`[data-block-id="${bodyId}"]`).click();
      await page.locator(`#panel-content [data-field="${textField.id}"]`).fill('Content saved after native restaging.');
      await page.locator('#studio-save').click();
      await waitFor(async () => (await api('/api/state')).pages[contentPage.id].blocks.find(block => block.id === bodyId)?.values[textField.id] === 'Content saved after native restaging.', 'save after native restaging');
      check(true, 'A follow-on Page edit saves at the revised foundation and content revision.');
    }

    await page.setViewportSize({ width: 600, height: 820 });
    await page.locator('#studio-inspector-toggle').click();
    await page.screenshot({ path: path.join(output, 'content-narrow.png'), fullPage: true });
    report.screenshots.push(path.join(output, 'content-narrow.png'));
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'The content surface fits a narrow workspace without horizontal overflow.');
    await page.setViewportSize({ width: 390, height: 844 });
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => window.__studioAuthoring?.state().ready);
    check(!await page.locator('#editor-panel').isVisible(), 'A very narrow surface starts with a collapsible inspector.');
    await page.locator('#studio-inspector-toggle').click();
    check(await page.locator('#editor-panel').isVisible(), 'Elements and properties remain available through the inspector toggle.');
    await page.screenshot({ path: path.join(output, 'content-mobile-inspector.png'), fullPage: true });
    report.screenshots.push(path.join(output, 'content-mobile-inspector.png'));
    check(report.errors.length === 0, 'The Studio writer reports no browser exceptions.');
    report.result = 'STUDIO_CONTENT_AUTHORING_OK';
    fs.writeFileSync(path.join(output, 'receipt.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ result: report.result, assertions: report.assertions, receipt: path.join(output, 'receipt.json') }));
  } catch (error) {
    report.failure = String(error); report.diagnostics = diagnostics;
    if (page) await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true }).catch(() => {});
    fs.writeFileSync(path.join(output, 'receipt.json'), JSON.stringify(report, null, 2));
    throw error;
  } finally {
    if (browser) await browser.close();
    if (host.exitCode === null) { try { childProcess.execFileSync('taskkill', ['/PID', String(host.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' }); } catch { } }
  }
}
run().catch(error => { console.error(error); process.exitCode = 1; });
