/* Page-local element overrides against an actual browser DOM, without a gateway or shared project. */
const fs = require('fs'), path = require('path');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

(async () => {
  const repository = path.resolve(process.argv[2] || path.join(__dirname, '..'));
  const source = fs.readFileSync(path.join(repository, 'src/Hypershell/Services/StudioElementEditingRuntime.cs'), 'utf8');
  const marker = 'internal const string Script = """';
  const script = source.slice(source.indexOf(marker) + marker.length, source.lastIndexOf('""";'));
  const browser = await chromium.launch({ headless:true,
    executablePath:'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe' });
  try {
    const page = await browser.newPage();
    await page.setContent('<!doctype html><body class="hl-editing"><main class="hl-document"><section class="hl-region"><section class="hl-block hl-type-example hl-selected" data-block-id="saved-owner" data-block-type="example"><article data-vp-id="article"><figure data-vp-id="figure"><img data-vp-id="image" src="/media/original.png" alt="Original"><figcaption data-vp-id="caption" data-field="caption">Original caption</figcaption></figure><button data-vp-id="button">Run</button><div><span>Numeric fallback</span></div></article></section></section></main>');
    await page.addScriptTag({ content:script });
    await page.addScriptTag({ content:fs.readFileSync(path.join(repository, 'src/Hypershell/Assets/StudioHyperlibAuthoring/element-editor.js'), 'utf8') });
    const report = await page.evaluate(async () => {
      const checks = [], check = (value, message) => { if (!value) throw new Error(message); checks.push(message); };
      const api = window.hyperlibStudioElements, block = document.querySelector('[data-block-id]');
      api.decorate();
      check(api.path(block, block.querySelector('img')) === '@image', 'Visual element identities are stable anchors within their owning saved block.');
      check(api.resolve(block, '@figure') === block.querySelector('figure'), 'Stable anchors resolve the selected nested container.');
      check(api.path(block, block.querySelector('span')) === '0/2/0', 'Non-visual components use an element-only path fallback.');
      const image = { path:'@image', attrs:{ alt:'Edited image' }, styles:{ 'border-radius':'17px' } };
      api.apply(block, [image]);
      check(block.querySelector('img').alt === 'Edited image' && block.querySelector('img').style.borderRadius === '17px', 'Nested image attribute and appearance edits apply independently.');
      const figureCode = api.snapshot(block.querySelector('figure')).replace('Original caption', 'Code-edited caption');
      check(figureCode.includes('data-vp-id="image"') && figureCode.includes('data-field="caption"'), 'Code preserves stable child identities and editable field bindings.');
      const figure = { path:'@figure', html:figureCode };
      api.apply(block, [image, figure]);
      check(block.querySelector('figcaption').textContent === 'Code-edited caption', 'Container code updates the selected fragment.');
      check(block.querySelector('img').alt === 'Edited image' && block.querySelector('img').style.borderRadius === '17px', 'Child edits survive a parent Code override regardless of record order.');
      await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      check(block.querySelector('figcaption').textContent === 'Code-edited caption', 'The runtime observer preserves a live Code draft while the Page save is pending.');
      api.apply(block, [image]);
      check(block.querySelector('figcaption').textContent === 'Original caption' && block.querySelector('img').alt === 'Edited image', 'Clearing parent Code restores the original component while retaining child edits.');
      api.apply(block, []);
      check(block.querySelector('img').alt === 'Original' && !block.querySelector('img').style.borderRadius, 'Undo restores the original descendant attributes and styles.');
      const originalButton = block.querySelector('button'); let siblingClicks = 0;
      originalButton.addEventListener('click', () => siblingClicks++);
      api.apply(block, [figure]);
      originalButton.click();
      check(siblingClicks === 1 && block.querySelector('button') === originalButton, 'Editing one template fragment preserves sibling nodes and their existing behavior.');
      api.apply(block, []);
      const rootCode = api.snapshot(block).replace('Numeric fallback', 'Root Code content');
      const root = { path:'', html:rootCode };
      api.apply(block, [root]);
      check(block === document.querySelector('[data-block-id="saved-owner"]') && block.dataset.blockType === 'example', 'Root Code keeps the original saved wrapper and its ownership metadata.');
      check(block.classList.contains('hl-block') && block.classList.contains('hl-selected') && block.textContent.includes('Root Code content'), 'Root Code preserves renderer classes while updating content.');
      api.apply(block, []);
      check(block.textContent.includes('Numeric fallback') && block.querySelector('button') === originalButton, 'Undo restores the original root children with existing event bindings.');
      const action = { path:'@button', events:{ click:'this.dataset.invocations = String(Number(this.dataset.invocations || 0) + 1);' } };
      api.apply(block, [action]); block.querySelector('button').click();
      check(!block.querySelector('button').dataset.invocations, 'Authored event handlers do not execute while selecting elements in edit mode.');
      document.body.classList.remove('hl-editing'); block.querySelector('button').click();
      check(block.querySelector('button').dataset.invocations === '1', 'Authored event handlers execute on the published reader surface.');
      api.apply(block, []); block.querySelector('button').click();
      check(block.querySelector('button').dataset.invocations === '1', 'Clearing an event edit removes its listener.');
      const unsafe = ['<div></div><div></div>', '<script>alert(1)</script>', '<figure onclick="alert(1)"></figure>', '<img src="javascript:alert(1)">'];
      for (const value of unsafe) { let rejected = false; try { api.validateMarkup(value); } catch { rejected = true; } check(rejected, 'Invalid or executable fragment is rejected: ' + value.slice(0, 30)); }
      let mismatched = false; try { api.validateMarkup('<section></section>', 'figure'); } catch { mismatched = true; }
      check(mismatched, 'Code cannot replace the selected tag and invalidate its component identity.');
      check(!api.apply(block, '{bad json'), 'Malformed persisted edit JSON is ignored without disturbing component content.');
      const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      svg.innerHTML = '<path data-vp-id="vector-path" d="M0 0 L10 10"></path>'; block.append(svg);
      const vector = svg.firstElementChild;
      const vectorCode = api.validateMarkup('<path data-vp-id="vector-path" d="M2 3 L20 30"></path>', vector.tagName, vector.namespaceURI);
      api.apply(block, [{ path:'@vector-path', html:vectorCode }]);
      check(svg.firstElementChild instanceof SVGElement && svg.firstElementChild.namespaceURI === 'http://www.w3.org/2000/svg'
        && svg.firstElementChild.getAttribute('d') === 'M2 3 L20 30', 'Code edits preserve the namespace and geometry of a selected SVG descendant.');
      api.apply(block, []);
      check(svg.firstElementChild === vector && vector.getAttribute('d') === 'M0 0 L10 10', 'Undo restores the original SVG element and namespace.');
      const math = document.createElementNS('http://www.w3.org/1998/Math/MathML', 'math');
      math.innerHTML = '<mi data-vp-id="math-variable">x</mi>'; block.append(math);
      const variable = math.firstElementChild;
      api.apply(block, [{ path:'@math-variable', html:api.validateMarkup('<mi data-vp-id="math-variable">y</mi>', 'mi', variable.namespaceURI) }]);
      check(math.firstElementChild.namespaceURI === 'http://www.w3.org/1998/Math/MathML' && math.textContent === 'y', 'Code edits preserve MathML descendant namespaces.');
      api.apply(block, []);
      const table = document.createElement('table'); table.innerHTML = '<tbody><tr data-vp-id="table-row"><td>Original cell</td></tr></tbody>'; block.append(table);
      const rowCode = api.validateMarkup('<tr data-vp-id="table-row"><td>Edited cell</td></tr>', 'tr', 'http://www.w3.org/1999/xhtml');
      api.apply(block, [{ path:'@table-row', html:rowCode }]);
      check(table.querySelector('tr').tagName === 'TR' && table.querySelector('td').textContent === 'Edited cell', 'Code parses standalone table row fragments in the correct HTML context.');
      api.apply(block, []);
      svg.remove(); math.remove(); table.remove();
      block.setAttribute('data-studio-elements', JSON.stringify([image])); api.decorate();
      check(block.querySelector('img').alt === 'Edited image', 'Normal runtime metadata reapplies persisted edits without editor APIs.');
      const cleanCode = api.snapshot(block);
      check(!cleanCode.includes('data-block-id') && !cleanCode.includes('data-studio-elements') && !cleanCode.includes('hl-selected'), 'Code omits editor ownership, nested metadata, and selection chrome.');
      document.body.classList.add('hl-editing');
      api.apply(block, [figure]);
      const model = { id:'test-page', blocks:[{ id:'saved-owner', values:{ studioElements:JSON.stringify([figure]), caption:'Original typed field' } }] };
      let selected = 'saved-owner', staged = 0;
      const controller = window.createStudioElementEditor({
        escape:value => String(value), page:() => model, selected:() => selected, editing:() => true,
        selectOwner:id => { selected = id; }, render:() => {}, refreshHandles:() => {},
        stage:(id, value) => { model.blocks.find(item => item.id === id).values.studioElements = value; staged++; },
        save:async () => {}, toast:message => { throw new Error(message); }, codeDraft:() => {}, pendingCode:() => null, clearCode:() => {}
      });
      controller.attach({ contentDocument:document, contentWindow:window });
      const caption = block.querySelector('figcaption'); caption.contentEditable = 'true';
      caption.textContent = 'Inline after Code'; caption.dispatchEvent(new Event('input', { bubbles:true }));
      check(JSON.parse(model.blocks[0].values.studioElements)[0].html.includes('Inline after Code'), 'Inline typing updates the nearest existing Code override instead of losing text on save.');
      check(block.querySelector('figcaption') === caption && staged === 1, 'Inline Code synchronization keeps the active editable DOM node and stages one edit.');
      check(controller.consumeInlineChange('saved-owner', 'caption') && model.blocks[0].values.caption === 'Original typed field', 'Renderer field messages inside Code are consumed without writing unrelated typed source values.');
      const button = block.querySelector('button'); button.dataset.field = 'button-label';
      check(!controller.consumeInlineChange('saved-owner', 'button-label'), 'Original inline component fields outside Code keep their existing save route.');
      caption.dataset.field = 'custom-code-field'; caption.textContent = 'Code-only field'; caption.dispatchEvent(new Event('input', { bubbles:true }));
      check(controller.consumeInlineChange('saved-owner', 'custom-code-field')
        && !Object.hasOwn(model.blocks[0].values, 'custom-code-field')
        && JSON.parse(model.blocks[0].values.studioElements)[0].html.includes('Code-only field'),
        'Code-added editable fields persist as HTML without introducing undeclared schema values.');
      controller.destroy();
      return { result:'STUDIO_ELEMENT_RUNTIME_OK', checks };
    });
    if (process.argv[3]) fs.writeFileSync(path.resolve(process.argv[3]), JSON.stringify(report, null, 2));
    process.stdout.write(`${report.result}|checks=${report.checks.length}\n`);
  } finally { await browser.close(); }
})().catch(error => { process.stderr.write(String(error.stack || error)); process.exitCode = 1; });
