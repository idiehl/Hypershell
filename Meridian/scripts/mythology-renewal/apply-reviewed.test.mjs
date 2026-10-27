/** Isolated HTTP-contract tests; this file never contacts a running Meridian host. */
import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,mkdir,readFile,writeFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {newProject,addNode} from '../../src/core/model.ts';
import {REVIEW_PROJECTS,canonicalSha256,createReviewManifest,runReviewedApply,parseOptions} from './apply-reviewed.mjs';

const stamp='2026-10-18T20:00:00.000Z',json=value=>new Response(JSON.stringify(value),{headers:{'Content-Type':'application/json'}});
async function fixture(){
 const directory=await mkdtemp(path.join(tmpdir(),'meridian-reviewed-')),projects=new Map(),candidates=new Map();
 for(const known of REVIEW_PROJECTS){
  const base=newProject(`Original ${known.key}`);base.id=known.projectId;base.revision=known.baselineRevision;
  const note=addNode(base,base.entryCanvasId,'media','Retained text');note.text='Keep every original word.';base.assets.push({id:crypto.randomUUID(),name:'kept.txt',mime:'text/plain',base64:'a2VwdA=='});
  const candidate=structuredClone(base);candidate.nodes[1].style={portalColor:'#aabbcc'};const addition=addNode(candidate,candidate.entryCanvasId,'text','A new caption');addition.text='Presentation improvement.';
  await mkdir(path.join(directory,'baseline'),{recursive:true});await mkdir(path.join(directory,known.key));
  await writeFile(path.join(directory,'baseline',`${known.projectId}.json`),JSON.stringify(base));await writeFile(path.join(directory,known.key,'draft.json'),JSON.stringify(candidate));projects.set(known.projectId,base);candidates.set(known.projectId,candidate);
 }
 const manifest=await createReviewManifest({directory,reviewedAt:stamp}),manifestFile=path.join(directory,'reviewed-content.json');await writeFile(manifestFile,JSON.stringify(manifest));
 return {directory,projects,candidates,manifest,options:{manifest:manifestFile,out:path.join(directory,'receipts'),manifestSha256:canonicalSha256(manifest)}};
}
function server(fixture,hooks={}){
 const state={projects:structuredClone(fixture.projects),requests:[],checkpoints:new Map(),library:{schemaVersion:1,revision:9,entries:REVIEW_PROJECTS.map((known,i)=>({id:`library-${i}`,kind:'canvas',title:known.key,parentId:null,projectId:known.projectId,canvasId:fixture.projects.get(known.projectId).entryCanvasId,collapsed:true,x:i*200,y:0}))}};
 const summaries=()=>[...state.projects.values()].map(p=>({id:p.id,title:p.title,revision:p.revision,updatedAt:stamp}));
 const catalog=()=>[...state.projects.values()].map(p=>({...summaries().find(item=>item.id===p.id),entryCanvasId:p.entryCanvasId,canvases:p.canvases.map(c=>({id:c.id,rootId:c.rootId,title:c.title,nodeCount:p.nodes.filter(n=>n.canvasId===c.id&&n.kind!=='canvas').length})),links:[]}));
 const fetch=async(url,init={})=>{
  const method=init.method??'GET',pathname=new URL(url).pathname,body=init.body?JSON.parse(init.body):undefined,call={method,path:pathname,body};state.requests.push(call);
  const intercepted=await hooks.before?.(call,state);if(intercepted)return intercepted;
  if(pathname==='/__meridian_status')return json({application:'atlas-meridian',status:'ready',capabilities:['authoring-save-guard-v1']});
  if(pathname==='/api/library')return json(state.library);
  if(pathname==='/api/library/catalog')return json(catalog());
  if(pathname==='/api/projects')return json(summaries());
  const [,id,suffix,checkpointId]=pathname.match(/^\/api\/projects\/([^/]+)(?:\/(recovery|checkpoints)(?:\/([^/]+))?)?$/)??[];
  assert.ok(id,`Unexpected endpoint ${pathname}`);
  if(suffix==='recovery')return new Response('',{status:404});
  if(suffix==='checkpoints'&&method==='POST'){
   const metadata={id:crypto.randomUUID().replaceAll('-',''),name:body.name,revision:body.project.revision,createdAt:stamp};state.checkpoints.set(metadata.id,structuredClone(body.project));return json(metadata);
  }
  if(suffix==='checkpoints'&&checkpointId)return json(state.checkpoints.get(checkpointId));
  if(method==='GET')return json(state.projects.get(id));
  if(method==='PUT'){
   assert.equal(body.requireNoRecovery,true);assert.equal(body.expectedRevision,state.projects.get(id).revision);assert.equal(body.project.revision,body.expectedRevision);
   const saved={...body.project,revision:body.expectedRevision+1};state.projects.set(id,structuredClone(saved));await hooks.afterPut?.(call,state,saved);return json(saved);
  }
  assert.fail(`Unexpected request ${method} ${pathname}`);
 };
 return {state,fetch};
}
const writing=fixture=>({...fixture.options,apply:true,savedCheckpoint:true});

test('canonical hashes sort object keys and retain arrays; parser rejects ambiguous flags',()=>{
 assert.equal(canonicalSha256({b:2,a:[{z:1,x:2}]}),canonicalSha256({a:[{x:2,z:1}],b:2}));assert.notEqual(canonicalSha256([1,2]),canonicalSha256([2,1]));
 assert.deepEqual(parseOptions(['--verify-only','--manifest','x.json']),{verifyOnly:true,manifest:'x.json'});assert.throws(()=>parseOptions(['--apply','--apply']),/Repeated/);
});
test('default verification issues GET requests only and retains prior catalogs and library destinations',async()=>{
 const f=await fixture(),s=server(f),{receipt}=await runReviewedApply(f.options,{fetch:s.fetch});assert.equal(receipt.status,'verified-no-host-writes');assert.ok(s.state.requests.every(r=>r.method==='GET'));assert.equal(receipt.libraryDestinations.length,3);assert.equal(receipt.projectCatalogBefore.length,3);assert.equal(receipt.libraryCatalogBefore.length,3);assert.equal(s.state.checkpoints.size,0);
});
test('all three checkpoints are verified before the first exact guarded PUT; revision alone advances',async()=>{
 const f=await fixture(),s=server(f),{directory,receipt}=await runReviewedApply(writing(f),{fetch:s.fetch});assert.equal(receipt.status,'applied-and-verified');
 const firstPut=s.state.requests.findIndex(r=>r.method==='PUT');assert.equal(s.state.requests.slice(0,firstPut).filter(r=>r.method==='POST').length,3);assert.equal(s.state.requests.slice(0,firstPut).filter(r=>/\/checkpoints\//.test(r.path)).length,3);
 for(const known of REVIEW_PROJECTS){assert.deepEqual(s.state.projects.get(known.projectId),{...f.candidates.get(known.projectId),revision:known.baselineRevision+1});const before=JSON.parse(await readFile(path.join(directory,known.key,'before.json'),'utf8'));assert.deepEqual(before,f.projects.get(known.projectId));}
 assert.ok(s.state.requests.filter(r=>r.method!=='GET').every(r=>r.path.startsWith('/api/projects/')));
});
test('missing apply assertions, changed manifest, or changed candidate stop before host access',async()=>{
 const f=await fixture();let calls=0;const fetch=()=>{calls++;throw Error('No request expected');};
 await assert.rejects(runReviewedApply({...f.options,apply:true},{fetch}),/saved-checkpoint/);
 await assert.rejects(runReviewedApply({...writing(f),manifestSha256:'0'.repeat(64)},{fetch}),/manifest hash changed/);
 const candidate=f.candidates.get(REVIEW_PROJECTS[0].projectId);candidate.nodes[1].style.portalColor='#ffffff';await writeFile(path.join(f.directory,'gods/draft.json'),JSON.stringify(candidate));
 await assert.rejects(runReviewedApply(writing(f),{fetch}),/candidate hash changed/);assert.equal(calls,0);
});
test('recovery or same-revision baseline changes stop preflight without any POST or PUT',async()=>{
 for(const mode of ['recovery','changed']){const f=await fixture(),s=server(f,{before(call,state){if(mode==='recovery'&&call.path.endsWith('/recovery'))return json({project:{},savedAt:stamp});if(mode==='changed'&&call.path===`/api/projects/${REVIEW_PROJECTS[0].projectId}`)return json({...state.projects.get(REVIEW_PROJECTS[0].projectId),title:'Concurrent edit'});}});
  await assert.rejects(runReviewedApply(writing(f),{fetch:s.fetch}),/Recovery draft|baseline content changed/);assert.ok(s.state.requests.every(r=>r.method==='GET'));
 }
});
test('third checkpoint failure prevents every PUT',async()=>{
 const f=await fixture();let posts=0;const s=server(f,{before(call){if(call.method==='POST'&&++posts===3)return new Response(JSON.stringify({error:'Conflict'}),{status:409});}});
 await assert.rejects(runReviewedApply(writing(f),{fetch:s.fetch}),/409/);assert.equal(posts,3);assert.equal(s.state.requests.filter(r=>r.method==='PUT').length,0);assert.deepEqual(s.state.projects,f.projects);
});
test('second PUT conflict leaves the first save intact and never retries, rolls back or writes the third',async()=>{
 const f=await fixture(),s=server(f,{before(call){if(call.method==='PUT'&&call.path.endsWith(REVIEW_PROJECTS[1].projectId))return new Response(JSON.stringify({error:'Recovery appeared'}),{status:409});}});
 let failure;await assert.rejects(runReviewedApply(writing(f),{fetch:s.fetch}),error=>(failure=error,/409/.test(error.message)));
 assert.equal(s.state.requests.filter(r=>r.method==='PUT').length,2);assert.equal(s.state.projects.get(REVIEW_PROJECTS[0].projectId).revision,48);assert.equal(s.state.projects.get(REVIEW_PROJECTS[1].projectId).revision,5);assert.equal(s.state.projects.get(REVIEW_PROJECTS[2].projectId).revision,83);
 const receipt=JSON.parse(await readFile(path.join(failure.receiptDirectory,'receipt.json'),'utf8'));assert.equal(receipt.status,'stopped');assert.equal(receipt.projects[1].state,'put-requested');assert.match(receipt.recoveryGuidance,/may have committed/);
});
test('lost PUT response after commit is recorded as uncertain and does not trigger a retry',async()=>{
 const f=await fixture(),s=server(f,{afterPut(){throw Error('Connection lost after commit');}});let failure;
 await assert.rejects(runReviewedApply(writing(f),{fetch:s.fetch}),error=>(failure=error,/Connection lost/.test(error.message)));
 assert.equal(s.state.requests.filter(r=>r.method==='PUT').length,1);assert.equal(s.state.projects.get(REVIEW_PROJECTS[0].projectId).revision,48);
 const observation=JSON.parse(await readFile(path.join(failure.receiptDirectory,'failure-observation.json'),'utf8'));assert.equal(observation[0].project.revision,48);
});
test('a changed library after checkpointing prevents every project PUT',async()=>{
 const f=await fixture();let libraryReads=0;const s=server(f,{before(call,state){if(call.path==='/api/library'&&++libraryReads===2){state.library.entries[0].title='User changed this';return json(state.library);}}});
 await assert.rejects(runReviewedApply(writing(f),{fetch:s.fetch}),/canvas library changed/);assert.equal(s.state.checkpoints.size,3);assert.equal(s.state.requests.filter(r=>r.method==='PUT').length,0);
});
