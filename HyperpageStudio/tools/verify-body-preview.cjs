const fs=require('fs'),path=require('path');
const {chromium}=require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const [file,output]=process.argv.slice(2),config=JSON.parse(fs.readFileSync(file,'utf8'));
 const browser=await chromium.launch({headless:true,executablePath:'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe'});
 const report={result:'STUDIO_BODY_PREVIEW_BROWSER_OK',checks:[],screenshots:[]},errors=[];let page,generation=0;
 const check=(yes,message)=>{if(!yes)throw new Error(message);report.checks.push(message);};
 try{
  page=await browser.newPage({viewport:{width:1280,height:950}});page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript({content:'window.__bodyMessages=[];window.chrome??={};window.chrome.webview={postMessage:m=>window.__bodyMessages.push(JSON.parse(JSON.stringify(m)))};\n'+fs.readFileSync(config.dragScript,'utf8')});
  const item=id=>page.locator('[data-studio-region="body"] [data-header-element="'+id+'"]');
  const header=()=>page.locator('[data-studio-region="header"] [data-header-element="body-text"]');
  const messages=type=>page.evaluate(type=>window.__bodyMessages.filter(m=>m.type===type),type);
  async function arm(region='body'){await page.evaluate(c=>window.hyperlibGuidedHeaderDrag.configure(c),{token:'body-'+(++generation),generation,documentId:'body-document',region});}
  async function open(url=config.staticUrl){await page.goto(url,{waitUntil:'networkidle'});generation=0;
   await page.locator('[data-guided-page-preview]').waitFor();if(new URL(url).pathname.startsWith('/interactive/'))await page.waitForFunction(()=>document.documentElement.dataset.guidedInteractiveReady==='true');await arm();}
  async function bounds(id){return item(id).evaluate(n=>{const b=n.getBoundingClientRect(),p=n.parentElement,r=p.getBoundingClientRect();return {left:b.left,top:b.top,right:b.right,bottom:b.bottom,width:b.width,height:b.height,parentLeft:r.left+p.clientLeft,parentTop:r.top+p.clientTop,parentWidth:p.clientWidth,parentHeight:p.clientHeight,style:n.style.cssText};});}
  async function drag(id,dx,dy){await item(id).scrollIntoViewIfNeeded();const b=await bounds(id),x=b.left+Math.min(35,b.width/2),y=b.top+Math.min(16,b.height/2);
   await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x+dx,y+dy,{steps:7});await page.mouse.up();return (await messages('studio-header-drag')).at(-1);}
  async function resize(id,handle,dx,dy){await item(id).scrollIntoViewIfNeeded();const b=await bounds(id);await page.mouse.move(b.left+b.width/2,b.top+Math.min(13,b.height/2));
   const grip=page.locator('#studio-header-preview-resize [data-header-resize-handle="'+handle+'"]');await grip.waitFor({state:'visible'});const g=await grip.boundingBox(),x=g.x+g.width/2,y=g.y+g.height/2;
   await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x+dx,y+dy,{steps:7});await page.mouse.up();return (await messages('studio-header-resize')).at(-1);}
  await open();
  check(await item('body-text-slot').getAttribute('data-content-slot')==='true'&&await item('body-text-slot').getAttribute('data-slot-name')==='Article title'
   &&(await item('body-text-slot').innerText()).includes('Article title')&&(await item('body-image-slot').innerText()).includes('Page photograph')
   &&await item('body-image-slot').locator('img').count()===0,'Empty Body text and media slots show clear design labels without inventing actual Page content.');
  check(await item('body-text').count()===1&&await header().count()===1,'Header and Body may reuse the same row and element IDs while retaining distinct rendered regions.');
  check(!await header().evaluate(n=>n.hasAttribute('data-preview-header-draggable')||n.hasAttribute('data-preview-header-resizable'))
   &&await item('body-text').evaluate(n=>n.hasAttribute('data-preview-header-draggable')&&n.hasAttribute('data-preview-header-resizable')),'A Body capability enables only Body geometry even when Header IDs match.');
  const originalHeader=await header().evaluate(n=>n.outerHTML),before=await bounds('body-text'),move=await drag('body-text',35,24),moved=await bounds('body-text');
  check(move?.region==='body'&&move.elementId==='body-text'&&move.parentId==='body-row'&&moved.left>before.left+30&&moved.top>before.top+20,'A real Body pointer drag emits its independent region, parent and changed geometry.');
  check(await header().evaluate(n=>n.outerHTML)===originalHeader,'Dragging a Body object cannot change an identically named Header object.');
  await open();await drag('body-child',-500,-300);const nested=await bounds('body-child');
  check(nested.left>=nested.parentLeft-1&&nested.top>=nested.parentTop-1&&(await messages('studio-header-drag')).at(-1).parentId==='body-box','Nested Body dragging clamps to the padded immediate Box.');
  for(const handle of ['n','ne','e','se','s','sw','w','nw']){
   await open();const before=await bounds('body-text');const message=await resize('body-text',handle,handle.includes('w')?-18:handle.includes('e')?18:0,handle.includes('n')?-10:handle.includes('s')?12:0),after=await bounds('body-text');
   check(message?.region==='body'&&message.handle===handle&&message.elementId==='body-text'&&message.previous.widthPixels===180&&message.previous.height===52
    &&Math.abs(after.width-(before.width+(/[ew]/.test(handle)?18:0)))<1&&Math.abs(after.height-(before.height+(/[ns]/.test(handle)?handle.includes('n')?10:12:0)))<1,
    'Body '+handle+' resizing uses the real pointer handle and retains the original typed baseline.');
  }
  await open();await resize('body-child','se',600,400);const child=await bounds('body-child');
  check(child.right<=child.parentLeft+child.parentWidth+1&&child.bottom<=child.parentTop+child.parentHeight+1,'Body resizing clamps the whole rectangle to the immediate padded Box.');
  await open();const flowBefore=await bounds('body-flow');await drag('body-flow',30,20);
  check((await messages('studio-header-drag')).length===0,'Side-by-side Body elements retain flow placement instead of becoming free drags.');
  const flowMessage=await resize('body-flow','e',35,0),flowAfter=await bounds('body-flow');
  check(flowMessage?.region==='body'&&flowAfter.width>flowBefore.width+30&&Math.abs(flowAfter.left-flowBefore.left)<1,'Flow Body elements resize their dimensions without changing placement.');
  await open();await item('body-image').locator('img').evaluate(n=>n.decode());
  await page.evaluate(()=>{window.__bodyDocument=document;window.__bodyHeader=document.querySelector('[data-studio-region="header"]');window.__bodyImage=document.querySelector('[data-studio-region="body"] [data-header-element="body-image"] img');});
  check(await page.evaluate(script=>eval(script),config.staticPatch),'The same shared renderer patches static Body rows without navigating.');await arm();
  check(await item('body-text').innerText()==='Body live text'&&Math.abs((await bounds('body-text')).width-230)<1
   &&await page.evaluate(()=>document===window.__bodyDocument&&document.querySelector('[data-studio-region="header"]')===window.__bodyHeader&&document.querySelector('[data-studio-region="body"] [data-header-element="body-image"] img')===window.__bodyImage),
   'Immediate Body text and size changes preserve the document, Header and existing media instance.');
  const staticShot=path.join(output,'body-preview-static.png');await page.screenshot({path:staticShot,fullPage:true});report.screenshots.push(staticShot);
  await open(config.interactiveUrl);
  check(await item('body-text-slot').locator('[data-studio-slot-placeholder]').count()===1&&await item('body-image-slot').locator('[data-studio-slot-placeholder]').count()===1,
   'The actual interactive Body renderer shows the same private empty-slot guides as the static preview.');
  const counter=item('body-counter').locator('.hl-counter'),wave=item('body-wave').locator('.hl-wave');
  await counter.locator('[data-action=add]').click();await counter.locator('output').filter({hasText:/^8$/}).waitFor();await wave.getByRole('button',{name:'Shift phase'}).click();await wave.locator('output').filter({hasText:'1 shifts'}).waitFor();
  await page.evaluate(()=>{window.__bodyDocument=document;window.__bodyCounter=document.querySelector('[data-header-element="body-counter"] .hl-counter');window.__bodyWave=document.querySelector('[data-header-element="body-wave"] .hl-wave');});
  await resize('body-counter','se',25,20);await arm();await drag('body-text',20,15);await page.evaluate(()=>window.hyperlibGuidedHeaderDrag.configure(null));
  check(await page.evaluate(id=>window.hyperlibGuidedPreview.update(id,300),config.committedRequestId),'The actual Body component circuit acknowledges committed drag and resize geometry.');
  check(await item('body-text').evaluate(n=>Number(n.dataset.headerX)===12&&Number(n.dataset.headerY)===16&&Number(n.dataset.headerWidthPixels)===240&&Number(n.dataset.headerHeight)===80)
   &&await counter.locator('output').textContent()==='8'&&(await wave.locator('output').textContent()).includes('1 shifts')
   &&await page.evaluate(()=>document===window.__bodyDocument&&document.querySelector('[data-header-element="body-counter"] .hl-counter')===window.__bodyCounter&&document.querySelector('[data-header-element="body-wave"] .hl-wave')===window.__bodyWave),
   'Committed Body updates preserve the document and live JavaScript/C# tool instances and state.');
  await arm('header');check(await header().evaluate(n=>n.hasAttribute('data-preview-header-resizable'))&&!await item('body-text').evaluate(n=>n.hasAttribute('data-preview-header-resizable')),'Switching to Header editing removes Body capabilities without confusing matching element IDs.');
  await arm();await page.setViewportSize({width:700,height:950});
  const interactiveShot=path.join(output,'body-preview-interactive-narrow.png');await page.screenshot({path:interactiveShot,fullPage:true});report.screenshots.push(interactiveShot);
  check(errors.length===0,'Static and interactive Body previews have no uncaught browser errors. '+errors.join('; '));
  fs.writeFileSync(path.join(output,'body-preview-browser-result.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({result:report.result,assertions:report.checks.length}));
 }catch(error){if(page)await page.screenshot({path:path.join(output,'body-preview-failure.png'),fullPage:true}).catch(()=>{});fs.writeFileSync(path.join(output,'body-preview-errors.json'),JSON.stringify(errors,null,2));throw error;}finally{await browser.close();}
})().catch(error=>{console.error(error.stack||String(error));process.exitCode=1;});
