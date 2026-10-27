/** Lossless presentation renewal of the current one-canvas living atlas.
 * Reads frozen API exports and local licensed media; never calls a live host. */
import assert from 'node:assert/strict';
import path from 'node:path';
import { baseline, projectIds, loadMedia, mediaAsset, artCredit, originalBindings, stageRecipe, auditPreservation, writeJSON, OUT } from './shared.mjs';

const original=await baseline(projectIds.living), canvas=original.canvases[0];
assert.equal(original.canvases.length,1);assert.equal(original.nodes.length,5942);
const records=new Map(original.nodes.map(n=>[n.id,structuredClone(n)]));
const oldIds=new Set(records.keys()), children=new Map();
const newSpecs=new Map(), modified=new Map(), placements=new Map(), expanded=new Map();
const media=(await loadMedia()).items, byObject=new Map(media.map(m=>[m.objectId,m]));
const bounded=n=>['container','spreadsheet','module'].includes(n.kind);
const key=id=>id===canvas.rootId?`canvas/${canvas.id}`:oldIds.has(id)?`node/${id}`:id;
const palette={ink:'#263B3D',paper:'#F4EEE2',ivory:'#FCF8EF',line:'#C9BDA7',gold:'#9D713D',sea:'#386C70',wine:'#765565',rust:'#9C5941',olive:'#687553'};
function patch(id,fields){const n=records.get(id);assert.ok(n,`Missing ${id}`);Object.assign(n,fields);modified.set(id,{...(modified.get(id)||{}),...fields});}
function add(k,fields){assert.ok(!records.has(k));const n={id:k,canvasId:canvas.id,parentId:canvas.rootId,kind:'media',title:'',x:0,y:0,width:420,height:240,sizeMode:'fixed',childPosition:'manual',placement:'free',...fields};records.set(k,n);newSpecs.set(k,n);return k;}
function rebuild(){children.clear();for(const n of records.values())if(n.parentId){const cs=children.get(n.parentId)||[];cs.push(n);children.set(n.parentId,cs);}}
rebuild();
const find=(title,kind='container')=>{const matches=[...records.values()].filter(n=>n.title===title&&n.kind===kind);assert.equal(matches.length,1,`Unique ${title}`);return matches[0];};
const mains=['The genealogy','The Iliad','The Odyssey','Stories & traditions','Hesiod & early poetry','Sources & reading room'].map(t=>find(t));
const mainByTitle=new Map(mains.map(n=>[n.title,n]));
const family=mains[0],iliad=mains[1],odyssey=mains[2],stories=mains[3],hesiod=mains[4],sources=mains[5];

// New picture exhibitions use real Gallery containers and ordinary media nodes.
// Full image descriptions/credits also have one permanent document in Sources.
const exhibits=[
 {key:'divine-body',title:'The divine body',parent:stories.id,objects:[247971,246679,254697,246432],accent:palette.gold},
 {key:'artemis',title:'Artemis · image and sanctuary',parent:find('D12 · Artemis').id,objects:[248387,244854,252463],accent:palette.olive},
 {key:'dionysos',title:'Dionysos · glass and marble',parent:find('D17 · Dionysus').id,objects:[249254,255973],accent:palette.wine},
 {key:'trojan-reception',title:'The Trojan story · later visions',parent:[...records.values()].find(n=>n.title==='Context & reading aids'&&n.parentId===iliad.id).id,objects:[209060,816618,338084],accent:palette.rust},
 {key:'return',title:'Sea, earth and return',parent:odyssey.id,objects:[250547,257623,248899,250945],accent:palette.sea},
 {key:'music',title:'Music and victory',parent:hesiod.id,objects:[248205,252976],accent:palette.olive},
];
// Two identically named epic aid containers have distinct owners.
exhibits.find(e=>e.key==='trojan-reception').parent=[...records.values()].find(n=>n.title==='Context & reading aids'&&n.parentId===iliad.id).id;
const artLabels={247971:'Apollo · Greek bronze · ca. 500 BCE',249254:'Dionysos and a nymph · Roman glass',209060:'Achilles, Hector, Athena · Benzoni · 19th century',250547:'Poseidon or Nereus? · Attic drinking cup',246679:'Hermes · Greek or Roman bronze',254697:'Aphrodite · Roman marble',246432:'Herakles · Italic bronze',248387:'Artemis · Tarentine terracotta',244854:'Winged figure · Corinthian perfume vase',257623:'A veiled goddess · possibly Demeter',248899:'Eleusinian Relief · Roman fragments in a cast',248205:'Muses and Sirens · Roman sarcophagus',250945:'Perseus and Andromeda · Roman fresco',816618:'The Judgment of Paris · Turchi · ca. 1640',338084:'The Judgment of Paris · Allori · 1583',252463:'Artemis at Sardis · architectural fragments',252976:'Nike and Eros · Attic painted bobbin',255973:'Dionysos · Roman statue, later restoration'};
const usedObjects=new Set(exhibits.flatMap(e=>e.objects));
const assetSpecs=await Promise.all([...usedObjects].map(mediaAsset));
for(const ex of exhibits){
 const parent=add(`renewal/gallery/${ex.key}`,{parentId:ex.parent,kind:'container',title:ex.title,tag:ex.title,collapsed:true,containerVariant:'gallery',childLayout:'contained',width:470,height:230,style:{fill:palette.ivory,border:palette.line,textColor:palette.ink,accent:ex.accent,fontFamily:'palatino-linotype',titleSize:18,fontSize:12,radius:0,padding:16,childPadding:5,imageFit:'contain'}});
 for(const objectId of ex.objects){const m=byObject.get(objectId);add(`renewal/art/${objectId}`,{parentId:parent,kind:'media',title:artLabels[objectId],tag:`Met ${m.accession} · ${m.title}`,contentType:'asset',asset:m.key,text:await artCredit(objectId),width:620,height:620,style:{fill:'#F6F2E9',border:palette.line,textColor:palette.ink,accent:ex.accent,fontFamily:'palatino-linotype',titleSize:18,fontSize:11,padding:12,imageFit:'contain',backgroundDisplay:'hover',radius:0}});}
}
const registerText=['MUSEUM OBJECTS · IMAGES AND RECEPTION','These photographs show material representations, not proof of mythical events. Dates, object culture, uncertainty and later restorations remain explicit. Photographs are unmodified official museum files.',...await Promise.all([...usedObjects].map(artCredit))].join('\n\n---\n\n');
assetSpecs.push({key:'renewal/museum-register',name:'Museum objects — captions, provenance and CC0 credits.txt',mime:'text/plain',base64:Buffer.from(registerText).toString('base64')});
add('renewal/museum-register-node',{parentId:sources.id,kind:'document',title:'Museum objects · captions and credits',tag:'18 museum objects · attribution register',asset:'renewal/museum-register',width:640,height:360,style:{fill:palette.ivory,border:palette.line,textColor:palette.ink,accent:palette.gold,fontFamily:'segoe-ui',fontSize:18,titleSize:27,radius:0,contentSizing:'fixed'}});
// A decorative reuse shares an existing image asset; it does not duplicate bytes.
add('renewal/foyer-frieze',{kind:'media',title:'',tag:'Muses and Sirens · Roman sarcophagus · Met 10.104',contentType:'asset',asset:'met-248205',x:1080,y:48,width:870,height:400,style:{variant:'label',shape:'none',imageFit:'contain',padding:0,borderWidth:0}});
add('renewal/foyer-frieze-caption',{kind:'text',title:'',tag:'Foyer image caption',text:'Muses and Sirens · Roman marble, 3rd century CE\nThe Metropolitan Museum of Art · Open Access / CC0',x:1100,y:448,width:820,height:80,style:{variant:'label',fontFamily:'palatino-linotype',fontSize:17,titleSize:20,textColor:palette.ink,textAlign:'center',padding:0,contentSizing:'fixed'}});
for(const [index,n] of mains.entries()){
 const accent=[palette.gold,palette.rust,palette.sea,palette.wine,palette.olive,palette.gold][index];
 patch(n.id,{collapsed:true,style:{...n.style,fill:[ '#EADBC0','#EFE1D4','#DDE9E3','#E6DFE4','#E5E7D9','#ECE8DC'][index],border:palette.line,textColor:palette.ink,accent,fontFamily:'palatino-linotype',fontSize:21,titleSize:34,radius:0,padding:20,childPadding:18,textAlign:'left',backgroundDisplay:'hover'},width:560,height:230});
}
rebuild();

// Presentation styling leaves authored content, active content, IDs and kind intact.
const parentOf=n=>records.get(n.parentId);
const isPassage=n=>/· Book \d+ · text$/.test(parentOf(n)?.title||'');
const isReadingText=n=>/· Book \d+ · text$/.test(n.title);
const isBook=n=>/^Book \d+ ·/.test(n.title)&&n.kind==='container';
const isBookMap=n=>n.title==='Books 1–24 · reading order';
const isLogic=n=>['input','number','joint','junction','output','switch'].includes(n.kind);
for(const n of [...records.values()]){
 if(n.kind==='canvas'||newSpecs.has(n.id)||mains.some(m=>m.id===n.id)||isLogic(n))continue;
 const source=n.style||{},hasChildren=children.has(n.id);
 const light=source.textColor?.toUpperCase().startsWith('#F')||source.fill?.toUpperCase()==='#203D4D';
 let style={...source,fontFamily:source.variant==='label'?'palatino-linotype':'segoe-ui',fontSize:isPassage(n)?19:18,titleSize:hasChildren?28:25,contentSizing:'fit',padding:14,contentGap:10,textMediaGap:12,childPadding:14,radius:0,borderWidth:1,textAlign:source.variant==='label'?'left':source.textAlign||'left'};
 if(bounded(n)){style={...style,fill:palette.ivory,border:palette.line,textColor:palette.ink,accent:source.accent||palette.gold,backgroundDisplay:'hover'};}
 else if(source.variant==='label'){style={...style,shape:'none',variant:'label',borderWidth:0,textColor:palette.ink,padding:0,contentSizing:'fixed',titleSize:32,fontSize:20};}
 else if(n.kind==='text'&&/account|source|scope|notes|edition/i.test(n.title)){style={...style,fill:palette.ivory,border:palette.line,textColor:palette.ink,backgroundDisplay:'hover'};}
 else if(hasChildren&&n.kind==='text'){style={...style,fill:/#E9D4AF/i.test(source.fill||'')?'#EFE1C6':'#FBF8EF',border:palette.line,textColor:palette.ink,fontFamily:'palatino-linotype',titleSize:30};}
 else if(light&&!source.variant){style={...style,fill:'#EEF1EA',textColor:palette.ink,border:palette.line};}
 if(n.kind==='document'||n.kind==='spreadsheet')style={...style,contentSizing:'fixed'};
 patch(n.id,{style,sizeMode:'fixed',childPosition:'manual',placement:'free',...(!n.tag?{tag:n.title||`${n.kind} · ${n.id.slice(0,8)}`}:{})});
}
// Existing cover hierarchy remains recognizable, with typography sized for reading.
patch('a0be0237-0c05-4bf8-a678-bccd0a1924ca',{x:80,y:40,width:910,height:44,style:{...records.get('a0be0237-0c05-4bf8-a678-bccd0a1924ca').style,fontFamily:'segoe-ui',titleSize:18,fontSize:18,contentSizing:'fixed'}});
patch('a801bc8b-0dd0-4db4-a5f4-936c04c8d2e7',{x:80,y:110,width:930,height:220,style:{...records.get('a801bc8b-0dd0-4db4-a5f4-936c04c8d2e7').style,fontFamily:'georgia',titleSize:72,fontSize:20,contentSizing:'fixed'}});
patch('46621095-861d-4e36-a53d-27fd75ac4807',{x:85,y:355,width:880,height:138,style:{...records.get('46621095-861d-4e36-a53d-27fd75ac4807').style,fontFamily:'segoe-ui',titleSize:25,fontSize:19,contentSizing:'fixed'}});
patch('0c303031-2e98-4a08-a75f-0a5d6b663480',{x:85,y:1150,width:1810,height:90,style:{...records.get('0c303031-2e98-4a08-a75f-0a5d6b663480').style,fontFamily:'segoe-ui',titleSize:20,fontSize:17,contentSizing:'fixed'}});

// Cover sizes deliberately differ by function; long translated passages have
// generous vertical reading space rather than being crushed into tiny cards.
function cover(n){
 if(mains.some(m=>m.id===n.id))return{width:560,height:230};
 if(newSpecs.has(n.id))return{width:n.width,height:n.height};
 if(isLogic(n))return{width:n.width,height:n.height};
 if(isPassage(n))return{width:650,height:860};
 if(n.kind==='spreadsheet')return{width:470,height:240};
 if(bounded(n))return{width:isBook(n)?400:450,height:isBook(n)?220:230};
 if(n.kind==='document')return{width:480,height:320};
 if(n.kind==='image')return{width:520,height:580};
 if(n.kind==='reference')return{width:350,height:210};
 if(n.style?.variant==='label')return{width:1000,height:n.title.length>100?160:100};
 if((n.text?.length||0)>650)return{width:640,height:Math.max(330,Math.ceil(n.text.length/76)*25+110)};
 return{width:children.has(n.id)?370:460,height:children.has(n.id)?230:240};
}
for(const n of records.values())if(n.kind!=='canvas'&&!['renewal/foyer-frieze','renewal/foyer-frieze-caption'].includes(n.id)&&!['a0be0237-0c05-4bf8-a678-bccd0a1924ca','a801bc8b-0dd0-4db4-a5f4-936c04c8d2e7','46621095-861d-4e36-a53d-27fd75ac4807','0c303031-2e98-4a08-a75f-0a5d6b663480'].includes(n.id))patch(n.id,{...cover(n),sizeMode:'fixed',childPosition:'manual',placement:'free'});

const otherOrigins=find('Parents whose ancestry is not selected');
patch(otherOrigins.id,{containerVariant:'window',childLayout:'contained',childArrangement:'grid',grid:{columns:4,rows:Math.ceil(children.get(otherOrigins.id).length/4),cellWidth:370,cellHeight:230,gap:0,showHeaders:false,windowColumns:3,windowRows:3,scrollDirection:'vertical'}});
const readingDesks=[...records.values()].filter(isReadingText);
assert.equal(readingDesks.length,48,'All original epic reading groups retained');
for(const desk of readingDesks){
 const passages=children.get(desk.id)||[];
 patch(desk.id,{containerVariant:'window',childLayout:'contained',childArrangement:'grid',grid:{columns:2,rows:Math.ceil(passages.length/2),cellWidth:650,cellHeight:860,gap:0,showHeaders:false,windowColumns:2,windowRows:1,scrollDirection:'vertical'},style:{...desk.style,variant:'card',fill:palette.ivory,contentSizing:'fixed',fontFamily:'palatino-linotype',titleSize:28,fontSize:19,childPadding:8}});
 for(const passage of passages)patch(passage.id,{style:{...passage.style,contentSizing:'fixed',fontFamily:'palatino-linotype',fontSize:19,titleSize:23,padding:18,textAlign:'left'}});
}
// Addressed source library: preserve every existing address and its grid exactly.
const spreadsheet=[...records.values()].find(n=>n.kind==='spreadsheet');
const protectedCells=new Set((children.get(spreadsheet.id)||[]).map(n=>n.id));

// Bottom-up local compositions. Every bounded surface receives its true expanded
// frame before it is placed. Ordinary parents retain exterior Expansion Trays.
const local=new Map();
function arrange(n){
 const cs=children.get(n.id)||[];for(const child of cs)arrange(child);
 if(!cs.length)return;
 if(n.kind==='canvas')return;
 const inset=bounded(n)?24:16,gap=isReadingText(n)?32:32;
 const topInset=inset+(bounded(n)&&n.title?Math.ceil((n.style?.titleSize||28)*1.45+(n.style?.padding||14)):0);
 const size=child=>bounded(child)&&!child.collapsed?expanded.get(child.id)||cover(child):cover(child);
 const positions=new Map();let width=0,height=0;
 if(n.id===spreadsheet.id){
  width=original.nodes.find(x=>x.id===n.id).width;height=original.nodes.find(x=>x.id===n.id).height;
  const originalParent=original.nodes.find(x=>x.id===n.id);
  for(const child of cs){const old=original.nodes.find(x=>x.id===child.id);positions.set(child.id,{x:old.x-originalParent.x,y:old.y-originalParent.y});}
 }else if(n.containerVariant==='window'){
  const g=n.grid;width=g.windowColumns*g.cellWidth+inset*2;height=g.windowRows*g.cellHeight+topInset+inset;
  cs.forEach((child,i)=>{const cell={column:i%g.columns+1,row:Math.floor(i/g.columns)+1};patch(child.id,{cell});positions.set(child.id,{x:inset+(cell.column-1)*g.cellWidth,y:topInset+(cell.row-1)*g.cellHeight});});
 }else if(n.containerVariant==='gallery'){
  width=690;height=720;for(const child of cs)positions.set(child.id,{x:inset,y:inset});
 }else if(isBook(n)){
  const text=cs.find(isReadingText),aids=cs.filter(c=>c!==text);let y=80;
  if(text){const s=size(text);positions.set(text.id,{x:inset,y});width=s.width+inset*2;height=s.height+y+inset;}
  for(const child of aids){const s=size(child);positions.set(child.id,{x:width+gap,y});width=Math.max(width,width+gap+s.width+inset);height=Math.max(height,y+s.height+inset);y+=s.height+gap;}
 }else{
  const headings=cs.filter(c=>c.style?.variant==='label'&&!children.has(c.id)&&!c.asset&&!c.assetId),items=cs.filter(c=>!headings.includes(c));
  let y=topInset;
  for(const child of headings){const s=cover(child);positions.set(child.id,{x:inset,y});width=Math.max(width,s.width+inset*2);y+=s.height+22;}
  const columns=isBookMap(n)?6:isReadingText(n)?2:items.length>64?10:items.length>24?6:items.length>9?4:items.length>3?3:Math.max(1,Math.min(2,items.length));
  for(let start=0;start<items.length;start+=columns){const row=items.slice(start,start+columns);let x=inset,rowHeight=0;const reversed=isBookMap(n)&&Math.floor(start/columns)%2===1;const ordered=reversed?[...row].reverse():row;
   for(const child of ordered){const s=size(child);positions.set(child.id,{x,y});x+=s.width+gap;rowHeight=Math.max(rowHeight,s.height);}width=Math.max(width,x-gap+inset);y+=rowHeight+gap;
  }height=Math.max(y-gap+inset,200);
 }
 width=Math.max(width,250);height=Math.max(height,180);local.set(n.id,positions);expanded.set(n.id,{width,height});
 if(bounded(n)){
  const compact=cover(n);patch(n.id,{containerVariant:n.id===spreadsheet.id?'spreadsheet':n.containerVariant||'contained',childLayout:'contained',childArrangement:n.containerVariant==='window'||n.id===spreadsheet.id?'grid':'free',containerSizes:{collapsed:{...compact,sizeMode:'fixed'},expanded:{width,height,sizeMode:'fixed'}},...(!n.collapsed?{width,height}:{})});
 }else{
  patch(n.id,{childLayout:'spatial',expansionTray:{offset:{x:n.width+70,y:0},size:{width:width+10,height:height+10},layoutVersion:3}});
 }
}
arrange(records.get(canvas.rootId));

function place(n){
 const cs=children.get(n.id)||[],positions=local.get(n.id);
 for(const child of cs){const at=positions?.get(child.id);if(at){const exterior=!bounded(n)&&n.kind!=='canvas';const dx=exterior?n.expansionTray.offset.x:0;patch(child.id,{x:n.x+at.x+dx,y:n.y+at.y});}place(child);}
}
const root=records.get(canvas.rootId);
for(const [i,n] of mains.entries())patch(n.id,{x:80+(i%3)*610,y:550+Math.floor(i/3)*290});
place(root);

// Three authored main covers reuse imagery quietly; the actual objects and full
// credits remain in Galleries. This is paint only and never changes content.
const backdropObjects=[248205,209060,250945,254697,252976,252463];
for(const [i,n] of mains.entries())patch(n.id,{backgroundAsset:`met-${backdropObjects[i]}`,style:{...n.style,backgroundFit:'cover',backgroundOpacity:.13,backgroundDisplay:'hover'}});
const oldArt={circe:original.assets.find(a=>a.name==='circe-met-253627.jpg')?.id,persephone:original.assets.find(a=>a.name==='persephone-met-252973.jpg')?.id,athena:original.assets.find(a=>a.name==='athena-met-244457.jpg')?.id,zeus:original.assets.find(a=>a.name==='zeus-met-252511.jpg')?.id};
const chapterArt=[[/D11 · Apollo/,'met-247971'],[/D12 · Artemis/,'met-248387'],[/D13 · Hermes/,'met-246679'],[/D15 · Aphrodite/,'met-254697'],[/D17 · Dionysus/,'met-255973'],[/D07 · Zeus/,oldArt.zeus],[/D10 · Athena/,oldArt.athena],[/03 · A story inside a story/,oldArt.circe],[/05 · A return with a cost/,oldArt.persephone],[/04 · Shared forms, different worlds/,'met-255973'],[/H1 · Theogony/,'met-248205'],[/H2 · Works and Days/,'met-252463'],[/H3 · Shield of Heracles/,'met-246432'],[/H4 · Catalogue/,'met-252976'],[/50 · Greek and Roman/,'met-255973']];
for(const n of records.values())if(oldIds.has(n.id)&&bounded(n)&&n.collapsed){const art=chapterArt.find(([pattern,asset])=>asset&&pattern.test(n.title));if(art)patch(n.id,{backgroundAsset:art[1],style:{...n.style,backgroundFit:'contain',backgroundOpacity:.23,backgroundDisplay:'always'}});}

// Real route utilities make selected descent legible without inventing any edge.
const originalById=new Map(original.nodes.map(n=>[n.id,n]));
const important=[['34817749-bf21-487d-bde0-f645b78ae723','0d463ff6-01b9-49c4-acca-ca79c17f5851'],['0d463ff6-01b9-49c4-acca-ca79c17f5851','2b174f0b-dummy'],['b831444a-f5c7-4829-bb0e-e908e097d015','3d0e120c-36b7-4b4f-b70f-952e92db8f75']];
const uranusChildren=original.links.filter(e=>e.sourceId===important[1][0]&&originalById.get(e.targetId)?.title==='Cronus');if(uranusChildren.length)important[1][1]=uranusChildren[0].targetId;
const routed=new Set(),routes=[];
for(const [sourceId,targetId] of important){const e=original.links.find(l=>l.kind==='visual'&&l.sourceId===sourceId&&l.targetId===targetId);if(!e)continue;const s=records.get(sourceId),t=records.get(targetId);routed.add(e.id);routes.push({key:`link/${e.id}`,canvas:`canvas/${canvas.id}`,source:key(sourceId),target:key(targetId),parent:e.parentId?key(e.parentId):null,via:[{key:`renewal/lineage/${e.id}`,x:t.x-18,y:t.y+t.height/2,width:10,height:10,variant:'joint',style:{fill:palette.gold,border:palette.gold}}],style:{color:palette.gold,width:2}});}
const recipe={version:1,bindings:originalBindings(original),canvases:[{key:`canvas/${canvas.id}`,style:{background:palette.paper,gridVisible:false,portalStyle:'doorway',portalColor:palette.gold}}],assets:assetSpecs,nodes:[],links:[],routes};
for(const [id,fields] of modified){if(newSpecs.has(id))continue;const originalNode=originalById.get(id);const n=records.get(id);const unset=['style.radius'];if(bounded(n)&&originalNode.kind!=='spreadsheet')unset.push('childOffset');recipe.nodes.push({key:key(id),...fields,unset});}
for(const [id,n] of newSpecs){const {id:_,canvasId,parentId,...fields}=n;recipe.nodes.push({key:id,canvas:`canvas/${canvas.id}`,parent:key(parentId),...fields});}
for(const e of original.links){if(e.kind!=='visual'||routed.has(e.id))continue;const alternative=e.style?.dashed||e.relation==='mutual';recipe.links.push({key:`link/${e.id}`,unset:['points'],style:{...e.style,color:alternative?palette.wine:e.relation==='chronological'?palette.rust:palette.gold,width:e.relation==='chronological'?2:1.5}});}
// This declaration intentionally uses an unset subfield through the new typed API.
// It removes only old radius overrides; authored content and rule fields remain.
const result=await stageRecipe(original,recipe,'living');
const audit=auditPreservation(original,result.project);
await writeJSON(path.join(OUT,'living/design-report.json'),{projectId:original.id,revision:original.revision,scope:'One connected canvas with all original source identities and text retained',originalNodes:original.nodes.length,renewedNodes:result.project.nodes.length,museumObjects:[...usedObjects],newGalleryIds:exhibits.map(e=>({key:e.key,id:result.aliases.nodes[`renewal/gallery/${e.key}`],title:e.title})),mainBranches:mains.map(n=>({id:n.id,title:n.title,collapsedFrame:n.containerSizes.collapsed,expandedFrame:n.containerSizes.expanded})),windowNodeId:otherOrigins.id,sourceSpreadsheetId:spreadsheet.id,routedOriginalLinks:[...routed],expandedFrames:expanded.size,explicitTrays:[...records.values()].filter(n=>n.expansionTray).length,audit});
console.log(JSON.stringify({directory:result.directory,nodes:result.project.nodes.length,links:result.project.links.length,assets:result.project.assets.length,serializedBytes:audit.serializedBytes,routes:routes.length,galleries:exhibits.length,sourcePreservation:audit.passed}));
