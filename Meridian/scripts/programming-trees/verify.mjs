/** Independent preservation and actual native-scene geometry gate.
 * Reads the frozen candidates; writes reports only. No host/network operations.
 * Expansion is a clone-only collapsed flag edit, so no layout repair can conceal
 * an authored collision. Uses the existing shared effective-scene audit.
 */
import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {isDeepStrictEqual} from 'node:util';
import {auditScene} from '../programming-atlas/verify.mjs';
import {validateProject} from '../../src/core/rules.ts';

const sha=value=>createHash('sha256').update(typeof value==='string'||Buffer.isBuffer(value)?value:JSON.stringify(value)).digest('hex');
const read=async file=>JSON.parse((await readFile(file,'utf8')).replace(/^\uFEFF/,''));
for(const slug of process.argv.slice(2).length?process.argv.slice(2):['csharp','cpp']){
  assert(['csharp','cpp'].includes(slug));
  const dir=path.resolve('output/programming-trees-20261021',slug);
  const files=['baseline.json','draft.json','aliases.json','manifest.json'];
  const originalHashes=Object.fromEntries(await Promise.all(files.map(async name=>[name,sha(await readFile(path.join(dir,name)))])));
  const [baseline,draft,aliases,manifest]=await Promise.all(files.map(name=>read(path.join(dir,name))));
  assert.equal(sha(draft),manifest.draftSHA256,'Frozen candidate hash no longer matches.');
  assert.equal(sha(baseline),manifest.baselineSHA256,'Captured original hash no longer matches.');
  assert.equal(baseline.revision,slug==='csharp'?9:1);assert.equal(draft.revision,baseline.revision);
  assert.equal(draft.id,baseline.id);assert.equal(draft.title,baseline.title);
  assert.deepEqual(validateProject(draft),[]);
  const preserved={};
  for(const kind of ['canvases','nodes','links','assets']){
    const current=new Map(draft[kind].map(item=>[item.id,item]));
    assert.equal(current.size,draft[kind].length,`${kind}: duplicate identity.`);
    for(const original of baseline[kind])assert(isDeepStrictEqual(current.get(original.id),original),`Existing ${kind} changed: ${original.id}`);
    preserved[kind]={before:baseline[kind].length,after:draft[kind].length,unchangedOriginals:baseline[kind].length};
  }
  for(const [id,snapshot]of Object.entries(baseline.snapshots))assert(isDeepStrictEqual(draft.snapshots[id],snapshot),`Original snapshot changed: ${id}`);
  preserved.snapshots={before:Object.keys(baseline.snapshots).length,after:Object.keys(draft.snapshots).length,unchangedOriginals:Object.keys(baseline.snapshots).length};
  assert.equal(draft.canvases.length,baseline.canvases.length+1,'Exactly one new Work must be appended.');
  const oldWorks=new Set(baseline.canvases.map(item=>item.id)),newWorks=draft.canvases.filter(item=>!oldWorks.has(item.id));
  assert.equal(newWorks.length,1);assert.equal(newWorks[0].id,aliases.canvases.tree);assert.equal(newWorks[0].id,manifest.newWorkId);
  assert.equal(aliases.canvases.collection,baseline.entryCanvasId);assert.equal(aliases.roots.tree,newWorks[0].rootId);
  const identities={canvases:new Set(draft.canvases.map(item=>item.id)),roots:new Set(draft.canvases.map(item=>item.rootId)),nodes:new Set(draft.nodes.map(item=>item.id)),links:new Set(draft.links.map(item=>item.id)),assets:new Set(draft.assets.map(item=>item.id))};
  const aliasCounts={};
  for(const[kind,map]of Object.entries(aliases)){
    assert(identities[kind],`Unexpected alias kind ${kind}`);
    aliasCounts[kind]=Object.keys(map).length;
    for(const[key,id]of Object.entries(map))assert(identities[kind].has(id),`Stale ${kind} alias ${key}`);
  }
  const newWorkId=newWorks[0].id;
  const branchPrefix=slug==='cpp'?/^(?:topic|category|group|type)\//:/^namespace\//;
  const branchIds=new Set(Object.entries(aliases.nodes).filter(([key])=>branchPrefix.test(key)).map(([,id])=>id));
  assert.equal(branchIds.size,slug==='cpp'?1041:736);
  const expanded=structuredClone(draft);
  for(const node of expanded.nodes)if(branchIds.has(node.id))node.collapsed=false;
  // Prove that no layout solver, reveal helper, or normalization moved anything.
  const onlyDisclosureChanged=structuredClone(expanded);
  for(const node of onlyDisclosureChanged.nodes){const original=draft.nodes.find(item=>item.id===node.id);if(Object.hasOwn(original,'collapsed'))node.collapsed=original.collapsed;else delete node.collapsed;}
  assert(isDeepStrictEqual(onlyDisclosureChanged,draft),'Disclosure clone changed something other than subject collapsed flags.');
  const states=[];
  for(const[state,project]of [['initial',draft],['all-subject-branches-expanded',expanded]]){
    console.log(`${slug}: auditing effective native scene ${state}...`);
    const start=Date.now(),scene=auditScene(project,newWorkId);
    const issues=scene.issues.map(issue=>{
      const ids=[...(issue.nodeIds??[]),...(issue.nodeId?[issue.nodeId]:[])];
      return {...issue,nodeKinds:ids.map(id=>project.nodes.find(item=>item.id===id)?.kind??null)};
    });
    states.push({state,...scene,issues,elapsedMs:Date.now()-start});
    console.log(`${slug}: ${state}: ${scene.visibleNodes} native bodies, ${scene.visibleLinks} routes, ${issues.length} findings.`);
  }
  const samePoint=(one,two)=>Math.hypot(one.x+one.width/2-two.x-two.width/2,one.y+one.height/2-two.y-two.height/2)<1e-6;
  const utilityAssessment=states.map(state=>({state:state.state,findings:state.issues.map(issue=>{
    if(issue.code==='node-overlap'){
      const [one,two]=issue.nodeIds.map(id=>draft.nodes.find(node=>node.id===id));
      return {code:issue.code,nodeIds:issue.nodeIds,sharedBusCoincidentJoint:one?.kind==='junction'&&two?.kind==='junction'&&one.parentId===two.parentId&&samePoint(one,two),sameParent:one?.parentId===two?.parentId};
    }
    if(issue.code==='route-crosses-node'){
      const obstacle=draft.nodes.find(node=>node.id===issue.nodeId),link=draft.links.find(link=>link.id===issue.linkId);
      const jointEndpoint=link&&[link.sourceId,link.targetId].map(id=>draft.nodes.find(node=>node.id===id)).find(node=>node?.kind==='junction'&&obstacle?.kind==='junction'&&node.parentId===obstacle.parentId&&samePoint(node,obstacle));
      return {code:issue.code,linkId:issue.linkId,nodeId:issue.nodeId,sharedBusCoincidentJoint:!!jointEndpoint,jointEndpointId:jointEndpoint?.id};
    }
    return {code:issue.code,sharedBusCoincidentJoint:false};
  })}));
  const unchanged=Object.fromEntries(await Promise.all(files.map(async name=>[name,sha(await readFile(path.join(dir,name)))===originalHashes[name]])));
  assert(Object.values(unchanged).every(Boolean),'Input files changed during the audit.');
  const report={generatedAt:new Date().toISOString(),slug,projectId:draft.id,newWorkId,baselineRevision:baseline.revision,
    baselineSHA256:sha(baseline),draftSHA256:sha(draft),preserved,aliasCounts,newWorks:newWorks.map(item=>({id:item.id,title:item.title,rootId:item.rootId})),
    subjectBranchCount:branchIds.size,geometryMethod:'auditScene from scripts/programming-atlas/verify.mjs; effective native display bounds, silhouettes, Window clipping, real link endpoints and Joint paths.',
    disclosureMethod:'Clone only; subject branch collapsed flags set false; all positions, dimensions, links, assets and original identities retained exactly. Non-subject support Windows retain authored disclosure.',
    limits:['Checks actual native body geometry and centerline route clearance; browser typography, contrast, text clipping and complete interaction behavior are separately covered by opened viewer captures.','No finding is waived as a routing-utility contact; utility findings, if any, are retained with their node kinds for explicit review.'],
    states,utilityAssessment,unchanged,passed:states.every(item=>item.issues.length===0)};
  const out=path.join(dir,'preservation-geometry.json');await writeFile(out,JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify({slug,passed:report.passed,preserved,aliasCounts,subjectBranches:branchIds.size,findings:states.map(item=>({state:item.state,count:item.issues.length})),report:out}));
  if(!report.passed)process.exitCode=1;
}
