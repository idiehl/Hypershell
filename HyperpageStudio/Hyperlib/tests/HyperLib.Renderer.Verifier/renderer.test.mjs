import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const root = new URL('../../', import.meta.url), messages = [], windowEvents = new Map(), documentEvents = new Map();
const classes = () => ({values:new Set(),toggle(name,on){if(on)this.values.add(name);else this.values.delete(name);},contains(name){return this.values.has(name);}});
const block = {dataset:{blockId:'block-a'},classList:classes(),setAttribute(){},removeAttribute(){}};
const field = {dataset:{field:'title'},textContent:'Original title',closest(selector){return selector==='[data-block-id]'?block:(selector.startsWith('[data-field]')||selector.includes('[contenteditable="true"]'))&&this.contentEditable==='true'?this:null;},contains(){return false;}};
const other = {closest(){return null;}};
const parent = {postMessage(message,origin){assert.equal(origin,'http://localhost:54321');messages.push(message);}};
const document = {body:{classList:classes(),style:{},getBoundingClientRect:()=>({height:720})},documentElement:{style:{}},activeElement:null,
  addEventListener(type,handler){documentEvents.set(type,handler);},querySelectorAll(selector){return selector==='[data-field]'?[field]:selector==='[data-block-id]'?[block]:[];},querySelector(){return {};}};
const window = {parent,addEventListener(type,handler){windowEvents.set(type,handler);},getSelection(){return null;}};
const timers = new Map();let nextTimer=0;
vm.runInNewContext(fs.readFileSync(new URL('src/HyperLib.Runtime/wwwroot/renderer.js',root),'utf8'), {
  window,document,location:{origin:'http://localhost:54321',reload(){throw new Error('Unexpected reload');}},
  setTimeout(callback){timers.set(++nextTimer,callback);return nextTimer;},clearTimeout(id){timers.delete(id);},
  ResizeObserver:class{observe(){}disconnect(){}},getComputedStyle:()=>({getPropertyValue:()=>'#123456',colorScheme:'dark'}),CSS:{escape:value=>value}
});
let refreshes=0;
const bridge={async invokeMethodAsync(name){assert.equal(name,'Refresh');refreshes++;}};
const message = data => windowEvents.get('message')({origin:'http://localhost:54321',source:parent,data:{source:'hyperlib-editor',...data}});
window.hyperlibRenderer.connect(bridge,true,'page-a','generation-a');messages.length=0;
field.textContent='Unsaved typed title';document.activeElement=field;documentEvents.get('input')({target:field});
assert.equal(messages[0].kind,'dirty','Dirty must precede debounce');
await message({kind:'flush',requestId:'save-1'});
assert.deepEqual(messages.map(x=>x.kind),['dirty','change','flushed']);
assert.equal(messages[1].value,'Unsaved typed title');assert.equal(messages[2].requestId,'save-1');assert.equal(messages[2].pageId,'page-a');
messages.length=0;await message({kind:'flush',requestId:'clean'});assert.deepEqual(messages.map(x=>x.kind),['flushed'],'Clean flush must still acknowledge');
documentEvents.get('click')({target:field,preventDefault(){}});assert.ok(block.classList.contains('hl-selected'));
window.hyperlibRenderer.connect(bridge,true,'page-a','generation-a');assert.ok(block.classList.contains('hl-selected'),'Selection must survive refreshed DOM decoration');
await message({kind:'refresh',blockId:'block-a'});assert.equal(refreshes,1);
await message({kind:'select',blockId:null});assert.ok(!block.classList.contains('hl-selected'),'Explicit clear must clear outline');
await message({kind:'select',blockId:'block-a'});await message({kind:'edit-state',editing:false});assert.ok(!block.classList.contains('hl-selected'));assert.equal(field.contentEditable,'false');
await message({kind:'edit-state',editing:true});assert.ok(block.classList.contains('hl-selected'));
messages.length=0;documentEvents.get('input')({target:other});assert.equal(messages.length,0,'Interactive controls are not content edits');
let prevented=false;documentEvents.get('keydown')({target:other,key:'z',ctrlKey:true,preventDefault(){prevented=true;}});assert.ok(prevented);assert.equal(messages.at(-1).action,'undo');
messages.length=0;documentEvents.get('keydown')({target:field,key:'z',ctrlKey:true,preventDefault(){throw new Error('Native text undo intercepted');}});assert.equal(messages.length,0);
messages.length=0;await windowEvents.get('message')({origin:'https://untrusted.invalid',source:parent,data:{source:'hyperlib-editor',kind:'flush'}});assert.equal(messages.length,0);
window.hyperlibRenderer.connect(bridge,true,'page-b','generation-b');assert.ok(!block.classList.contains('hl-selected'),'Selection must not leak across page navigation');

const tools = await import('data:text/javascript,'+encodeURIComponent(fs.readFileSync(new URL('Starters/standard/src/Pages/wwwroot/blocks/tool.js',root),'utf8')));
function element(nodes,all=[]) {return {listeners:new Map(),querySelector(key){return nodes[key];},querySelectorAll(){return all;},addEventListener(type,fn){this.listeners.set(type,fn);},removeEventListener(type){this.listeners.delete(type);}};}
const a={value:'12'},b={value:'3'},operation={value:'divide'},result={textContent:''};
const calculator=element({'[data-number="a"]':a,'[data-number="b"]':b,'[data-operation]':operation,'.hl-tool-result':result});
tools.mount(calculator,'calculator',0,'');calculator.listeners.get('input')({type:'input'});assert.equal(result.textContent,'4');
b.value='0';calculator.listeners.get('input')({type:'input'});assert.match(result.textContent,/divide by zero/);
a.value='Infinity';calculator.listeners.get('input')({type:'input'});assert.match(result.textContent,/finite numbers/);tools.dispose(calculator);assert.equal(calculator.listeners.size,0);
const status={textContent:''},checks=[{checked:true},{checked:false},{checked:true}],checklist=element({'.hl-tool-status':status},checks);
tools.mount(checklist,'checklist',0,'a\nb\nc');checklist.listeners.get('change')({type:'change'});assert.equal(status.textContent,'2 of 3 complete');tools.dispose(checklist);
const output={textContent:'00:01'},toggle={textContent:'Start'},timerStatus={textContent:'Ready'},timer=element({'.hl-timer-output':output,'[data-action="toggle"]':toggle,'.hl-tool-status':timerStatus});
tools.mount(timer,'timer',1,'');timer.listeners.get('click')({type:'click',target:{closest:selector=>selector==='button'?toggle:{dataset:{action:'toggle'}}}});
await new Promise(resolve=>setTimeout(resolve,1250));assert.equal(output.textContent,'00:00');assert.equal(timerStatus.textContent,'Timer complete.');tools.dispose(timer);assert.equal(timer.listeners.size,0);
const counter = await import('data:text/javascript,'+encodeURIComponent(fs.readFileSync(new URL('Starters/standard/src/Pages/wwwroot/blocks/counter.js',root),'utf8')));
const count={textContent:''},counterElement=element({'output':count});counter.mount(counterElement,4);counterElement.listeners.get('click')({target:{closest:()=>({dataset:{action:'add'}})}});assert.equal(count.textContent,5);counter.update(counterElement,4);assert.equal(count.textContent,5);counter.update(counterElement,9);assert.equal(count.textContent,9);counter.dispose(counterElement);assert.equal(counterElement.listeners.size,0);
console.log('HYPERLIB_RENDERER_JS_OK flush-order selection-refresh history local-tools counter-lifecycle');
