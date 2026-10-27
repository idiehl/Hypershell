import fs from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {pathToFileURL} from 'node:url';
import {chromium} from '@playwright/test';
const out='I:/dev/Meridian/output/mythology-refinement-20261023/gods';
const project=JSON.parse(await fs.readFile(out+'/draft.json','utf8'));
const receipt={sourceHash:createHash('sha256').update(JSON.stringify(project)).digest('hex'),checks:[],browserErrors:[],gallery:'No active Gallery surface remains; Museum Objects uses original art nodes with their own attribution disclosures.'};
const browser=await chromium.launch({headless:true});
function check(name,pass,details){receipt.checks.push({name,pass,details});if(!pass)throw new Error(name);}
try{
 const page=await browser.newPage({viewport:{width:1600,height:1000}});page.on('pageerror',e=>receipt.browserErrors.push(e.message));await page.route(/^https?:/,route=>route.abort());
 await page.goto(pathToFileURL(out+'/review-final/publication.html').href,{waitUntil:'domcontentloaded'});
 await page.getByLabel('Choose Work',{exact:true}).selectOption(project.canvases[46].id);
 await page.getByRole('button',{name:'Read source-register.txt in this node',exact:true}).click();
 await page.getByRole('region',{name:'Document viewer: source-register.txt',exact:true}).waitFor();
 const content=await page.locator('.ms-document-open .mdv-source').textContent();check('Original source register readable inline',content.includes('GREEK MYTHOLOGY SOURCE REGISTER')&&content.includes('Rights/provenance'));
 const scroll=await page.locator('.ms-document-open .mdv-scroll').evaluate(el=>{el.scrollTop=400;return{top:el.scrollTop,scroll:el.scrollHeight,height:el.clientHeight};});check('Original reader scrolls',scroll.top>0,scroll);
 await page.getByRole('searchbox',{name:'Find in document',exact:true}).fill('Hecate');check('Document search available',(await page.getByRole('searchbox',{name:'Find in document',exact:true}).inputValue())==='Hecate');
 await page.screenshot({path:out+'/reader-inline.png'});
 await page.getByRole('button',{name:'Open larger document reading view',exact:true}).click();await page.getByRole('dialog',{name:'Read source-register.txt',exact:true}).waitFor();check('Larger reader opens',await page.getByRole('dialog',{name:'Read source-register.txt',exact:true}).isVisible());await page.getByRole('button',{name:'Close attachment',exact:true}).click();
 await page.getByRole('button',{name:'Collapse document viewer',exact:true}).click();check('Inline reader closes',(await page.locator('.ms-document-open').count())===0);
 await page.getByLabel('Choose Work',{exact:true}).selectOption(project.canvases[10].id);
 const reading=project.nodes.find(n=>n.canvasId===project.canvases[10].id&&n.title==='Reading');await page.locator(`[data-node-id="${reading.id}"]`).dblclick({position:{x:12,y:12}});
 await page.getByRole('button',{name:'Read Zeus Reading.md in this node',exact:true}).click();await page.getByRole('region',{name:'Document viewer: Zeus Reading.md',exact:true}).waitFor();const archived=await page.locator('.ms-document-open .mdv-viewer').textContent();check('Collected prose and qualifications accessible',archived.includes('Metis and the succession')&&archived.includes('theogony-household')&&archived.includes('Original identity:'));
 await page.screenshot({path:out+'/reader-archive.png'});
 await page.getByLabel('Choose Work',{exact:true}).selectOption(project.canvases[45].id);await page.getByRole('button',{name:'Run simulation',exact:true}).click();
 const voyage=project.nodes.find(n=>n.title==='Voyage ready'),unwired=project.nodes.find(n=>n.title==='Deliberately unwired output');const readState=async id=>(await page.locator(`[data-node-id="${id}"] .ms-state`).textContent()).trim();
 check('Voyage initial signal',await readState(voyage.id)==='On');await page.getByRole('switch',{name:'Crew prepared: toggle input',exact:true}).click();check('Input updates output immediately',await readState(voyage.id)==='Off');await page.getByRole('switch',{name:'Crew prepared: toggle input',exact:true}).click();check('Input restores output',await readState(voyage.id)==='On');await page.getByRole('switch',{name:'Enable voyage: toggle switch',exact:true}).click();check('Open switch produces Unknown',await readState(voyage.id)==='Unknown');check('Unwired output remains Unknown',await readState(unwired.id)==='Unknown');
 await page.getByRole('switch',{name:'Enable voyage: toggle switch',exact:true}).click();await page.screenshot({path:out+'/lab-running.png'});
 check('No browser errors',receipt.browserErrors.length===0,receipt.browserErrors);
}finally{await browser.close();await fs.writeFile(out+'/behavior.json',JSON.stringify(receipt,null,2));}
console.log(JSON.stringify({sourceHash:receipt.sourceHash,checks:receipt.checks.length,allPassed:receipt.checks.every(c=>c.pass),browserErrors:receipt.browserErrors.length}));

