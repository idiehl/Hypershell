import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {applyRecipe,inspectProject} from '../../src/core/authoring.ts';
import {commitSnapshots} from '../../src/core/model.ts';
const out='I:/dev/Meridian/output/mythology-current-rework-20261023/visual';
const base=JSON.parse(await readFile('I:/dev/Meridian/output/mythology-current-rework-20261023/baseline/e35cba82-f644-416a-ac14-12645893ed88.json','utf8'));
await mkdir(out,{recursive:true});
const recipe={version:1,nodes:[],links:[],assets:[]};
const byId=new Map(base.nodes.map(n=>[n.id,n]));
const style=work=>base.nodes.find(n=>n.canvasId===work&&n.kind==='text'&&n.style?.shape==='rectangle').style;
const node=(id,patch)=>recipe.nodes.push({key:`n-${id}`,existingId:id,...patch});
const link=(id,patch)=>recipe.links.push({key:`l-${id}`,existingId:id,...patch});
const add=(key,canvas,parent,title,x,y,width=300,height=130,extra={})=>recipe.nodes.push({key,canvas,parent,kind:'media',title,text:'',x,y,width,height,contentType:'text',childPosition:'manual',placement:'free',sizeMode:'fixed',style:{...style(canvas),titleSize:32,textAlign:'center'},...extra});
const relationship=(key,canvas,source,target,label,extra={})=>recipe.links.push({key,canvas,source,target,kind:'visual',relation:'generic',direction:'forward',label,style:{color:base.canvases.find(w=>w.id===canvas).style?.accent??style(canvas).accent,width:2,lineStyle:'solid'},...extra});

// The overview keeps its subject constellation. Short object labels stay on art;
// attribution and uncertain dating remain in the existing Notes archive.
node('3a911d72-09f6-4d03-abfc-963bc35b1ba7',{title:'Roman Jupiter'});
node('eaac40b1-d25d-464c-aebc-8c61cf5650ad',{title:'Zeus & Ganymede'});
node('fb51f319-64bc-4329-8d64-565d066ab2df',{title:'Renaissance Jupiter'});
// All notes panels use the same quiet title; their original records stay intact.
for(const n of base.nodes.filter(n=>n.kind==='document'&&n.title==='Passages & provenance'))node(n.id,{title:'Sources',text:''});

// Aphrodite's alternatives become two explicit, separate traditions rather than
// a prose exception beside an otherwise purely named genealogy.
const family='77a19894-5bf3-4365-b4ad-7587288c300c',familyFrame='e3bdb991-aea2-45c9-944a-c1397d59cc76';
node(familyFrame,{height:1500,containerSizes:{expanded:{width:1800,height:1500,sizeMode:'fixed'}}});
node('402ef00c-503e-4f7a-affb-0d869a0e405a',{title:'Aphrodite',text:'',x:160,y:900,width:340,height:130,collapsed:false});
add('aphrodite-hesiod',family,familyFrame,'Sea Foam',30,1230,290,130);
add('aphrodite-iliad',family,familyFrame,'Zeus & Dione',430,1230,320,130);
relationship('aphrodite-hesiod-tradition',family,'402ef00c-503e-4f7a-affb-0d869a0e405a','aphrodite-hesiod','Hesiod',{relation:'generic',direction:'none',sourceAnchor:{x:.25,y:1},targetAnchor:{x:.5,y:0},style:{color:'#986838',width:2,lineStyle:'dashed'}});
relationship('aphrodite-iliad-tradition',family,'402ef00c-503e-4f7a-affb-0d869a0e405a','aphrodite-iliad','Iliad',{relation:'generic',direction:'none',sourceAnchor:{x:.75,y:1},targetAnchor:{x:.5,y:0},style:{color:'#986838',width:2,lineStyle:'dashed'}});
node('97ae0ecf-2bb8-4369-8724-46321bb28a2e',{y:1630});
node('662394dc-21bb-431e-88cc-49b14e903af6',{y:1630});

// Books provide the narrative axis, not physical map coordinates. The remembered
// sequence has room to disclose a separate shipwreck instead of merging it with
// the Sirens. Existing original episode text is retained in Notes.
const odyssey='c6d6b948-5092-496d-baea-017a02ed9ce1',voyage='cbf37a78-5d51-470c-bc9b-2db3525744bd';
node('6f691c29-49ca-4ac3-b03f-44c7463f2bea',{title:'Odysseus',text:'Books 9–12'});
node(voyage,{width:2000,containerSizes:{expanded:{width:2000,height:320,sizeMode:'fixed'}}});
node('95f44b12-5a4e-4b63-94d4-3b8ba9892d81',{width:2000});
for(const [id,book] of [['75ea4050-aab9-4ae9-8602-eb0bd4559105','Book 9'],['26989b25-4020-453c-b895-ea9c6eb727e0','Book 10'],['a91bfb08-fd20-4660-b20e-170217affdc9','Book 11']])node(id,{text:book});
node('3b90c6db-cbd4-44ac-a4bb-8beb2433d7b6',{title:'Sirens',text:'Book 12'});
add('odyssey-shipwreck',odyssey,voyage,'Shipwreck',2180,845,320,155,{text:'Book 12'});
relationship('sirens-to-shipwreck',odyssey,'3b90c6db-cbd4-44ac-a4bb-8beb2433d7b6','odyssey-shipwreck','',{sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5}});
for(const l of base.links.filter(l=>l.canvasId===odyssey)){
 const patch={relation:'generic',direction:'forward'};
 if(l.label==='the focus shifts')patch.label='meanwhile';
 if(l.label==='he begins to tell'||l.label==='remembers')patch.label='recounts';
 if(l.label==='present resumes')patch.label='returns';
 if(l.label==='then')patch.label='';
 link(l.id,patch);
}

// Parallel Greek/Roman counterparts retain three equal row axes. Avoid mixing
// a generic shared-attribute edge with a more definite identification elsewhere.
const gods='f335888d-5280-4343-854e-326ffd92f6a1';
for(const l of base.links.filter(l=>l.canvasId===gods))link(l.id,{...(l.label?{label:'Roman counterpart'}:{}),relation:'generic',direction:'none',style:{lineStyle:'solid'}});
node('ba8ba7e5-ca77-455c-98c4-d25a8e58150c',{title:'Paris · Turchi'});
node('e50e59cf-a629-4b90-a2ea-8d73430ae3f4',{title:'Paris · Allori'});

// The hymn's actual one-third/two-thirds settlement becomes a proportional
// subject diagram. It is content, not a statistic about the Work.
const persephone='5d9ed78a-ee1f-4f98-bca4-047f24307295',year='386a80ca-9ce9-4f52-af18-1f8b4b430108';
node('1f862f8f-77f4-4536-86e0-c9fd934780cc',{text:''});
node(year,{title:'Divided Year',text:'',collapsed:false});
add('year-above',persephone,year,'Above',550,1510,360,140,{text:'Two thirds'});
add('year-below',persephone,year,'Below',990,1510,180,140,{text:'One third'});
relationship('year-above-share',persephone,year,'year-above','',{sourceAnchor:{x:.25,y:1},targetAnchor:{x:.5,y:0},style:{color:'#D6B077',width:2,lineStyle:'solid'}});
relationship('year-below-share',persephone,year,'year-below','',{sourceAnchor:{x:.75,y:1},targetAnchor:{x:.5,y:0},style:{color:'#D6B077',width:2,lineStyle:'solid'}});
// Preserve every revised original string in a supporting, explicit change packet.
const changed=recipe.nodes.filter(d=>d.existingId&&(d.title!==undefined&&d.title!==byId.get(d.existingId).title||d.text!==undefined&&d.text!==byId.get(d.existingId).text));
const record=changed.map(d=>{const n=byId.get(d.existingId);return `${n.title}\n${n.text??''}\nOriginal identity: ${n.id}\n`}).join('\n────────────────────────\n\n');
recipe.assets.push({key:'refinement-original-reading',name:'Visual Atlas Reading.txt',mime:'text/plain',base64:Buffer.from(record,'utf8').toString('base64')});
const notes='a629a20d-cff4-4dec-afed-28e3b00473f4';
const oldNotes=byId.get(notes);
node(notes,{grid:{...oldNotes.grid,rows:oldNotes.grid.rows+1}});
add('refinement-reading',base.entryCanvasId,notes,'Reading Notes',1600,7490,800,540,{kind:'document',contentType:'asset',asset:'refinement-original-reading',style:{...oldNotes.style,textAlign:'left',titleSize:28}});
const result=applyRecipe(base,recipe),candidate=commitSnapshots(result.project);
for(const [name,value] of [['recipe.json',recipe],['draft.json',candidate],['aliases.json',result.aliases],['inspection.json',inspectProject(candidate)]])await writeFile(`${out}/${name}`,JSON.stringify(value,null,2));
const hash=v=>createHash('sha256').update(JSON.stringify(v)).digest('hex');
await writeFile(`${out}/preservation.json`,JSON.stringify({baselineRevision:base.revision,baselineHash:hash(base),candidateHash:hash(candidate),allOriginalNodesPreserved:base.nodes.every(n=>candidate.nodes.some(x=>x.id===n.id)),allOriginalLinksPreserved:base.links.every(l=>candidate.links.some(x=>x.id===l.id)),allOriginalAssetsPreserved:base.assets.every(a=>candidate.assets.some(x=>hash(x)===hash(a))),originalWorkIds:base.canvases.map(w=>w.id),newNodes:candidate.nodes.length-base.nodes.length,newLinks:candidate.links.length-base.links.length,newAssets:candidate.assets.length-base.assets.length,factualSupport:'All substantive additions are decompositions of the existing Divine Family, Odyssey and Persephone reading notes. No new mythology claims or external assets introduced.'},null,2));
console.log(JSON.stringify({out,revision:candidate.revision,nodes:candidate.nodes.length,links:candidate.links.length}));
