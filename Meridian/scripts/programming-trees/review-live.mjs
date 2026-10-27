/** Read-only verification of the actual saved editor after the parent's apply.
 * Requires the application receipt before making any request. Only same-origin
 * GET requests are permitted. Every mutation and every external request aborts.
 * Never expand/collapse, edit, save, import, or restore any live document. */
import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {chromium} from 'playwright';

const args=new Map();
for(let at=2;at<process.argv.length;at+=2) {
  assert(['--directory','--receipt','--out'].includes(process.argv[at])&&process.argv[at+1], 'Use [--directory DIR] [--receipt FILE] [--out DIR]');
  args.set(process.argv[at],process.argv[at+1]);
}
const directory=path.resolve(args.get('--directory')??'output/programming-trees-20261021');
const out=path.resolve(args.get('--out')??path.join(directory,'live-browser-review'));
const receiptFile=path.resolve(args.get('--receipt')??path.join(directory,'application-receipt.json'));
const origin='http://127.0.0.1:5240';
const json=async file=>JSON.parse((await readFile(file,'utf8')).replace(/^\uFEFF/,''));
const hash=value=>createHash('sha256').update(typeof value==='string'||Buffer.isBuffer(value)?value:JSON.stringify(value)).digest('hex');
function contrastRatio(foreground,background) {
  const rgb=color=>color.startsWith('#')?[0,2,4].map(at=>parseInt(color.slice(1+at,3+at),16)):(color.match(/[\d.]+/g)??[]).slice(0,3).map(Number);
  const luminance=color=>rgb(color).map(value=>{const s=value/255;return s<=.04045?s/12.92:((s+.055)/1.055)**2.4;}).reduce((sum,value,at)=>sum+value*[.2126,.7152,.0722][at],0);
  const first=luminance(foreground),second=luminance(background);return (Math.max(first,second)+.05)/(Math.min(first,second)+.05);
}
const receipt=await json(receiptFile); // Deliberately before every live request.
assert.equal(receipt.completed,true,'Do not browse until the parent confirms the complete application receipt.');
assert.equal(receipt.origin,origin);
assert(receipt.projects?.csharp&&receipt.projects?.cpp&&receipt.library,'The completed apply receipt is required.');
const [libraryAliases,composition,...projects]=await Promise.all([
  json(path.join(directory,'library.aliases.json')),json(path.join(directory,'library-composition.json')),
  ...['csharp','cpp'].map(async slug=>({slug,aliases:await json(path.join(directory,slug,'aliases.json')),manifest:await json(path.join(directory,slug,'manifest.json'))})),
]);
const expected={
  csharp:{projectId:'47f9024f-2a26-4a9a-91b9-83927029fbf3',revision:10,workId:'617d8cba-c876-4b0d-925f-5ec50489eed5',title:'C# namespaces',launcher:'8633b570-e119-4036-8ee2-f18c24192e53',focus:'namespace/System.Text'},
  cpp:{projectId:'2a9aa31d-b13c-4d0b-9192-b1a94b7c0fce',revision:2,workId:'6bdf2562-d8db-44ae-af8e-31cfc3d738f7',title:'C++ classes and types',launcher:'3a5b645a-4993-4d02-8bcc-6fe5aaae0779',focus:'category/containers'},
};
for(const [slug,spec]of Object.entries(expected)) {
  const item=receipt.projects[slug];
  assert.equal(item.id??item.projectId,spec.projectId);assert.equal(item.revision,spec.revision);assert(/^[a-f0-9]{64}$/i.test(item.sha256));
}
assert.equal(receipt.library.revision,479);assert(/^[a-f0-9]{64}$/i.test(receipt.library.sha256));
await mkdir(out,{recursive:true});
const apiGets=[];
async function get(endpoint) {
  assert(endpoint.startsWith('/api/'),'Only an explicit local API GET is allowed.');
  const response=await fetch(origin+endpoint,{method:'GET',redirect:'error',signal:AbortSignal.timeout(30000)});
  assert(response.ok,`GET ${endpoint}: ${response.status}`);apiGets.push(endpoint);return response.json();
}
const readSaved=async()=>Object.fromEntries(await Promise.all([
  ...Object.entries(expected).map(async([slug,spec])=>[slug,await get(`/api/projects/${spec.projectId}`)]),
  (async()=>['library',await get('/api/library')])(),
]));
const before=await readSaved();
for(const [slug,spec]of Object.entries(expected)) {
  assert.equal(before[slug].revision,spec.revision);assert.equal(hash(before[slug]),receipt.projects[slug].sha256,`${slug}: saved project differs from the applied receipt`);
}
assert.equal(before.library.revision,479);assert.equal(hash(before.library),receipt.library.sha256);assert.equal(Object.keys(before.library.canvasTargets).length,84);
const checks=[],browserErrors=[],blockedMutations=[],blockedExternal=[],browserGets=[];
const browser=await chromium.launch({headless:true});
const context=await browser.newContext({viewport:{width:1900,height:1200},deviceScaleFactor:1,reducedMotion:'reduce'});
await context.route('**/*',route=>{
  const request=route.request(),url=new URL(request.url());
  if(['blob:','data:'].includes(url.protocol))return route.continue();
  if(url.origin!==origin){blockedExternal.push({url:url.href,method:request.method()});return route.abort();}
  if(request.method()!=='GET'){blockedMutations.push({url:url.href,method:request.method()});return route.abort();}
  browserGets.push(url.pathname+url.search);return route.continue();
});
await context.routeWebSocket(/.*/,socket=>socket.close());
const page=await context.newPage();page.setDefaultTimeout(18000);page.on('pageerror',error=>browserErrors.push(error.message));
const card=id=>page.locator(`.ms-node[data-node-id="${id}"]`);
const settle=async()=>page.evaluate(async()=>{await document.fonts.ready;await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));});
async function ready(){await page.locator('.workspace .ms-world').waitFor();await settle();}
async function shot(name){await page.mouse.move(4,4);await settle();await page.screenshot({path:path.join(out,name)});return name;}
async function check(name,run){try{checks.push({name,passed:true,...await run()});}catch(error){let screenshot;try{screenshot=await shot(`FAILED-${name.replace(/[^a-z0-9]/gi,'-').slice(0,65)}.png`);}catch{}checks.push({name,passed:false,error:error.stack??String(error),screenshot});}}
async function fit(){await page.getByRole('button',{name:'Fit Work',exact:true}).click();await settle();}
async function targetVisible(project,id) {
  const node=project.nodes.find(node=>node.id===id);assert(node,'Missing saved navigation target');
  let parentId=node.parentId;
  while(parentId){const parent=project.nodes.find(node=>node.id===parentId);assert(parent);assert(!parent.collapsed||parent.kind==='canvas','Read-only target must have no collapsed ancestor');parentId=parent.parentId;}
  await page.keyboard.press('Escape');await page.keyboard.press('Control+f');
  const search=page.getByRole('searchbox',{name:'Search Work nodes',exact:true});await search.fill(id);
  const row=page.locator(`[data-outline-node="${id}"]`);await row.waitFor();
  await row.getByRole('button',{name:/^Jump to Node:/}).click();await search.fill('');await card(id).waitFor();await settle();
  assert.equal(blockedMutations.length,0,'Targeting visible content must not edit a live document');
}
async function expectWork(workId,title) {
  await page.waitForURL(url=>url.searchParams.get('canvas')===workId,{timeout:20000});await ready();
  assert((await page.locator('[data-current-canvas]').innerText()).startsWith(title));
}
try {
  await check('Saved Library has exact applied hashes, three subject containers, and both new launchers',async()=>{
    const library=before.library,root=library.project.canvases.find(work=>work.id===library.project.entryCanvasId).rootId;
    const categories=library.project.nodes.filter(node=>node.parentId===root&&node.kind==='container');
    assert.deepEqual(categories.map(node=>node.tag??node.title).sort(),['Coding','Mythology','Tutorial']);
    const placements=[];
    for(const [slug,spec]of Object.entries(expected)) {
      assert.equal(libraryAliases.nodes[`programming-tree/${slug}`],spec.launcher);
      const portal=library.project.nodes.find(node=>node.id===spec.launcher);assert(portal);
      const source=composition.sources.find(source=>source.slug===slug);assert.equal(portal.parentId,source.parentId);
      assert.deepEqual(library.canvasTargets[spec.launcher],{projectId:spec.projectId,canvasId:spec.workId});
      const parent=library.project.nodes.find(node=>node.id===portal.parentId);
      const category=library.project.nodes.find(node=>node.id===parent.parentId);assert.equal(category.tag??category.title,'Coding');
      placements.push({slug,nodeId:portal.id,parentId:portal.parentId,category:'Coding',target:library.canvasTargets[portal.id]});
    }
    await page.goto(origin+'/');await ready();await fit();
    return {libraryRevision:479,targets:84,categories:categories.map(node=>node.tag??node.title),placements,screenshot:await shot('01-saved-library.png')};
  });
  for(const {slug,aliases,manifest}of projects)await check(`Actual Library → ${slug} collection → new subject Work navigation`,async()=>{
    const spec=expected[slug],source=composition.sources.find(source=>source.slug===slug);
    await page.goto(origin+'/');await ready();await fit();
    const libraryEntry=card(source.parentId).locator('.ms-canvas-open');assert(await libraryEntry.isVisible());await libraryEntry.click();
    await page.waitForURL(url=>url.searchParams.get('project')===spec.projectId,{timeout:20000});await ready();
    const collectionPortal=aliases.nodes['collection/new-tree'];assert(collectionPortal);
    await targetVisible(before[slug],collectionPortal);
    await card(collectionPortal).locator('.ms-canvas-open').click();await expectWork(spec.workId,spec.title);await fit();
    const overview=await shot(`02-${slug}-saved-subject-work.png`);
    const focusId=aliases.nodes[spec.focus];assert(focusId);await targetVisible(before[slug],focusId);
    const focusScreenshot=await shot(`03-${slug}-saved-focused-branch.png`);
    const selectedClass=await card(focusId).getAttribute('class');assert(selectedClass.includes('ms-selected'));
    assert.equal(blockedMutations.length,0,'Navigation must not initiate project/library writes');
    await page.getByRole('button',{name:'Back',exact:true}).click();await expectWork(aliases.canvases.collection,before[slug].canvases.find(work=>work.id===aliases.canvases.collection).title);
    await page.getByRole('button',{name:'Forward',exact:true}).click();await expectWork(spec.workId,spec.title);
    return {projectId:spec.projectId,revision:before[slug].revision,newWorkId:spec.workId,title:spec.title,sourceDraftSHA256:manifest.draftSHA256,overview,focusScreenshot,history:'Back → collection; Forward → new subject Work'};
  });
  await check('Actual saved-editor source hyperlinks respect authored text color and readable contrast',async()=>{
    const {aliases}=projects.find(item=>item.slug==='csharp'),project=before.csharp,id=aliases.nodes['namespace/Accessibility'];
    await page.goto(`${origin}/?project=${project.id}&canvas=${aliases.canvases.tree}`);await ready();await targetVisible(project,id);
    const paints=await page.locator('.ms-node a.ms-content-link').evaluateAll(anchors=>anchors.map(anchor=>{
      const node=anchor.closest('.ms-node');return {nodeId:node.dataset.nodeId,url:anchor.href,color:getComputedStyle(anchor).color,nodeColor:getComputedStyle(node).color};
    }));
    assert(paints.length>=3,'Expected already-visible source leaf links in the C# root branches.');
    const results=paints.map(paint=>{const source=project.nodes.find(node=>node.id===paint.nodeId);assert(source?.style?.fill);return {...paint,fill:source.style.fill,ratio:contrastRatio(paint.color,source.style.fill)};});
    for(const result of results){assert.equal(result.color,result.nodeColor,'Node appearance text color should govern its native source link');assert(result.ratio>=4.5,`Poor native source-link contrast: ${result.ratio}`);}
    return {links:results,minimumContrast:Math.min(...results.map(result=>result.ratio)),screenshot:await shot('04-native-editor-source-links.png')};
  });
  await check('Live saved documents remain byte-equivalent after GET-only browsing',async()=>{
    const after=await readSaved();
    for(const slug of ['csharp','cpp','library'])assert.equal(hash(after[slug]),hash(before[slug]),`${slug}: live state changed during read-only verification`);
    assert.equal(blockedMutations.length,0);assert.equal(blockedExternal.length,0);assert.equal(browserErrors.length,0);
    return {hashes:Object.fromEntries(Object.entries(after).map(([slug,value])=>[slug,hash(value)])),unchanged:true};
  });
}finally{await context.close();await browser.close();}
const report={generatedAt:new Date().toISOString(),origin,receiptFile,receiptSHA256:hash(await readFile(receiptFile)),summary:{checks:checks.length,passed:checks.filter(check=>check.passed).length,failed:checks.filter(check=>!check.passed).length},checks,apiGets,browserGets,blockedMutations,blockedExternal,browserErrors,liveWrites:false};
await writeFile(path.join(out,'review.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({summary:report.summary,blockedMutations,blockedExternal,browserErrors,failures:checks.filter(check=>!check.passed).map(check=>({name:check.name,error:check.error.split('\n').slice(0,3).join('\n')})),report:path.join(out,'review.json')},null,2));
if(report.summary.failed||blockedMutations.length||blockedExternal.length||browserErrors.length)process.exitCode=1;
