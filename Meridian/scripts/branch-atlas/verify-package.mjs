import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import path from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import { once } from 'node:events';
import { createServer } from 'node:net';
import { createHash } from 'node:crypto';
import { chromium, expect } from '@playwright/test';
import { readPublication } from '../../src/publication.ts';

const folder = path.resolve('output/branch-atlas'), output = path.resolve('output/branch-qa');
const sandbox = path.resolve('.cache/branch-cycle/package-qa-' + Date.now()), data = path.join(sandbox, 'data'), staged = path.join(sandbox, 'staged');
await mkdir(output, { recursive: true }); await mkdir(staged, { recursive: true });
const baseline = JSON.parse(await readFile(path.join(folder, 'baseline.json'), 'utf8'));
const candidate = JSON.parse(await readFile(path.join(folder, 'atlas.json'), 'utf8'));
const aliases = JSON.parse(await readFile(path.join(folder, 'aliases.json'), 'utf8'));
const manifest = JSON.parse(await readFile(path.join(folder, 'manifest.json'), 'utf8'));
await mkdir(path.join(data, 'projects', baseline.id), { recursive: true });
await writeFile(path.join(data, 'projects', baseline.id, 'project.json'), JSON.stringify({ project: baseline, updatedAt: new Date().toISOString() }));
for (const name of ['baseline.json','atlas.json','library.json','library-baseline.json']) await copyFile(path.join(folder,name),path.join(staged,name));
await copyFile(path.join(folder,'library-baseline.json'),path.join(data,'library.json'));
const offline = path.resolve('.cache/branch-cycle/offline-artifacts/bin/OfflineAuthoring/debug/OfflineAuthoring.dll');
const apply = spawnSync('dotnet', [offline, data, staged], { encoding: 'utf8', windowsHide: true });
assert.equal(apply.status, 0, apply.stdout + apply.stderr);
const repeated = spawnSync('dotnet', [offline, data, staged], { encoding: 'utf8', windowsHide: true });
assert.notEqual(repeated.status, 0, 'Repeated stale application must fail before writes');
const server = createServer(); server.listen(0,'127.0.0.1'); await once(server,'listening'); const port = server.address().port; await new Promise(resolve => server.close(resolve));
const origin = `http://127.0.0.1:${port}`;
const host = spawn(path.resolve('output/branch-20261005/app/Meridian.Host.exe'), ['--data-root', data, '--urls', origin], { windowsHide: true, stdio: ['ignore','pipe','pipe'] });
let logs=''; host.stdout.on('data', chunk => logs+=chunk); host.stderr.on('data',chunk=>logs+=chunk);
const report = { origin, isolatedData: data, offlineApply: true, rejectsStaleApply: true, errors: [] };
const request = async (route, method='GET', body) => { const response=await fetch(origin+route,{method,...(body?{headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}:{})});assert.ok(response.ok,`${route}: ${response.status} ${response.ok?'':await response.text()}`);return response; };
const canonical=value=>Array.isArray(value)?value.map(canonical):value&&typeof value==='object'?Object.fromEntries(Object.keys(value).sort().map(k=>[k,canonical(value[k])])):value; const hash=value=>createHash('sha256').update(JSON.stringify(canonical(value))).digest('hex');
let browser;
try {
  for(let attempt=0;attempt<160;attempt++){ if(host.exitCode!==null)throw new Error(logs);try{if((await fetch(origin+'/__meridian_status')).ok)break;}catch{} await new Promise(resolve=>setTimeout(resolve,50)); }
  const status=await (await request('/__meridian_status')).json(); assert.ok(status.capabilities.includes('link-disclosure-v1'));
  const saved=await (await request(`/api/projects/${candidate.id}`)).json(); assert.equal(saved.revision,baseline.revision+1); assert.deepEqual(saved,{...candidate,revision:saved.revision});
  const checkpoints=await(await request(`/api/projects/${candidate.id}/checkpoints`)).json();assert.equal(checkpoints.length,1);assert.deepEqual(await(await request(`/api/projects/${candidate.id}/checkpoints/${checkpoints[0].id}`)).json(),baseline);
  const library=await(await request('/api/library')).json();assert.equal(library.entries.length,58);
  const native=Buffer.from(await(await request('/api/export','POST',saved)).arrayBuffer()); await writeFile(path.join(output,'greek-mythology.meridian'),native);
  const form=new FormData();form.append('file',new Blob([native]),'atlas.meridian');const imported=await fetch(origin+'/api/import',{method:'POST',body:form});assert.ok(imported.ok);assert.deepEqual(await imported.json(),saved);report.nativeRoundtrip=true;
  browser=await chromium.launch();const page=await browser.newPage({viewport:{width:1800,height:1120},acceptDownloads:true});page.on('pageerror',e=>report.errors.push(e.message));
  const start=performance.now();await page.goto(`${origin}/?project=${candidate.id}&canvas=${candidate.entryCanvasId}`); await expect(page.locator('.ms-node')).toHaveCount(10);report.openMs=Math.round(performance.now()-start);
  await page.getByRole('button',{name:'Fit canvas',exact:true}).click();await page.screenshot({path:path.join(output,'atlas-overview.png')});
  const card=id=>page.locator(`.ms-node[data-node-id="${id}"]`), genealogy=aliases.genealogy.nodes['genealogy-root'];
  await card(genealogy).locator('.ms-node-title').click();await expect(page.locator('.inspector').getByLabel('Title',{exact:true})).toHaveValue('The genealogy');assert.equal(await page.locator('.ms-link').count(),0);
  await page.waitForTimeout(550);const camera=await page.locator('.ms-world').evaluate(n=>n.style.transform),url=page.url();
  await card(genealogy).locator('.ms-node-title').dblclick();await expect(card(genealogy)).toHaveAttribute('aria-expanded','true');assert.equal(page.url(),url);assert.equal(await page.locator('.ms-world').evaluate(n=>n.style.transform),camera);
  await page.getByRole('button',{name:'Save',exact:true}).click();await expect(page.locator('.save-state')).toHaveText('Saved · r5');await page.reload();await expect(card(genealogy)).toHaveAttribute('aria-expanded','true');report.editorSaveReopen=true;
  const after=await(await request(`/api/projects/${candidate.id}`)).json();assert.equal(hash(after.assets),hash(saved.assets));assert.equal(after.canvases.length,1);
  // Publish using the actual editor command, then parse the downloaded portable HTML.
  const downloaded=page.waitForEvent('download');await page.getByRole('button',{name:'Publish',exact:true}).click();await page.getByRole('button',{name:'Publish HTML',exact:true}).click();const download=await downloaded;const file=path.join(output,'greek-mythology.html');await download.saveAs(file);const publication=readPublication(await readFile(file,'utf8'));assert.equal(publication.canvases.length,1);assert.equal(publication.nodes.length,candidate.nodes.length);const nodeIds=new Map(publication.nodes.map((n,i)=>[n.id,after.nodes[i].id])); const restoredLinks=publication.links.map((l,i)=>({...l,id:after.links[i].id,canvasId:after.links[i].canvasId,sourceId:nodeIds.get(l.sourceId),targetId:nodeIds.get(l.targetId),...(l.parentId?{parentId:nodeIds.get(l.parentId)}:{})})); assert.equal(hash(restoredLinks),hash(after.links),'Publication import must remap identities while retaining relationship ownership');report.publicationRoundtrip=true;
  await page.goto(origin+'/');await expect(page.locator('.library-workspace')).toBeVisible();assert.equal((await(await request('/api/library')).json()).entries.length,58);report.libraryStaysConsolidated=true;
  await page.screenshot({path:path.join(output,'library.png')});
  assert.deepEqual(report.errors,[]);report.passed=true;report.counts=manifest.after;
} catch(error){ report.error=String(error.stack).slice(0,6000);throw error; }
finally{if(browser)await browser.close();if(host.exitCode===null){const ended=once(host,'exit');host.kill();await ended;}await writeFile(path.join(output,'package-verification.json'),JSON.stringify(report,null,2));}
console.log(JSON.stringify(report));
