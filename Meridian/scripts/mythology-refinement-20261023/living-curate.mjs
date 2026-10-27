/** Curated subject diagrams, with the complete canonical graph retained. */
import{readFile,writeFile}from'node:fs/promises';
import{applyRecipe}from'../../src/core/authoring.ts';
import{commitSnapshots}from'../../src/core/model.ts';
import{createHash}from'node:crypto';
const dir='I:/dev/Meridian/output/mythology-refinement-20261023/living';
const p=JSON.parse(await readFile(`${dir}/draft.json`,'utf8')),aliases=JSON.parse(await readFile(`${dir}/aliases.json`,'utf8'));
const b=JSON.parse(await readFile('I:/dev/Meridian/output/mythology-refinement-20261023/baseline/3d896c72-36b9-4900-adb6-a68934591233.json','utf8'));
const by=new Map(p.nodes.map(n=>[n.id,n])),before=new Map(b.nodes.map(n=>[n.id,n]));
const cs=new Map();for(const n of p.nodes){let a=cs.get(n.parentId)||[];a.push(n);cs.set(n.parentId,a)}
const work=t=>p.canvases.find(w=>w.title===t),canon=new Set(b.nodes.filter(n=>/· account & sources$/.test(n.title)).map(n=>n.parentId));
const P={paper:'#F4EEE2',ink:'#263B3D',surface:'#FCF8EF',line:'#C9BDA7',gold:'#9D713D',sea:'#386C70',wine:'#765565'};
const style={fill:P.surface,border:P.line,textColor:P.ink,accent:P.gold,fontFamily:'palatino-linotype',titleSize:28,fontSize:18,padding:16,childPadding:18,radius:0,contentSizing:'fixed'};
const nodes=[],links=[],routes=[],canvases=[{key:'curated/lineage',title:'Lineage',style:{background:P.paper,gridVisible:false,portalColor:P.gold}}];
const nodeKeys=new Map(Object.entries(aliases.nodes).map(([k,v])=>[v,k])),workKeys=new Map(Object.entries(aliases.canvases).map(([k,v])=>[v,k]));
const nodeKey=id=>nodeKeys.get(id)||id;
const workKey=id=>workKeys.get(id)||id;
const declare=(key,fields)=>{nodes.push({key,...fields});return key};
const update=(id,fields)=>declare(nodeKey(id),fields);
const descendants=id=>{const all=[];const visit=id=>{for(const n of cs.get(id)||[]){all.push(n);visit(n.id)}};visit(id);return all};
const gen=work('Genealogy');
// Preserve the canonical graph as one source Work. Its nodes are not duplicated.
for(const n of p.nodes.filter(n=>n.canvasId===gen.id&&n.kind!=='canvas'))update(n.id,{canvas:'curated/lineage',...(n.parentId===gen.rootId?{parent:'curated/lineage'}:{})});
for(const e of p.links.filter(e=>e.canvasId===gen.id))links.push({key:Object.entries(aliases.links).find(([,v])=>v===e.id)[0],canvas:'curated/lineage',...(e.parentId===gen.rootId?{parent:'curated/lineage'}:{})});
declare('curated/lineage-portal',{canvas:workKey(gen.id),kind:'canvas-link',title:'Lineage',target:'curated/lineage',x:2920,y:100,width:430,height:230,style});

// Every other subject retains its originals behind one plainly named disclosure
// surface below the primary diagram. Opening it cannot obscure the diagram.
const support=[];
for(const name of ['Iliad','Odyssey','Myths','Hesiod','Readings']){
 const w=work(name),roots=(cs.get(w.rootId)||[]),all=roots.flatMap(n=>[n,...descendants(n.id)]);
 const minX=Math.min(...all.map(n=>n.x)),minY=Math.min(...all.map(n=>n.y));
 const maxX=Math.max(...all.map(n=>n.x+(n.containerSizes?.expanded?.width||n.width))),maxY=Math.max(...all.map(n=>n.y+(n.containerSizes?.expanded?.height||n.height)));
 const key=`curated/support/${name}`,y=name==='Iliad'||name==='Odyssey'?1580:1750;
 declare(key,{canvas:workKey(w.id),kind:'container',title:name==='Readings'?'Annotations':'Context',x:80,y,width:450,height:210,collapsed:true,childLayout:'contained',containerVariant:'contained',childArrangement:'free',sizeMode:'fixed',containerSizes:{collapsed:{width:450,height:210,sizeMode:'fixed'},expanded:{width:Math.max(1800,maxX-minX+1200),height:Math.max(1200,maxY-minY+1200),sizeMode:'fixed'}},style});
 for(const n of all)update(n.id,{x:n.x-minX+120,y:n.y-minY+y+120,...(n.parentId===w.rootId?{parent:key}:{})});
 support.push({name,key});
}
// Correct the ordinary-parent packing error in preserved source surfaces:
// original descendants start beyond their own cover, not on top of it.
for(const n of p.nodes.filter(n=>n.expansionTray&&!['container','spreadsheet','module'].includes(n.kind)&&!canon.has(n.id))){
 const children=cs.get(n.id)||[];if(!children.length)continue;
 const minX=Math.min(...children.map(c=>c.x));
 if(minX<n.x+n.width+40){const dx=n.x+n.width+100-minX;for(const c of descendants(n.id)){const existing=nodes.find(s=>s.key===nodeKey(c.id));if(existing)existing.x=(existing.x??c.x)+dx;else update(c.id,{x:c.x+dx});}}
}

// A readable genealogy uses actual Reference identities and true parentage.
const find=t=>b.nodes.find(n=>canon.has(n.id)&&n.title.toLowerCase()===t.toLowerCase());
const chosen=[['Chaos',80,120],['Nyx',620,120],['Erebus',620,420],['Aether',1160,120],['Hemera',1160,420],['Gaia',80,840],['Uranus',620,840],['Cronus',1160,720],['Rhea',1160,1100],['Hestia',1900,120],['Demeter',1900,420],['Hera',1900,720],['Hades',1900,1020],['Poseidon',1900,1320],['Zeus',1900,1620]];
const chart=new Map();for(const[t,x,y]of chosen){const source=find(t);if(!source)throw new Error(t);chart.set(t,declare(`curated/figure/${t}`,{canvas:workKey(gen.id),kind:'reference',title:t,target:source.id,x,y,width:350,height:150,sizeMode:'fixed',collapsed:true,text:'',style}));}
function edge(a,z,points=[]){links.push({key:`curated/parent/${a}/${z}`,canvas:workKey(gen.id),source:chart.get(a),target:chart.get(z),kind:'visual',relation:'causal',direction:'forward',label:'',sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5},points,style:{color:P.gold,width:2}})}
edge('Chaos','Nyx');edge('Chaos','Erebus',[{x:510,y:195},{x:510,y:495}]);edge('Nyx','Aether');edge('Nyx','Hemera',[{x:1040,y:195},{x:1040,y:495}]);edge('Gaia','Uranus');edge('Uranus','Cronus',[{x:1060,y:915},{x:1060,y:795}]);edge('Uranus','Rhea',[{x:1060,y:915},{x:1060,y:1175}]);
declare('curated/olympian-joint',{canvas:workKey(gen.id),kind:'junction',title:'',x:1674,y:915,width:12,height:12,utilityVariant:'joint',style:{fill:P.gold,border:P.gold,shape:'circle'}});
for(const parent of ['Cronus','Rhea'])links.push({key:`curated/pair/${parent}`,canvas:workKey(gen.id),source:chart.get(parent),target:'curated/olympian-joint',kind:'visual',relation:'causal',direction:'none',sourceAnchor:{x:1,y:.5},points:[{x:1600,y:chosen.find(x=>x[0]===parent)[2]+75},{x:1600,y:921}],style:{color:P.gold,width:2}});
for(const child of ['Hestia','Demeter','Hera','Hades','Poseidon','Zeus'])links.push({key:`curated/olympian/${child}`,canvas:workKey(gen.id),source:'curated/olympian-joint',target:chart.get(child),kind:'visual',relation:'causal',direction:'forward',targetAnchor:{x:0,y:.5},points:[{x:1770,y:921},{x:1770,y:chosen.find(x=>x[0]===child)[2]+75}],style:{color:P.gold,width:2}});

// All canonical figures are browsable through finite, nested family Galleries.
// Container pages own their children, so no exterior text tray escapes a cell.
const familyChildren=new Map(),familyRoots=[];
for(const id of canon){let parent=before.get(id)?.parentId;while(parent&&!canon.has(parent))parent=before.get(parent)?.parentId;if(parent){let a=familyChildren.get(parent)||[];a.push(id);familyChildren.set(parent,a)}else familyRoots.push(id)}
declare('curated/families',{canvas:workKey(gen.id),kind:'container',title:'Families',x:2920,y:500,width:450,height:210,collapsed:true,containerVariant:'window',childLayout:'contained',childArrangement:'grid',grid:{columns:2,rows:Math.ceil(familyRoots.length/2),cellWidth:920,cellHeight:680,windowColumns:2,windowRows:1,showHeaders:false,scrollDirection:'vertical'},containerSizes:{collapsed:{width:450,height:210,sizeMode:'fixed'},expanded:{width:1880,height:760,sizeMode:'fixed'}},sizeMode:'fixed',style});
const allFamilyKeys=[];
function family(id,parent,cell){const source=before.get(id),children=familyChildren.get(id)||[],key=`curated/family/${id}`;allFamilyKeys.push(key);
 if(!children.length)return declare(key,{canvas:workKey(gen.id),parent,kind:'reference',title:source.title,target:id,x:2920,y:500,width:850,height:610,sizeMode:'fixed',collapsed:true,...(cell?{cell}:{}),style});
 declare(key,{canvas:workKey(gen.id),parent,kind:'container',title:source.title,x:2920,y:500,width:850,height:610,collapsed:true,sizeMode:'fixed',containerVariant:'gallery',childLayout:'contained',childArrangement:'free',containerSizes:{collapsed:{width:850,height:610,sizeMode:'fixed'},expanded:{width:850,height:610,sizeMode:'fixed'}},...(cell?{cell}:{}),style});
 declare(`${key}/identity`,{canvas:workKey(gen.id),parent:key,kind:'reference',title:source.title,target:id,x:2940,y:540,width:790,height:510,sizeMode:'fixed',collapsed:true,style});
 for(const child of children)family(child,key);
 return key;
}
familyRoots.forEach((id,i)=>family(id,'curated/families',{column:i%2+1,row:Math.floor(i/2)+1}));

// Twenty-four concise book References form an actual chronological diagram.
for(const name of ['Iliad','Odyssey']){
 const w=work(name),originals=p.nodes.filter(n=>n.canvasId===w.id&&n.kind==='container'&&/^Book \d+ ·/.test(n.title)).sort((a,z)=>Number(a.title.match(/^Book (\d+)/)[1])-Number(z.title.match(/^Book (\d+)/)[1]));
 const keys=originals.map((n,i)=>declare(`curated/${name}/book/${i+1}`,{canvas:workKey(w.id),kind:'reference',title:n.title.replace(/^Book /,''),target:n.id,x:80+(i%6)*520,y:120+Math.floor(i/6)*320,width:450,height:200,collapsed:true,sizeMode:'fixed',style}));
 keys.forEach((key,i)=>{if(!i)return;const wrap=i%6===0,row=Math.floor(i/6);links.push({key:`curated/${name}/sequence/${i}`,canvas:workKey(w.id),source:keys[i-1],target:key,kind:'visual',relation:'chronological',direction:'forward',label:'',sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5},...(wrap?{points:[{x:3340,y:120+(row-1)*320+100},{x:3340,y:120+(row-1)*320+260},{x:40,y:120+(row-1)*320+260},{x:40,y:120+row*320+100}]}:{}),style:{color:name==='Iliad'?'#9C5941':P.sea,width:2}})});
}
// Other subject entrances are subject constellations and a poetry comparison.
for(const name of ['Myths','Hesiod']){const w=work(name),roots=cs.get(w.rootId)||[];let i=0;for(const source of roots.filter(n=>n.kind==='container')){
 const angle=(i-1.5)*Math.PI*2/Math.max(roots.length,4),x=name==='Hesiod'?80+(i%2)*850:1100+Math.cos(angle)*950,y=name==='Hesiod'?120+Math.floor(i/2)*430:700+Math.sin(angle)*600;
 declare(`curated/${name}/${source.id}`,{canvas:workKey(w.id),kind:'reference',title:source.title,target:source.id,x,y,width:name==='Hesiod'?700:500,height:240,collapsed:true,sizeMode:'fixed',style});i++;
 }}
const reading=work('Readings');
const documents=[['Iliad','iliad-butler-full.txt'],['Odyssey','odyssey-butler-full.txt'],['Hesiod','hesiod-hymns-evelyn-white-full.txt']];
for(const[i,[title,file]]of documents.entries()){const a=p.assets.find(a=>a.name===file);declare(`curated/reading/${title}`,{canvas:workKey(reading.id),kind:'document',title,asset:a.id,x:80+i*760,y:140,width:640,height:350,collapsed:true,sizeMode:'fixed',containerSizes:{collapsed:{width:640,height:350,sizeMode:'fixed'},expanded:{width:700,height:1000,sizeMode:'fixed'}},style});}
const credits=p.nodes.find(n=>n.title==='Image credits'&&n.assetId);if(credits)declare('curated/reading/credits',{canvas:workKey(reading.id),kind:'document',title:'Image credits',asset:credits.assetId,x:80,y:650,width:640,height:300,collapsed:true,sizeMode:'fixed',style});

// Merge duplicate update declarations before applying one atomic typed recipe.
const unique=new Map();for(const spec of nodes)unique.set(spec.key,{...(unique.get(spec.key)||{}),...spec});
const recipe={version:1,bindings:aliases,canvases,nodes:[...unique.values()],links,routes};
console.log(JSON.stringify({stage:'typed-apply',nodes:unique.size,links:links.length}));
const result=applyRecipe(p,JSON.parse(JSON.stringify(recipe)));console.log(JSON.stringify({stage:'commit-snapshots'}));const candidate=commitSnapshots(result.project);
const bytes=Buffer.byteLength(JSON.stringify(candidate));if(bytes>=104857600)throw new Error(`Over host capacity: ${bytes}`);
await writeFile(`${dir}/curation-recipe.json`,JSON.stringify(recipe));await writeFile(`${dir}/draft.json`,JSON.stringify(candidate,null,2));await writeFile(`${dir}/aliases.json`,JSON.stringify(result.aliases,null,2));
const sha256=createHash('sha256').update(JSON.stringify(candidate)).digest('hex');
const report={sha256,serializedBytes:bytes,lineageWork:result.aliases.canvases['curated/lineage'],canonicalFigures:canon.size,familyNodes:allFamilyKeys.length,newPrimaryDiagram:'Truthful selected genealogy, complete family Galleries, chronological epic diagrams, thematic constellations and embedded readings',primaryWorks:p.canvases.map(w=>({id:w.id,title:w.title})),support: support.map(s=>({title:s.name,id:result.aliases.nodes[s.key]})),canonicalGraphNodeIdsPreserved:true,originalLinkSemanticsPreserved:true};
await writeFile(`${dir}/curation-report.json`,JSON.stringify(report,null,2));
const plan=p.canvases.map(w=>({workId:w.id,label:w.title}));plan.push({workId:gen.id,nodeId:result.aliases.nodes['curated/families'],scope:'node',label:'Families',expand:[result.aliases.nodes['curated/families']]});
plan.push({workId:gen.id,nodeId:result.aliases.nodes[`curated/family/${find('Gaia').id}`],scope:'node',label:'Gaia family',expand:[result.aliases.nodes['curated/families'],result.aliases.nodes[`curated/family/${find('Gaia').id}`]]});
await writeFile(`${dir}/review-plan.json`,JSON.stringify(plan,null,2));console.log(JSON.stringify(report,null,2));
