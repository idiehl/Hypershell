import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';
import {applyRecipe,inspectProject} from '../../src/core/authoring.ts';
const root='I:/dev/Meridian/output/mythology-current-rework-20261023',out=root+'/library';
const before=JSON.parse(await readFile(root+'/library-before.json','utf8'));
const visual=JSON.parse(await readFile(root+'/visual/draft.json','utf8'));
const godsMap=JSON.parse(await readFile(root+'/gods/title-map.json','utf8'));
const myth='a3d00829-1577-4dec-8591-24495b6c885d',nodes=new Map(before.project.nodes.map(n=>[n.id,n]));
const entries={
 'd130e3cd-d7a7-48c6-85ed-da060fe872f8':'Living Atlas',
 '842eae57-c870-4a25-9ec1-568fc3c6e420':'Visual Atlas',
 '42dc8ec1-c709-4543-ba51-706fb937d212':'Gods & Epics',
};
const titles=new Map([...visual.canvases.map(w=>[w.id,w.title]),...godsMap.map(w=>[w.id,w.title])]);
const isMyth=n=>{const seen=new Set();while(n&&!seen.has(n.id)){if(n.id===myth)return true;seen.add(n.id);n=nodes.get(n.parentId);}return false;};
const changes=[];
for(const n of before.project.nodes){
 if(n.kind!=='canvas-link'||!isMyth(n))continue;
 const title=entries[n.id]??titles.get(n.canvasTarget?.canvasId);
 if(title&&title!==n.title)changes.push({id:n.id,before:n.title,after:title,target:n.canvasTarget});
}
const recipe={version:1,nodes:changes.map((c,i)=>({key:'myth-portal-'+(i+1),existingId:c.id,title:c.after}))};
const result=applyRecipe(before.project,recipe),candidate={...before,project:result.project};
const nodeIds=new Set(changes.map(c=>c.id));
// Reject even incidental normalization: only title strings may change.
assert.equal(candidate.project.nodes.length,before.project.nodes.length);
for(const n of candidate.project.nodes){const b=nodes.get(n.id);assert(b);assert.deepEqual({...n,title:b.title},b,'Non-title field changed: '+n.id);if(!nodeIds.has(n.id))assert.deepEqual(n,b);}
assert.deepEqual({...candidate.project,nodes:before.project.nodes},before.project);
assert.deepEqual({...candidate,project:before.project},before);
const hash=x=>createHash('sha256').update(JSON.stringify(x)).digest('hex');
const receipt={preparedAt:new Date().toISOString(),expectedRevision:before.revision,baselineHash:hash(before),sourceHash:hash(candidate),baselineProjectHash:hash(before.project),candidateProjectHash:hash(candidate.project),scope:'Existing Mythology portal title strings only',changes,changedTitles:changes.length,noNodesAddedOrRemoved:true,allIdsDestinationsGeometryAndOtherFieldsUnchanged:true,codingAndTutorialUnchanged:true,discoveryAndTombstonesUnchanged:true,savedLive:false,saveInstructions:[
 'GET /api/library and require exact baselineHash plus revision480; any difference requires a fresh title-only rebase and re-review.',
 'GET /api/library/recovery and require absent/404. Do not consume or discard recovery.',
 'Create a named original-Library checkpoint using existing endpoint contract.',
 'Immediately repeat the saved Library and recovery reads, since the Library endpoint has no atomic no-recovery guard. Refuse any changed baseline or new recovery.',
 'PUT the reviewed Library candidate using its expected revision. Treat a conflict as refusal; do not retry against a newer baseline automatically.',
 'GET saved Library and verify the expected title-only change, unchanged destinations/discovery/geometry/otherfields, save receipt/checkpoint, and fresh no-recovery state.',
 'Retain native windows. A Library title change does not require restarting Meridian.'
 ]};
const plan=[{workId:before.project.entryCanvasId,label:'Library default overview'},{workId:before.project.entryCanvasId,nodeId:myth,scope:'branch',label:'Mythology covers'},{workId:before.project.entryCanvasId,nodeId:'842eae57-c870-4a25-9ec1-568fc3c6e420',scope:'branch',label:'Visual Atlas titles'},{workId:before.project.entryCanvasId,nodeId:'42dc8ec1-c709-4543-ba51-706fb937d212',scope:'branch',label:'Gods and Epics titles'}];
await mkdir(out,{recursive:true});
for(const [name,value] of [['library-candidate.json',candidate],['library-project.json',candidate.project],['recipe.json',recipe],['aliases.json',result.aliases],['inspection.json',inspectProject(candidate.project)],['preservation.json',receipt],['review-plan.json',plan]])await writeFile(out+'/'+name,JSON.stringify(value,null,2));
console.log(JSON.stringify({out,changedTitles:changes.length,baselineHash:receipt.baselineHash,sourceHash:receipt.sourceHash}));
