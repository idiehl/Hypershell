const fs=require('fs'),path=require('path');
const {chromium}=require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
(async()=>{
 const [file,output]=process.argv.slice(2),config=JSON.parse(fs.readFileSync(file,'utf8'));
 const browser=await chromium.launch({headless:true,executablePath:'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe'});
 const report={result:'STUDIO_FOOTER_PREVIEW_BROWSER_OK',checks:[],screenshots:[]},errors=[];let page,generation=0;
 const check=(yes,message)=>{if(!yes)throw new Error(message);report.checks.push(message);};
 try{
  page=await browser.newPage({viewport:{width:1280,height:950}});page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript({content:'window.__footerMessages=[];window.chrome??={};window.chrome.webview={postMessage:m=>window.__footerMessages.push(JSON.parse(JSON.stringify(m)))};\n'+fs.readFileSync(config.dragScript,'utf8')});
  const item=(id,region='footer')=>page.locator('[data-studio-region="'+region+'"] [data-header-element="'+id+'"]');
  const messages=type=>page.evaluate(type=>window.__footerMessages.filter(m=>m.type===type),type);
  async function arm(region='footer'){await page.evaluate(c=>window.hyperlibGuidedHeaderDrag.configure(c),{token:'footer-'+(++generation),generation,documentId:'footer-document',region});}
  async function open(url=config.staticUrl){await page.goto(url,{waitUntil:'networkidle'});generation=0;await page.locator('[data-guided-page-preview]').waitFor();
   if(new URL(url).pathname.startsWith('/interactive/'))await page.waitForFunction(()=>document.documentElement.dataset.guidedInteractiveReady==='true');await arm();}
  async function bounds(id){return item(id).evaluate(n=>{const b=n.getBoundingClientRect(),p=n.parentElement,r=p.getBoundingClientRect();return {left:b.left,top:b.top,right:b.right,bottom:b.bottom,width:b.width,height:b.height,parentLeft:r.left+p.clientLeft,parentTop:r.top+p.clientTop,parentWidth:p.clientWidth,parentHeight:p.clientHeight};});}
  async function drag(id,dx,dy){await item(id).scrollIntoViewIfNeeded();const b=await bounds(id),x=b.left+Math.min(35,b.width/2),y=b.top+Math.min(16,b.height/2);await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x+dx,y+dy,{steps:7});await page.mouse.up();return (await messages('studio-header-drag')).at(-1);}
  async function resize(id,handle,dx,dy){await item(id).scrollIntoViewIfNeeded();const b=await bounds(id);await page.mouse.move(b.left+b.width/2,b.top+Math.min(13,b.height/2));const grip=page.locator('#studio-header-preview-resize [data-header-resize-handle="'+handle+'"]');await grip.waitFor({state:'visible'});const g=await grip.boundingBox(),x=g.x+g.width/2,y=g.y+g.height/2;await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x+dx,y+dy,{steps:7});await page.mouse.up();return (await messages('studio-header-resize')).at(-1);}
  await open();
  check(await item('shared-text','header').count()===1&&await item('shared-text','body').count()===1&&await item('shared-text').count()===1,'Header, Body and Footer retain independent matching row and element IDs.');
  check(await item('shared-text').evaluate(n=>n.hasAttribute('data-preview-header-draggable')&&n.hasAttribute('data-preview-header-resizable'))
   &&!await item('shared-text','header').evaluate(n=>n.hasAttribute('data-preview-header-resizable'))&&!await item('shared-text','body').evaluate(n=>n.hasAttribute('data-preview-header-resizable')),'Footer capabilities protect the visible Header and Body objects.');
  const header=await item('shared-text','header').evaluate(n=>n.outerHTML),body=await item('shared-text','body').evaluate(n=>n.outerHTML),before=await bounds('shared-text'),move=await drag('shared-text',30,20),after=await bounds('shared-text');
  check(move?.region==='footer'&&move.parentId==='shared-row'&&after.left>before.left+25&&after.top>before.top+15,'A real Footer drag commits geometry with its distinct region identity.');
  check(await item('shared-text','header').evaluate(n=>n.outerHTML)===header&&await item('shared-text','body').evaluate(n=>n.outerHTML)===body,'A Footer drag leaves identically named Header and Body elements unchanged.');
  for(const handle of ['n','ne','e','se','s','sw','w','nw']){await open();const b=await bounds('shared-text');const message=await resize('shared-text',handle,handle.includes('w')?-16:handle.includes('e')?16:0,handle.includes('n')?-10:handle.includes('s')?12:0),a=await bounds('shared-text');
   check(message?.region==='footer'&&message.handle===handle&&message.previous.widthPixels===190&&message.previous.height===52
    &&Math.abs(a.width-(b.width+(/[ew]/.test(handle)?16:0)))<1&&Math.abs(a.height-(b.height+(/[ns]/.test(handle)?handle.includes('n')?10:12:0)))<1,'Footer '+handle+' resizing uses the actual pointer handle and typed baseline.');}
  await open();await resize('footer-child','se',600,400);const nested=await bounds('footer-child');
  check(nested.right<=nested.parentLeft+nested.parentWidth+1&&nested.bottom<=nested.parentTop+nested.parentHeight+1,'Nested Footer resizing respects the immediate padded Box.');
  await open();await item('footer-image').locator('img').evaluate(n=>n.decode());await page.evaluate(()=>{window.__footerDocument=document;window.__footerImage=document.querySelector('[data-studio-region="footer"] img');window.__footerHeader=document.querySelector('[data-studio-region="header"]');});
  check(await page.evaluate(script=>eval(script),config.staticPatch),'The exact static renderer patches only the Footer region.');await arm();
  check((await item('shared-text').innerText())==='Live footer identity'&&Math.abs((await bounds('shared-text')).width-225)<1
   &&await page.evaluate(()=>document===window.__footerDocument&&document.querySelector('[data-studio-region="footer"] img')===window.__footerImage&&document.querySelector('[data-studio-region="header"]')===window.__footerHeader),'Live Footer text and size changes retain the document, media and Header instances.');
  const staticShot=path.join(output,'footer-preview-static.png');await page.screenshot({path:staticShot,fullPage:true});report.screenshots.push(staticShot);
  await open(config.interactiveUrl);const counter=item('footer-counter').locator('.hl-counter'),wave=item('footer-wave').locator('.hl-wave');
  await counter.locator('[data-action=add]').click();await counter.locator('output').filter({hasText:/^8$/}).waitFor();await wave.getByRole('button',{name:'Shift phase'}).click();await wave.locator('output').filter({hasText:'1 shifts'}).waitFor();
  check(await item('footer-tool-box').locator('[data-studio-shared-template]').count()===2,'Nested Footer Boxes render the actual selected library objects.');
  const steps=item('footer-navigation').locator('[data-studio-nav-preview-steps]');
  check(await steps.count()===1&&(await steps.innerText()).includes('Previous Page')&&(await steps.innerText()).includes('Next Page')
   &&await steps.locator('a[href]').count()===0&&await page.locator('[data-studio-nav-preview-steps]').count()===1,'Only the requested Navigation element displays non-navigating previous/next Page design guides.');
  const address=page.url();await steps.getByText('Previous Page').click();check(page.url()===address,'Preview adjacent-Page guides cannot navigate away from the current builder document.');
  await page.evaluate(()=>{window.__footerDocument=document;window.__footerCounter=document.querySelector('[data-header-element="footer-counter"] .hl-counter');window.__footerWave=document.querySelector('[data-header-element="footer-wave"] .hl-wave');});
  await resize('footer-counter','se',25,20);await arm();await drag('shared-text',18,14);await page.evaluate(()=>window.hyperlibGuidedHeaderDrag.configure(null));
  check(await page.evaluate(id=>window.hyperlibGuidedPreview.update(id,400),config.committedRequestId),'The real Footer component circuit acknowledges committed pointer geometry.');
  check(await item('shared-text').evaluate(n=>Number(n.dataset.headerX)===12&&Number(n.dataset.headerY)===18&&Number(n.dataset.headerWidthPixels)===240&&Number(n.dataset.headerHeight)===80)
   &&await counter.locator('output').textContent()==='8'&&(await wave.locator('output').textContent()).includes('1 shifts')
   &&await page.evaluate(()=>document===window.__footerDocument&&document.querySelector('[data-header-element="footer-counter"] .hl-counter')===window.__footerCounter&&document.querySelector('[data-header-element="footer-wave"] .hl-wave')===window.__footerWave),'Footer updates preserve live nested JavaScript/C# tool instances and state.');
  check(await page.evaluate(id=>window.hyperlibGuidedPreview.update(id,401),config.withoutStepsRequestId)&&await page.locator('[data-studio-nav-preview-steps]').count()===0,'Disabling ShowPreviousNext removes the guides without adding fake Page buttons elsewhere.');
  await arm('body');check(!await item('shared-text').evaluate(n=>n.hasAttribute('data-preview-header-resizable'))&&await item('shared-text','body').evaluate(n=>n.hasAttribute('data-preview-header-resizable')),'Leaving Footer editing protects its objects while Body receives its own capability.');
  await page.setViewportSize({width:700,height:950});const interactiveShot=path.join(output,'footer-preview-interactive-narrow.png');await page.screenshot({path:interactiveShot,fullPage:true});report.screenshots.push(interactiveShot);
  check(errors.length===0,'Static and interactive Footer editing has no uncaught browser errors. '+errors.join('; '));
  fs.writeFileSync(path.join(output,'footer-preview-browser-result.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({result:report.result,assertions:report.checks.length}));
 }catch(error){if(page)await page.screenshot({path:path.join(output,'footer-preview-failure.png'),fullPage:true}).catch(()=>{});fs.writeFileSync(path.join(output,'footer-preview-errors.json'),JSON.stringify(errors,null,2));throw error;}finally{await browser.close();}
})().catch(error=>{console.error(error.stack||String(error));process.exitCode=1;});
