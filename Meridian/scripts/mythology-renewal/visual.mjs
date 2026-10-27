/** Five lossless, art-directed Visual atlas canvases. Local staging only. */
import {baseline,projectIds,originalBindings,loadMedia,mediaAsset,artCredit,stageRecipe,OUT,writeJSON} from './shared.mjs';
import path from 'node:path';
import {navigationIndex} from '../../src/core/navigation.ts';
const base=await baseline(projectIds.visual),media=await loadMedia();
const recipe={version:1,bindings:originalBindings(base),canvases:[],nodes:[],assets:[],links:[],routes:[]};
const byId=new Map(base.nodes.map((n,i)=>[n.id,i])),originalNav=navigationIndex(base);
const ref=i=>`node/${base.nodes[i].id}`,canvasRef=i=>`canvas/${base.canvases[i].id}`;
const themes=[
 {bg:'#101F29',paper:'#F1E3C7',text:'#E2E9E7',muted:'#AEC3C5',fill:'#19313B',edge:'#4F6E73',accent:'#D7B377'},
 {bg:'#F2EADA',paper:'#253F45',text:'#263C43',muted:'#60706B',fill:'#E9DDC5',edge:'#B6A58A',accent:'#976B3E'},
 {bg:'#112A31',paper:'#EFE2BD',text:'#E0E9E5',muted:'#ACC5C4',fill:'#1D3D44',edge:'#4D7374',accent:'#D7B377'},
 {bg:'#EEE5D3',paper:'#283E43',text:'#2D4247',muted:'#65716B',fill:'#E4D8C1',edge:'#B1A38B',accent:'#91633C'},
 {bg:'#23232D',paper:'#EFDFBF',text:'#E4DED7',muted:'#C2B9C2',fill:'#343644',edge:'#807780',accent:'#D6B077'},
];
const ci=n=>base.canvases.findIndex(c=>c.id===n.canvasId);
const common=t=>({fill:t.fill,border:t.edge,textColor:t.text,accent:t.accent,fontFamily:'sans',fontSize:19,titleSize:27,contentSizing:'fixed',textAlign:'left',shape:'rectangle',radius:0,borderWidth:1,padding:16,contentGap:10,textMediaGap:8,childPadding:12});
const label=(t,size=24)=>({...common(t),shape:'none',variant:'label',borderWidth:0,padding:0,titleSize:size,fontSize:19,textColor:t.paper});
const head=(t,size=50)=>({...label(t,size),fontFamily:'garamond'});
const photo=(t)=>({...common(t),fontFamily:'sans',titleSize:20,fontSize:15,imageFit:'contain',borderWidth:0,padding:0,backgroundDisplay:'hover'});
function edit(i,x,y,width,height,style={},extra={}){
 const n=base.nodes[i],t=themes[ci(n)];
 const spec={key:ref(i),x,y,width,height,sizeMode:'fixed',childPosition:'manual',placement:'free',unset:['style'],style:{...common(t),...style},...extra};
 if(['container','module','spreadsheet'].includes(n.kind)&&!extra.containerSizes)spec.containerSizes={expanded:{width,height,sizeMode:'fixed'},collapsed:{width:n.collapsed?width:350,height:n.collapsed?height:130,sizeMode:'fixed'}};
 recipe.nodes.push(spec);return spec;
}
function add(key,c,x,y,width,height,title,text,style,extra={}){
 const spec={key,canvas:canvasRef(c),kind:'media',title,text,x,y,width,height,sizeMode:'fixed',childPosition:'manual',placement:'free',style:{...common(themes[c]),...style},...extra};recipe.nodes.push(spec);return key;
}
const originalCaptions=[];
function caption(i,c,x,y,width,height,parent){const n=base.nodes[i];if(!n.text)return;add(`visual/caption/${i}`,c,x,y,width,height,'',n.text,{...label(themes[c],16),fontSize:16,textColor:themes[c].muted},parent?{parent}:{});originalCaptions.push({originalId:n.id,captionKey:`visual/caption/${i}`});}
const artByCanvas=new Map();
async function art(key,objectId,c,x,y,width,height,title,extra={}){
 const asset=await mediaAsset(objectId);if(!recipe.assets.some(a=>a.key===asset.key))recipe.assets.push(asset);
 const metadata=media.items.find(m=>m.objectId===objectId),credit=await artCredit(objectId);
 add(key,c,x,y,width,height,title??metadata.title,credit,photo(themes[c]),{contentType:'asset',asset:asset.key,...extra});
 artByCanvas.set(c,[...(artByCanvas.get(c)??[]),{key,objectId,credit}]);return key;
}
function originalRoute(linkIndex,points,sourceAnchor,targetAnchor){const l=base.links[linkIndex];recipe.links.push({key:`link/${l.id}`,points,sourceAnchor,targetAnchor,style:{color:themes[base.canvases.findIndex(c=>c.id===l.canvasId)].accent,width:2,lineStyle:'solid'}});}
function routed(linkIndex,via,sourceAnchor,targetAnchor){const l=base.links[linkIndex];recipe.routes.push({key:`link/${l.id}`,source:ref(byId.get(l.sourceId)),target:ref(byId.get(l.targetId)),parent:originalNav.linkParent(l)?.id??base.canvases.find(c=>c.id===l.canvasId).rootId,via,sourceAnchor,targetAnchor,style:{color:themes[base.canvases.findIndex(c=>c.id===l.canvasId)].accent,width:2,lineStyle:'solid'}});}
for(const [i,c]of base.canvases.entries())recipe.canvases.push({key:canvasRef(i),style:{background:themes[i].bg,gridVisible:false,portalStyle:i%2?'passage':'doorway',portalColor:themes[i].accent,backgroundPattern:'none'}});

// 01 — An exhibition foyer, with the user's five-object comparison preserved.
{
 const t=themes[0];
 edit(5,50,30,1000,36,label(t,17));edit(6,50,95,1010,200,head(t,72));edit(7,55,315,940,115,{...label(t,25),textColor:t.text});
 edit(9,1170,75,360,270,photo(t));caption(9,0,1170,360,360,90);
 edit(74,45,465,1490,265,{fill:t.fill,border:t.edge,childPadding:8},{containerVariant:'horizontal',collapsed:false,grid:{columns:4,rows:1,cellWidth:370,cellHeight:245,gap:0,showHeaders:false,fillOrder:'row'}});
 for(const [j,i]of [10,11,12,13].entries())edit(i,53+j*368.5,473,368.5,249,{...common(t),fill:['#203B41','#243A4C','#403B31','#3F3544'][j],titleSize:24,fontSize:19,textAlign:'center',borderWidth:0,contentGap:6,portalColor:[t.accent,'#7DB0C3','#D09C63','#C49BB0'][j],padding:8});
 edit(8,45,805,1490,350,{fill:'#183039',border:'#526C6E',childPadding:4},{containerVariant:'horizontal',collapsed:false,grid:{columns:5,rows:1,cellWidth:296.4,cellHeight:340,gap:0,showHeaders:false,fillOrder:'row'}});
 add('visual/comparison-heading',0,50,755,1480,40,'FIVE VIEWS OF ZEUS & JUPITER','',label(t,24));
 for(const [j,i]of [82,83,84,85,86].entries()){edit(i,49+j*296.4,809,296.4,342,{...photo(t),borderWidth:1,border:'#435E64',titleSize:18,padding:8});caption(i,0,52+j*296.4,1170,286,165);}
 edit(75,45,1380,1490,210,{fill:'#19313A',borderWidth:1,childPadding:10},{containerVariant:'window',collapsed:false,grid:{columns:1,rows:3,cellWidth:1450,cellHeight:180,windowColumns:1,windowRows:1,scrollDirection:'vertical',showHeaders:false,gap:0}});
 edit(14,55,1390,1450,180,{...label(t,24),fontSize:19,textColor:t.text},{cell:{column:1,row:1}});
 edit(15,55,1570,1450,180,{...common(t),titleSize:20,fontSize:16},{cell:{column:1,row:2}});
 // Original unlabeled snapped relationships remain real links without changing their semantics.
 // Existing snapped relationships retain their complete original shape.
}

// 02 — A readable pedigree. Each source passage and alternative is kept intact.
{
 const t=themes[1];edit(16,50,30,1490,36,label(t,17));edit(17,50,85,1450,120,head(t,59));
 edit(77,40,230,1660,1030,{fill:'#EDE3CF',border:'#BAA98B',backgroundDisplay:'hover',childPadding:16},{collapsed:false,containerVariant:'contained',childArrangement:'free'});
 edit(18,70,315,405,180,{...label(t,25),fontSize:20,textColor:t.text});
 edit(19,570,320,280,160,{fill:'#E0CCAA',titleSize:34,fontSize:21});edit(20,950,320,280,160,{fill:'#D8E0CB',titleSize:34,fontSize:21});
 edit(21,1335,310,320,275,photo(t));caption(21,1,1310,880,340,65,ref(77));
 edit(22,535,505,740,70,{...head(t,35),textAlign:'center',textColor:t.accent});
 const xs=[75,335,595,855,1115,1375];
 for(const [j,i]of [23,24,25,26,27,28].entries())edit(i,xs[j],650,230,165,{fill:i===28?'#D4C29B':'#F7F0E1',titleSize:29,fontSize:19,border:'#B5A183',backgroundDisplay:i===28?'always':'hover'});
 edit(81,888,600,14,14,{shape:'circle',fill:t.accent,border:t.accent,borderWidth:0,padding:0});
 edit(29,850,970,415,165,{fill:'#DDE1CB',titleSize:34,fontSize:21,border:'#8FA080'});
 edit(30,70,970,675,190,{fill:'#E3DCE6',border:'#AFA0B5',titleSize:26,fontSize:20});
 edit(31,50,1305,285,135,{...common(t),titleSize:21,portalColor:t.accent,backgroundDisplay:'hover'});
 edit(32,400,1305,340,135,{...common(t),titleSize:23,portalColor:'#8F718E',backgroundDisplay:'hover'});
 edit(76,1310,1320,390,165,{titleSize:25,fontSize:18,fill:'#DFD6BE'},{containerVariant:'window',grid:{columns:1,rows:2,cellWidth:650,cellHeight:370,windowColumns:1,windowRows:1,scrollDirection:'vertical',showHeaders:false,gap:0},containerSizes:{expanded:{width:730,height:430,sizeMode:'fixed'},collapsed:{width:390,height:165,sizeMode:'fixed'}},collapsed:true});
 edit(33,1320,1330,650,370,{titleSize:23,fontSize:16},{cell:{column:1,row:1}});
 for(const i of [0,1])originalRoute(i,[{x:i?1090:710,y:490},{x:895,y:490}],{x:.5,y:1},{x:.5,y:0});
 originalRoute(24,[],{x:.5,y:1},{x:.5,y:0});
 for(const [j,i]of [2,3,4,5,6,7].entries())originalRoute(i,[{x:895,y:625},{x:xs[j]+115,y:625}],{x:.5,y:1},{x:.5,y:0});
 routed(8,[{key:'visual/demeter-lineage',x:450,y:895,variant:'pivot'},{key:'visual/demeter-lineage-turn',x:940,y:895,variant:'joint'}],{x:.5,y:1},{x:.22,y:0});
 routed(9,[{key:'visual/zeus-lineage',x:1490,y:855,variant:'pivot'},{key:'visual/zeus-lineage-turn',x:1175,y:855,variant:'joint'}],{x:.5,y:1},{x:.78,y:0});
 await art('visual/family/artifact',257623,1,1750,315,350,470,'A veiled goddess (possibly Demeter)');
 add('visual/family/artifact-caption',1,1750,805,350,90,'Identification remains uncertain.','',{...label(t,16),textColor:t.muted});
}

// 03 — Narrative time and remembered time occupy different physical spaces.
{
 const t=themes[2];edit(34,50,30,1450,36,label(t,17));edit(35,50,85,1450,100,head(t,60));edit(36,55,200,1500,75,{...label(t,25),textColor:t.muted});
 [37,38,39,40].forEach((i,j)=>edit(i,50,330+j*205,345,180,{fill:i===39?'#4F4431':t.fill,border:i===39?t.accent:t.edge,titleSize:26,fontSize:20,backgroundDisplay:i===39?'always':'hover'}));
 edit(41,465,330,1170,765,{fill:'#18383E',border:'#587C78',childPadding:16},{collapsed:false,containerVariant:'contained',childArrangement:'free'});
 edit(42,495,415,325,190,{titleSize:30,fontSize:21,fill:'#24474B',backgroundDisplay:'hover'});edit(43,880,415,325,190,{titleSize:30,fontSize:21,fill:'#24474B',backgroundDisplay:'hover'});
 edit(44,880,700,325,195,{titleSize:28,fontSize:21,fill:'#24474B',backgroundDisplay:'hover'});edit(45,495,700,325,195,{titleSize:28,fontSize:21,fill:'#24474B',backgroundDisplay:'hover'});
 edit(46,1255,415,340,420,photo(t));caption(46,2,1255,855,340,185,ref(41));
 edit(47,495,930,720,145,{...label(t,23),fontSize:18,textColor:t.muted});
 add('visual/narrative-frame-label',2,475,290,1160,35,'THE REMEMBERED VOYAGE  /  BOOKS 9–12','',label(t,20));
 edit(48,50,1170,300,135,{titleSize:23,portalColor:t.accent,backgroundDisplay:'hover'});
 edit(78,1265,1175,370,165,{titleSize:24,fontSize:18,fill:'#213E42'},{containerVariant:'window',collapsed:true,grid:{columns:1,rows:2,cellWidth:720,cellHeight:400,windowColumns:1,windowRows:1,scrollDirection:'vertical',showHeaders:false,gap:0},containerSizes:{expanded:{width:800,height:460,sizeMode:'fixed'},collapsed:{width:370,height:165,sizeMode:'fixed'}}});
 edit(49,1275,1185,720,400,{titleSize:23,fontSize:17},{cell:{column:1,row:1}});
 // Reuse the three existing utilities as the unobtrusive path to the source window.
 edit(87,219,1090,12,12,{shape:'circle',fill:t.accent,borderWidth:0,padding:0});edit(88,750,1090,12,12,{shape:'circle',fill:t.accent,borderWidth:0,padding:0});edit(89,750,1223,20,6,{shape:'rectangle',fill:t.accent,borderWidth:0,padding:0});
 for(const i of [10,11,12])originalRoute(i,[],{x:.5,y:1},{x:.5,y:0});
 originalRoute(13,[],{x:1,y:.5},{x:0,y:.5});originalRoute(14,[],{x:.5,y:1},{x:.5,y:0});originalRoute(15,[],{x:0,y:.5},{x:1,y:.5});
 routed(16,[{key:'visual/narration',x:430,y:815,variant:'joint'},{key:'visual/narration-top',x:430,y:365,variant:'pivot'}],{x:1,y:.5},{x:0,y:.2});
 for(const i of [25,26,27,28])originalRoute(i,[],{x:.5,y:.5},{x:.5,y:.5});
 await art('visual/odyssey/frieze',248205,2,470,1140,680,235,'Muses and Sirens — a Roman funerary frieze');
 add('visual/odyssey/frieze-label',2,475,1385,1150,75,'AN IMAGE HAS ITS OWN CONTEXT','Roman sarcophagus, third quarter of the third century CE. Musical rivalry in a funerary setting; this is not a book illustration for Homer.',{...label(t,17),fontSize:16,textColor:t.muted});
}

// 04 — Correspondence is a comparison, never a flattened equivalence.
{
 const t=themes[3];edit(50,50,30,1530,36,label(t,17));edit(51,50,90,760,170,head(t,59));edit(52,900,85,695,170,{...label(t,26),fontSize:22,textColor:t.text});
 edit(53,55,330,265,355,photo(t));caption(53,3,55,700,265,95);edit(54,365,360,330,250,{fill:'#D8DED0',border:'#A1A992',titleSize:38,fontSize:23});edit(55,880,360,350,250,{fill:'#E2D1AC',border:'#AC966E',titleSize:38,fontSize:22});edit(56,1280,330,310,355,photo(t));caption(56,3,1280,700,310,100);
 edit(57,55,890,265,320,photo(t));caption(57,3,55,1225,265,110);edit(58,365,925,330,225,{fill:'#D8DED0',titleSize:35,fontSize:22});edit(59,880,925,350,225,{fill:'#E2D1AC',titleSize:35,fontSize:22});edit(60,1280,925,310,260,{fill:'#DED7E3',titleSize:27,fontSize:21,borderWidth:0});
 edit(61,50,1330,300,165,{titleSize:23,portalColor:t.accent,backgroundDisplay:'hover'});
 edit(79,1230,1330,370,165,{titleSize:24,fontSize:18,fill:'#DFD2BB'},{containerVariant:'window',collapsed:true,grid:{columns:1,rows:2,cellWidth:720,cellHeight:400,windowColumns:1,windowRows:1,scrollDirection:'vertical',showHeaders:false,gap:0},containerSizes:{expanded:{width:800,height:460,sizeMode:'fixed'},collapsed:{width:370,height:165,sizeMode:'fixed'}}});
 edit(62,1240,1340,720,400,{titleSize:23,fontSize:17},{cell:{column:1,row:1}});
 routed(17,[{key:'visual/greek-roman-zeus',x:785,y:485,variant:'joint'}],{x:1,y:.5},{x:0,y:.5});routed(18,[{key:'visual/greek-roman-athena',x:785,y:1038,variant:'joint'}],{x:1,y:.5},{x:0,y:.5});
 add('visual/comparison/lane1',3,365,290,860,35,'GREEK DIVINE IDENTITY    ↔    ROMAN CIVIC SETTING','',label(t,19));
 add('visual/comparison/lane2',3,365,845,860,35,'SHARED ATTRIBUTES    ≠    IDENTICAL INSTITUTIONS','',label(t,19));
 // A compact Gallery makes reception available without shrinking the comparison lanes.
 add('visual/reception-gallery',3,1710,330,460,800,'MYTH IN A ROMAN INTERIOR','',common(t),{kind:'container',containerVariant:'gallery',childLayout:'contained',collapsed:false});
 await art('visual/roman-fresco',250945,3,1720,340,440,720,'Perseus and Andromeda — Roman wall painting',{parent:'visual/reception-gallery'});
 await art('visual/paris-painting',816618,3,1720,340,440,720,'Judgment of Paris — Turchi, about 1640',{parent:'visual/reception-gallery'});
 await art('visual/paris-drawing',338084,3,1720,340,440,720,'Judgment of Paris — Allori, 1583',{parent:'visual/reception-gallery'});
 add('visual/reception-note',3,1710,1170,460,210,'OBJECT, STORY, AUDIENCE','Roman fresco from Boscotrecase; then two early modern interpretations of the Judgment of Paris. The Gallery compares separate contexts, not contemporary evidence of mythic events.',{...label(t,20),fontSize:18,textColor:t.muted});
}

// 05 — Follow the consequences, with the mother's return left visibly incomplete.
{
 const t=themes[4];edit(63,50,30,1650,36,label(t,17));edit(64,50,90,1000,180,head(t,58));edit(65,1120,90,550,160,{...label(t,25),textColor:t.muted});
 edit(66,50,360,370,190,{fill:'#34434A',titleSize:32,fontSize:22,backgroundDisplay:'hover'});edit(67,1200,360,400,190,{fill:'#473848',titleSize:32,fontSize:22,backgroundDisplay:'hover'});
 edit(68,490,320,620,550,photo(t));caption(68,4,495,885,610,125);
 edit(69,50,700,370,190,{fill:'#34434A',titleSize:32,fontSize:22,backgroundDisplay:'hover'});edit(70,1200,700,400,190,{fill:'#504338',titleSize:32,fontSize:22,backgroundDisplay:'hover'});
 edit(71,465,1100,690,250,{fill:'#484639',border:'#B09C70',titleSize:35,fontSize:22});edit(72,50,1150,300,135,{titleSize:23,portalColor:t.accent,backgroundDisplay:'hover'});
 edit(80,1230,1165,370,165,{titleSize:24,fontSize:18,fill:'#353D43'},{containerVariant:'window',collapsed:true,grid:{columns:1,rows:2,cellWidth:720,cellHeight:400,windowColumns:1,windowRows:1,scrollDirection:'vertical',showHeaders:false,gap:0},containerSizes:{expanded:{width:800,height:460,sizeMode:'fixed'},collapsed:{width:370,height:165,sizeMode:'fixed'}}});
 edit(73,1240,1175,720,400,{titleSize:23,fontSize:17},{cell:{column:1,row:1}});
 originalRoute(19,[],{x:.5,y:1},{x:.5,y:0});originalRoute(20,[],{x:1,y:.5},{x:0,y:.24});originalRoute(21,[],{x:0,y:.5},{x:1,y:.24});
 routed(22,[{key:'visual/famine-bottom',x:235,y:1040,variant:'pivot'},{key:'visual/negotiation-bottom',x:1400,y:1040,variant:'joint'}],{x:.5,y:1},{x:.5,y:1});
 routed(23,[{key:'visual/divided-year-turn',x:1190,y:1000,variant:'pivot'},{key:'visual/divided-year',x:1190,y:1225,variant:'joint'}],{x:0,y:1},{x:1,y:.5});
 add('visual/demeter-gallery',4,1700,320,470,670,'DEMETER & ELEUSIS  /  IMAGES IN CONTEXT','',common(t),{kind:'container',containerVariant:'gallery',childLayout:'contained',collapsed:false});
 await art('visual/demeter-veiled',257623,4,1710,330,450,590,'A veiled goddess — possibly Demeter',{parent:'visual/demeter-gallery'});
 await art('visual/eleusis-relief',248899,4,1710,330,450,590,'Eleusinian Relief — fragments in a cast',{parent:'visual/demeter-gallery'});
 add('visual/demeter-gallery-note',4,1700,1020,470,300,'IMAGES PRESERVE THEIR OWN UNCERTAINTIES','The veiled head is not securely identified as Demeter. The Eleusinian Relief is a reconstruction incorporating ancient fragments. Neither object fixes one single version of the hymn.',{...head(t,30),fontSize:23,textColor:t.muted});
}

// New image provenance is readable inside the existing source windows.
for(const [c,items]of artByCanvas){
 const sourceIndex=[75,76,78,79,80][c],source=base.nodes[sourceIndex],sourceSpec=recipe.nodes.find(n=>n.key===ref(sourceIndex));
 const row=c===0?3:2,credit=items.map(item=>item.credit).join('\n\n————————————————\n\n');
 add(`visual/new-image-credits/${c}`,c,sourceSpec.x+10,sourceSpec.y+400,sourceSpec.grid.cellWidth,sourceSpec.grid.cellHeight,'Additional image provenance',credit,{...common(themes[c]),titleSize:24,fontSize:17},{parent:ref(sourceIndex),cell:{column:1,row}});
}
// The narrative source utilities and original snapped links all keep original labels and endpoints.
const result=await stageRecipe(base,recipe,'visual');
await writeJSON(path.join(OUT,'visual','editorial-notes.json'),{strategy:'Five distinct comparative compositions; existing prose, source objects and identities preserved.',newMuseumObjects:[...new Set([...artByCanvas.values()].flat().map(item=>item.objectId))],originalCaptions,sourceWindows:[75,76,78,79,80].map(i=>base.nodes[i].id),proof:'Capture all five actual canvases, Gallery paging and expanded source Windows.'});
console.log(JSON.stringify({directory:result.directory,...result.audit}));
