// Native header integration uses in-memory DOM, history, and save fixtures.
const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { randomUUID } = require("node:crypto");
const wwwroot = path.join(__dirname, "../src/HyperLib.Host/wwwroot");
const source = fs.readFileSync(path.join(wwwroot, "app.js"), "utf8");

function fixture({ native = false, child = false } = {}) {
  const elements = new Map(), handlers = new Map(), messages = [], storage = new Map();
  const classes = () => {
    const values = new Set();
    return { add: (name) => values.add(name), remove: (name) => values.delete(name),
      contains: (name) => values.has(name), toggle(name, on) { on ? values.add(name) : values.delete(name); } };
  };
  function element(id) {
    if (!elements.has(id)) elements.set(id, {
      dataset: {}, hidden: false, disabled: false, attributes: new Map(), classList: classes(),
      style: { removeProperty() {}, setProperty() {} },
      setAttribute(name, value) { this.attributes.set(name, String(value)); },
      addEventListener() {}, replaceChildren() {}, focus() {}, append() {},
      querySelectorAll: () => [], getBoundingClientRect: () => ({ width: 1000 }),
    });
    return elements.get(id);
  }
  const history = { state: null, visits: [],
    replaceState(state, title, url) { this.state = state; if (url) this.url = url; },
    go(delta) { this.visits.push(delta); },
  };
  const window = { __hyperlibNativeChrome: native,
    chrome: { webview: { postMessage: (message) => messages.push(message), addEventListener() {} } },
    addEventListener: (name, callback) => handlers.set(name, callback), scrollTo() {},
  };
  window.top = child ? {} : window;
  const document = { querySelector: element, querySelectorAll: () => [], addEventListener() {},
    body: element("body"), documentElement: element("html"), createTextNode: (text) => text,
    createElement: () => element("created"),
  };
  const storageApi = { getItem: (key) => storage.get(key) ?? null,
    setItem: (key, value) => storage.set(key, value), removeItem: (key) => storage.delete(key) };
  const context = vm.createContext({ console, structuredClone, URL, FormData, Blob,
    crypto: { randomUUID }, setTimeout, clearTimeout, setInterval() {},
    requestAnimationFrame: (fn) => fn(), matchMedia: () => ({ matches: false }),
    location: { origin: "http://127.0.0.1:12345", hash: "#library" },
    sessionStorage: storageApi, localStorage: storageApi, history, window, document,
  });
  vm.runInContext(source.slice(0, source.lastIndexOf("\nstart();")), context);
  return { context, elements, handlers, messages, history, window, document,
    run: (js) => vm.runInContext(js, context),
    dispose() { vm.runInContext("clearTimeout(app.timer); clearTimeout(toastTimer)", context); } };
}

test("native marker hides the web header before first paint only in the top frame", () => {
  const html = fs.readFileSync(path.join(wwwroot, "index.html"), "utf8");
  const markerScript = html.match(/<script>([\s\S]*?)<\/script>/)[1];
  assert.ok(html.indexOf("hyperlib-native-chrome") < html.indexOf('rel="stylesheet"'));
  for (const config of [{ native: true }, { native: false }, { native: true, child: true }]) {
    const f = fixture(config);
    try {
      vm.runInContext(markerScript, f.context);
      assert.equal(f.document.documentElement.classList.contains("hyperlib-native-chrome"), config.native && !config.child);
    } finally { f.dispose(); }
  }
});

test("native toggle waits for a flush before changing mode and acknowledges the result", async () => {
  const f = fixture({ native: true });
  try {
    f.run("app.chromeReady = true; render = async () => {}; globalThis.finishFlush = null; flushDraft = () => new Promise(resolve => finishFlush = resolve)");
    const transition = f.run("setEditMode(true)");
    assert.equal(f.run("app.edit"), false);
    assert.equal(f.elements.get("#edit-toggle").disabled, true);
    assert.deepEqual(JSON.parse(JSON.stringify(f.messages.at(-1))), { type: "hyperlib:chrome-state", editing: false, canEdit: false });
    f.run("finishFlush()");
    await transition;
    assert.equal(f.document.body.classList.contains("editing"), true);
    assert.equal(f.elements.get("#edit-toggle").attributes.get("aria-pressed"), "true");
    assert.deepEqual(JSON.parse(JSON.stringify(f.messages.at(-1))), { type: "hyperlib:chrome-state", editing: true, canEdit: true });
  } finally { f.dispose(); }
});

test("save conflict keeps the current editing mode and restores the native toggle", async () => {
  const f = fixture({ native: true });
  try {
    f.run("app.chromeReady = true; app.edit = true; flushDraft = async () => { app.draft = { id: 'unsaved' }; }; render = async () => { throw new Error('Mode should stay unchanged'); }");
    await f.run("setEditMode(false)");
    assert.equal(f.run("app.edit"), true);
    assert.equal(f.run("app.draft.id"), "unsaved");
    assert.deepEqual(JSON.parse(JSON.stringify(f.messages.at(-1))), { type: "hyperlib:chrome-state", editing: true, canEdit: true });
  } finally { f.dispose(); }
});

test("opening failure reports authoring unavailable and ignores toggle requests", async () => {
  const f = fixture({ native: true });
  try {
    f.run("loadState = async () => { throw new Error('Library unavailable'); }");
    await f.run("start()");
    assert.match(f.elements.get("#landing").innerHTML, /Library unavailable/);
    assert.deepEqual(JSON.parse(JSON.stringify(f.messages.at(-1))), { type: "hyperlib:chrome-state", editing: false, canEdit: false });
    await f.run("setEditMode(true)");
    assert.equal(f.run("app.edit"), false);
    assert.equal(f.elements.get("#edit-toggle").disabled, true);
  } finally { f.dispose(); }
});

test("late native marker synchronizes state; ordinary and nested frames reject native mode events", () => {
  for (const config of [{ native: true }, { native: false }, { native: true, child: true }]) {
    const f = fixture(config);
    try {
      f.run("app.chromeReady = true; globalThis.modeRequests = []; setEditMode = enabled => modeRequests.push(enabled)");
      f.handlers.get("hyperlib:native-chrome")();
      f.handlers.get("hyperlib:edit-mode")({ detail: { enabled: true } });
      f.handlers.get("hyperlib:edit-mode")({ detail: { enabled: "true" } });
      const allowed = config.native && !config.child;
      assert.equal(f.document.documentElement.classList.contains("hyperlib-native-chrome"), allowed);
      assert.equal(f.messages.length, allowed ? 1 : 0);
      assert.equal(f.run("modeRequests.length"), allowed ? 1 : 0);
    } finally { f.dispose(); }
  }
});

test("browser history buttons follow visits, Back/Forward, branching, and reload", () => {
  const f = fixture();
  try {
    f.run("initializeBrowserHistory()");
    const initial = structuredClone(f.history.state);
    assert.equal(f.elements.get("#history-back").disabled, true);
    assert.equal(f.elements.get("#history-forward").disabled, true);
    f.history.state = null;
    f.run("synchronizeBrowserHistory()");
    const second = structuredClone(f.history.state);
    f.history.state = null;
    f.run("synchronizeBrowserHistory()");
    assert.equal(f.elements.get("#history-back").disabled, false);
    f.history.state = second;
    f.handlers.get("popstate")();
    assert.equal(f.elements.get("#history-forward").disabled, false);
    f.run("initializeBrowserHistory()");
    assert.equal(f.elements.get("#history-forward").disabled, false);
    f.history.state = initial;
    f.handlers.get("popstate")();
    assert.equal(f.elements.get("#history-back").disabled, true);
    assert.equal(f.elements.get("#history-forward").disabled, false);
    f.history.state = null;
    f.run("synchronizeBrowserHistory()");
    assert.equal(f.elements.get("#history-forward").disabled, true);
  } finally { f.dispose(); }
});

test("standalone native header starts immediately without a Studio capabilities request", async () => {
  const f=fixture({native:true});
  try{
    f.run(`globalThis.capabilityCalls=[];
      native=async method=>{capabilityCalls.push(method);return {canOpenStudio:true}};
      loadState=async()=>{app.state={libraryId:'library',nodes:[{id:'library',type:'library'}]}};
      render=async()=>{};
      globalThis.EventSource=class{addEventListener(){}};
    `);
    await f.run("start()");
    assert.equal(f.run("capabilityCalls.length"),0);
    assert.equal(f.run("app.chromeReady"),true);
    assert.equal(f.elements.get("#edit-toggle").disabled,false);
  }finally{f.dispose();}
});
