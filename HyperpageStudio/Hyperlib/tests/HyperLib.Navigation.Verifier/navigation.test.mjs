import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const source = fs.readFileSync(new URL('../../src/HyperLib.Host/wwwroot/app.js', import.meta.url), 'utf8');
const storage = new Map(), tree = { innerHTML: '' }, count = { textContent: '' }, focused = [];
const document = {
  documentElement: { style: { setProperty() {} } },
  querySelector(selector) { return selector === '#nav-tree' ? tree : selector === '#library-count' ? count : null; },
  querySelectorAll(selector) { return selector === '.nav-home' ? [{ classList: { toggle() {} } }] : []; },
};
tree.querySelectorAll = (selector) => selector === '[data-nav-toggle]'
  ? [...tree.innerHTML.matchAll(/data-nav-toggle="([^"]+)"/g)].map(match => ({ dataset: { navToggle: match[1] }, focus() { focused.push(match[1]); } })) : [];
const context = vm.createContext({ document, window: { addEventListener() {} }, location: { origin: 'http://localhost' },
  localStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) },
  fetch() { throw new Error('Navigation disclosure must not fetch or load a Page.'); }, setTimeout, clearTimeout, URL, console,
});
vm.runInContext(source.slice(0, source.indexOf('function setRuntimeNotice')) + '\nglobalThis.navTest={app,renderNavigation,toggleNavigationBranch};', context);
const { app, renderNavigation, toggleNavigationBranch } = context.navTest;
const nodes = [{ id: 'core', type: 'collection', title: 'Core Template Library', parentId: 'library', order: 0 },
  { id: 'demos', type: 'collection', title: 'Demo Works', parentId: 'library', order: 1 }];
const pages = {};
for (let category = 0; category < 19; category++) {
  nodes.push({ id: `category-${category}`, type: 'work', title: `Category ${category}`, parentId: 'core', order: category });
  for (let entry = 0; entry < 26; entry++) {
    const id = `entry-${category}-${entry}`;
    nodes.push({ id, type: 'page', title: `Entry ${entry}`, parentId: `category-${category}`, workId: `category-${category}`, order: entry });
    Object.defineProperty(pages, id, { enumerable: true, get() { throw new Error('Collapsed navigation must never read Page payloads.'); } });
  }
}
nodes.push({ id: 'demo-work', type: 'work', title: 'Demonstration', parentId: 'demos', order: 0 });
app.state = { libraryId: 'library', nodes, pages, appearance: {} };
const visible = () => [...tree.innerHTML.matchAll(/data-navigate="([^"]+)"/g)].map(match => match[1]);
let assertions = 0;
function check(condition, description) { assert.ok(condition, description); assertions++; }
renderNavigation();
assert.deepEqual(visible(), ['core', 'demos']); assertions++;
check(!tree.innerHTML.includes('entry-') && tree.innerHTML.includes('aria-expanded="false"'), 'A 494-Page library initially renders only the two collection rows.');
toggleNavigationBranch('core');
check(visible().length === 21 && !tree.innerHTML.includes('entry-'), 'Opening the Core Library reveals only its 19 categories, without loading Page content.');
check(focused.at(-1) === 'core', 'The branch toggle retains keyboard focus after rerender.');
renderNavigation();
check(visible().length === 21, 'Explicit expansion survives ordinary rerenders.');
app.navLibrary = null; app.navBranches = new Map(); renderNavigation();
check(visible().length === 21, 'Explicit expansion is restored from this library’s saved preferences.');
toggleNavigationBranch('core');
app.navLibrary = null; renderNavigation();
check(visible().length === 2, 'Explicit collapse also survives reload.');
app.id = 'entry-7-12'; renderNavigation();
check(visible().length === 47 && visible().includes('entry-7-12') && !visible().includes('entry-8-0'), 'Navigating directly to a Page reveals its ancestry and only that category’s Pages.');
check(tree.innerHTML.includes('data-navigate="entry-7-12" aria-current="page"'), 'The selected Page has an accessible current-location marker.');
toggleNavigationBranch('category-7'); renderNavigation();
check(!visible().includes('entry-7-12'), 'A user can collapse the current category without navigation immediately reopening it.');
app.id = 'entry-7-13'; renderNavigation();
check(visible().includes('entry-7-13'), 'Navigating to a new Page reveals a previously collapsed ancestry.');
toggleNavigationBranch('category-8');
check(visible().includes('entry-8-0') && visible().includes('entry-7-13'), 'A second category can be deliberately opened independently.');
toggleNavigationBranch('entry-7-13');
check(!app.navBranches.has('entry-7-13'), 'Leaf Pages have no branch behavior.');
app.state = { ...app.state, libraryId: 'other-library', nodes: nodes.map(node => ({ ...node, parentId: node.parentId === 'library' ? 'other-library' : node.parentId })) };
app.id = 'library'; renderNavigation();
check(visible().length === 2, 'Expansion choices do not leak into another library.');
storage.set('hyperlib.navigation.other-library', 'broken'); app.navLibrary = null; renderNavigation();
check(visible().length === 2, 'Invalid stored preferences do not prevent browsing.');
const clickHandler = source.slice(source.indexOf('document.addEventListener("click", (e) => {'), source.indexOf('window.addEventListener("beforeunload"'));
document.addEventListener = (type, handler) => { if (type === 'click') context.click = handler; };
vm.runInContext('globalThis.navigationCalls = 0; navigate = () => navigationCalls++;\n' + clickHandler, context);
context.click({ target: { closest: selector => selector === '[data-nav-toggle]' ? { dataset: { navToggle: 'core' } }
  : selector === '[data-navigate]' ? { dataset: { navigate: 'core' } } : null } });
check(context.navigationCalls === 0 && visible().length === 21, 'Clicking a disclosure expands its branch without navigating or loading the destination.');
context.click({ target: { closest: selector => selector === '[data-navigate]' ? { dataset: { navigate: 'category-7' } } : null } });
check(context.navigationCalls === 1, 'The adjacent Work navigation button still uses the existing navigation handler.');
console.log(`HYPERLIB_NAVIGATION_OK assertions=${assertions} fixturePages=494 rootRows=2`);
