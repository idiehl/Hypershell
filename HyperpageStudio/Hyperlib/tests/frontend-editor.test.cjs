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
      showModal() { this.open = true; }, close() { this.open = false; },
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

test("Collections can nest while the organize picker excludes self and descendants", () => {
  const f = fixture();
  try {
    f.run(`app.state.nodes = [
      {id:'top',type:'collection',parentId:'library',title:'Top'},
      {id:'nested',type:'collection',parentId:'top',title:'Nested'},
      {id:'sibling',type:'collection',parentId:'library',title:'Sibling'}];
      app.id='top'; openDialog=(title,body)=>globalThis.organizeMarkup=body; organizeDialog();`);
    assert.ok(Array.from(f.run("allowedChildTypes(node('sibling'))")).includes("collection"));
    const markup = f.run("organizeMarkup");
    assert.match(markup, /value="library"/);
    assert.match(markup, /value="sibling"/);
    assert.doesNotMatch(markup, /<option value="(?:top|nested)"/);
  } finally { f.dispose(); }
});

test("Page rows expose escaped names and catalog reference IDs to the landing search", () => {
  const f = fixture();
  try {
    f.run(`app.state.pages.p.title = 'Chart & <figure>'; app.state.pages.p.blocks = [{id:'catalog-entry',values:{text:'HLC-CHART-123'}}];`);
    const markup = f.run("recentRow(app.state.pages.p)");
    assert.match(markup, /data-card-title="chart &amp; &lt;figure&gt; hlc-chart-123"/);
    assert.doesNotMatch(markup, /<figure>/);
  } finally { f.dispose(); }
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

function archiveFixture() {
  const f = fixture();
  f.run(`
    globalThis.reviewNodes = [
      {id:'w',type:'work',parentId:'library',title:'Field notes'},
      {id:'part',type:'part',parentId:'w',title:'Observations'},
      {id:'p',type:'page',parentId:'part',title:'First Page'},
      {id:'q',type:'page',parentId:'part',title:'Second Page'},
      {id:'r',type:'page',parentId:'w',title:'Appendix'}
    ];
    globalThis.serverDraft = {id:'draft',rootNodeId:'w',title:'Field notes',contentRevision:1,
      draftRevision:1,reviewHash:'hash-1',stale:false,nodes:reviewNodes,
      selectedNodeIds:reviewNodes.map(item=>item.id)};
    app.archives = {rootNodeId:'w',draft:structuredClone(serverDraft),selected:new Set(serverDraft.selectedNodeIds),
      collapsed:new Set(),dirty:false,saving:null,version:0,busy:false,loading:false,syncing:false,
      conflicted:false,target:{canSync:true,targetTitle:'My Archives'},error:'',receipt:null};
    globalThis.traffic = [];
    api = async (path,options={}) => {
      const body=options.body ? JSON.parse(options.body) : null;
      traffic.push({path,method:options.method||'GET',body});
      if(options.method==='PUT') {
        if(body.expectedDraftRevision!==serverDraft.draftRevision) throw Object.assign(new Error('Review conflict'),{status:409});
        serverDraft={...serverDraft,draftRevision:serverDraft.draftRevision+1,selectedNodeIds:body.selectedNodeIds,reviewHash:'hash-'+(serverDraft.draftRevision+1)};
      }
      return structuredClone(serverDraft);
    };
    native = async (method,params) => {
      traffic.push({method,params});
      return method==='archives.status' ? {canSync:true,targetTitle:'My Archives'} : {receipt:{url:'https://archives.example/field-notes',message:'Five items updated.'}};
    };
    $('#archives-dialog').showModal();
  `);
  return f;
}

test("Archives checkboxes cascade and derive partial parents while preserving sibling choices", () => {
  const f = archiveFixture();
  try {
    f.run("globalThis.selection = archiveToggleSelection(reviewNodes,new Set(),'part',true)");
    assert.deepEqual(Array.from(f.run("[...selection].sort()")), ["p", "part", "q", "w"]);
    assert.equal(f.run("archiveCheckState(reviewNodes,selection,'part').checked"), true);
    assert.equal(f.run("archiveCheckState(reviewNodes,selection,'w').mixed"), true);
    f.run("selection=archiveToggleSelection(reviewNodes,selection,'q',false)");
    assert.equal(f.run("archiveCheckState(reviewNodes,selection,'part').mixed"), true);
    assert.equal(f.run("selection.has('p') && selection.has('part') && !selection.has('q')"), true);
    f.run("selection=archiveToggleSelection(reviewNodes,selection,'part',false)");
    assert.equal(f.run("selection.size"), 0);
    f.run("selection=archiveToggleSelection(reviewNodes,selection,'w',true)");
    assert.equal(f.run("selection.size"), 5);
    f.run("selection=archiveToggleSelection(reviewNodes,selection,'w',false)");
    assert.equal(f.run("selection.size"), 0);
    f.run("reviewNodes[0].title='<img src=x onerror=evil()>'; selection=new Set(['w','part','p'])");
    const markup = f.run("archiveTreeMarkup({nodes:reviewNodes},selection,new Set(['part']))");
    assert.match(markup, /aria-checked="mixed"/);
    assert.match(markup, /aria-expanded="false"/);
    assert.match(markup, /&lt;img src=x onerror=evil\(\)&gt;/);
    assert.doesNotMatch(markup, /<img/);
  } finally { f.dispose(); }
});

test("preparing an Archives review flushes Page writing and queued changes before local draft creation", async () => {
  const f = archiveFixture();
  try {
    f.run(`
      app.archives=null;
      app.mutation=new Promise(resolve=>globalThis.releaseMutation=resolve);
      api=async (path,options={})=>{
        traffic.push({path,method:options.method||'GET',body:options.body && JSON.parse(options.body),saved:saved.length});
        if(!options.method) return null;
        return {...structuredClone(serverDraft),contentRevision:app.state.revision};
      };
    `);
    f.type("last local sentence");
    const opening = f.run("openArchiveReview('w')");
    await new Promise(setImmediate);
    assert.equal(f.run("saved[0].blocks[0].values.text"), "last local sentence");
    assert.equal(f.run("traffic.length"), 0);
    f.run("releaseMutation()");
    await opening;
    assert.equal(f.run("traffic[1].method"), "POST");
    assert.equal(f.run("traffic[1].body.expectedContentRevision"), 2);
    assert.equal(f.run("traffic.some(call=>call.method==='archives.sync')"), false);
    assert.equal(f.run("app.archives.draft.stale"), false);
    assert.equal(f.elements.get("#archives-dialog").open, true);
    // A closed, saved draft is resumed rather than recreated.
    f.run("globalThis.persistedReview=structuredClone(app.archives.draft); api=async()=>structuredClone(persistedReview)");
    await f.run("closeArchiveReview()");
    await f.run("openArchiveReview('w')");
    assert.equal(f.run("app.archives.selected.size"), 5);
  } finally { f.dispose(); }
});

test("Archives preparation preserves conflicted Page writing without creating or publishing a draft", async () => {
  const f = archiveFixture();
  try {
    f.type("unreconciled local writing");
    f.run("mutate=async()=>{throw new Error('Page conflict')}; showConflict=()=>{};");
    await f.run("openArchiveReview('w')");
    assert.equal(f.run("app.draft.blocks[0].values.text"), "unreconciled local writing");
    assert.equal(f.run("traffic.length"), 0);
    assert.match(f.elements.get("#toast").textContent, /writing is preserved/);
  } finally { f.dispose(); }
});

test("rapid Archives choices serialize saves and retain the newest selection through late responses", async () => {
  const f = archiveFixture();
  try {
    f.run(`
      globalThis.firstWrite=true;
      globalThis.originalApi=api;
      api=async(path,options)=>{
        if(firstWrite) { firstWrite=false; await new Promise(resolve=>globalThis.releaseWrite=resolve); }
        return originalApi(path,options);
      };
      changeArchiveSelection('part',false);
      changeArchiveSelection('r',false);
    `);
    assert.equal(f.run("app.archives.selected.size"), 0);
    assert.equal(f.run("app.archives.dirty"), true);
    const saving = f.run("saveArchiveSelection(app.archives)");
    f.run("releaseWrite()");
    await saving;
    assert.equal(f.run("traffic.length"), 2);
    assert.deepEqual(Array.from(f.run("traffic.map(call=>call.body.expectedDraftRevision)")), [1, 2]);
    assert.equal(f.run("serverDraft.selectedNodeIds.length"), 0);
    assert.equal(f.run("app.archives.selected.size"), 0);
    assert.equal(f.run("app.archives.draft.draftRevision"), 3);
    assert.equal(f.run("app.archives.dirty"), false);
  } finally { f.dispose(); }
});

test("conflicted Archives choices survive close and explicit refresh; new Pages start unchecked", async () => {
  const f = archiveFixture();
  try {
    f.run(`
      app.archives.selected=new Set(['w','r']); app.archives.dirty=true; app.archives.version++;
      serverDraft.draftRevision=2;
    `);
    await assert.rejects(f.run("saveArchiveSelection(app.archives)"), /Review conflict/);
    assert.equal(f.run("app.archives.conflicted"), true);
    await f.run("closeArchiveReview()");
    assert.equal(f.elements.get("#archives-dialog").open, true);
    assert.deepEqual(Array.from(f.run("[...app.archives.selected]")), ["w", "r"]);
    f.run(`
      globalThis.originalApi=api;
      api=async(path,options={})=>{
        if(path.endsWith('/refresh')) {
          traffic.push({path,method:options.method,body:JSON.parse(options.body)});
          serverDraft={...serverDraft,draftRevision:3,nodes:[...reviewNodes,{id:'new',parentId:'w',type:'page',title:'New Page'}]};
          return structuredClone(serverDraft);
        }
        return originalApi(path,options);
      };
    `);
    await f.run("refreshArchiveReview()");
    assert.equal(f.run("app.archives.conflicted"), false);
    assert.deepEqual(Array.from(f.run("serverDraft.selectedNodeIds")), ["w", "r"]);
    assert.equal(f.run("app.archives.selected.has('new')"), false);
    assert.equal(f.run("app.archives.draft.nodes.length"), 6);
    assert.equal(f.run("traffic.some(call=>call.method==='archives.sync')"), false);
    await f.run("closeArchiveReview()");
    assert.equal(f.elements.get("#archives-dialog").open, false);
  } finally { f.dispose(); }
});

test("Archives sync sends only the saved review identity and keeps draft and Pages on transport failure", async () => {
  const f = archiveFixture();
  try {
    f.run("app.archives.selected=new Set(['w','r']); app.archives.dirty=true; app.archives.version++");
    const localBefore = f.run("JSON.stringify(app.state.pages)");
    await f.run("syncArchiveReview()");
    const sync = f.run("traffic.find(call=>call.method==='archives.sync')");
    assert.deepEqual(JSON.parse(JSON.stringify(sync.params)), {
      libraryId: "library", draftId: "draft", expectedDraftRevision: 2,
      expectedContentRevision: 1, reviewHash: "hash-2",
    });
    assert.equal(f.run("JSON.stringify(app.state.pages)"), localBefore);
    assert.match(f.elements.get("#archives-receipt").innerHTML, /Open in Archives/);
    assert.equal(f.elements.get("#archives-dialog").open, true);
    f.run("native=async()=>{throw new Error('Connection interrupted')}; app.archives.receipt=null;");
    await f.run("syncArchiveReview()");
    assert.equal(f.run("app.archives.error"), "Connection interrupted");
    assert.deepEqual(Array.from(f.run("[...app.archives.selected]")), ["w", "r"]);
    assert.equal(f.run("app.archives.draft.id"), "draft");
    assert.equal(f.run("JSON.stringify(app.state.pages)"), localBefore);
  } finally { f.dispose(); }
});

test("local changes flushed at final sync require a fresh Archives review", async () => {
  const f = archiveFixture();
  try {
    f.type("written after the review");
    await f.run("syncArchiveReview()");
    assert.equal(f.run("saved[0].blocks[0].values.text"), "written after the review");
    assert.equal(f.run("traffic.some(call=>call.method==='archives.sync')"), false);
    assert.match(f.run("app.archives.error"), /Refresh review/);
    assert.equal(f.run("app.archives.selected.size"), 5);
  } finally { f.dispose(); }
});
