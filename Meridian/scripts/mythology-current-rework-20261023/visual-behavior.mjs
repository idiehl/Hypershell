import {readFile,writeFile} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
import {chromium} from '@playwright/test';
const out='I:/dev/Meridian/output/mythology-current-rework-20261023/visual';
const p=JSON.parse(await readFile(out+'/draft.json','utf8'));
const report={checks:[],browserErrors:[],imagePages:[],notes:[]};
const check=(name,pass,detail)=>{report.checks.push({name,pass,detail});if(!pass)throw new Error(name);};
const browser=await chromium.launch({headless:true,executablePath:'I:/dev/Meridian/output/mcp-browser-runtime/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe'});
try{
 const page=await browser.newPage({viewport:{width:1600,height:1000}});page.on('pageerror',e=>report.browserErrors.push(e.message));await page.route(/^https?:/,r=>r.abort());
 const focus=async id=>{await page.getByRole('button',{name:'Explore project',exact:true}).click();await page.getByRole('searchbox',{name:'Search project',exact:true}).fill(id);await page.locator('[data-navigation-node]').evaluateAll((items,id)=>{const x=items.find(el=>el.getAttribute('data-navigation-node')===id);if(!x)throw new Error(id);x.click();},id);await page.getByRole('button',{name:'Close project navigation',exact:true}).click();};
 await page.goto(pathToFileURL(out+'/review/publication.html').href,{waitUntil:'domcontentloaded'});
 for(const w of p.canvases){
  await page.getByLabel('Choose Work',{exact:true}).selectOption(w.id);
  const notes=p.nodes.find(n=>n.canvasId===w.id&&n.title==='Notes');
  await focus(notes.id);await page.locator(`[data-node-id="${notes.id}"]`).dblclick({position:{x:10,y:10}});
  const doc=p.nodes.find(n=>n.parentId===notes.id&&n.kind==='document'&&n.title==='Reading Notes');const a=p.assets.find(a=>a.id===doc.assetId);
  await page.getByRole('button',{name:`Read ${a.name} in this node`,exact:true}).click();
  await page.getByRole('region',{name:`Document viewer: ${a.name}`,exact:true}).waitFor();
  const text=await page.locator('.ms-document-open .mdv-viewer').textContent();check(w.title+' original reading retained',text.includes('Original record'));
  const scroll=await page.locator('.ms-document-open .mdv-scroll').evaluate(e=>{e.scrollTop=300;return{top:e.scrollTop,height:e.scrollHeight,client:e.clientHeight};});check(w.title+' reading scrolls',scroll.top>0,scroll);
  await page.getByRole('button',{name:'Open larger document reading view',exact:true}).click();await page.getByRole('dialog',{name:`Read ${a.name}`,exact:true}).waitFor();check(w.title+' larger reader opens',await page.getByRole('dialog',{name:`Read ${a.name}`,exact:true}).isVisible());await page.getByRole('button',{name:'Close attachment',exact:true}).click();
  await page.getByRole('button',{name:'Collapse document viewer',exact:true}).click();check(w.title+' inline reader closes',await page.locator('.ms-document-open').count()===0);
  const pane=page.locator(`[data-window-id="${notes.id}"]`);const movement=await pane.evaluate(el=>{const before=el.scrollTop;el.scrollTop=540;return{before,after:el.scrollTop};});check(w.title+' Notes window scrolls',movement.after>movement.before,movement);
  report.notes.push({workId:w.id,documentId:doc.id,readerCharacters:text.length,scroll,movement});
 }
 for(const gallery of p.nodes.filter(n=>n.containerVariant==='gallery')){
  await page.getByLabel('Choose Work',{exact:true}).selectOption(gallery.canvasId);
  await focus(gallery.id);
  const items=p.nodes.filter(n=>n.parentId===gallery.id);
  for(let i=0;i<items.length;i++){
   const g=page.locator(`[data-gallery-id="${gallery.id}"]`);await g.locator('img').evaluateAll(imgs=>Promise.all(imgs.map(img=>img.decode())));
   const info=await g.locator('[data-gallery-item-id]').evaluate(el=>({id:el.getAttribute('data-gallery-item-id'),images:[...el.querySelectorAll('img')].map(i=>({loaded:i.complete&&i.naturalWidth>0,width:i.naturalWidth,height:i.naturalHeight}))}));
   check(gallery.title+' page '+(i+1)+' decodes',info.id===items[i].id&&info.images.every(i=>i.loaded),info);report.imagePages.push({galleryId:gallery.id,...info});
   if(i<items.length-1)await g.locator('[data-gallery-control="next"]').click();
  }
 }
 await page.getByLabel('Choose Work',{exact:true}).selectOption(p.canvases[4].id);await focus('386a80ca-9ce9-4f52-af18-1f8b4b430108');
 const above=p.nodes.find(n=>n.title==='Above'&&n.canvasId===p.canvases[4].id);const year=page.locator('[data-node-id="386a80ca-9ce9-4f52-af18-1f8b4b430108"]');
 check('Divided Year initially disclosed',await page.locator(`[data-node-id="${above.id}"]`).count()===1);await year.dblclick({position:{x:10,y:10}});check('Divided Year collapses both shares',await page.locator(`[data-node-id="${above.id}"]`).count()===0);await year.dblclick({position:{x:10,y:10}});check('Divided Year restores both shares',await page.locator(`[data-node-id="${above.id}"]`).count()===1);
 await page.screenshot({path:out+'/behavior-final.png'});
 check('No viewer errors',report.browserErrors.length===0,report.browserErrors);
}finally{await browser.close();await writeFile(out+'/behavior.json',JSON.stringify(report,null,2));}
console.log(JSON.stringify({checks:report.checks.length,allPassed:report.checks.every(c=>c.pass),galleryPages:report.imagePages.length}));
