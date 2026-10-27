// Portable counterpart of Atlas.Web's scroll header and Project Lab command panels.
// No fetch, global IDs, account handlers, or external resources.
const instances = new WeakMap();
export function mount(root) {
  if (instances.has(root)) return;
  const controller = new AbortController(), signal = controller.signal;
  const update = () => root.querySelectorAll('[data-atlas-header]').forEach(header => header.classList.toggle('is-scrolled', scrollY > 60));
  window.addEventListener('scroll', update, {passive:true, signal}); update();
  root.addEventListener('click', async event => {
    const button = event.target.closest('[data-atlas-copy]'); if (!button || !root.contains(button)) return;
    const container = button.closest('.command-panel,.project-card'), code = container?.querySelector('pre code'), status = container?.querySelector('[data-copy-status]');
    if (!code) return;
    try { await navigator.clipboard.writeText(code.textContent); if(status) status.textContent = 'Copied.'; }
    catch { const range = document.createRange(); range.selectNodeContents(code); const selection = getSelection(); selection.removeAllRanges(); selection.addRange(range); if(status) status.textContent = 'Command selected. Use your copy shortcut.'; }
  }, {signal});
  instances.set(root, controller);
}
export function dispose(root) { instances.get(root)?.abort(); instances.delete(root); }