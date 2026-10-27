window.hyperlibRenderer = (() => {
  let bridge, editing = false, pageId = '', generationId = '', selectedId = null, observer, inputTimer, pendingSelection;
  const lastSent = new WeakMap(), pendingFields = new Set();
  const send = (kind, more = {}) => window.parent.postMessage({source:'hyperlib-renderer',kind,pageId,generationId,...more},location.origin);
  const height = () => {
    const surface = document.querySelector('.hl-work-site') ?? document.body;
    send('height',{height:Math.ceil(surface.getBoundingClientRect().height)});
  };
  const fieldOf = target => target?.closest?.('[data-field][contenteditable="true"]');
  function flush(target) {
    const field = fieldOf(target), block = field?.closest('[data-block-id]');
    if (field && block && lastSent.get(field) !== field.textContent) {
      lastSent.set(field, field.textContent);
      send('change',{blockId:block.dataset.blockId,field:field.dataset.field,value:field.textContent});
    }
    if (field) pendingFields.delete(field);
  }
  function flushAll() {
    clearTimeout(inputTimer);
    [...pendingFields].forEach(flush);
    flush(document.activeElement);
  }
  function navigate(nodeId) {
    if (window.parent === window || typeof nodeId !== 'string'
        || !/^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$/.test(nodeId)) return false;
    flushAll(); send('navigate',{nodeId}); return true;
  }
  function select(id, notify = false) {
    selectedId = typeof id === 'string' ? id : null;
    document.querySelectorAll('[data-block-id]').forEach(block => {
      const selected = editing && block.dataset.blockId === selectedId;
      block.classList.toggle('hl-selected', selected);
      if (selected) block.setAttribute('data-selected', 'true'); else block.removeAttribute('data-selected');
    });
    if (notify) send('select',{blockId:selectedId});
  }
  window.addEventListener('message', async event => {
    if (event.origin !== location.origin || event.source !== window.parent || event.data?.source !== 'hyperlib-editor') return;
    const message = event.data;
    if (message.kind === 'flush') { flushAll(); send('flushed',{requestId:message.requestId}); }
    if (message.kind === 'refresh') {
      if ('blockId' in message) selectedId = message.blockId;
      if (bridge) { rememberSelection(); await bridge.invokeMethodAsync('Refresh'); } else location.reload();
    }
    if (message.kind === 'select' || message.kind === 'selection') { flushAll(); select(message.blockId); }
    if (message.kind === 'edit-state') {
      flushAll(); editing = !!message.editing;
      if ('blockId' in message) selectedId = message.blockId;
      decorate();
    }
  });
  document.addEventListener('click', event => {
    const contentsJump = event.target.closest?.('a[data-work-contents-jump]');
    if (contentsJump && !event.defaultPrevented && event.button === 0
        && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey
        && !contentsJump.hasAttribute?.('download')
        && (!contentsJump.target || contentsJump.target.toLowerCase() === '_self')) {
      const contents = document.getElementById('work-contents');
      if (contents) {
        event.preventDefault(); contents.scrollIntoView({block:'start',behavior:'instant'});
        try {
          if (window.parent !== window && window.parent.location.origin === location.origin && window.frameElement) {
            const headerBottom = Math.max(0, window.parent.document.querySelector('.topbar')?.getBoundingClientRect().bottom || 0);
            if (headerBottom > 0) {
              const targetTop = window.frameElement.getBoundingClientRect().top + contents.getBoundingClientRect().top;
              window.parent.scrollBy({top:targetTop - headerBottom - 12,behavior:'instant'});
            }
          }
        } catch { /* Cross-origin readers keep the normal in-document scroll. */ }
        return;
      }
    }
    const destination = event.target.closest?.('a[data-hyperlib-navigate]');
    if (destination && !event.defaultPrevented && event.button === 0
        && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey
        && !destination.hasAttribute?.('download')
        && (!destination.target || destination.target.toLowerCase() === '_self')
        && navigate(destination.dataset.hyperlibNavigate)) {
      event.preventDefault();
      return;
    }
    const block = event.target.closest?.('[data-block-id]');
    if (editing && block && !event.target.closest?.('[data-work-navigation]')) {
      flushAll(); select(block.dataset.blockId, true);
      if (event.target.closest('a')) event.preventDefault();
    }
  });
  document.addEventListener('input', event => {
    const field = fieldOf(event.target);
    if (!field) return;
    pendingFields.add(field); send('dirty');
    clearTimeout(inputTimer); inputTimer = setTimeout(flushAll,100);
  });
  document.addEventListener('paste', event => {
    const field = fieldOf(event.target), selection = window.getSelection();
    if (!field || !selection?.rangeCount) return;
    event.preventDefault();
    const range = selection.getRangeAt(0);
    if (!field.contains(range.commonAncestorContainer)) return;
    const text = document.createTextNode(event.clipboardData?.getData('text/plain') ?? '');
    range.deleteContents(); range.insertNode(text); range.setStartAfter(text); range.collapse(true);
    selection.removeAllRanges(); selection.addRange(range);
    field.dispatchEvent(new Event('input',{bubbles:true}));
  });
  document.addEventListener('focusout', event => flush(event.target));
  document.addEventListener('keydown', event => {
    if (!editing || !(event.ctrlKey || event.metaKey) || event.altKey || event.target.closest?.('input,textarea,select,[contenteditable="true"]')) return;
    const key = event.key.toLowerCase();
    if (key !== 'z' && key !== 'y') return;
    event.preventDefault(); flushAll(); send('history',{action:key === 'y' || event.shiftKey ? 'redo' : 'undo'});
  });
  window.addEventListener('pagehide',flushAll);
  function rememberSelection() {
    const field = fieldOf(document.activeElement), block = field?.closest('[data-block-id]'), selection = window.getSelection();
    flushAll();
    if (!field || !block || !selection?.rangeCount || !field.contains(selection.anchorNode)) return;
    const range = selection.getRangeAt(0).cloneRange();
    range.selectNodeContents(field); range.setEnd(selection.anchorNode,selection.anchorOffset);
    pendingSelection = {blockId:block.dataset.blockId,field:field.dataset.field,offset:range.toString().length};
  }
  function decorate() {
    document.body.classList.toggle('hl-editing',editing);
    document.querySelectorAll('[data-field]').forEach(field => {
      field.contentEditable = editing ? 'true' : 'false'; field.spellcheck = editing;
      if (!lastSent.has(field)) lastSent.set(field,field.textContent);
    });
    select(selectedId);
    if (!pendingSelection || !editing) return;
    const {blockId,field:fieldId,offset} = pendingSelection; pendingSelection = null;
    const block = [...document.querySelectorAll('[data-block-id]')].find(block => block.dataset.blockId === blockId);
    const field = block?.querySelector('[data-field="' + CSS.escape(fieldId) + '"]');
    if (!field) return;
    field.focus({preventScroll:true});
    const range = document.createRange(), walker = document.createTreeWalker(field,NodeFilter.SHOW_TEXT);
    let remaining = offset, node, restored = false;
    while (node = walker.nextNode()) {
      if (remaining <= node.length) { range.setStart(node,remaining); restored = true; break; }
      remaining -= node.length;
    }
    if (!restored) { range.selectNodeContents(field); range.collapse(false); } else range.collapse(true);
    const selection = window.getSelection(); selection.removeAllRanges(); selection.addRange(range);
  }
  return {
    navigate,
    connect(reference, edit, id, generation) {
      if (pageId && pageId !== id) { selectedId = null; pendingSelection = null; pendingFields.clear(); }
      bridge = reference; editing = edit; pageId = id; generationId = generation; decorate(); this.ready(generation, id);
    },
    ready(generation, id) {
      generationId = generation;
      if (typeof id === 'string') pageId = id;
      const surface = document.querySelector('.hl-document,.hl-browse');
      if (surface) {
        const styles = getComputedStyle(surface), paper = styles.getPropertyValue('--hl-paper');
        document.documentElement.style.background = paper; document.body.style.background = paper;
        document.documentElement.style.colorScheme = styles.colorScheme;
      }
      send('ready',{generation,blockId:selectedId});
      observer?.disconnect(); observer = new ResizeObserver(height); observer.observe(document.body); height();
    }
  };
})();
