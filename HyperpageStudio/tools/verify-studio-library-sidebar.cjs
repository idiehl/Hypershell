/* Browser-side acceptance, injected into the actual hidden Studio WebView by StudioLibrarySidebarNativeVerifier.
   All catalogue, preview, import and template saves use its real chrome.webview/C# bridge. No bridge or API is mocked. */
(async () => {
  const options = window.__studioLibrarySidebarQaOptions;
  const report = window.__studioLibrarySidebarQa = { done: false, passed: false, checks: [], data: {}, progress: 'Starting' };
  const check = (condition, message) => { if (!condition) throw new Error(message); report.checks.push(message); };
  const wait = async (predicate, message, timeout = 90000) => {
    const end = Date.now() + timeout;
    while (Date.now() < end) {
      const failed = replies.find(message => message.success === false && message.error !== 'Preview selection changed.');
      if (failed) throw new Error('Native library action failed: ' + failed.error);
      if (await predicate()) return;
      await new Promise(resolve => setTimeout(resolve, 80));
    }
    throw new Error('Timed out: ' + message);
  };
  const visible = element => !!element && !element.hidden && getComputedStyle(element).display !== 'none' && !!element.getClientRects().length;
  const click = selector => { const element = document.querySelector(selector); if (!element) throw new Error('Missing ' + selector); element.click(); return element; };
  const setValue = (element, value, event = 'change') => {
    if (!element) throw new Error('Missing input for ' + value);
    element.value = value; element.dispatchEvent(new Event(event, { bubbles: true }));
  };
  const api = async route => { const response = await fetch(route); if (!response.ok) throw new Error(route + ': ' + response.status + ' ' + await response.text()); return response.json(); };
  const state = () => api('/api/state');
  const pending = new Map();
  const replies = [];
  const flushResults = [];
  const observedStates = [];
  let trustedFrameReady = null;
  let trustedFrameDocument = null;
  const recordFlush = event => { flushResults.push(event.detail); if (flushResults.length > 12) flushResults.shift(); };
  const recordState = event => { observedStates.push(event.detail); if (observedStates.length > 8) observedStates.shift(); };
  const recordFrameReady = event => {
    const frame = document.querySelector('#page-frame'), message = event.data;
    if (!frame || event.origin !== location.origin || event.source !== frame.contentWindow
      || message?.source !== 'hyperlib-renderer' || message.kind !== 'ready'
      || message.pageId !== frame.dataset.pageId || message.pageId !== window.__studioAuthoring?.state()?.nodeId
      || !frame.dataset.generationId || message.generationId !== frame.dataset.generationId) return;
    trustedFrameReady = { pageId: message.pageId, generationId: message.generationId };
    trustedFrameDocument = frame.contentDocument;
  };
  const receive = event => {
    const message = event.data;
    if (message?.type !== 'studio:library-result') return;
    replies.push(message);
    const request = pending.get(message.requestId);
    if (!request) return;
    pending.delete(message.requestId);
    message.success ? request.resolve(message.result) : request.reject(new Error(message.error || 'Native library request failed.'));
  };
  const nativeRead = (action, payload = {}) => new Promise((resolve, reject) => {
    const requestId = 'native-sidebar-qa-' + crypto.randomUUID();
    pending.set(requestId, { resolve, reject });
    chrome.webview.postMessage({ type: 'studio:library-request', requestId, action, ...payload });
  });
  async function readyPage(id, blocks) {
    await wait(() => {
      const frame = document.querySelector('#page-frame'), current = window.__studioAuthoring.state();
      if (!frame || current.libraryBusy || current.libraryMutationPending || current.nodeId !== id || current.currentGenerationReady !== true || !current.frameReady || current.frameLoading
        || frame.inert || frame.dataset.pageId !== id || current.framePageId !== id || current.frameReadyPageId !== id
        || current.frameGenerationId !== frame.dataset.generationId || current.frameReadyGenerationId !== frame.dataset.generationId
        || trustedFrameReady?.pageId !== id || trustedFrameReady?.generationId !== frame.dataset.generationId
        || !frame.contentDocument || frame.contentDocument !== trustedFrameDocument) return false;
      const field = frame.contentDocument.querySelector('[data-block-id] [data-field="text"][contenteditable="true"]');
      return !!field && blocks.some(block => block.id === field.closest('[data-block-id]')?.dataset.blockId);
    }, 'the actual Page ' + id + ' and its current generation are ready for inline editing');
  }
  async function textGeometry(blockId) {
    const frame = document.querySelector('#page-frame'), doc = frame.contentDocument;
    await doc.fonts?.ready;
    await new Promise(resolve => frame.contentWindow.requestAnimationFrame(() => frame.contentWindow.requestAnimationFrame(resolve)));
    const block = [...doc.querySelectorAll('[data-block-id]')].find(element => element.dataset.blockId === blockId);
    const root = doc.querySelector('.hl-document');
    if (!block || !root || doc !== trustedFrameDocument) throw new Error('The current rendered text geometry is unavailable.');
    const rect = block.getBoundingClientRect(), pageRect = root.getBoundingClientRect();
    return { top: rect.top - pageRect.top, left: rect.left - pageRect.left, width: rect.width, height: rect.height,
      region: block.closest('.hl-region')?.dataset.region };
  }
  async function flush() {
    check(await window.__studioAuthoring.flush('sidebar-qa-save-' + crypto.randomUUID()), 'The actual Page writer confirms its saved content before the next sidebar action.');
    await wait(() => !window.__studioAuthoring.state().dirty, 'confirmed Page save');
  }
  async function scope(value, search = '') {
    await wait(() => !window.__studioAuthoring.state().libraryBusy && !window.__studioAuthoring.state().libraryMutationPending, 'completed library placement before changing scope');
    click('[data-panel="templates"]');
    await wait(() => !!document.querySelector('#template-scope'), 'Template scope chooser');
    setValue(document.querySelector('#template-scope'), value);
    if (document.querySelector('#library-search')) setValue(document.querySelector('#library-search'), search, 'input');
    await wait(() => document.querySelectorAll('[data-library-id]').length > 0, value + ' catalogue rows');
  }
  function auditTemplateRows(catalogue) {
    const list = document.querySelector('.studio-template-list');
    const rows = [...document.querySelectorAll('.studio-template-row[data-library-id]')];
    check(!!list && rows.length > 0 && JSON.stringify([...document.querySelectorAll('.studio-template-columns span')].map(item => item.textContent.trim())) === JSON.stringify(['Name', 'Type', 'Project']),
      'Templates use a visible Name, Type and Project column header above compact rows.');
    check(rows.every(row => {
      const item = catalogue.find(candidate => candidate.id === row.dataset.libraryId);
      return !!item && row.querySelector('.studio-template-name')?.textContent.trim() === item.title
        && row.querySelector('.studio-template-type')?.textContent.trim() === item.type
        && row.querySelector('.studio-template-project')?.textContent.trim() === (item.project || 'No project');
    }), 'Every displayed template row shows the exact name, placement type and actual project for its stable ID.');
    check(rows.every(row => !row.querySelector('svg,img,.studio-library-icon,.studio-template-icon'))
      && rows.every(row => getComputedStyle(row).gridTemplateColumns.split(' ').length === 3),
      'Template rows contain only their three text columns and reserve no decorative icon column.');
    const short = rows.find(row => row.querySelector('.studio-template-name').textContent.length < 24) || rows[0];
    const sample = short.cloneNode(true);
    sample.removeAttribute('data-library-id'); sample.classList.remove('selected');
    sample.querySelector('.studio-template-name').textContent = 'A long descriptive reusable template name that wraps naturally over several lines without clipping any words or creating a large blank card.';
    list.appendChild(sample);
    const bounds = short.getBoundingClientRect(), longBounds = sample.getBoundingClientRect(), style = getComputedStyle(short);
    const naturalRows = style.maxHeight === 'none' && bounds.height <= 58 && longBounds.height > bounds.height + 10
      && sample.scrollHeight <= sample.clientHeight + 2 && short.querySelector('.studio-template-name').getBoundingClientRect().left - bounds.left <= 11;
    sample.remove();
    check(naturalRows, 'Short template names occupy compact text rows; longer names increase row height naturally without clipping or an empty icon gutter.');
    report.data.rowGeometry = { rowCount: rows.length, shortHeight: bounds.height, wrappedHeight: longBounds.height, columns: style.gridTemplateColumns, gap: style.columnGap };
  }
  async function selectLibrary(id) {
    await wait(() => !!document.querySelector('[data-library-id="' + CSS.escape(id) + '"]'), 'library card ' + id);
    document.querySelector('[data-library-id="' + CSS.escape(id) + '"]').focus();
    click('[data-library-id="' + CSS.escape(id) + '"]');
    await wait(() => !!document.querySelector('#library-add') && !document.querySelector('#library-add').disabled, 'selected library Add action');
  }
  async function readyPreview(id) {
    await wait(() => {
      const surface = document.querySelector('#studio-library-preview'), frame = document.querySelector('#studio-library-preview-frame');
      return visible(surface) && surface.dataset.itemId === id && surface.dataset.state === 'ready'
        && frame?.dataset.loaded === 'true' && /^http:\/\/127\.0\.0\.1:\d+\//.test(frame.src);
    }, 'automatically rendered main-workspace preview for ' + id, 180000);
    return document.querySelector('#studio-library-preview-frame');
  }
  async function dismissPreview() {
    click('#studio-library-preview-close');
    await wait(() => !visible(document.querySelector('#studio-library-preview')), 'restored editable Page canvas');
  }
  async function addItem(type, title, parentId) {
    await wait(() => !window.__studioAuthoring.state().libraryBusy && !window.__studioAuthoring.state().libraryMutationPending, 'completed library placement before hierarchy navigation');
    await window.__studioAuthoring.navigate(parentId, 'sidebar-hierarchy-' + crypto.randomUUID());
    click('[data-left-panel="contents"]');
    click('#studio-add-item');
    await wait(() => document.querySelector('#dialog')?.open && !!document.querySelector('#dialog [name="type"]'), 'add hierarchy item dialog');
    const types = [...document.querySelectorAll('#dialog [name="type"] option')].map(option => option.value);
    check(types.includes(type) && !types.includes('work'), 'The current Work offers the valid ' + type + ' hierarchy level without creating another Work.');
    setValue(document.querySelector('#dialog [name="type"]'), type);
    const parents = document.querySelector('#dialog [name="parentId"]');
    if (parents) setValue(parents, parentId);
    setValue(document.querySelector('#dialog [name="title"]'), title, 'input');
    click('#dialog-submit');
    await wait(async () => !(document.querySelector('#dialog')?.open) && (await state()).nodes.some(node => node.title === title), 'persist ' + title);
    const node = (await state()).nodes.find(item => item.title === title);
    check(node.parentId === parentId && node.type === type && node.workId === options.workId,
      'Creating ' + title + ' preserves its selected parent, hierarchy type and Work ownership.');
    return node.id;
  }
  try {
    if (!options || !window.chrome?.webview) throw new Error('This script must run in the owned native Studio WebView.');
    chrome.webview.addEventListener('message', receive);
    window.addEventListener('studio:flush-result', recordFlush);
    window.addEventListener('studio:state', recordState);
    window.addEventListener('message', recordFrameReady);
    await wait(() => window.__studioAuthoring?.state()?.ready, 'native authoring ready');
    check(JSON.stringify([...document.querySelectorAll('#editor-panel [data-panel]')].map(button => button.textContent.trim())) === JSON.stringify(['Template', 'Elements']),
      'The right sidebar contains Template and Elements tabs.');
    check(!!document.querySelector('[data-left-panel="contents"]') && !!document.querySelector('[data-left-panel="inspector"]')
      && !document.querySelector('[data-panel="assistant"]'), 'Contents and actual Properties are on the left, and Assistant is absent.');

    report.progress = 'Reading actual Core and project catalogue';
    const catalogueResult = await nativeRead('catalog');
    const catalogue = Array.isArray(catalogueResult) ? catalogueResult : catalogueResult.items;
    check(Array.isArray(catalogue) && catalogue.length > 500, 'The native sidebar exposes the full Core object collection instead of a small placeholder list.');
    report.data.catalogCount = catalogue.length;
    for (const expected of ['page', 'section', 'module']) check(catalogue.some(item => item.scope === expected), 'The reusable catalogue includes ' + expected + ' templates.');
    for (const value of ['page', 'section', 'module']) {
      await scope(value);
      const allowed = new Set(catalogue.filter(item => item.scope === value).map(item => item.id));
      const shown = [...document.querySelectorAll('[data-library-id]')].map(card => card.dataset.libraryId);
      check(shown.length > 0 && shown.every(id => allowed.has(id)), 'Choosing ' + value + ' displays only matching templates.');
      check(document.querySelector('#template-scope')?.closest('label')?.querySelector('span')?.textContent.trim() === 'Type'
        && document.querySelector('#template-project')?.closest('label')?.querySelector('span')?.textContent.trim() === 'Project',
        'The ' + value + ' template filters expose the actual placement Type and Project.');
      auditTemplateRows(catalogue);
    }

    report.progress = 'Combining real Project, Type, Category and search filters';
    await scope('page');
    const atlasPage = catalogue.find(item => item.scope === 'page' && item.project === 'Atlas');
    if (!atlasPage) throw new Error('The catalog must retain an actual Atlas Page project attribution.');
    check([...document.querySelector('#template-project').options].some(option => option.value === 'Atlas')
      && [...document.querySelector('#template-project').options].some(option => option.value === 'none'),
      'The Project filter exposes the real Atlas project and No project without deriving projects from collections.');
    setValue(document.querySelector('#template-project'), 'Atlas');
    setValue(document.querySelector('#template-category'), atlasPage.category);
    setValue(document.querySelector('#library-search'), atlasPage.title, 'input');
    let shownIds = [...document.querySelectorAll('[data-library-id]')].map(row => row.dataset.libraryId);
    check(shownIds.includes(atlasPage.id) && shownIds.every(id => {
      const item = catalogue.find(candidate => candidate.id === id);
      return item.scope === 'page' && item.project === 'Atlas' && item.category === atlasPage.category;
    }), 'Project, Type, Category and name search compose while preserving the exact selected source IDs.');
    if (atlasPage.sourceTitle && atlasPage.sourceTitle !== atlasPage.title) {
      setValue(document.querySelector('#library-search'), atlasPage.sourceTitle, 'input');
      check(!!document.querySelector('[data-library-id="' + CSS.escape(atlasPage.id) + '"]'),
        'Searching an original source title still finds the same template after its displayed name becomes generic.');
    }
    setValue(document.querySelector('#library-search'), '', 'input');
    setValue(document.querySelector('#template-category'), 'all');
    setValue(document.querySelector('#template-project'), 'none');
    shownIds = [...document.querySelectorAll('[data-library-id]')].map(row => row.dataset.libraryId);
    check(shownIds.length > 0 && shownIds.every(id => !catalogue.find(item => item.id === id)?.project),
      'No project displays only templates whose actual project attribution is absent.');
    setValue(document.querySelector('#template-project'), 'all');

    report.progress = 'Automatically previewing real Sections without changing the Page';
    const header = catalogue.find(item => item.id === 'section:header:simple-title')
      || catalogue.find(item => item.scope === 'section' && /header/i.test(item.title + ' ' + item.category));
    if (!header) throw new Error('The reusable header section collection is missing.');
    const secondHeader = catalogue.find(item => item.scope === 'section' && item.category === header.category && item.id !== header.id);
    if (!secondHeader) throw new Error('A second actual Header section is required to verify rapid selection.');
    await scope('section');
    const category = document.querySelector('#template-category');
    check(category?.closest('label')?.querySelector('span')?.textContent.trim() === 'Category'
      && document.querySelectorAll('#template-category').length === 1
      && ![...document.querySelectorAll('.studio-library-filters label span')].some(label => /Use case|Page section/i.test(label.textContent)),
      'Section scope has one functional Category chooser; Page region independently controls insertion.');
    setValue(category, header.category);
    check([...document.querySelectorAll('[data-library-id]')].every(card => catalogue.find(item => item.id === card.dataset.libraryId)?.category === header.category),
      'The functional category filters the displayed Section templates.');
    const initial = await state(), before = initial.pages[options.landingPageId].blocks.map(block => block.id);
    const initialSerialized = JSON.stringify(initial), originalFrame = document.querySelector('#page-frame'), originalDocument = originalFrame.contentDocument;
    const previewRepliesBefore = replies.filter(reply => reply.success && reply.result?.url || reply.error === 'Preview selection changed.').length;
    await selectLibrary(header.id);
    await wait(() => window.__studioAuthoring.state().libraryPendingRequests > 0, 'first real automatic preview request is pending');
    await selectLibrary(secondHeader.id);
    const previewFrame = await readyPreview(secondHeader.id);
    await wait(() => window.__studioAuthoring.state().libraryPendingRequests === 0, 'both actual preview replies settle', 180000);
    check(replies.filter(reply => reply.success && reply.result?.url || reply.error === 'Preview selection changed.').length >= previewRepliesBefore + 2
      && document.querySelector('#studio-library-preview').dataset.itemId === secondHeader.id
      && previewFrame.src === replies.filter(reply => reply.success && reply.result?.url).at(-1).result.url,
      'Switching selection while an actual native preview is pending displays only the latest choice after both requests settle.');
    check(document.activeElement?.dataset.libraryId === secondHeader.id
      && document.activeElement?.getAttribute('aria-pressed') === 'true',
      'The selected template row retains keyboard focus and pressed state through the actual asynchronous preview reply.');
    check(!document.querySelector('#library-preview') && document.querySelectorAll('.studio-library-actions button').length === 1
      && document.querySelector('#library-add').textContent.trim() === 'Add to Page',
      'Selection automatically previews the object and exposes only the Add to Page action.');
    const surface = document.querySelector('#studio-library-preview'), mainBounds = document.querySelector('#main').getBoundingClientRect(), previewBounds = surface.getBoundingClientRect();
    check(surface.tagName !== 'DIALOG' && !!surface.closest('#main') && previewBounds.left >= mainBounds.left - 1 && previewBounds.right <= mainBounds.right + 1
      && previewBounds.width > 200 && previewBounds.height > 200 && !document.querySelector('dialog[open]'),
      'The actual renderer preview occupies the main workspace while both sidebar libraries remain usable.');
    check(JSON.stringify(await state()) === initialSerialized,
      'Selecting and switching a template changes neither the content revision nor any Page, node or authored block.');
    check(/^http:\/\/127\.0\.0\.1:\d+\//.test(previewFrame.src), 'Automatic selection loads the actual owned renderer URL instead of a generic thumbnail.');
    report.data.previewLoaded = true; report.data.previewUrl = previewFrame.src;
    await dismissPreview();
    check(document.querySelector('#page-frame') === originalFrame && originalFrame.contentDocument === originalDocument,
      'Dismissing a preview restores the same editable Page document without reloading or replacing it.');
    await selectLibrary(header.id); await readyPreview(header.id);
    setValue(document.querySelector('#library-search'), 'no preview object matches this query', 'input');
    check(!visible(document.querySelector('#studio-library-preview')) && !document.querySelector('#library-add'),
      'Changing the library filter dismisses the temporary selection and restores the Page canvas.');
    setValue(document.querySelector('#library-search'), '', 'input');
    await selectLibrary(header.id); await readyPreview(header.id);
    report.previewCheckpoint = true;
    await wait(() => window.__studioLibrarySidebarQaContinuePreview === true, 'native source, rendered preview and journal fingerprint checkpoint');
    click('[data-panel="elements"]');
    check(!visible(document.querySelector('#studio-library-preview')) && originalFrame.contentDocument === originalDocument,
      'Switching library tabs dismisses the preview and preserves the editable Page document.');
    check(JSON.stringify(await state()) === initialSerialized, 'Preview filtering and tab changes do not save or import anything into the Work.');
    await scope('section', header.title);
    setValue(document.querySelector('#template-category'), header.category);
    await selectLibrary(header.id); await readyPreview(header.id);
    report.progress = 'Committing the selected real Header Section once';
    const addRepliesBefore = replies.filter(reply => reply.success && Array.isArray(reply.result?.addedBlockIds)).length;
    click('#library-add');
    document.querySelector('#library-add')?.click();
    await wait(async () => (await state()).pages[options.landingPageId].blocks.some(block => !before.includes(block.id)), 'actual Header import and Page insertion', 180000);
    await wait(() => !window.__studioAuthoring.state().libraryBusy && !window.__studioAuthoring.state().libraryMutationPending,
      'the one Header Add request and its native acknowledgment settle', 180000);
    const after = await state();
    const inserted = after.pages[options.landingPageId].blocks.filter(block => !before.includes(block.id));
    check(inserted.length === 1 && inserted[0].region === 'header'
      && JSON.stringify(inserted[0]).includes('Your site') && !visible(document.querySelector('#studio-library-preview')),
      'Rapid duplicate Add activation commits the selected Header exactly once into the chosen Page Section and restores the real canvas.');
    check(replies.filter(reply => reply.success && Array.isArray(reply.result?.addedBlockIds)).length === addRepliesBefore + 1,
      'The real native bridge acknowledges one Header insertion after rapid duplicate Add activation.');

    report.progress = 'Building Part, Chapter and Page hierarchy';
    report.data.partId = await addItem('part', 'Sidebar acceptance Part', options.workId);
    report.data.chapterId = await addItem('chapter', 'Sidebar acceptance Chapter', report.data.partId);
    report.data.pageId = await addItem('page', 'Sidebar acceptance Page', report.data.chapterId);
    await wait(() => window.__studioAuthoring.state().nodeId === report.data.pageId, 'new nested Page selected');
    click('[data-left-panel="contents"]');
    const tree = document.querySelector('#nav-tree').textContent;
    check(['Sidebar acceptance Part', 'Sidebar acceptance Chapter', 'Sidebar acceptance Page'].every(title => tree.includes(title)),
      'Contents shows the new organizational levels and Page in the actual project hierarchy.');

    report.progress = 'Filtering Elements and editing through left Properties';
    click('[data-panel="elements"]');
    await wait(() => !!document.querySelector('#element-search'), 'Elements search');
    setValue(document.querySelector('#element-search'), '', 'input');
    await wait(() => !!document.querySelector('[data-add-block="text"]') && !!document.querySelector('#element-type'), 'Elements and type filter');
    check(document.querySelector('#element-category')?.closest('label')?.querySelector('span')?.textContent.trim() === 'Type'
      && document.querySelector('#element-category')?.options[0]?.textContent.trim() === 'Type'
      && ![...document.querySelectorAll('.studio-library-filters label span')].some(label => /Use case/i.test(label.textContent)),
      'Elements replaces Use case and All use cases with the concise Type label and default option.');
    const typeSelect = document.querySelector('#element-type');
    const imageOption = [...typeSelect.options].find(option => /image/i.test(option.textContent));
    if (!imageOption) throw new Error('The Elements type filter does not include images.');
    setValue(typeSelect, imageOption.value);
    check(visible(document.querySelector('[data-add-block="image"]')) && !visible(document.querySelector('[data-add-block="text"]')),
      'The Elements type filter narrows the native objects to images without moving or replacing the canvas.');
    const currentTypeSelect = document.querySelector('#element-type');
    setValue(currentTypeSelect, [...currentTypeSelect.options].find(option => /all/i.test(option.textContent))?.value || 'all');
    setValue(document.querySelector('#element-search'), 'Text', 'input');
    await wait(() => visible(document.querySelector('[data-add-block="text"]')), 'native Text search');
    click('[data-add-block="text"]');
    await wait(() => !!document.querySelector('#library-add') && !document.querySelector('#library-add').disabled, 'selected Text Add action');
    const pageBefore = (await state()).pages[report.data.pageId].blocks.map(block => block.id);
    click('#library-add');
    await wait(async () => (await state()).pages[report.data.pageId].blocks.some(block => block.type === 'text' && !pageBefore.includes(block.id)), 'native text added');
    await wait(() => !window.__studioAuthoring.state().libraryBusy && !window.__studioAuthoring.state().libraryMutationPending, 'Text placement and display refresh complete');
    const pageWithText = (await state()).pages[report.data.pageId];
    await readyPage(report.data.pageId, pageWithText.blocks);
    click('[data-left-panel="inspector"]');
    await wait(() => !!document.querySelector('#properties-content [data-field="text"]'), 'actual Properties inspector moved to left');
    setValue(document.querySelector('#properties-content [data-field="text"]'), options.text, 'input');
    const authoredDocument = document.querySelector('#page-frame').contentDocument;
    const authoredField = document.querySelector('#properties-content [data-field="text"]');
    check(window.__studioAuthoring.state().dirty, 'The preview-preservation check begins with an actual unsaved authored Page draft.');
    await scope('section', header.title);
    await selectLibrary(header.id); await readyPreview(header.id);
    await dismissPreview();
    const restoredDraft = window.__studioAuthoring.state();
    report.data.draftPreviewPreservation = {
      sameDocument: document.querySelector('#page-frame').contentDocument === authoredDocument,
      samePropertiesControl: document.querySelector('#properties-content [data-field="text"]') === authoredField,
      preservedText: authoredField.value === options.text,
      retainedDraft: restoredDraft.hasDraft,
      persistedText: (await state()).pages[report.data.pageId].blocks.some(block => block.values.text === options.text)
    };
    check(document.querySelector('#page-frame').contentDocument === authoredDocument
      && document.querySelector('#properties-content [data-field="text"]') === authoredField && authoredField.value === options.text
      && (restoredDraft.hasDraft || (await state()).pages[report.data.pageId].blocks.some(block => block.values.text === options.text)),
      'Browsing and dismissing a real template preview preserves the current authored text, Properties control and Page document through draft autosave.');
    await flush();
    report.data.editedText = options.text;
    check((await state()).pages[report.data.pageId].blocks.some(block => block.values.text === options.text),
      'The relocated Properties panel edits actual persisted Page content using the existing writer tools.');
    await window.__studioAuthoring.navigate(report.data.pageId, 'sidebar-original-geometry-' + crypto.randomUUID());
    const sourcePage = (await state()).pages[report.data.pageId];
    const sourceTextBlock = sourcePage.blocks.find(block => block.values.text === options.text);
    await readyPage(sourcePage.id, sourcePage.blocks);
    report.data.originalTextGeometry = await textGeometry(sourceTextBlock.id);

    report.progress = 'Saving an authored Page as a reusable template';
    click('[data-left-panel="contents"]');
    click('#studio-save-page-template');
    await wait(() => document.querySelector('#dialog')?.open, 'save Page template dialog');
    setValue(document.querySelector('#dialog [name="title"]'), options.savedTemplateTitle, 'input');
    click('#dialog-submit');
    await wait(() => !document.querySelector('#dialog')?.open && replies.some(reply => reply.success && reply.result?.item?.title === options.savedTemplateTitle),
      'saved real reusable Page template', 180000);
    const savedItem = replies.find(reply => reply.success && reply.result?.item?.title === options.savedTemplateTitle).result.item;
    report.data.savedTemplateId = savedItem.id;
    check(savedItem.scope === 'page' && savedItem.id.startsWith('saved:'), 'Saving a Page creates a genuine reusable Page template in the project collection.');
    await scope('page', options.savedTemplateTitle);
    await selectLibrary(savedItem.id);
    const previousPages = Object.keys((await state()).pages);
    click('#library-add');
    await wait(() => document.querySelector('#dialog')?.open && !!document.querySelector('#dialog [name="parentId"]'), 'add saved Page dialog');
    setValue(document.querySelector('#dialog [name="title"]'), 'Independent saved template Page', 'input');
    setValue(document.querySelector('#dialog [name="parentId"]'), report.data.chapterId);
    click('#dialog-submit');
    await wait(async () => Object.keys((await state()).pages).some(id => !previousPages.includes(id)), 'instantiate saved Page template', 180000);
    await wait(() => !window.__studioAuthoring.state().libraryBusy && !window.__studioAuthoring.state().libraryMutationPending, 'saved Page placement and presentation complete');
    const cloneState = await state(), cloneId = Object.keys(cloneState.pages).find(id => !previousPages.includes(id));
    report.data.clonePageId = cloneId;
    const original = cloneState.pages[report.data.pageId], clone = cloneState.pages[cloneId];
    check(original.blocks.some(block => block.values.text === options.text) && clone.blocks.some(block => block.values.text === options.text)
      && clone.blocks.every(block => !original.blocks.some(source => source.id === block.id))
      && cloneState.nodes.find(node => node.id === cloneId).parentId === report.data.chapterId,
      'Adding the saved Page template creates independent block identities at the chosen parent while retaining the authored content.');
    const originalTextBlock = original.blocks.find(block => block.values.text === options.text);
    const clonedTextBlock = clone.blocks.find(block => block.values.text === options.text);
    check(clonedTextBlock.region === originalTextBlock.region,
      'The saved Page clone keeps its authored text in the original content region instead of adding an empty region before it.');
    await window.__studioAuthoring.navigate(cloneId, 'sidebar-clone-' + crypto.randomUUID());
    await readyPage(cloneId, clone.blocks);
    check(trustedFrameReady.pageId === cloneId && window.__studioAuthoring.state().nodeId === cloneId,
      'The clone inline edit starts only after its own trusted renderer Page and generation are ready, without editing the previous Page DOM.');
    report.data.cloneTextGeometry = await textGeometry(clonedTextBlock.id);
    check(Math.abs(report.data.cloneTextGeometry.top - report.data.originalTextGeometry.top) <= 3,
      'The saved Page clone preserves the text position within three CSS pixels of the original Page, without an extra empty content region.');
    const frame = document.querySelector('#page-frame'), field = frame.contentDocument.querySelector('[data-block-id] [data-field="text"][contenteditable="true"]');
    field.textContent = 'Only the saved template clone changes.';
    field.dispatchEvent(new frame.contentWindow.Event('input', { bubbles: true }));
    await flush();
    const independent = await state();
    check(independent.pages[report.data.pageId].blocks.some(block => block.values.text === options.text)
      && independent.pages[cloneId].blocks.some(block => block.values.text === 'Only the saved template clone changes.'),
      'Inline edits to a Page created from a saved template leave the original authored Page unchanged.');

    report.progress = 'Using the designated Work landing slot through actual creation and template actions';
    click('[data-left-panel="contents"]');
    click('#studio-landing');
    await wait(() => window.__studioAuthoring.state().nodeId === options.landingPageId && window.__studioAuthoring.state().currentGenerationReady,
      'the actual Landing page shortcut opens its designated editable canvas');
    const beforeLanding = await state(), workNode = beforeLanding.nodes.find(item => item.id === options.workId);
    const landingNode = beforeLanding.nodes.find(item => item.id === options.landingPageId);
    const firstTreePage = document.querySelector('#nav-tree > .nav-branch:first-child [data-landing-page]');
    check(workNode.appearance.workLandingPage === options.landingPageId && landingNode.parentId === options.workId
      && firstTreePage?.dataset.landingPage === options.landingPageId
      && firstTreePage.closest('.nav-row').style.getPropertyValue('--depth') === '0'
      && document.querySelectorAll('#nav-tree [data-navigate="' + CSS.escape(options.landingPageId) + '"]').length === 1,
      'The designated landing is the first root-level Contents entry, outside every Part and Chapter, with one actual tree row.');
    const landingPagesBefore = Object.keys(beforeLanding.pages);
    await window.__studioAuthoring.navigate(report.data.chapterId, 'sidebar-landing-parent-' + crypto.randomUUID());
    click('#studio-create-page');
    await wait(() => document.querySelector('#dialog')?.open && !!document.querySelector('#dialog [name="type"]'), 'New Page dialog on a nested Chapter');
    check([...document.querySelectorAll('#dialog [name="type"] option')].some(option => option.value === 'landing' && option.textContent.trim() === 'Landing'),
      'New Page offers the explicit Landing type even while the current location is inside a Chapter.');
    setValue(document.querySelector('#dialog [name="type"]'), 'landing');
    check(!visible(document.querySelector('#studio-page-template-chooser')) && visible(document.querySelector('#studio-landing-note')),
      'Landing creation explains its dedicated Work slot and does not apply a content-Page template by accident.');
    setValue(document.querySelector('#dialog [name="title"]'), 'Do not replace an authored landing', 'input');
    click('#dialog-submit');
    await wait(() => !document.querySelector('#dialog')?.open && !window.__studioAuthoring.state().libraryBusy
      && window.__studioAuthoring.state().nodeId === options.landingPageId && window.__studioAuthoring.state().currentGenerationReady,
      'explicit Landing creation reopens the one existing landing', 180000);
    const reusedLanding = await state();
    check(JSON.stringify(Object.keys(reusedLanding.pages)) === JSON.stringify(landingPagesBefore)
      && reusedLanding.nodes.find(item => item.id === options.landingPageId).parentId === options.workId
      && reusedLanding.nodes.find(item => item.id === options.landingPageId).title === landingNode.title
      && JSON.stringify(reusedLanding.pages[options.landingPageId].blocks) === JSON.stringify(beforeLanding.pages[options.landingPageId].blocks),
      'Creating Landing from a Chapter reuses its stable Page identity and preserves its authored title and blocks without creating a nested Page.');

    await scope('page', options.savedTemplateTitle);
    await selectLibrary(savedItem.id); await readyPreview(savedItem.id);
    const landingBlocksBefore = reusedLanding.pages[options.landingPageId].blocks.map(block => block.id);
    click('#library-add');
    check(!document.querySelector('#dialog')?.open, 'Adding a Page template while Landing is selected commits to that slot without a content-Page parent dialog.');
    await wait(async () => !window.__studioAuthoring.state().libraryBusy
      && (await state()).pages[options.landingPageId].blocks.some(block => block.values.text === options.text),
      'the saved authored template is placed into the existing landing', 180000);
    const populatedLanding = await state(), authoredLanding = populatedLanding.pages[options.landingPageId];
    check(window.__studioAuthoring.state().nodeId === options.landingPageId
      && JSON.stringify(Object.keys(populatedLanding.pages)) === JSON.stringify(landingPagesBefore)
      && landingBlocksBefore.every(id => authoredLanding.blocks.some(block => block.id === id)),
      'Page-template placement keeps the designated landing selected, preserves its existing blocks and does not create a content Page.');
    await readyPage(options.landingPageId, authoredLanding.blocks);
    await flush();
    const citationState = await state(), citedLanding = structuredClone(citationState.pages[options.landingPageId]);
    const citedText = citedLanding.blocks.find(block => block.values.text === options.text);
    const citation = { id: 'landing-author-source', title: 'Landing authored source', url: 'https://example.com/landing-author-source',
      retrievedAt: new Date().toISOString(), blockIds: [citedText.id], verified: false };
    citedLanding.sources.push(citation);
    const savedCitation = await fetch('/api/operations', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-HyperLib-Client': 'editor' },
      body: JSON.stringify({ expectedRevision: citationState.revision, description: 'Cite authored landing content',
        operations: [{ kind: 'savePage', id: options.landingPageId, page: citedLanding }] }) });
    if (!savedCitation.ok) throw new Error('The real landing citation writer failed: ' + await savedCitation.text());
    const citationSavedState = await savedCitation.json();
    const persistedCitation = citationSavedState.pages[options.landingPageId].sources.find(source => source.id === citation.id);
    check(await window.__studioAuthoring.refreshFoundation('sidebar-landing-citation-' + crypto.randomUUID()),
      'A real revision-checked landing citation is saved before the next template placement.');
    await readyPage(options.landingPageId, citedLanding.blocks);
    const landingTemplate = catalogue.find(item => item.id === 'page:hyperlib:atlas-work-landing-observatory');
    check(landingTemplate?.scope === 'page' && landingTemplate.category === 'Navigation' && landingTemplate.project === 'Atlas',
      'The landing collection retains its actual Atlas Page template with functional Navigation category and separate project attribution.');
    await scope('page', landingTemplate.title);
    await selectLibrary(landingTemplate.id); await readyPreview(landingTemplate.id);
    const pageBeforeLandingPreviewAdd = await state();
    check(pageBeforeLandingPreviewAdd.pages[options.landingPageId].sources.some(source => source.id === citation.id),
      'Automatically previewing a landing design leaves the existing authored source citation available.');
    click('#library-add'); document.querySelector('#library-add')?.click();
    await wait(async () => !window.__studioAuthoring.state().libraryBusy && !window.__studioAuthoring.state().libraryMutationPending
      && (await state()).pages[options.landingPageId].blocks.length > citedLanding.blocks.length,
      'the real Atlas landing template commits to the root landing', 180000);
    const completedLanding = await state(), landingAfter = completedLanding.pages[options.landingPageId];
    check(window.__studioAuthoring.state().nodeId === options.landingPageId
      && JSON.stringify(Object.keys(completedLanding.pages)) === JSON.stringify(landingPagesBefore)
      && citedLanding.blocks.every(block => landingAfter.blocks.some(kept => JSON.stringify(kept) === JSON.stringify(block)))
      && landingAfter.sources.some(source => JSON.stringify(source) === JSON.stringify(persistedCitation))
      && JSON.stringify(completedLanding.pages[report.data.pageId]) === JSON.stringify(pageBeforeLandingPreviewAdd.pages[report.data.pageId])
      && JSON.stringify(completedLanding.pages[cloneId]) === JSON.stringify(pageBeforeLandingPreviewAdd.pages[cloneId]),
      'Adding a landing template preserves every existing authored block and citation, both independent Chapter Pages, and the one designated landing ID.');
    await readyPage(options.landingPageId, landingAfter.blocks);
    const landingFrame = document.querySelector('#page-frame');
    await wait(() => !!landingFrame.contentDocument.querySelector('.wle[data-work-landing="true"]'), 'actual live Atlas landing contents component');
    const contentsPageIds = [...landingFrame.contentDocument.querySelectorAll('.wle-page-list [data-work-page]')].map(item => item.dataset.workPage).sort();
    const expectedPageIds = Object.keys(completedLanding.pages).filter(id => id !== options.landingPageId).sort();
    check(JSON.stringify(contentsPageIds) === JSON.stringify(expectedPageIds)
      && landingFrame.contentDocument.querySelector('.wle-wordmark')?.getAttribute('data-hyperlib-navigate') === options.landingPageId,
      'The committed landing lists the real Work Pages exactly once, excludes itself from the list, and links its brand navigation to the canonical landing.');
    report.data.landingPageId = options.landingPageId; report.data.landingTemplateId = landingTemplate.id;
    report.data.landingContentsPageIds = contentsPageIds; report.data.landingCitationId = citation.id;

    click('[data-left-panel="contents"]');
    click('.nav-home[data-navigate]');
    await wait(() => window.__studioAuthoring.state().nodeId === options.workId && visible(document.querySelector('#landing')),
      'the generic Work overview remains directly accessible');
    check(document.querySelector('#nav-tree > .nav-branch:first-child [data-landing-page]')?.dataset.landingPage === options.landingPageId,
      'The generic Work overview retains the special landing as its first Contents entry.');
    await window.__studioAuthoring.navigate(workNode.parentId, 'sidebar-work-card-' + crypto.randomUUID());
    const workCard = document.querySelector('.collection-card[href="#' + encodeURIComponent(options.landingPageId) + '"]');
    check(!!workCard && workCard.querySelector('.card-title')?.textContent.trim() === workNode.title,
      'The Work card opens its authored landing instead of the generated generic Work overview.');
    workCard.click();
    await wait(() => window.__studioAuthoring.state().nodeId === options.landingPageId && window.__studioAuthoring.state().currentGenerationReady,
      'clicking the actual Work card opens the designated landing');
    await window.__studioAuthoring.navigate(cloneId, 'sidebar-return-clone-' + crypto.randomUUID());
    await readyPage(cloneId, completedLanding.pages[cloneId].blocks);
    click('[data-panel="templates"]');
    await scope('page');
    auditTemplateRows([...catalogue, savedItem]);
    report.passed = true; report.progress = 'Complete';
  } catch (error) {
    report.failure = error.stack || error.message || String(error);
    const dialog = document.querySelector('#dialog'), frame = document.querySelector('#page-frame');
    report.failureDiagnostics = {
      state: window.__studioAuthoring?.state(), flushResults, observedStates,
      dialog: { open: dialog?.open, title: document.querySelector('#dialog-title')?.textContent,
        error: document.querySelector('#dialog-error')?.textContent, submitDisabled: document.querySelector('#dialog-submit')?.disabled,
        controls: [...(dialog?.querySelectorAll('input,select,textarea,button') || [])].map(control => ({ name: control.name, id: control.id, value: control.value, disabled: control.disabled, valid: control.checkValidity?.() })) },
      library: { status: document.querySelector('.studio-library-status')?.textContent,
        addDisabled: document.querySelector('#library-add')?.disabled, previewDisabled: document.querySelector('#library-preview')?.disabled,
        savePageDisabled: document.querySelector('#studio-save-page-template')?.disabled, pendingRequestIds: [...pending.keys()],
        replies: replies.map(reply => ({ requestId: reply.requestId, success: reply.success, error: reply.error, savedItem: reply.result?.item })) },
      inert: { main: document.querySelector('#main')?.inert, contents: document.querySelector('#studio-contents')?.inert, properties: document.querySelector('#properties-content')?.inert },
      frame: { src: frame?.src, pageId: frame?.dataset.pageId, generationId: frame?.dataset.generationId,
        readyState: frame?.contentDocument?.readyState, bodyClass: frame?.contentDocument?.body?.className,
        activeField: frame?.contentDocument?.activeElement?.getAttribute('data-field'), trustedReady: trustedFrameReady,
        trustedDocumentMatches: !!frame?.contentDocument && frame.contentDocument === trustedFrameDocument }
    };
  }
  finally {
    chrome.webview.removeEventListener('message', receive);
    window.removeEventListener('studio:flush-result', recordFlush);
    window.removeEventListener('studio:state', recordState);
    window.removeEventListener('message', recordFrameReady);
    report.done = true;
  }
})();
