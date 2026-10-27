/** Independent, read-only source/graph audit. Never trusts recipe preservation claims. */
import {readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import {isDeepStrictEqual} from 'node:util';
import assert from 'node:assert/strict';
import {evaluate} from '../../src/core/engine.ts';

const ROOT=fileURLToPath(new URL('../../',import.meta.url));
const DEFAULT=path.join(ROOT,'output/mythology-refinement-20261023');
const projects={visual:'e35cba82-f644-416a-ac14-12645893ed88',gods:'3c8176d0-884a-4ac9-9a99-a0d5548de629',living:'3d896c72-36b9-4900-adb6-a68934591233'};
const visualRemoved=new Set(['87b7ec6d-b4c6-420d-80cc-10310a84a8b0','c6c3f6fe-a4e1-4cb4-99a3-f83b2f1a689a','2b43461d-babe-416f-9934-1755b4f19612','141380a6-617c-4d6d-8bc5-3548a6edf468','28cced08-813f-4b04-9be7-411cac73ef65','a50f1775-b28a-4bd5-8192-e756872cf95b','763b0b0d-ea44-4f4b-9042-73acdea96283']);
const yearLink='12a86360-8513-4967-b1f6-bf86bed15ddb';
const hash=value=>createHash('sha256').update(JSON.stringify(value)).digest('hex');
const equal=isDeepStrictEqual;

/** Prove directed storage connectivity through added routing utilities only.
 * A path must contain the original edge ID. Other original edges and ordinary
 * subject nodes may not bridge a broken relationship. Arrow style is reported
 * separately because split visual routes move arrows to a terminal segment. */
export function routeProof(baseEdge,current,nodes,baseNodeIds,baseEdgeIds,links,expectedTarget=baseEdge.targetId){
  if(!current)return;
  const isNewPivot=id=>!baseNodeIds.has(id)&&nodes.get(id)?.kind==='junction'&&nodes.get(id)?.utilityVariant==='pivot';
  const adjacency=new Map();
  for(const edge of links)if(edge.canvasId===current.canvasId&&edge.kind===baseEdge.kind&&(edge.id===baseEdge.id||!baseEdgeIds.has(edge.id))){
    const list=adjacency.get(edge.sourceId)??[];list.push(edge);adjacency.set(edge.sourceId,list);
  }
  const queue=[{id:baseEdge.sourceId,used:false,path:[],pivots:[]}],seen=new Set();
  for(let at=0;at<queue.length;at++){
    const item=queue[at],key=item.id+'/'+item.used;if(seen.has(key))continue;seen.add(key);
    if(item.id===expectedTarget&&item.used)return{edgeIds:item.path,insertedPivotIds:item.pivots};
    for(const edge of adjacency.get(item.id)??[]){
      if(edge.id===baseEdge.id&&item.used)continue;
      const next=edge.targetId;if(next!==expectedTarget&&!isNewPivot(next))continue;
      const used=item.used||edge.id===baseEdge.id;
      if(next===expectedTarget&&!used)continue;
      queue.push({id:next,used,path:[...item.path,edge.id],pivots:isNewPivot(next)?[...item.pivots,next]:item.pivots});
    }
  }
}

export function auditPreservation(base,candidate,name){
  const errors=[],notes=[],nodes=new Map(candidate.nodes.map(n=>[n.id,n])),links=new Map(candidate.links.map(l=>[l.id,l])),assets=new Map(candidate.assets.map(a=>[a.id,a]));
  const baseNodeIds=new Set(base.nodes.map(n=>n.id)),baseEdgeIds=new Set(base.links.map(l=>l.id));
  const attached=new Set(candidate.nodes.flatMap(n=>[n.assetId,...n.attachments??[]]).filter(Boolean));
  const textSources=candidate.nodes.flatMap(n=>[n.title,n.text].filter(Boolean));
  for(const asset of candidate.assets)if(attached.has(asset.id)&&/^(text\/|application\/json)/.test(asset.mime))textSources.push(Buffer.from(asset.base64,'base64').toString('utf8'));
  const retainedText=textSources.join('\n\u0000\n');
  const relocatedNodes=[],addedPrimaryAssets=[],routingAllowances=[],bodyProof=[],paths=[],removed=[],visualPresentationChanges=[];
  const fail=(code,detail)=>errors.push({code,...detail});
  if(candidate.id!==base.id)fail('project-id-changed',{});
  if(candidate.entryCanvasId!==base.entryCanvasId)notes.push({code:'entry-work-changed',before:base.entryCanvasId,after:candidate.entryCanvasId});
  for(const work of base.canvases){const now=candidate.canvases.find(w=>w.id===work.id);if(!now||now.rootId!==work.rootId)fail('original-work-or-root-lost',{workId:work.id});}
  for(const original of base.nodes){
    const now=nodes.get(original.id);if(!now){fail('original-node-lost',{nodeId:original.id,title:original.title});continue;}
    for(const field of ['kind','attachments','hyperlinks','remoteLinks','targetId','canvasTarget','targetCell','dependencyIds','operation','value','valueType','closed','exposed','retiredBlockContent'])if(!equal(original[field],now[field]))fail('original-node-field-changed',{nodeId:original.id,title:original.title,field});
    if(original.assetId&&now.assetId!==original.assetId)fail('original-primary-asset-changed',{nodeId:original.id,before:original.assetId,after:now.assetId});
    else if(!original.assetId&&now.assetId)addedPrimaryAssets.push({nodeId:original.id,assetId:now.assetId});
    if(now.canvasId!==original.canvasId)relocatedNodes.push({nodeId:original.id,from:original.canvasId,to:now.canvasId});
    if(!equal(original.rules,now.rules)){
      const before=original.rules,after=now.rules;
      if(before&&after&&equal(before.linkKinds,after.linkKinds)&&equal(before.operations,after.operations)&&equal([...before.nodeKinds??[],'junction'],after.nodeKinds))routingAllowances.push({nodeId:original.id,addedAllowedKind:'junction'});
      else fail('original-rules-changed',{nodeId:original.id,before,after});
    }
    if(original.text){const inPlace=now.text===original.text;if(!inPlace&&!retainedText.includes(original.text))fail('original-body-lost',{nodeId:original.id,title:original.title});else bodyProof.push({nodeId:original.id,location:inPlace?'original node':'accessible reading source',sha256:hash(original.text)});}
  }
  for(const asset of base.assets)if(!equal(assets.get(asset.id),asset))fail('original-asset-changed-or-lost',{assetId:asset.id,name:asset.name});
  for(const [id,snapshot] of Object.entries(base.snapshots))if(!baseNodeIds.has(id)&&!equal(snapshot,candidate.snapshots[id]))fail('external-snapshot-changed-or-lost',{nodeId:id});
  if(!equal(base.dependencies,candidate.dependencies))fail('external-dependencies-changed',{});
  for(const original of base.links){
    const now=links.get(original.id);
    if(!now){
      if(name==='visual'&&visualRemoved.has(original.id)&&original.kind==='visual'&&original.relation==='generic'&&!original.label){removed.push({linkId:original.id,reason:'Documented generic reading/navigation chrome contact; complete original record archived in supporting Notes.'});continue;}
      fail('original-edge-lost',{linkId:original.id});continue;
    }
    for(const field of ['kind','relation','targetPort','snap'])if(!equal(original[field],now[field]))fail('original-edge-semantic-field-changed',{linkId:original.id,field});
    let target=original.targetId;
    if(name==='visual'&&original.id===yearLink){
      const revised=nodes.get(now.targetId),retained=nodes.get(original.targetId);
      if(revised?.kind==='media'&&revised.title==='A Divided Year'&&revised.text==='One third below · Two thirds above'&&retained?.kind==='number'&&retained.value===0){target=revised.id;notes.push({code:'documented-year-media-retarget',linkId:original.id,originalTarget:original.targetId,target});}
      else fail('year-retarget-outside-approved-scope',{linkId:original.id});
    }
    const proof=routeProof(original,now,nodes,baseNodeIds,baseEdgeIds,candidate.links,target);
    if(!proof)fail('original-subject-edge-disconnected',{linkId:original.id,originalSource:original.sourceId,originalTarget:original.targetId,currentSource:now.sourceId,currentTarget:now.targetId});
    else{
      if(original.kind!=='visual')for(const id of proof.insertedPivotIds)if(nodes.get(id).valueType!==(original.kind==='functional'?'boolean':'number'))fail('routing-value-type-changed',{linkId:original.id,nodeId:id});
      paths.push({linkId:original.id,kind:original.kind,...proof});
    }
    if(now.label!==original.label||now.direction!==original.direction)visualPresentationChanges.push({linkId:original.id,oldLabel:original.label,newLabel:now.label,oldDirection:original.direction,newDirection:now.direction});
  }
  for(const relocation of relocatedNodes)if(!candidate.canvases.some(w=>w.id===relocation.to))fail('node-relocated-to-missing-work',relocation);
  for(const edge of candidate.links)if(!nodes.has(edge.sourceId)||!nodes.has(edge.targetId)||nodes.get(edge.sourceId).canvasId!==edge.canvasId||nodes.get(edge.targetId).canvasId!==edge.canvasId)fail('edge-endpoint-work-mismatch',{linkId:edge.id});
  const functionalNodes=base.nodes.filter(n=>['input','number','switch','joint','output'].includes(n.kind)),controls=base.nodes.filter(n=>n.kind==='input'||n.kind==='switch'),groups=new Map();
  for(const n of controls){const list=groups.get(n.parentId)??[];list.push(n);groups.set(n.parentId,list);}
  const scenarios=[new Map()];for(const list of groups.values()){if(list.length>8){fail('functional-control-group-too-large',{nodeIds:list.map(n=>n.id)});continue;}for(let mask=0;mask<2**list.length;mask++)scenarios.push(new Map(list.map((n,i)=>[n.id,{[n.kind==='switch'?'closed':'value']:!!(mask&(1<<i))}])));}
  let functionalComparisons=0;for(const scenario of functionalNodes.length?scenarios:[]){const before=evaluate({...base,nodes:base.nodes.map(n=>scenario.has(n.id)?{...n,...scenario.get(n.id)}:n)}),after=evaluate({...candidate,nodes:candidate.nodes.map(n=>scenario.has(n.id)?{...n,...scenario.get(n.id)}:n)});for(const n of functionalNodes){functionalComparisons++;if(before.values[n.id]!==after.values[n.id]||before.errors[n.id]!==after.errors[n.id])fail('functional-evaluation-changed',{nodeId:n.id,scenario:Object.fromEntries(scenario),before:before.values[n.id],after:after.values[n.id],oldError:before.errors[n.id],newError:after.errors[n.id]});}}
  return{project:name,projectId:base.id,passed:!errors.length,sourceHash:hash(candidate),baselineRevision:base.revision,counts:{originalWorks:base.canvases.length,candidateWorks:candidate.canvases.length,originalNodes:base.nodes.length,candidateNodes:candidate.nodes.length,originalAssets:base.assets.length,originalEdges:base.links.length,provedEdges:paths.length,documentedRemovedNonsemanticEdges:removed.length,retainedBodies:bodyProof.length,routedEdges:paths.filter(p=>p.edgeIds.length>1).length,relocatedOriginalNodes:relocatedNodes.length,primaryAssetsAddedToFormerTextNodes:addedPrimaryAssets.length,routingKindAllowances:routingAllowances.length,functionalAssignments:functionalNodes.length?scenarios.length:0,functionalComparisons,errors:errors.length},errors,notes,removedNonsemanticEdges:removed,routingKindAllowances:routingAllowances,relocatedOriginalNodes:relocatedNodes,addedPrimaryAssets,visualPresentationChanges,bodyProof,edgeProof:paths};
}

// Guard the audit itself against accepting an unrelated subject as a routing hop.
const fixtureEdge={id:'original',canvasId:'work',sourceId:'a',targetId:'b',kind:'visual'},fixtureNodes=new Map([['a',{id:'a',kind:'media'}],['b',{id:'b',kind:'media'}],['p',{id:'p',kind:'junction',utilityVariant:'pivot'}]]),fixtureOriginalIds=new Set(['a','b']);
const fixtureLinks=[{id:'new',canvasId:'work',kind:'visual',sourceId:'a',targetId:'p'},{...fixtureEdge,sourceId:'p'}];
assert(routeProof(fixtureEdge,fixtureLinks[1],fixtureNodes,fixtureOriginalIds,new Set(['original']),fixtureLinks));
assert(!routeProof(fixtureEdge,fixtureLinks[1],new Map([...fixtureNodes,['p',{id:'p',kind:'media'}]]),fixtureOriginalIds,new Set(['original']),fixtureLinks));
assert(!routeProof(fixtureEdge,fixtureLinks[1],fixtureNodes,fixtureOriginalIds,new Set(['original']),fixtureLinks.slice(1)));

if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  const args=new Map();for(let i=2;i<process.argv.length;i+=2)args.set(process.argv[i],process.argv[i+1]);
  const directory=path.resolve(args.get('--root')??DEFAULT),selected=args.has('--project')?[args.get('--project')]:Object.keys(projects),reports=[];
  for(const name of selected){if(!projects[name])throw new Error('Unknown project '+name);const [base,candidate]=await Promise.all([readFile(path.join(directory,'baseline',projects[name]+'.json'),'utf8'),readFile(path.join(directory,name,'draft.json'),'utf8')]);const report=auditPreservation(JSON.parse(base),JSON.parse(candidate),name);reports.push(report);console.log(JSON.stringify({project:name,passed:report.passed,hash:report.sourceHash,...report.counts,firstErrors:report.errors.slice(0,5)}));}
  const output=path.resolve(args.get('--out')??path.join(directory,'preservation-proof.json'));await writeFile(output,JSON.stringify({recordedAt:new Date().toISOString(),passed:reports.every(r=>r.passed),method:'Independent frozen-baseline comparison; source content in accessible attachments; original edge ID on a directed path with only added Pivot intermediates.',projects:reports},null,2));
  console.log(JSON.stringify({report:output,passed:reports.every(r=>r.passed)}));if(reports.some(r=>!r.passed))process.exitCode=1;
}
