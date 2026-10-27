#!/usr/bin/env node
/** Guarded installation of three already-reviewed JSON candidates. Never regenerates content. */
import assert from 'node:assert/strict';
import {createHash,randomUUID} from 'node:crypto';
import {readFile,writeFile,mkdir,rename} from 'node:fs/promises';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {isProjectShape,validateProject} from '../../src/core/rules.ts';
import {auditPreservation,OUT} from './shared.mjs';

export const REVIEW_PROJECTS=Object.freeze([
 {key:'gods',projectId:'3c8176d0-884a-4ac9-9a99-a0d5548de629',baselineRevision:47},
 {key:'living',projectId:'3d896c72-36b9-4900-adb6-a68934591233',baselineRevision:5},
 {key:'visual',projectId:'e35cba82-f644-416a-ac14-12645893ed88',baselineRevision:83},
]);
const CANONICALIZATION='sorted-object-keys-v1',MAX_BYTES=100*1024*1024;
const sha=value=>createHash('sha256').update(value).digest('hex');
/** Sort object keys recursively; keep array order, strings, numbers and timestamps exactly. */
export function canonicalJSON(value){
 if(Array.isArray(value))return `[${value.map(canonicalJSON).join(',')}]`;
 if(value&&typeof value==='object')return `{${Object.keys(value).sort().map(key=>`${JSON.stringify(key)}:${canonicalJSON(value[key])}`).join(',')}}`;
 const result=JSON.stringify(value);assert.notEqual(result,undefined,'Canonical JSON cannot include undefined.');return result;
}
export const canonicalSha256=value=>sha(canonicalJSON(value));
const equal=(actual,expected,message)=>assert.equal(canonicalSha256(actual),canonicalSha256(expected),message);
const validHash=value=>typeof value==='string'&&/^[0-9a-f]{64}$/.test(value);
const parseJSON=text=>JSON.parse(text.replace(/^\uFEFF/,''));
async function readJSON(file){return parseJSON(await readFile(file,'utf8'));}
async function writeJSON(file,value){
 await mkdir(path.dirname(file),{recursive:true});const temp=`${file}.${randomUUID()}.tmp`;
 await writeFile(temp,JSON.stringify(value,null,2)+'\n');await rename(temp,file);
}
function localFile(directory,relative){
 assert.ok(typeof relative==='string'&&relative.length&&!path.isAbsolute(relative),'Manifest paths must be relative to its directory.');
 const file=path.resolve(directory,relative),within=path.relative(directory,file);
 assert.ok(within&&within!=='..'&&!within.startsWith(`..${path.sep}`)&&!path.isAbsolute(within),'Manifest paths must stay inside its directory.');return file;
}
function validProject(project,description){
 assert.ok(isProjectShape(project),`${description}: invalid project shape.`);const issues=validateProject(project);
 assert.deepEqual(issues,[],`${description}: invalid graph.`);return project;
}
function preservation(base,candidate){
 const audit=auditPreservation(base,candidate);
 equal(candidate.dependencies,base.dependencies,'External dependency declarations changed.');
 assert.equal(candidate.title,base.title,'Project title changed.');assert.equal(candidate.revision,base.revision,'Candidate revision must equal its reviewed baseline.');
 assert.equal(candidate.schemaVersion,base.schemaVersion,'Schema version changed.');return audit;
}

/** Pure local preparation. The caller writes and reviews this manifest, then binds its hash. */
export async function createReviewManifest({directory=OUT,reviewedAt=new Date().toISOString(),projects=REVIEW_PROJECTS.map(project=>({...project,baselinePath:`baseline/${project.projectId}.json`,candidatePath:`${project.key}/draft.json`}))}={}){
 assert.equal(projects.length,REVIEW_PROJECTS.length,'Exactly three reviewed projects are required.');
 const entries=[];
 for(const known of REVIEW_PROJECTS){
  const spec=projects.find(project=>project.key===known.key);assert.ok(spec,`Missing ${known.key}.`);
  const base=validProject(await readJSON(localFile(directory,spec.baselinePath)),`${known.key} baseline`),candidate=validProject(await readJSON(localFile(directory,spec.candidatePath)),`${known.key} candidate`);
  assert.equal(base.id,known.projectId);assert.equal(base.revision,known.baselineRevision);preservation(base,candidate);
  entries.push({...known,baselinePath:spec.baselinePath,baselineSha256:canonicalSha256(base),candidatePath:spec.candidatePath,candidateSha256:canonicalSha256(candidate)});
 }
 return {version:1,canonicalization:CANONICALIZATION,reviewedAt,projects:entries};
}

async function loadReviewed(manifestFile,expectedManifestHash){
 const manifest=await readJSON(manifestFile),directory=path.dirname(manifestFile),manifestSha256=canonicalSha256(manifest);
 assert.equal(manifest.version,1,'Unsupported review manifest version.');assert.equal(manifest.canonicalization,CANONICALIZATION,'Unsupported canonicalization.');
 assert.ok(typeof manifest.reviewedAt==='string'&&Number.isFinite(Date.parse(manifest.reviewedAt)),'Manifest needs its review timestamp.');
 assert.ok(Array.isArray(manifest.projects)&&manifest.projects.length===3,'Manifest must list exactly three projects.');
 if(expectedManifestHash!==undefined){assert.ok(validHash(expectedManifestHash),'--manifest-sha256 must be 64 lowercase hexadecimal digits.');assert.equal(manifestSha256,expectedManifestHash,'Review manifest hash changed.');}
 const entries=[];
 for(const known of REVIEW_PROJECTS){
  const matches=manifest.projects.filter(entry=>entry.key===known.key);assert.equal(matches.length,1,`Expected one ${known.key} entry.`);const entry=matches[0];
  assert.equal(entry.projectId,known.projectId,'Unexpected project identity.');assert.equal(entry.baselineRevision,known.baselineRevision,'Unexpected baseline revision.');
  assert.ok(validHash(entry.baselineSha256)&&validHash(entry.candidateSha256),'Manifest hashes must be lowercase canonical SHA-256.');
  const baselineFile=localFile(directory,entry.baselinePath),candidateFile=localFile(directory,entry.candidatePath);
  const baselineText=await readFile(baselineFile,'utf8'),candidateText=await readFile(candidateFile,'utf8');
  assert.ok(Buffer.byteLength(candidateText)<MAX_BYTES-1024,'Candidate exceeds the request-size boundary.');
  const base=validProject(parseJSON(baselineText),`${known.key} baseline`),candidate=validProject(parseJSON(candidateText),`${known.key} candidate`);
  assert.equal(base.id,known.projectId);assert.equal(base.revision,known.baselineRevision);
  assert.equal(canonicalSha256(base),entry.baselineSha256,'Baseline hash changed.');assert.equal(canonicalSha256(candidate),entry.candidateSha256,'Reviewed candidate hash changed.');
  const audit=preservation(base,candidate);
  entries.push({...entry,base,candidate,candidateText:candidateText.replace(/^\uFEFF/,''),candidateFileSha256:sha(candidateText),audit});
 }
 return {manifest,manifestSha256,entries};
}
function localHost(value){
 const host=new URL(value??'http://127.0.0.1:5240');
 assert.ok(['localhost','127.0.0.1','[::1]'].includes(host.hostname)&&host.protocol==='http:'&&!host.username&&!host.password&&host.pathname==='/'&&!host.search&&!host.hash,'--host must be a local Meridian HTTP origin.');return host;
}
function expectedSaved(entry){return {...entry.candidate,revision:entry.baselineRevision+1};}
function catalogFor(project){
 const canvasIds=new Set(project.canvases.map(canvas=>canvas.id)),roots=new Map(project.canvases.map(canvas=>[canvas.rootId,canvas.id])),seen=new Set(),links=[];
 for(const node of project.nodes.filter(node=>node.kind==='canvas-link')){
  const target=roots.get(node.targetId)??(canvasIds.has(node.targetId)?node.targetId:undefined),key=`${node.canvasId}:${target}`;
  if(target&&!seen.has(key)){seen.add(key);links.push({sourceCanvasId:node.canvasId,targetCanvasId:target});}
 }
 return {id:project.id,title:project.title,revision:project.revision,entryCanvasId:project.entryCanvasId,canvases:project.canvases.map(canvas=>({id:canvas.id,rootId:canvas.rootId,title:canvas.title,nodeCount:project.nodes.filter(node=>node.canvasId===canvas.id&&node.kind!=='canvas').length})),links};
}
function checkCatalog(catalog,entries,saved=false){
 assert.ok(Array.isArray(catalog),'Invalid library catalog.');
 for(const entry of entries){const actual=catalog.filter(item=>item.id===entry.projectId);assert.equal(actual.length,1,`Catalog missing ${entry.key}.`);
  const {updatedAt,...content}=actual[0];assert.ok(typeof updatedAt==='string'&&Number.isFinite(Date.parse(updatedAt)),'Catalog timestamp is invalid.');
  equal(content,catalogFor(saved?expectedSaved(entry):entry.base),`Catalog destination/name changed: ${entry.key}.`);
 }
}
function checkProjectCatalog(catalog,entries,saved=false){
 assert.ok(Array.isArray(catalog),'Invalid project catalog.');
 for(const entry of entries){const matches=catalog.filter(item=>item.id===entry.projectId);assert.equal(matches.length,1,`Project catalog missing ${entry.key}.`);const actual=matches[0];
  assert.equal(actual.title,entry.base.title,'Catalog title changed.');assert.equal(actual.revision,entry.baselineRevision+(saved?1:0),'Catalog revision changed.');
  assert.ok(typeof actual.updatedAt==='string'&&Number.isFinite(Date.parse(actual.updatedAt)),'Project catalog timestamp is invalid.');
 }
}
/** The library itself is never written; existing external launch destinations stay valid. */
function libraryDestinations(library,entries){
 const byId=new Map(entries.map(entry=>[entry.projectId,entry])),targets=[];
 for(const [nodeId,target]of Object.entries(library.canvasTargets??{}))targets.push({nodeId,target});
 for(const node of library.project?.nodes??[])if(node.canvasTarget)targets.push({nodeId:node.id,target:node.canvasTarget});
 for(const item of library.entries??[])if(item.projectId)targets.push({nodeId:item.id,target:{projectId:item.projectId,...(item.canvasId?{canvasId:item.canvasId}:{})}});
 const checked=[];
 for(const item of targets){const entry=byId.get(item.target.projectId);if(!entry)continue;
  const canvasId=item.target.canvasId??entry.base.entryCanvasId;
  const before=entry.base.canvases.find(canvas=>canvas.id===canvasId),after=entry.candidate.canvases.find(canvas=>canvas.id===canvasId);
  assert.ok(before&&after,'A library destination would become unavailable.');equal({id:after.id,rootId:after.rootId,title:after.title},{id:before.id,rootId:before.rootId,title:before.title},'A library destination changed.');
  checked.push({...item,canvasId,title:after.title});
 }
 return checked;
}

/** Dependency-injected fetch is solely for isolated contract tests. No automatic retries. */
export async function runReviewedApply(options={},dependencies={}){
 const apply=options.apply===true;assert.ok(!apply||options.savedCheckpoint===true,'--apply also requires --saved-checkpoint after the user saved every open Meridian window.');
 assert.ok(!(apply&&options.verifyOnly),'Choose --apply or --verify-only.');assert.ok(!apply||validHash(options.manifestSha256),'--apply requires the independently reviewed --manifest-sha256.');
 const host=localHost(options.host),fetcher=dependencies.fetch??fetch,manifestFile=path.resolve(options.manifest??path.join(OUT,'reviewed-content.json'));
 const {manifest,manifestSha256,entries}=await loadReviewed(manifestFile,options.manifestSha256);
 const outRoot=path.resolve(options.out??path.join(path.dirname(manifestFile),'application'));
 await mkdir(outRoot,{recursive:true});const directory=path.join(outRoot,`${apply?'apply':'verify'}-${new Date().toISOString().replace(/[:.]/g,'-')}-${randomUUID().slice(0,8)}`);await mkdir(directory);
 const receipt={version:1,mode:apply?'apply':'verify-only',startedAt:new Date().toISOString(),host:host.origin,manifestFile,manifestSha256,canonicalization:CANONICALIZATION,status:'preparing',projects:entries.map(entry=>({key:entry.key,projectId:entry.projectId,baselineRevision:entry.baselineRevision,baselineCanonicalSha256:entry.baselineSha256,candidateCanonicalSha256:entry.candidateSha256,candidateFileSha256:entry.candidateFileSha256,candidateJsonStringifySha256:sha(JSON.stringify(entry.candidate)),state:'not-written'}))};
 const receiptFile=path.join(directory,'receipt.json'),persist=()=>writeJSON(receiptFile,receipt);
 async function request(endpoint,method='GET',body){
  const response=await fetcher(new URL(endpoint,host),{method,redirect:'error',signal:AbortSignal.timeout(60000),...(body===undefined?{}:{headers:{'Content-Type':'application/json'},body:typeof body==='string'?body:JSON.stringify(body)})});
  const text=await response.text();if(!response.ok){const detail=text?(()=>{try{return parseJSON(text).error??response.statusText;}catch{return response.statusText;}})():response.statusText;const error=new Error(`${method} ${endpoint}: ${response.status} ${detail}`);error.httpStatus=response.status;throw error;}
  return response.status===204?undefined:parseJSON(text);
 }
 async function noRecovery(id){
  const endpoint=`/api/projects/${encodeURIComponent(id)}/recovery`,response=await fetcher(new URL(endpoint,host),{method:'GET',redirect:'error',signal:AbortSignal.timeout(60000)});
  await response.text();if(response.status===404)return;throw new Error(response.ok?`Recovery draft exists for ${id}; no further writes attempted.`:`Cannot check recovery for ${id} (${response.status}).`);
 }
 async function unchanged(entry){
  const [project]=await Promise.all([request(`/api/projects/${entry.projectId}`),noRecovery(entry.projectId)]);
  assert.equal(project.revision,entry.baselineRevision,`${entry.key}: revision conflict.`);equal(project,entry.base,`${entry.key}: saved baseline content changed.`);return project;
 }
 async function libraryUnchanged(){const current=await request('/api/library');equal(current,receipt.libraryBefore,'The canvas library changed during the operation.');return current;}
 try{
  await persist();const status=await request('/__meridian_status');assert.equal(status.application,'atlas-meridian');assert.equal(status.status,'ready');assert.ok(status.capabilities?.includes('authoring-save-guard-v1'),'Host lacks authoring save guards.');receipt.hostStatus=status;
  const [before,catalog,libraryCatalog,library]=await Promise.all([Promise.all(entries.map(unchanged)),request('/api/projects'),request('/api/library/catalog'),request('/api/library')]);
  checkProjectCatalog(catalog,entries);checkCatalog(libraryCatalog,entries);receipt.projectCatalogBefore=catalog;receipt.libraryCatalogBefore=libraryCatalog;receipt.libraryBefore=library;receipt.libraryCanonicalSha256=canonicalSha256(library);receipt.libraryDestinations=libraryDestinations(library,entries);
  await writeJSON(path.join(directory,'reviewed-content.json'),manifest);
  for(const [index,entry]of entries.entries()){
   const folder=path.join(directory,entry.key);await writeJSON(path.join(folder,'before.json'),before[index]);await writeFile(path.join(folder,'reviewed-candidate.json'),entry.candidateText);
   await writeJSON(path.join(folder,'preservation.json'),entry.audit);receipt.projects[index].state='preflight-verified';
  }
  receipt.status=apply?'preflight-verified':'verified-no-host-writes';receipt.preflightCompletedAt=new Date().toISOString();await persist();
  if(!apply)return {directory,receipt};
  // Every original is checkpointed and read back before the first project replacement.
  for(const [index,entry]of entries.entries()){
   await unchanged(entry);const record=receipt.projects[index];record.state='checkpoint-requested';await persist();
   const metadata=await request(`/api/projects/${entry.projectId}/checkpoints`,'POST',{project:entry.base,name:`Before mythology renewal · r${entry.baselineRevision}`});
   assert.ok(typeof metadata.id==='string'&&metadata.id.length);assert.equal(metadata.revision,entry.baselineRevision);record.checkpoint=metadata;await persist();
   const checkpoint=await request(`/api/projects/${entry.projectId}/checkpoints/${encodeURIComponent(metadata.id)}`);equal(checkpoint,entry.base,'Checkpoint readback differs from the saved baseline.');
   await writeJSON(path.join(directory,entry.key,'checkpoint-readback.json'),checkpoint);record.state='checkpoint-verified';await persist();
  }
  await Promise.all(entries.map(unchanged));await libraryUnchanged();receipt.status='all-checkpoints-verified';await persist();
  for(const [index,entry]of entries.entries()){
   // Recheck all remaining projects, so an observed concurrent edit stops the batch early.
   await Promise.all(entries.slice(index).map(unchanged));await libraryUnchanged();const record=receipt.projects[index];record.state='put-requested';await persist();
   // Preserve the reviewed document's JSON directly. The host changes revision only.
   const body=`{"project":${entry.candidateText},"expectedRevision":${entry.baselineRevision},"requireNoRecovery":true}`;
   const saved=await request(`/api/projects/${entry.projectId}`,'PUT',body);await writeJSON(path.join(directory,entry.key,'put-response.json'),saved);
   equal(saved,expectedSaved(entry),'Save response differs from the exact reviewed candidate (apart from revision).');record.savedRevision=saved.revision;record.state='put-verified';await persist();
   const readback=await request(`/api/projects/${entry.projectId}`);await writeJSON(path.join(directory,entry.key,'saved-readback.json'),readback);
   equal(readback,expectedSaved(entry),'Saved readback differs from the exact reviewed candidate.');await noRecovery(entry.projectId);
   record.savedCanonicalSha256=canonicalSha256(readback);record.state='saved-and-readback-verified';await persist();
  }
  const [projectsAfter,libraryCatalogAfter,libraryAfter]=await Promise.all([request('/api/projects'),request('/api/library/catalog'),libraryUnchanged()]);
  receipt.projectCatalogAfter=projectsAfter;receipt.libraryCatalogAfter=libraryCatalogAfter;receipt.libraryAfter=libraryAfter;await persist();
  checkProjectCatalog(projectsAfter,entries,true);checkCatalog(libraryCatalogAfter,entries,true);libraryDestinations(libraryAfter,entries);
  receipt.status='applied-and-verified';receipt.completedAt=new Date().toISOString();await persist();return {directory,receipt};
 }catch(caught){
  const error=caught instanceof Error?caught:new Error(String(caught));error.receiptDirectory=directory;
  receipt.status='stopped';receipt.error={message:error instanceof Error?error.message:String(error),...(error.httpStatus?{httpStatus:error.httpStatus}:{})};receipt.stoppedAt=new Date().toISOString();
  receipt.recoveryGuidance='No automatic rollback or retry was performed. A requested PUT may have committed even if its response was lost. Review receipts, checkpoints and current saved/recovery state before any further action.';
  // Read-only observations help distinguish an uncommitted conflict from an uncertain save.
  const observations=await Promise.allSettled(entries.map(async entry=>({key:entry.key,project:await request(`/api/projects/${entry.projectId}`)})));
  try{await writeJSON(path.join(directory,'failure-observation.json'),observations.map((item,index)=>item.status==='fulfilled'?item.value:{key:entries[index].key,error:String(item.reason)}));}
  catch(observationError){receipt.observationWriteError=String(observationError);}
  try{await persist();}catch(receiptError){error.message+=` Failure receipt could not be updated: ${String(receiptError)}.`;}
  throw error;
 }
}

const HELP=`Verify or apply the exact reviewed mythology candidates (Node.js 24).

node --experimental-strip-types scripts/mythology-renewal/apply-reviewed.mjs [--verify-only]
  --manifest output/mythology-renewal-20261018/reviewed-content.json
  [--host http://127.0.0.1:5240] [--out local-receipt-directory]
  [--manifest-sha256 CANONICAL_HASH]

Writing additionally requires --apply --saved-checkpoint --manifest-sha256 HASH.
--saved-checkpoint asserts that the user confirmed saving each open Meridian window.
Verification is the default and issues GET requests only. Application creates and
reads back all three server checkpoints before the first guarded PUT. It never
regenerates recipes, modifies the library, deletes recovery, retries or rolls back.
An interrupted/partial batch must be reviewed; do not rerun against new revisions.

Manifest generation is local only (after all candidates have been reviewed):
import {createReviewManifest,canonicalSha256} from './scripts/mythology-renewal/apply-reviewed.mjs';
const manifest=await createReviewManifest(); // write it, review it, record canonicalSha256(manifest)
Hashes sort object keys recursively and preserve arrays. JSON.stringify/file hashes
are recorded separately and are not interchangeable with canonical manifest hashes.`;
export function parseOptions(argv){
 const options={},flags=new Map([['--apply','apply'],['--verify-only','verifyOnly'],['--saved-checkpoint','savedCheckpoint'],['--help','help']]),values=new Map([['--manifest','manifest'],['--manifest-sha256','manifestSha256'],['--host','host'],['--out','out']]);
 for(let i=0;i<argv.length;i++){
  const arg=argv[i],key=flags.get(arg)??values.get(arg);assert.ok(key,`Unknown option ${arg}.`);assert.ok(!Object.hasOwn(options,key),`Repeated option ${arg}.`);
  if(flags.has(arg))options[key]=true;else{assert.ok(argv[i+1]&&!argv[i+1].startsWith('--'),`Missing value for ${arg}.`);options[key]=argv[++i];}
 }
 return options;
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href){
 try{const options=parseOptions(process.argv.slice(2));if(options.help)console.log(HELP);else{const {directory,receipt}=await runReviewedApply(options);console.log(JSON.stringify({directory,status:receipt.status,manifestCanonicalSha256:receipt.manifestSha256,projects:receipt.projects},null,2));}}
 catch(error){console.error(error.message);if(error.receiptDirectory)console.error(`Receipt: ${error.receiptDirectory}`);process.exitCode=1;}
}
