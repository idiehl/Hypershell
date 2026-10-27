import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {evaluate} from '../../src/core/engine.ts';
import {commitSnapshots} from '../../src/core/model.ts';
import {validateProject,validateChange,reparentNode} from '../../src/core/rules.ts';
import {readPublication,createPublicationPayload} from '../../src/publication.ts';

const folder=new URL('../../examples/greek-mythology/',import.meta.url);
const p=JSON.parse(await readFile(new URL('greek-mythology.project.json',folder),'utf8'));
const manifest=JSON.parse(await readFile(new URL('collection-manifest.json',folder),'utf8'));
const html=await readFile(new URL('greek-mythology.html',folder),'utf8');
const hash=b=>createHash('sha256').update(b).digest('hex');
const node=(project,key)=>project.nodes.find(n=>n.id===manifest.nodeKeys[key]);
const clone=()=>structuredClone(p);
const checks=[];
function check(name,action){action();checks.push(name);}
check('All authored nodes, links, containment rules and asset references validate',()=>assert.deepEqual(validateProject(p),[]));
const epics=JSON.parse(await readFile(new URL('../../content/greek-mythology/epics.json',import.meta.url),'utf8'));
for(const epic of epics.canvases)assert.deepEqual(epic.sections.flatMap(s=>s.books.map(b=>b.book)),Array.from({length:24},(_,i)=>i+1));
checks.push('All 48 epic books occur exactly once in source content');
check('Authored circuit and all saved computational snapshots agree',()=>{
 const e=evaluate(p);assert.equal(e.values[node(p,'voyage-output').id],true);
 for(const n of p.nodes.filter(n=>['input','output','joint','switch','reference'].includes(n.kind)))assert.equal(p.snapshots[n.id].value,e.values[n.id],n.title);
 assert.equal(e.values[node(p,'missing-input-output').id],null);
});
check('Every Boolean gate passes every input combination in the actual collection',()=>{
 const expected={AND:(a,b)=>a&&b,OR:(a,b)=>a||b,NOT:a=>!a,NAND:(a,b)=>!(a&&b),NOR:(a,b)=>!(a||b),XOR:(a,b)=>a!==b,XNOR:(a,b)=>a===b};
 for(const [op,fn] of Object.entries(expected))for(const a of [false,true])for(const b of op==='NOT'?[false]:[false,true]){const q=clone();node(q,`truth-${op}-a`).value=a;if(op!=='NOT')node(q,`truth-${op}-b`).value=b;assert.equal(evaluate(q).values[node(q,`truth-${op}-output`).id],fn(a,b),`${op} ${a} ${b}`);}
});
check('Open switches clear dependents and reopening restores a value',()=>{const q=clone();node(q,'voyage-switch').closed=false;assert.equal(evaluate(q).values[node(q,'voyage-output').id],null);node(q,'voyage-switch').closed=true;assert.equal(evaluate(q).values[node(q,'voyage-output').id],true);});
check('Same-canvas references are live; cross-canvas references ignore unsaved edits',()=>{let q=clone();node(q,'crew-ready').value=false;assert.equal(evaluate(q).values[node(q,'crew-live').id],false);q=clone();node(q,'archive-reviewed').value=false;assert.equal(evaluate(q).values[node(q,'voyage-output').id],true);q=commitSnapshots(q);assert.equal(evaluate(q).values[node(q,'voyage-output').id],false);});
check('Collapsing the preparation container does not change sibling output computation',()=>{const q=clone();node(q,'voyage-system').collapsed=true;assert.equal(evaluate(q).values[node(q,'voyage-output').id],true);assert.notEqual(node(q,'voyage-output').parentId,node(q,'voyage-system').id);});
check('Conflicting inherited rules and forbidden reparenting are rejected',()=>{let q=clone();node(q,'voyage-system').rules.operations=['OR'];assert.ok(validateChange(p,q).length);q=clone();assert.throws(()=>reparentNode(q,node(q,'lab-instructions').id,node(q,'voyage-system').id));});
check('Computational feedback is rejected without forbidding visual cycles',()=>{const q=clone(),gate=node(q,'voyage-and');q.links=q.links.filter(l=>!(l.targetId===gate.id&&l.targetPort===0));q.links.push({id:crypto.randomUUID(),canvasId:gate.canvasId,sourceId:node(q,'voyage-output').id,targetId:gate.id,kind:'functional',relation:'causal',targetPort:0});assert.ok(validateProject(q).some(i=>/feedback/i.test(i.message)));assert.ok(p.links.some(l=>l.kind==='visual'&&l.relation==='cyclical'));});
check('Publications include selected external snapshots and name excluded requirements',()=>{assert.deepEqual(createPublicationPayload(p).missing,[]);const missing=createPublicationPayload(p,{includeExternalReferences:false}).missing;assert.ok(missing.some(x=>x.includes('Curator')||x.includes('curator')));});
const imported=readPublication(html);
check('Publication import refreshes all identities and remaps every internal relationship',()=>{
 assert.notEqual(imported.id,p.id);assert.equal(imported.nodes.length,p.nodes.length);assert.equal(imported.canvases.length,p.canvases.length);assert.equal(imported.links.length,p.links.length);
 const ids=new Map(p.nodes.map((n,i)=>[n.id,imported.nodes[i].id]));const cids=new Map(p.canvases.map((c,i)=>[c.id,imported.canvases[i].id]));
 for(let i=0;i<p.nodes.length;i++){const a=p.nodes[i],b=imported.nodes[i];assert.notEqual(a.id,b.id);assert.equal(b.canvasId,cids.get(a.canvasId));assert.equal(b.parentId,a.parentId?ids.get(a.parentId):null);if(a.targetId&&ids.has(a.targetId))assert.equal(b.targetId,ids.get(a.targetId));}
 for(let i=0;i<p.links.length;i++){const a=p.links[i],b=imported.links[i];assert.notEqual(a.id,b.id);assert.equal(b.sourceId,ids.get(a.sourceId));assert.equal(b.targetId,ids.get(a.targetId));}
 for(const a of p.assets)assert.equal(imported.assets.find(x=>x.name===a.name).base64,a.base64);
 assert.deepEqual(validateProject(imported),[]);
});
const media=JSON.parse(await readFile(new URL('../../content/greek-mythology/media.json',import.meta.url),'utf8'));
for(const a of p.assets){const original=await readFile(new URL(`../../content/greek-mythology/media/${a.name}`,import.meta.url));assert.equal(hash(original),hash(Buffer.from(a.base64,'base64')),a.name);const record=media.find(m=>m.file===a.name);if(record)assert.equal(hash(original),record.sha256);}
checks.push(`All ${p.assets.length} embedded assets match their source bytes and six museum provenance hashes`);
const host=process.env.MERIDIAN_URL??'http://127.0.0.1:5240';
const before=await (await fetch(host+'/api/projects')).text();
const exported=await fetch(host+'/api/export',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(p)});
assert.ok(exported.ok,await exported.clone().text());const bytes=Buffer.from(await exported.arrayBuffer());
await writeFile(new URL('greek-mythology.meridian',folder),bytes);
const form=new FormData();form.append('file',new Blob([bytes]),'greek-mythology.meridian');
const reopened=await fetch(host+'/api/import',{method:'POST',body:form});assert.ok(reopened.ok,await reopened.clone().text());assert.deepEqual(await reopened.json(),p);
assert.equal(await (await fetch(host+'/api/projects')).text(),before);
checks.push('Real host ZIP export/import preserves every GUID, canvas, node, link, snapshot and asset byte');
checks.push('Package verification leaves the live project library unchanged');
const report={verifiedAt:new Date().toISOString(),projectId:p.id,canvases:p.canvases.length,nodes:p.nodes.length,links:p.links.length,assets:p.assets.length,packageBytes:bytes.length,packageSha256:hash(bytes),checks};
await writeFile(new URL('../../output/mythology-qa/collection-verification.json',import.meta.url),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
