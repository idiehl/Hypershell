import{readFile,writeFile}from'node:fs/promises';
import{applyRecipe}from'../../src/core/authoring.ts';
import{commitSnapshots}from'../../src/core/model.ts';
import{createHash}from'node:crypto';
const dir='I:/dev/Meridian/output/mythology-refinement-20261023/living';
const p=JSON.parse(await readFile(`${dir}/draft.json`,'utf8')),aliases=JSON.parse(await readFile(`${dir}/aliases.json`,'utf8')),report=JSON.parse(await readFile(`${dir}/curation-report.json`,'utf8'));
const by=new Map(p.nodes.map(n=>[n.id,n])),reverse=new Map(Object.entries(aliases.nodes).map(([k,v])=>[v,k]));
const key=id=>reverse.get(id)||id,cs=new Map();for(const n of p.nodes){let a=cs.get(n.parentId)||[];a.push(n);cs.set(n.parentId,a)}
const descendants=id=>{const a=[];const visit=id=>{for(const n of cs.get(id)||[]){a.push(n);visit(n.id)}};visit(id);return a};
const updates=new Map(),nodes=[],remove=[];
const set=(id,fields)=>updates.set(id,{...(updates.get(id)||{}),...fields});
const style={fill:'#FCF8EF',border:'#C9BDA7',textColor:'#263B3D',accent:'#9D713D',fontFamily:'palatino-linotype',titleSize:28,fontSize:18,padding:16,childPadding:18,radius:0,contentSizing:'fixed'};
for(const [k,id]of Object.entries(aliases.nodes))if(/^curated\/(?:figure\/|(?:Iliad|Odyssey)\/book\/|Myths\/|Hesiod\/)/.test(k))set(id,{height:k.startsWith('curated/figure/')?72:115,style:{...by.get(id).style,contentGap:0},collapsed:true});
// Reuse the original complete-text document identities instead of adding
// duplicate full-corpus nodes and duplicate snapshot copies of their bytes.
for(const[title,original]of [['Iliad','bc9c6de5-ae1c-422e-b939-578d7d75b5c7'],['Odyssey','4e21c21a-7543-4f64-bee1-79e07b57a4a2'],['Hesiod','f233df1f-f062-4ed2-82be-cb15be4f6813']]){
 const added=by.get(aliases.nodes[`curated/reading/${title}`]);remove.push(added.id);
 set(original,{parent:p.canvases.find(w=>w.title==='Readings').rootId,title,text:'',x:added.x,y:added.y,width:640,height:350,collapsed:true,containerSizes:added.containerSizes,style:{...style}});
}
const newCredits=aliases.nodes['curated/reading/credits'];if(newCredits)set(newCredits,{y:1270});

// Preserve the full source graph in a mutually exclusive family presentation.
// It remains the one canonical set of identities, not a second graph copy.
const lineage=p.canvases.find(w=>w.id===report.lineageWork),roots=cs.get(lineage.rootId)||[];
nodes.push({key:'curated/canonical-pages',canvas:lineage.id,kind:'container',title:'Lineage',x:80,y:100,width:1280,height:900,collapsed:false,sizeMode:'fixed',containerVariant:'gallery',childLayout:'contained',childArrangement:'free',containerSizes:{collapsed:{width:1280,height:900,sizeMode:'fixed'},expanded:{width:1280,height:900,sizeMode:'fixed'}},style});
for(const root of roots){const dx=120-root.x,dy=170-root.y;for(const n of[root,...descendants(root.id)])set(n.id,{x:n.x+dx,y:n.y+dy,...(n.id===root.id?{parent:'curated/canonical-pages'}:{})});}
const canonicalIds=new Set(Object.entries(aliases.nodes).filter(([k])=>/^curated\/family\//.test(k)).map(([,id])=>id));
for(const n of p.nodes){
 if(n.canvasId===p.entryCanvasId||canonicalIds.has(n.id)||n.kind!=='container'&&n.kind!=='spreadsheet')continue;
 const k=key(n.id);if(k.startsWith('curated/'))continue;
 const reading=/· Book \d+ · text$/.test(n.tag||'')||n.title==='Text'&&n.containerVariant==='window';
 const functional=descendants(n.id).some(c=>['input','number','joint','output','switch','operation'].includes(c.kind));
 const isArchive=n.containerVariant==='window'&&!reading||n.kind==='spreadsheet'||n.title==='Annotations'||(cs.get(n.id)||[]).length>12&&!functional&&!reading;
 if(isArchive)set(n.id,{containerVariant:'gallery',childArrangement:n.kind==='spreadsheet'?'grid':'free',childLayout:'contained',containerSizes:{collapsed:{width:450,height:210,sizeMode:'fixed'},expanded:{width:1150,height:850,sizeMode:'fixed'}},width:450,height:210,collapsed:true});
}
const recipe={version:1,bindings:aliases,nodes:[...updates].map(([id,u])=>({key:key(id),...u})).concat(nodes),remove:{nodes:remove}};
const result=applyRecipe(p,recipe),candidate=commitSnapshots(result.project),bytes=Buffer.byteLength(JSON.stringify(candidate));
if(bytes>=104857600)throw new Error(`Over capacity: ${bytes}`);
const sha256=createHash('sha256').update(JSON.stringify(candidate)).digest('hex');
await writeFile(`${dir}/finish-recipe.json`,JSON.stringify(recipe));await writeFile(`${dir}/draft.json`,JSON.stringify(candidate,null,2));await writeFile(`${dir}/aliases.json`,JSON.stringify(result.aliases,null,2));
report.sha256=sha256;report.serializedBytes=bytes;report.originalCorpusDocumentsReused=true;report.canonicalPages=result.aliases.nodes['curated/canonical-pages'];await writeFile(`${dir}/curation-report.json`,JSON.stringify(report,null,2));
const gen=p.canvases.find(w=>w.title==='Genealogy'),gaiaOriginal='34817749-bf21-487d-bde0-f645b78ae723',uranusOriginal='0d463ff6-01b9-49c4-acca-ca79c17f5851';
const plan=p.canvases.filter(w=>w.id!==lineage.id).map(w=>({workId:w.id,label:w.title}));
plan.push({workId:gen.id,nodeId:result.aliases.nodes['curated/families'],scope:'node',label:'Families',expand:[result.aliases.nodes['curated/families']]});
for(const[id,label]of [[gaiaOriginal,'Gaia family'],[uranusOriginal,'Uranus family']])plan.push({workId:gen.id,nodeId:result.aliases.nodes[`curated/family/${id}`],scope:'node',label,expand:[result.aliases.nodes['curated/families'],result.aliases.nodes[`curated/family/${gaiaOriginal}`],result.aliases.nodes[`curated/family/${id}`]]});
plan.push({workId:lineage.id,label:'Canonical lineage',nodeId:gaiaOriginal,scope:'node',scale:.8,expand:[result.aliases.nodes['curated/canonical-pages'],gaiaOriginal]});
await writeFile(`${dir}/review-plan.json`,JSON.stringify(plan,null,2));console.log(JSON.stringify({sha256,bytes,updates:updates.size,removedNewDuplicateCorpusNodes:remove.length,works:candidate.canvases.length,nodes:candidate.nodes.length,links:candidate.links.length}));
