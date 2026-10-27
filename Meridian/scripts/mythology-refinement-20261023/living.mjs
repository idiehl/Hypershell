/** Offline, identity-preserving refinement of the living mythology atlas. */
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {applyRecipe,inspectProject} from '../../src/core/authoring.ts';
import {commitSnapshots} from '../../src/core/model.ts';
import {validateProject} from '../../src/core/rules.ts';

const ROOT='I:/dev/Meridian', OUT=path.join(ROOT,'output/mythology-refinement-20261023/living');
const base=JSON.parse((await readFile(path.join(ROOT,'output/mythology-refinement-20261023/baseline/3d896c72-36b9-4900-adb6-a68934591233.json'),'utf8')).replace(/^\uFEFF/,''));
const canvas=base.canvases[0],rootId=canvas.rootId;
const records=new Map(base.nodes.map(n=>[n.id,structuredClone(n)])),originalById=new Map(base.nodes.map(n=>[n.id,n]));
const updates=new Map(),newNodes=new Map(),assets=[],children=new Map(),local=new Map(),footprints=new Map();
const P={paper:'#F4EEE2',ink:'#263B3D',surface:'#FCF8EF',line:'#C9BDA7',gold:'#9D713D',sea:'#386C70',wine:'#765565',olive:'#687553',rust:'#9C5941'};
const k=id=>id===rootId?'work':originalById.has(id)?`n/${id}`:id;
const bounded=n=>['container','module','spreadsheet'].includes(n.kind);
function rebuild(){children.clear();for(const n of records.values())if(n.parentId){let a=children.get(n.parentId)||[];a.push(n);children.set(n.parentId,a)}}
function patch(id,fields,unset=[]){const n=records.get(id);assert.ok(n,id);for(const f of unset)delete n[f];Object.assign(n,fields);const prior=updates.get(id)||{};updates.set(id,{...prior,...fields,unset:[...new Set([...(prior.unset||[]),...unset])]});return n}
function add(id,fields){const n={id,canvasId:canvas.id,parentId:rootId,kind:'text',title:'',x:0,y:0,width:380,height:160,sizeMode:'fixed',childPosition:'manual',placement:'free',...fields};records.set(id,n);newNodes.set(id,n);return n}
const mainNames=['The genealogy','The Iliad','The Odyssey','Stories & traditions','Hesiod & early poetry','Sources & reading room'];
const mains=mainNames.map(t=>base.nodes.find(n=>n.title===t));assert.ok(mains.every(Boolean));
const [genealogy,iliad,odyssey,stories,hesiod,sources]=mains;
const archivedText=base.nodes.filter(n=>n.title||n.text).map(n=>`## ${n.title||n.tag||n.kind}\n\nIdentity: ${n.id}\n\n${n.text||''}`).join('\n\n---\n\n');
assets.push({key:'refinement/complete-reading',name:'Greek mythology — retained original readings.md',mime:'text/markdown',base64:Buffer.from(archivedText).toString('base64')});
add('refinement/reading-archive',{kind:'document',parentId:sources.id,title:'Readings',tag:'Original atlas readings and provenance',asset:'refinement/complete-reading',collapsed:true,width:430,height:180});
add('refinement/accounts',{kind:'container',parentId:genealogy.id,title:'Accounts',tag:'Genealogy — attributed accounts',collapsed:true,containerVariant:'window',childLayout:'contained',childArrangement:'grid',width:430,height:180,grid:{columns:3,rows:177,cellWidth:430,cellHeight:220,gap:0,windowColumns:3,windowRows:3,scrollDirection:'vertical',showHeaders:false}});
add('refinement/retained',{kind:'container',parentId:sources.id,title:'Annotations',tag:'Preserved contextual and earlier material',collapsed:true,containerVariant:'window',childLayout:'contained',childArrangement:'grid',width:430,height:180,grid:{columns:3,rows:80,cellWidth:430,cellHeight:260,gap:0,windowColumns:3,windowRows:2,scrollDirection:'vertical',showHeaders:false}});

rebuild();
const canonical=new Set(base.nodes.filter(n=>/· account & sources$/.test(n.title)).map(n=>n.parentId));
const accountNodes=base.nodes.filter(n=>/· account & sources$/.test(n.title));
// The original figure identities stay in their actual genealogical branch.
// Their long attributed accounts move to a searchable, addressed reading surface.
for(const [i,n] of accountNodes.entries()){
  const figure=records.get(n.parentId);
  const a=`refinement/account/${n.id}`;
  assets.push({key:a,name:`${figure.title} — account.txt`,mime:'text/plain',base64:Buffer.from(n.text||'').toString('base64')});
  patch(n.id,{parent:'refinement/accounts',parentId:'refinement/accounts',title:figure.title,tag:`${figure.title} · attributed account`,contentType:'asset',asset:a,collapsed:true,cell:{column:i%3+1,row:Math.floor(i/3)+1},width:430,height:220},['titleRuns','textRuns','expansionTray']);
  patch(figure.id,{text:'',collapsed:true},['textRuns']);
}
const referenceNotes=base.nodes.filter(n=>n.title==='Selected account · same figure');
for(const [i,n] of referenceNotes.entries()){
 const figure=originalById.get(n.parentId),a=`refinement/reference-account/${n.id}`;
 assets.push({key:a,name:`${figure.title} — testimony.txt`,mime:'text/plain',base64:Buffer.from(n.text||'').toString('base64')});
 patch(n.id,{parent:'refinement/accounts',parentId:'refinement/accounts',title:figure.title,tag:`${figure.title} · parentage testimony`,contentType:'asset',asset:a,collapsed:true,width:430,height:220},['titleRuns','textRuns','expansionTray']);
}
records.get('refinement/accounts').grid.rows=Math.ceil((accountNodes.length+referenceNotes.length)/3);

const retained=records.get('refinement/retained');
const moveToAnnotations=(n,title=n.title)=>patch(n.id,{parent:'refinement/retained',parentId:'refinement/retained',title,collapsed:children.has(n.id)?true:n.collapsed});
for(const id of ['a0be0237-0c05-4bf8-a678-bccd0a1924ca','46621095-861d-4e36-a53d-27fd75ac4807','0c303031-2e98-4a08-a75f-0a5d6b663480','e547bbb1-3ed9-4305-9747-a4f196304660'])moveToAnnotations(records.get(id));
const heading=patch('a801bc8b-0dd0-4db4-a5f4-936c04c8d2e7',{title:'Greek mythology',text:'',width:1080,height:180,x:80,y:40},['titleRuns','textRuns']);

const titleMap=new Map([
['The genealogy','Genealogy'],['The Iliad','Iliad'],['The Odyssey','Odyssey'],['Stories & traditions','Myths'],['Hesiod & early poetry','Hesiod'],['Sources & reading room','Readings'],
['Context & reading aids','Context'],['Books 1–24 · reading order','Books'],['03 · A story inside a story','Odysseus'],['04 · Shared forms, different worlds','Divine forms'],['05 · A return with a cost','Persephone'],['45 · Read the epics together','Epic themes'],
['20 · Gods · stories and sources','Gods'],['50 · Greek and Roman correspondences','Greek & Roman'],['The divine body','Divine forms'],['The Trojan story · later visions','Trojan War'],['Sea, earth and return','Homecoming'],['Music and victory','Muses'],
['Parents whose ancestry is not selected','Other lineages'],['Sources, earlier appearances & scope','Testimonia'],['Museum objects · captions and credits','Image credits'],['Sources & further reading','Sources'],['Earlier reading map','Annotations'],
['Passages & provenance','Passages'],['Explore this chapter','Themes'],['← Back to overview','Overview'],['Selected account · same figure','Account'],
['People, parents & passages','Accounts'],['Selected family relationships','Lineage'],['Sources & variants','Variants'],['The account','Story'],['People and powers','Characters'],['Interpretive lens','Themes'],['Themes to trace','Themes'],['Read the entire book','Text'],['Follow the events','Events'],['Reading aids & original edition','Readings'],['People & interpretation','Characters'],['The telling','Narrative'],['Children and traditions','Children'],['Stories and ancient passages','Myths'],['Explore the genealogy','Genealogy'],['Stories & alternative tellings','Variants'],['Source shelf','Sources'],['Mothers, fathers and supporting families','Families'],['Follow this reading sequence','Sequence'],['Family branch directory','Families'],['Complete atlas directory','Directory'],['How to explore the atlas','Notes'],['Complete translation','Text'],['How to read these families','Notes'],
['42 · Odyssey · telling and journey','Odysseus'],['42.1 · Odyssey · journey sequence','Ithaca'],['42.2 · Odyssey · journey sequence','Voyage'],['42.3 · Odyssey · journey sequence','Return']
]);
function cleanTitle(title){
 if(titleMap.has(title))return titleMap.get(title);
 let t=title.replace(/^(?:D\d+|H\d+|\d+(?:\.\d+)*)\s*·\s*/,'');
 t=t.replace(/· alternative offspring accounts$/,'· variants').replace(/· earlier appearances$/,'· appearances');
 if(/· Book \d+ · text$/.test(t))return 'Text';
 if(/^Edition notes · \d+ of \d+$/.test(t))return 'Edition notes';
 if(/^Book \d+ ·/.test(t))return t;
 if(/^The (Iliad|Odyssey) \d/.test(t))return t.replace(/^The /,'');
 if(/^Sources,? credits/i.test(t))return 'Sources';
 return t;
}
const navigationPattern=/^(?:ATLAS MERIDIAN|Choose |Begin with|Select |Double.click|← Back|How to |Read the source|Read the epics|Order in heaven\. Work on earth\.|Complete translations, original notes|Theogony and Works and Days take different routes)/i;
for(const original of base.nodes){
 const n=records.get(original.id);if(n.kind==='canvas')continue;
 const hierarchical=(children.has(n.id)||['container','reference','canvas-link'].includes(n.kind));
 const originalTitle=original.title;
 if(!updates.has(n.id))patch(n.id,{});
 let title=cleanTitle(originalTitle);
 if(referenceNotes.includes(original))title=originalById.get(original.parentId).title;
 if(n.kind==='reference'&&/^(?:←|Next book|More figures|Read the entire|Four-book overview|Explore|Complete translation)/.test(originalTitle)){
   const target=originalById.get(n.targetId);if(target){title=cleanTitle(target.title);if(/^Book \d+ ·/.test(title))title=title.match(/^Book \d+/)[0];}
 }
 if(title!==originalTitle)patch(n.id,{title,tag:original.tag||originalTitle},['titleRuns']);
 if(n.parentId!==rootId&&navigationPattern.test(originalTitle)&&!canonical.has(n.id)&&!accountNodes.includes(original))moveToAnnotations(n);
 if(hierarchical&&!accountNodes.includes(original)&&n.kind!=='reference')patch(n.id,{text:''},['textRuns']);
 if(n.kind==='reference')patch(n.id,{text:''},['textRuns']);
 if(n.kind==='document'&&/^(?:Open |Full notes remain|This (?:document|chapter|file)|Download )/i.test(original.text||''))patch(n.id,{text:''},['textRuns']);
 if(title.length>120&&!canonical.has(n.id)&&n.kind==='text'&&!(n.text?.length))patch(n.id,{title:'Context',text:originalTitle,tag:original.tag||originalTitle},['titleRuns']);
 // Chronological book chapters retain their event title; counts and translator
 // scaffolding remain in the complete reading document, not the graph.
 if(/^Book \d+ ·/.test(originalTitle)&&n.kind==='container')patch(n.id,{text:''});
 const isPassage=/· Book \d+ · text$/.test(originalById.get(original.parentId)?.title||'');
 if(isPassage)patch(n.id,{title:'',tag:originalTitle},['titleRuns']);
 const isFunctional=['input','number','joint','junction','output','switch','operation'].includes(n.kind);
 if(!isFunctional){
  const isArt=['image','media'].includes(n.kind)&&!!n.assetId;
  let style={...n.style,fontFamily:'palatino-linotype',fontSize:isPassage?19:18,titleSize:canonical.has(n.id)?27:25,textColor:P.ink,fill:P.surface,border:P.line,accent:n.style?.accent||P.gold,radius:0,borderWidth:1,padding:14,contentGap:8,textMediaGap:10,childPadding:16,contentSizing:isPassage||n.kind==='document'?'fixed':'fit',textAlign:'left'};
  if(n.style?.variant==='label')style={...style,shape:'none',variant:'label',fill:P.surface,borderWidth:0,padding:0,contentSizing:'fixed'};
  if(isArt)style={...style,imageFit:'contain',contentSizing:'fixed',backgroundDisplay:'hover'};
  patch(n.id,{style,sizeMode:'fixed',childPosition:'manual',placement:'free'});
 }
}
patch(heading.id,{style:{...heading.style,fontFamily:'palatino-linotype',titleSize:82,fontSize:24,variant:'label',shape:'none',borderWidth:0,padding:0,contentSizing:'fixed'}});
for(const [i,n] of mains.entries())patch(n.id,{text:'',collapsed:true,width:610,height:240,style:{...records.get(n.id).style,fontFamily:'palatino-linotype',titleSize:42,fontSize:20,padding:20,childPadding:28,backgroundOpacity:.13,backgroundDisplay:'always',accent:[P.gold,P.rust,P.sea,P.wine,P.olive,P.gold][i]}});

// Duplicate technical notices and old source registers remain reachable, but
// they do not compete with the subjects in the principal composition.
rebuild();
for(const n of newNodes.values())if(!n.style)n.style={fill:P.surface,border:P.line,textColor:P.ink,accent:P.gold,fontFamily:'palatino-linotype',titleSize:27,fontSize:18,padding:14,childPadding:16,radius:0,contentSizing:'fixed'};
const isReading=n=>/· Book \d+ · text$/.test(originalById.get(n.id)?.title||'');
const isLogic=n=>['input','number','joint','junction','output','switch','operation'].includes(n.kind);
function cover(n){
 if(mains.some(m=>m.id===n.id))return {width:610,height:240};
 if(isLogic(n))return {width:n.width,height:n.height};
 if(canonical.has(n.id))return {width:350,height:112};
 if(n.kind==='reference')return {width:320,height:112};
 if(n.kind==='document'||accountNodes.some(x=>x.id===n.id))return {width:430,height:210};
 if(['image','media'].includes(n.kind)&&n.assetId)return {width:500,height:560};
 if(bounded(n))return {width:430,height:190};
 if(n.style?.variant==='label')return {width:Math.min(900,Math.max(430,n.title.length*17)),height:n.title.length>100?130:90};
 if((n.text?.length||0)>700)return {width:640,height:Math.min(850,180+Math.ceil(n.text.length/70)*27)};
 return {width:430,height:(n.text?.length||0)>160?270:170};
}
for(const n of records.values())if(n.kind!=='canvas'&&n.id!==heading.id)patch(n.id,{...cover(n)});

// Window reading desks and object Galleries retain finite, consistent frames.
for(const n of records.values())if(isReading(n)){
 const cs=children.get(n.id)||[];
 patch(n.id,{containerVariant:'window',childLayout:'contained',childArrangement:'grid',collapsed:true,grid:{columns:1,rows:Math.max(1,cs.length),cellWidth:720,cellHeight:900,gap:0,windowColumns:1,windowRows:1,showHeaders:false,scrollDirection:'vertical'}});
 cs.forEach((c,i)=>patch(c.id,{cell:{column:1,row:i+1},width:720,height:900,style:{...c.style,fontFamily:'palatino-linotype',fontSize:19,titleSize:22,contentSizing:'fixed',padding:22}}));
}

const seen=new Set();
function pack(n){
 if(seen.has(n.id))throw new Error(`Parent cycle ${n.id}`);seen.add(n.id);
 const cs=children.get(n.id)||[];for(const c of cs)pack(c);
 const own=cover(n);if(!cs.length){footprints.set(n.id,own);seen.delete(n.id);return own}
 const gap=70,inset=bounded(n)?28:18,positions=new Map();let w=0,h=0;
 if(n.kind==='canvas'){seen.delete(n.id);return own}
 if(n.containerVariant==='window'||n.kind==='spreadsheet'){
  const g=n.grid||{columns:3,rows:Math.ceil(cs.length/3),cellWidth:430,cellHeight:260,windowColumns:3,windowRows:2,showHeaders:false};
  if(n.id==='refinement/retained')g.rows=Math.max(1,Math.ceil(cs.length/g.columns));
  patch(n.id,{grid:g});
  w=(g.windowColumns||3)*g.cellWidth+inset*2;h=(g.windowRows||2)*g.cellHeight+90+inset*2;
  cs.forEach((c,i)=>{const cell=n.kind==='spreadsheet'&&c.cell?c.cell:{column:i%g.columns+1,row:Math.floor(i/g.columns)+1};patch(c.id,{cell});positions.set(c.id,{x:inset+(cell.column-1)*g.cellWidth,y:90+inset+(cell.row-1)*g.cellHeight});});
 }else if(n.containerVariant==='gallery'){
  w=780;h=900;cs.forEach(c=>positions.set(c.id,{x:inset,y:90+inset}));
 }else if(canonical.has(n.id)){
  // Names remain at a common reading scale; sibling branches receive their
  // complete reserved rectangles instead of being forced into a tiny card grid.
  const cols=cs.length>4?2:1;
  const widths=Array.from({length:cols},()=>0),heights=Array.from({length:cols},()=>0),assigned=[];
  for(const c of [...cs].sort((a,b)=>footprints.get(b.id).height-footprints.get(a.id).height)){
   const col=heights.indexOf(Math.min(...heights)),s=footprints.get(c.id);assigned.push({c,col,y:heights[col]});widths[col]=Math.max(widths[col],s.width);heights[col]+=s.height+gap;
  }
  const xs=widths.map((_,i)=>own.width+150+widths.slice(0,i).reduce((a,b)=>a+b+gap,0));
  assigned.forEach(({c,col,y})=>positions.set(c.id,{x:xs[col],y}));
  w=own.width+150+widths.reduce((a,b)=>a+b,0)+(cols-1)*gap;h=Math.max(own.height,Math.max(...heights)-gap);
 }else{
  const bookMap=originalById.get(n.id)?.title==='Books 1–24 · reading order';
  const book=/^Book \d+ ·/.test(originalById.get(n.id)?.title||'');
  const sequence=bookMap||book;
  const cols=bookMap?6:book?cs.length:cs.length>20?5:cs.length>8?3:cs.length>3?2:Math.min(cs.length,2);
  const rowWidths=[],rowHeights=[];
  for(let i=0;i<cs.length;i+=cols){const row=cs.slice(i,i+cols);rowWidths.push(row.reduce((a,c)=>a+footprints.get(c.id).width,0)+(row.length-1)*gap);rowHeights.push(Math.max(...row.map(c=>footprints.get(c.id).height)))}
  let y=90+inset;for(let r=0,i=0;i<cs.length;r++,i+=cols){let x=inset;for(const c of cs.slice(i,i+cols)){const s=footprints.get(c.id);positions.set(c.id,{x,y});x+=s.width+gap}y+=rowHeights[r]+gap}
  w=Math.max(own.width,...rowWidths)+inset*2;h=Math.max(own.height,y-gap+inset);
 }
 local.set(n.id,positions);
 if(bounded(n)){
  const expanded={width:w,height:h,sizeMode:'fixed'};patch(n.id,{containerSizes:{collapsed:{...own,sizeMode:'fixed'},expanded},childLayout:'contained',childArrangement:n.containerVariant==='window'||n.kind==='spreadsheet'?'grid':'free',collapsed:true,...own});
  footprints.set(n.id,{width:w,height:h});
 }else{
  patch(n.id,{childLayout:'spatial',expansionTray:{offset:{x:own.width+150,y:0},size:{width:Math.max(100,w-own.width-150),height:Math.max(100,h)},layoutVersion:3},collapsed:true});
  footprints.set(n.id,{width:Math.max(w,own.width),height:Math.max(h,own.height)});
 }
 seen.delete(n.id);return footprints.get(n.id);
}
for(const n of records.values())if(n.parentId===rootId)pack(n);
// The actual subjects form a spatial progression: origins, epics, traditions,
// then the supporting reading surface. Their expanded rectangles are reserved.
let x=80;const topY=420;
for(const n of mains){const s=footprints.get(n.id);patch(n.id,{x,y:topY});x+=s.width+420}
patch('a432620a-41b0-459c-ba89-f018e9b17600',{x:1260,y:30,width:680,height:300});
function place(n){for(const c of children.get(n.id)||[]){const pos=local.get(n.id)?.get(c.id);if(pos)patch(c.id,{x:n.x+pos.x,y:n.y+pos.y});place(c)}}
for(const n of mains)place(records.get(n.id));

// Six subject spaces replace the oversized all-in-one opening. The original
// overview Work, original roots and all existing graph identities remain.
// Every original branch connection stays within its destination subject Work.
rebuild();
const subjects=mains.map((n,i)=>({source:n.id,key:`subject/${i}`,title:['Genealogy','Iliad','Odyssey','Myths','Hesiod','Readings'][i],members:[]}));
const subjectOf=new Map();
function collect(id,subject){for(const child of children.get(id)||[]){subject.members.push(child.id);subjectOf.set(child.id,subject);collect(child.id,subject)}}
subjects.forEach(s=>collect(s.source,s));
const overviewPlaces=[{x:80,y:420},{x:820,y:420},{x:1560,y:420},{x:80,y:900},{x:820,y:900},{x:1560,y:900}];
for(const [i,s] of subjects.entries()){
 const parent=records.get(s.source), old={x:parent.x,y:parent.y};
 const roots=(children.get(s.source)||[]).slice();
 for(const id of s.members){const n=records.get(id);patch(id,{canvas:s.key,x:n.x-old.x+80,y:n.y-old.y+100,...(n.parentId===s.source?{parent:s.key,parentId:s.key}:{})});}
 // A Work root itself is unbounded. Only top-level authored subject tiles are
 // shown initially; the surrounding empty workspace is available for disclosure.
 patch(s.source,{...overviewPlaces[i],width:650,height:360,collapsed:false,text:'',title:'',containerSizes:{collapsed:{width:650,height:360,sizeMode:'fixed'},expanded:{width:650,height:360,sizeMode:'fixed'}},style:{...parent.style,variant:'label',shape:'none',borderWidth:0,padding:0,childPadding:0,backgroundOpacity:0}},['titleRuns','textRuns']);
 add(`refinement/portal/${i}`,{kind:'canvas-link',parentId:s.source,title:s.title,tag:s.title,target:s.key,x:overviewPlaces[i].x,y:overviewPlaces[i].y,width:650,height:360,style:{fill:P.surface,border:P.line,textColor:P.ink,accent:[P.gold,P.rust,P.sea,P.wine,P.olive,P.gold][i],fontFamily:'palatino-linotype',titleSize:42,fontSize:18,padding:26,portalStyle:'doorway',portalColor:[P.gold,P.rust,P.sea,P.wine,P.olive,P.gold][i],radius:0,backgroundAssetId:parent.style?.backgroundAssetId,backgroundOpacity:.1,backgroundDisplay:'always'}});
 s.topLevel=roots.map(n=>n.id);
}
// Work-specific top-level arrangements follow their actual subject: epic books
// progress across lanes; the genealogy uses roots on the left and descendants
// to the right; traditions group thematic branches without an artificial chain.
for(const s of subjects){
 const roots=s.topLevel.map(id=>records.get(id));
 if(s.title==='Genealogy'){
  const figures=roots.filter(n=>canonical.has(n.id)),support=roots.filter(n=>!canonical.has(n.id));let y=80;
  for(const n of figures){const oldX=n.x,oldY=n.y,at={x:1480,y};const members=[];function ids(id){members.push(id);for(const c of children.get(id)||[])ids(c.id)}ids(n.id);for(const id of members){const c=records.get(id);patch(id,{x:c.x+at.x-oldX+(id===n.id?0:650),y:c.y+at.y-oldY})}patch(n.id,{width:1000,height:400,style:{...n.style,titleSize:90},...(n.expansionTray?{expansionTray:{...n.expansionTray,offset:{...n.expansionTray.offset,x:n.expansionTray.offset.x+650}}}:{})});y+=footprints.get(n.id).height+320;}
  let sy=80;for(const n of support){const oldX=n.x,oldY=n.y;const members=[];function ids(id){members.push(id);for(const c of children.get(id)||[])ids(c.id)}ids(n.id);for(const id of members){const c=records.get(id);patch(id,{x:c.x-oldX+80,y:c.y-oldY+sy})}sy+=footprints.get(n.id).height+180;}
 }
}

// Existing link identities and meanings are retained. Root performs the
// installed-renderer geometry review before applying this staged recipe.
const bindings={canvases:{work:canvas.id},roots:{work:rootId},nodes:Object.fromEntries(base.nodes.filter(n=>n.kind!=='canvas').map(n=>[k(n.id),n.id])),assets:Object.fromEntries(base.assets.map(a=>[`a/${a.id}`,a.id])),links:Object.fromEntries(base.links.map(e=>[`e/${e.id}`,e.id]))};
const recipe={version:1,bindings,canvases:[{key:'work',style:{background:P.paper,gridVisible:false,portalColor:P.gold,portalStyle:'doorway'}},...subjects.map(s=>({key:s.key,title:s.title,style:{background:P.paper,gridVisible:false,portalColor:P.gold,portalStyle:'doorway'}}))],assets,nodes:[],links:[]};
for(const [id,u] of updates){if(newNodes.has(id))continue;const spec={key:k(id),...u};delete spec.parentId;if(u.parentId)spec.parent=k(u.parentId);if(!spec.unset?.length)delete spec.unset;recipe.nodes.push(spec)}
for(const [id,n] of newNodes){const {id:_,canvasId,parentId,...fields}=n;const s=subjectOf.get(id);recipe.nodes.push({key:id,canvas:s?.key||'work',parent:k(parentId),...fields})}
for(const e of base.links){const s=subjectOf.get(e.sourceId);assert.equal(s?.key,subjectOf.get(e.targetId)?.key,`Cross-subject link ${e.id}`);recipe.links.push({key:`e/${e.id}`,...(s?{canvas:s.key}:{}),...(s&&e.parentId&&subjectOf.get(e.parentId)?.key!==s.key?{parent:s.key}:{}),...(e.kind==='visual'?{unset:['points'],style:{...e.style,color:e.style?.dashed?P.wine:e.relation==='chronological'?P.rust:P.gold,width:1.6}}:{})});}
await mkdir(OUT,{recursive:true});
await writeFile(path.join(OUT,'recipe.json'),JSON.stringify(recipe));
const result=applyRecipe(base,JSON.parse(JSON.stringify(recipe))),candidate=commitSnapshots(result.project);assert.deepEqual(validateProject(candidate),[]);
const afterNodes=new Map(candidate.nodes.map(n=>[n.id,n])),afterAssets=new Map(candidate.assets.map(a=>[a.id,a]));
for(const n of base.nodes){assert.ok(afterNodes.has(n.id));for(const f of ['kind','assetId','attachments','hyperlinks','remoteLinks','targetId','targetCell','canvasTarget','dependencyIds','operation','value','valueType','closed','exposed','rules'])if(f!=='assetId'||n.assetId)assert.deepEqual(afterNodes.get(n.id)[f],n[f],`${n.id}.${f}`)}
for(const a of base.assets)assert.deepEqual(afterAssets.get(a.id),a);
const audit={projectId:base.id,baseRevision:base.revision,originalNodes:base.nodes.length,retainedNodes:base.nodes.length,newNodes:candidate.nodes.length-base.nodes.length,originalAssets:base.assets.length,retainedAssets:base.assets.length,newAssets:assets.length,originalLinks:base.links.length,retainedLinks:candidate.links.length,canonicalFigures:canonical.size,movedAccounts:accountNodes.length,completeOriginalTextSha256:createHash('sha256').update(archivedText).digest('hex'),subjects:subjects.map(s=>({title:s.title,workId:result.aliases.canvases[s.key],rootId:result.aliases.roots[s.key],migratedNodes:s.members.length,originalBranch:s.source,fullRegion:footprints.get(s.source)})),serializedBytes:Buffer.byteLength(JSON.stringify(candidate)),passed:true};
for(const [file,value] of [['draft.json',candidate],['aliases.json',result.aliases],['preservation.json',audit],['inspection.json',inspectProject(candidate)]])await writeFile(path.join(OUT,file),JSON.stringify(value,null,2));
const plan=[{workId:canvas.id,label:'overview'},...subjects.map(s=>({workId:result.aliases.canvases[s.key],label:s.title})),{workId:result.aliases.canvases['subject/0'],nodeId:'b831444a-f5c7-4829-bb0e-e908e097d015',scope:'node',scale:.85,label:'Zeus',expand:['34817749-bf21-487d-bde0-f645b78ae723','0d463ff6-01b9-49c4-acca-ca79c17f5851','62d6f98d-fd8d-4e32-9957-cfab19d9f563','b831444a-f5c7-4829-bb0e-e908e097d015']}];
await writeFile(path.join(OUT,'review-plan.json'),JSON.stringify(plan,null,2));
console.log(JSON.stringify(audit,null,2));


