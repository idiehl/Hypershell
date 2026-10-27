import assert from 'node:assert/strict';
import path from 'node:path';
import { readFile, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { galleryItems } from '../../src/core/gallery.ts';

// One requested collection edit, with saved/draft checkpoints and optimistic
// concurrency. Default execution prepares files only; --apply performs the save.
const apply = process.argv.slice(2).includes('--apply');
assert.ok(process.argv.slice(2).every(arg=>arg==='--apply'));
const workspace=path.resolve(import.meta.dirname,'../..'),qa=path.join(workspace,'output/gallery-qa-20261008');
assert.equal(workspace.toLowerCase(),path.resolve('I:/dev/Meridian').toLowerCase());
const origin='http://127.0.0.1:5240',id='e35cba82-f644-416a-ac14-12645893ed88',nodeId='005fe68a-bc30-4b11-af1d-4dadec799b1c';
const json=async(route,method='GET',body)=>{
  const response=await fetch(origin+route,{method,headers:body===undefined?{}:{'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});
  if(method==='GET'&&route.endsWith('/recovery')&&response.status===404)return null;
  assert.ok(response.ok,`${method} ${route}: ${response.status} ${await response.clone().text()}`);return response.json();
};
const status=await json('/__meridian_status');assert.equal(status.application,'atlas-meridian');assert.ok(status.capabilities.includes('authoring-recovery-guard-v1'));
const saved=await json(`/api/projects/${id}`),recovery=await json(`/api/projects/${id}/recovery`);
assert.ok(!recovery||recovery.baseRevision===saved.revision,'A stale draft must remain protected; review it before composition.');
const base=recovery?{...recovery.project,revision:saved.revision}:saved;
await writeFile(path.join(qa,'live-zeus-saved-before.json'),JSON.stringify(saved,null,2));
if(recovery)await writeFile(path.join(qa,'live-zeus-recovery-before.json'),JSON.stringify(recovery,null,2));
const input=path.join(qa,'live-zeus-current-base.json'),output=path.join(qa,'live-zeus-candidate.json');
await writeFile(input,JSON.stringify(base,null,2));
execFileSync(process.execPath,['--experimental-strip-types',path.join(workspace,'scripts/gallery-cycle/compose-zeus-gallery.mjs'),'--input',input,'--out',output],{cwd:workspace,encoding:'utf8',windowsHide:true,maxBuffer:1024*1024});
const candidate=JSON.parse(await readFile(output,'utf8')),diff=JSON.parse(await readFile(`${output}.diff.json`,'utf8'));
if(!apply){console.log(JSON.stringify({prepared:true,expectedRevision:saved.revision,expectedRecoverySavedAt:recovery?.savedAt??null,candidate:output,diff},null,2));process.exit(0);}
const review=JSON.parse(await readFile(path.join(qa,'package-review.json'),'utf8'));
const protection=JSON.parse(await readFile(path.join(qa,'package-protected-after.json'),'utf8'));
assert.equal(review.passed,true);assert.equal(protection.succeeded,true);assert.equal(protection.savedDataUnchanged,true);
for(const filename of ['editor.js','viewer.js','app.css','scene.css','index.html']){
  const hash=async(file)=>createHash('sha256').update(await readFile(file)).digest('hex');
  assert.equal(await hash(path.join(workspace,'output/app/wwwroot',filename)),await hash(path.join(workspace,'output/gallery-package-20261008/app/wwwroot',filename)),`Install the verified gallery build first: ${filename}`);
}
assert.deepEqual(await json(`/api/projects/${id}`),saved,'Saved content changed; rerun composition against the new revision.');
assert.deepEqual(await json(`/api/projects/${id}/recovery`),recovery,'A recovery draft changed; preserve it and compose again.');
const checkpointName='Before Zeus gallery · 2026-10-08';
const applied=await json(`/api/projects/${id}/authoring`,'POST',{project:candidate,expectedRevision:saved.revision,expectedRecoverySavedAt:recovery?.savedAt??null,checkpointName});
assert.equal(applied.revision,saved.revision+1);assert.deepEqual(await json(`/api/projects/${id}`),applied);
const checkpoints=await json(`/api/projects/${id}/checkpoints`),checkpoint=checkpoints.find(item=>item.name===checkpointName+' · saved');assert.ok(checkpoint);
assert.deepEqual(await json(`/api/projects/${id}/checkpoints/${checkpoint.id}`),saved);
if(recovery){const draft=checkpoints.find(item=>item.name===checkpointName+' · draft');assert.ok(draft);assert.deepEqual(await json(`/api/projects/${id}/checkpoints/${draft.id}`),recovery.project);}
const parent=applied.nodes.find(node=>node.id===nodeId),items=galleryItems(applied,parent);assert.equal(items.length,5);assert.equal(parent.containerVariant,'gallery');
const evidence={applied:true,projectId:id,revision:applied.revision,nodeId,title:parent.title,canvasId:parent.canvasId,checkpointId:checkpoint.id,checkpointName:checkpoint.name,incorporatedRecovery:!!recovery,childIds:items.map(item=>item.id),diff};
await writeFile(path.join(qa,'live-zeus-gallery-applied.json'),JSON.stringify(evidence,null,2));
await writeFile(path.join(qa,'live-zeus-saved-after.json'),JSON.stringify(applied,null,2));
console.log(JSON.stringify(evidence,null,2));
