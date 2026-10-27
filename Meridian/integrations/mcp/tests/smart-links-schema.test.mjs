import test from 'node:test';
import assert from 'node:assert/strict';
import { executeWorker } from '../src/authoring-worker.mjs';
import { newProject } from '../../../src/core/model.ts';

test('MCP schema and declarative apply expose portable visual Smart Links and resolve ordinary parent aliases',async()=>{
 const schema=await executeWorker({command:'schema'});assert(schema.nodeFields.includes('smartLink'));assert.deepEqual(schema.smartLinks.gapRange,[8,10000]);
 const base=newProject('Smart Link authoring');const result=await executeWorker({command:'apply',project:base,recipe:{version:1,nodes:[
  {key:'rail',canvas:base.entryCanvasId,kind:'junction',utilityVariant:'joint',smartLink:{direction:'down',gap:32},x:100,y:100,width:12,height:12},
  {key:'subject',canvas:base.entryCanvasId,parent:'rail',kind:'media',title:'Subject',showTitle:false,contentItems:[{id:'subject',type:'text',text:'Subject'}]},
 ]}});const rail=result.project.nodes.find(n=>n.id===result.aliases.nodes.rail),subject=result.project.nodes.find(n=>n.id===result.aliases.nodes.subject);assert.equal(subject.parentId,rail.id);assert.equal(rail.smartLink.gap,32);assert(subject.x>rail.x);assert.equal(base.nodes.length,1);
 await assert.rejects(()=>executeWorker({command:'apply',project:base,recipe:{version:1,nodes:[{key:'bad',canvas:base.entryCanvasId,kind:'junction',smartLink:{direction:'down',gap:0}}]}}),/Smart Links/);
});
