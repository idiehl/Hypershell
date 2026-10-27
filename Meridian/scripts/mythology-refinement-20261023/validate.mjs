import{readFile,writeFile,mkdir}from'node:fs/promises';
import path from'node:path';
import{createHash}from'node:crypto';
import{validateProject}from'../../src/core/rules.ts';
import{geometryDiagnostics}from'../../integrations/mcp/src/authoring-worker.mjs';
const args=new Map();for(let i=2;i<process.argv.length;i+=2)args.set(process.argv[i],process.argv[i+1]);
if(!args.has('--input'))throw new Error('Use --input draft.json --out report.json [--baseline project.json].');
const p=JSON.parse(await readFile(path.resolve(args.get('--input')),'utf8'));
const structural=validateProject(p),works=[];
for(const w of p.canvases){const geometry=geometryDiagnostics(p,w.id);works.push({id:w.id,title:w.title,...geometry});console.log(JSON.stringify({title:w.title,checked:geometry.checkedNodes,errors:geometry.diagnostics.length,capped:geometry.diagnosticLimitReached}));}
let preservation;
if(args.has('--baseline')){
 const b=JSON.parse(await readFile(path.resolve(args.get('--baseline')),'utf8')),nodes=new Map(p.nodes.map(n=>[n.id,n])),assets=new Map(p.assets.map(a=>[a.id,a])),links=new Map(p.links.map(l=>[l.id,l]));
 const archivalText=p.assets.filter(a=>!b.assets.some(x=>x.id===a.id)&&(/^(text\/|application\/json)/.test(a.mime))).map(a=>Buffer.from(a.base64,'base64').toString('utf8')).join('\n');
 const missingNodes=b.nodes.filter(n=>!nodes.has(n.id)).map(n=>n.id),missingLinks=b.links.filter(l=>!links.has(l.id)).map(l=>l.id);
 const alteredAssets=b.assets.filter(a=>JSON.stringify(a)!==JSON.stringify(assets.get(a.id))).map(a=>a.id);
 const lostBodies=b.nodes.filter(n=>n.text&&nodes.has(n.id)&&nodes.get(n.id).text!==n.text&&!archivalText.includes(n.text)).map(n=>({id:n.id,title:n.title}));
 const changedFunctional=[],movedNodes=[];
 for(const n of b.nodes){const after=nodes.get(n.id);if(!after)continue;if(n.canvasId!==after.canvasId)movedNodes.push({id:n.id,from:n.canvasId,to:after.canvasId});for(const field of['kind','value','valueType','operation','closed','exposed','dependencyIds','remoteLinks','targetId','targetCell','canvasTarget','attachments','hyperlinks'])if(JSON.stringify(n[field])!==JSON.stringify(after[field]))changedFunctional.push({id:n.id,title:n.title,field});}
 preservation={projectIdUnchanged:p.id===b.id,entryWorkUnchanged:p.entryCanvasId===b.entryCanvasId,workIdsPreserved:b.canvases.every(w=>p.canvases.some(c=>c.id===w.id&&c.rootId===w.rootId)),missingNodes,missingLinks,alteredAssets,lostBodies,changedFunctional,movedNodes,baselineRevision:b.revision,newNodes:p.nodes.length-b.nodes.length,newLinks:p.links.length-b.links.length,newAssets:p.assets.length-b.assets.length};
}
const report={recordedAt:new Date().toISOString(),sourceHash:createHash('sha256').update(JSON.stringify(p)).digest('hex'),structural,works,preservation};
const file=path.resolve(args.get('--out')??'output/mythology-refinement-20261023/validation.json');await mkdir(path.dirname(file),{recursive:true});await writeFile(file,JSON.stringify(report,null,2));
console.log(JSON.stringify({report:file,structuralErrors:structural.length,geometryErrors:works.reduce((sum,w)=>sum+w.diagnostics.length,0),preservation:preservation&&Object.fromEntries(Object.entries(preservation).map(([key,value])=>[key,Array.isArray(value)?value.length:value]))}));
