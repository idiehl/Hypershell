import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {randomUUID,createHash} from 'node:crypto';
import {applyRecipe,inspectProject} from '../../src/core/authoring.ts';
import {commitSnapshots} from '../../src/core/model.ts';
import {validateProject} from '../../src/core/rules.ts';
import {geometryDiagnostics} from '../../integrations/mcp/src/authoring-worker.mjs';
import assert from 'node:assert/strict';
import {navigationIndex} from '../../src/core/navigation.ts';
import {gridGeometry,isEnclosingContainer} from '../../src/core/cells.ts';
const OUT='I:/dev/Meridian/output/mythology-current-rework-20261023/living';
const BASE='I:/dev/Meridian/output/mythology-current-rework-20261023/baseline/3d896c72-36b9-4900-adb6-a68934591233.json';
const base=JSON.parse(await readFile(BASE,'utf8'));
const old=JSON.parse(await readFile('I:/dev/Meridian/output/mythology-refinement-20261023/living/draft.json','utf8'));
const oldAliases=JSON.parse(await readFile('I:/dev/Meridian/output/mythology-refinement-20261023/living/aliases.json','utf8'));
const p=structuredClone(old),before=new Map(base.nodes.map(n=>[n.id,n]));
const reverse=new Map(Object.entries(oldAliases.nodes).map(([k,id])=>[id,k]));
const canon=new Set(base.nodes.filter(n=>/· account & sources$/.test(n.title)).map(n=>n.parentId));
const account=new Map(base.nodes.filter(n=>/· account & sources$/.test(n.title)).map(n=>[n.parentId,n.id]));
const familyChildren=new Map();for(const id of canon){let a=before.get(id).parentId;while(a&&!canon.has(a))a=before.get(a)?.parentId;if(a){const v=familyChildren.get(a)||[];v.push(id);familyChildren.set(a,v)}}
const P={paper:'#F4EEE2',ink:'#263B3D',surface:'#FCF8EF',line:'#C9BDA7',gold:'#9D713D',sea:'#386C70',wine:'#765565',rust:'#9C5941'};
const style={fill:P.surface,border:P.line,textColor:P.ink,accent:P.gold,fontFamily:'palatino-linotype',titleSize:28,fontSize:19,padding:18,childPadding:22,radius:0,contentSizing:'fixed',textAlign:'center',borderWidth:1};
const work=t=>p.canvases.find(w=>w.title===t);
const get=id=>p.nodes.find(n=>n.id===id);
const aliasTo=id=>reverse.get(id)||`source/${id}`;
const fresh=(key,fields)=>{const id=randomUUID();reverse.set(id,key);const n={id,canvasId:work('Genealogy').id,parentId:work('Genealogy').rootId,kind:'media',title:'',x:0,y:0,width:320,height:110,sizeMode:'fixed',childPosition:'manual',placement:'free',collapsed:true,style:{...style},...fields};p.nodes.push(n);return n};
const newWork=(key,title)=>{const id=randomUUID(),rootId=randomUUID();p.canvases.push({id,rootId,title,style:{background:P.paper,gridVisible:false,portalStyle:'doorway',portalColor:P.gold},snapping:{smartAlign:true}});p.nodes.push({id:rootId,canvasId:id,parentId:null,kind:'canvas',title,x:0,y:0,width:1600,height:1000});reverse.set(rootId,`${key}/root`);return p.canvases.at(-1)};
const edge=(key,source,target,fields={})=>{const id=randomUUID();reverse.set(id,key);const e={id,canvasId:get(source).canvasId,parentId:p.canvases.find(w=>w.id===get(source).canvasId).rootId,sourceId:source,targetId:target,kind:'visual',relation:'causal',direction:'forward',sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5},style:{color:P.gold,width:2},...fields};p.links.push(e);return e};
// The old unreviewed family decks displayed a huge isolated name. Their new
// identities are replaced; none of the user's canonical figure IDs are removed.
const removeNew=new Set(Object.entries(oldAliases.nodes).filter(([key])=>key==='curated/families'||key.startsWith('curated/family/')).map(([,id])=>id));
p.nodes=p.nodes.filter(n=>!removeNew.has(n.id));p.links=p.links.filter(e=>!removeNew.has(e.sourceId)&&!removeNew.has(e.targetId)&&!removeNew.has(e.parentId));
for(const [id] of reverse)if(removeNew.has(id))reverse.delete(id);
const genealogy=work('Genealogy');
// Every real family gets one finite relationship diagram, never a recursively
// shrinking wall. Children with families open their own diagram; leaf names
// retain actual References to the canonical figure identity and testimony.
const families=new Map();for(const id of familyChildren.keys())if(familyChildren.get(id).length>=8)families.set(id,newWork(`family/${id}`,before.get(id).title));
const art=new Map([['Zeus','9b34573a-42b7-4739-98cb-bc81e8258bcd'],['Apollo','fc6dcec3-4e28-4bc4-bf02-0ab5af615523'],['Hermes','853fd2fb-6aa9-4f12-8697-39c9406f505f'],['Aphrodite','fb1d7506-0449-4987-8aea-e05b5899a1c9'],['Heracles','157e4687-84aa-4946-bc07-007598dc7a2f'],['Dionysus','44d65d24-bd59-41cc-b8ba-1bb4de9d8b54']]);
for(const [id,w] of families){
 const figure=before.get(id),children=familyChildren.get(id),a=get(account.get(id));
 const parent=fresh(`family/${id}/parent`,{canvasId:w.id,parentId:w.rootId,kind:'reference',title:figure.title,targetId:id,x:80,y:350,width:370,height:130});
 const ncols=children.length>12?3:children.length>4?2:1,rows=Math.ceil(children.length/ncols),visibleRows=Math.min(rows,3);
 const sw=340*ncols+44,sh=340*visibleRows+104;
 const panel=fresh(`family/${id}/children`,{canvasId:w.id,parentId:w.rootId,kind:'container',title:'Children',x:650,y:100,width:sw,height:sh,collapsed:false,containerVariant:'window',childLayout:'contained',childArrangement:'grid',grid:{columns:ncols,rows,cellWidth:340,cellHeight:340,gap:0,windowColumns:ncols,windowRows:visibleRows,showHeaders:false,scrollDirection:'vertical'},containerSizes:{collapsed:{width:450,height:150,sizeMode:'fixed'},expanded:{width:sw,height:sh,sizeMode:'fixed'}},style:{...style,backgroundOpacity:.35}});
 edge(`family/${id}/parentage`,parent.id,panel.id,{label:'parent of'});
 children.forEach((child,i)=>{const c=before.get(child),dest=families.get(child);fresh(`family/${id}/child/${child}`,{canvasId:w.id,parentId:panel.id,kind:dest?'canvas-link':'reference',title:c.title,targetId:dest?.rootId||child,x:672+(i%ncols)*340,y:182+Math.floor(i/ncols)*340,width:340,height:260,cell:{column:i%ncols+1,row:Math.floor(i/ncols)+1},...(dest?{style:{...style,titleSize:25,portalStyle:'doorway',portalColor:P.gold}}:{})})});
 if(a?.assetId)fresh(`family/${id}/account`,{canvasId:w.id,parentId:w.rootId,kind:'document',title:'Tradition',assetId:a.assetId,x:80,y:1180,width:370,height:160,containerSizes:{collapsed:{width:370,height:160,sizeMode:'fixed'},expanded:{width:520,height:850,sizeMode:'fixed'}}});
 const image=art.get(figure.title);if(image)fresh(`family/${id}/art`,{canvasId:w.id,parentId:w.rootId,kind:'image',title:'',assetId:image,x:80,y:570,width:370,height:500,style:{...style,shape:'none',variant:'label',borderWidth:0,imageFit:'contain',backgroundDisplay:'hover',padding:0}});
}
// A finite searchable figure register is an actual Window, with the same body
// scale for every name. It opens beside the origin diagram rather than over it.
const register=fresh('current/figures',{kind:'container',title:'Figures',x:2850,y:750,width:450,height:160,containerVariant:'window',childLayout:'contained',childArrangement:'grid',grid:{columns:3,rows:Math.ceil(canon.size/3),cellWidth:340,cellHeight:340,gap:0,windowColumns:3,windowRows:3,showHeaders:false,scrollDirection:'vertical'},containerSizes:{collapsed:{width:450,height:160,sizeMode:'fixed'},expanded:{width:1064,height:1124,sizeMode:'fixed'}},style:{...style,backgroundOpacity:.3}});
[...canon].sort((a,b)=>before.get(a).title.localeCompare(before.get(b).title)).forEach((id,i)=>{const dest=families.get(id);fresh(`current/register/${id}`,{kind:dest?'canvas-link':'reference',title:before.get(id).title,targetId:dest?.rootId||id,parentId:register.id,x:2872+(i%3)*340,y:832+Math.floor(i/3)*340,width:340,height:260,cell:{column:i%3+1,row:Math.floor(i/3)+1}})});
// Small families disclose in place. The Gallery slot stays finite and readable;
// every member retains a canonical Reference or a portal to its busy family.
const smallRoots=p.nodes.filter(n=>n.kind==='reference'&&familyChildren.has(n.targetId)&&!families.has(n.targetId)&&!before.has(n.id));
const compactFamily=(node,id)=>{
 const members=familyChildren.get(id)||[],cols=members.length>3?2:1,cellW=cols===1?304:148;
 node.kind='container';delete node.targetId;node.containerVariant='contained';node.childLayout='contained';node.childArrangement='free';node.width=340;node.height=300;node.containerSizes={collapsed:{width:340,height:300,sizeMode:'fixed'},expanded:{width:340,height:300,sizeMode:'fixed'}};node.style={...style,titleSize:26,fontSize:18,padding:12,childPadding:12};
 for(const[at,child]of members.entries()){const dest=families.get(child);fresh(`small/${node.id}/${child}`,{canvasId:node.canvasId,parentId:node.id,kind:'media',title:before.get(child).title,remoteLinks:[{id:randomUUID(),targetId:child,relation:'generic',label:'same figure'}],x:node.x+16+(at%cols)*(cellW+8),y:node.y+70+Math.floor(at/cols)*54,width:cellW,height:50,style:{...style,titleSize:18,fontSize:16,padding:5,contentGap:0,variant:'label',shape:'none',borderWidth:0}});}
};
for(const n of smallRoots)compactFamily(n,n.targetId);
// Turn selected genealogy references that have offspring into direct family
// portals. These are new identities; the original actual graph remains intact.
for(const [key,id] of Object.entries(oldAliases.nodes))if(key.startsWith('curated/figure/')){const n=get(id),dest=families.get(n?.targetId);if(n&&dest){n.kind='canvas-link';n.targetId=dest.rootId;n.height=150;n.style={...style,titleSize:28,portalColor:P.gold,portalStyle:'doorway'};}}
// Retained originals are a testimony collection. Every canonical figure gets
// an equal-size source page, with no false connectedness added to the graph.
const lineage=work('Lineage');lineage.title='Testimonia';get(lineage.rootId).title='Testimonia';
const pageDeck=get(oldAliases.nodes['curated/canonical-pages']);pageDeck.title='Testimonia';pageDeck.style={...style,titleSize:30};
// Bounded Galleries intentionally present source objects mutually exclusively.
// Their original ownership and edges stay in the project; primary diagrams
// above provide the legible relationships.
for(const n of p.nodes.filter(n=>n.canvasId===lineage.id&&canon.has(n.id))){n.style={...n.style,titleSize:30,fontSize:19};n.width=350;n.height=112;}
const portal=get(oldAliases.nodes['curated/lineage-portal']);portal.title='Testimonia';portal.x=2850;portal.y=110;portal.width=450;portal.height=200;
// Canonical source identities become equal-sized testimony records. Their
// original parentage edges remain stored, while the complete family diagrams
// above show those relationships at a useful scale. Every original account
// returns to its own figure, so References open a readable original context.
for(const id of canon){const n=get(id),a=get(account.get(id));n.parentId=pageDeck.id;n.x=120;n.y=170;n.width=1100;n.height=700;n.containerVariant='contained';n.childLayout='spatial';n.childArrangement='free';n.collapsed=true;if(a?.assetId){n.contentType='asset';n.assetId=a.assetId;}n.style={...style,titleSize:30};delete n.expansionTray;delete n.containerSizes;if(a){a.parentId=id;a.title='Tradition';a.x=140;a.y=250;a.width=1000;a.height=550;a.collapsed=true;a.style={...style,titleSize:28};}}

// Add subject images outside the route lanes, using existing attributed assets.
for(const[name,asset]of [['Iliad','b8aca55b-7232-4460-91e9-48ee45c8e6bc'],['Odyssey','4bf03bfd-301c-4590-a811-5691ebaf122e'],['Hesiod','e9471ce2-009f-40dc-a63e-dc49213d045f']]){const w=work(name);fresh(`current/${name}/image`,{canvasId:w.id,parentId:w.rootId,kind:'image',title:'',assetId:asset,x:3520,y:100,width:600,height:800,style:{...style,shape:'none',variant:'label',borderWidth:0,padding:0,imageFit:'contain'}})}
// Subject constellations and poetic branches communicate actual membership,
// instead of a disconnected set of page-style reading cards.
{
 const w=work('Myths'),items=p.nodes.filter(n=>n.canvasId===w.id&&n.parentId===w.rootId&&n.kind==='reference'&&reverse.get(n.id)?.startsWith('curated/Myths/'));
 const hub=fresh('current/myths/hub',{canvasId:w.id,parentId:w.rootId,title:'Myths',x:1250,y:720,width:420,height:160,style:{...style,titleSize:44,variant:'label',shape:'none',borderWidth:0}});
 const center={x:1460,y:800},angles=[-130,-80,-30,25,75,125,180];
 items.forEach((n,i)=>{const a=angles[i]*Math.PI/180;n.x=center.x+Math.cos(a)*1100-250;n.y=center.y+Math.sin(a)*650-80;n.width=500;n.height=160;n.style={...style,titleSize:30};edge(`current/myths/topic/${i}`,hub.id,n.id,{kind:'visual',relation:'generic',direction:'none',sourceAnchor:undefined,targetAnchor:undefined,style:{color:P.wine,width:2}})});
 const support=p.nodes.find(n=>reverse.get(n.id)==='curated/support/Myths');if(support)support.y=2000;
}
{
 const w=work('Hesiod'),items=p.nodes.filter(n=>n.canvasId===w.id&&n.parentId===w.rootId&&n.kind==='reference'&&reverse.get(n.id)?.startsWith('curated/Hesiod/'));
 const poems=items.filter(n=>!/^(Themes|Muses)$/.test(n.title));
 const hub=fresh('current/hesiod/hub',{canvasId:w.id,parentId:w.rootId,title:'Hesiod',x:80,y:650,width:450,height:180,style:{...style,titleSize:44}});
 poems.forEach((n,i)=>{n.x=850;n.y=100+i*340;n.width=700;n.height=180;n.style={...style,titleSize:32};edge(`current/hesiod/poem/${i}`,hub.id,n.id,{kind:'visual',relation:'generic',direction:'none',points:[{x:680,y:740},{x:680,y:n.y+90}],style:{color:P.gold,width:2}})});
 const support=p.nodes.find(n=>reverse.get(n.id)==='curated/support/Hesiod');if(support){support.y=1700;for(const n of items.filter(n=>!poems.includes(n))){p.nodes=p.nodes.filter(x=>x.id!==n.id);reverse.delete(n.id);}}
 const img=p.nodes.find(n=>reverse.get(n.id)==='current/Hesiod/image');img.x=1840;img.y=100;img.width=600;img.height=1150;
}

// Readings remain finite reading surfaces rather than giant empty covers.
for(const id of ['bc9c6de5-ae1c-422e-b939-578d7d75b5c7','4e21c21a-7543-4f64-bee1-79e07b57a4a2','f233df1f-f062-4ed2-82be-cb15be4f6813']){const n=get(id);n.height=190;n.style={...style,titleSize:30};n.containerSizes={collapsed:{width:640,height:190,sizeMode:'fixed'},expanded:{width:700,height:900,sizeMode:'fixed'}};}
// Correct original overview heading clipping and keep one deliberate title.
get('a801bc8b-0dd0-4db4-a5f4-936c04c8d2e7').height=220;
// Preserve only material actually relocated from the primary graph. Repeating
// every unchanged epic passage in an extra archive duplicated many megabytes.
const archiveAsset=p.assets.find(a=>a.id===oldAliases.assets['refinement/complete-reading']);
const newAssetTexts=p.assets.filter(a=>a.id!==archiveAsset.id&&!base.assets.some(b=>b.id===a.id)&&/^(?:text\/|application\/json)/.test(a.mime)).map(a=>Buffer.from(a.base64,'base64').toString('utf8')).join('\n\u0000\n');
const relocated=base.nodes.filter(n=>n.text&&get(n.id)?.text!==n.text&&!newAssetTexts.includes(n.text));
archiveAsset.base64=Buffer.from(relocated.map(n=>`## ${n.title||n.tag||n.kind}\n\nIdentity: ${n.id}\n\n${n.text}`).join('\n\n---\n\n')).toString('base64');
// Store positions from each Window's actual expanded interior, including
// its title/padding. Nominal grid dimensions alone leave fractional overlap.
for(const parent of p.nodes.filter(n=>n.containerVariant==='window'&&n.grid)){
 const expanded={...parent,...parent.containerSizes?.expanded,collapsed:false},g=gridGeometry(expanded,p);
 for(const child of p.nodes.filter(n=>n.parentId===parent.id&&n.cell&&n.kind!=='junction')){
  const x=g.left+(child.cell.column-1)*g.cellWidth,y=g.top+(child.cell.row-1)*g.cellHeight,dx=x-child.x,dy=y-child.y;
  const ids=new Set([child.id]);for(let pass=0;pass<25;pass++)for(const n of p.nodes)if(ids.has(n.parentId))ids.add(n.id);
  for(const n of p.nodes)if(ids.has(n.id)){n.x+=dx;n.y+=dy;}
 }
}

// Original semantic routes are repaired with real, persistent Pivot nodes.
// The original edge ID stays on the terminal segment of the same relationship.
const segmentHits=(a,b,r)=>{let lo=0,hi=1,dx=b.x-a.x,dy=b.y-a.y;for(const[q,d]of [[-dx,a.x-r.x],[dx,r.x+r.width-a.x],[-dy,a.y-r.y],[dy,r.y+r.height-a.y]]){if(Math.abs(q)<1e-9){if(d<0)return false;}else{const t=d/q;if(q<0)lo=Math.max(lo,t);else hi=Math.min(hi,t);if(lo>hi)return false;}}return hi>lo+1e-8;};
const repairLog=[];
for(const w of p.canvases){
 const diagnostics=geometryDiagnostics(p,w.id).diagnostics,edgeIds=[...new Set(diagnostics.filter(d=>d.code==='link-crosses-node').map(d=>d.linkId))];if(!edgeIds.length)continue;
 const expanded=structuredClone(p);for(const n of expanded.nodes)if(n.canvasId===w.id){if(n.collapsed&&n.containerSizes?.expanded)Object.assign(n,n.containerSizes.expanded);n.collapsed=false;}
 const nav=navigationIndex(expanded),by=new Map(expanded.nodes.map(n=>[n.id,n]));
 for(const id of edgeIds){
  const e=p.links.find(e=>e.id===id),source=by.get(e.sourceId),target=by.get(e.targetId),sb=nav.bounds(source),tb=nav.bounds(target),excluded=new Set([source.id,target.id]);
  for(const n of [...nav.ancestors(source.id),...nav.ancestors(target.id)])if(isEnclosingContainer(n))excluded.add(n.id);
  const obstacles=expanded.nodes.filter(n=>n.canvasId===w.id&&n.kind!=='canvas'&&n.kind!=='junction'&&nav.isNodeVisible(n.id)&&!excluded.has(n.id)).map(n=>nav.bounds(n));
  const clean=pts=>pts.every((b,i)=>{if(i===0)return true;const blocks=[...obstacles,...(i>1?[sb]:[]),...(i<pts.length-1?[tb]:[])];return!blocks.some(r=>segmentHits(pts[i-1],b,{x:r.x+1,y:r.y+1,width:r.width-2,height:r.height-2}));});
  const anchors=[{x:1,y:.5},{x:0,y:.5},{x:.5,y:0},{x:.5,y:1}],point=(b,a)=>({x:b.x+b.width*a.x,y:b.y+b.height*a.y}),escape=(b,a)=>{const z=point(b,a);return{x:z.x+(a.x===0?-35:a.x===1?35:0),y:z.y+(a.y===0?-35:a.y===1?35:0)}};
  const xs=[...new Set([sb.x-50,sb.x+sb.width+50,tb.x-50,tb.x+tb.width+50,...obstacles.flatMap(r=>[r.x-30,r.x+r.width+30])])];
  const ys=[...new Set([sb.y-50,sb.y+sb.height+50,tb.y-50,tb.y+tb.height+50,...obstacles.flatMap(r=>[r.y-30,r.y+r.height+30])])];
  let selected;
  outer:for(const sa of anchors)for(const ta of e.kind==='visual'?anchors:[{x:0,y:.5}]){
   const a=point(sb,sa),b=point(tb,ta),ae=escape(sb,sa),be=escape(tb,ta);
   const candidates=[[a,ae,{x:be.x,y:ae.y},be,b],[a,ae,{x:ae.x,y:be.y},be,b],...xs.map(x=>[a,ae,{x,y:ae.y},{x,y:be.y},be,b]),...ys.map(y=>[a,ae,{x:ae.x,y},{x:be.x,y},be,b])].sort((a,b)=>{const len=p=>p.slice(1).reduce((s,z,i)=>s+Math.abs(z.x-p[i].x)+Math.abs(z.y-p[i].y),0);return len(a)-len(b)});
   for(const pts of candidates)if(clean(pts)){selected={sa,ta,pts};break outer;}
  }
  if(!selected)throw new Error('No clear saved route for '+id);
  const points=selected.pts.slice(1,-1).filter((p,i,a)=>!i||p.x!==a[i-1].x||p.y!==a[i-1].y),parent=e.parentId||nav.linkParent(e)?.id||w.rootId;
  let prior=e.sourceId;const oldSource=e.sourceId;
  for(const[i,point]of points.entries()){
   const pivot=fresh(`route/${e.id}/pivot/${i}`,{canvasId:w.id,parentId:parent,kind:'junction',utilityVariant:'pivot',title:'',x:point.x-6,y:point.y-6,width:12,height:12,...(e.kind!=='visual'?{valueType:e.kind==='functional'?'boolean':'number'}:{}),style:{fill:e.style?.color||P.gold,border:e.style?.color||P.gold,shape:'circle'}});
   edge(`route/${e.id}/segment/${i}`,prior,pivot.id,{canvasId:w.id,parentId:parent,kind:e.kind,relation:e.relation,direction:'none',sourceAnchor:i?undefined:selected.sa,targetAnchor:undefined,style:e.style});prior=pivot.id;
  }
  e.sourceId=prior;e.parentId=parent;e.targetAnchor=selected.ta;delete e.sourceAnchor;delete e.points;
  repairLog.push({edgeId:id,originalSource:oldSource,target:e.targetId,pivots:points.length});
 }
}
await writeFile(`${OUT}/routing-repairs.json`,JSON.stringify(repairLog,null,2));
// Export the final native state as one typed declarative recipe against r7.
// New aliases are regenerated by the API, while original IDs remain bound.
const workKeys=new Map(p.canvases.map(w=>[w.id,base.canvases.some(b=>b.id===w.id)?'work':`w/${w.id}`]));
const rootKeys=new Map(p.canvases.map(w=>[w.rootId,workKeys.get(w.id)]));
const nodeKeys=new Map(p.nodes.filter(n=>n.kind!=='canvas').map(n=>[n.id,before.has(n.id)?`n/${n.id}`:aliasTo(n.id)]));
const assetKeys=new Map(p.assets.map(a=>[a.id,base.assets.some(b=>b.id===a.id)?`a/${a.id}`:`asset/${a.id}`]));
const linkKeys=new Map(p.links.map(e=>[e.id,base.links.some(b=>b.id===e.id)?`e/${e.id}`:`link/${e.id}`]));
const ref=id=>nodeKeys.get(id)||rootKeys.get(id)||id;
const bindings={canvases:{work:base.canvases[0].id},roots:{work:base.canvases[0].rootId},nodes:Object.fromEntries([...nodeKeys].filter(([id])=>before.has(id)).map(([id,key])=>[key,id])),assets:Object.fromEntries(base.assets.map(a=>[`a/${a.id}`,a.id])),links:Object.fromEntries(base.links.map(e=>[`e/${e.id}`,e.id]))};
const nodeFields=['title','tag','titleRuns','x','y','width','height','positionLocked','sizeMode','containerSizes','retiredBlockContent','contentType','childPosition','childOffset','expansionTray','collapsed','text','textRuns','attachments','hyperlinks','value','valueType','closed','operation','exposed','rules','snapping','style','canvasTarget','containerVariant','utilityVariant','remoteLinks','childLayout','placement','attachSide','grid','cell','targetCell','childArrangement'];
const unsetFields=['titleRuns','text','textRuns','tag','positionLocked','sizeMode','containerSizes','retiredBlockContent','contentType','childPosition','childOffset','expansionTray','collapsed','asset','backgroundAsset','attachments','hyperlinks','remoteLinks','target','targetCell','canvasTarget','dependencies','value','valueType','closed','operation','exposed','rules','snapping','style','containerVariant','utilityVariant','childLayout','placement','attachSide','grid','cell','childArrangement'];
const recipe={version:1,title:p.title,bindings,canvases:p.canvases.map(w=>({key:workKeys.get(w.id),title:w.title,style:w.style,...(w.snapping?{snapping:w.snapping}:{})})),assets:p.assets.filter(a=>!base.assets.some(b=>b.id===a.id)).map(({id,...a})=>({key:assetKeys.get(id),...a})),nodes:[],links:[]};
for(const n of p.nodes){if(n.kind==='canvas')continue;const s={key:nodeKeys.get(n.id),canvas:workKeys.get(n.canvasId),parent:ref(n.parentId),kind:n.kind};for(const key of nodeFields)if(n[key]!==undefined)s[key]=structuredClone(n[key]);if(n.assetId)s.asset=assetKeys.get(n.assetId);if(n.targetId)s.target=n.kind==='reference'?(nodeKeys.get(n.targetId)||n.targetId):ref(n.targetId);if(n.dependencyIds)s.dependencies=n.dependencyIds.map(ref);if(s.attachments)s.attachments=s.attachments.map(id=>assetKeys.get(id)||id);if(s.remoteLinks)s.remoteLinks=s.remoteLinks.map(l=>({...l,targetId:ref(l.targetId)}));if(s.style?.backgroundAssetId){s.backgroundAsset=assetKeys.get(s.style.backgroundAssetId);delete s.style.backgroundAssetId;}if(before.has(n.id)){const old=before.get(n.id);s.unset=unsetFields.filter(key=>key==='asset'?old.assetId&&!n.assetId:key==='target'?old.targetId&&!n.targetId:key==='dependencies'?old.dependencyIds&&!n.dependencyIds:old[key]!==undefined&&n[key]===undefined);s.unset.push('style');if(!s.unset.length)delete s.unset;}recipe.nodes.push(s);}
for(const e of p.links){const s={key:linkKeys.get(e.id),canvas:workKeys.get(e.canvasId),parent:e.parentId?ref(e.parentId):null,source:ref(e.sourceId),target:ref(e.targetId)};for(const k of ['kind','relation','direction','targetPort','sourceAnchor','targetAnchor','points','label','style'])if(e[k]!==undefined)s[k]=e[k];const original=base.links.find(l=>l.id===e.id);if(original){s.unset=['points','style','label','sourceAnchor','targetAnchor','direction','targetPort'].filter(k=>original[k]!==undefined&&e[k]===undefined);if(!s.unset.length)delete s.unset;}recipe.links.push(s);}
await mkdir(OUT,{recursive:true});await writeFile(`${OUT}/recipe.json`,JSON.stringify(recipe));
console.log(JSON.stringify({stage:'apply',nodes:recipe.nodes.length,works:recipe.canvases.length,assets:recipe.assets.length}));
const result=applyRecipe(base,JSON.parse(JSON.stringify(recipe))),draft=commitSnapshots(result.project);assert.deepEqual(validateProject(draft),[]);
await writeFile(`${OUT}/draft.json`,JSON.stringify(draft));await writeFile(`${OUT}/aliases.json`,JSON.stringify(result.aliases,null,2));
const remap=id=>result.aliases.nodes[nodeKeys.get(id)]||result.aliases.roots[rootKeys.get(id)]||id;
const remapWork=id=>result.aliases.canvases[workKeys.get(id)]||id;
const plan=[...old.canvases.filter(w=>w.title!=='Lineage').map(w=>({workId:remapWork(w.id),label:w.title})),{workId:remapWork(lineage.id),label:'Testimonia'},...['Gaia','Zeus','Nereus'].map(title=>{const orig=[...families].find(([id])=>before.get(id).title===title);return{workId:remapWork(orig[1].id),label:title}}),{workId:remapWork(genealogy.id),nodeId:remap(register.id),scope:'node',label:'Figures',expand:[remap(register.id)]}];
await writeFile(`${OUT}/review-plan.json`,JSON.stringify(plan,null,2));
const report={at:new Date().toISOString(),baseRevision:base.revision,sourceHash:createHash('sha256').update(JSON.stringify(draft)).digest('hex'),bytes:Buffer.byteLength(JSON.stringify(draft)),nodes:draft.nodes.length,works:draft.canvases.length,familyWorks:families.size,canonicalFigures:canon.size,originalsRetained:base.nodes.every(n=>draft.nodes.some(a=>a.id===n.id)),note:'Consolidated typed recipe; old identical r7 baseline reconciled; new unreviewed family galleries replaced with finite subject relationship diagrams.'};
await writeFile(`${OUT}/composition.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report));

