/* The actual copied Hyperlib editor and Work renderer, on one disposable private project. */
const fs = require('fs'), path = require('path'), net = require('net');
const { spawn, spawnSync } = require('child_process');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const [rootArgument, outputArgument, assetsArgument] = process.argv.slice(2);
  if (!rootArgument || !outputArgument || !assetsArgument)
    throw new Error('Usage: node verify-studio-direct-editor.cjs PRIVATE_QA_LIBRARY OUTPUT STUDIO_ASSETS');
  const root = path.resolve(rootArgument), output = path.resolve(outputArgument), assets = path.resolve(assetsArgument);
  if (!root.toLowerCase().includes('qa') || !fs.existsSync(path.join(root, '.studio', 'hyperlib-work.json')))
    throw new Error('This workflow edits only a generated disposable QA project.');
  fs.mkdirSync(output, { recursive: true });
  const web = path.join(root, 'src', 'HyperLib.Host', 'wwwroot', 'studio-authoring'); fs.mkdirSync(web, { recursive: true });
  for (const file of ['index.html', 'app.js', 'element-editor.js', 'app.css', 'studio.css']) fs.copyFileSync(path.join(assets, file), path.join(web, file));
  const port = await new Promise((resolve, reject) => {
    const server = net.createServer(); server.once('error', reject);
    server.listen(0, '127.0.0.1', () => { const value = server.address().port; server.close(() => resolve(value)); });
  });
  const origin = 'http://127.0.0.1:' + port;
  const host = spawn('dotnet', ['run', '--configuration', 'Release', '--project', path.join(root, 'src', 'HyperLib.Host', 'HyperLib.Host.csproj'),
    '--no-launch-profile', '--', '--root', root, '--port', String(port)],
  { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env, DOTNET_PROCESSOR_COUNT: '2', DOTNET_NOLOGO: '1' } });
  let logs = '', browser, page;
  host.stdout.on('data', data => logs = (logs + data).slice(-30000)); host.stderr.on('data', data => logs = (logs + data).slice(-30000));
  const report = { result: 'STUDIO_DIRECT_EDITOR_OK', root, checks: [], screenshots: [], errors: [] };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.checks.push(message); };
  const api = async route => { const response = await fetch(origin + route); if (!response.ok) throw new Error(route + ': ' + response.status + ' ' + await response.text()); return response.json(); };
  const wait = async (predicate, description, timeout = 90000) => {
    const end = Date.now() + timeout;
    while (Date.now() < end) {
      if (host.exitCode !== null) throw new Error('Owned QA gateway exited: ' + logs);
      try { if (await predicate()) return; } catch { }
      await new Promise(resolve => setTimeout(resolve, 120));
    }
    throw new Error('Timed out: ' + description);
  };
  try {
    await wait(async () => (await api('/api/health')).root.toLowerCase() === root.toLowerCase(), 'owned gateway', 120000);
    const pipeline = await api('/api/studio/pipeline'), initial = await api('/api/state');
    const definition = await api('/api/templates/' + pipeline.workId), runtime = await api('/api/runtime/' + pipeline.workId);
    check(!!runtime.proxyPrefix, 'The actual private Work renderer builds for the direct editor.');
    const geometryFields = ['studioPositioned', 'studioX', 'studioY', 'studioWidth', 'studioHeight'];
    const textType = definition.blocks.find(block => block.id === 'text') || definition.blocks.find(block => block.component === 'TextBlock');
    const textField = textType?.fields.find(field => ['text', 'content'].includes(field.id));
    check(!!textType && !!textField && geometryFields.every(id => textType.fields.some(field => field.id === id)),
      'The original text tool has the declared, validated canvas geometry fields.');
    browser = await chromium.launch({ headless: true, executablePath: 'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
    page = await browser.newPage({ viewport: { width: 1500, height: 1000 } });
    page.on('pageerror', error => report.errors.push(error.message));
    await page.addInitScript(() => {
      window.__directFlushResults = [];
      window.addEventListener('studio:state', event => window.__directLastState = event.detail);
      window.addEventListener('studio:flush-result', event => {
        window.__directFlushResults.push(event.detail);
        window.__directLastFlushError = event.detail.error || null;
      });
      window.addEventListener('message', event => {
        const frame = document.querySelector('#page-frame'), message = event.data;
        if (frame && event.source === frame.contentWindow && event.origin === location.origin
          && message?.source === 'hyperlib-renderer' && message.kind === 'ready') {
          window.__directReadyDocument = frame.contentDocument;
          window.__directReadyIdentity = { pageId: message.pageId, generationId: message.generationId };
        }
      });
    });
    const requests = [];
    page.on('request', request => { if (request.url() === origin + '/api/operations' && request.method() === 'POST') requests.push(request.postDataJSON()); });
    await page.goto(origin + '/studio-authoring/index.html', { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => window.__studioAuthoring?.state().ready);
    const frame = page.frameLocator('#page-frame');
    await frame.locator('#studio-canvas-tools').waitFor({ state: 'attached' });
    check(await page.locator('[data-panel="templates"]').isVisible() && await page.locator('[data-panel="elements"]').isVisible()
      && await page.locator('[data-left-panel="contents"]').isVisible() && await page.locator('[data-left-panel="inspector"]').isVisible()
      && await page.locator('[data-panel="assistant"]').count() === 0, 'The sidebar exposes Template and Elements on the right and Contents and Properties on the left, with Assistant omitted.');
    const region = definition.regions.find(item => !item.allowedBlocks.length || item.allowedBlocks.includes(textType.id));
    async function selectRegion(type) {
      await page.locator('[data-panel="elements"]').click();
      for (const selector of ['#element-category', '#element-type']) {
        const filter = page.locator(selector);
        if (await filter.count()) {
          const all = await filter.locator('option').evaluateAll(items => items.find(item => item.value === 'all')?.value);
          if (all !== undefined) await filter.selectOption(all);
        }
      }
      if (await page.locator('#element-search').count()) await page.locator('#element-search').fill('');
      const selected = definition.regions.find(item => !item.allowedBlocks.length || item.allowedBlocks.includes(type.id));
      if (await page.locator('#region-select').count()) await page.locator('#region-select').selectOption(selected.id);
      return selected;
    }
    await selectRegion(textType);
    const exposed = await page.locator('[data-add-block]').evaluateAll(items => items.map(item => item.dataset.addBlock));
    const privateTypes = ['studio-header-row', 'studio-body-row', 'studio-footer-row', 'studio-navigation-layout'];
    const expected = definition.blocks.filter(item => !privateTypes.includes(item.id) && (!region.allowedBlocks.length || region.allowedBlocks.includes(item.id))).map(item => item.id);
    check(exposed.length === expected.length && expected.every(id => exposed.includes(id)), 'Elements exposes the full original compatible collection, including objects outside the old wizard toolset.');
    check(exposed.every(id => !privateTypes.includes(id)), 'Private serialized-layout protocol rows are absent from the element palette, while ordinary Header, Footer and Work navigation remain available.');
    check(await page.locator('#element-search').isVisible() && await page.locator('#element-type').isVisible()
      && (await page.locator('#element-category option[value="all"]').textContent()).trim() === 'Type', 'The Elements panel retains its searchable palette and provides the requested Type filter.');
    await page.locator('#element-search').fill(textType.title);
    check(await page.locator('[data-add-block="' + textType.id + '"]').isVisible(), 'The original element search narrows the collection without moving the canvas.');
    const pageId = await page.evaluate(() => window.__studioAuthoring.state().nodeId);
    const blocks = async () => (await api('/api/state')).pages[pageId].blocks;
    async function add(type) {
      const previousBlocks = await blocks(), before = previousBlocks.map(block => block.id); await selectRegion(type);
      await page.locator('[data-add-block="' + type.id + '"]').click();
      await page.locator('#library-add').click();
      await wait(async () => (await blocks()).some(block => block.type === type.id && !before.includes(block.id)), 'add ' + type.title);
      const added = (await blocks()).find(block => block.type === type.id && !before.includes(block.id));
      await page.waitForFunction(({ pageId, blockId }) => {
        const current = window.__studioAuthoring.state(), canvas = document.querySelector('#page-frame');
        return current.nodeId === pageId && !current.libraryBusy && !current.libraryMutationPending
          && current.currentGenerationReady && !current.frameLoading && !canvas.inert
          && !document.querySelector('#main').inert && !document.querySelector('#properties-content').inert
          && current.frameReadyPageId === pageId && current.frameReadyGenerationId === canvas.dataset.generationId
          && !!canvas.contentDocument?.querySelector('[data-block-id="' + CSS.escape(blockId) + '"]');
      }, { pageId, blockId: added.id }, { timeout: 90000 });
      const occupied = previousBlocks.filter(block => block.region === added.region && block.values.studioPositioned === true);
      if (occupied.length) {
        const addedElement = frame.locator('[data-block-id="' + added.id + '"]'); await addedElement.waitFor();
        const addedBounds = await addedElement.boundingBox();
        const existingBounds = await Promise.all(occupied.map(block => frame.locator('[data-block-id="' + block.id + '"]').boundingBox()));
        check(added.values.studioPositioned === true && addedBounds && existingBounds.every(bounds => !bounds || addedBounds.y >= bounds.y + bounds.height + 7),
          'Adding ' + type.title + ' begins below existing positioned elements, leaving them visible and selectable.');
      }
      return added;
    }
    const text = await add(textType), target = frame.locator('[data-block-id="' + text.id + '"]');
    await page.locator('#properties-content [data-field="' + textField.id + '"]').fill('Created directly in Hyperpage Studio.');
    await page.locator('#studio-save').click();
    await wait(async () => (await blocks()).find(block => block.id === text.id).values[textField.id] === 'Created directly in Hyperpage Studio.', 'properties save');
    const inline = target.locator('[data-field="' + textField.id + '"][contenteditable="true"]').first();
    await inline.waitFor(); await inline.fill('Inline content remains independent of its page foundation.');
    await page.locator('#studio-save').click();
    await wait(async () => (await blocks()).find(block => block.id === text.id).values[textField.id] === 'Inline content remains independent of its page foundation.', 'inline save');
    check(true, 'Properties and in-line writing use the actual Hyperlib renderer and durable revision-checked gateway.');
    check(await page.locator('#properties-content [data-field="' + textField.id + '"]').inputValue() === 'Inline content remains independent of its page foundation.',
      'The selected Properties field stays synchronized with text written directly on the canvas.');
    check(await page.locator('#properties-content [data-field="studioX"]').count() === 0, 'Canvas geometry does not add raw technical fields to the relocated Properties panel.');
    async function handles(id = text.id) {
      await frame.locator('[data-block-id="' + id + '"]').click({ position: { x: 6, y: 6 } });
      // Canvas clicks now select the actual nested element under the pointer.
      // These original assertions exercise the saved outer block's geometry,
      // so explicitly choose that block through the same ancestry control as a user.
      const selected = await page.evaluate(() => window.__studioAuthoring.state().selectedElement);
      if (selected?.path) await page.locator('#properties-content button[data-element-path=""]').click();
      await page.waitForFunction(id => {
        const selection = window.__studioAuthoring.state().selectedElement;
        return selection?.blockId === id && selection.path === '';
      }, id);
      const overlay = frame.locator('[data-studio-canvas-overlay][data-studio-canvas-block-id="' + id + '"]');
      await overlay.waitFor();
      await wait(() => overlay.evaluate((element, id) => {
        const block = [...element.ownerDocument.querySelectorAll('[data-block-id]')].find(block => block.dataset.blockId === id);
        if (!block || element.hidden) return false;
        const a = block.getBoundingClientRect(), b = element.getBoundingClientRect();
        return Math.abs(a.left - b.left) <= 2 && Math.abs(a.top - b.top) <= 2
          && Math.abs(a.width - b.width) <= 2 && Math.abs(a.height - b.height) <= 2;
      }, id), 'actual outer-block handles');
      return overlay;
    }
    async function gesture(handle, dx, dy, cancel = false, expectFailure = false, id = text.id, during = null) {
      const overlay = await handles(id), button = overlay.locator('[data-studio-handle="' + handle + '"]');
      await button.scrollIntoViewIfNeeded(); const bounds = await button.boundingBox();
      await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
      const before = requests.length, old = structuredClone((await blocks()).find(block => block.id === id).values);
      const beforeState = await page.evaluate(() => window.__studioAuthoring.state());
      await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2); await page.mouse.down();
      await page.mouse.move(bounds.x + bounds.width / 2 + dx, bounds.y + bounds.height / 2 + dy, { steps: 8 });
      check(requests.length === before, 'Pointer movement keeps ' + handle + ' changes in one recoverable draft until the gesture ends.');
      if (during) await during();
      if (cancel) await page.keyboard.press('Escape'); await page.mouse.up();
      if (cancel) {
        await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
        const afterValues = (await blocks()).find(block => block.id === id).values;
        const afterState = await page.evaluate(() => window.__studioAuthoring.state());
        (report.cancellationDiagnostics ||= []).push({ blockId: id, handle, dx, dy, requestsBefore: before, requestsAfter: requests.length,
          oldValues: old, newValues: afterValues, requestsDuringCancellation: requests.slice(before), beforeState, afterState,
          actualTarget: await frame.locator('[data-block-id="' + id + '"]').evaluate(element => ({ style: element.getAttribute('style'),
            positioned: element.classList.contains('studio-canvas-positioned'), sized: element.getAttribute('data-studio-sized'),
            rectangle: element.getBoundingClientRect().toJSON() })) });
        check(requests.length === before && JSON.stringify(afterValues) === JSON.stringify(old), 'Escape cancels the gesture without an operation or a changed saved layout.');
        return old;
      }
      if (expectFailure) {
        await page.getByRole('button', { name: 'Download draft', exact: true }).waitFor();
        return old;
      }
      await wait(async () => requests.length > before && (await blocks()).find(block => block.id === id).values.studioPositioned === true, 'geometry save');
      await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
      check(requests.length === before + 1, 'The entire ' + handle + ' gesture saves as exactly one Page operation.');
      return (await blocks()).find(block => block.id === id).values;
    }
    const beforeRect = await target.boundingBox();
    check(beforeRect.height > 10 && beforeRect.height < 120, 'An unpositioned text element keeps its natural content height rather than stretching across the empty canvas.');
    const legibility = await target.evaluate(element => {
      const paper = getComputedStyle(element.closest('.hl-document')), ink = getComputedStyle(element.querySelector('[data-field]') || element);
      const luminance = css => {
        const channels = (css.match(/[\d.]+/g) || []).slice(0, 3).map(Number).map(value => {
          const normalized = value / 255; return normalized <= .04045 ? normalized / 12.92 : ((normalized + .055) / 1.055) ** 2.4;
        });
        return channels[0] * .2126 + channels[1] * .7152 + channels[2] * .0722;
      };
      const foreground = luminance(ink.color), background = luminance(paper.backgroundColor);
      return { foreground: ink.color, background: paper.backgroundColor, contrast: (Math.max(foreground, background) + .05) / (Math.min(foreground, background) + .05) };
    });
    check(legibility.contrast >= 4.5, 'Native text remains legible against the actual canvas background (contrast ' + legibility.contrast.toFixed(2) + ':1).');
    const resized = await gesture('se', -Math.min(150, beforeRect.width * .3), 40);
    check(resized.studioWidth < 99 && resized.studioHeight > beforeRect.height + 25, 'Dragging a corner persists an independently smaller width and larger height.');
    const moved = await gesture('move', 45, 35);
    check(moved.studioX > 0 && moved.studioY > resized.studioY + 25 && moved[textField.id] === resized[textField.id], 'Dragging the move handle positions the element freely and preserves its content.');
    await page.locator('#undo-button').click();
    await wait(async () => (await blocks()).find(block => block.id === text.id).values.studioY === resized.studioY, 'undo whole drag');
    await page.locator('#redo-button').click();
    await wait(async () => (await blocks()).find(block => block.id === text.id).values.studioY === moved.studioY, 'redo whole drag');
    check(true, 'Undo and Redo restore each complete direct manipulation as one action.');
    await gesture('move', 15, 17, true);
    await page.reload({ waitUntil: 'domcontentloaded' }); await page.waitForFunction(() => window.__studioAuthoring?.state().ready);
    await frame.locator('#studio-canvas-tools').waitFor({ state: 'attached' });
    const metrics = await target.evaluate(element => {
      const region = element.closest('.hl-region'), rect = element.getBoundingClientRect(), parent = region.getBoundingClientRect();
      return { x: (rect.left - parent.left - region.clientLeft) / region.clientWidth * 100, y: rect.top - parent.top - region.clientTop,
        width: rect.width / region.clientWidth * 100, height: rect.height, text: element.textContent };
    });
    check(Math.abs(metrics.x - moved.studioX) < .2 && Math.abs(metrics.y - moved.studioY) < 2
      && Math.abs(metrics.width - moved.studioWidth) < .2 && Math.abs(metrics.height - moved.studioHeight) < 2,
    'Reloaded renderer geometry matches the saved coordinates and dimensions.');
    check(metrics.text.includes(moved[textField.id]), 'Reloading restores independent inline content alongside direct geometry.');
    const overlay = await handles(), moveHandle = overlay.locator('[data-studio-handle="move"]'); await moveHandle.focus();
    await page.keyboard.press('Shift+ArrowRight');
    await wait(async () => (await blocks()).find(block => block.id === text.id).values.studioX > moved.studioX, 'accessible keyboard nudge');
    await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
    check(true, 'Focused handles support keyboard placement, including larger Shift increments.');
    const imageType = definition.blocks.find(item => item.id === 'image');
    if (imageType) {
      const image = await add(imageType), file = path.join(output, 'direct-image.png');
      fs.writeFileSync(file, Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aOCkAAAAASUVORK5CYII=', 'base64'));
      const picker = page.waitForEvent('filechooser'); await page.locator('[data-upload-field="src"]').click(); await (await picker).setFiles(file);
      await wait(async () => (await blocks()).find(item => item.id === image.id).values.src?.startsWith('/media/'), 'local media');
      const persistedMediaPath = (await blocks()).find(item => item.id === image.id).values.src;
      await wait(() => page.evaluate(({ pageId, blockId, mediaPath }) => {
        const state = window.__studioAuthoring?.state(), iframe = document.querySelector('#page-frame');
        const frameDocument = iframe?.contentDocument, image = frameDocument?.querySelector('[data-block-id="' + blockId + '"] img');
        return state?.currentGenerationReady === true && state.nodeId === pageId
          && state.framePageId === pageId && state.frameReadyPageId === pageId
          && state.frameGenerationId === iframe?.dataset.generationId
          && state.frameReadyGenerationId === iframe?.dataset.generationId
          && iframe?.dataset.pageId === pageId && !iframe.inert && !iframe.hidden
          && image?.complete && image.naturalWidth > 0
          && new URL(image.currentSrc || image.src, frameDocument.baseURI).pathname === new URL(mediaPath, location.origin).pathname;
      }, { pageId, blockId: image.id, mediaPath: persistedMediaPath }), 'current renderer displays the persisted local image');
      await frame.locator('[data-block-id="' + image.id + '"] img').evaluate(image => image.decode());
      check(true, 'The unchanged media picker imports a project-local image and displays it in the actual canvas.');
      const imageBlock = frame.locator('[data-block-id="' + image.id + '"]');
      const mediaContained = element => {
        const parent = element.closest('.hl-region'), bounds = element.getBoundingClientRect(), regionBounds = parent.getBoundingClientRect();
        const imageBounds = element.querySelector('img').getBoundingClientRect();
        const regions = [...element.ownerDocument.querySelectorAll('.hl-region')], following = regions.slice(regions.indexOf(parent) + 1);
        return imageBounds.height > 0 && bounds.height > 0 && regionBounds.bottom >= bounds.bottom - 1
          && following.every(region => region.getBoundingClientRect().top >= bounds.bottom - 1);
      };
      const waitForMediaLayout = async (element, description) => {
        try { await wait(async () => await element.evaluate(mediaContained), description); }
        catch (error) {
          const metrics = await element.evaluate(element => {
            const parent = element.closest('.hl-region'), image = element.querySelector('img');
            const rectangle = item => item?.getBoundingClientRect().toJSON();
            const regions = [...element.ownerDocument.querySelectorAll('.hl-region')];
            return { block: rectangle(element), image: rectangle(image), parent: rectangle(parent),
              following: regions.slice(regions.indexOf(parent) + 1).map(rectangle),
              blockStyle: element.getAttribute('style'), sized: element.getAttribute('data-studio-sized'),
              parentStyle: parent.getAttribute('style'), parentMinHeight: getComputedStyle(parent).minHeight,
              imageStyle: image && { width: getComputedStyle(image).width, height: getComputedStyle(image).height,
                complete: image.complete, naturalWidth: image.naturalWidth, naturalHeight: image.naturalHeight } };
          }).catch(failure => ({ inspectionError: failure.message }));
          report.mediaDiagnostics = { description, metrics };
          throw new Error(error.message + '; actual layout: ' + JSON.stringify(metrics));
        }
      };
      await waitForMediaLayout(imageBlock, 'automatic image height and following region separation');
      check((await blocks()).find(item => item.id === image.id).values.studioHeight === 0,
        'A newly placed image keeps its automatic height until the author resizes it.');
      check(true, 'The live canvas region contains its automatic-height image and leaves following sections below it.');
      const autoReader = await browser.newPage({ viewport: { width: 1100, height: 900 } });
      autoReader.on('pageerror', error => report.errors.push(error.message));
      await autoReader.goto(origin + runtime.proxyPrefix + 'page/' + pageId + '?edit=0', { waitUntil: 'domcontentloaded' });
      const readerImage = autoReader.locator('[data-block-id="' + image.id + '"]');
      await readerImage.waitFor();
      await wait(() => readerImage.locator('img').evaluate((image, mediaPath) => image.complete && image.naturalWidth > 0
        && new URL(image.currentSrc || image.src, image.ownerDocument.baseURI).pathname === new URL(mediaPath, location.origin).pathname,
      persistedMediaPath), 'reader displays the persisted local image');
      await readerImage.locator('img').evaluate(image => image.decode());
      await waitForMediaLayout(readerImage, 'reader automatic image height and following section separation');
      check(true, 'The actual reader also contains automatic-height media and keeps any following regions clear.');
      await autoReader.close();
      const originalPositionedClass = await imageBlock.evaluate(element => element.classList.contains('studio-canvas-positioned'));
      const originalSizedAttribute = await imageBlock.getAttribute('data-studio-sized');
      const fitDuringGesture = async () => {
        const media = await imageBlock.evaluate(element => {
          const block = element.getBoundingClientRect(), image = element.querySelector('img').getBoundingClientRect();
          const caption = element.querySelector('figcaption')?.getBoundingClientRect();
          return { positioned: element.classList.contains('studio-canvas-positioned'), sized: element.dataset.studioSized === 'true', fits: image.left >= block.left - 1 && image.right <= block.right + 1
            && image.top >= block.top - 1 && image.bottom <= block.bottom + 1 && (!caption || caption.bottom <= block.bottom + 1) };
        });
        check(media.positioned && media.sized && media.fits, 'The image and any caption fit the resized outer box live, before the first gesture is saved.');
      };
      await gesture('se', -90, -50, true, false, image.id, fitDuringGesture);
      check(await imageBlock.evaluate(element => element.classList.contains('studio-canvas-positioned')) === originalPositionedClass, 'Cancelling a first media gesture restores its original placement class as well as its geometry.');
      check(await imageBlock.getAttribute('data-studio-sized') === originalSizedAttribute, 'Cancelling the first image resize restores its automatic-height sizing marker.');
      await gesture('se', -90, -50, false, false, image.id, fitDuringGesture);
    }
    const counterType = definition.blocks.find(item => item.id === 'counter');
    if (counterType) {
      const counter = await add(counterType), widget = frame.locator('[data-block-id="' + counter.id + '"] .hl-counter');
      const counterGeometry = geometryFields.map(field => [field, counter.values[field]]);
      await widget.waitFor(); const before = await widget.locator('output').textContent();
      await widget.getByRole('button').first().click();
      await wait(async () => await widget.locator('output').textContent() !== before, 'original interactive tool');
      const savedCounter = (await blocks()).find(item => item.id === counter.id);
      check(counterGeometry.every(([field, value]) => savedCounter.values[field] === value), 'Original interactive controls remain usable without accidentally moving or resizing the widget.');
    }
    await handles(); await inline.fill('After moving, text is still editable on the canvas.'); await page.locator('#studio-save').click();
    await wait(async () => (await blocks()).find(item => item.id === text.id).values[textField.id] === 'After moving, text is still editable on the canvas.', 'post-gesture inline edit');
    const stable = (await blocks()).find(item => item.id === text.id).values;
    report.recoveryGeometry = { blockId: text.id, savedHeight: stable.studioHeight };
    await page.route('**/api/operations', route => route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ error: 'QA concurrent edit' }) }));
    await gesture('se', -12, 12, false, true);
    await page.getByRole('button', { name: 'Download draft', exact: true }).waitFor();
    check(await page.evaluate(() => window.__studioAuthoring.state().dirty), 'A rejected geometry save retains the recoverable draft instead of dropping the manipulation.');
    page.once('dialog', dialog => dialog.accept()); await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => window.__studioAuthoring?.state().ready);
    await page.getByRole('button', { name: 'Restore draft', exact: true }).click();
    // Restore completes asynchronously and refreshes the renderer after the
    // outer application is ready. Keep the intentional conflict in place until
    // that actual Page generation is ready, then exercise the real Save button.
    await wait(() => page.evaluate(() => {
      const state = window.__studioAuthoring.state(), frame = document.querySelector('#page-frame');
      const ready = window.__directReadyIdentity;
      return state.hasDraft && state.currentGenerationReady && !state.frameLoading && !state.saving
        && document.querySelector('#save-status')?.textContent === 'Recovered draft — save to keep it'
        && !!ready && ready.pageId === state.nodeId && ready.generationId === state.frameGenerationId
        && window.__directReadyDocument === frame.contentDocument && !!frame.contentDocument?.querySelector('#studio-canvas-tools');
    }), 'recovered draft and its actual Page generation are ready for Save');
    await page.unroute('**/api/operations'); await page.locator('#studio-save').click();
    await wait(async () => (await blocks()).find(item => item.id === text.id).values.studioHeight > stable.studioHeight, 'recovered geometry save');
    await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
    check(true, 'A geometry draft survives a reload and saves after the conflict is resolved.');
    await handles();
    const desktop = path.join(output, 'direct-editor-desktop.png'); await page.screenshot({ path: desktop, fullPage: true }); report.screenshots.push(desktop);
    const finalValues = (await blocks()).find(item => item.id === text.id).values;
    const published = await browser.newPage({ viewport: { width: 1100, height: 900 } });
    await published.goto(origin + runtime.proxyPrefix + 'page/' + pageId + '?edit=0', { waitUntil: 'domcontentloaded' });
    const publishedBlock = published.locator('[data-block-id="' + text.id + '"]'); await publishedBlock.waitFor();
    const readPublishedGeometry = () => publishedBlock.evaluate(element => {
      const style = getComputedStyle(element), rect = element.getBoundingClientRect(), parent = element.closest('.hl-region'), bounds = parent.getBoundingClientRect();
      return { position: style.position, left: element.style.left, top: element.style.top, width: element.style.width, height: element.style.height,
        inline: element.getAttribute('style'), rect: rect.toJSON(), parent: bounds.toJSON(),
        xPercent: (rect.left - bounds.left - parent.clientLeft) / parent.clientWidth * 100,
        widthPercent: rect.width / parent.clientWidth * 100, yPixels: rect.top - bounds.top - parent.clientTop };
    });
    const matchesPublishedGeometry = style => style.position === 'absolute' && Math.abs(parseFloat(style.left) - finalValues.studioX) < .1
      && Math.abs(parseFloat(style.top) - finalValues.studioY) < .1 && Math.abs(parseFloat(style.width) - finalValues.studioWidth) < .1
      && Math.abs(parseFloat(style.height) - finalValues.studioHeight) < .1 && Math.abs(style.xPercent - finalValues.studioX) < .2
      && Math.abs(style.widthPercent - finalValues.studioWidth) < .2 && Math.abs(style.yPixels - finalValues.studioY) < 1
      && Math.abs(style.rect.height - finalValues.studioHeight) < 1;
    const firstReaderGeometry = await readPublishedGeometry(), readerStarted = Date.now();
    report.readerGeometry = { saved: finalValues, initial: firstReaderGeometry, viewport: { width: 1100, height: 900 } };
    let publishedStyle = firstReaderGeometry;
    // The renderer follows the content journal independently of the gateway's
    // save acknowledgement. Require its current values, not merely an SSR node.
    await wait(async () => { publishedStyle = await readPublishedGeometry(); return matchesPublishedGeometry(publishedStyle); }, 'reader catches current saved geometry', 10000);
    report.readerGeometry.final = publishedStyle; report.readerGeometry.refreshMilliseconds = Date.now() - readerStarted;
    check(matchesPublishedGeometry(publishedStyle), 'The actual reader output preserves the saved direct placement and size.');
    check(await published.locator('[data-studio-canvas-overlay]').count() === 0, 'Editing handles are absent from the reader and published output.');
    await published.close();
    await handles(); await page.locator('#block-reset-placement').click();
    await wait(async () => { const values = (await blocks()).find(item => item.id === text.id).values;
      return geometryFields.every(field => !Object.hasOwn(values, field)); }, 'return element to document flow');
    await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
    await wait(async () => await target.evaluate(element => getComputedStyle(element).position !== 'absolute'), 'normal rendered document flow');
    check((await blocks()).find(item => item.id === text.id).values[textField.id] === finalValues[textField.id], 'Reset placement clears canvas geometry and returns the element to normal document flow without changing its content.');
    await page.locator('#undo-button').click();
    await wait(async () => (await blocks()).find(item => item.id === text.id).values.studioPositioned === true
      && (await blocks()).find(item => item.id === text.id).values.studioHeight === finalValues.studioHeight, 'undo placement reset');
    check(true, 'Undo restores the complete saved placement after Reset placement.');
    const appearanceBaseline = (await api('/api/state')).nodes.find(item => item.id === pageId).appearance || {};
    const backgroundBaseline = await frame.locator('.hl-document').evaluate(element => getComputedStyle(element).backgroundColor);
    await page.locator('#appearance-button').click(); await page.locator('#dialog[open]').waitFor();
    check(await page.locator('#dialog [data-field="background"]').count() === 1
      && await page.locator('#dialog [data-field^="studio"]').count() === 0, 'Appearance retains the public background controls and hides private recipe and runtime protocol fields.');
    await page.locator('#dialog [data-field="background"]').evaluate(input => {
      input.value = '#182b37'; input.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await page.locator('#dialog-submit').click(); await page.locator('#dialog').waitFor({ state: 'hidden' });
    await wait(async () => (await api('/api/state')).nodes.find(item => item.id === pageId).appearance.background === '#182b37', 'public appearance background save');
    await wait(async () => await frame.locator('.hl-document').evaluate(element => getComputedStyle(element).backgroundColor === 'rgb(24, 43, 55)'), 'actual canvas appearance refresh');
    check(true, 'The public background picker updates both the saved Page appearance and the actual rendered canvas.');
    await page.locator('#appearance-button').click(); await page.locator('#dialog[open]').waitFor();
    if (Object.hasOwn(appearanceBaseline, 'background')) await page.locator('#dialog [data-field="background"]').evaluate((input, value) => {
      input.value = value; input.dispatchEvent(new Event('input', { bubbles: true }));
    }, appearanceBaseline.background);
    else await page.locator('#dialog [data-clear-field="background"]').click();
    await page.locator('#dialog-submit').click(); await page.locator('#dialog').waitFor({ state: 'hidden' });
    await wait(async () => await frame.locator('.hl-document').evaluate((element, expected) => getComputedStyle(element).backgroundColor === expected, backgroundBaseline), 'restore original background');
    check(JSON.stringify((await api('/api/state')).nodes.find(item => item.id === pageId).appearance) === JSON.stringify(appearanceBaseline),
      'Restoring the background returns to the exact original appearance without changing private project markers.');
    const readCanvasHeight = async label => {
      const documentHeight = await frame.locator('.hl-document').evaluate(async element => {
        await element.ownerDocument.fonts.ready;
        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        return element.getBoundingClientRect().height;
      });
      return { label, documentHeight, frameHeight: (await page.locator('#page-frame').boundingBox()).height };
    };
    const originalHeight = await readCanvasHeight('before repeated refresh');
    const heights = [originalHeight];
    for (let index = 0; index < 3; index++) {
      check(await page.evaluate(index => window.__studioAuthoring.refreshFoundation('qa-height-' + index), index), 'The saved canvas refreshes without modifying content.');
      await frame.locator('#studio-canvas-tools').waitFor({ state: 'attached' }); await handles();
      heights.push(await readCanvasHeight('same content refresh ' + index));
    }
    check(heights.every(height => Math.abs(height.documentHeight - originalHeight.documentHeight) < 4
      && Math.abs(height.frameHeight - originalHeight.frameHeight) < 4), 'Repeated refresh and selection keep the canvas and iframe heights stable when content has not grown.');
    await page.setViewportSize({ width: 600, height: 844 }); heights.push(await readCanvasHeight('narrow window'));
    await page.setViewportSize({ width: 1500, height: 1000 }); const restoredHeight = await readCanvasHeight('original window restored'); heights.push(restoredHeight);
    report.canvasHeightSnapshots = heights;
    check(Math.abs(restoredHeight.documentHeight - originalHeight.documentHeight) < 4
      && Math.abs(restoredHeight.frameHeight - originalHeight.frameHeight) < 4, 'Resizing the window and restoring its original dimensions does not accumulate blank canvas height.');

    // The original gesture checks above deliberately select the saved outer
    // block. Separately exercise the newly selectable child with real pointers.
    const childBaseline = structuredClone((await blocks()).find(block => block.id === text.id).values);
    const childSource = async () => JSON.parse((await blocks()).find(block => block.id === text.id).values.studioElements || '[]');
    let childPath;
    async function childHandles() {
      // Select through the content interior, clear of the outer block's corner
      // handles. Playwright's normal center click retains real hit testing.
      await inline.click();
      await page.waitForFunction(id => {
        const selection = window.__studioAuthoring.state().selectedElement;
        return selection?.blockId === id && !!selection.path;
      }, text.id);
      childPath = await page.evaluate(() => window.__studioAuthoring.state().selectedElement.path);
      const overlay = frame.locator('[data-studio-canvas-overlay][data-studio-canvas-block-id="' + text.id + '"]');
      await wait(() => overlay.evaluate(element => {
        const selected = element.ownerDocument.querySelector('[data-studio-element-selected="true"]');
        if (!selected || element.hidden) return false;
        const a = selected.getBoundingClientRect(), b = element.getBoundingClientRect();
        return a.width > 0 && a.height > 0 && Math.abs(a.left - b.left) <= 2 && Math.abs(a.top - b.top) <= 2
          && Math.abs(a.width - b.width) <= 2 && Math.abs(a.height - b.height) <= 2;
      }), 'actual selected-child handles');
      return overlay;
    }
    async function childGesture(handle, dx, dy) {
      const overlay = await childHandles(), button = overlay.locator('[data-studio-handle="' + handle + '"]');
      await button.scrollIntoViewIfNeeded();
      const bounds = await button.boundingBox(), operationCount = requests.length;
      await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
      await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2); await page.mouse.down();
      await page.mouse.move(bounds.x + bounds.width / 2 + dx, bounds.y + bounds.height / 2 + dy, { steps: 6 });
      check(requests.length === operationCount, 'Dragging the selected child ' + handle + ' retains one recoverable draft until release.');
      await page.mouse.up();
      await wait(async () => requests.length > operationCount && (await childSource()).some(record => record.path === childPath && record.styles?.position === 'absolute'),
        'selected-child ' + handle + ' geometry save');
      await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
      check(requests.length === operationCount + 1, 'A selected child ' + handle + ' gesture saves as one ordinary Page operation.');
      return (await childSource()).find(record => record.path === childPath).styles;
    }
    await childHandles();
    const childBefore = await inline.boundingBox();
    const childResized = await childGesture('se', -Math.min(60, childBefore.width * .15), 18);
    check(parseFloat(childResized.width) < childBefore.width - 15 && parseFloat(childResized.height) > childBefore.height + 10,
      'Dragging a selected nested element corner persists that child\'s independent width and height.');
    const childMoved = await childGesture('move', 20, 17);
    check(parseFloat(childMoved.left) > parseFloat(childResized.left) + 10 && parseFloat(childMoved.top) > parseFloat(childResized.top) + 10,
      'Dragging a nested element move handle persists its own offset inside the containing block.');
    const childSaved = (await blocks()).find(block => block.id === text.id).values;
    check(geometryFields.every(field => childSaved[field] === childBaseline[field]) && childSaved[textField.id] === childBaseline[textField.id],
      'Nested pointer resize and drag preserve their owning block\'s saved geometry and text content.');
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => window.__studioAuthoring?.state().currentGenerationReady === true);
    await wait(() => inline.evaluate((element, expected) => {
      const style = getComputedStyle(element);
      return style.position === 'absolute' && Math.abs(parseFloat(style.left) - parseFloat(expected.left)) <= 1
        && Math.abs(parseFloat(style.top) - parseFloat(expected.top)) <= 1
        && Math.abs(parseFloat(style.width) - parseFloat(expected.width)) <= 1
        && Math.abs(parseFloat(style.height) - parseFloat(expected.height)) <= 1;
    }, childMoved), 'reloaded nested pointer geometry');
    check(true, 'Reopening renders the nested child at its actual saved pointer placement and size.');
    await page.locator('#undo-button').click();
    await wait(async () => (await childSource()).find(record => record.path === childPath)?.styles?.left === childResized.left,
      'undo nested pointer move');
    await page.locator('#undo-button').click();
    await wait(async () => JSON.stringify((await blocks()).find(block => block.id === text.id).values) === JSON.stringify(childBaseline),
      'undo nested pointer resize');
    await page.waitForFunction(() => !window.__studioAuthoring.state().dirty);
    check(true, 'Undo restores the exact block values from before both nested gestures.');
    await page.locator('[data-panel="elements"]').click();
    const elementsShot = path.join(output, 'direct-editor-elements.png');
    await page.screenshot({ path: elementsShot, fullPage: true }); report.screenshots.push(elementsShot);
    for (const width of [600, 390]) {
      await page.setViewportSize({ width, height: 844 });
      if (width === 390 && await page.locator('#editor-panel').isVisible()) await page.locator('#studio-inspector-toggle').click();
      check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'The direct editor fits a ' + width + '-pixel workspace without horizontal overflow.');
      if (!await page.locator('#editor-panel').isVisible()) await page.locator('#studio-inspector-toggle').click();
      // A late canvas selection can reopen the legacy Properties drawer between
      // separate protocol calls. Check both library choices in the same browser
      // turn as their actual reopen control, rather than observing mixed states.
      check(await page.evaluate(() => {
        const panel = document.querySelector('#editor-panel');
        if (getComputedStyle(panel).display === 'none') document.querySelector('#studio-inspector-toggle').click();
        return ['elements', 'templates'].every(id => {
          const button = document.querySelector('[data-panel="' + id + '"]');
          return button && !button.hidden && getComputedStyle(button).display !== 'none'
            && button.getBoundingClientRect().width > 0 && button.getBoundingClientRect().height > 0;
        });
      }), 'Template and Elements remain reachable at ' + width + ' pixels.');
      const shot = path.join(output, 'direct-editor-' + width + '.png'); await page.screenshot({ path: shot, fullPage: true }); report.screenshots.push(shot);
    }
    check(report.errors.length === 0, 'The complete direct-editing workflow produces no uncaught browser errors. ' + report.errors.join('; '));
    fs.writeFileSync(path.join(output, 'studio-direct-editor-result.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify({ result: report.result, assertions: report.checks.length, receipt: path.join(output, 'studio-direct-editor-result.json') }));
  } catch (error) {
    fs.writeFileSync(path.join(output, 'studio-direct-editor-host.log'), logs);
    report.failure = error.stack || String(error);
    if (page) report.failureDiagnostics = await page.evaluate(() => {
      const frame = document.querySelector('#page-frame');
      const draft = window.__directLastState?.draft;
      return { state: window.__studioAuthoring?.state(), frame: { pageId: frame?.dataset.pageId, generation: frame?.dataset.generationId,
        blocks: [...(frame?.contentDocument?.querySelectorAll('[data-block-id]') || [])].map(block => block.dataset.blockId) },
        trustedReady: { ...window.__directReadyIdentity, sameDocument: frame?.contentDocument === window.__directReadyDocument,
          canvasAttached: !!frame?.contentDocument?.querySelector('#studio-canvas-tools') },
        flushResults: window.__directFlushResults?.slice(-8), lastFlushError: window.__directLastFlushError || null,
        draftGeometry: draft ? { id: draft.id, revision: draft.revision, blocks: draft.blocks.map(block => ({ id: block.id, type: block.type,
          values: Object.fromEntries(['studioPositioned', 'studioX', 'studioY', 'studioWidth', 'studioHeight'].filter(field => Object.hasOwn(block.values, field)).map(field => [field, block.values[field]])) })) } : null,
        properties: [...document.querySelectorAll('#properties-content [data-field]')].map(input => ({ field: input.dataset.field, value: input.value })),
        inert: { main: document.querySelector('#main')?.inert, properties: document.querySelector('#properties-content')?.inert },
        status: document.querySelector('#save-status')?.textContent };
    }).catch(() => null);
    fs.writeFileSync(path.join(output, 'studio-direct-editor-failure.json'), JSON.stringify(report, null, 2));
    if (report.mediaDiagnostics) fs.writeFileSync(path.join(output, 'studio-direct-editor-layout-failure.json'), JSON.stringify(report.mediaDiagnostics, null, 2));
    if (report.readerGeometry) fs.writeFileSync(path.join(output, 'studio-direct-editor-reader-geometry-failure.json'), JSON.stringify(report.readerGeometry, null, 2));
    if (page) await page.screenshot({ path: path.join(output, 'studio-direct-editor-failure.png'), fullPage: true }).catch(() => {});
    throw error;
  } finally {
    if (browser) await browser.close();
    if (host.exitCode === null) spawnSync('taskkill.exe', ['/PID', String(host.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
  }
})().catch(error => { console.error(error.stack || String(error)); process.exitCode = 1; });
