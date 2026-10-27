/** Identity-preserving editorial renewal of the 48-canvas source collection. */
import {baseline,projectIds,originalBindings,mediaAsset,artCredit,loadMedia,stageRecipe,writeJSON,OUT} from './shared.mjs';
import path from 'node:path';
const base=await baseline(projectIds.gods),nodes=new Map(base.nodes.map(n=>[n.id,structuredClone(n)]));
const recipe={version:1,bindings:originalBindings(base),canvases:[],nodes:[],assets:[],links:[],routes:[]};
const edits=new Map(),linkEdits=new Map(),usedAssets=new Set(),report=[];
const children=id=>[...nodes.values()].filter(n=>n.parentId===id);
const key=n=>`node/${typeof n==='string'?n:n.id}`;
const themes={paper:{background:'#F3EDDF',fill:'#FCF8EF',ink:'#253D42',muted:'#65716B',border:'#CCBFA8',accent:'#A57543'},sea:{background:'#112932',fill:'#1A3841',ink:'#F0E9DC',muted:'#B1C5BE',border:'#406167',accent:'#87C4B7'},clay:{background:'#F0E3D5',fill:'#FFF7EB',ink:'#48362D',muted:'#786553',border:'#CBA88D',accent:'#B16C49'},wine:{background:'#29242D',fill:'#39303D',ink:'#F2E8D9',muted:'#C7B8C5',border:'#756175',accent:'#D5B17C'}};
const theme=c=>/Odyssey/.test(c.title)?themes.sea:/Iliad/.test(c.title)?themes.clay:/Rome|Variant/.test(c.title)?themes.wine:themes.paper;
const style=(p,extra={})=>({variant:'card',shape:'rectangle',radius:0,fill:p.fill,border:p.border,textColor:p.ink,accent:p.accent,fontFamily:'sans',fontSize:18,titleSize:23,textAlign:'left',contentSizing:'fit',padding:10,contentGap:8,childPadding:8,...extra});
function edit(n,fields){if(typeof n==='string')n=nodes.get(n);if(!n||n.kind==='canvas')return;let spec=edits.get(n.id);if(!spec){spec={key:key(n)};edits.set(n.id,spec);recipe.nodes.push(spec);}for(const [k,v]of Object.entries(fields)){if(k==='style'){spec.style={...spec.style,...v};n.style={...n.style,...v};}else if(k==='unset'){spec.unset=[...new Set([...(spec.unset??[]),...v])];for(const field of v){if(field.startsWith('style.'))delete n.style?.[field.slice(6)];else delete n[field];}}else{spec[k]=structuredClone(v);if(k==='parent')n.parentId=v===null?n.canvasId:v;else n[k]=structuredClone(v);}}}
function add(c,k,fields){const spec={key:`renewal/${c.id}/${k}`,canvas:c.id,...fields};recipe.nodes.push(spec);return spec.key;}
function rect(n,x,y,width,height,fields={}){edit(n,{x,y,width,height,sizeMode:'fixed',childPosition:'manual',...fields});}
function frame(n,x,y,width,height,{collapsed=false,pattern='contained',...fields}={}){rect(n,x,y,collapsed?Math.min(width,420):width,collapsed?160:height,{collapsed,containerVariant:pattern,childLayout:'contained',childArrangement:pattern==='contained'?'free':pattern==='horizontal'?'row':'column',containerSizes:{expanded:{width,height,sizeMode:'fixed'},collapsed:{width:Math.min(width,420),height:160,sizeMode:'fixed'}},...fields});}
function label(c,k,title,text,x,y,w,h,p,size=24){return add(c,k,{kind:'text',title,text,x,y,width:w,height:h,sizeMode:'fixed',style:style(p,{variant:'label',shape:'none',titleSize:size,fontSize:18,fontFamily:'garamond',padding:0,contentSizing:'fixed'})});}
async function asset(id){if(!usedAssets.has(id)){recipe.assets.push(await mediaAsset(id));usedAssets.add(id);}return `met-${id}`;}
async function artwork(c,id,x,y,w,h,p,k=`art-${id}`){
 const item=(await loadMedia()).items.find(i=>i.objectId===id),a=await asset(id);
 const image=add(c,k,{kind:'media',title:item.title,contentType:'asset',asset:a,x,y,width:w,height:h*.69,sizeMode:'fixed',collapsed:true,hyperlinks:[{url:item.sourceUrl,label:'Museum object and full credit'}],style:style(p,{shape:'none',variant:'label',imageFit:'contain',titleSize:15,padding:0})});
 add(c,`${k}-caption`,{kind:'text',title:`${item.culture} · ${item.date}`,text:`${item.caption}\nMet ${item.accession} · CC0`,x,y:y+h*.69+8,width:w,height:h*.31-8,sizeMode:'fixed',style:style(p,{variant:'label',shape:'none',titleSize:14,fontSize:14,textColor:p.muted,padding:0})});
 add(c,`${k}-credit`,{kind:'text',parent:image,title:'Object record and attribution',text:await artCredit(id),x:x+w+65,y,width:480,height:400,sizeMode:'fixed',style:style(p,{fontSize:17,titleSize:23})});return image;
}
function pack(n,x,y,w,h,p,{collapsed=false,columns}={}){
 const kids=children(n.id);if(!kids.length){rect(n,x,y,w,h,{...(['text','media'].includes(n.kind)?{style:{contentSizing:'fixed',fontSize:n.title==='Hephaestus and incompatible family memories'?15:16,titleSize:n.title==='Hephaestus and incompatible family memories'?18:20}}:{})});return;}
 if(!['container','module','spreadsheet'].includes(n.kind)){rect(n,x,y,w,h,{collapsed:true});return;}
 const pattern=['horizontal','vertical'].includes(n.containerVariant)?n.containerVariant:'contained';frame(n,x,y,w,h,{collapsed,pattern,style:style(p,{backgroundDisplay:'hover',contentSizing:'fixed',titleSize:16})});
 const cols=pattern==='horizontal'?kids.length:pattern==='vertical'?1:columns??(kids.length===1?1:Math.min(3,kids.length));const rows=Math.ceil(kids.length/cols),gap=18,pad=10,head=Math.max(55,Math.ceil(n.title.length/(w/13))*28+16);
 for(const [i,ch]of kids.entries()){
  const cw=(w-2*pad-(cols-1)*gap)/cols,chh=(h-2*pad-head-(rows-1)*gap)/rows;
  pack(ch,x+pad+i%cols*(cw+gap),y+pad+head+Math.floor(i/cols)*(chh+gap),cw,chh,p,{collapsed:false});
 }
}
function nav(n,x,y,w,h,p){rect(n,x,y,w,h,{style:style(p,{titleSize:16,fontSize:15,textAlign:'center',backgroundDisplay:'hover',portalColor:p.accent})});}
function heading(c,p){
 const roots=children(c.rootId),h=roots.find(n=>n.kind==='text'&&n.y<200)??roots.find(n=>n.kind==='text');
 if(h)rect(h,65,60,(h.text?.length??0)>500?1425:1120,205,{style:style(p,{variant:'label',shape:'none',fontFamily:'garamond',titleSize:(h.text?.length??0)>500?32:h.title.length>65?36:46,fontSize:(h.text?.length??0)>500?18:21,padding:0,contentSizing:'fixed'})});
 label(c,'eyebrow','ATLAS MERIDIAN  /  GREEK MYTHOLOGY','',65,20,1150,28,p,14);
 return h;
}
for(const c of base.canvases){const p=theme(c);recipe.canvases.push({key:`canvas/${c.id}`,style:{background:p.background,gridVisible:false,portalColor:p.accent,portalStyle:/Odyssey/.test(c.title)?'wormhole':'doorway'}});for(const n of [...nodes.values()].filter(n=>n.canvasId===c.id&&n.kind!=='canvas'))edit(n,{sizeMode:'fixed',style:style(p,{...(n.kind==='image'?{titleSize:16,imageFit:'contain'}:{}),...(['joint','junction','input','output','switch'].includes(n.kind)?{fontSize:16,titleSize:18}:{})})});}

// Dossiers: a reading spread with two source tellings and a restrained artifact lane.
const profileArt=[[/Apollo/,247971],[/Artemis/,244854],[/Hermes/,246679],[/Aphrodite/,254697],[/Dionysus/,255973],[/Demeter/,257623],[/Poseidon/,250547],[/Hera|Hestia/,248205],[/Hecate|Hades|Cronus|Gaia|Chaos|Titans|Atlas|Ares/,248205]];
for(const c of base.canvases.filter(c=>/^\d\d ·/.test(c.title)&&Number(c.title.slice(0,2))>=3)){
 const p=theme(c),h=heading(c,p),roots=children(c.rootId),stories=roots.find(n=>n.kind==='container'&&/Stories and reading/.test(n.title));
 const navs=roots.filter(n=>n.kind==='canvas-link');navs.forEach((n,i)=>nav(n,i?580:65,i?1100:285,i?400:235,i?185:105,p));
 if(stories){frame(stories,65,435,970,620,{style:style(p,{backgroundDisplay:'hover',contentSizing:'fixed'})});const parts=children(stories.id),intro=parts.find(n=>n.kind==='text'),accounts=parts.filter(n=>n.kind==='container');
  if(intro)rect(intro,85,495,925,110,{style:style(p,{variant:'label',shape:'none',titleSize:22,fontSize:17,contentSizing:'fixed'})});
  accounts.forEach((n,i)=>pack(n,85+i*465,625,445,410,p));
  label(c,'tellings-label','TWO TELLINGS · READ ACROSS THE SOURCES','',65,390,960,35,p,20);
 }
 const refs=roots.filter(n=>n.kind==='reference');refs.forEach((n,i)=>rect(n,65+i*420,1100,390,185,{style:style(p,{fontSize:15,titleSize:20,backgroundDisplay:'hover'})}));
 const oldImage=roots.find(n=>n.kind==='image'),oldCaption=roots.find(n=>/Object and interpretation/.test(n.title));
 if(oldImage){rect(oldImage,1100,300,350,420,{style:style(p,{variant:'label',shape:'none',titleSize:16})});if(oldCaption)rect(oldCaption,1100,745,350,270,{style:style(p,{variant:'label',shape:'none',titleSize:18,fontSize:15,textColor:p.muted})});}
 else{const id=profileArt.find(([re])=>re.test(c.title))?.[1]??248205;await artwork(c,id,1100,300,350,710,p);}
 const handled=new Set([h?.id,stories?.id,...navs.map(n=>n.id),...refs.map(n=>n.id),oldImage?.id,oldCaption?.id]);let extra=0;for(const n of roots)if(!handled.has(n.id))rect(n,65+(extra++%2)*710,1100+Math.floor((extra-1)/2)*240,670,215);
 report.push({canvas:c.title,strategy:'source-tellings-and-artifact-spread'});
}

// Four-book reading spreads: horizontal narrative order, full accounts and sources.
for(const c of base.canvases.filter(c=>/^(Iliad|Odyssey) · Books/.test(c.title))){const p=theme(c),h=heading(c,p),roots=children(c.rootId),books=roots.filter(n=>n.kind==='container'&&/^Book \d+ · account/.test(n.title));const navs=roots.filter(n=>n.kind==='canvas-link');navs.forEach((n,i)=>nav(n,65+i*310,285,280,105,p));
 const desk=add(c,'book-reading-window',{kind:'container',title:'Read the four books · scroll for the next pair',x:65,y:430,width:1425,height:635,sizeMode:'fixed',collapsed:false,containerVariant:'window',childLayout:'contained',grid:{columns:2,rows:2,cellWidth:700,cellHeight:550,windowColumns:2,windowRows:1,showHeaders:false},style:style(p,{titleSize:22,contentSizing:'fixed'})});
 books.forEach((n,i)=>{edit(n,{parent:desk});pack(n,75+i%2*700,500+Math.floor(i/2)*550,700,550,p);edit(n,{cell:{column:i%2+1,row:Math.floor(i/2)+1}});const child=children(n.id)[0];if(child)edit(child,{style:style(p,{fontSize:16,titleSize:22,contentSizing:'fixed',backgroundDisplay:'hover'})});});
 for(let i=1;i<books.length;i++)recipe.routes.push({key:`renewal/${c.id}/reading-${i}`,canvas:c.id,parent:desk,source:key(books[i-1]),target:key(books[i]),kind:'visual',relation:'chronological',direction:'forward',label:'then',style:{color:p.accent,width:1.5},via:[{key:`renewal/${c.id}/turn-${i}`,x:65+(i%2)*700,y:480+Math.floor(i/2)*550,variant:'pivot'}]});
 const source=roots.find(n=>n.kind==='container'&&/Reading questions/.test(n.title));if(source)pack(source,65,1120,1425,520,p,{collapsed:true,columns:2});
 if(h){edit(h,{backgroundAsset:await asset(248205),style:style(p,{shape:'rectangle',borderWidth:0,backgroundDisplay:'hover',backgroundOpacity:.1,backgroundFit:'cover',fontFamily:'garamond',titleSize:34,fontSize:20,padding:0})});}
 label(c,'frieze-credit','Roman Muses and Sirens · a later visual tradition','The decorative frieze is an artifact of reception, not an illustration of these four books. Met 10.104 · Open Access.',540,1120,950,120,p,17);
 report.push({canvas:c.title,strategy:'four-book-horizontal-sequence-with-readable-accounts'});
}

// Two genealogy maps retain each identity; context launchers disclose from their figures.
for(const c of base.canvases.filter(c=>/^0[12] ·/.test(c.title))){const p=theme(c),h=heading(c,p),roots=children(c.rootId),primary=roots.filter(n=>n.kind==='text'&&n!==h&&n.title.length<45&&n.width<600),primaryIds=new Set(primary.map(n=>n.id));
 const incoming=new Map(primary.map(n=>[n.id,base.links.filter(l=>l.canvasId===c.id&&l.kind==='visual'&&l.relation==='causal'&&l.targetId===n.id&&primaryIds.has(l.sourceId)).map(l=>l.sourceId)]));const depth=new Map();function level(id,seen=new Set()){if(depth.has(id))return depth.get(id);if(seen.has(id))return 0;seen.add(id);const d=Math.min(8,Math.max(-1,...(incoming.get(id)??[]).map(x=>level(x,new Set(seen))))+1);depth.set(id,d);return d;}primary.forEach(n=>level(n.id));
 const layers=new Map();for(const n of primary){const list=layers.get(depth.get(n.id))??[];list.push(n);layers.set(depth.get(n.id),list);}const maxCols=Math.max(...[...layers.values()].map(x=>x.length)),width=Math.max(1500,maxCols*240),positions=new Map();
 for(const [d,list]of layers)list.forEach((n,i)=>{const x=65+(width-list.length*240)/2+i*240,y=360+d*290;rect(n,x,y,215,200,{collapsed:true,style:style(p,{fontSize:15,titleSize:22,backgroundDisplay:'hover'})});positions.set(n.id,{x,y});});
 const context=roots.filter(n=>n.kind==='canvas-link'&&/^Read (the related traditions|Hecate|Zeus|Hera|Poseidon|Athena)/.test(n.title));for(const n of context){const old=base.nodes.find(x=>x.id===n.id);const nearest=primary.reduce((best,x)=>{const original=base.nodes.find(b=>b.id===x.id),distance=Math.hypot(original.x-old.x,original.y-original.height-old.y);return !best||distance<best.distance?{node:x,distance}:best;},null);if(nearest){edit(n,{parent:key(nearest.node)});nav(n,nearest.node.x+245,nearest.node.y+20,260,150,p);}}
 let footer=0;for(const n of roots.filter(n=>n!==h&&!primaryIds.has(n.id)&&!context.includes(n))){const y=405+layers.size*290+Math.floor(footer/4)*220;rect(n,65+(footer++%4)*355,y,330,n.kind==='canvas-link'?130:185,{style:style(p,{fontSize:15,titleSize:19,backgroundDisplay:'hover',portalColor:p.accent})});}
 // A shared route joint communicates each named parent's selected outgoing branch.
 for(const source of primary){const edges=base.links.filter(l=>l.canvasId===c.id&&l.kind==='visual'&&l.relation==='causal'&&l.sourceId===source.id&&primaryIds.has(l.targetId));if(edges.length<3)continue;const j=add(c,`lineage-${source.id}`,{kind:'junction',utilityVariant:'joint',title:`${source.title} · selected descent`,x:source.x+99,y:source.y+225,width:16,height:16,sizeMode:'fixed',style:{fill:p.accent,border:p.accent,textColor:p.ink}});recipe.links.push({key:`renewal/${c.id}/lineage-in-${source.id}`,canvas:c.id,source:key(source),target:j,kind:'visual',relation:'causal',direction:'forward',style:{color:p.accent,width:1.5}});
  for(const edge of edges){const target=nodes.get(edge.targetId),lane=target.y-30;recipe.links.push({key:`link/${edge.id}`,source:j,points:[{x:source.x+107,y:lane},{x:target.x+107,y:lane}],style:{color:p.accent,width:1.3,lineStyle:'solid'}});}
 }
 label(c,'graph-legend','A SELECTED GENEALOGY','Solid routes show selected descent. Alternative traditions remain distinct. Double-click a figure to reveal its retained reading portal; follow sources before merging accounts.',65,270,Math.min(width,1425),65,p,20);
 report.push({canvas:c.title,strategy:'ranked-genealogy-with-real-descent-joints',layers:layers.size,primaryFigures:primary.length});
}

// Entry foyer: compact existing navigation, ornamental artifact paint and readable principles.
{
 const c=base.canvases.find(c=>c.id===base.entryCanvasId),p=theme(c),h=heading(c,p),roots=children(c.rootId),groups=roots.filter(n=>n.kind==='container');
 groups.forEach((n,i)=>{const kids=children(n.id);if(i<2)pack(n,65+i*475,345,440,480,p,{collapsed:false,columns:2});else pack(n,65,880,915,350,p,{collapsed:true,columns:3});});
 const other=roots.filter(n=>n!==h&&!groups.includes(n));let note=0;for(const n of other){if(n.kind==='image')rect(n,1045,610,390,285,{style:style(p,{shape:'none',variant:'label',titleSize:17})});else if(n.kind==='input')rect(n,1090,930,300,95);else rect(n,note?65:1045,note?1090:345,note?1370:390,note?135:230,{style:style(p,{variant:'label',shape:'none',fontSize:17,titleSize:23})}),note++;}
 if(h)edit(h,{backgroundAsset:await asset(248205),style:{shape:'rectangle',variant:'card',borderWidth:0,backgroundFit:'cover',backgroundOpacity:.1,backgroundDisplay:'always'}});
 label(c,'entrance-note','FOLLOW A RELATIONSHIP. OPEN A SOURCE.','Expand a subject group, follow a portal, or inspect a museum object and its source.',65,280,1370,68,p,20);
 report.push({canvas:c.title,strategy:'compact-three-domain-editorial-foyer'});
}

// Remaining reading canvases use a clear main sequence plus specialized reference registers.
for(const c of base.canvases.filter(c=>!report.some(r=>r.canvas===c.title))){const p=theme(c),h=heading(c,p),roots=children(c.rootId),navs=roots.filter(n=>n.kind==='canvas-link'&&/directory|overview|Back|Continue|Next|Previous/i.test(n.title));navs.forEach((n,i)=>nav(n,65+i*290,285,260,110,p));const rest=roots.filter(n=>n!==h&&!navs.includes(n));
 if(c.title==='Gods · Dossier directory'){
  const headings=rest.filter(n=>n.kind==='text'&&!/^Reading principle/.test(n.title)),sections=[];for(const section of headings){const group=rest.filter(n=>n.y>section.y&&n.y<(headings[headings.indexOf(section)+1]?.y??2400)&&n.kind==='canvas-link');sections.push({heading:section,members:group});}
  const special=rest.find(n=>n.kind==='container');if(special)sections.splice(2,0,{heading:null,members:[special]});
  const window=add(c,'dossier-window',{kind:'container',title:'Browse the dossier chapters',x:65,y:445,width:1415,height:800,sizeMode:'fixed',collapsed:false,containerVariant:'window',childLayout:'contained',grid:{columns:1,rows:sections.length,cellWidth:1390,cellHeight:380,windowColumns:1,windowRows:2,showHeaders:false,gap:0},style:style(p,{childPadding:8})});const assigned=new Set();
  for(const [i,s]of sections.entries()){const y=455+i*380,g=add(c,`chapter-${i}`,{kind:'container',parent:window,title:s.heading?.title??special.title,x:75,y,width:1390,height:380,sizeMode:'fixed',collapsed:false,childLayout:'contained',containerVariant:'contained',cell:{column:1,row:i+1},style:style(p,{backgroundDisplay:'hover'})});if(s.heading){edit(s.heading,{parent:g});rect(s.heading,95,y+10,1330,65,{style:style(p,{variant:'label',shape:'none',titleSize:28,fontSize:17})});assigned.add(s.heading.id);}s.members.forEach((n,j)=>{edit(n,{parent:g});if(n===special)pack(n,95,y+20,1330,325,p,{collapsed:false});else nav(n,95+j%3*435,y+100+Math.floor(j/3)*130,410,120,p);assigned.add(n.id);});}
  const notes=rest.filter(n=>!assigned.has(n.id)),frameAlias=add(c,'reading-principles',{kind:'container',title:'Reading principles and scope',text:'Open for source qualifications and the limits of the selected divine roster.',x:65,y:1300,width:1415,height:160,sizeMode:'fixed',collapsed:true,childLayout:'contained',containerVariant:'contained',style:style(p)});notes.forEach((n,i)=>{edit(n,{parent:frameAlias});rect(n,85+i%2*690,1500+Math.floor(i/2)*240,665,215);});
 }else if(/Museum and image/.test(c.title)){
  const oldImages=rest.filter(n=>n.kind==='image'),captions=rest.filter(n=>n.kind==='text');oldImages.forEach((n,i)=>rect(n,65+i%3*350,445+Math.floor(i/3)*465,320,285,{style:style(p,{shape:'none',variant:'label',titleSize:15})}));captions.forEach((n,i)=>rect(n,65+i%3*350,740+Math.floor(i/3)*465,320,155,{style:style(p,{variant:'label',shape:'none',titleSize:17,fontSize:14,textColor:p.muted})}));
  const gallery=add(c,'museum-gallery',{kind:'container',title:'More ways of picturing the gods',x:1145,y:445,width:480,height:885,sizeMode:'fixed',collapsed:false,containerVariant:'gallery',childLayout:'contained',style:style(p)});
  for(const [i,item]of (await loadMedia()).items.entries()){const a=await asset(item.objectId),photo=add(c,`museum-page-${item.objectId}`,{kind:'media',parent:gallery,title:`${item.title} · ${item.date}`,contentType:'asset',asset:a,x:1155,y:455,width:460,height:790,sizeMode:'fixed',collapsed:true,hyperlinks:[{url:item.sourceUrl,label:'Museum object record'}],style:style(p,{titleSize:16,imageFit:'contain'})});add(c,`museum-credit-${item.objectId}`,{kind:'text',parent:photo,title:'Attribution and interpretation',text:await artCredit(item.objectId),x:1680,y:460,width:500,height:440,sizeMode:'fixed',style:style(p,{fontSize:17})});}
  label(c,'museum-note','ANCIENT OBJECTS / LATER RECEPTION','Browse the added collection with the arrows. The full source and credit remain available on each image; double-click to reveal the attribution note.',1145,1365,480,150,p,22);
 }else if(/Overview/.test(c.title)){
  const entrances=rest.filter(n=>n.kind==='canvas-link'),narratives=rest.filter(n=>n.kind==='text'&&n.title==='Narrative frame'),support=rest.filter(n=>!entrances.includes(n)&&!narratives.includes(n));entrances.forEach((n,i)=>nav(n,65+i%3*475,445+Math.floor(i/3)*365,445,160,p));narratives.forEach((n,i)=>rect(n,65+i%3*475,615+Math.floor(i/3)*365,445,175,{style:style(p,{variant:'label',shape:'none',fontSize:17,titleSize:17})}));
  const register=add(c,'people-and-reading-window',{kind:'container',title:'People, divine interventions and reading notes',x:65,y:1225,width:1410,height:510,sizeMode:'fixed',collapsed:false,containerVariant:'window',childLayout:'contained',grid:{columns:3,rows:Math.ceil(support.length/3),cellWidth:460,cellHeight:470,windowColumns:3,windowRows:1,showHeaders:false},style:style(p)});support.forEach((n,i)=>{edit(n,{parent:register});rect(n,75+i%3*460,1235+Math.floor(i/3)*470,460,470,{cell:{column:i%3+1,row:Math.floor(i/3)+1},style:style(p,{fontSize:17,titleSize:23})});});
  for(let i=1;i<entrances.length;i++)recipe.routes.push({key:`renewal/${c.id}/movement-${i}`,canvas:c.id,source:key(entrances[i-1]),target:key(entrances[i]),kind:'visual',relation:'chronological',direction:'forward',label:'reading order',style:{color:p.accent,width:1.5},via:[{key:`renewal/${c.id}/sequence-turn-${i}`,x:55+(i%3)*475,y:420+Math.floor(i/3)*365,variant:'pivot'}]});
 }else{
  const columns=/Lab/.test(c.title)?2:3;let y=445;const rows=[];
  rest.forEach((n,i)=>{const row=Math.floor(i/columns),col=i%columns;const w=columns===2?690:450,h=n.kind==='container'?430:n.kind==='document'?450:n.kind==='image'?430:n.kind==='canvas-link'?160:Math.min(670,Math.max(260,120+Math.ceil((n.text?.length??200)/(w/9))*20));rows[row]=Math.max(rows[row]??0,h);const top=445+rows.slice(0,row).reduce((a,b)=>a+b+32,0);if(n.kind==='container')pack(n,65+col*(w+30),top,w,h,p,{collapsed:n.collapsed??false});else rect(n,65+col*(w+30),top,w,h,{style:style(p,{...(n.kind==='canvas-link'?{textAlign:'center',portalColor:p.accent,backgroundDisplay:'hover'}:{}),...(n.kind==='text'?{backgroundDisplay:'hover'}:{})})});});
  if(rest.length>8&&!/Lab/.test(c.title)){const body=add(c,'reference-window',{kind:'container',title:'Read the selected accounts',x:55,y:425,width:1450,height:760,sizeMode:'fixed',collapsed:false,containerVariant:'window',childLayout:'contained',grid:{columns,rows:Math.ceil(rest.length/columns),cellWidth:475,cellHeight:720,windowColumns:columns,windowRows:1,showHeaders:false},style:style(p)});rest.forEach((n,i)=>{if(n.cell)return;edit(n,{parent:body});if(n.kind==='container')pack(n,65+i%columns*475,435+Math.floor(i/columns)*720,475,720,p,{collapsed:n.collapsed??false});else rect(n,65+i%columns*475,435+Math.floor(i/columns)*720,475,720,{cell:{column:i%columns+1,row:Math.floor(i/columns)+1}});});}
 }
 report.push({canvas:c.title,strategy:/Overview/.test(c.title)?'six-movement-sequence-and-character-window':/Dossier directory/.test(c.title)?'thematic-dossier-window':/Museum/.test(c.title)?'museum-exhibition-and-comparison-gallery':'curated-reading-register'});
}
// Unchanged semantic links receive a coherent palette; manually routed links stay explicit.
for(const link of base.links)if(!recipe.links.some(l=>l.key===`link/${link.id}`))recipe.links.push({key:`link/${link.id}`,style:{color:theme(base.canvases.find(c=>c.id===link.canvasId)).accent,width:1.4,lineStyle:link.relation==='causal'||link.relation==='mutual'?'solid':'dashed'},unset:link.points?.length?['points']:[]});
for(const link of recipe.links)if(!link.key.startsWith('link/'))link.style={...link.style,lineStyle:'solid'};
for(const route of recipe.routes)route.style={...route.style,lineStyle:'solid'};
const result=await stageRecipe(base,recipe,'gods');await writeJSON(path.join(OUT,'gods/design-report.json'),{canvases:report,usedMuseumObjects:[...usedAssets],audit:result.audit});console.log(JSON.stringify({canvases:report.length,...result.audit}));
