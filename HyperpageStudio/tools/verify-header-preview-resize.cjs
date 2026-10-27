const fs=require('fs'),path=require('path');
const {chromium}=require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const [file,output]=process.argv.slice(2),config=JSON.parse(fs.readFileSync(file,'utf8'));
 const browser=await chromium.launch({headless:true,executablePath:'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe'});
 const report={result:'HYPERLIB_HEADER_PREVIEW_RESIZE_BROWSER_OK',checks:[],screenshots:[]};
 const check=(yes,message)=>{if(!yes)throw new Error(message);report.checks.push(message);};let page;const diagnostics={console:[],errors:[],responses:[]};
 try{
  page=await browser.newPage({viewport:{width:1280,height:1000}});const errors=[];page.on('pageerror',e=>{errors.push(e.message);diagnostics.errors.push(e.stack);});
  page.on('console',message=>{if(message.type()==='error'||message.type()==='warning')diagnostics.console.push({type:message.type(),text:message.text()});});
  page.on('response',response=>{if(response.status()>=400)diagnostics.responses.push({url:response.url(),status:response.status()});});
  await page.addInitScript({content:'window.__resizeMessages=[];window.chrome??={};window.chrome.webview={postMessage:m=>window.__resizeMessages.push(JSON.parse(JSON.stringify(m)))};\n'+fs.readFileSync(config.dragScript,'utf8')});
  let generation=0;
  const element=id=>page.locator(`[data-header-element="${id}"]`);
  const drops=()=>page.evaluate(()=>window.__resizeMessages.filter(m=>m.type==='studio-header-resize'));
  async function arm(region='header'){return page.evaluate(c=>window.hyperlibGuidedHeaderDrag.configure(c),{token:'resize-'+(++generation),generation,documentId:'resize-document',region});}
  async function open(url=config.staticUrl){await page.goto(url,{waitUntil:'networkidle'});generation=0;await page.locator('[data-guided-page-preview]').waitFor();
   if(new URL(url).pathname.startsWith('/interactive/'))await page.waitForFunction(()=>document.documentElement.dataset.guidedInteractiveReady==='true');
   await arm();}
  async function rect(id){return element(id).evaluate(n=>{const b=n.getBoundingClientRect(),p=n.parentElement,r=p.getBoundingClientRect();return {left:b.left,top:b.top,right:b.right,bottom:b.bottom,width:b.width,height:b.height,parentLeft:r.left+p.clientLeft,parentTop:r.top+p.clientTop,parentWidth:p.clientWidth,parentHeight:p.clientHeight,style:n.style.cssText};});}
  async function handlePoint(id,handle){await element(id).scrollIntoViewIfNeeded();const b=await rect(id);await page.mouse.move(b.left+b.width/2,b.top+Math.min(14,b.height/2));const h=page.locator('#studio-header-preview-resize [data-header-resize-handle="'+handle+'"]');await h.waitFor({state:'visible'});const p=await h.boundingBox();return {x:p.x+p.width/2,y:p.y+p.height/2};}
  async function resize(id,handle,dx,dy){const start=await handlePoint(id,handle);await page.mouse.move(start.x,start.y);await page.mouse.down();await page.mouse.move(start.x+dx,start.y+dy,{steps:8});await page.mouse.up();return (await drops()).at(-1);}
  for(const handle of ['n','ne','e','se','s','sw','w','nw']){
   await open();const before=await rect('drag-text');
   const message=await resize('drag-text',handle,handle.includes('w')?-20:handle.includes('e')?20:0,handle.includes('n')?-10:handle.includes('s')?15:0);
   const after=await rect('drag-text');
   check(message?.handle===handle&&message.elementId==='drag-text'&&message.parentId==='drag-row'&&message.region==='header'
    &&message.previous.widthMode==='pixels'&&message.previous.widthPixels===150&&message.previous.height===48&&message.previous.renderedWidth===150,
    handle+': a real edge/corner pointer resize sends one capability-protected commit with the original typed geometry.');
   check(Math.abs(after.width-(before.width+(/[ew]/.test(handle)?20:0)))<1&&Math.abs(after.height-(before.height+(/[ns]/.test(handle)?handle.includes('n')?10:15:0)))<1
    &&(!handle.includes('w')||Math.abs(after.right-before.right)<1)&&(!handle.includes('n')||Math.abs(after.bottom-before.bottom)<1),
    handle+': the dragged edges change while the opposite edges remain anchored.');
   check((await drops()).length===1,'A completed '+handle+' resize contributes one model commit.');
  }
  await open();await resize('drag-text','se',-500,-400);const minimum=await rect('drag-text');
  check(Math.abs(minimum.width-16)<1&&Math.abs(minimum.height-16)<1,'Shrinking past the opposite corner respects the 16px minimum border-box size.');
  await open();await resize('drag-text','se',800,500);const bounded=await rect('drag-text');
  check(bounded.right<=bounded.parentLeft+bounded.parentWidth+1&&bounded.bottom<=bounded.parentTop+bounded.parentHeight+1,'A corner resize clamps the whole element to its padded parent content plane.');
  await open();await resize('nested-text','nw',-300,-300);const nested=await rect('nested-text');
  check(nested.left>=nested.parentLeft-1&&nested.top>=nested.parentTop-1&&(await drops()).at(-1).parentId==='drag-box','A nested resize clamps against the immediate Box, including its padding, instead of the outer row.');
  await open();await element('nested-text').scrollIntoViewIfNeeded();let edge=await rect('nested-text');
  await page.mouse.move(edge.left+edge.width/2,edge.top+12);await page.mouse.move(edge.left-6,edge.top+edge.height/2);
  check(await page.locator('#studio-header-preview-resize').evaluate((overlay,left)=>!overlay.hidden&&Math.abs(overlay.getBoundingClientRect().left-left)<1,edge.left),
   'The resize frame remains attached to a nested child across the small gap outside its edge instead of jumping to its Box.');
  await open();await resize('padding-floor-box','se',-500,-500);let floorBox=await rect('padding-floor-box');
  check(Math.abs(floorBox.width-40)<1&&Math.abs(floorBox.height-40)<1&&(await drops()).at(-1).width===40,
   'A padded Box stops at its physical 18px padding plus 2px border on each side, instead of storing an impossible 16px rectangle.');
  await open(config.floorUrl);await resize('padding-floor-box','se',30,20);floorBox=await rect('padding-floor-box');
  check(Math.abs(floorBox.width-70)<1&&Math.abs(floorBox.height-60)<1&&(await drops()).at(-1).previous.widthPixels===40,
   'A saved minimum-size padded Box can immediately be resized again with an accurate typed and rendered baseline.');
  await open();await page.evaluate(()=>window.__resizeNested=document.querySelector('[data-header-element="nested-text"]'));
  const boxBefore=await rect('drag-box');const boxMessage=await resize('drag-box','se',40,25);const boxAfter=await rect('drag-box');
  check(boxMessage.previous.widthMode==='percent'&&Math.abs(boxMessage.width/boxMessage.parentWidth*100-boxAfter.width/boxAfter.parentWidth*100)<.15&&boxAfter.width>boxBefore.width+30,
   'A Box keeps responsive percentage width while its pointer resize updates its actual bounds.');
  check(await element('nested-text').evaluate(n=>n===window.__resizeNested&&Number(n.dataset.headerX)===6&&Number(n.dataset.headerY)===10),'Resizing a Box retains its children, their instances and their stored local positions.');
  await open();await element('padded-image').locator('img').evaluate(n=>n.decode());const mediaMessage=await resize('padded-image','e',35,0);const media=await rect('padded-image');
  check(mediaMessage.previous.height===100&&Math.abs(mediaMessage.previous.renderedHeight-78)<.1&&Math.abs(media.height-78)<.1&&media.width>130
   &&media.bottom<=media.parentTop+media.parentHeight+1,'A padded image resizes horizontally while retaining its constrained 78px rendered height and original 100px model height.');
  await open();const autoMessage=await resize('auto-text','e',30,0);
  check(autoMessage.previous.height===0&&await element('auto-text').evaluate(n=>n.style.height==='auto'),'A width-only resize keeps an auto-height text element natural.');
  await open();const autoVertical=await resize('auto-text','s',0,30);
  check(autoVertical.previous.height===0&&autoVertical.height>30&&await element('auto-text').evaluate(n=>n.style.height.endsWith('px')),'Resizing an auto-height element vertically supplies an explicit pixel height.');
  await open();const flowBefore=await rect('horizontal-text'),flowMessage=await resize('horizontal-text','e',55,0),flowAfter=await rect('horizontal-text');
  check(flowMessage.width>220&&flowMessage.x===flowMessage.previous.x&&flowMessage.y===flowMessage.previous.y&&Math.abs(flowAfter.left-flowBefore.left)<1
   &&await element('horizontal-text').evaluate(n=>getComputedStyle(n).position==='relative'),'Side-by-side flow resizes dimensions without becoming a free-placement drag.');
  await open();const nextBefore=await rect('aligned-text');await resize('vertical-text','s',0,110);const nextAfter=await rect('aligned-text');
  check(nextAfter.top>nextBefore.top+50,'Increasing a stacked-flow element grows its row and moves the following row down.');
  await open();const canceled=await rect('drag-text'),count=(await drops()).length,p=await handlePoint('drag-text','se');
  await page.mouse.move(p.x,p.y);await page.mouse.down();await page.mouse.move(p.x+40,p.y+30,{steps:4});await page.keyboard.press('Escape');await page.mouse.up();
  check((await rect('drag-text')).style===canceled.style&&(await drops()).length===count,'Escape rolls back every temporary resize property without a commit.');
  await arm();const q=await handlePoint('drag-text','se');await page.mouse.move(q.x,q.y);await page.mouse.down();await page.mouse.move(q.x+35,q.y+20,{steps:4});
  await page.evaluate(()=>window.hyperlibGuidedHeaderDrag.configure(null));await page.mouse.up();
  check((await rect('drag-text')).style===canceled.style&&(await drops()).length===count,'A native draft invalidation rolls back an in-progress resize and prevents its stale drop.');
  const staticShot=path.join(output,'header-preview-resize-static.png');await page.screenshot({path:staticShot,fullPage:true});report.screenshots.push(staticShot);
  await open(config.interactiveUrl);await page.waitForFunction(()=>document.documentElement.dataset.guidedInteractiveReady==='true');
  const counter=element('drag-counter').locator('.hl-counter'),wave=element('drag-wave').locator('.hl-wave');
  await counter.locator('[data-action=add]').click();await counter.locator('output').filter({hasText:/^8$/}).waitFor();
  await wave.getByRole('button',{name:'Shift phase'}).click();await wave.locator('output').filter({hasText:'1 shifts'}).waitFor();
  await page.evaluate(()=>{window.__resizeCounter=document.querySelector('[data-header-element="drag-counter"] .hl-counter');window.__resizeWave=document.querySelector('[data-header-element="drag-wave"] .hl-wave');window.__resizeDocument=document;});
  const toolMessage=await resize('drag-counter','se',35,25);
  check(toolMessage.elementId==='drag-counter'&&await counter.locator('output').textContent()==='8'&&(await wave.locator('output').textContent()).includes('1 shifts'),'Resizing a real library tool preserves its working JavaScript and C# state.');
  await arm();await resize('drag-text','se',70,32);await page.evaluate(()=>window.hyperlibGuidedHeaderDrag.configure(null));
  check(await page.evaluate(id=>window.hyperlibGuidedPreview.update(id,200),config.resizeRequestId),'The actual component circuit acknowledges the committed resize payload.');
  check(await element('drag-text').evaluate(n=>Number(n.dataset.headerWidthPixels)===220&&Number(n.dataset.headerHeight)===80)
   &&await counter.locator('output').textContent()==='8'&&(await wave.locator('output').textContent()).includes('1 shifts')
   &&await page.evaluate(()=>document===window.__resizeDocument&&document.querySelector('[data-header-element="drag-counter"] .hl-counter')===window.__resizeCounter&&document.querySelector('[data-header-element="drag-wave"] .hl-wave')===window.__resizeWave),
   'Committed dimensions reach the real DOM before acknowledgment while the document and tool instances stay alive.');
  const interactiveShot=path.join(output,'header-preview-resize-interactive.png');await page.screenshot({path:interactiveShot,fullPage:true});report.screenshots.push(interactiveShot);
  for(let attempt=0;attempt<3;attempt++){
   const startup=await browser.newPage({viewport:{width:960,height:760}});
   try{
    await startup.goto(config.navigationUrl,{waitUntil:'domcontentloaded'});
    await startup.waitForFunction(()=>document.documentElement.dataset.guidedInteractiveReady==='true');
    const startupWave=startup.locator('[data-header-element="drag-wave"] .hl-wave');
    await startupWave.getByRole('button',{name:'Shift phase'}).click();
    await startupWave.locator('output').filter({hasText:'1 shifts'}).waitFor();
    check(true,'Fresh Navigation startup '+(attempt+1)+' connects after asynchronous Page loading and runs a real C# event without any retry.');
   }finally{await startup.close();}
  }
  await open(config.navigationUrl);await page.waitForFunction(()=>document.documentElement.dataset.guidedInteractiveReady==='true');await arm('navigation');
  check(await element('navigation-links').locator('nav.studio-navigation-links a[href]').count()>0,'The Navigation builder renders its real reader links through the shared component.');
  check((await page.locator('[data-region=main]').textContent()).includes('Body-only region marker')
   &&!(await page.locator('[data-region=navigation]').textContent()).includes('Body-only region marker'),
   'Navigation retains its own appearance region and never duplicates Body text or chrome.');
  check(!await element('drag-text').evaluate(n=>n.hasAttribute('data-preview-header-resizable'))&&await element('navigation-links').evaluate(n=>n.hasAttribute('data-preview-header-resizable')),'A Navigation capability enables only the current Navigation layout, keeping Header objects protected.');
  const navResize=await resize('navigation-links','se',30,20);
  check(navResize.region==='navigation'&&navResize.elementId==='navigation-links','A Navigation resize carries its own region identity and cannot be mistaken for a Header edit.');
  await page.setViewportSize({width:600,height:900});const menu=element('navigation-links').locator('.studio-nav-menu');await menu.waitFor({state:'visible'});
  check(!await menu.evaluate(node=>node.hasAttribute('@onclick')),'Navigation menu markup binds a real Blazor event rather than a literal Razor attribute.');
  await menu.click();await page.waitForFunction(()=>document.querySelector('[data-header-element="navigation-links"] .studio-nav-menu')?.getAttribute('aria-expanded')==='true');
  check(await menu.getAttribute('aria-expanded')==='true','The real responsive Navigation menu remains interactive after resizing.');
  const navShot=path.join(output,'navigation-preview-resize.png');await page.screenshot({path:navShot,fullPage:true});report.screenshots.push(navShot);
  check(errors.length===0,'Static, interactive and Navigation resizing have no uncaught browser errors. '+errors.join('; '));
  fs.writeFileSync(path.join(output,'header-preview-resize-browser-result.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({result:report.result,assertions:report.checks.length}));
 }catch(error){if(page){diagnostics.state=await page.evaluate(()=>({url:location.href,ready:document.documentElement.dataset.guidedInteractiveReady,html:document.documentElement.outerHTML.slice(-6000)})).catch(()=>null);await page.screenshot({path:path.join(output,'header-preview-resize-failure.png'),fullPage:true}).catch(()=>{});}fs.writeFileSync(path.join(output,'header-preview-resize-failure.json'),JSON.stringify(diagnostics,null,2));throw error;}finally{await browser.close();}
})().catch(e=>{console.error(e.stack||String(e));process.exitCode=1;});
