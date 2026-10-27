/** Offline, source-bound QA of the authored C# namespace tree.
 * Opens the local portable viewer only. HTTP(S), websocket, and host APIs are
 * blocked. UI interactions change only its temporary runtime clone. */
import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {createHash} from 'node:crypto';
import {chromium} from 'playwright';
import {revealChildren} from '../../src/core/reveal.ts';
import {navigationIndex} from '../../src/core/navigation.ts';

const args=new Map();
for(let at=2;at<process.argv.length;at+=2) args.set(process.argv[at].replace(/^--/,''),process.argv[at+1]);
const directory=path.resolve(args.get('input')??'output/programming-trees-20261021/csharp');
const out=path.resolve(args.get('out')??path.join(directory,'browser-review'));
await mkdir(out,{recursive:true});
const json=async file=>JSON.parse((await readFile(file,'utf8')).replace(/^\uFEFF/,''));
const [project,aliases,manifest,catalog]=await Promise.all([
  ...['draft.json','aliases.json','manifest.json'].map(file=>json(path.join(directory,file))),
  json('content/programming-trees-20261021/csharp-namespaces.json'),
]);
const sha=bytes=>createHash('sha256').update(bytes).digest('hex');
function contrastRatio(foreground,background) {
  const rgb=color=>color.startsWith('#')?[0,2,4].map(at=>parseInt(color.slice(1+at,3+at),16)):(color.match(/[\d.]+/g)??[]).slice(0,3).map(Number);
  const luminance=color=>rgb(color).map(value=>{const s=value/255;return s<=.04045?s/12.92:((s+.055)/1.055)**2.4;}).reduce((sum,value,at)=>sum+value*[.2126,.7152,.0722][at],0);
  const first=luminance(foreground),second=luminance(background);return (Math.max(first,second)+.05)/(Math.min(first,second)+.05);
}
const sourceFiles=['draft.json','aliases.json','manifest.json','publication.html'];
const sourceHashes=Object.fromEntries(await Promise.all(sourceFiles.map(async file=>[file,sha(await readFile(path.join(directory,file)))])));
const workId=aliases.canvases.tree;
assert(workId,'Missing canvases.tree alias.');
const work=project.canvases.find(work=>work.id===workId);
const byId=new Map(project.nodes.map(node=>[node.id,node]));
const namespaceId=name=>{const id=aliases.nodes[`namespace/${name}`];assert(id,`Missing namespace alias ${name}`);return id;};
const namespaceNode=name=>{const node=byId.get(namespaceId(name));assert(node,`Missing node ${name}`);return node;};
const allNamespaceIds=new Set(catalog.namespaceEntries.map(entry=>namespaceId(entry.name)));
const frame=node=>({id:node.id,x:node.x,y:node.y,width:node.width,height:node.height});
const frames=source=>source.nodes.filter(node=>node.canvasId===workId).map(frame).sort((a,b)=>a.id.localeCompare(b.id));
const checks=[],errors=[],networkRequests=[];
const browser=await chromium.launch({headless:true});
const context=await browser.newContext({viewport:{width:1900,height:1200},acceptDownloads:true});
await context.route(/^https?:/,route=>{networkRequests.push({url:route.request().url(),method:route.request().method()});return route.abort();});
await context.routeWebSocket(/.*/,socket=>socket.close());
const page=await context.newPage();page.setDefaultTimeout(15000);
page.on('pageerror',error=>errors.push(error.message));
const card=name=>page.locator(`.ms-node[data-node-id="${namespaceId(name)}"]`);
let screenshotCount=0,initialPayload;
const shot=async name=>{
  await page.mouse.move(5,5);
  await page.evaluate(async()=>{await document.fonts.ready;await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));});
  const file=`${String(++screenshotCount).padStart(2,'0')}-${name}.png`;
  await page.screenshot({path:path.join(out,file)});return file;
};
async function check(name,run) {
  try {checks.push({name,passed:true,...await run()});}
  catch(error) {let screenshot;try{screenshot=await shot(`FAILED-${name.replace(/[^a-z0-9]/gi,'-').slice(0,65)}`);}catch{}checks.push({name,passed:false,error:error.stack??String(error),screenshot});}
}
async function openPanel(){if(await page.getByRole('button',{name:'Explore project',exact:true}).getAttribute('aria-expanded')!=='true')await page.getByRole('button',{name:'Explore project',exact:true}).click();}
async function closePanel(){const button=page.getByRole('button',{name:'Close project navigation',exact:true});if(await button.isVisible())await button.click();}
async function focusId(id,query=id,{keepPanel=false}={}) {
  await openPanel();await page.getByRole('searchbox',{name:'Search project',exact:true}).fill(query);
  const result=page.locator(`[data-navigation-node="${id}"]`);await result.click();
  if(!keepPanel)await closePanel();
}
const focus=async(name,options={})=>focusId(namespaceId(name),name,options);
async function toggle(name){await focus(name,{keepPanel:true});await page.getByRole('button',{name:'Toggle contents from properties',exact:true}).click();await closePanel();}
async function reset(){await page.getByRole('button',{name:'Reset to authored state',exact:true}).click();await page.getByLabel('Choose Work',{exact:true}).selectOption(workId);}
const styleFrame=name=>card(name).evaluate(element=>({left:element.style.left,top:element.style.top,width:element.style.width,height:element.style.height}));
const scale=()=>page.locator('.ms-world').evaluate(element=>Number(getComputedStyle(element).getPropertyValue('--ms-camera-scale')));
async function branchShot(name,label) {
  await focus(name);
  await page.getByRole('button',{name:'Fit selected branch',exact:true}).click();
  return {screenshot:await shot(label),scale:await scale()};
}
function pathThroughJoints(source,target) {
  const queue=[{id:source,path:[]}],seen=new Set([source]);
  while(queue.length){const state=queue.shift();for(const link of project.links.filter(link=>link.canvasId===workId&&(link.sourceId===state.id||link.targetId===state.id&&['none','both'].includes(link.direction)))) {
    const next=byId.get(link.sourceId===state.id?link.targetId:link.sourceId);if(!next||seen.has(next.id))continue;
    const route=[...state.path,link.id];if(next.id===target)return route;
    if(next.kind==='junction'){seen.add(next.id);queue.push({id:next.id,path:route});}
  }}
  return null;
}
try {
  await page.goto(pathToFileURL(path.join(directory,'publication.html')).href);
  await page.getByLabel('Choose Work',{exact:true}).selectOption(workId);
  initialPayload=JSON.parse(await page.locator('#meridian-data').textContent());
  await check('All 736 namespace entries have genuine hierarchy and downward child branches',async()=>{
    assert.equal(work.title,'C# namespaces');assert.equal(catalog.namespaceEntries.length,736);
    assert.equal(catalog.namespaceEntries.filter(entry=>!entry.prefixOnly).length,701);
    const rows=[];
    for(const entry of catalog.namespaceEntries) {
      const node=namespaceNode(entry.name),parent=entry.parent?namespaceNode(entry.parent):byId.get(work.rootId);
      assert.equal(node.canvasId,workId);assert.equal(node.parentId,parent.id,`${entry.name}: genuine dotted parent`);
      assert.equal(node.kind,'media');assert.equal(node.childLayout,'spatial');assert.equal(node.childPosition,'manual');
      assert(!node.expansionTray,`${entry.name} should disclose in the Work, not a tray`);
      if(entry.parent)assert(node.y>=parent.y+parent.height,`${entry.name}: child does not grow downward`);
      rows.push({name:entry.name,nodeId:node.id,parent:entry.parent,prefixOnly:entry.prefixOnly});
    }
    await writeFile(path.join(out,'namespace-membership.json'),JSON.stringify(rows,null,2)+'\n');
    return {namespaces:701,prefixes:35,treeNodes:rows.length,maximumDepth:Math.max(...catalog.namespaceEntries.map(entry=>entry.depth))};
  });
  await check('Each child is attached to its parent by a real routed relationship through Joints',async()=>{
    const paths=[];
    for(const entry of catalog.namespaceEntries.filter(entry=>entry.parent)) {
      const route=pathThroughJoints(namespaceId(entry.parent),namespaceId(entry.name));
      assert(route,`Missing semantic namespace route: ${entry.parent} → ${entry.name}`);
      paths.push({parent:entry.parent,child:entry.name,links:route});
    }
    await writeFile(path.join(out,'relationship-paths.json'),JSON.stringify(paths,null,2)+'\n');
    return {relationships:paths.length,jointRoutes:paths.filter(route=>route.links.length>1).length};
  });
  await check('Root overview and native disclosure use the authored forest',async()=>{
    const count=await page.locator('.ms-node[data-node-id]').count();
    assert(await card('System').count());assert(await card('Microsoft').count());
    assert.equal(await page.locator('.ms-expansion-tray').count(),0);
    return {visibleObjects:count,screenshot:await shot('namespace-root-overview'),scale:await scale()};
  });
  await check('Full namespace search reveals System → Text → Json → Serialization',async()=>{
    await focus('System.Text.Json.Serialization');
    for(const name of ['System','System.Text','System.Text.Json','System.Text.Json.Serialization'])assert.equal(await card(name).count(),1,`Unrevealed ancestor ${name}`);
    const selected=card('System.Text.Json.Serialization');assert((await selected.getAttribute('class')).includes('ms-selected'));
    const box=await selected.boundingBox();assert(box.width>150&&box.height>100,'Target should be readable at focused scale');
    return {search:'System.Text.Json.Serialization',screenFrame:box,scale:await scale(),screenshot:await shot('json-serialization-reading')};
  });
  await check('Text and JSON child branches expand downward and stay readable',async()=>{
    await focus('System.Text.Json');
    const children=catalog.namespaceEntries.find(entry=>entry.name==='System.Text.Json').children;
    if(!await card(children[0]).count())await toggle('System.Text.Json');
    for(const name of children)assert(await card(name).count(),`Missing direct JSON branch ${name}`);
    const jsonFrame=await styleFrame('System.Text.Json');
    const childFrames=Object.fromEntries(await Promise.all(children.map(async name=>[name,await styleFrame(name)])));
    const branch=await branchShot('System.Text.Json','json-branches');
    const titleFrames=Object.fromEntries(await Promise.all(['System.Text.Json',...children].map(async name=>[name,await card(name).locator('.ms-node-title').first().boundingBox()])));
    assert(Object.values(titleFrames).every(box=>box&&box.height>=9),'This small JSON family should show readable parent and direct-child labels in its fitted branch view.');
    return {jsonFrame,childFrames,titleFrames,...branch};
  });
  await check('Sibling branches collapse and reopen with exact original positions and identities',async()=>{
    await reset();await focus('System.Text.Json.Serialization.Metadata');
    await focus('System.Collections.Generic');
    const names=['System.Text','System.Text.Json','System.Text.Json.Serialization','System.Text.Json.Serialization.Metadata','System.Collections','System.Collections.Generic'];
    const before=Object.fromEntries(await Promise.all(names.map(async name=>[name,await styleFrame(name)])));
    await toggle('System.Text');assert.equal(await card('System.Text.Json').count(),0);assert.equal(await card('System.Collections.Generic').count(),1);
    await toggle('System.Text');assert.equal(await card('System.Text.Json.Serialization.Metadata').count(),1);
    await toggle('System.Collections');assert.equal(await card('System.Collections.Generic').count(),0);assert.equal(await card('System.Text.Json.Serialization.Metadata').count(),1);
    await toggle('System.Collections');
    const after=Object.fromEntries(await Promise.all(names.map(async name=>[name,await styleFrame(name)])));
    assert.deepEqual(after,before);assert.equal(await page.locator('.ms-expansion-tray').count(),0);
    return {frames:after,...await branchShot('System.Text','remembered-text-branch')};
  });
  await check('Core disclosure and JSON reopen preserve every namespace frame and link identity',async()=>{
    let runtime=structuredClone(project);
    for(const name of ['System','System.Text','System.Text.Json','System.Text.Json.Serialization','System.Collections'])runtime=revealChildren(runtime,namespaceId(name),true);
    const before=frames(runtime),links=runtime.links.map(link=>link.id),ids=runtime.nodes.map(node=>node.id);
    runtime=revealChildren(runtime,namespaceId('System'),false);
    runtime=JSON.parse(JSON.stringify(runtime));
    runtime=revealChildren(runtime,namespaceId('System'),true);
    assert.deepEqual(frames(runtime),before);assert.deepEqual(runtime.links.map(link=>link.id),links);assert.deepEqual(runtime.nodes.map(node=>node.id),ids);
    assert.equal(runtime.nodes.find(node=>node.id===namespaceId('System.Text.Json.Serialization')).collapsed,false);
    const navigation=navigationIndex(runtime);assert(navigation.isNodeVisible(namespaceId('System.Text.Json.Serialization.Metadata')));
    assert(runtime.nodes.filter(node=>node.canvasId===workId).every(node=>!node.expansionTray));
    return {unchangedFrames:before.length,unchangedIdentities:ids.length,unchangedLinks:links.length,deepDisclosureRetained:true};
  });
  await check('Modern and .NET Framework branches retain accurate availability and source destinations',async()=>{
    const references=[];
    for(const entry of catalog.namespaceEntries.filter(entry=>!entry.prefixOnly)) {
      const referenceId=aliases.nodes[`reference/${entry.name}`];
      const reference=byId.get(referenceId);
      assert(reference&&reference.contentType==='link',`Missing active official reference: ${entry.name}`);
      assert(reference.hyperlinks?.some(link=>link.url===entry.documentationUrl),`Changed source destination: ${entry.name}`);
      references.push({namespace:entry.name,nodeId:referenceId,url:entry.documentationUrl});
    }
    assert.equal(references.length,701);
    const legacy=namespaceNode('System.Web.UI'),modern=namespaceNode('Microsoft.AspNetCore.Mvc');
    assert(catalog.namespaceEntries.find(entry=>entry.name==='System.Web.UI').legacy&&!catalog.namespaceEntries.find(entry=>entry.name==='System.Web.UI').modern);
    assert(catalog.namespaceEntries.find(entry=>entry.name==='Microsoft.AspNetCore.Mvc').modern&&!catalog.namespaceEntries.find(entry=>entry.name==='Microsoft.AspNetCore.Mvc').legacy);
    await focus('System.Web');
    const legacyText=await page.locator('.mv-node-context').innerText().catch(()=>legacy.text??'');
    assert(/Framework|legacy/i.test(legacy.text??''),'Legacy availability should be in the authored finding aid');
    assert(/ASP.NET|MVC|modern|10\./i.test(modern.text??''));
    const legacyUrl=catalog.namespaceEntries.find(entry=>entry.name==='System.Web.UI').documentationUrl;
    const modernUrl=catalog.namespaceEntries.find(entry=>entry.name==='Microsoft.AspNetCore.Mvc').documentationUrl;
    const legacySource=project.nodes.filter(node=>node.canvasId===workId&&node.contentType==='link'&&node.hyperlinks?.some(link=>link.url===legacyUrl));
    const modernSource=project.nodes.filter(node=>node.canvasId===workId&&node.contentType==='link'&&node.hyperlinks?.some(link=>link.url===modernUrl));
    assert(legacySource.length&&modernSource.length,'Native official-source hyperlink nodes must be present');
    await focusId(legacySource[0].id);const href=await page.locator(`.ms-node[data-node-id="${legacySource[0].id}"] a`).getAttribute('href');assert.equal(href,legacyUrl);
    await focusId(modernSource[0].id);const modernHref=await page.locator(`.ms-node[data-node-id="${modernSource[0].id}"] a`).getAttribute('href');assert.equal(modernHref,modernUrl);
    const color=await page.locator(`.ms-node[data-node-id="${modernSource[0].id}"] a`).evaluate(anchor=>getComputedStyle(anchor).color);
    const fill=modernSource[0].style.fill;
    const contrast={color,fill,ratio:contrastRatio(color,fill)};
    assert(contrast.ratio>=4.5,'Official source hyperlinks need readable contrast against their authored frame.');
    const modernScreenshot=await shot('aspnet-core-source-reading');await focusId(legacySource[0].id);
    return {nativeOfficialSources:references.length,legacyNode:legacy.id,modernNode:modern.id,legacySource:href,modernSource:modernHref,legacyContext:legacyText,contrast,screenshot:await shot('framework-source-reading'),modernScreenshot};
  });
  await check('Complete CSV original downloads with exact bytes',async()=>{
    const asset=project.assets.find(asset=>asset.name==='CSharp-namespace-type-index.csv'||/namespace.type.index.*\.csv/i.test(asset.name));assert(asset,'Complete namespace/type CSV asset missing');
    const doc=project.nodes.find(node=>node.canvasId===workId&&node.assetId===asset.id);assert(doc,'Complete CSV document node missing');
    await focusId(doc.id,doc.id,{keepPanel:true});await page.getByRole('button',{name:'Open selected attachment',exact:true}).click();
    const dialog=page.locator('.ms-asset-dialog').last();const downloadWait=page.waitForEvent('download');
    await dialog.getByRole('button',{name:`Download ${asset.name}`,exact:true}).click();
    const download=await downloadWait,bytes=await readFile(await download.path()),original=await readFile('content/programming-trees-20261021/CSharp-namespace-type-index.csv');
    assert.deepEqual(bytes,original);assert.deepEqual(bytes,Buffer.from(asset.base64,'base64'));
    await dialog.getByRole('button',{name:'Close attachment',exact:true}).click();await closePanel();
    return {bytes:bytes.length,sha256:sha(bytes),filename:download.suggestedFilename()};
  });
  await check('Exploration does not alter the published original or authored files',async()=>{
    assert.deepEqual(JSON.parse(await page.locator('#meridian-data').textContent()),initialPayload);
    return {authoredPayloadImmutable:true};
  });
} finally {await context.close();await browser.close();}
const unchanged=Object.fromEntries(await Promise.all(sourceFiles.map(async file=>[file,sha(await readFile(path.join(directory,file)))===sourceHashes[file]])));
const report={generatedAt:new Date().toISOString(),directory,sourceHashes,manifest,summary:{checks:checks.length,passed:checks.filter(check=>check.passed).length,failed:checks.filter(check=>!check.passed).length,screenshots:screenshotCount},checks,errors,networkRequests,unchanged};
await writeFile(path.join(out,'review.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({summary:report.summary,errors,networkRequests,unchanged,failures:checks.filter(check=>!check.passed).map(check=>({name:check.name,error:check.error.split('\n').slice(0,3).join('\n')})),report:path.join(out,'review.json')},null,2));
if(report.summary.failed||errors.length||networkRequests.length||Object.values(unchanged).some(value=>!value))process.exitCode=1;
