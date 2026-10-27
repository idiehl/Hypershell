import test from 'node:test';
import assert from 'node:assert/strict';
import { performance } from 'node:perf_hooks';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { addSmartLinkMember, configureSmartLink, createSmartLink, detachSmartLinkMember, reflowSmartLinks, smartLinkGeometries, smartLinkGeometry, smartLinkMembers } from '../src/core/smart-links.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { createPublicationPayload } from '../src/publication.ts';
import { applyRecipe } from '../src/core/authoring.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { createModuleFromSelection, instantiateModule } from '../src/core/modules.ts';
import { createSnippet } from '../src/core/selection.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { gridGeometry } from '../src/core/cells.ts';

function member(p:MeridianProject,parent:MeridianNode,title:string,x=500,y=500,width=100,height=40){const n=addNode(p,parent.canvasId,'media',title);Object.assign(n,{parentId:parent.id,x,y,width,height,sizeMode:'fixed',childPosition:'manual',style:{shape:'rectangle',padding:0,contentSizing:'fixed',fontSize:12,titleSize:13}});return n;}
function box(n:MeridianNode){return{x:n.x,y:n.y,width:n.width,height:n.height};}
const overlap=(a:ReturnType<typeof box>,b:ReturnType<typeof box>)=>a.x<b.x+b.width&&b.x<a.x+a.width&&a.y<b.y+b.height&&b.y<a.y+a.height;

test('Smart Link children pack at a regular gap and a second reflow is stable',()=>{
 const p=newProject('Regular'),r=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:32});
 const list=[member(p,r,'A'),member(p,r,'B'),member(p,r,'C')];const result=reflowSmartLinks(p);
 assert.equal(result.movedNodeIds.length,3);assert.equal(list[0].x,132);assert.equal(list[1].x,list[0].x);assert.equal(list[1].y-list[0].y,72);assert.equal(list[2].y-list[1].y,72);
 const first=structuredClone(p);assert.deepEqual(reflowSmartLinks(p).movedNodeIds,[]);assert.deepEqual(p,first);assert.deepEqual(validateProject(p),[]);
 assert.equal((list[0].y+list[2].y+list[2].height)/2,100);
 const geometry=smartLinkGeometry(p,r.id)!;assert.equal(geometry.direction,'down');assert.equal(geometry.segments.length,5);assert(geometry.start.y<geometry.origin.y&&geometry.end.y>geometry.origin.y);assert.deepEqual(geometry.memberIds,list.map(n=>n.id));assert.deepEqual(smartLinkGeometries(p).get(r.id),geometry);
});

test('only currently expanded branch extents reserve space, while hidden descendants travel with their subject',()=>{
 const p=newProject('Disclosure'),r=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:24});
 const a=member(p,r,'A',500,500),b=member(p,r,'B'),c=member(p,a,'A child',680,860,100,100);a.collapsed=true;
 reflowSmartLinks(p);const compactGap=b.y-a.y,relative={x:c.x-a.x,y:c.y-a.y};assert.equal(compactGap,64);
 a.collapsed=false;reflowSmartLinks(p);assert(b.y>=c.y+c.height+24);assert(b.y-a.y>compactGap);assert.deepEqual({x:c.x-a.x,y:c.y-a.y},relative);
 a.collapsed=true;reflowSmartLinks(p);assert.equal(b.y-a.y,compactGap);assert.deepEqual({x:c.x-a.x,y:c.y-a.y},relative);
});

test('nested Smart Links plan bottom-up, preserve relative descendants, and compact after deep disclosure closes',()=>{
 const p=newProject('Nested'),outer=createSmartLink(p,p.entryCanvasId,{x:10,y:10},{direction:'down',gap:30});
 const inner=createSmartLink(p,p.entryCanvasId,{x:400,y:400},{parentId:outer.id,direction:'right',gap:20}),a=member(p,inner,'A'),b=member(p,inner,'B'),peer=member(p,outer,'Peer');
 const child=member(p,a,'Deep',850,1100,120,300);a.collapsed=false;reflowSmartLinks(p);
 assert.equal(a.y,b.y);assert(b.x>=a.x+a.width+20);assert(peer.y>child.y+child.height);const expanded=peer.y;
 a.collapsed=true;reflowSmartLinks(p);assert(peer.y<expanded);const state=structuredClone(p);assert.deepEqual(reflowSmartLinks(p).movedNodeIds,[]);assert.deepEqual(p,state);
});

test('a rail avoids fixed content and other opted-in rails without moving unrelated nodes or Works',()=>{
 const p=newProject('Aware'),r=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:32});
 const a=member(p,r,'A'),b=member(p,r,'B'),fixed=member(p,p.nodes[0],'Fixed',132,132,180,140);
 const other=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:32}),c=member(p,other,'C');
 const foreign=addCanvas(p,'Untouched'),foreignNode=addNode(p,foreign.id,'media','Foreign'),fixedBefore=structuredClone(fixed),foreignBefore=structuredClone(foreignNode);
 const out=reflowSmartLinks(p,p.entryCanvasId);assert(out.obstacleChecks>0);for(const n of[a,b,c])assert(!overlap(n,fixed));assert(!overlap(a,c));assert(!overlap(b,c));assert.deepEqual(fixed,fixedBefore);assert.deepEqual(foreignNode,foreignBefore);
 const state=structuredClone(p);reflowSmartLinks(p,p.entryCanvasId);assert.deepEqual(p,state);
});

test('automatic orientation is deterministic and retains a useful orientation on later reflows',()=>{
 const p=newProject('Auto'),r=createSmartLink(p,p.entryCanvasId,{x:100,y:100});member(p,r,'A');member(p,r,'B');member(p,p.nodes[0],'Obstacle',125,125,120,500);
 reflowSmartLinks(p);assert(['right','down','left','up'].includes(r.smartLink!.resolvedDirection!));const direction=r.smartLink!.resolvedDirection,state=structuredClone(p);reflowSmartLinks(p);assert.equal(r.smartLink!.resolvedDirection,direction);assert.deepEqual(p,state);
 configureSmartLink(p,r.id,{direction:'left',gap:64});reflowSmartLinks(p);assert.equal(smartLinkGeometry(p,r.id)!.direction,'left');assert.equal(r.smartLink!.resolvedDirection,undefined);
});

test('indexed orthogonal rail and stub detours stay connected and never cross unrelated node bodies',()=>{
 const p=newProject('Routes around content'),r=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:48}),a=member(p,r,'A'),b=member(p,r,'B'),c=member(p,r,'C');
 const spineBlock=member(p,p.nodes[0],'Spine blocker',90,180,20,100),stubBlock=member(p,p.nodes[0],'Stub blocker',114,158,15,25);reflowSmartLinks(p);
 const geometry=smartLinkGeometries(p).get(r.id)!;assert.equal(geometry.blockedNodeIds,undefined);assert(geometry.segments.length>4);
 const trunks=geometry.segments.filter(segment=>!segment.nodeId);
 for(const segment of geometry.segments){assert(segment.from.x===segment.to.x||segment.from.y===segment.to.y);const line={x:Math.min(segment.from.x,segment.to.x),y:Math.min(segment.from.y,segment.to.y),width:Math.abs(segment.from.x-segment.to.x)||.0001,height:Math.abs(segment.from.y-segment.to.y)||.0001};assert(!overlap(line,spineBlock));assert(!overlap(line,stubBlock));}
 for(const node of[a,b,c]){const stem=geometry.segments.filter(s=>s.nodeId===node.id);assert(stem.length);const join=stem[0].from;assert(trunks.some(s=>join.x>=Math.min(s.from.x,s.to.x)-.001&&join.x<=Math.max(s.from.x,s.to.x)+.001&&join.y>=Math.min(s.from.y,s.to.y)-.001&&join.y<=Math.max(s.from.y,s.to.y)+.001));}
});

test('position locks and locked branch contacts are not broken by dynamic spacing',()=>{
 const p=newProject('Pinned'),r=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down'}),a=member(p,r,'A',400,300),b=member(p,r,'B'),hidden=member(p,a,'Locked descendant',800,800);a.collapsed=true;hidden.positionLocked=true;
 const before=[a,hidden].map(n=>box(n)),result=reflowSmartLinks(p);assert(result.pinnedNodeIds.includes(a.id));assert.deepEqual([a,hidden].map(n=>box(n)),before);assert(!overlap(a,b));
});

test('authored bends move only when both endpoints share the same branch translation',()=>{
 const p=newProject('Routes'),r=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down'}),a=member(p,r,'A'),child=member(p,a,'Child',650,600);a.collapsed=false;
 const fixed=member(p,p.nodes[0],'Fixed',900,900);p.links.push({id:'inside-route',canvasId:p.entryCanvasId,sourceId:a.id,targetId:child.id,kind:'visual',relation:'generic',points:[{x:600,y:550}]},{id:'outside-route',canvasId:p.entryCanvasId,sourceId:a.id,targetId:fixed.id,kind:'visual',relation:'generic',points:[{x:800,y:800}]});
 const before=box(a);reflowSmartLinks(p);assert.deepEqual(p.links[0].points,[{x:600+a.x-before.x,y:550+a.y-before.y}]);assert.deepEqual(p.links[1].points,[{x:800,y:800}]);
});

test('members insert and reorder using ordinary identity order, detach without losing content or descendants',()=>{
 const p=newProject('Membership'),r=createSmartLink(p,p.entryCanvasId,{x:100,y:100}),a=member(p,r,'A'),b=member(p,r,'B'),c=member(p,p.nodes[0],'C');c.contentItems=[{id:'notes',type:'text',text:'Retained'}];const child=member(p,c,'Child');
 addSmartLinkMember(p,r.id,c.id,1);assert.deepEqual(smartLinkMembers(p,r.id).map(n=>n.id),[a.id,c.id,b.id]);assert.equal(child.parentId,c.id);detachSmartLinkMember(p,c.id);assert.equal(c.parentId,r.parentId);assert.equal(c.contentItems[0].type,'text');assert.equal(child.parentId,c.id);
 assert.throws(()=>addSmartLinkMember(p,c.id,a.id));assert.throws(()=>addSmartLinkMember(p,r.id,p.nodes[0].id));assert.throws(()=>addSmartLinkMember(p,r.id,r.id));
});

function windowFixture(p:MeridianProject,parent:MeridianNode,bounded=true){
 const window=addNode(p,parent.canvasId,bounded?'container':'media','Window');Object.assign(window,{parentId:parent.id,x:500,y:500,width:bounded?400:100,height:bounded?250:40,sizeMode:'fixed',collapsed:false,containerVariant:'window',childLayout:bounded?'contained':'spatial',childArrangement:'grid',grid:{columns:1,rows:40,cellWidth:180,cellHeight:80,windowColumns:1,windowRows:2}});
 const cells=[];for(let i=0;i<40;i++){const cell=member(p,window,'Row '+(i+1),520,580+i*80,180,80);cell.cell={column:1,row:i+1};cells.push(cell);}return{window,cells};
}
test('a forty-row bounded Window reserves only its 400 by 250 frame and keeps every hidden cell',()=>{
 const p=newProject('Capped Window'),rail=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:48}),{window,cells}=windowFixture(p,rail),peer=member(p,rail,'Peer');
 const original=cells.map(cell=>({id:cell.id,cell:structuredClone(cell.cell),dx:cell.x-window.x,dy:cell.y-window.y}));reflowSmartLinks(p);assert.equal(peer.y-window.y,298);assert.equal(cells.length,40);assert.deepEqual(cells.map(cell=>({id:cell.id,cell:cell.cell,dx:cell.x-window.x,dy:cell.y-window.y})),original);assert.deepEqual(reflowSmartLinks(p).movedNodeIds,[]);
});
test('off-viewport Window rows do not become obstacles outside the scrolling surface',()=>{
 const p=newProject('Hidden rows'),{window,cells}=windowFixture(p,p.nodes[0]),last=cells.at(-1)!,rail=createSmartLink(p,p.entryCanvasId,{x:last.x-48,y:last.y-48},{direction:'down',gap:48}),subject=member(p,rail,'Subject'),before=box(last);
 reflowSmartLinks(p);assert.equal(subject.x,last.x);assert.equal(subject.y+subject.height/2,rail.y+rail.height/2);assert.deepEqual(box(last),before);assert.equal(window.height,250);const geometry=smartLinkGeometry(p,rail.id)!;assert.equal(geometry.blockedNodeIds,undefined);assert.equal(geometry.segments.length,3);
});
test('a bounded Window attachment reaches its frame despite contained-cell clearance and still detours around unrelated content',()=>{
 const p=newProject('Window attachment'),rail=createSmartLink(p,p.entryCanvasId,{x:120,y:100},{direction:'down',gap:48}),window=addNode(p,p.entryCanvasId,'container','Register');
 Object.assign(window,{x:180,y:160,width:400,height:250,sizeMode:'fixed',containerVariant:'window',collapsed:false,childLayout:'contained',childArrangement:'grid',grid:{columns:1,rows:40,cellWidth:380,cellHeight:100,windowColumns:1,windowRows:2,showHeaders:false,gap:0},style:{fill:'#17343c',border:'#608b86',titleSize:24,fontSize:18,padding:5,radius:0}});addSmartLinkMember(p,rail.id,window.id);
 const surface=gridGeometry(window,p);for(let row=1;row<=40;row++){const cell=member(p,window,`Item ${row}`,surface.left,surface.top+(row-1)*surface.cellHeight,surface.cellWidth,surface.cellHeight);cell.cell={column:1,row};}
 member(p,rail,'Peer');reflowSmartLinks(p);const placed=navigationIndex(p).bounds(window),unrelated=member(p,p.nodes[0],'Separate content',138,placed.y+placed.height/2-9,14,18);
 const geometry=smartLinkGeometry(p,rail.id)!;assert.equal(geometry.blockedNodeIds,undefined);const stem=geometry.segments.filter(segment=>segment.nodeId===window.id);assert(stem.length>1);const frame=navigationIndex(p).bounds(window),end=stem.at(-1)!.to;assert.deepEqual(end,{x:frame.x,y:frame.y+frame.height/2});
 for(const segment of stem){const line={x:Math.min(segment.from.x,segment.to.x),y:Math.min(segment.from.y,segment.to.y),width:Math.abs(segment.from.x-segment.to.x)||.0001,height:Math.abs(segment.from.y-segment.to.y)||.0001};assert(!overlap(line,unrelated));}
});
test('an exterior Window reserves its visible viewport and not the complete original table',()=>{
 const p=newProject('Exterior Window'),rail=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:48}),{window,cells}=windowFixture(p,rail,false),peer=member(p,rail,'Peer'),expected=navigationIndex(p).branchBounds(window).height+48;
 reflowSmartLinks(p);assert.equal(peer.y-window.y,expected);assert(expected<500);assert.equal(cells.length,40);assert.deepEqual(reflowSmartLinks(p).movedNodeIds,[]);
});
test('bounded children cannot inflate their external footprint while nested Smart Links still lay out',()=>{
 const p=newProject('Capped nested'),outer=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:48}),container=addNode(p,p.entryCanvasId,'container','Enclosure');Object.assign(container,{parentId:outer.id,x:500,y:500,width:400,height:250,sizeMode:'fixed',collapsed:false});
 const inner=createSmartLink(p,p.entryCanvasId,{x:530,y:580},{parentId:container.id,direction:'down',gap:20}),a=member(p,inner,'A',1500,1500),b=member(p,inner,'B',1700,1700),far=member(p,container,'Contained source',9000,9000),peer=member(p,outer,'Peer');
 reflowSmartLinks(p);assert.equal(peer.y-container.y,298);assert.equal(b.y-a.y,60);assert(a.x>inner.x);assert.equal(far.parentId,container.id);assert.deepEqual(reflowSmartLinks(p).movedNodeIds,[]);
});
test('an exterior Gallery reserves its active shared page and navigation frame rather than all stored page coordinates',()=>{
 const p=newProject('Gallery footprint'),rail=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:48}),gallery=member(p,rail,'Gallery',500,500,100,40);gallery.containerVariant='gallery';gallery.collapsed=false;
 const first=member(p,gallery,'First',700,600,280,200),second=member(p,gallery,'Second',9000,10000,320,220);member(p,second,'Other page material',12000,12000,200,200);const peer=member(p,rail,'Peer'),context={galleryItems:new Map([[gallery.id,first.id]])},expected=navigationIndex(p,context).branchBounds(gallery).height+48;
 reflowSmartLinks(p,p.entryCanvasId,{context});assert.equal(peer.y-gallery.y,expected);assert(expected<1000);assert.equal(second.parentId,gallery.id);assert.deepEqual(reflowSmartLinks(p,p.entryCanvasId,{context}).movedNodeIds,[]);
});
test('automatic enclosing frames still separate a following peer after nested Smart Link placement',()=>{
 const p=newProject('Automatic enclosure'),outer=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:48}),container=addNode(p,p.entryCanvasId,'container','Automatic');Object.assign(container,{parentId:outer.id,x:500,y:500,width:400,height:250,sizeMode:'auto',collapsed:false});
 const inner=createSmartLink(p,p.entryCanvasId,{x:530,y:580},{parentId:container.id,direction:'down',gap:20}),a=member(p,inner,'A',550,600),b=member(p,inner,'B',650,650),peer=member(p,outer,'Peer');
 reflowSmartLinks(p);const final=navigationIndex(p).bounds(container);assert.equal(b.y-a.y,60);assert(peer.y>=final.y+final.height+48-1e-6,JSON.stringify({peer:box(peer),final}));assert.deepEqual(reflowSmartLinks(p).movedNodeIds,[]);
});
test('addressed cells cannot silently lose their allocation when attached to a Smart Link',()=>{
 const p=newProject('Keep addresses'),rail=createSmartLink(p,p.entryCanvasId,{x:100,y:100}),{cells}=windowFixture(p,p.nodes[0]),before=structuredClone(p);assert.throws(()=>addSmartLinkMember(p,rail.id,cells[0].id),/addressed cell/);assert.deepEqual(p,before);
});

test('strict shape and recipes reject unsafe or contradictory Smart Link settings and signal wires',()=>{
 const p=newProject('Validate'),r=createSmartLink(p,p.entryCanvasId,{x:100,y:100});
 for(const bad of[{direction:'diagonal',gap:48},{direction:'down',gap:0},{direction:'down',gap:Infinity},{direction:'auto',gap:48,code:'evil'},{direction:'auto',gap:48,resolvedDirection:'auto'}]){const q=structuredClone(p);q.nodes.find(n=>n.id===r.id)!.smartLink=bad as never;assert.equal(isProjectShape(q),false);}
 const q=structuredClone(p);q.nodes.find(n=>n.id===r.id)!.kind='media';assert.equal(isProjectShape(q),false);
 const n=addNode(p,p.entryCanvasId,'input','Input');p.links.push({id:'bad-signal',canvasId:p.entryCanvasId,sourceId:n.id,targetId:r.id,kind:'functional',relation:'causal'});assert(validateProject(p).some(issue=>issue.message.includes('Smart Links')));
 const base=newProject();assert.throws(()=>applyRecipe(base,{version:1,nodes:[{key:'bad',canvas:base.entryCanvasId,kind:'media',smartLink:{direction:'down',gap:48}}]}),/Smart Links/);
});

test('Smart Link settings and ordered membership survive forks, snapshots, publication, clipboard, Modules and selected snippets',()=>{
 const p=newProject('Portable'),r=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:24}),a=member(p,r,'A'),b=member(p,r,'B');reflowSmartLinks(p);const saved=commitSnapshots(p),fork=forkProject(saved);
 const assertRail=(q:MeridianProject)=>{const rail=q.nodes.find(n=>n.smartLink)!;assert(rail);assert.equal(rail.smartLink!.gap,24);assert.equal(smartLinkMembers(q,rail.id).length,2);assert.deepEqual(validateProject(q),[]);};
 assertRail(fork);assertRail(createPublicationPayload(saved).project);assert.equal(saved.snapshots[r.id].node.smartLink!.direction,'down');
 const clipboard=copyNodes(p,[r.id]),destination=newProject('Paste');assertRail(pasteNodes(destination,clipboard,destination.entryCanvasId).project);
 const stored=createModuleFromSelection(p,[r.id,a.id,b.id],'Smart assembly'),moduleTarget=newProject('Module target');assertRail(instantiateModule(moduleTarget,stored,{canvasId:moduleTarget.entryCanvasId,x:300,y:300}).project);
 assertRail(createSnippet(saved,[r.id],{includeDescendants:true,includeExternalReferences:true}).project);
});

test('Smart Link recipes resolve parent aliases and compact after a member is removed',()=>{
 const base=newProject('Recipe'),out=applyRecipe(base,{version:1,nodes:[{key:'rail',canvas:base.entryCanvasId,kind:'junction',utilityVariant:'joint',x:100,y:100,width:12,height:12,smartLink:{direction:'down',gap:24}},{key:'a',canvas:base.entryCanvasId,parent:'rail',kind:'media',title:'A',width:100,height:40,sizeMode:'fixed'},{key:'b',canvas:base.entryCanvasId,parent:'rail',kind:'media',title:'B',width:100,height:40,sizeMode:'fixed'}]});
 const a=out.project.nodes.find(n=>n.id===out.aliases.nodes.a)!,b=out.project.nodes.find(n=>n.id===out.aliases.nodes.b)!;assert.equal(b.y-a.y,64);assert.equal(base.nodes.length,1);
 const removed=applyRecipe(out.project,{version:1,bindings:out.aliases,remove:{nodes:['a']}}),remaining=removed.project.nodes.find(n=>n.id===b.id)!,rail=removed.project.nodes.find(n=>n.id===out.aliases.nodes.rail)!;assert.equal(remaining.y+remaining.height/2,rail.y+rail.height/2);assert.equal(remaining.parentId,out.aliases.nodes.rail);
});

test('all four rail axes center unequal direct bodies and expose both signed trunk halves',()=>{
 for(const direction of ['right','down','left','up'] as const){const p=newProject(direction),rail=createSmartLink(p,p.entryCanvasId,{x:400,y:400},{direction,gap:32}),items=[member(p,rail,'First',0,0,80,40),member(p,rail,'Second',0,0,180,80),member(p,rail,'Third',0,0,120,60)];reflowSmartLinks(p);const horizontal=direction==='right'||direction==='left',leading=Math.min(...items.map(n=>horizontal?n.x:n.y)),trailing=Math.max(...items.map(n=>horizontal?n.x+n.width:n.y+n.height));assert.equal((leading+trailing)/2,400,direction);assert(leading<400&&trailing>400);for(const n of items){if(direction==='right')assert(n.y>=432);else if(direction==='left')assert(n.y+n.height<=368);else if(direction==='down')assert(n.x>=432);else assert(n.x+n.width<=368);}
  const geometry=smartLinkGeometry(p,rail.id)!;assert.equal(geometry.blockedNodeIds,undefined);const dot=(point:{x:number;y:number})=>direction==='right'?point.x:direction==='left'?-point.x:direction==='down'?point.y:-point.y;assert(dot(geometry.start)<dot(geometry.origin));assert(dot(geometry.end)>dot(geometry.origin));const before=structuredClone(p);assert.deepEqual(reflowSmartLinks(p).movedNodeIds,[]);assert.deepEqual(p,before);
 }
});

test('semantic parent centers nested child chains while expanded asymmetric descendants reserve clear gaps',()=>{
 const p=newProject('Centered parents'),parent=member(p,p.nodes[0],'Parent',500,100,200,60),rail=createSmartLink(p,p.entryCanvasId,{x:650,y:250},{parentId:parent.id,direction:'right',gap:32}),a=member(p,rail,'A',0,0,100,40),b=member(p,rail,'B',0,0,180,40),c=member(p,rail,'C',0,0,80,40),inner=createSmartLink(p,p.entryCanvasId,{x:0,y:500},{parentId:a.id,direction:'right',gap:24}),aa=member(p,inner,'AA',0,0,90,40),ab=member(p,inner,'AB',0,0,300,40);a.collapsed=true;reflowSmartLinks(p);const compact=[a,b,c].map(box);assert.equal(rail.x+6,parent.x+100);assert.equal((a.x+c.x+c.width)/2,parent.x+100);
 for(let i=0;i<3;i++){a.collapsed=false;reflowSmartLinks(p);assert.equal(inner.x+6,a.x+a.width/2);assert.equal((aa.x+ab.x+ab.width)/2,a.x+a.width/2);assert.equal((a.x+c.x+c.width)/2,parent.x+100);assert(b.x>=ab.x+ab.width+32);a.collapsed=true;reflowSmartLinks(p);assert.deepEqual([a,b,c].map(box),compact);}
});

test('a rotated semantic rail stays clear of its parent while bounded interior and locked anchors retain their location',()=>{
 const p=newProject('Anchor roles'),parent=member(p,p.nodes[0],'Parent',400,400,300,180),rail=createSmartLink(p,p.entryCanvasId,{x:780,y:490},{parentId:parent.id,direction:'down'}),a=member(p,rail,'A'),b=member(p,rail,'B');reflowSmartLinks(p);configureSmartLink(p,rail.id,{direction:'right'});reflowSmartLinks(p);assert.equal(rail.x+6,parent.x+150);assert(rail.y+6>=parent.y+parent.height+24);assert(!overlap(parent,a)&&!overlap(parent,b));
 const container=addNode(p,p.entryCanvasId,'container','Interior');Object.assign(container,{x:1400,y:200,width:500,height:500,sizeMode:'fixed',collapsed:false});const inside=createSmartLink(p,p.entryCanvasId,{x:1480,y:340},{parentId:container.id,direction:'right'});member(p,inside,'Inner');const before=box(inside);reflowSmartLinks(p);assert.deepEqual(box(inside),before);
 const locked=member(p,rail,'Locked',1800,800);locked.positionLocked=true;const anchor=box(rail),lockedBefore=box(locked);parent.x+=100;reflowSmartLinks(p);assert.deepEqual(box(rail),anchor);assert.deepEqual(box(locked),lockedBefore);
});

test('indexed obstacle avoidance can use the negative half and maintains canonical insertion order',()=>{
 const p=newProject('Both directions'),rail=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:48}),a=member(p,rail,'A'),obstacle=member(p,p.nodes[0],'Fixed',148,80,100,90);reflowSmartLinks(p);assert(a.y+a.height<=obstacle.y);assert(!overlap(a,obstacle));const stable=structuredClone(p);reflowSmartLinks(p);assert.deepEqual(p,stable);
 const q=newProject('Signed insertion'),r=createSmartLink(q,q.entryCanvasId,{x:500,y:500},{direction:'right',gap:32}),one=member(q,r,'One'),two=member(q,r,'Two'),inserted=member(q,q.nodes[0],'Inserted');addSmartLinkMember(q,r.id,inserted.id,0);reflowSmartLinks(q);assert.deepEqual(smartLinkMembers(q,r.id).map(n=>n.id),[inserted.id,one.id,two.id]);assert(inserted.x<500);assert(inserted.x<one.x&&one.x<two.x);assert.equal((inserted.x+two.x+two.width)/2,500);const geometry=smartLinkGeometry(q,r.id)!;assert(geometry.start.x<500&&geometry.end.x>500);
});

test('unrelated world obstacles do not bias nested local plans and a semantic chain keeps its center when avoiding them',()=>{
 const p=newProject('World and local frames'),owner=member(p,p.nodes[0],'Owner',400,100,200,60),rail=createSmartLink(p,p.entryCanvasId,{x:530,y:300},{parentId:owner.id,direction:'right',gap:32}),a=member(p,rail,'A'),b=member(p,rail,'B'),c=member(p,rail,'C'),nested=createSmartLink(p,p.entryCanvasId,{x:550,y:600},{parentId:a.id,direction:'right',gap:24});member(p,nested,'Nested A',200,700,120,40);member(p,nested,'Nested B',500,700,160,40);a.collapsed=false;
 const obstacle=member(p,p.nodes[0],'Fixed world content',300,332,260,260),before=box(obstacle);reflowSmartLinks(p);assert.equal((a.x+c.x+c.width)/2,owner.x+owner.width/2);assert.equal(nested.x+6,a.x+a.width/2);for(const node of[a,b,c,...smartLinkMembers(p,nested.id)])assert(!overlap(node,obstacle));assert.deepEqual(box(obstacle),before);const once=structuredClone(p);assert.deepEqual(reflowSmartLinks(p).movedNodeIds,[]);assert.deepEqual(p,once);
});

test('50,000-node Work spacing measures each node once and broad-phase checks stay local',()=>{
 const p=newProject('Scale'),r=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down'});
 for(let i=0;i<49998;i++){const n=member(p,i<400?r:p.nodes[0],String(i),i<400?100:10000+(i%200)*200,i<400?100:10000+Math.floor(i/200)*100,80,30);if(i>=400)delete n.style;}
 const started=performance.now(),out=reflowSmartLinks(p),elapsed=performance.now()-started;assert.equal(p.nodes.length,50000);assert.equal(out.movedNodeIds.length,400);assert(out.obstacleChecks<1000,`Unexpected broad phase work: ${out.obstacleChecks}`);assert.deepEqual(reflowSmartLinks(p).movedNodeIds,[]);console.log(`SMART_LINK_50000 | ${elapsed.toFixed(1)} ms | obstacle checks ${out.obstacleChecks}`);
});
