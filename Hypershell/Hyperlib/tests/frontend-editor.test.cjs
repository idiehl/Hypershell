// Built-in Node tests; all renderer/API traffic uses local in-memory fixtures.
const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { randomUUID } = require("node:crypto");
const wwwroot = path.join(__dirname, "../src/HyperLib.Host/wwwroot");
const source = fs.readFileSync(path.join(wwwroot, "app.js"), "utf8");

function fixture() {
  const elements = new Map();
  const handlers = new Map();
  const calls = [];
  const element = (id) => {
    if (!elements.has(id)) elements.set(id, {
      id: id.slice(1), dataset: {}, hidden: false, style: {},
      classList: { add() {}, remove() {}, toggle() {}, contains: () => false },
      addEventListener() {}, replaceChildren() {}, focus() {}, append() {},
      querySelectorAll: () => [],
    });
    return elements.get(id);
  };
  const frame = element("#page-frame");
  frame.dataset.pageId = "p";
  const context = vm.createContext({
    console, structuredClone, URL, FormData, Blob, crypto: { randomUUID },
    setTimeout, clearTimeout, setInterval() {},
    requestAnimationFrame: (fn) => fn(),
    location: { origin: "http://127.0.0.1:12345", hash: "#p" },
    history: { replaceState() {} },
    matchMedia: () => ({ matches: false }),
    document: {
      querySelector: element,
      querySelectorAll: (selector) => selector === "iframe" ? [frame] : [],
      addEventListener() {},
      createTextNode: (text) => text,
      createElement: () => element("#created"),
      body: element("#body"),
    },
    window: {
      addEventListener: (name, callback) => handlers.set(name, callback),
      scrollTo() {},
    },
  });
  const emit = (message, overrides = {}) => handlers.get("message")({
    origin: "http://127.0.0.1:12345", source: frame.contentWindow,
    data: { source: "hyperlib-renderer", pageId: "p", ...message }, ...overrides,
  });
  let pending = null;
  frame.contentWindow = { postMessage(message) {
    calls.push(message);
    if (message.kind === "flush") queueMicrotask(() => {
      if (pending !== null) {
        const value = pending; pending = null;
        emit({ kind: "change", blockId: "b", field: "text", value });
      }
      emit({ kind: "flushed", requestId: message.requestId });
    });
  } };
  // Evaluate production functions/listeners, omitting only network startup/lease.
  vm.runInContext(source.slice(0, source.lastIndexOf("\nstart();")), context);
  vm.runInContext(`
    app.state = { libraryId: 'library', revision: 1, labels: {}, nodes: [
      {id:'library',type:'library'}, {id:'w',type:'work',parentId:'library'},
      {id:'p',type:'page',parentId:'w',workId:'w'}], pages: {
      p: {id:'p',title:'Page',workId:'w',revision:1,blocks:[{id:'b',type:'text',values:{text:'saved'},region:'main'}],sources:[]}}};
    app.id = 'p'; app.edit = true; app.frameReady = true; app.selected = 'b';
    globalThis.saved = [];
    mutate = async (operations) => {
      const p = structuredClone(operations[0].page);
      saved.push(structuredClone(p));
      p.revision++;
      app.state.pages[p.id] = p; app.state.revision++;
      if (globalThis.duringSave) { const action = globalThis.duringSave; globalThis.duringSave = null; action(); }
      return app.state;
    };
  `, context);
  return { context, frame, calls, emit, elements,
    run: (js) => vm.runInContext(js, context),
    type(value) { pending = value; emit({ kind: "dirty" }); },
    dispose() { vm.runInContext("clearTimeout(app.timer); clearTimeout(toastTimer)", context); },
  };
}

test("history controls occupy the top action bar; page has no add toolbar", () => {
  const html = fs.readFileSync(path.join(wwwroot, "index.html"), "utf8");
  const actions = html.slice(html.indexOf('class="view-actions"'), html.indexOf('id="runtime-notice"'));
  assert.match(actions, /id="undo-button"/);
  assert.match(actions, /id="redo-button"/);
  assert.doesNotMatch(html + source, /add-block-button|page-toolbar|landing-undo|landing-redo/);
});

test("chooser safely renders all supplied designs and preserves selected ID", () => {
  const f = fixture();
  try {
    const catalog = Array.from({ length: 17 }, (_, i) => ({ id: `design-${i}`, title: `Design ${i}`, description: '<img src=x onerror=evil()>', category: 'Any "subject"', background: '#fafafa', accent: '#123456', ink: '#111111' }));
    f.context.catalog = catalog;
    const markup = f.run("templateChoices(catalog, 'design-8')");
    assert.equal((markup.match(/class="template-option"/g) || []).length, 17);
    assert.equal((markup.match(/name="starterId"/g) || []).length, 17);
    assert.match(markup, /value="design-8" checked/);
    assert.doesNotMatch(markup, /<img|onerror="/);
    assert.match(markup, /&lt;img src=x onerror=evil\(\)&gt;/);
    assert.match(markup, /--preview-bg:#fafafa/);
    f.context.catalog[0].background = 'red;position:fixed';
    assert.doesNotMatch(f.run("templatePreview(catalog[0])"), /position:fixed/);
  } finally { f.dispose(); }
});

test("optional schema style values inherit cleanly and color reset removes override", () => {
  const f = fixture();
  try {
    const markup = f.run("fieldControl({id:'span',title:'Width',type:'select',options:['full','half','third','two-thirds']},undefined)");
    assert.match(markup, /Template default/);
    assert.match(markup, /value="two-thirds"/);
    assert.match(markup, /Two thirds/);
    assert.equal(f.run("fieldValue({type:'select',options:['full']},{value:'',dataset:{}})"), undefined);
    assert.equal(f.run("fieldValue({type:'color'},{value:'#123456',dataset:{empty:'true'}})"), undefined);
    f.run("editBlock('b','color','#123456'); editBlock('b','color',undefined)");
    assert.equal(f.run("Object.hasOwn(app.draft.blocks[0].values,'color')"), false);
    assert.match(f.run("fallbackStyle({values:{span:'half',font:'serif',align:'right',color:'#abcdef'}})"), /--block-span:3;.*text-align:right;.*color:#abcdef/);
    assert.doesNotMatch(f.run("fallbackStyle({values:{font:'x;background:red',color:'red;position:fixed'}})"), /background|position/);
  } finally { f.dispose(); }
});

test("flush captures unsent renderer text before saving or refreshing", async () => {
  const f = fixture();
  try {
    f.type("last keystroke before navigation");
    await f.run("flushDraft()");
    assert.equal(f.run("saved[0].blocks[0].values.text"), "last keystroke before navigation");
    assert.equal(f.run("app.draft"), null);
    assert.equal(f.calls[0].kind, "flush");
    assert.equal(f.calls.at(-1).kind, "refresh");
    assert.equal(f.calls.at(-1).blockId, "b");
  } finally { f.dispose(); }
});

test("new inline text during an in-flight save is captured before refresh", async () => {
  const f = fixture();
  try {
    f.type("first edit");
    f.context.duringSave = () => f.type("continued typing during save");
    await f.run("flushDraft()");
    assert.equal(f.run("saved.length"), 2);
    assert.equal(f.run("saved[1].blocks[0].values.text"), "continued typing during save");
    assert.equal(f.run("saved[1].revision"), 2);
    assert.equal(f.run("app.state.pages.p.blocks[0].values.text"), "continued typing during save");
    assert.equal(f.calls.filter((message) => message.kind === "refresh").length, 1);
  } finally { f.dispose(); }
});

test("conflicting saves retain draft; foreign frames cannot modify content", async () => {
  const f = fixture();
  try {
    f.emit({ kind: "change", blockId: "b", field: "text", value: "foreign" }, { origin: "https://elsewhere.test" });
    assert.equal(f.run("app.draft"), null);
    f.emit({ kind: "change", blockId: "b", field: "text", value: "foreign" }, { source: {} });
    assert.equal(f.run("app.draft"), null);
    f.type("keep this draft");
    f.run("mutate = async () => { throw new Error('Conflict'); }; showConflict = () => { globalThis.conflictShown = true; }");
    await f.run("flushDraft()");
    assert.equal(f.run("app.draft.blocks[0].values.text"), "keep this draft");
    assert.equal(f.run("conflictShown"), true);
    assert.equal(f.calls.some((message) => message.kind === "refresh"), false);
  } finally { f.dispose(); }
});

test("reconnect restores selected element without selecting or focusing text", () => {
  const f = fixture();
  try {
    f.run("app.frameReady = false");
    f.emit({ kind: "ready" });
    assert.equal(f.run("app.frameReady"), true);
    assert.equal(f.calls.at(-1).kind, "select");
    assert.equal(f.calls.at(-1).blockId, "b");
    f.run("app.selected = null");
    f.emit({ kind: "ready" });
    assert.equal(f.calls.at(-1).blockId, null);
  } finally { f.dispose(); }
});
