import{readFile,writeFile}from'node:fs/promises';
import{createHash}from'node:crypto';
import assert from'node:assert/strict';
import{applyRecipe}from'../../src/core/authoring.ts';
import{validateProject}from'../../src/core/rules.ts';
import{geometryDiagnostics}from'../../integrations/mcp/src/authoring-worker.mjs';
const out='I:/dev/Meridian/output/mythology-current-rework-20261023/gods-entry-polish';
const b=JSON.parse(await readFile(out+'/baseline.json','utf8'));
assert.equal(b.revision,59);
const reading='6dff51ba-32de-41bd-8ae0-48ccb93360e4',byId=new Map(b.nodes.map(n=>[n.id,n]));
const moved=new Set([reading]);for(let changed=true;changed;){changed=false;for(const n of b.nodes)if(moved.has(n.parentId)&&!moved.has(n.id)){moved.add(n.id);changed=true;}}
const recipe={version:1,nodes:b.nodes.filter(n=>moved.has(n.id)).map((n,i)=>({key:'reading-'+i,existingId:n.id,y:n.y-840})),links:[]};
for(const l of b.links)if(l.points?.length&&moved.has(l.sourceId)&&moved.has(l.targetId))recipe.links.push({key:'reading-route-'+recipe.links.length,existingId:l.id,points:l.points.map(p=>({x:p.x,y:p.y-840}))});
const result=applyRecipe(b,recipe),p=result.project,hash=x=>createHash('sha256').update(JSON.stringify(x)).digest('hex');
assert.deepEqual(validateProject(p),[]);
const pbyId=new Map(p.nodes.map(n=>[n.id,n]));
for(const n of b.nodes){const a=pbyId.get(n.id);assert(a);assert.deepEqual({...a,y:moved.has(n.id)?a.y+840:a.y},n);}
assert.deepEqual({...p,nodes:b.nodes,links:b.links},b);
for(const w of b.canvases.slice(1)){assert.deepEqual(p.nodes.filter(n=>n.canvasId===w.id),b.nodes.filter(n=>n.canvasId===w.id));assert.deepEqual(p.links.filter(l=>l.canvasId===w.id),b.links.filter(l=>l.canvasId===w.id));}
const geometry=geometryDiagnostics(p,p.entryCanvasId);assert.equal(geometry.diagnostics.length,0,JSON.stringify(geometry.diagnostics));
const expanded=structuredClone(p),parents=new Set(p.nodes.map(n=>n.parentId));
for(const n of expanded.nodes)if(n.canvasId===p.entryCanvasId&&parents.has(n.id)&&n.kind!=='canvas'&&n.kind!=='document'){if(n.collapsed&&n.containerSizes?.expanded)Object.assign(n,n.containerSizes.expanded);n.collapsed=false;}
const eg=geometryDiagnostics(expanded,p.entryCanvasId);assert.equal(eg.diagnostics.length,0,JSON.stringify(eg.diagnostics));
const proof={baselineRevision:59,projectId:b.id,baselineHash:hash(b),sourceHash:hash(p),changedNodeIds:[...moved],movedNodes:moved.size,translation:{x:0,y:-840},allOther47WorksUnchanged:true,allIdsAssetsReferencesDestinationsAndOtherFieldsPreserved:true,structuralErrors:0,collapsedGeometry:geometry,expandedGeometry:eg,savedLive:false};
const plan=[{workId:p.entryCanvasId,label:'Greek Mythology entry default Fit'},{workId:p.entryCanvasId,expand:true,label:'Greek Mythology entry all branches expanded'}];
for(const[name,value]of[['draft.json',p],['recipe.json',recipe],['aliases.json',result.aliases],['preservation.json',proof],['review-plan.json',plan]])await writeFile(out+'/'+name,JSON.stringify(value,null,2));
console.log(JSON.stringify({out,movedNodes:moved.size,geometry:geometry.diagnostics.length,expandedGeometry:eg.diagnostics.length,sourceHash:proof.sourceHash}));
