import {readFile,writeFile,mkdir} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {newProject,addCanvas,addNode,commitSnapshots} from '../../src/core/model.ts';
import {validateProject} from '../../src/core/rules.ts';
import {evaluate} from '../../src/core/engine.ts';
import {buildPublication,readPublication,createPublicationPayload} from '../../src/publication.ts';

const root=fileURLToPath(new URL('../../',import.meta.url));
const content=path.join(root,'content/greek-mythology'),out=path.join(root,'examples/greek-mythology');
await mkdir(out,{recursive:true});
const json=async name=>JSON.parse(await readFile(path.join(content,name),'utf8'));
const [gods,epics,hr,media,fixtures]=await Promise.all(['gods.json','epics.json','hesiod-rome.json','media.json','fixtures.json'].map(json));
const sources=new Map();
for(const source of [...gods.sources,...epics.sources,...hr.sources]){
  if(sources.has(source.id)&&sources.get(source.id).url!==source.url)throw new Error(`Conflicting source identity ${source.id}`);
  sources.set(source.id,source);
}
const missingIds=new Set();
function auditSources(value){if(!value||typeof value!=='object')return;if(value.sourceIds)for(const id of value.sourceIds)if(!sources.has(id))missingIds.add(id);for(const v of Object.values(value))if(typeof v==='object')auditSources(v);}
for(const data of [gods,epics,hr])auditSources(data);
if(missingIds.size)throw new Error(`Unresolved source IDs: ${[...missingIds]}`);
const cite=ids=>ids?.length?'\n\nSources: '+ids.map(id=>`[${id}]`).join(' '):'';
const textOf=value=>typeof value==='string'?value:Array.isArray(value)?value.map(textOf).join('\n\n'):value&&typeof value==='object'?Object.entries(value).map(([k,v])=>`${k}: ${textOf(v)}`).join('\n'):String(value??'');
let p=newProject('Greek Mythology · Gods, Epics and Roman Worlds');
const canvases=new Map(),nodes=new Map(),assets=new Map();
const alias=key=>({kronos:'cronus',uranus:'ouranos'}[key]??key);
const home=p.canvases[0];home.title='00 · Begin here';p.nodes[0].title=home.title;canvases.set('home',home);
function canvas(key,title){const c=addCanvas(p,title);canvases.set(key,c);return c;}
function n(c,key,kind,title,x,y,width=430,height=160,parent){const a=addNode(p,c.id,kind,title);Object.assign(a,{x,y,width,height,parentId:parent?.id??c.rootId});nodes.set(key,a);return a;}
function height(text,width=440){let lines=0;for(const line of text.split('\n'))lines+=Math.max(1,Math.ceil(line.length/((width-25)/6.6)));return Math.max(130,78+lines*18);}
function t(c,key,title,text,x,y,width=450,parent){const node=n(c,key,'text',title,x,y,width,height(text,width),parent);node.text=text;return node;}
function link(a,b,relation='causal',kind='visual',port,points){if(a.canvasId!==b.canvasId)throw new Error('Use references between canvases');p.links.push({id:crypto.randomUUID(),canvasId:a.canvasId,sourceId:a.id,targetId:b.id,kind,relation,...(port===undefined?{}:{targetPort:port}),sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5},...(points?{points}:{})});}
function launch(c,key,title,target,x,y,width=280,parent){const node=n(c,key,'canvas-link',title,x,y,width,90,parent);node.targetId=target.rootId;return node;}
function ref(c,key,title,target,x,y,width=400,h=240,parent){const node=n(c,key,'reference',title,x,y,width,h,parent);node.targetId=target.id;return node;}
function heading(c,title,text,width=1110){return t(c,`${c.id}-intro`,title,text,55,45,width);}
function homeLink(c,y=210){return launch(c,`${c.id}-home`,'Collection directory',home,55,y,260);}
function grid(c,prefix,items,startY=360,cols=2,width=500,gap=36,parent){let y=startY;const made=[];for(let at=0;at<items.length;at+=cols){let rowH=0;for(let j=0;j<cols&&at+j<items.length;j++){const item=items[at+j],node=t(c,`${prefix}-${at+j}`,item.title,item.text,55+j*(width+gap),y,width,parent);made.push(node);rowH=Math.max(rowH,node.height);}y+=rowH+gap;}return{nodes:made,bottom:y};}
async function asset(file,mime,key=file){if(assets.has(key))return assets.get(key);const bytes=await readFile(path.join(content,'media',file));const a={id:crypto.randomUUID(),name:file,mime,base64:bytes.toString('base64')};p.assets.push(a);assets.set(key,a);return a;}
function mediaNode(c,key,title,a,x,y,width=420,h=365){const node=n(c,key,/^image\/(png|jpeg|webp|gif)$/.test(a.mime)?'image':'document',title,x,y,width,h);node.assetId=a.id;return node;}

// Directory and a source input intentionally used from another canvas.
heading(home,'Greek mythology as a connected collection','Follow a god from the family tree to a story, compare a passage with another tradition, then inspect how an image reinterprets it. This collection separates literary versions instead of inventing a single universal canon. All narrative summaries are original paraphrases; source identifiers resolve in the embedded source register.');
const permit=n(home,'archive-reviewed','input','Archive reviewed',820,225,280,130);permit.value=true;permit.exposed=true;
t(home,'reading-instructions','How to read and test','Use the canvas list or the launch cards below. Fit gives an overview; zoom in to read. Principal gods have dossiers; other figures link to related traditions. Each epic has six four-book canvases. Source register and Media gallery hold the original attachments. The Lab contains labelled test models, including a saved reference to Archive reviewed. Changing it here only reaches the Lab after an editor save. Published controls are temporary.',55,225,720);

const origins=canvas('origins','01 · Origins and Titans'),olympus=canvas('olympus','02 · Olympian family tree');
const genealogyNotes=canvas('genealogy-notes','Family · Variant traditions');
const olympianKeys=new Set(['hestia','demeter','hera','hades','poseidon','zeus','athena','apollo','artemis','ares','hephaestus','aphrodite','hermes','persephone','harmonia','dionysus']);
const familyMap=new Map(gods.family.map(f=>[alias(f.key),f]));
const godNodes=new Map(),godCanvases=new Map();
for(const c of [origins,olympus]){
  heading(c,c===origins?'From first beings to divine succession':'The Olympian family and its alternatives','Arrows indicate the selected parentage stated on each card, not marriage, causation or certainty. An empty parent list is not proof that every source leaves a figure parentless. Partner lines, where shown, mean a narrated relationship and do not imply a consensual union. This map chiefly follows Hesiod; the dossier/source notes retain alternatives.');
  homeLink(c,245);
}
function generation(key,seen=new Set()){if(seen.has(key))return 0;const f=familyMap.get(key);if(!f||!f.parents?.length)return 0;const next=new Set(seen);next.add(key);return 1+Math.max(...f.parents.map(k=>generation(alias(k),next)));}
for(const c of [origins,olympus]){
  const chosen=gods.family.filter(f=>olympianKeys.has(alias(f.key))===(c===olympus));
  const ranks=[...new Set(chosen.map(f=>generation(alias(f.key))))].sort((a,b)=>a-b);let y=420;
  for(const rank of ranks){const row=chosen.filter(f=>generation(alias(f.key))===rank);let rowY=y;
    for(let i=0;i<row.length;i++){
      if(i>0&&i%6===0)rowY+=480;
      const f=row[i],key=alias(f.key),x=55+(i%6)*355;
      const parents=(f.parents??[]).map(k=>familyMap.get(alias(k))?.name??k).join(' + ')||'No parent asserted here';
      const summary=f.summary.length>190?f.summary.slice(0,187)+'…':f.summary;
      const node=t(c,`god-${key}`,f.name,`${summary}\n\nSelected descent: ${parents}.${f.variantNotes?'\nAlternative accounts: see dossier and genealogy notes.':''}`,x,rowY,315);
      node.height=Math.max(node.height,290);godNodes.set(key,node);
    }y=rowY+480;
  }
  launch(c,`${c.id}-genealogy-notes`,'Sources and variant genealogies',genealogyNotes,720,245,380);
}
heading(genealogyNotes,'Selected descent and alternative accounts','These notes accompany the two family maps. A variant is an alternative telling, not an additional simultaneous parent. An empty parent list can mean that ancestry lies outside this selected map. Follow each cited passage before extending the genealogy.');homeLink(genealogyNotes,245);launch(genealogyNotes,'notes-origins','Origins and Titans',origins,365,245,300);launch(genealogyNotes,'notes-olympus','Olympian family',olympus,710,245,300);
grid(genealogyNotes,'family-variant',gods.family.filter(f=>f.variantNotes).map(f=>({title:`${f.name} · variants`,text:textOf(f.variantNotes)+cite(f.sourceIds)})),400,4,510,30);
// Resolve descent with true reference appearances whenever a parent belongs to the other map.
for(const c of [origins,olympus]){
  const externals=new Map();let count=0;
  for(const f of gods.family){const child=godNodes.get(alias(f.key));if(child.canvasId!==c.id)continue;
    for(const parentKey of f.parents??[]){let parent=godNodes.get(alias(parentKey));if(!parent)continue;
      if(parent.canvasId!==c.id){if(!externals.has(parent.id)){externals.set(parent.id,ref(c,`${c.id}-ancestor-${parentKey}`,`${parent.title} · saved ancestor`,parent,2220,420+(count++)*260,360,225));}parent=externals.get(parent.id);}
      link(parent,child,'causal');
      const descent=p.links.at(-1);descent.sourceAnchor={x:.5,y:1};descent.targetAnchor={x:.5,y:0};
    }
  }
}
let dossierNumber=3;
for(const group of gods.groups){for(const entry of group.entries){
  const key=alias(entry.key),c=canvas(`dossier-${key}`,`${String(dossierNumber++).padStart(2,'0')} · ${entry.title.split('—')[0].trim().slice(0,30)}`);godCanvases.set(key,c);
  heading(c,entry.title,entry.summary+cite(entry.sourceIds));homeLink(c,headingHeight(c)+75);
  const original=godNodes.get(key);const top=Math.max(380,headingHeight(c)+235);
  if(original)ref(c,`dossier-${key}-identity`,'Identity in the family tree',original,820,top,350,280);
  const stories=entry.stories.map(s=>({title:s.title,text:s.text+cite(s.sourceIds)}));const placed=grid(c,`dossier-${key}-story`,stories,top,2,355,32);
  const readingGroup=n(c,`dossier-${key}-reading-group`,'container','Stories and reading questions',35,top-48,770,placed.bottom-top+340);
  for(const story of placed.nodes)story.parentId=readingGroup.id;
  if(original)launch(c,`dossier-${key}-tree`,'Open the family tree',p.canvases.find(x=>x.id===original.canvasId),820,top+320,350);
  const object=media.find(m=>m.key===key||(key==='hephaestus'&&m.key==='thetis'));
  if(object){const a=await asset(object.file,object.mime);mediaNode(c,`dossier-${key}-art`,object.title,a,820,top+455,350,350);t(c,`dossier-${key}-credit`,'Object and interpretation',`${object.caption}\n${object.date}; ${object.culture}.\n${object.url}\n${object.rights}`,820,top+830,350);}
  const question=t(c,`dossier-${key}-question`,'Read across versions','Which part of this god’s identity is emphasized by each story? Compare a genealogy, a hymn, and an epic scene before treating one label such as “war” or “wisdom” as the god’s whole character.',55,placed.bottom,740,readingGroup);
  readingGroup.height=question.y+question.height+25-readingGroup.y;
}}
function headingHeight(c){return nodes.get(`${c.id}-intro`).height;}
// Family appearances lead into their full dossiers without changing the identity of an original.
const groupedProfiles={
  'first-beings':['chaos','tartarus','eros','erebus','nyx','aether','hemera'],
  'earth-and-sky':['gaia','ouranos','pontus','nereus','doris','eurybia'],
  'titan-branches':['oceanus','coeus','crius','hyperion','theia','themis','mnemosyne','phoebe','tethys','leto','asteria','perses','helios','selene','eos','maia'],
  'iapetid-brothers':['iapetus','clymene','atlas','prometheus','epimetheus','menoetius','pleione'],
  'succession-and-war':['cronus','rhea','metis']
};
const relatedProfiles={dione:'aphrodite',cadmus:'dionysus',eurynome:'hephaestus',amphitrite:'poseidon',aglaea:'hephaestus',semele:'dionysus',harmonia:'aphrodite'};
for(const [key,original] of godNodes){const group=Object.keys(groupedProfiles).find(g=>groupedProfiles[g].includes(key));const c=godCanvases.get(key)??godCanvases.get(group)??godCanvases.get(relatedProfiles[key]);if(!c)continue;launch(p.canvases.find(x=>x.id===original.canvasId),`family-dossier-${key}`,godCanvases.has(key)?`Read ${original.title}`:'Read the related traditions',c,original.x+10,original.y+original.height+8,original.width-20);}
launch(origins,'origins-next','Continue to the Olympians',olympus,365,245,310);
launch(olympus,'olympus-before','Return to Origins and Titans',origins,365,245,310);

// Epic overviews and compact four-book reading canvases.
for(const epic of epics.canvases){
  const isIliad=epic.key==='iliad',name=isIliad?'Iliad':'Odyssey';const overview=canvas(epic.key,`${name} · Overview`);
  heading(overview,epic.title,epic.intro);homeLink(overview,headingHeight(overview)+75);
  let sectionY=Math.max(400,headingHeight(overview)+210);
  for(let i=0;i<epic.sections.length;i++){
    const section=epic.sections[i],first=section.books[0].book,last=section.books.at(-1).book;
    const c=canvas(section.key,`${name} · Books ${String(first).padStart(2,'0')}–${String(last).padStart(2,'0')}`);
    heading(c,section.title,section.summary);
    launch(c,`${section.key}-overview`,`${name} overview`,overview,55,headingHeight(c)+75,300);
    const start=Math.max(330,headingHeight(c)+195);
    const placed=grid(c,section.key,section.books.map(b=>({title:`Book ${b.book} · ${b.title}`,text:b.text+cite(b.sourceIds)})),start,2,510,35);
    let themes=grid(c,`${section.key}-theme`,section.themes.map(q=>({title:q.title,text:q.text+cite(q.sourceIds)})),placed.bottom+10,2,510,35);
    const citations=section.books.flatMap(b=>b.sourceIds).map(id=>sources.get(id)).filter(Boolean);
    t(c,`${section.key}-sources`,'Read the ancient text',citations.map(s=>`[${s.id}] ${s.title}\n${s.locators??''}\n${s.url}`).join('\n\n'),55,themes.bottom,1055);
    const card=launch(overview,`${section.key}-launch`,section.title,c,55+(i%2)*575,sectionY+Math.floor(i/2)*420,535);
    t(overview,`${section.key}-summary`,'Narrative frame',section.summary,card.x,card.y+105,535);
  }
  const after=sectionY+Math.ceil(epic.sections.length/2)*420+40;
  const chars=grid(overview,`${epic.key}-characters`,epic.characters.map(ch=>({title:ch.title,text:ch.text+cite(ch.sourceIds)})),after,3,345,32);
  for(const [i,ch] of epic.characters.entries()){const original=godNodes.get(alias(ch.key));if(original){const card=chars.nodes[i];ref(overview,`${epic.key}-god-${ch.key}`,`${original.title} · family reference`,original,card.x,chars.bottom+Math.floor(i/3)*250,330,220);}}
  t(overview,`${epic.key}-version`,'Narrative and naming notes',textOf(epic.variantNotes),55,chars.bottom+Math.ceil(epic.characters.length/3)*250+30,1100);
  const art=media.find(m=>m.key===(isIliad?'thetis':'circe'));const a=await asset(art.file,art.mime);mediaNode(overview,`${epic.key}-image`,art.title,a,1250,sectionY,450,440);t(overview,`${epic.key}-image-credit`,'Image as interpretation',`${art.caption}\n${art.artist}\n${art.date}\n${art.url}\n${art.rights}`,1250,sectionY+465,450);
  const fixture=fixtures.find(f=>f.file===(isIliad?'iliad-four-movements.webp':'odyssey-reading-order.gif'));
  mediaNode(overview,`${epic.key}-diagram`,fixture.title,await asset(fixture.file,fixture.mime),1250,sectionY+820,450,335);
}
// Cross-link the successive book canvases after every target root exists.
for(const epic of epics.canvases)for(let i=0;i<epic.sections.length;i++){
  const c=canvases.get(epic.sections[i].key);const y=headingHeight(c)+75;
  if(i>0)launch(c,`${c.id}-previous`,'Previous four books',canvases.get(epic.sections[i-1].key),385,y,285);
  if(i+1<epic.sections.length)launch(c,`${c.id}-next`,'Next four books',canvases.get(epic.sections[i+1].key),720,y,285);
}

for(const work of hr.hesiod){const c=canvas(work.key,`Hesiod · ${work.title.replace(/^Hesiod[’']?s?\s*/i,'').slice(0,30)}`);heading(c,work.title,work.intro);homeLink(c,headingHeight(c)+75);const placed=grid(c,work.key,work.sections.map(s=>({title:s.title,text:s.text+cite(s.sourceIds)})),Math.max(380,headingHeight(c)+190),3,390,34);grid(c,`${work.key}-note`,work.notes.map(s=>({title:s.title,text:s.text+cite(s.sourceIds)})),placed.bottom,2,600,35);}
const rome=canvas('rome','Rome · Gods and counterparts');
heading(rome,hr.roman.title,hr.roman.intro);homeLink(rome,headingHeight(rome)+75);
const romanItems=hr.roman.comparisons.map(q=>({title:`${q.roman} ↔ ${q.greek}`,text:`Shared associations\n${q.shared}\n\nRoman distinctions\n${q.differences}\n\nA story or practice\n${q.story}`+cite(q.sourceIds)}));
const comparisons=grid(rome,'roman-comparison',romanItems,Math.max(400,headingHeight(rome)+210),3,430,35);
for(let i=0;i<hr.roman.comparisons.length;i++){const q=hr.roman.comparisons[i],target=godNodes.get(alias(q.greekKey));if(!target)continue;const original=comparisons.nodes[i];ref(rome,`roman-reference-${q.key}`,`${q.greek} · saved Greek identity`,target,1510+(i%2)*380,420+Math.floor(i/2)*280,350,240);}
const romanLife=canvas('roman-life','Rome · Ritual and household');heading(romanLife,'Roman contexts that resist a name chart','Greek correspondences were useful in Roman literature and art, but cult, civic institutions, household practices, and local divine names have their own histories. These notes keep comparison from turning into an assertion that Roman religion was simply Greek religion renamed.');homeLink(romanLife,245);launch(romanLife,'roman-chart','Open the comparison chart',rome,360,245,320);const native=grid(romanLife,'roman-native',hr.roman.native.map(q=>({title:q.title,text:q.text+cite(q.sourceIds)})),385,2,520,35);grid(romanLife,'roman-notes',hr.roman.notes.map(q=>({title:q.title,text:q.text+cite(q.sourceIds)})),native.bottom,2,520,35);launch(rome,'roman-context','Roman ritual and household',romanLife,365,headingHeight(rome)+75,360);

const gallery=canvas('gallery','Media · Museum and image gallery');heading(gallery,'Objects, dates and interpretations','Six images from The Metropolitan Museum of Art are embedded as their original downloaded JPEG bytes. Captions retain qualified identifications and distinguish ancient objects from modern reception. Dates and cultures describe the museum objects, not the date of a mythological event.');homeLink(gallery,245);
for(let i=0;i<media.length;i++){const m=media[i],x=55+(i%3)*490,y=380+Math.floor(i/3)*820;mediaNode(gallery,`gallery-${m.key}`,m.title,await asset(m.file,m.mime),x,y,455,430);t(gallery,`gallery-credit-${m.key}`,'Provenance and reading prompt',`${m.caption}\n\n${m.artist?m.artist+'\n':''}${m.date}; ${m.culture}. Object ${m.accession}.\n${m.credit}\n${m.url}\n${m.rights}`,x,y+455,455);}
const mediaLab=canvas('media-tests','Media · Files and test cases');heading(mediaLab,'A varied attachment collection','PNG, JPEG and WebP are core image cases. GIF, Markdown, SVG, WAV and JSON deliberately probe the boundary between native preview and a preserved download. The audio is an original test motif, not reconstructed ancient music. Every file stays embedded and is included in byte-for-byte package tests.');homeLink(mediaLab,245);
let fileRow=0;
for(const f of fixtures){const x=55+(fileRow%3)*440,y=390+Math.floor(fileRow/3)*680;mediaNode(mediaLab,`fixture-${f.file}`,f.title,await asset(f.file,f.mime),x,y,405,350);t(mediaLab,`fixture-note-${f.file}`,'Expected behavior',f.description+'\n\n'+f.rights,x,y+375,405);fileRow++;}

const lab=canvas('lab','Lab · References and Boolean logic');heading(lab,'A labelled test system, not a theory of myth','This toy model asks whether an imagined reading voyage is ready. Its switches do not decide what Homer meant. Use it to test all seven Boolean gates, a same-canvas live reference, a cross-canvas saved reference, explicit joints, undefined results, and collapse without stopping computation. Viewer Run starts evaluation; Stop preserves positions and wiring.');homeLink(lab,245);
const system=n(lab,'voyage-system','container','Voyage preparation · collapse me',55,405,1140,410);system.rules={nodeKinds:['input','switch','joint','output','reference'],linkKinds:['functional'],operations:['AND','OR','NOT','NAND','NOR','XOR','XNOR']};
const crew=n(lab,'crew-ready','input','Crew prepared',85,475,250,125,system);crew.value=true;crew.exposed=true;
const permitRef=ref(lab,'permit-reference','Archive reviewed · saved',permit,85,640,250,135,system);
const gate=n(lab,'voyage-and','joint','Both preparations',440,485,245,130,system);gate.operation='AND';
const sw=n(lab,'voyage-switch','switch','Enable voyage',825,485,300,135,system);sw.closed=true;sw.exposed=true;
const output=n(lab,'voyage-output','output','Voyage ready',1265,480,270,140);
link(crew,gate,'causal','functional',0);link(permitRef,gate,'causal','functional',1,[{x:385,y:710},{x:385,y:575}]);link(gate,sw,'causal','functional',0);link(sw,output,'causal','functional',0);
const live=ref(lab,'crew-live','Crew prepared · live mirror',crew,1265,680,270,140);t(lab,'lab-instructions','Try these three changes','1. Toggle Crew prepared. Its live mirror and Voyage ready update immediately.\n2. Open Enable voyage. Voyage ready becomes Unknown; closing it restores computation.\n3. In Begin here, change Archive reviewed without saving, then return. The saved reference stays unchanged until a successful editor save. Collapse Voyage preparation: the sibling output remains visible and keeps computing.',55,850,1110);
let truthY=1210;
for(let i=0;i<7;i++){const op=['AND','OR','NOT','NAND','NOR','XOR','XNOR'][i],x=55+(i%2)*810,y=truthY+Math.floor(i/2)*520;const box=n(lab,`truth-${op}`,'container',`${op} · truth-table exercise`,x,y,760,450);box.rules=system.rules;
  const a=n(lab,`truth-${op}-a`,'input',`${op} input A`,x+25,y+85,190,120,box);a.value=true;a.exposed=true;
  const g=n(lab,`truth-${op}-gate`,'joint',`${op} operation`,x+275,y+140,190,130,box);g.operation=op;
  const o=n(lab,`truth-${op}-output`,'output',`${op} result`,x+520,y+140,210,130,box);link(a,g,'causal','functional',0);link(g,o,'causal','functional',0);
  if(op!=='NOT'){const b=n(lab,`truth-${op}-b`,'input',`${op} input B`,x+25,y+265,190,120,box);b.value=false;b.exposed=true;link(b,g,'causal','functional',1);}
}
const undefinedNode=n(lab,'missing-input-output','output','Deliberately unwired output',55,3370,350,150);
t(lab,'undefined-note','Undefined is not false','The unwired output should read Unknown while running. It has no input, so there is no Boolean result. This deliberate incomplete case must not invent a value, interfere with other circuits, or prevent project saving.',450,3370,650);
const cycleNodes=['Warning','Choice','Consequence'].map((name,i)=>t(lab,`visual-cycle-${i}`,name,'A visual cycle used as a reading prompt. These lines are not functional signal connections.',55+i*450,3680,365));
cycleNodes.forEach((a,i)=>link(a,cycleNodes[(i+1)%3],'cyclical','visual',undefined,i===2?[{x:1320,y:3970},{x:20,y:3970}]:undefined));
const externalCanvasId=crypto.randomUUID(),externalId=crypto.randomUUID();
p.snapshots[externalId]={node:{id:externalId,canvasId:externalCanvasId,parentId:null,kind:'text',title:'Curator archive fixture',text:'This is an authored external-reference fixture. Only this saved note is packaged; its source canvas is deliberately absent. It makes the publication dependency switch testable without a remote service.',x:0,y:0,width:400,height:220},value:null,savedAt:new Date().toISOString()};
p.dependencies.push({canvasId:externalCanvasId,title:'Curator archive fixture',required:true});
const external=n(lab,'external-fixture','reference','Pinned curator note · external fixture',55,4140,650,265);external.targetId=externalId;
t(lab,'external-explanation','Publication dependency test','Publish with Download External References selected to include the saved curator note. Publish with it unselected to see an explicit missing-dependency notice. This fixture is separate from the mythology sources and never supplies a secret replacement value.',760,4140,640);

const sourceCanvas=canvas('sources','Sources · Reading register');heading(sourceCanvas,'Ancient passages and image provenance','Bracketed identifiers beside the narrative resolve in the full source register attached here. The summaries are newly written English paraphrases. Ancient works do not form a uniform canon; each citation identifies a telling. Public-domain translation status is recorded separately from museum image rights. No modern translation has been reproduced at length.');homeLink(sourceCanvas,245);
const register=[...sources.values()].map(s=>`[${s.id}] ${s.title}\nAuthor: ${s.author??''}\nPassage: ${textOf(s.locators??'')}\n${s.url}\nRights/provenance: ${s.rights??'See edition source.'}`).join('\n\n');
const imageRegister=media.map(m=>`${m.title}\n${m.date}; ${m.culture}; ${m.artist}\nObject ${m.accession}; ${m.credit}\n${m.url}\nImage: ${m.imageUrl}\n${m.rights}\nOriginal image SHA-256: ${m.sha256}`).join('\n\n');
await writeFile(path.join(content,'media','source-register.txt'),`GREEK MYTHOLOGY SOURCE REGISTER\n\n${register}\n\nMUSEUM IMAGES\n\n${imageRegister}`,'utf8');
await writeFile(path.join(content,'media','source-register.json'),JSON.stringify({sources:[...sources.values()],images:media},null,2),'utf8');
mediaNode(sourceCanvas,'source-register-file','Complete source register · TXT',await asset('source-register.txt','text/plain'),55,390,570,410);
mediaNode(sourceCanvas,'source-register-data','Source metadata · JSON',await asset('source-register.json','application/json'),690,390,500,235);
t(sourceCanvas,'source-method','How the sources are used','Book and line ranges identify the passage used, not a claim that every modern retelling agrees. The Iliad’s narrative stops at Hector’s funeral. The Odyssey stages much of its voyage through Odysseus’s own retrospective narration. Hesiod’s two principal poems have different aims. Roman names are compared through their literary and cultic contexts, with partial correspondences explicitly identified.\n\nTXT cards and published viewer previews show the first 3,000 characters. Open TXT in the editor for full text, or Download the complete file.',690,670,500);
let sourceY=nodes.get('source-method').y+nodes.get('source-method').height+60;
for(const work of [{title:'Homer and epic',ids:[...sources.keys()].filter(k=>/iliad|odyssey|homer/i.test(k))},{title:'Hesiod and the hymns',ids:[...sources.keys()].filter(k=>/hesiod|theog|works|hymn/i.test(k))},{title:'Other ancient and modern records',ids:[...sources.keys()].filter(k=>!/iliad|odyssey|homer|hesiod|theog|works|hymn/i.test(k))}]){
  const card=t(sourceCanvas,`source-group-${sourceY}`,work.title,work.ids.map(k=>`[${k}] ${sources.get(k).title}`).join('\n'),55,sourceY,1100);sourceY+=card.height+60;
}

mediaNode(home,'home-reading-compass','Four reading paths',await asset('reading-compass.png','image/png'),1170,45,580,430);

const dossierIndex=canvas('dossiers','Gods · Dossier directory');heading(dossierIndex,'Read the gods across traditions','Choose a profile below, or enter through the family tree. Each dossier groups stories inside a collapsible container and keeps a saved reference to the original family identity. These profiles include first beings and Titans as well as principal Olympian and underworld gods.');homeLink(dossierIndex,245);
let navY=600;const directory=[['origins','Origins and Titans'],['olympus','Olympian family tree'],['dossiers','Individual gods and stories'],['genealogy-notes','Genealogy variants and sources'],['iliad','The Iliad'],['odyssey','The Odyssey'],...hr.hesiod.map(w=>[w.key,w.title]),['rome','Roman gods and counterparts'],['roman-life','Roman ritual and household'],['gallery','Museum image gallery'],['media-tests','Documents and media tests'],['lab','References and Boolean lab'],['sources','Source register']];
let count=0;
for(const [key,title] of directory){if(!key)continue;launch(home,`home-nav-${key}`,title,canvases.get(key),55+(count%3)*370,navY+Math.floor(count/3)*125,335);count++;}
navY+=Math.ceil(count/3)*125+65;
t(home,'editorial-note','A living atlas and a test collection','The collection is intentionally detailed and has large canvases. Test zoom, navigation and saved references before editing the content. Images and attachments remain local and portable. Unsupported rich media are labelled download-only. Text attachments provide an alternative reading surface for long passages.',55,navY,1090);
let dossierY=380;
for(const group of gods.groups){const groupCard=t(dossierIndex,`group-${group.key}`,group.title,group.subtitle??'Open an individual dossier to follow the god through several ancient tellings.',55,dossierY,1090);dossierY+=groupCard.height+25;let j=0;for(const entry of group.entries){const c=godCanvases.get(alias(entry.key));launch(dossierIndex,`home-god-${entry.key}`,entry.title,c,55+(j%3)*370,dossierY+Math.floor(j/3)*115,335);j++;}dossierY+=Math.ceil(j/3)*115+50;}
grid(dossierIndex,'editorial-method',gods.editorialNotes.map((text,i)=>({title:`Reading principle ${i+1}`,text})),dossierY,2,525,35);
// A text attachment gives long passages a separate reading surface.
const corpus=p.canvases.map(c=>`\n\n${'='.repeat(72)}\n${c.title}\n${'='.repeat(72)}\n`+p.nodes.filter(n=>n.canvasId===c.id&&n.text).map(n=>`\n${n.title}\n${n.text}\n`).join('')).join('')+'\n\n'+register;
await writeFile(path.join(content,'media','collection-reader.txt'),corpus,'utf8');
mediaNode(mediaLab,'collection-reader','Whole collection reader · TXT',await asset('collection-reader.txt','text/plain'),55,1840,620,380);
try{mediaNode(mediaLab,'companion-pdf','Reader and test companion · PDF',await asset('mythology-companion.pdf','application/pdf'),730,1840,560,380);}catch(e){if(e.code!=='ENOENT')throw e;}

// Prime the initial source snapshots, then capture the resolved authored circuit.
// This is a new fixture, not an editor save or adoption of an unsaved draft.
p=commitSnapshots(commitSnapshots(p));
const issues=validateProject(p);if(issues.length)throw new Error(issues.map(i=>i.message).join('\n'));
const evaluation=evaluate(p);if(evaluation.values[output.id]!==true)throw new Error('Voyage circuit must begin true');
if(evaluation.values[undefinedNode.id]!==null)throw new Error('Unwired output must remain undefined');
const [js,css]=await Promise.all(['viewer.js','scene.css'].map(file=>readFile(path.join(root,'dist',file),'utf8')));
const publication=buildPublication(p,js,css,{includeExternalReferences:true});
const imported=readPublication(publication);if(imported.id===p.id||imported.nodes.some(n=>p.nodes.some(o=>o.id===n.id)))throw new Error('Publication must import with fresh node identities');
if(createPublicationPayload(p,{includeExternalReferences:false}).missing.length===0)throw new Error('External exclusion must remain observable');
await writeFile(path.join(out,'greek-mythology.project.json'),JSON.stringify(p),'utf8');
await writeFile(path.join(out,'greek-mythology.html'),publication,'utf8');
const nodeKeys=Object.fromEntries([...nodes].filter(([,v])=>v.id).map(([k,v])=>[k,v.id]));
const manifest={title:p.title,projectId:p.id,createdAt:new Date().toISOString(),canvases:p.canvases.map(c=>({key:[...canvases].find(([,v])=>v.id===c.id)?.[0],...c,nodes:p.nodes.filter(n=>n.canvasId===c.id).length})),nodes:p.nodes.length,links:p.links.length,assets:p.assets.map(a=>({id:a.id,name:a.name,mime:a.mime,bytes:Buffer.from(a.base64,'base64').length})),sources:sources.size,wordCount:corpus.split(/\s+/).length,nodeKeys,gateDefaults:Object.fromEntries(['AND','OR','NOT','NAND','NOR','XOR','XNOR'].map(op=>[op,evaluation.values[nodes.get(`truth-${op}-output`).id]]))};
await writeFile(path.join(out,'collection-manifest.json'),JSON.stringify(manifest,null,2),'utf8');
await writeFile(path.join(out,'embed-example.html'),`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Greek mythology embedded canvas</title><style>body{margin:0;background:#101c2b;color:#eee;font:16px system-ui}header{padding:20px}iframe{border:0;width:100%;height:85vh}</style><header><h1>Greek mythology</h1><p>An embedded Meridian publication. Navigate, zoom, open documents, and run the labelled logic lab.</p></header><iframe src="greek-mythology.html" title="Greek mythology Meridian collection" allowfullscreen></iframe></html>`,'utf8');
console.log(JSON.stringify({projectId:p.id,canvases:p.canvases.length,nodes:p.nodes.length,links:p.links.length,assets:p.assets.length,sources:sources.size,wordCount:manifest.wordCount},null,2));
