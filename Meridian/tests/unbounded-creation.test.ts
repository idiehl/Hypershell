import { test } from 'node:test';
import assert from 'node:assert/strict';
import { addNode, newProject } from '../src/core/model.ts';
import { createNodeFromSpec, CREATION_TYPES, type NodeCreationSpec } from '../src/core/creation.ts';
import { updateNodeType } from '../src/core/node-editing.ts';
import { activeNodeContent } from '../src/core/content.ts';
import { isEnclosingContainer } from '../src/core/cells.ts';
import { navigationIndex } from '../src/core/navigation.ts';

function fixture(){const project=newProject('Unified node contents');return {project,canvasId:project.entryCanvasId,parentId:project.canvases[0]!.rootId};}
function spec(f:ReturnType<typeof fixture>,patch:Partial<NodeCreationSpec>={}):NodeCreationSpec{return {canvasId:f.canvasId,parentId:f.parentId,x:100,y:100,width:240,height:160,type:'media',title:'A node',contentType:'text',text:'One passage',...patch};}

test('creation choices expose one Media type while legacy imports remain readable',()=>{
  assert.equal(CREATION_TYPES.filter(type=>type.label==='Media').length,1);
  assert.equal(CREATION_TYPES.some(type=>['text','document'].includes(type.value)),false);
  const f=fixture(),created=createNodeFromSpec(f.project,spec(f));
  const node=created.project.nodes.find(node=>node.id===created.nodeId)!;
  assert.equal(node.kind,'media');assert.equal(node.childLayout,'spatial');assert.equal(node.containerVariant,'contained');assert.equal(node.childArrangement,'radial');
  assert.deepEqual(activeNodeContent(node),{type:'text',text:'One passage'});
});

test('one selected content item is enforced atomically and files are available to every type',()=>{
  const f=fixture(),asset={id:crypto.randomUUID(),name:'notes.pdf',mime:'application/pdf',base64:'SGVsbG8='},before=structuredClone(f.project);
  assert.throws(()=>createNodeFromSpec(f.project,spec(f,{contentType:'asset',asset})),/one content item/);
  assert.deepEqual(f.project,before);
  for(const type of ['media','container','number'] as const){
    const result=createNodeFromSpec(f.project,spec(f,{type,contentType:'asset',text:undefined,asset})),node=result.project.nodes.find(node=>node.id===result.nodeId)!;
    assert.equal(node.assetId,asset.id);assert.equal(node.contentType,'asset');assert.ok(!node.text);
    assert.equal(result.project.assets.find(item=>item.id===asset.id)!.base64,asset.base64);
  }
  const linked=createNodeFromSpec(f.project,spec(f,{text:undefined,contentType:'link',hyperlink:{url:'https://example.com',label:'An original source'}}));
  assert.deepEqual(activeNodeContent(linked.project.nodes.find(node=>node.id===linked.nodeId)!).hyperlink,{url:'https://example.com',label:'An original source'});
});

test('API creation infers asset or hyperlink content without discarding older caption fields',()=>{
  const f=fixture(),asset={id:crypto.randomUUID(),name:'original.txt',mime:'text/plain',base64:'SGVsbG8='};
  const file=createNodeFromSpec(f.project,spec(f,{contentType:undefined,asset,text:'Retained older API caption'})),node=file.project.nodes.find(node=>node.id===file.nodeId)!;
  assert.equal(node.contentType,'asset');assert.equal(activeNodeContent(node).assetId,asset.id);assert.equal(node.text,'Retained older API caption');
  const linked=createNodeFromSpec(f.project,spec(f,{contentType:undefined,text:undefined,hyperlink:{url:'https://example.com'}}));
  assert.equal(activeNodeContent(linked.project.nodes.find(node=>node.id===linked.nodeId)!).type,'link');
});

test('bounded content starts compressed and an unbounded ordered parent stays the same node type',()=>{
  const f=fixture(),bounded=createNodeFromSpec(f.project,spec(f,{type:'container'}));
  assert.equal(bounded.project.nodes.find(node=>node.id===bounded.nodeId)!.collapsed,true);
  const created=createNodeFromSpec(f.project,spec(f,{variant:'vertical'})),parent=created.project.nodes.find(node=>node.id===created.nodeId)!;
  const child=createNodeFromSpec(created.project,spec(f,{parentId:parent.id,title:'A child',text:'A smaller passage'}));
  const current=child.project.nodes.find(node=>node.id===parent.id)!;
  assert.equal(current.kind,'media');assert.equal(isEnclosingContainer(current),false);
  const bounds=navigationIndex(child.project).bounds(current),childBounds=navigationIndex(child.project).bounds(child.project.nodes.find(node=>node.id===child.nodeId)!);
  assert.ok(childBounds.y>=bounds.y+bounds.height);
  assert.equal(child.project.nodes.find(node=>node.id===child.nodeId)!.childPosition,'auto');
});

test('type and display-pattern edits preserve legacy mixed contents without forcing Media into Container',()=>{
  const f=fixture(),node=addNode(f.project,f.canvasId,'document','Old mixed document');node.text='Retained caption';node.assetId=crypto.randomUUID();node.hyperlinks=[{url:'https://example.com'}];
  f.project.assets.push({id:node.assetId,name:'source.txt',mime:'text/plain',base64:'SGVsbG8='});
  const converted=updateNodeType(f.project,node.id,{kind:'media',containerVariant:'grid'}),current=converted.nodes.find(item=>item.id===node.id)!;
  assert.equal(current.kind,'media');assert.equal(current.childLayout,'spatial');assert.equal(current.containerVariant,'grid');
  assert.equal(current.text,node.text);assert.equal(current.assetId,node.assetId);assert.deepEqual(current.hyperlinks,node.hyperlinks);
  assert.equal(activeNodeContent(current).type,'asset');
  const free=updateNodeType(converted,node.id,{kind:'media',containerVariant:'contained'}).nodes.find(item=>item.id===node.id)!;
  assert.ok(['free','radial'].includes(free.childArrangement!));assert.equal(free.kind,'media');
});
