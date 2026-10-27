// Per-Page element edits retain the reusable component's source and bindings.
window.createStudioElementEditor = function (host) {
  let selection = null, frame = null, detach = null;
  const esc = host.escape;
  const api = () => frame?.contentWindow?.hyperlibStudioElements;
  const blocks = () => host.page()?.blocks || [];
  const outer = id => [...(frame?.contentDocument?.querySelectorAll('.hl-region > .hl-block[data-block-id]') || [])].find(e => e.dataset.blockId === id);
  function records(block) { try { const v = JSON.parse(block?.values?.studioElements || '[]'); return Array.isArray(v) ? v : []; } catch { return []; } }
  function context() {
    if (!api() || !host.page()) return null;
    const block = blocks().find(b => b.id === host.selected()), owner = block && outer(block.id);
    if (!owner) return null;
    if (!selection || selection.pageId !== host.page().id || selection.blockId !== block.id)
      selection = { pageId: host.page().id, blockId: block.id, path: host.pendingPath?.(block.id) || '' };
    const element = api().resolve(owner, selection.path);
    return { block, owner, element: element || owner, path: element ? selection.path : '' };
  }
  function mark() {
    const c = context(), doc = frame?.contentDocument;
    doc?.querySelectorAll('[data-studio-element-selected]').forEach(e => e.removeAttribute('data-studio-element-selected'));
    doc?.body.classList.toggle('studio-inner-selection', !!c?.path);
    c?.element.setAttribute('data-studio-element-selected', 'true');
    host.refreshHandles();
  }
  function select(blockId, path = '') {
    const owner = outer(blockId);
    if (!owner || !api()?.resolve(owner, path)) return false;
    selection = { pageId: host.page().id, blockId, path };
    host.selectOwner(blockId); mark(); host.render(); return true;
  }
  function write(c, list) {
    const value = JSON.stringify(list);
    if (value.length > 200000) throw new Error('This element has too much custom code. Use a smaller section.');
    host.stage(c.block.id, value);
    api().apply(c.owner, list); mark();
  }
  function patch(change, c = context()) {
    if (!c) return;
    const list = structuredClone(records(blocks().find(b => b.id === c.block.id) || c.block));
    let record = list.find(item => item.path === c.path);
    if (!record) { record = { path: c.path }; list.push(record); }
    for (const [key, value] of Object.entries(change)) {
      if (['attrs', 'styles', 'events'].includes(key)) record[key] = { ...record[key], ...value };
      else if (value === undefined) delete record[key]; else record[key] = value;
    }
    if (list.length > 1000) throw new Error('This Page section has too many element edits. Use another section.');
    write(c, list);
  }
  function owningSavedBlock(element) {
    let owner = element?.closest?.('[data-block-id]');
    while (owner && !blocks().some(block => block.id === owner.dataset.blockId)) owner = owner.parentElement?.closest('[data-block-id]');
    return owner;
  }
  function inlineOverride(element) {
    const owner = owningSavedBlock(element), block = owner && blocks().find(item => item.id === owner.dataset.blockId);
    if (!block) return null;
    const list = records(block);
    for (let ancestor = element; ancestor && owner.contains(ancestor); ancestor = ancestor.parentElement) {
      const match = list.find(record => typeof record.html === 'string' && api().resolve(owner, record.path) === ancestor);
      if (match) return { block, owner, element: ancestor, path: match.path, list };
      if (ancestor === owner) break;
    }
    return null;
  }
  function syncInline(element) {
    const c = inlineOverride(element);
    if (!c) return false;
    const html = api().snapshot(c.element), list = structuredClone(c.list), record = list.find(item => item.path === c.path);
    if (record.html === html) return true;
    record.html = html;
    const serialized = JSON.stringify(list);
    if (serialized.length > 200000) {
      // Keep the typed text recoverable in Code rather than letting a renderer
      // field message write an unrelated or undeclared source-schema field.
      host.codeDraft(codeKey(c), html);
      throw new Error('This element has too much custom code. Open Code to shorten it before saving.');
    }
    // The live editable node already contains this change. Reapplying the HTML
    // here would replace that node, interrupt typing, and discard its caret.
    host.stage(c.block.id, serialized);
    return true;
  }
  function consumeInlineChange(blockId, field) {
    const owner = outer(blockId);
    if (!owner || typeof field !== 'string') return false;
    let consumed = false;
    for (const element of [owner, ...owner.querySelectorAll('[data-field]')]) {
      if (element.dataset.field !== field || !inlineOverride(element)) continue;
      consumed = true;
      try { syncInline(element); } catch (error) { host.toast(error.message, true); }
    }
    return consumed;
  }
  function codeKey(c) { return `${c.block.id}:studio-code:${c.path}`; }
  function rgb(value) { const v = value.match(/\d+/g); return v?.length >= 3 ? '#' + v.slice(0, 3).map(n => Math.min(255, +n).toString(16).padStart(2, '0')).join('') : '#000000'; }
  const input = (id, title, value, extra = '') => `<label class="field"><span>${title}</span><input id="${id}" value="${esc(value ?? '')}" ${extra}></label>`;
  function heading(c) {
    const chain = [];
    for (let e = c.element; e && c.owner.contains(e); e = e.parentElement) {
      const path = api().path(c.owner, e);
      if (path !== null) chain.unshift(`<button type="button" class="quiet-button" data-element-path="${esc(path)}" aria-current="${e === c.element ? 'true' : 'false'}">${esc(e.tagName.toLowerCase())}</button>`);
      if (e === c.owner) break;
    }
    return `<div class="panel-toolbar"><strong class="block-name">${esc(c.element.tagName.toLowerCase())}${c.element.id ? ' #' + esc(c.element.id) : ''}</strong></div><nav class="studio-element-breadcrumb" aria-label="Selected element ancestry">${chain.join('<span aria-hidden="true">›</span>')}</nav>`;
  }
  function render(panel, mode) {
    const c = context();
    if (!c || (!c.path && !['code', 'actions'].includes(mode))) return false;
    panel.onfocusin = null;
    Object.assign(panel.dataset, { panel: mode, elementBlock: c.block.id, elementPath: c.path });
    panel.setAttribute('aria-label', ({ code: 'Code', actions: 'Actions', appearance: 'Appearance' })[mode] || 'Properties');
    let body = '';
    if (mode === 'code') {
      const html = host.pendingCode(codeKey(c)) ?? api().snapshot(c.element);
      body = `<label class="field"><span>HTML</span><textarea id="element-code" class="studio-element-code" spellcheck="false" aria-label="Selected element HTML">${esc(html)}</textarea></label><div class="studio-element-actions"><button type="button" class="primary-button" id="element-code-apply">Apply</button><button type="button" class="quiet-button" id="element-code-reset">Reset editor</button></div><p id="element-code-status" class="panel-intro" role="status">Edits apply to this element on this Page.</p>`;
    } else if (mode === 'actions') {
      const saved = records(c.block).find(item => item.path === c.path)?.events || {};
      const events = ['click', 'dblclick', 'change', 'input', 'submit', 'focus', 'blur', 'keydown', 'keyup', 'pointerenter', 'pointerleave'];
      body = `<label class="field"><span>Event</span><select id="element-event">${events.map(e => `<option value="${e}">${e}</option>`).join('')}</select></label><label class="field"><span>Handler</span><textarea id="element-handler" class="studio-element-code" spellcheck="false" aria-label="Event handler JavaScript">${esc(saved.click || '')}</textarea></label><div class="studio-element-actions"><button type="button" class="primary-button" id="element-event-apply">Apply</button><button type="button" class="quiet-button" id="element-event-remove">Remove</button></div><p class="panel-intro">JavaScript runs when viewing the Page. Use <code>event</code> and <code>element</code> in the handler.</p>`;
    } else if (mode === 'appearance') {
      const computed = frame.contentWindow.getComputedStyle(c.element);
      body = input('element-color', 'Text color', rgb(computed.color), 'type="color"') + input('element-background', 'Background', rgb(computed.backgroundColor), 'type="color"')
        + input('element-font-size', 'Font size (px)', parseFloat(computed.fontSize), 'type="number" min="1" max="500"')
        + input('element-style-name', 'CSS property', '', 'placeholder="border-radius"') + input('element-style-value', 'Value', '', 'placeholder="12px"')
        + '<div class="studio-element-actions"><button type="button" class="primary-button" id="element-style-apply">Apply</button><button type="button" class="quiet-button" id="element-style-remove">Remove</button></div>'
        + `<div class="studio-element-applied">${Object.entries(records(c.block).find(item => item.path === c.path)?.styles || {}).filter(([, v]) => v != null).map(([k, v]) => `<button type="button" class="quiet-button" data-element-style="${esc(k)}">${esc(k)}: ${esc(v)}</button>`).join('')}</div>`;
    } else {
      const attrs = ['id', 'class', 'title', ...(c.element.matches('img,video,audio,source,iframe') ? ['src'] : []), ...(c.element.matches('img') ? ['alt'] : []), ...(c.element.matches('a') ? ['href', 'target'] : [])];
      body = attrs.map(name => `<label class="field"><span>${esc(name)}</span><input data-element-attribute="${name}" value="${esc(c.element.getAttribute(name) || '')}"></label>`).join('');
      if (!c.element.children.length && !c.element.matches('img,video,audio,input,source,iframe,br,hr')) body += `<label class="field"><span>Text</span><textarea id="element-text">${esc(c.element.textContent)}</textarea></label>`;
      body += input('element-attribute-name', 'Attribute', '', 'placeholder="aria-label"') + input('element-attribute-value', 'Value', '')
        + '<div class="studio-element-actions"><button type="button" class="primary-button" id="element-attribute-apply">Apply</button><button type="button" class="quiet-button" id="element-attribute-remove">Remove</button></div>';
    }
    panel.innerHTML = heading(c) + body;
    const q = s => panel.querySelector(s);
    const safely = work => async () => { try { await work(); } catch (error) { host.toast(error.message, true); if (q('#element-code-status')) q('#element-code-status').textContent = error.message; } };
    panel.querySelectorAll('[data-element-path]').forEach(b => b.onclick = () => select(c.block.id, b.dataset.elementPath));
    if (mode === 'code') {
      const editor = q('#element-code'), key = codeKey(c);
      if (host.pendingCode(key) != null) q('#element-code-status').textContent = 'Unapplied code — choose Apply or Reset editor';
      editor.oninput = () => { host.codeDraft(key, editor.value); q('#element-code-status').textContent = 'Unapplied code'; };
      editor.onkeydown = e => { if (e.key === 'Tab') { e.preventDefault(); editor.setRangeText('  ', editor.selectionStart, editor.selectionEnd, 'end'); editor.dispatchEvent(new Event('input', { bubbles: true })); } };
      q('#element-code-apply').onclick = safely(async () => { const html = api().validateMarkup(editor.value, c.element.tagName, c.element.namespaceURI); patch({ html }, c); host.clearCode(key); await host.save(); host.render(); });
      q('#element-code-reset').onclick = () => { host.clearCode(key); host.render(); };
    } else if (mode === 'actions') {
      const saved = records(c.block).find(item => item.path === c.path)?.events || {};
      q('#element-event').onchange = () => q('#element-handler').value = saved[q('#element-event').value] || '';
      q('#element-event-apply').onclick = safely(async () => { const code = q('#element-handler').value; if (code.length > 20000) throw new Error('Keep this handler below 20,000 characters.'); new Function('event', 'element', code); patch({ events: { [q('#element-event').value]: code } }, c); await host.save(); });
      q('#element-event-remove').onclick = safely(async () => { patch({ events: { [q('#element-event').value]: null } }, c); await host.save(); q('#element-handler').value = ''; });
    } else if (mode === 'appearance') {
      for (const [id, prop] of [['element-color', 'color'], ['element-background', 'background-color'], ['element-font-size', 'font-size']]) q('#' + id).oninput = safely(() => patch({ styles: { [prop]: q('#' + id).value + (prop === 'font-size' ? 'px' : '') } }, c));
      const styleChange = remove => safely(async () => { const name = q('#element-style-name').value.trim(), value = q('#element-style-value').value.trim(); if (!/^--[a-z\d_-]+$|^[a-z][a-z\d-]*$/i.test(name)) throw new Error('Choose a CSS property.'); if (!remove && !frame.contentWindow.CSS.supports(name, value)) throw new Error('Enter a valid CSS value.'); patch({ styles: { [name]: remove ? null : value } }, c); await host.save(); host.render(); });
      q('#element-style-apply').onclick = styleChange(false); q('#element-style-remove').onclick = styleChange(true);
      panel.querySelectorAll('[data-element-style]').forEach(b => b.onclick = () => { q('#element-style-name').value = b.dataset.elementStyle; q('#element-style-value').value = records(c.block).find(item => item.path === c.path)?.styles?.[b.dataset.elementStyle] || ''; });
    } else {
      panel.querySelectorAll('[data-element-attribute]').forEach(e => e.onchange = safely(() => patch({ attrs: { [e.dataset.elementAttribute]: e.value } }, c)));
      const attrChange = remove => safely(async () => { const name = q('#element-attribute-name').value.trim(); if (!/^[a-z][a-z\d:_-]*$/i.test(name) || /^(on|data-studio-|data-block-|data-vp-id$|contenteditable$|style$)/i.test(name)) throw new Error('Use Appearance for styles and Actions for event handlers.'); patch({ attrs: { [name]: remove ? null : q('#element-attribute-value').value } }, c); await host.save(); });
      q('#element-attribute-apply').onclick = attrChange(false); q('#element-attribute-remove').onclick = attrChange(true);
      if (q('#element-text')) q('#element-text').oninput = safely(() => { const copy = c.element.cloneNode(false); copy.textContent = q('#element-text').value; patch({ html: api().validateMarkup(copy.outerHTML, c.element.tagName, c.element.namespaceURI) }, c); });
    }
    return true;
  }
  function attach(current) {
    detach?.(); frame = current;
    const doc = frame.contentDocument;
    if (!api() || !doc?.body) return;
    api().decorate();
    const style = doc.createElement('style'); style.dataset.studioElementTools = '';
    style.textContent = '.studio-inner-selection .hl-selected{outline:none!important;box-shadow:none!important}[data-studio-element-selected]{outline:1px solid var(--hl-accent,#a7d7c5)!important;outline-offset:2px}'; doc.head.append(style);
    const click = e => {
      if (!host.editing() || e.target.closest?.('[data-studio-canvas-overlay]')) return;
      const owner = owningSavedBlock(e.target);
      if (!owner) return;
      const path = api().path(owner, e.target);
      if (path !== null) select(owner.dataset.blockId, path);
      if (e.target.closest('a,button,input,select,textarea,form')) e.preventDefault();
    };
    const input = e => {
      if (!host.editing()) return;
      const field = e.target.closest?.('[data-field][contenteditable="true"]');
      if (!field) return;
      try { syncInline(field); } catch (error) { host.toast(error.message, true); }
    };
    doc.addEventListener('click', click, true);
    doc.addEventListener('input', input, true);
    detach = () => { doc.removeEventListener('click', click, true); doc.removeEventListener('input', input, true); style.remove(); };
    mark();
  }
  return { attach, render, select, context, patch, records, mark, consumeInlineChange,
    state: () => { const c = context(); return c ? { blockId: c.block.id, path: c.path } : null; },
    reset: () => { selection = null; }, destroy: () => { detach?.(); detach = null; frame = null; selection = null; } };
};
