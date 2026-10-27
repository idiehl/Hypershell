import {readFile,writeFile} from 'node:fs/promises';
import {applyRecipe} from '../../src/core/authoring.ts';
import {commitSnapshots} from '../../src/core/model.ts';
const dir='I:/dev/Meridian/output/mythology-refinement-20261023/living';
const p=JSON.parse(await readFile(`${dir}/draft.json`,'utf8'));
const aliases=JSON.parse(await readFile(`${dir}/aliases.json`,'utf8'));
const originalRecipe=JSON.parse(await readFile(`${dir}/recipe.json`,'utf8'));
const ids=['3fc89062-7273-4c8c-8b6a-0397c31b6803','34817749-bf21-487d-bde0-f645b78ae723','8938d5fe-24e7-43f2-870a-32c35f645528','e8663b9a-6951-4dc2-b18e-ed54902d83b4'];
const by=new Map(p.nodes.map(n=>[n.id,n])),children=new Map();for(const n of p.nodes){const arr=children.get(n.parentId)||[];arr.push(n);children.set(n.parentId,arr)}
const specs=[];const set=(id,fields)=>{specs.push({key:`n/${id}`,...fields});const spec=originalRecipe.nodes.find(n=>n.key===`n/${id}`);Object.assign(spec,fields)};
for(const id of ids){const root=by.get(id);if(root.expansionTray){set(id,{expansionTray:{...root.expansionTray,offset:{...root.expansionTray.offset,x:root.expansionTray.offset.x+650}}});const visit=id=>{for(const n of children.get(id)||[]){set(n.id,{x:n.x+650});visit(n.id)}};visit(id)}}
const result=applyRecipe(p,{version:1,bindings:aliases,nodes:specs});const out=commitSnapshots(result.project);
await writeFile(`${dir}/draft.json`,JSON.stringify(out,null,2));await writeFile(`${dir}/recipe.json`,JSON.stringify(originalRecipe));
const preservation=JSON.parse(await readFile(`${dir}/preservation.json`,'utf8'));preservation.serializedBytes=Buffer.byteLength(JSON.stringify(out));preservation.primordialCoverClearanceFixed=true;await writeFile(`${dir}/preservation.json`,JSON.stringify(preservation,null,2));
console.log(JSON.stringify({moved:specs.length,bytes:preservation.serializedBytes}));
