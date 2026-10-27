import {readFile,writeFile,mkdir} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
import {chromium} from '@playwright/test';
import {buildPublication} from '../../src/publication.ts';
import {documentNodeAsset} from '../../src/core/document-node.ts';

const root=fileURLToPath(new URL('../../',import.meta.url));
const args=new Map();for(let i=2;i<process.argv.length;i+=2)args.set(process.argv[i],process.argv[i+1]);
const input=path.resolve(args.get('--input')??''),out=path.resolve(args.get('--out')??'output/mythology-refinement-20261023/review');
if(!args.has('--input'))throw new Error('Use --input draft.json --out directory [--plan plan.json].');
const bytes=await readFile(input),project=JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/,''));
const sourceHash=createHash('sha256').update(JSON.stringify(project)).digest('hex');
await mkdir(out,{recursive:true});
let plan=args.has('--plan')?JSON.parse(await readFile(path.resolve(args.get('--plan')),'utf8')):project.canvases.map(w=>({workId:w.id,label:w.title}));
if(!Array.isArray(plan))plan=plan.views??plan.captures??plan.entries;
if(!Array.isArray(plan))throw new Error('Review plan must be an array or contain views/captures/entries.');
for(const spec of plan)if(spec.expand!==undefined&&spec.expand!==false&&spec.expand!==true&&!Array.isArray(spec.expand))throw new Error('Review expansion must be true or an explicit array of node IDs.');
const [js,css]=await Promise.all(['viewer.js','scene.css'].map(name=>readFile(path.join(root,'output/app/wwwroot',name),'utf8')));
const baseFile=path.join(out,'publication.html');await writeFile(baseFile,buildPublication(project,js,css));
let expandedFile;
if(plan.some(v=>v.expand)){
 const expanded=structuredClone(project),parents=new Set(project.nodes.map(n=>n.parentId));
 const expandAll=plan.some(v=>v.expand===true),requested=new Set(plan.flatMap(v=>Array.isArray(v.expand)?v.expand:[]));
 // A specified disclosure path must not open thousands of unrelated archival
 // documents. Whole-project expansion is available explicitly with expand:true.
 for(const n of expanded.nodes)if((expandAll||requested.has(n.id))&&parents.has(n.id)&&n.kind!=='canvas'&&!documentNodeAsset(expanded,n)){
  if(n.collapsed&&n.containerSizes?.expanded)Object.assign(n,n.containerSizes.expanded);
  n.collapsed=false;
 }
 expandedFile=path.join(out,'publication-expanded.html');await writeFile(expandedFile,buildPublication(expanded,js,css));
}
const report={sourceFile:input,sourceHash,viewerSource:'installed output/app/wwwroot',views:[],browserErrors:[]};
const browser=await chromium.launch({headless:true,executablePath:'I:/dev/Meridian/output/mcp-browser-runtime/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe'});
try{
 const page=await browser.newPage({viewport:{width:1600,height:1000},deviceScaleFactor:1});
 page.on('pageerror',error=>report.browserErrors.push(error.message));
 await page.route(/^https?:/,route=>route.abort());
 let currentFile;
 for(let i=0;i<plan.length;i++){
  const spec=plan[i],workId=spec.workId??spec.id,work=project.canvases.find(w=>w.id===workId);
  if(!work)throw new Error(`Unknown review Work ${workId}`);
  const file=spec.expand?expandedFile:baseFile;
  if(file!==currentFile){await page.goto(pathToFileURL(file).href,{waitUntil:'domcontentloaded',timeout:120000});await page.getByLabel('Choose Work',{exact:true}).waitFor({timeout:60000});currentFile=file;}
  await page.getByLabel('Choose Work',{exact:true}).selectOption(workId);
  if(spec.nodeId){
   await page.getByRole('button',{name:'Explore project',exact:true}).click();
   await page.getByRole('searchbox',{name:'Search project',exact:true}).fill(spec.nodeId);
   await page.locator('[data-navigation-node]').evaluateAll((items,id)=>{const item=items.find(el=>el.getAttribute('data-navigation-node')===id);if(!item)throw new Error(`Focus node absent: ${id}`);item.click();},spec.nodeId);
   await page.getByRole('button',{name:'Close project navigation',exact:true}).click();
   if(spec.scope==='branch')await page.getByRole('button',{name:'Fit selected branch',exact:true}).click();
  }else await page.getByRole('button',{name:'Fit Work',exact:true}).click();
  if(spec.scale!==undefined){
   const current=await page.locator('.ms-world').evaluate(world=>Number(getComputedStyle(world).getPropertyValue('--ms-camera-scale')));
   const viewport=await page.locator('.ms-viewport').boundingBox();
   await page.mouse.move(viewport.x+viewport.width/2,viewport.y+viewport.height/2);
   await page.mouse.wheel(0,-Math.log(spec.scale/current)/.0013);
  }
  await page.evaluate(()=>document.fonts.ready);
  await page.locator('img').evaluateAll(images=>Promise.all(images.map(img=>img.decode().catch(()=>{}))));
  await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
  const screenshot=`${String(i+1).padStart(3,'0')}-${workId}${spec.expand?'-expanded':''}.png`;
  await page.screenshot({path:path.join(out,screenshot),timeout:60000});
  const metrics=await page.locator('.ms-node,[data-gallery-item-id]').evaluateAll(cards=>cards.map(card=>{
   const box=card.getBoundingClientRect(),title=card.querySelector('.ms-node-title,.ms-title'),body=card.querySelector('.ms-node-body'),style=getComputedStyle(card);
   return{id:card.getAttribute('data-node-id')??card.getAttribute('data-gallery-item-id'),title:title?.textContent,screen:{x:box.x,y:box.y,width:box.width,height:box.height},font:style.fontFamily,titleClipped:!!title&&(title.scrollWidth>title.clientWidth+2||title.scrollHeight>title.clientHeight+2),bodyRequiresScrolling:!!body&&(body.scrollHeight>body.clientHeight+2||body.scrollWidth>body.clientWidth+2),images:[...card.querySelectorAll('img')].map(img=>({loaded:img.complete&&img.naturalWidth>0,width:img.naturalWidth,height:img.naturalHeight}))};}));
  report.views.push({...spec,workId,title:work.title,label:spec.label??work.title,screenshot,metrics});
  await writeFile(path.join(out,'review.json'),JSON.stringify(report,null,2));
  console.log(JSON.stringify({index:i+1,total:plan.length,title:work.title,label:spec.label??work.title,nodes:metrics.length,titleClipped:metrics.filter(n=>n.titleClipped).length,brokenImages:metrics.flatMap(n=>n.images).filter(img=>!img.loaded).length}));
 }
 const escape=s=>String(s).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('"','&quot;');
 for(let i=0;i<report.views.length;i+=6){
  const group=report.views.slice(i,i+6),file=path.join(out,`sheet-${i/6+1}.html`);
  await writeFile(file,`<!doctype html><meta charset="utf-8"><style>body{margin:0;background:#233139;color:#f4ead8;font:16px system-ui;display:grid;grid-template-columns:repeat(3,540px)}figure{margin:12px 8px}figcaption{height:42px}img{width:524px;border:1px solid #788680}</style>${group.map(v=>`<figure><figcaption>${escape(v.label)}</figcaption><img src="${escape(pathToFileURL(path.join(out,v.screenshot)).href)}"></figure>`).join('')}`);
  await page.goto(pathToFileURL(file).href);await page.locator('img').evaluateAll(images=>Promise.all(images.map(img=>img.decode())));await page.screenshot({path:path.join(out,`sheet-${i/6+1}.png`),fullPage:true});
 }
}finally{await browser.close();}
if(report.browserErrors.length)throw new Error(`Viewer errors: ${report.browserErrors.join('; ')}`);
console.log(JSON.stringify({out,views:report.views.length,sourceHash,browserErrors:report.browserErrors.length}));

