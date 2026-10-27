/** Shared offline authoring/review utilities. No host access or live writes. */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { applyRecipe, inspectProject } from '../../src/core/authoring.ts';
import { commitSnapshots } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';
import { buildPublication } from '../../src/publication.ts';

export const ROOT=fileURLToPath(new URL('../../',import.meta.url));
export const OUT=path.join(ROOT,'output/mythology-renewal-20261018');
export const projectIds={gods:'3c8176d0-884a-4ac9-9a99-a0d5548de629',living:'3d896c72-36b9-4900-adb6-a68934591233',visual:'e35cba82-f644-416a-ac14-12645893ed88'};
export const readJSON=async file=>JSON.parse((await readFile(file,'utf8')).replace(/^\uFEFF/,''));
export async function writeJSON(file,value){await mkdir(path.dirname(file),{recursive:true});await writeFile(file,JSON.stringify(value,null,2)+'\n');}
export const baseline=id=>readJSON(path.join(OUT,'baseline',`${id}.json`));
let media;
export async function loadMedia(){return media??=await readJSON(path.join(OUT,'media/manifest.json'));}
export async function mediaAsset(objectId){
 const item=(await loadMedia()).items.find(item=>item.objectId===objectId);assert.ok(item,`Unknown museum object ${objectId}`);
 return {key:item.key,name:`${item.title} · Met ${item.accession}`,mime:item.mime,base64:(await readFile(path.join(OUT,'media',item.file))).toString('base64')};
}
export async function artCredit(objectId){
 const item=(await loadMedia()).items.find(item=>item.objectId===objectId);assert.ok(item);
 return `${item.caption}\n\n${item.title}\n${[item.culture,item.date].filter(Boolean).join(' · ')}\n${item.artist?[item.artistPrefix,item.artist].filter(Boolean).join(' ')+ '\n':''}The Metropolitan Museum of Art · ${item.accession}\n${item.credit}\n${item.rights}\n${item.sourceUrl}`;
}
export function originalBindings(base){
 return {canvases:Object.fromEntries(base.canvases.map(c=>[`canvas/${c.id}`,c.id])),roots:Object.fromEntries(base.canvases.map(c=>[`canvas/${c.id}`,c.rootId])),nodes:Object.fromEntries(base.nodes.filter(n=>n.kind!=='canvas').map(n=>[`node/${n.id}`,n.id])),assets:Object.fromEntries(base.assets.map(a=>[`asset/${a.id}`,a.id])),links:Object.fromEntries(base.links.map(l=>[`link/${l.id}`,l.id]))};
}

/** Verify the renewal changes presentation, not the attributed source collection. */
export function auditPreservation(base,candidate){
 assert.equal(candidate.id,base.id,'Project identity changed');assert.equal(candidate.entryCanvasId,base.entryCanvasId,'Entry canvas changed');
 const nodes=new Map(candidate.nodes.map(n=>[n.id,n])),canvases=new Map(candidate.canvases.map(c=>[c.id,c])),links=new Map(candidate.links.map(l=>[l.id,l])),assets=new Map(candidate.assets.map(a=>[a.id,a]));
 for(const canvas of base.canvases){assert.ok(canvases.has(canvas.id),`Lost canvas ${canvas.title}`);assert.equal(canvases.get(canvas.id).rootId,canvas.rootId,'Canvas root changed');assert.equal(canvases.get(canvas.id).title,canvas.title,'Existing canvas title changed');}
 const exact=['kind','canvasId','title','text','titleRuns','textRuns','assetId','attachments','hyperlinks','remoteLinks','targetId','targetCell','canvasTarget','dependencyIds','operation','value','valueType','closed','exposed','rules','retiredBlockContent'];
 for(const n of base.nodes){const current=nodes.get(n.id);assert.ok(current,`Lost node ${n.title} (${n.id})`);for(const field of exact)assert.deepEqual(current[field],n[field],`Source field changed: ${n.title}.${field}`);if(n.tag!==undefined)assert.equal(current.tag,n.tag,`Tag changed: ${n.tag}`);if(n.cell!==undefined)assert.deepEqual(current.cell,n.cell,`Address changed: ${n.title}`);}
 for(const asset of base.assets)assert.deepEqual(assets.get(asset.id),asset,`Attachment changed or lost: ${asset.name}`);
 const routeReaches=(source,target,terminalId)=>{
  const visited=new Set(),queue=[source];while(queue.length){const id=queue.shift();if(id===target)return true;if(visited.has(id))continue;visited.add(id);for(const edge of candidate.links)if(edge.sourceId===id&&edge.id!==terminalId){const next=nodes.get(edge.targetId);if(next&&(next.id===target||next.kind==='junction'))queue.push(next.id);}}
  return false;
 };
 for(const link of base.links){const current=links.get(link.id);assert.ok(current,`Lost original connection ${link.id}`);for(const field of ['canvasId','kind','relation','direction','label','targetId','targetPort','snap'])assert.deepEqual(current[field],link[field],`Relationship changed: ${link.id}.${field}`);if(current.sourceId!==link.sourceId)assert.ok(nodes.get(current.sourceId)?.kind==='junction'&&routeReaches(link.sourceId,current.sourceId,current.id),`Original source lost behind route: ${link.id}`);}
 assert.deepEqual(validateProject(candidate),[],'Renewed graph is invalid');
 const bytes=Buffer.byteLength(JSON.stringify(candidate));assert.ok(bytes<100*1024*1024,`Candidate exceeds host limit (${bytes} bytes)`);
 return {passed:true,originalProjectId:base.id,originalRevision:base.revision,retainedCanvases:base.canvases.length,retainedNodes:base.nodes.length,retainedLinks:base.links.length,retainedAssets:base.assets.length,sourceFieldsUnchanged:exact,serializedBytes:bytes,newNodes:candidate.nodes.length-base.nodes.length,newLinks:candidate.links.length-base.links.length,newAssets:candidate.assets.length-base.assets.length,sha256:createHash('sha256').update(JSON.stringify(candidate)).digest('hex')};
}
export async function stageRecipe(base,recipe,folder){
 const directory=path.isAbsolute(folder)?folder:path.join(OUT,folder);await mkdir(directory,{recursive:true});
 const result=applyRecipe(base,recipe),project=commitSnapshots(result.project),audit=auditPreservation(base,project);
 await writeJSON(path.join(directory,'recipe.json'),recipe);await writeJSON(path.join(directory,'draft.json'),project);await writeJSON(path.join(directory,'aliases.json'),result.aliases);await writeJSON(path.join(directory,'inspection.json'),inspectProject(project));await writeJSON(path.join(directory,'preservation.json'),audit);
 const [js,css]=await Promise.all(['viewer.js','scene.css'].map(file=>readFile(path.join(ROOT,'dist',file),'utf8')));await writeFile(path.join(directory,'publication.html'),buildPublication(project,js,css));await writeFile(path.join(directory,'renewed.meridian'),JSON.stringify(project));
 return {...result,project,audit,directory};
}
