const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Run the actual Work module's navigation functions without mounting its instruments.
const source = fs.readFileSync(path.join(__dirname, '../Works/atlas-climate/src/Pages/wwwroot/atlas.js'), 'utf8')
  .replace(/^import .*;\r?\n/gm, '').replace(/^export /gm, '')
  + '\nwindow.atlasNavigationTest = { navigate, toggleMenu, integrateWorkNavigation };';
const storage = new Map(), navigations = [], highlights = [], replacements = [];
let editing = false, acceptsNavigate = true;
const anchor = {
  classList: { add(value) { highlights.push(value); }, remove() {} },
  closest() { return null; }, hasAttribute() { return false; }, setAttribute() {}, removeAttribute() {},
  focus() {}, scrollIntoView() {}, getBoundingClientRect() { return { top: 200 }; }
};
const document = {
  body: { classList: { contains() { return editing; } } },
  baseURI: 'http://127.0.0.1:5277/_runtime/atlas-climate/generation/',
  getElementById(id) { return id === 'reference-1' ? anchor : id === 'atlas-chapter-menu' ? menu : null; }
};
const menu = { hidden: true };
const window = {
  frameElement: { getBoundingClientRect() { return { top: 0 }; } },
  parent: { location: { origin: 'http://127.0.0.1:5277' }, document: { querySelector() { return null; } }, scrollBy() {},
    postMessage() { throw new Error('Atlas must use the scoped runtime API instead of posting its old envelope.'); } },
  hyperlibRenderer: { navigate(id) { if (!acceptsNavigate) return false; navigations.push(id); return true; } },
  addEventListener() {}, setTimeout() { return 1; }, scrollY: 0, innerHeight: 700
};
const context = vm.createContext({ window, document,
  location: { origin: 'http://127.0.0.1:5277', pathname: '/_runtime/atlas-climate/generation/page/atlas-climate-introduction', hash: '' },
  sessionStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) },
  history: { state: {}, replaceState(state, title, url) { replacements.push(url); } },
  URL, Date, console, clearTimeout() {}, requestAnimationFrame() { return 1; }, cancelAnimationFrame() {} });
vm.runInContext(source, context);
const api = window.atlasNavigationTest;
function click(id, hash = '', extra = {}, inNavigation = true) {
  const link = { dataset: { atlasPage: id }, href: document.baseURI + 'page/' + id + hash,
    target: extra.linkTarget ?? '', hasAttribute(name) { return name === 'download' && !!extra.linkDownload; },
    closest() { return inNavigation ? {} : null; } };
  const event = { defaultPrevented: false, button: 0, target: { closest() { return link; } },
    preventDefault() { this.defaultPrevented = true; }, ...extra };
  api.navigate(event);
  return event;
}
assert.equal(click('atlas-climate-sources', '#reference-1').defaultPrevented, true);
assert.deepEqual(navigations, ['atlas-climate-sources']);
assert.equal(JSON.parse(storage.get('atlas-climate.pending-anchor')).anchor, 'reference-1',
  'Cross-page citation navigation must preserve Atlas anchor restoration');
assert.equal(click('atlas-climate-introduction', '#reference-1').defaultPrevented, true);
assert.equal(highlights.at(-1), 'atlas-citation-target');
assert.equal(navigations.length, 1, 'Same-page anchors must not create a parent navigation');
assert(replacements[0].endsWith('#reference-1'));
assert.equal(click('atlas-climate-sources', '', { ctrlKey: true }).defaultPrevented, false);
assert.equal(click('atlas-climate-sources', '', { button: 1 }).defaultPrevented, false);
assert.equal(click('atlas-climate-sources', '', { linkTarget: '_blank' }).defaultPrevented, false);
assert.equal(click('atlas-climate-sources', '', { linkDownload: true }).defaultPrevented, false);
acceptsNavigate = false;
assert.equal(click('atlas-climate-sources').defaultPrevented, false, 'An unavailable runtime bridge must retain the real href fallback');
acceptsNavigate = true;
editing = true;
assert.equal(click('atlas-climate-sources').defaultPrevented, true, 'Existing header navigation must work during editing');
assert.equal(click('atlas-climate-sources', '', {}, false).defaultPrevented, false,
  'Ordinary editable content links must retain their original editing behavior');
const button = { closest() { return {}; }, getAttribute() { return 'atlas-chapter-menu'; }, setAttribute(name, value) { this[name] = value; } };
api.toggleMenu({ target: { closest() { return button; } } });
assert.equal(menu.hidden, false, 'The existing Chapters control must stay usable during editing');
const parent = window.parent;
window.parent = window;
editing = false;
assert.equal(click('atlas-climate-sources').defaultPrevented, false, 'Standalone Atlas links must retain their real hrefs');
window.parent = parent;

function element(tag) {
  return { tag, dataset: {}, attributes: {}, children: [], className: '', textContent: '',
    setAttribute(name, value) { this.attributes[name] = value; },
    append(...children) { this.children.push(...children); },
    querySelector(selector) { return selector === '.atlas-library-return' ? this.children.find(child => child.className === 'atlas-library-return') ?? null : null; } };
}
document.createElement = element;
const brand = element('a'); brand.dataset.atlasPage = 'atlas-climate-introduction';
const actions = element('div');
const header = element('nav');
header.querySelector = selector => selector === '.atlas-brand' ? brand : selector === '.atlas-nav-links' ? actions : null;
const chapterMenu = element('section');
const pagination = element('nav');
const site = { dataset: { hyperlibWorkId: 'atlas-climate', hyperlibWorkHome: '/_runtime/atlas-climate/generation/browse/atlas-climate',
  hyperlibLibraryId: 'atlas-universalis', hyperlibLibraryHref: '/#atlas-universalis' } };
const section = { closest() { return site; }, querySelector() { return header; }, querySelectorAll() { return [chapterMenu, pagination]; } };
api.integrateWorkNavigation(section);
api.integrateWorkNavigation(section);
assert.equal(brand.dataset.hyperlibNavigate, 'atlas-climate');
assert.equal(brand.dataset.atlasPage, undefined);
assert.equal(brand.href, site.dataset.hyperlibWorkHome);
assert.equal(actions.children.length, 1, 'Rerendering must not duplicate the Library return link');
assert.equal(actions.children[0].dataset.hyperlibNavigate, 'atlas-universalis');
assert.equal(actions.children[0].href, '/#atlas-universalis');
assert.equal(actions.children[0].children[1].textContent, '← Library');
assert.equal(header.attributes['data-work-navigation'], '');
assert.equal(chapterMenu.attributes['data-work-navigation'], '');
assert.equal(pagination.attributes['data-work-navigation'], '');
editing = true;
const markedPagination = Object.hasOwn(pagination.attributes, 'data-work-navigation');
assert.equal(click('atlas-climate-earth-system', '', {}, markedPagination).defaultPrevented, true);
assert.equal(click('atlas-climate-ice-and-orbits', '', {}, markedPagination).defaultPrevented, true);
assert.deepEqual(navigations.slice(-2), ['atlas-climate-earth-system', 'atlas-climate-ice-and-orbits'],
  'Existing Previous/Next links must retain scoped navigation during editing');
console.log('ATLAS_NAVIGATION_OK scoped-runtime-api|citation-anchors|same-page-anchor|editing-header+pagination|standalone|work-home|library-return|idempotent-mount');
