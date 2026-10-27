import {readFile,writeFile,mkdir} from 'node:fs/promises';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {isDeepStrictEqual} from 'node:util';
import {validateProject} from '../../src/core/rules.ts';

const args=new Map();for(let i=2;i<process.argv.length;i+=2)args.set(process.argv[i],process.argv[i+1]);
if(!args.has('--manifest'))throw new Error('Use --manifest reviewed-manifest.json. This command applies only frozen, reviewed candidates.');
const manifestPath=path.resolve(args.get('--manifest')),root=path.dirname(manifestPath);
const manifest=JSON.parse(await readFile(manifestPath,'utf8'));
const origin='http://127.0.0.1:5240',stamp=new Date().toISOString().replace(/[:.]/g,'-'),out=path.join(root,'apply-'+stamp);
await mkdir(out,{recursive:true});
const hash=p=>createHash('sha256').update(JSON.stringify(p)).digest('hex');
const json=async file=>JSON.parse(await readFile(path.resolve(file),'utf8'));
const receipt={startedAt:new Date().toISOString(),manifest:manifestPath,projects:[],state:'preflight'};
const record=async()=>writeFile(path.join(out,'receipt.json'),JSON.stringify(receipt,null,2));
const api=async (url,init)=>{const r=await fetch(origin+url,{...init,signal:AbortSignal.timeout(180000)});if(!r.ok)throw new Error(`${init?.method??'GET'} ${url}: ${r.status} ${await r.text()}`);return r.json();};
const noRecovery=async id=>{const r=await fetch(`${origin}/api/projects/${id}/recovery`,{signal:AbortSignal.timeout(180000)});if(r.status!==404)throw new Error(`Recovery exists or could not be checked for ${id} (${r.status}); preserve it and rebase.`);};
const ensure=(ok,message)=>{if(!ok)throw new Error(message);};
await record();
try{
 const status=await api('/__meridian_status');
 for(const cap of ['authoring-save-guard-v1','authoring-recovery-guard-v1'])ensure(status.capabilities.includes(cap),`Missing backend capability ${cap}`);
 const beforeList=await api('/api/projects'),beforeLibrary=await api('/api/library');
 await writeFile(path.join(out,'library-before.json'),JSON.stringify(beforeLibrary));
 const candidates=[];
 for(const entry of manifest.projects){
  ensure(entry.approved===true,'Each frozen project requires a concrete completed root review.');
  const candidate=await json(entry.candidate),baseline=await json(entry.baseline),validation=await json(entry.validation),review=await json(entry.review);
  ensure(candidate.id===baseline.id&&candidate.revision===baseline.revision,'Candidate identity/revision mismatch.');
  ensure(hash(candidate)===entry.hash&&validation.sourceHash===entry.hash&&review.sourceHash===entry.hash,'Candidate changed since validation or visual review.');
  ensure(validateProject(candidate).length===0&&validation.structural.length===0,'Structural validation did not pass.');
  ensure(validation.works.every(w=>w.diagnostics.length===0&&!w.diagnosticLimitReached),'Expanded geometry has unresolved or capped errors.');
  ensure(!review.browserErrors?.length,'Installed viewer reported browser errors.');
  const p=validation.preservation;
  ensure(p.projectIdUnchanged&&p.entryWorkUnchanged&&p.workIdsPreserved,'Project/Work identity preservation failed.');
  for(const key of ['missingNodes','alteredAssets','lostBodies','changedFunctional'])ensure(p[key].length===0,`Preservation failed: ${key}`);
  ensure(isDeepStrictEqual([...p.missingLinks].sort(),[...(entry.allowedRemovedLinks??[])].sort()),'Undocumented original link removal.');
  ensure((p.movedNodes?.length??0)===(entry.allowedNodeMoves??0),'Undocumented cross-Work node migration.');
  const current=await api(`/api/projects/${candidate.id}`);
  ensure(isDeepStrictEqual(current,baseline),`Saved baseline changed for ${candidate.id}; rebase before applying.`);
  await noRecovery(candidate.id);
  const body=JSON.stringify({project:candidate,expectedRevision:baseline.revision,expectedRecoverySavedAt:null,checkpointName:'Before mythology refinement'});
  ensure(Buffer.byteLength(body)<100*1024*1024,'Request exceeds the installed host 100MiB limit.');
  const checkpointsBefore=await api(`/api/projects/${candidate.id}/checkpoints`);
  candidates.push({entry,candidate,baseline,body,checkpointsBefore});
  receipt.projects.push({id:candidate.id,title:candidate.title,hash:entry.hash,expectedRevision:baseline.revision,state:'preflight-passed',bytes:Buffer.byteLength(body)});
 }
 await record();
 for(let i=0;i<candidates.length;i++){
  const {entry,candidate,baseline,body,checkpointsBefore}=candidates[i],r=receipt.projects[i],id=candidate.id;
  // Recheck immediately before every mutation. The atomic endpoint independently
  // checks the revision and recovery token while checkpointing under one lock.
  ensure(isDeepStrictEqual(await api(`/api/projects/${id}`),baseline),'Baseline changed during batch preflight.');await noRecovery(id);
  r.state='save-request-started';r.saveStartedAt=new Date().toISOString();await record();
  let response,error;
  try{response=await api(`/api/projects/${id}/authoring`,{method:'POST',headers:{'Content-Type':'application/json'},body});}
  catch(e){error=String(e);r.responseError=error;}
  const expected={...candidate,revision:baseline.revision+1},saved=await api(`/api/projects/${id}`);
  if(!isDeepStrictEqual(saved,expected)){r.state='save-not-proven';await record();throw new Error(error??`Saved readback did not match reviewed candidate ${id}. No retry or rollback performed.`);}
  if(response)ensure(isDeepStrictEqual(response,expected),'Save response differs from readback.');
  const cp=await api(`/api/projects/${id}/checkpoints`),oldIds=new Set(checkpointsBefore.map(c=>c.id));
  const created=cp.filter(c=>!oldIds.has(c.id)&&c.name==='Before mythology refinement · saved'&&c.revision===baseline.revision);
  ensure(created.length===1,'Expected one atomic saved-baseline checkpoint.');
  ensure(isDeepStrictEqual(await api(`/api/projects/${id}/checkpoints/${created[0].id}`),baseline),'Checkpoint readback differs from original saved project.');
  await noRecovery(id);
  await writeFile(path.join(out,id+'-saved.json'),JSON.stringify(saved));
  Object.assign(r,{state:'saved-and-readback-verified',revision:saved.revision,checkpoint:created[0],savedHash:hash(saved),completedAt:new Date().toISOString()});
  console.log(JSON.stringify({title:r.title,revision:r.revision,checkpoint:r.checkpoint.id,state:r.state}));await record();
 }
 ensure(isDeepStrictEqual(await api('/api/library'),beforeLibrary),'Library changed during operation; inspect before claiming unchanged.');
 const afterList=await api('/api/projects'),targetIds=new Set(candidates.map(c=>c.candidate.id));
 receipt.unrelatedProjectChanges=beforeList.filter(p=>!targetIds.has(p.id)&&!isDeepStrictEqual(afterList.find(a=>a.id===p.id),p));
 ensure(receipt.unrelatedProjectChanges.length===0,'An unrelated project changed during operation; inspect concurrency.');
 receipt.state='complete';receipt.completedAt=new Date().toISOString();await record();console.log(JSON.stringify({receipt:path.join(out,'receipt.json'),state:receipt.state}));
}catch(error){receipt.state='stopped';receipt.error=String(error);await record();throw error;}
