/* Real Phase 3 content-slot editing against a disposable generated Footer/Body Work. */
const fs=require('fs'),path=require('path'),net=require('net'),zlib=require('zlib');
const {spawn,spawnSync}=require('child_process');
const {chromium}=require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const [workArgument,outputArgument,assetsArgument,verifierArgument,sourceArgument]=process.argv.slice(2);
 if(!workArgument||!outputArgument||!assetsArgument||!verifierArgument||!sourceArgument)throw new Error('Usage: node verify-body-slots-authoring.cjs WORKROOT OUTPUT ASSETS VERIFIER_EXE SOURCE_LIBRARY');
 const workRoot=path.resolve(workArgument),root=path.resolve(workRoot,'../..'),output=path.resolve(outputArgument),assets=path.resolve(assetsArgument),verifier=path.resolve(verifierArgument),sourceLibrary=path.resolve(sourceArgument);
 if(!root.toLowerCase().includes('qa')||!fs.existsSync(path.join(root,'.studio','hyperlib-work.json'))||!fs.existsSync(path.join(workRoot,'.studio','pipeline.json')))
  throw new Error('Select a disposable generated QA Work with an independently owned private library.');
 if(!fs.existsSync(verifier))throw new Error('The built verifier executable is required for native foundation restaging.');
 fs.mkdirSync(output,{recursive:true});const web=path.join(root,'src','HyperLib.Host','wwwroot','studio-authoring');fs.mkdirSync(web,{recursive:true});
 for(const file of ['index.html','app.js','element-editor.js','app.css','studio.css'])fs.copyFileSync(path.join(assets,file),path.join(web,file));
 const picture=path.join(output,'body-slot-import.png');
 const chunk=(kind,data)=>{const type=Buffer.from(kind),bytes=Buffer.concat([type,data]);let crc=0xffffffff;for(const value of bytes){crc^=value;for(let bit=0;bit<8;bit++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}const length=Buffer.alloc(4),checksum=Buffer.alloc(4);length.writeUInt32BE(data.length);checksum.writeUInt32BE((crc^0xffffffff)>>>0);return Buffer.concat([length,bytes,checksum]);};
 const header=Buffer.alloc(13);header.writeUInt32BE(1,0);header.writeUInt32BE(1,4);header[8]=8;header[9]=6;
 fs.writeFileSync(picture,Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',header),chunk('IDAT',zlib.deflateSync(Buffer.from([0,101,168,142,255]))),chunk('IEND',Buffer.alloc(0))]));
 const port=await new Promise((resolve,reject)=>{const server=net.createServer();server.once('error',reject);server.listen(0,'127.0.0.1',()=>{const p=server.address().port;server.close(()=>resolve(p));});});
 const origin='http://127.0.0.1:'+port;
 const host=spawn('dotnet',['run','--configuration','Release','--project',path.join(root,'src','HyperLib.Host','HyperLib.Host.csproj'),'--no-launch-profile','--','--root',root,'--port',String(port)],
  {cwd:root,windowsHide:true,stdio:['ignore','pipe','pipe'],env:{...process.env,DOTNET_NOLOGO:'1',DOTNET_PROCESSOR_COUNT:'2'}});
 let logs='',browser,page;host.stdout.on('data',b=>logs=(logs+b).slice(-30000));host.stderr.on('data',b=>logs=(logs+b).slice(-30000));
 const report={result:'STUDIO_BODY_SLOTS_AUTHORING_OK',workRoot,root,checks:[],screenshots:[],errors:[]};
 const check=(yes,message)=>{if(!yes)throw new Error(message);report.checks.push(message);};
 const api=async route=>{const response=await fetch(origin+route);if(!response.ok)throw new Error(route+': '+response.status+' '+await response.text());return response.json();};
 const waitFor=async(predicate,label,timeout=60000)=>{const end=Date.now()+timeout;while(Date.now()<end){if(host.exitCode!==null)throw new Error('Owned QA host exited: '+logs);try{if(await predicate())return;}catch{}await new Promise(r=>setTimeout(r,120));}throw new Error('Timed out: '+label);};
 const rowOf=page=>page.blocks.find(block=>block.type==='studio-body-row'&&JSON.parse(block.values.layout).id==='article-slots-row');
 const elements=row=>{const result=[];const visit=items=>{for(const item of items||[]){result.push(item);visit(item.children);}};visit(row.elements);return result;};
 const slotOf=(state,id,slotId)=>elements(JSON.parse(rowOf(state.pages[id]).values.layout)).find(item=>item.id===slotId);
 const contentOf=(state,id)=>({text:slotOf(state,id,'article-text-slot').text,label:slotOf(state,id,'article-tool-slot').templateBlocks[0].values.label,
  media:slotOf(state,id,'article-image-slot').mediaPath,alt:slotOf(state,id,'article-image-slot').alt});
 const geometry=row=>({id:row.id,arrangement:row.arrangement,height:row.height,padding:row.padding,fillColor:row.fillColor,borderWidth:row.borderWidth,
  elements:elements(row).map(item=>({id:item.id,kind:item.kind,x:item.x,y:item.y,widthMode:item.widthMode,widthPercent:item.widthPercent,widthPixels:item.widthPixels,
   height:item.height,padding:item.padding,arrangement:item.arrangement,alignment:item.alignment,positionAlignment:item.positionAlignment,fontSize:item.fontSize,fontFamily:item.fontFamily,
   templateBlocks:(item.templateBlocks||[]).map(block=>({id:block.id,type:block.type}))}))});
 try{
  await waitFor(async()=>(await api('/api/health')).root.toLowerCase()===root.toLowerCase(),'owned gateway identity',120000);
  const pipeline=await api('/api/studio/pipeline'),initial=await api('/api/state');
  const originalContent=Object.entries(initial.pages).filter(([,p])=>rowOf(p)).map(([id,p])=>({id,content:contentOf(initial,id),sources:p.sources}));
  check(!!rowOf(initial.pages[pipeline.landingPageId]),'The saved fixture contains the real reusable article-slots-row.');await api('/api/runtime/'+pipeline.workId);
  browser=await chromium.launch({headless:true,executablePath:'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe'});
  page=await browser.newPage({viewport:{width:1440,height:1050}});page.on('pageerror',e=>report.errors.push(e.message));
  await page.addInitScript(()=>{const Original=window.EventSource;window.__slotStreams=[];window.EventSource=class extends Original{constructor(...args){super(...args);window.__slotStreams.push(this);}};
   window.__slotFrameReady=null;window.addEventListener('message',event=>{const frame=document.querySelector('#page-frame'),m=event.data;if(frame&&event.source===frame.contentWindow&&m?.source==='hyperlib-renderer'&&m.kind==='ready')window.__slotFrameReady={pageId:m.pageId,generationId:m.generationId};});});
  await page.goto(origin+'/studio-authoring/index.html',{waitUntil:'domcontentloaded'});await page.waitForFunction(()=>window.__studioAuthoring?.state().ready);
  const frame=page.frameLocator('#page-frame');
  async function ready(id){await page.waitForFunction(id=>{const f=document.querySelector('#page-frame'),r=window.__slotFrameReady;return window.__studioAuthoring.state().nodeId===id&&r?.pageId===id&&r?.generationId===f?.dataset.generationId;},id);await frame.locator('#studio-slot-hints').waitFor({state:'attached'});}
  async function selectSlots(id){await ready(id);const state=await api('/api/state'),block=rowOf(state.pages[id]);await frame.locator('[data-block-id="'+block.id+'"]').click({position:{x:5,y:5}});await page.locator('#properties-content').getByText('PAGE CONTENT',{exact:true}).waitFor();return block;}
  async function saved(predicate,label){await page.locator('#studio-save').click();await waitFor(async()=>predicate(await api('/api/state')),'save '+label);await page.waitForFunction(()=>!window.__studioAuthoring.state().dirty);}
  async function create(title){await page.locator('[data-left-panel="contents"]').click();await page.locator('#studio-create-page').click();await page.locator('#dialog[open]').waitFor();await page.locator('#dialog [name="type"]').selectOption('page');
   await page.locator('#dialog [name="studioPageTemplateId"]').selectOption(pipeline.defaultPageTemplateId);await page.locator('#dialog [name="title"]').fill(title);await page.locator('#dialog-submit').click();await page.locator('#dialog').waitFor({state:'hidden'});
   await waitFor(async()=>(await api('/api/state')).nodes.some(n=>n.title===title),'new Page '+title);const node=(await api('/api/state')).nodes.find(n=>n.title===title);await ready(node.id);return node;}
  const first=await create('Slot authoring first '+Date.now().toString(36));let state=await api('/api/state');
  check(contentOf(state,first.id).text===''&&contentOf(state,first.id).media==='','A new Page starts with empty primitive content slots from its selected foundation.');
  const baselineGeometry=geometry(JSON.parse(rowOf(state.pages[first.id]).values.layout));await selectSlots(first.id);
  check(await page.locator('[data-body-slot="article-text-slot"] [data-field="text"]').count()===1
   &&await page.locator('[data-body-slot="article-tool-slot"] [data-field="label"]').count()===1
   &&await page.locator('[data-body-slot="article-image-slot"] [data-field="mediaPath"]').count()===1,'The real Page inspector exposes text, selected tool fields and image controls for the named Body slots.');
  check(await page.locator('#properties-content [data-field="layout"]').count()===0&&await page.locator('#properties-content [data-field="widthPercent"]').count()===0
   &&await page.locator('[data-body-slot="article-tool-slot"] [data-field="start"]').count()===0,'Raw layout, geometry and structural numeric tool settings stay outside the Page content editor.');
  const firstText='Written through the Body content-slot inspector.',firstLabel='A Page-specific Counter label';
  await page.locator('[data-body-slot="article-text-slot"] [data-field="text"]').fill(firstText);await saved(s=>contentOf(s,first.id).text===firstText,'primitive text');await selectSlots(first.id);
  await page.locator('[data-body-slot="article-tool-slot"] [data-field="label"]').fill(firstLabel);await saved(s=>contentOf(s,first.id).label===firstLabel,'actual Counter label');await selectSlots(first.id);
  const chooser=page.waitForEvent('filechooser');await page.locator('[data-body-slot="article-image-slot"]').getByRole('button',{name:'Import local media'}).click();await(await chooser).setFiles(picture);
  await waitFor(async()=>contentOf(await api('/api/state'),first.id).media.startsWith('/media/'),'owned slot image import');await page.waitForFunction(()=>!window.__studioAuthoring.state().dirty);
  await selectSlots(first.id);await page.locator('[data-body-slot="article-image-slot"] [data-field="alt"]').fill('The photograph for this Page');await saved(s=>contentOf(s,first.id).alt==='The photograph for this Page','image description');
  state=await api('/api/state');const firstContent=contentOf(state,first.id);check(fs.existsSync(path.join(root,'Media',firstContent.media.slice('/media/'.length))),'The image picker imports the image into this private project and saves its local media reference.');
  check(JSON.stringify(geometry(JSON.parse(rowOf(state.pages[first.id]).values.layout)))===JSON.stringify(baselineGeometry),'Writing slot content preserves every element’s stored arrangement, dimensions, styling and resolved tool identity.');
  await page.reload({waitUntil:'domcontentloaded'});await page.waitForFunction(()=>window.__studioAuthoring?.state().ready);const row=await selectSlots(first.id);
  check(await page.locator('[data-body-slot="article-text-slot"] [data-field="text"]').inputValue()===firstText
   &&await page.locator('[data-body-slot="article-tool-slot"] [data-field="label"]').inputValue()===firstLabel
   &&await page.locator('[data-body-slot="article-image-slot"] [data-field="mediaPath"]').inputValue()===firstContent.media,'Reloading the real writer restores the saved text, tool field and Page-specific image.');
  await frame.locator('[data-block-id="'+row.id+'"] [data-header-element="article-image-slot"] img').evaluate(img=>img.decode());
  check((await frame.locator('[data-block-id="'+row.id+'"] [data-header-element="article-text-slot"]').textContent()).includes(firstText)
   &&(await frame.locator('[data-block-id="'+row.id+'"] [data-header-element="article-tool-slot"] .hl-counter').textContent()).includes(firstLabel),'The actual Work renderer displays authored primitive and nested template-slot content.');
  const beforeForge=await api('/api/state');await frame.locator('body').evaluate((body,blockId)=>{const owner=window.frameElement;window.parent.postMessage({source:'hyperlib-renderer',kind:'change',pageId:owner.dataset.pageId,generationId:owner.dataset.generationId,blockId,field:'layout',value:'Forged shared-layout replacement'},location.origin);},row.id);
  await page.waitForTimeout(800);check((await api('/api/state')).revision===beforeForge.revision&&!await page.evaluate(()=>window.__studioAuthoring.state().dirty),'A forged renderer message cannot overwrite serialized Body structure or create a dirty content draft.');
  const shot=path.join(output,'body-slots-authored-first.png');await page.screenshot({path:shot,fullPage:true});report.screenshots.push(shot);
  const second=await create('Slot authoring second '+Date.now().toString(36));state=await api('/api/state');
  check(contentOf(state,second.id).text===''&&contentOf(state,second.id).media===''&&contentOf(state,second.id).label!==firstLabel,'A second Page gets its own empty slots and original tool defaults rather than inheriting another Page’s content.');
  await selectSlots(second.id);const secondText='Independent writing on the second Page.';await page.locator('[data-body-slot="article-text-slot"] [data-field="text"]').fill(secondText);await saved(s=>contentOf(s,second.id).text===secondText,'second Page text');
  state=await api('/api/state');check(JSON.stringify(contentOf(state,first.id))===JSON.stringify(firstContent),'Editing the second Page leaves the first Page’s writing, tool field, picture and description unchanged.');
  const secondContent=contentOf(state,second.id),oldPadding=JSON.parse(rowOf(state.pages[first.id]).values.layout).padding;await page.evaluate(()=>window.__slotStreams.forEach(stream=>stream.close()));
  const restage=await new Promise((resolve,reject)=>{const child=spawn(verifier,['--studio-footer-restage-only',workRoot,sourceLibrary],{cwd:root,windowsHide:true,stdio:['ignore','pipe','pipe'],env:{...process.env,DOTNET_PROCESSOR_COUNT:'2'}});let text='';child.stdout.on('data',b=>text+=b);child.stderr.on('data',b=>text+=b);child.once('error',reject);child.once('exit',code=>code===0?resolve(text):reject(new Error('Native restaging failed '+code+': '+text)));});
  check(restage.includes('STUDIO_SLOTS_RESTAGED'),'The native explicit foundation update changes shared guidance and structure in this private Work.');
  state=await api('/api/state');check(JSON.stringify(contentOf(state,first.id))===JSON.stringify(firstContent)&&JSON.stringify(contentOf(state,second.id))===JSON.stringify(secondContent),'Reusable foundation propagation preserves independently authored content on both Pages.');
  check(JSON.parse(rowOf(state.pages[first.id]).values.layout).padding===(oldPadding<200?oldPadding+1:199)
   &&slotOf(state,first.id,'article-text-slot').slotHint==='Updated reusable layout guidance. Content stays with each Page.'
   &&slotOf(state,second.id,'article-image-slot').slotHint==='Updated reusable layout guidance. Content stays with each Page.','Updated row padding and slot guidance reach existing Pages without replacing their writing or media.');
  check(await page.evaluate(()=>window.__studioAuthoring.refreshFoundation('qa-slot-foundation')),'The still-open writer accepts the native foundation refresh without discarding saved Page content.');
  await selectSlots(second.id);check((await page.locator('[data-body-slot="article-text-slot"]').textContent()).includes('Updated reusable layout guidance. Content stays with each Page.')
   &&await page.locator('[data-body-slot="article-text-slot"] [data-field="text"]').inputValue()===secondText,'The refreshed inspector shows new foundation guidance alongside the current Page’s preserved content.');
  check(originalContent.every(p=>state.pages[p.id]&&JSON.stringify(contentOf(state,p.id))===JSON.stringify(p.content)&&JSON.stringify(state.pages[p.id].sources)===JSON.stringify(p.sources)),
   'The existing fixture Pages retain their original slot content and source citations after the writer workflow and foundation update.');
  check(report.errors.length===0,'The real slot-authoring workflow produces no uncaught browser errors. '+report.errors.join('; '));
  fs.writeFileSync(path.join(output,'body-slots-authoring-result.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({result:report.result,assertions:report.checks.length,firstPage:first.id,secondPage:second.id}));
 }catch(error){fs.writeFileSync(path.join(output,'body-slots-authoring-host.log'),logs);if(page)await page.screenshot({path:path.join(output,'body-slots-authoring-failure.png'),fullPage:true}).catch(()=>{});throw error;}
 finally{if(browser)await browser.close();if(host.exitCode===null)spawnSync('taskkill.exe',['/PID',String(host.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'});}
})().catch(error=>{console.error(error.stack||String(error));process.exitCode=1;});
