const fs = require('fs');
const path = require('path');
const { chromium } = require('C:/Users/ihigg/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
async function verifyNavigation(page) {
 const before=page.url();let popups=0;const popup=()=>popups++;page.on('popup',popup);
 if(await page.locator('[data-block-type="studio-navigation"]').count())throw new Error('Synthetic Page navigation remains in the guided preview');
 await page.evaluate(()=>{
  window.__previewNavigationDocument=document;window.__previewNavigationRoot=document.querySelector('[data-guided-page-preview]');
  const controls=document.createElement('section');controls.id='preview-navigation-checks';
  controls.innerHTML='<a id="preview-root-link" href="/">Host root</a><a id="preview-missing-link" href="/page/unavailable">Unavailable Page</a><a id="preview-empty-link" href="#">Empty placeholder</a><a id="preview-window-link" href="/" target="_blank">Another window</a><a id="preview-anchor-link" href="#preview-real-anchor">Real anchor</a><div id="preview-real-anchor" style="margin-top:1400px;height:40px">Actual target</div>';
  document.body.append(controls);
 });
 for(const id of ['preview-root-link','preview-missing-link','preview-empty-link','preview-window-link']){
  await page.locator('#'+id).click();
  if(page.url()!==before||!await page.evaluate(()=>document===window.__previewNavigationDocument))throw new Error(id+' left the preview document');
 }
 await page.locator('#preview-anchor-link').click();
 if(!await page.evaluate(()=>scrollY>500&&location.hash==='#preview-real-anchor'&&document===window.__previewNavigationDocument&&document.querySelector('[data-guided-page-preview]')===window.__previewNavigationRoot))throw new Error('A real preview anchor did not scroll in the same document');
 if(popups)throw new Error('Preview navigation opened another window');
 await page.evaluate(before=>{history.replaceState(null,'',before);document.getElementById('preview-navigation-checks').remove();scrollTo(0,0);},before);
 page.off('popup',popup);
}
(async () => {
 const [url, output] = process.argv.slice(2);
 const browser = await chromium.launch({headless:true,executablePath:'C:/Users/ihigg/AppData/Local/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe'});
 try {
  const page = await browser.newPage({viewport:{width:900,height:920}});
  await page.goto(url,{waitUntil:'networkidle'});
  await page.locator('[data-guided-page-preview]').waitFor();
  const failedNavigations=[];page.on('response',response=>{if(response.request().isNavigationRequest()&&response.request().frame()===page.mainFrame()&&response.status()===404)failedNavigations.push(response.url());});
  await verifyNavigation(page);
  if(failedNavigations.length)throw new Error('Preview links triggered a 404 document request: '+failedNavigations.join(', '));
  const errors = await page.evaluate(() => {
    const errors=[];
    const root=document.querySelector('[data-guided-page-preview]');
    if(getComputedStyle(root).backgroundColor!=='rgb(57, 24, 39)') errors.push('latest background missing');
    if(!document.body.textContent.includes('Header revised while typing')) errors.push('latest header text missing');
    if(!document.querySelector('[data-region="header"] [data-block-type]')) errors.push('actual header component missing');
    if(document.querySelectorAll('[data-block-type]').length<3) errors.push('composed objects missing');
    const logo=document.querySelector('.studio-region-logo');
    if(!logo || !logo.complete || !logo.naturalWidth) errors.push('local logo did not load');
    if(logo && (logo.getBoundingClientRect().width>64.1 || logo.getBoundingClientRect().height>64.1)) errors.push('reusable logo escaped its compact bounds');
    const text=document.querySelector('[data-region="header"] .studio-region-text');
    const header=document.querySelector('[data-region="header"]');
    if(!text || text.getBoundingClientRect().width<header.clientWidth*.65) errors.push('header chrome does not span the Page grid');
    if(text && text.getBoundingClientRect().height>70) errors.push('header text wraps into excessive narrow lines at wide size');
    if(document.documentElement.scrollWidth>innerWidth+1) errors.push('preview overflows at wide size');
    return errors;
  });
  if(errors.length) throw new Error(errors.join('; '));
  await page.screenshot({path:path.join(output,'guided-page-builder-wide.png'),fullPage:true});
  await page.setViewportSize({width:420,height:820});
  if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1)) throw new Error('Preview overflows compact size');
  await page.screenshot({path:path.join(output,'guided-page-builder-compact.png'),fullPage:true});
  console.log('GUIDED_PREVIEW_BROWSER_OK navigation=contained anchors=scroll no404=true');
 } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
