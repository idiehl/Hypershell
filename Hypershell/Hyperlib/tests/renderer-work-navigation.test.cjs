const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Exercise the production document bridge without a gateway or a writable library.
const code = fs.readFileSync(path.join(__dirname, '../src/HyperLib.Runtime/wwwroot/renderer.js'), 'utf8');
function fixture(embedded = true) {
  const listeners = new Map(), messages = [], scrolls = [];
  const surface = { getBoundingClientRect: () => ({ height: 460 }) };
  const contents = { scrollIntoView(options) { scrolls.push(options); }, getBoundingClientRect: () => ({ top: 500 }) };
  const block = { dataset: { blockId: 'text-1' }, classList: { toggle() {} }, setAttribute() {}, removeAttribute() {} };
  const field = { textContent: 'Original text', dataset: { field: 'text' }, closest(selector) {
    if (selector.includes('[data-field]')) return this;
    if (selector === '[data-block-id]') return block;
    return null;
  }};
  const document = {
    activeElement: null,
    body: { classList: { toggle() {} }, style: {}, getBoundingClientRect: () => ({ height: 900 }) },
    documentElement: { style: {} },
    addEventListener(name, callback) { listeners.set(name, callback); },
    querySelector(selector) { return selector.includes('.hl-') ? surface : null; },
    getElementById(id) { return id === 'work-contents' ? contents : null; },
    querySelectorAll(selector) { return selector === '[data-field]' ? [field] : selector === '[data-block-id]' ? [block] : []; }
  };
  const window = {
    addEventListener(name, callback) { listeners.set(`window:${name}`, callback); },
    getSelection() { return null; }
  };
  window.parent = embedded ? { postMessage(message, origin) { messages.push({ message, origin }); } } : window;
  if (!embedded) window.postMessage = (message, origin) => messages.push({ message, origin });
  const context = vm.createContext({ window, document, location: { origin: 'http://127.0.0.1:5277' },
    getComputedStyle: () => ({ getPropertyValue: () => '#f5f3ed', colorScheme: 'light' }),
    ResizeObserver: class { observe() {} disconnect() {} },
    setTimeout, clearTimeout, CSS: { escape: value => value } });
  vm.runInContext(code, context);
  const click = (nodeId, more = {}, contentsJump = false) => {
    const link = { dataset: { hyperlibNavigate: nodeId }, target: more.linkTarget ?? '',
      hasAttribute(name) { return name === 'download' && !!more.linkDownload; } };
    const event = { defaultPrevented: false, button: 0, preventDefault() { this.defaultPrevented = true; },
      target: { closest(selector) {
        if (selector === 'a[data-work-contents-jump]') return contentsJump ? link : null;
        if (selector === 'a[data-hyperlib-navigate]') return contentsJump ? null : link;
        return null;
      } }, ...more };
    listeners.get('click')(event);
    return event;
  };
  return { window, listeners, messages, field, click, scrolls };
}

const embedded = fixture();
embedded.window.hyperlibRenderer.connect({}, true, 'current-page', 'generation-2');
assert(embedded.messages.every(({ message, origin }) => message.pageId === 'current-page'
  && message.generationId === 'generation-2' && origin === 'http://127.0.0.1:5277'));
assert.equal(embedded.messages.find(({ message }) => message.kind === 'height').message.height, 460,
  'Height must describe content rather than the larger iframe viewport');
embedded.messages.length = 0;
embedded.field.textContent = 'Unsent edit';
embedded.listeners.get('input')({ target: embedded.field });
assert.equal(embedded.click('next-page').defaultPrevented, true);
assert.deepEqual(embedded.messages.map(({ message }) => message.kind), ['dirty', 'change', 'navigate'],
  'Navigate must follow the final content change so the parent can save before changing routes');
assert.equal(embedded.messages[1].message.value, 'Unsent edit');
assert.equal(embedded.messages[2].message.nodeId, 'next-page');
assert(embedded.messages.every(({ message }) => message.pageId === 'current-page' && message.generationId === 'generation-2'));
embedded.messages.length = 0;
assert.equal(embedded.click('next-page', { ctrlKey: true }).defaultPrevented, false);
assert.equal(embedded.click('next-page', { metaKey: true }).defaultPrevented, false);
assert.equal(embedded.click('next-page', { button: 1 }).defaultPrevented, false);
assert.equal(embedded.click('next-page', { linkTarget: '_blank' }).defaultPrevented, false);
assert.equal(embedded.click('next-page', { linkTarget: '_parent' }).defaultPrevented, false);
assert.equal(embedded.click('next-page', { linkDownload: true }).defaultPrevented, false);
assert.equal(embedded.click('bad/id').defaultPrevented, false, 'An invalid managed ID must retain its valid href fallback');
assert.equal(embedded.messages.length, 0, 'Modified and middle clicks must retain native link behavior');
assert.equal(embedded.click('next-page', { linkTarget: '_self' }).defaultPrevented, true);
assert.equal(embedded.messages[0].message.nodeId, 'next-page');
embedded.messages.length = 0;
assert.equal(embedded.click(null, {}, true).defaultPrevented, true);
assert.equal(embedded.scrolls.length, 1, 'View contents must scroll within the existing document');
assert.equal(embedded.messages.length, 0, 'View contents must not navigate or create a parent history entry');
assert.equal(embedded.click(null, { ctrlKey: true }, true).defaultPrevented, false,
  'A modified contents click must follow its concrete current-route fallback href');
assert.equal(embedded.click(null, { linkTarget: '_blank' }, true).defaultPrevented, false);
assert.equal(embedded.click(null, { linkDownload: true }, true).defaultPrevented, false);
embedded.field.textContent = 'Edit before custom navigation';
embedded.listeners.get('input')({ target: embedded.field });
assert.equal(embedded.window.hyperlibRenderer.navigate('custom-page'), true);
assert.deepEqual(embedded.messages.map(({ message }) => message.kind), ['dirty', 'change', 'navigate'],
  'Custom Work navigation must use the same edit-flush and scoped message boundary');
assert.equal(embedded.messages[2].message.generationId, 'generation-2');
assert.equal(embedded.messages[2].message.pageId, 'current-page');
assert.equal(embedded.window.hyperlibRenderer.navigate('bad/id'), false);
embedded.messages.length = 0;
embedded.window.parent.location = { origin: 'http://127.0.0.1:5277' };
embedded.window.parent.document = { querySelector: () => ({ getBoundingClientRect: () => ({ bottom: 40 }) }) };
const parentScrolls = [];
embedded.window.parent.scrollBy = options => parentScrolls.push(options);
embedded.window.frameElement = { getBoundingClientRect: () => ({ top: -500 }) };
embedded.click(null, {}, true);
assert.equal(parentScrolls[0].top, -52, 'Browser contents must remain visible beneath its 40px sticky header');
embedded.window.parent.document.querySelector = () => ({ getBoundingClientRect: () => ({ bottom: 0 }) });
embedded.click(null, {}, true);
assert.equal(parentScrolls.length, 1, 'A native window with no web header must retain its zero scroll offset');
embedded.window.hyperlibRenderer.ready('generation-3', 'work-home');
assert(embedded.messages.every(({ message }) => message.pageId === 'work-home' && message.generationId === 'generation-3'),
  'Landing routes must announce their own route and generation');

const standalone = fixture(false);
standalone.window.hyperlibRenderer.ready('standalone', 'work-home');
standalone.messages.length = 0;
assert.equal(standalone.click('next-page').defaultPrevented, false,
  'A standalone reader must follow real hrefs when no parent host exists');
assert.equal(standalone.messages.length, 0);
console.log('HYPERLIB_RENDERER_NAVIGATION_OK embedded|standalone|edit-flush-order|route+generation-scope|content-height|modified-links|contents-scroll');
