const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { randomUUID } = require("node:crypto");
const source = fs.readFileSync(path.join(__dirname, "../src/HyperLib.Host/wwwroot/app.js"), "utf8");

function fixture() {
  const elements = new Map(), handlers = new Map();
  const classes = () => {
    const values = new Set();
    return { add: (name) => values.add(name), remove: (name) => values.delete(name),
      contains: (name) => values.has(name), toggle(name, on) { on ? values.add(name) : values.delete(name); } };
  };
  function element(id) {
    if (!elements.has(id)) elements.set(id, { id: id.replace(/^#/, ""), dataset: {}, hidden: false,
      classList: classes(), style: {removeProperty(){},setProperty(){}},
      setAttribute(){},addEventListener(){},replaceChildren(){this.innerHTML="";},focus(){},append(){},
      querySelectorAll:()=>[],getBoundingClientRect:()=>({width:1120}),contentWindow:{postMessage(){}},
    });
    return elements.get(id);
  }
  const window = {innerHeight:800,addEventListener:(name, callback)=>handlers.set(name,callback),scrollTo(){}};
  window.top=window;
  const context=vm.createContext({console,structuredClone,URL,FormData,Blob,crypto:{randomUUID},
    setTimeout,clearTimeout,setInterval(){},requestAnimationFrame:fn=>fn(),
    matchMedia:()=>({matches:false}),getComputedStyle:()=>({getPropertyValue:()=>"0px"}),
    location:{origin:"http://127.0.0.1:12345",hash:"#library"},history:{state:null,replaceState(){}},
    localStorage:{getItem:()=>null,setItem(){},removeItem(){}},window,
    document:{body:element("body"),documentElement:element("html"),querySelector:element,
      querySelectorAll:selector=>selector==="iframe"?[element("#page-frame"),element("#work-frame")]:[],
      addEventListener(){},createTextNode:text=>text,createElement:()=>element("created")},
  });
  vm.runInContext(source.slice(0,source.lastIndexOf("\nstart();")),context);
  vm.runInContext(`app.state={libraryId:'library',revision:1,labels:{},pages:{p:{id:'p',workId:'w',title:'Chapter page',blocks:[]}},nodes:[
    {id:'library',type:'library',title:'Library'},
    {id:'c',type:'collection',parentId:'library',title:'Collection'},
    {id:'nested',type:'collection',parentId:'c',title:'Nested collection'},
    {id:'series',type:'series',parentId:'nested',title:'Series'},
    {id:'w',type:'work',parentId:'series',title:'A styled work',appearance:{background:'#121212',ink:'#f0f0f0',accent:'#ccbbaa'}},
    {id:'other',type:'work',parentId:'c',title:'Another work'},
    {id:'chapter',type:'chapter',parentId:'w',title:'A chapter'},
    {id:'p',type:'page',parentId:'chapter',workId:'w',title:'Chapter page'}]};
    loadTemplate=async()=>{};renderNavigation=()=>{};renderPanel=()=>{};renderPresentation=()=>{};
  `,context);
  return {context,elements,handlers,window,run:js=>vm.runInContext(js,context),
    emit(data,frame=element("#work-frame"),origin="http://127.0.0.1:12345"){
      handlers.get("message")({origin,source:frame.contentWindow,data:{source:"hyperlib-renderer",...data}});
    },dispose(){vm.runInContext("clearTimeout(app.timer);clearTimeout(toastTimer)",context);}};
}

test("reading Work and nested branches use the immersive surface; Edit Mode restores authoring navigation", async()=>{
  const f=fixture();
  try {
    f.run("globalThis.surfaces=[];renderWorkSurface=current=>surfaces.push(current.id);renderPage=()=>{};app.id='chapter'");
    await f.run("render()");
    assert.equal(f.run("work().id"),"w");
    assert.equal(f.elements.get("body").classList.contains("immersive-work"),true);
    assert.equal(f.elements.get("#landing").hidden,true);
    assert.equal(f.elements.get("#work-view").hidden,false);
    assert.equal(f.elements.get("#navigation").hidden,true);
    assert.equal(f.elements.get("#nav-expand").hidden,true);
    assert.equal(f.run("surfaces[0]"),"chapter");
    f.run("app.edit=true");await f.run("render()");
    assert.equal(f.elements.get("body").classList.contains("immersive-work"),false);
    assert.equal(f.elements.get("#work-view").hidden,true);
    assert.equal(f.elements.get("#landing").hidden,false);
    assert.equal(f.elements.get("#navigation").hidden,false);
    f.run("app.edit=false;app.id='p'");await f.run("render()");
    assert.equal(f.elements.get("body").classList.contains("immersive-work"),true);
    assert.equal(f.elements.get("#work-view").hidden,true);
    assert.equal(f.elements.get("#page-view").hidden,false);
  } finally {f.dispose();}
});

test("collection reading flattens nested series and collections into Work choices; library has no direct page shortcuts",()=>{
  const f=fixture();
  try {
    f.run("app.id='c';renderLanding(node())");
    let markup=f.elements.get("#landing").innerHTML;
    assert.match(markup,/href="#w"/);assert.match(markup,/href="#other"/);
    assert.doesNotMatch(markup,/href="#nested"|href="#series"|href="#p"/);
    f.run("app.id='library';renderLanding(node())");markup=f.elements.get("#landing").innerHTML;
    assert.match(markup,/href="#c"/);assert.doesNotMatch(markup,/Open a page|href="#p"/);
    f.run("app.id='c';app.edit=true;renderLanding(node())");
    assert.match(f.elements.get("#landing").innerHTML,/href="#nested"/);
  } finally {f.dispose();}
});

test("renderer-unavailable fallback keeps Work identity, contents, appearance, and a Collection return path",()=>{
  const f=fixture();
  try {
    f.run("app.id='w';renderWorkFallback(node(),'Preparing design')");
    const markup=f.elements.get("#work-fallback").innerHTML;
    assert.match(markup,/A styled work/);assert.match(markup,/href="#nested"/);
    assert.match(markup,/href="#p"/);assert.match(markup,/--work-background:#121212/);
    assert.match(markup,/Preparing design/);
    assert.doesNotMatch(markup,/<img|undefined/);
    assert.equal(f.run("safeUrl(undefined)"), "");
    assert.equal(f.run("localMedia('   ')"), "");
    f.run("app.id='p';renderFallback()");
    assert.match(f.elements.get("#page-fallback").innerHTML,/work-fallback-navigation/);
    assert.match(f.elements.get("#page-fallback").innerHTML,/href="#w"/);
  } finally {f.dispose();}
});

test("renderer navigation and sizing require current frame, route, generation, origin, and valid target",()=>{
  const f=fixture();
  try {
    const frame=f.run('$("#work-frame")');
    frame.dataset.routeId="w";frame.dataset.generationId="g";
    f.run("app.id='w';globalThis.visits=[];navigate=async id=>visits.push(id)");
    const valid={pageId:"w",generationId:"g",kind:"navigate",nodeId:"p"};
    f.emit({...valid,pageId:"other"});f.emit({...valid,generationId:"old"});
    f.emit(valid,frame,"https://untrusted.invalid");f.emit({...valid,nodeId:"missing"});
    assert.equal(f.run("visits.length"),0);
    f.emit(valid);assert.equal(f.run("visits[0]"),"p");
    f.emit({pageId:"other",generationId:"g",kind:"height",height:1400});assert.equal(frame.style.height,undefined);
    f.emit({pageId:"w",generationId:"g",kind:"height",height:400});assert.equal(frame.style.height,"800px");
    f.emit({pageId:"w",generationId:"g",kind:"height",height:1400});assert.equal(frame.style.height,"1400px");
  } finally {f.dispose();}
});

test("navigation flushes draft before changing hash and retains route after conflict",async()=>{
  const f=fixture();
  try {
    f.run("app.id='p';location.hash='#p';globalThis.finishFlush=null;flushDraft=()=>new Promise(resolve=>finishFlush=resolve)");
    const pending=f.run("navigate('w')");
    assert.equal(f.context.location.hash,"#p");
    f.run("finishFlush()");await pending;assert.equal(f.context.location.hash,"w");
    f.run("location.hash='#p';flushDraft=async()=>{app.draft={id:'p'}}");
    await f.run("navigate('w')");assert.equal(f.context.location.hash,"#p");
    assert.equal(f.run("app.draft.id"),"p");
  } finally {f.dispose();}
});

test("Work and Page document loads replace subframe history, including clearing a Work surface",async()=>{
  const f=fixture();
  try{
    const replacements=[];
    const workFrame=f.run('$("#work-frame")'),pageFrame=f.run('$("#page-frame")');
    for(const frame of [workFrame,pageFrame]){
      Object.defineProperty(frame,"src",{set(){throw new Error("src would add iframe history")}});
      frame.contentWindow.location={replace:url=>replacements.push({frame:frame.id,url})};
    }
    f.run("api=async()=>({generationId:'current',proxyPrefix:'/_runtime/w/current/'});app.id='w'");
    await f.run("renderWorkSurface(node(),app.navigation)");
    assert.equal(replacements[0].url,"http://127.0.0.1:12345/_runtime/w/current/browse/w?edit=0");
    assert.equal(workFrame.dataset.surfaceUrl,replacements[0].url);
    f.run("clearWorkFrame()");assert.equal(replacements[1].url,"about:blank");
    f.run("app.id='p'");await f.run("renderPage(app.navigation)");
    assert.equal(replacements[2].url,"http://127.0.0.1:12345/_runtime/w/current/page/p?edit=0");
    assert.equal(pageFrame.dataset.surfaceUrl,replacements[2].url);
  }finally{f.dispose();}
});
