/** Scope: export/color compatibility of the currently saved programming Works.
 * Reads installed.json and exact installed viewer/CSS bytes; builds separate
 * offline publications only. Never overwrites frozen candidates or host data.
 */
import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
import {chromium} from 'playwright';
import {buildPublication} from '../../src/publication.ts';

const root=path.resolve('output/programming-trees-20261021');
const out=path.join(root,'link-color-fix/publication-review');
await mkdir(out,{recursive:true});
const hash=value=>createHash('sha256').update(typeof value==='string'||Buffer.isBuffer(value)?value:JSON.stringify(value)).digest('hex');
const json=async file=>JSON.parse((await readFile(file,'utf8')).replace(/^\uFEFF/,''));
const receipt=await json(path.join(root,'application-receipt.json'));assert(receipt.completed);
const runtimeFiles={viewer:'output/app/wwwroot/viewer.js',css:'output/app/wwwroot/scene.css'};
const [viewer,css]=await Promise.all(Object.values(runtimeFiles).map(file=>readFile(file,'utf8')));
assert(css.includes('.ms-node.ms-custom-text .ms-content-link{color:var(--ms-text-color)}'),'Installed shared authored-link color rule absent.');
const runtimeHashes={viewer:hash(viewer),css:hash(css)};
const colors=color=>{
  if(color.startsWith('#')){const h=color.slice(1);assert.equal(h.length,6);return [0,2,4].map(at=>parseInt(h.slice(at,at+2),16));}
  return (color.match(/[\d.]+/g)??[]).slice(0,3).map(Number);
};
const luminance=color=>{const c=colors(color).map(n=>n/255).map(n=>n<=.04045?n/12.92:((n+.055)/1.055)**2.4);return c[0]*.2126+c[1]*.7152+c[2]*.0722;};
const contrast=(a,b)=>{const one=luminance(a),two=luminance(b);return(Math.max(one,two)+.05)/(Math.min(one,two)+.05);};
const reports=[],errors=[],requests=[];
const browser=await chromium.launch({headless:true});
const context=await browser.newContext({viewport:{width:1800,height:1160},acceptDownloads:true});
await context.route(/^https?:/,route=>{requests.push({method:route.request().method(),url:route.request().url()});return route.abort();});
await context.routeWebSocket(/.*/,socket=>socket.close());
async function focus(page,id,{keepPanel=false}={}){
  const open=page.getByRole('button',{name:'Explore project',exact:true});if(await open.getAttribute('aria-expanded')!=='true')await open.click();
  await page.getByRole('searchbox',{name:'Search project',exact:true}).fill(id);
  await page.locator(`[data-navigation-node="${id}"]`).click();
  if(!keepPanel)await page.getByRole('button',{name:'Close project navigation',exact:true}).click();
  await page.evaluate(async()=>{await document.fonts.ready;await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));});
}
try{
  for(const slug of ['cpp','csharp']){
    const directory=path.join(out,slug);await mkdir(directory,{recursive:true});
    const inputDir=path.join(root,slug),originalFiles=['installed.json','draft.json','aliases.json','manifest.json','publication.html'];
    const originalHashes=Object.fromEntries(await Promise.all(originalFiles.map(async name=>[name,hash(await readFile(path.join(inputDir,name)))])));
    const [saved,aliases]=await Promise.all(['installed.json','aliases.json'].map(name=>json(path.join(inputDir,name))));
    const savedHash=hash(saved);assert.equal(savedHash,receipt.projects[slug].sha256);
    const html=buildPublication(saved,viewer,css);
    assert.equal(hash(saved),savedHash,'Building the publication must not mutate saved data.');
    const htmlFile=path.join(directory,'publication.html');await writeFile(htmlFile,html);
    const page=await context.newPage();page.setDefaultTimeout(15000);page.on('pageerror',error=>errors.push({slug,message:error.message}));
    await page.goto(pathToFileURL(htmlFile).href);
    await page.getByLabel('Choose Work',{exact:true}).selectOption(aliases.canvases.tree);
    const initialPayload=JSON.parse(await page.locator('#meridian-data').textContent()),payloadHash=hash(initialPayload);
    assert.equal(initialPayload.project.id,saved.id);assert.equal(initialPayload.project.revision,saved.revision);
    const key=slug==='cpp'?'type/std::vector':'namespace/Accessibility',id=aliases.nodes[key];assert(id);
    const original=saved.nodes.find(node=>node.id===id);assert(original);
    const expectedHref=original.hyperlinks?.[0]?.url??original.hyperlink?.url;assert(expectedHref);
    await focus(page,id);
    const card=page.locator(`.ms-node[data-node-id="${id}"]`);
    for(let at=0;at<10;at++){const box=await card.boundingBox();if(!box||box.width<1050)break;await page.getByRole('button',{name:'Zoom out',exact:true}).click();}
    const anchor=card.locator('a.ms-content-link').first();await anchor.click({trial:true});
    const finding=await anchor.evaluate(element=>{
      const card=element.closest('.ms-node'),a=element.getBoundingClientRect(),b=card.getBoundingClientRect();
      return {text:element.textContent,url:element.href,target:element.target,rel:element.rel,color:getComputedStyle(element).color,
        titleColor:getComputedStyle(card.querySelector('.ms-node-title')).color,fill:getComputedStyle(card,'::after').backgroundColor,
        overflow:{left:Math.max(0,b.left-a.left),right:Math.max(0,a.right-b.right),top:Math.max(0,b.top-a.top),bottom:Math.max(0,a.bottom-b.bottom)}};
    });
    finding.authoredColor=original.style.textColor;finding.contrast=contrast(finding.color,finding.fill);
    assert.equal(finding.url,expectedHref);assert.deepEqual(colors(finding.color),colors(original.style.textColor));
    assert.deepEqual(colors(finding.titleColor),colors(original.style.textColor));assert(finding.contrast>=4.5);
    assert.equal(finding.target,'_blank');assert(finding.rel.includes('noopener'));assert(Math.max(...Object.values(finding.overflow))<2);
    await page.mouse.move(5,5);const screenshot=path.join(directory,'authored-source-color.png');await page.screenshot({path:screenshot});
    const csvName=slug==='cpp'?'Cpp-standard-library-types.csv':'CSharp-namespace-type-index.csv';
    const asset=saved.assets.find(asset=>asset.name===csvName);assert(asset);
    const doc=saved.nodes.find(node=>node.canvasId===aliases.canvases.tree&&node.assetId===asset.id);assert(doc);
    await focus(page,doc.id,{keepPanel:true});await page.getByRole('button',{name:'Open selected attachment',exact:true}).click();
    const dialog=page.locator('.ms-asset-dialog').last(),waiting=page.waitForEvent('download');
    await dialog.getByRole('button',{name:`Download ${asset.name}`,exact:true}).click();
    const download=await waiting,bytes=await readFile(await download.path());
    assert.deepEqual(bytes,Buffer.from(asset.base64,'base64'));assert.deepEqual(bytes,await readFile(path.join('content/programming-trees-20261021',csvName)));
    await dialog.getByRole('button',{name:'Close attachment',exact:true}).click();
    const afterPayload=JSON.parse(await page.locator('#meridian-data').textContent());assert.deepEqual(afterPayload,initialPayload);
    const unchanged=Object.fromEntries(await Promise.all(originalFiles.map(async name=>[name,hash(await readFile(path.join(inputDir,name)))===originalHashes[name]])));
    assert(Object.values(unchanged).every(Boolean));
    const report={slug,projectId:saved.id,revision:saved.revision,savedSHA256:savedHash,payloadSHA256:payloadHash,payloadAfterSHA256:hash(afterPayload),publicationSHA256:hash(html),runtimeHashes,
      nodeKey:key,nodeId:id,source: finding,csv:{name:csvName,bytes:bytes.length,sha256:hash(bytes),suggestedFilename:download.suggestedFilename()},
      publicationPayloadNote:'Publication deliberately retains needed snapshots and assets, so its serialized project is compared with its own inert initial payload; the complete saved installed.json hash independently matches the live-save receipt.',
      unchanged,screenshot,passed:true};
    await writeFile(path.join(directory,'review.json'),JSON.stringify(report,null,2)+'\n');reports.push(report);await page.close();
  }
}finally{await context.close();await browser.close();}
const runtimeUnchanged=Object.fromEntries(await Promise.all(Object.entries(runtimeFiles).map(async([name,file])=>[name,hash(await readFile(file))===runtimeHashes[name]])));
assert(Object.values(runtimeUnchanged).every(Boolean));
const report={generatedAt:new Date().toISOString(),scope:'Saved-project export and installed authored-link color compatibility only; no geometry repetition or host writes.',runtimeFiles,runtimeHashes,runtimeUnchanged,reports,errors,requests,passed:reports.length===2&&errors.length===0&&requests.length===0};
await writeFile(path.join(out,'review.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({passed:report.passed,runtimeUnchanged,errors,requests,projects:reports.map(item=>({slug:item.slug,revision:item.revision,sourceColor:item.source.color,authoredColor:item.source.authoredColor,contrast:item.source.contrast,savedSHA256:item.savedSHA256,csv:item.csv})),report:path.join(out,'review.json')},null,2));
if(!report.passed)process.exitCode=1;
