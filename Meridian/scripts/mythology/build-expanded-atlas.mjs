// A researched expansion of the visual atlas. Local files only; live installation is explicit.
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import epics from './epics-expanded.mjs';
import * as genealogy from './genealogy-expanded.mjs';
import { applyRecipe, inspectProject } from '../../src/core/authoring.ts';
import { commitSnapshots } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';
import { buildPublication, readPublication } from '../../src/publication.ts';

const root = fileURLToPath(new URL('../../', import.meta.url));
const out = path.join(root, 'examples/visual-authoring');
const corpus = path.join(root, 'content/greek-mythology');
const json = async name => JSON.parse(await readFile(name, 'utf8'));
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const originalPath = path.join(root, 'examples/greek-mythology/greek-mythology.project.json');
const originalHash = hash(await readFile(originalPath));
await mkdir(path.join(out, 'baseline-v1'), { recursive: true });
for (const suffix of ['project.json','aliases.json','html','recipe.json','manifest.json','meridian']) {
  const name = `visual-atlas.${suffix}`;
  try { await copyFile(path.join(out, name), path.join(out, 'baseline-v1', name), 1); }
  catch (error) { if (!['EEXIST','ENOENT'].includes(error.code)) throw error; }
}
const base = await json(path.join(out, 'visual-atlas.project.json'));
const oldAliases = await json(path.join(out, 'visual-atlas.aliases.json'));
const gods = await json(path.join(corpus, 'gods.json'));
const oldHesiod = await json(path.join(corpus, 'hesiod-rome.json'));
const media = await json(path.join(corpus, 'media.json'));
const profiles = gods.groups.flatMap(group => group.entries);
const recipe = { version: 1, title: 'Greek Mythology · Expanded reference atlas', entryCanvas: 'welcome', canvases: [], nodes: [], links: [], assets: [] };
const sources = new Map([...gods.sources,...oldHesiod.sources,...genealogy.sources].map(s=>[s.id,s]));
const palettes = {
  parchment: { background:'#F5F0E4', fill:'#FFFCF4', border:'#CFC4AC', textColor:'#243744', accent:'#9B673B' },
  sea: { background:'#122737', fill:'#203D4D', border:'#577583', textColor:'#E9F0EE', accent:'#D5B977' },
  wine: { background:'#281F2E', fill:'#423244', border:'#886D82', textColor:'#F5EADC', accent:'#D7B477' },
  olive: { background:'#EBEBDD', fill:'#FBFBF1', border:'#B2B895', textColor:'#2C3D32', accent:'#6A7C48' },
  clay: { background:'#EFE3D4', fill:'#FFF7EC', border:'#C4A283', textColor:'#49352B', accent:'#A55135' },
};
const canvasPalette = new Map();
const old = (group,key) => oldAliases[group]?.[key] ? { existingId:oldAliases[group][key] } : {};
const short = (s,n=180) => s.length > n ? s.slice(0,n).replace(/\s+\S*$/,'')+'…' : s;
const bookNumbers = keys => {
  const values=keys.map(k=>Number(k.slice(-2)));
  return values.length>2 && values.every((n,i)=>!i || n===values[i-1]+1)
    ? `${values[0]}–${values.at(-1)}` : values.join(', ');
};
const keyOf = name => name.normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLowerCase().replace(/[^a-z0-9]+/g,'-').replace(/^-|-$/g,'');
const chunks = (items,n) => Array.from({length:Math.ceil(items.length/n)},(_,i)=>items.slice(i*n,i*n+n));
function canvas(key,title,palette='parchment') {
  const p=palettes[palette]; canvasPalette.set(key,p);
  recipe.canvases.push({key,...old('canvases',key),title,style:{background:p.background,gridVisible:false}});
  return key;
}
function node(c,key,kind,title,text,x,y,width,height,extra={}) {
  const p=canvasPalette.get(c)||palettes.parchment;
  const {style,...rest}=extra;
  if(rest.target && kind==='reference' && oldAliases.nodes[rest.target])rest.target=oldAliases.nodes[rest.target];
  const entry={key,...old('nodes',key),canvas:c,parent:null,kind,title,text,x,y,width,height,
    style:{fill:p.fill,border:p.border,textColor:p.textColor,accent:p.accent,radius:12,fontSize:18,titleSize:24,...style},...rest};
  recipe.nodes.push(entry); return key;
}
function text(c,key,title,body,x,y,w=320,h=180,extra={}) { return node(c,key,'text',title,body,x,y,w,h,extra); }
function label(c,key,title,body,x,y,w=1200,h=120,extra={}) {
  const {style,...rest}=extra;
  return text(c,key,title,body,x,y,w,h,{style:{variant:'label',fontFamily:'serif',titleSize:44,...style},...rest});
}
function heading(c,title,subtitle) {
  label(c,`${c}-heading`,title,'',50,38,1260,120);
  label(c,`${c}-subtitle`,short(subtitle,290),'',54,163,1260,85,{style:{fontFamily:'sans',titleSize:18}});
}
function launch(c,key,title,target,body,x,y,w=310,h=170,extra={}) { return node(c,key,'canvas-link',title,body,x,y,w,Math.max(155,h),{target,...extra}); }
function link(c,key,source,target,label='',extra={}) {
  const {style,...rest}=extra;
  recipe.links.push({key,...old('links',key),canvas:c,source,target,kind:'visual',relation:'causal',label,
    style:{color:canvasPalette.get(c)?.accent||'#9B673B',width:2,...style},...rest});
}
const down={sourceAnchor:{x:.5,y:1},targetAnchor:{x:.5,y:0}};
const across={sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5}};
function asset(key,name,mime,bytes) { recipe.assets.push({key,...old('assets',key),name,mime,base64:Buffer.from(bytes).toString('base64')}); return key; }
function document(c,key,title,body,assetKey,x,y,w=310,h=170,extra={}) { return node(c,key,'document',title,body,x,y,w,h,{asset:assetKey,style:{titleSize:19,fontSize:13},...extra}); }
function sourcesText(refs=[]) {
  return refs.map(ref=>{
    const id=typeof ref==='string'?ref:ref.sourceId;
    const s=sources.get(id);
    return s ? `${s.author||''} · ${s.title}\n${typeof ref==='object'?ref.locator||'':''}\n${s.url}` : JSON.stringify(ref);
  }).join('\n\n');
}
function notes(c,key,title,body,refs,x=1080,y=930) {
  const a=asset(`asset-${key}`,`${key}.txt`,'text/plain',`${title}\n\n${body}\n\nANCIENT PASSAGES AND SOURCES\n${sourcesText(refs)}`);
  return document(c,key,title,'Open the complete, searchable reading notes.',a,x,y);
}
function back(c,target='welcome',x=50,y=1070) { launch(c,`${c}-back`,'← Back to overview',target,'Return to this section’s reading map.',x,y,270,120,{style:{titleSize:19,fontSize:14}}); }
for (const [key,palette] of [['welcome','sea'],['family','parchment'],['odyssey','sea'],['rome','parchment'],['persephone','wine']]) {
  const existing=base.canvases.find(c=>c.id===oldAliases.canvases[key]); canvas(key,existing.title,palette);
}
for (const m of media) {
  const bytes=await readFile(path.join(corpus,'media',m.file)); assert.equal(hash(bytes),m.sha256);
  asset(`art-${m.key}`,m.file,m.mime,bytes);
}
function image(c,key,art,title,x,y,w=280,h=340,extra={}) {
  const m=media.find(m=>m.key===art); assert.ok(m);
  return node(c,key,'image',title,`${m.culture}; ${m.date}. Met ${m.accession}.\n${m.caption}`,x,y,w,h,
    {asset:`art-${art}`,style:{fontSize:12,titleSize:19,imageFit:'contain'},...extra});
}

// Preserve the original five designed compositions, then connect them to substantial depth.
text('family','family-kicker','KINSHIP / EXPLORE THE PRIMORDIAL, TITAN, DIVINE AND HEROIC BRANCHES','',50,24,1200,38,{style:{variant:'label',fontSize:13,titleSize:13}});
text('family','family-premise','A doorway into the family','Hesiod’s six children of Cronus and Rhea anchor this overview. Follow the branch canvases below for earlier generations, consorts and descendants.',50,190,270,190,{style:{fill:'#E8E0CD',titleSize:19,fontSize:16}});
launch('family','family-branches','Explore the full genealogy','genealogy-index','Primordials, all twelve Titans, divine children and heroes; named source variants.',560,1060,440,170);
launch('family','family-zeus-branches','Zeus’s children','zeus-index','Individuals, collective powers and source variants.',1030,1060,310,180);
launch('odyssey','odyssey-complete','Read all 24 books','odyssey-index','Book-by-book narrative, event chains, themes and complete source text.',440,1070,470,160);
launch('rome','rome-complete','Greek and Roman gods','roman-index','The correspondence atlas: shared attributes, distinct cults and Roman contexts.',650,1090,400,160);
launch('persephone','persephone-profile','Demeter and Persephone','profile-demeter','Stories, source passages and their extended family.',420,1070,450,160);

// Reading-room attachments retain their full original files, including source license notices.
const fullTexts={};
for(const [key,name] of [['iliad','iliad-butler-full.txt'],['odyssey','odyssey-butler-full.txt'],['hesiod','hesiod-hymns-evelyn-white-full.txt']]) {
  const bytes=await readFile(path.join(corpus,'texts',name)); fullTexts[key]=bytes.toString('utf8');
  asset(`full-${key}`,name,'text/plain',bytes);
}
canvas('reading-room','90 · Reading room & source register','olive');
heading('reading-room','Read the source, not just the map','Complete translations, original notes, museum provenance, and a clear boundary between text and interpretation.');
for(const [i,key] of ['iliad','odyssey','hesiod'].entries()) {
  const title={iliad:'The Iliad · complete',odyssey:'The Odyssey · complete',hesiod:'Hesiod, hymns & fragments'}[key];
  document('reading-room',`reading-${key}`,title,'Full Project Gutenberg text. Original translator’s names and notes retained.',`full-${key}`,50+i*445,280,410,400);
}
text('reading-room','translation-guide','Names are part of the translation','Samuel Butler often uses Jove, Juno, Minerva, Neptune and Ulysses. Our explanatory canvases use Zeus, Hera, Athena, Poseidon and Odysseus. These naming choices do not turn Homer’s Greek setting into a Roman one. Butler’s prefatory theories are historical editorial opinions, not premises of this atlas.',50,730,840,250,{style:{titleSize:29,fontSize:20}});
notes('reading-room','reading-provenance','Edition and artwork provenance',media.map(m=>`${m.title}\n${m.credit}; ${m.rights}\n${m.url}\nSHA256 ${m.sha256}`).join('\n\n')+'\n\nComplete texts: Homer translated by Samuel Butler, Project Gutenberg2199 and1727; Hesiod and Homerica translated by H.G. Evelyn-White, Project Gutenberg348. Source files include their license and transcription credits.\nhttps://www.gutenberg.org/ebooks/2199\nhttps://www.gutenberg.org/ebooks/1727\nhttps://www.gutenberg.org/ebooks/348',[],970,730);
back('reading-room');

// Every epic book gets a reading canvas: narrative, an event chain, interpretive lens, and complete text.
for(const workKey of ['iliad','odyssey']) {
  const work=epics[workKey], palette=workKey==='iliad'?'clay':'sea';
  canvas(`${workKey}-index`,`${workKey==='iliad'?'30':'40'} · ${work.title} · all 24 books`,palette);
  heading(`${workKey}-index`,work.title,work.overview);
  image(`${workKey}-index`,`${workKey}-art`,workKey==='iliad'?'thetis':'circe',workKey==='iliad'?'Thetis and heroic arms':'Odysseus and Circe',1090,270,270,330);
  text(`${workKey}-index`,`${workKey}-scope`,'What this poem does and does not tell',(work.scopeNotes||[]).join(' '),50,270,960,300,{style:{titleSize:25,fontSize:19}});
  for(const [i,m] of work.movements.entries()) {
    const nums=m.books??Array.from({length:4},(_,j)=>i*4+j+1);
    const start=typeof nums[0]==='number'?nums[0]:i*4+1;
    const key=`${workKey}-movement-${i+1}`;
    canvas(key,`${workKey==='iliad'?'31':'41'}.${i+1} · ${work.title} ${start}–${start+3}`,palette);
    heading(key,m.title,typeof m.summary==='string'?m.summary:(m.overview||`Books ${start}–${start+3}`));
    const x=50+(i%3)*340,y=630+Math.floor(i/3)*280;
    launch(`${workKey}-index`,`${key}-portal`,`${String(start).padStart(2,'0')}–${start+3} · ${m.title}`,key,short(m.summary||m.overview||'',100),x,y,310,240,{style:{titleSize:23,fontSize:17}});
    if(i)link(`${workKey}-index`,`${workKey}-movement-order-${i}`,`${workKey}-movement-${i}-portal`,`${key}-portal`,'',i===3?{...down,points:[{x:885,y:890},{x:205,y:890}]}:across);
    for(let j=0;j<4;j++) {
      const b=work.books[start+j-1],bx=50+(j%2)*680,by=270+Math.floor(j/2)*380;
      launch(key,`${key}-book-${b.number}`,`BOOK ${b.number} · ${b.title}`,b.key,short(b.summary,300),bx,by,630,310,{style:{titleSize:29,fontSize:21}});
      if(j)link(key,`${key}-sequence-${j}`,`${key}-book-${start+j-1}`,`${key}-book-${b.number}`,'',j===2?{...down,points:[{x:1045,y:615},{x:365,y:615}]}:across);
    }
    back(key,`${workKey}-index`,50,1080);
  }
  back(`${workKey}-index`,'atlas-directory',50,1200);
  launch(`${workKey}-index`,`${workKey}-texts`,'Complete translation','reading-room','Full text and edition notes.',1090,810,270,245);
  const headings=[...fullTexts[workKey].matchAll(/^BOOK [IVXLC]+\.?\r?$/gm)]; assert.equal(headings.length,24);
  for(const b of work.books) {
    canvas(b.key,`${workKey==='iliad'?'I':'O'}${String(b.number).padStart(2,'0')} · ${b.title}`,palette);
    heading(b.key,`${work.title} · Book ${b.number}`,b.title);
    const main=text(b.key,`${b.key}-account`,'The account',b.summary,430,270,720,660,{style:{fontFamily:'serif',titleSize:34,fontSize:22}});
    const events=b.events||[];
    for(const [i,event] of events.entries()) {
      const title=typeof event==='string'?`${i+1}`:event.title;
      const body=typeof event==='string'?event:event.text||event.summary;
      text(b.key,`${b.key}-event-${i}`,title,body,50,270+i*142,330,125,{style:{fontSize:17,titleSize:21}});
      if(i)link(b.key,`${b.key}-event-sequence-${i}`,`${b.key}-event-${i-1}`,`${b.key}-event-${i}`,'',down);
    }
    text(b.key,`${b.key}-people`,'People and powers',b.characters.join(' · '),1190,270,310,200,{style:{titleSize:25,fontSize:20}});
    const lens=node(b.key,`${b.key}-lens`,'container','Interpretive lens','Expand for themes. These are reading aids, not extra events in the poem.',1190,510,340,360,{collapsed:true,style:{titleSize:23,fontSize:17}});
    const themeText=b.themes.map(t=>typeof t==='string'?t:JSON.stringify(t)).join(' · ');
    text(b.key,`${b.key}-themes`,'Themes to trace',`${themeText}\n\nNarrative position: ${b.narration}.`,1210,700,300,150,{parent:lens,style:{fontSize:17,titleSize:21}});
    const start=headings[b.number-1].index,end=headings[b.number]?.index??fullTexts[workKey].indexOf('*** END OF THE PROJECT GUTENBERG');
    const bookText=fullTexts[workKey].slice(start,end>start?end:undefined);
    const a=asset(`${b.key}-fulltext`,`${b.key}-complete-butler.txt`,'text/plain',`Homer, ${work.title}, Book ${b.number}\nTranslated by Samuel Butler. Excerpted without modernizing names from Project Gutenberg ${workKey==='iliad'?'2199':'1727'}.\nThe complete edition and original license are in the atlas Reading Room.\n${b.sourceUrl}\n\n${bookText}`);
    document(b.key,`${b.key}-source`,'Read the entire book','Full translation, not a preview-length summary.',a,1190,910,340,180);
    for(const [i,related] of (b.links||[]).slice(0,2).entries()) {
      const dest=[...epics.iliad.books,...epics.odyssey.books].find(book=>book.key===related.target);
      launch(b.key,`${b.key}-related-${i}`,related.label,related.target,`${dest.key.startsWith('iliad')?'Iliad':'Odyssey'} ${dest.number} · ${dest.title}`,430+i*370,950,345,140,{style:{titleSize:19,fontSize:15}});
      link(b.key,`${b.key}-related-link-${i}`,main,`${b.key}-related-${i}`,related.relation||'compare',{...down,style:{dashed:true,width:1}});
    }
    back(b.key,`${workKey}-index`,50,1150);
    if(b.number>1)launch(b.key,`${b.key}-previous`,'← Previous book',work.books[b.number-2].key,work.books[b.number-2].title,370,1150,300,125,{style:{titleSize:20,fontSize:16}});
    if(b.number<24)launch(b.key,`${b.key}-next`,'Next book →',work.books[b.number].key,work.books[b.number].title,710,1150,300,125,{style:{titleSize:20,fontSize:16}});
    launch(b.key,`${b.key}-movement`,'Four-book overview',`${workKey}-movement-${Math.floor((b.number-1)/4)+1}`,'See this book in context.',1050,1150,340,125,{style:{titleSize:20,fontSize:16}});
    link(b.key,`${b.key}-account-themes`,main,lens,'',{...across,style:{dashed:true}});
  }
}

// Compare reading order with narrated event order, without inventing modern geographical coordinates.
canvas('odyssey-chronology','42 · Odyssey · telling and journey','sea');
heading('odyssey-chronology','A return told out of order','The poem starts near the journey’s end. Odysseus then speaks his earlier adventures inside the Phaeacian frame.');
for(const [i,frame] of epics.odyssey.narrationMap.entries()) {
  const y=290+i*180;
  label('odyssey-chronology',`odyssey-frame-label-${i}`,`BOOKS ${frame.books[0]}–${frame.books.at(-1)}`,'',50,y+25,270,90,{style:{titleSize:26}});
  text('odyssey-chronology',`odyssey-frame-${i}`,frame.mode,frame.note,360,y,960,140,{style:{titleSize:27,fontSize:22,fill:i===2?'#51445B':'#203D4D'}});
  if(i)link('odyssey-chronology',`odyssey-frame-order-${i}`,`odyssey-frame-${i-1}`,`odyssey-frame-${i}`,'narration order',down);
}
const journeyPages=chunks(epics.odyssey.chronology,6);
for(const [i,steps] of journeyPages.entries()) {
  const c=`odyssey-journey-${i+1}`;canvas(c,`42.${i+1} · Odyssey · journey sequence`,'sea');heading(c,'The route within the story',`Events ${steps[0].order}–${steps.at(-1).order} · A narrated sequence, not an archaeological itinerary.`);
  launch('odyssey-chronology',`${c}-portal`,`Journey ${steps[0].order}–${steps.at(-1).order}`,c,`${steps[0].title} → ${steps.at(-1).title}`,50+i*445,1080,405,200,{style:{titleSize:27,fontSize:19}});
  for(const [j,step] of steps.entries()) {
    const col=j<3?j:5-j,x=50+col*445,y=300+Math.floor(j/3)*370;
    const key=`${c}-step-${step.order}`;
    launch(c,key,`${step.order} · ${step.title}`,step.books[0],`${step.note}\n\nTold in Book${step.books.length>1?'s':''} ${bookNumbers(step.books)}.`,x,y,405,280,{style:{titleSize:25,fontSize:21}});
    if(j)link(c,`${c}-order-${j}`,`${c}-step-${steps[j-1].order}`,key,'',j===3?down:j>3?{sourceAnchor:{x:0,y:.5},targetAnchor:{x:1,y:.5}}:across);
  }
  back(c,'odyssey-chronology');
  if(i<journeyPages.length-1)launch(c,`${c}-next`,'Continue the journey →',`odyssey-journey-${i+2}`,'The next stage in event order.',780,1070,530,150);
}
back('odyssey-chronology','odyssey-index',50,1340);
launch('odyssey-index','odyssey-chronology-portal','Telling versus journey','odyssey-chronology','The frame, retrospective and return.',370,1200,450,170,{style:{titleSize:27,fontSize:19}});
launch('odyssey-chronology','odyssey-designed-route','A visual journey map','odyssey','Return to the original illustrated route through selected encounters.',420,1340,900,170,{style:{titleSize:29,fontSize:21}});
canvas('epic-comparisons','45 · Read the epics together','wine');
heading('epic-comparisons','Read across the poems','Comparisons are interpretive questions. Their links take you directly to the books where the evidence can be read.');
for(const [i,comparison] of epics.comparisons.entries()) {
  const c=comparison.key;canvas(c,`45.${i+1} · ${comparison.title}`,'wine');heading(c,comparison.title,'A thematic constellation across books and poems.');
  launch('epic-comparisons',`${c}-portal`,comparison.title,c,comparison.question,50+(i%3)*445,290+Math.floor(i/3)*290,405,245,{style:{titleSize:26,fontSize:19}});
  text(c,`${c}-question`,'Read, compare, question',`${comparison.text}\n\n${comparison.question}`,460,460,550,410,{style:{titleSize:32,fontSize:24,fontFamily:'serif'}});
  const positions=[[50,290],[1050,290],[50,730],[1050,730],[555,960],[555,260]];
  for(const [j,bookKey] of comparison.books.entries()) {
    const book=[...epics.iliad.books,...epics.odyssey.books].find(b=>b.key===bookKey),[x,y]=positions[j];
    launch(c,`${c}-book-${j}`,`${bookKey.startsWith('iliad')?'Iliad':'Odyssey'} ${book.number}`,bookKey,book.title,x,y,350,195,{style:{titleSize:29,fontSize:21}});
    link(c,`${c}-evidence-${j}`,`${c}-book-${j}`,`${c}-question`,'compare',{relation:'mutual',style:{color:'#AF939F',width:1.5}});
  }
  back(c,'epic-comparisons',50,1220);
}
back('epic-comparisons','welcome',50,320+Math.ceil(epics.comparisons.length/3)*290);
for(const workKey of ['iliad','odyssey'])launch(`${workKey}-index`,`${workKey}-compare`,'Read across the epics','epic-comparisons','Anger, fame, hospitality, recognition, death and endings.',870,1200,490,170,{style:{titleSize:27,fontSize:19}});

// The family is a network of attributed accounts. Repeated appearances are saved references.
const people=new Map(genealogy.persons.map(p=>[p.key,p]));
const canonical=new Map(['cronus','rhea','hestia','demeter','hera','hades','poseidon','zeus','persephone'].map(k=>[k,`family-${k}`]));
const retiredOriginals=new Set();
// Original identities are independent of pagination; reference appearances belong
// to their individual maps. Migrate first-edition originals without changing IDs.
for(const person of genealogy.persons){
  if(canonical.has(person.key)){
    for(const [key,id]of Object.entries(oldAliases.nodes))if(key.endsWith(`-person-${person.key}`)&&base.nodes.find(n=>n.id===id)?.kind==='text'){
      retiredOriginals.add(id);delete oldAliases.nodes[key];
    }
    continue;
  }
  const stable=`original-person-${person.key}`;
  const previous=Object.entries(oldAliases.nodes).find(([key,id])=>key.endsWith(`-person-${person.key}`)&&base.nodes.find(n=>n.id===id)?.kind==='text');
  if(previous){
    oldAliases.nodes[stable]=previous[1];
    if(previous[0]!==stable)delete oldAliases.nodes[previous[0]];
  }
}
const branchPages=[];
canvas('genealogy-index','10 · Families · primordial to heroic','parchment');
heading('genealogy-index','The family is a network','Follow a branch, inspect its source, and keep alternative parentages separate. Kinship arrows are visual relationships, never logic signals.');
image('genealogy-index','genealogy-zeus-art','zeus','A probable Zeus',1110,265,250,330);
text('genealogy-index','genealogy-method','How to read these families','Follow a branch to see its selected parentage. Dashed alternative edges and the source notes distinguish other traditions. References return to one original figure. The catalog includes named individuals, unnamed parents and collective powers; counts describe records, not one definitive ancient census.',50,270,990,290,{style:{fontSize:21,titleSize:29}});
const branchShelves=chunks(genealogy.branches,6);
const branchDestinations=new Map();
for(const [i,shelf] of branchShelves.entries()) {
  const c=`genealogy-shelf-${i+1}`;
  canvas(c,`10.${i+1} · Family branch directory`,'parchment');
  heading(c,'Choose a family branch',shelf.map(b=>b.title).join(' · '));
  launch('genealogy-index',`${c}-portal`,`Family branches ${i*6+1}–${i*6+shelf.length}`,c,short(shelf.map(b=>b.title).join(' · '),170),50+(i%3)*445,640+Math.floor(i/3)*260,405,225,{style:{titleSize:26,fontSize:17}});
  for(const [j,branch] of shelf.entries())branchDestinations.set(branch.key,{canvas:c,index:j});
  back(c,'genealogy-index');
}
for(const [branchIndex,branch] of genealogy.branches.entries()) {
  const personKeys=[...new Set(branch.personKeys)].filter(k=>people.has(k));
  const parentKeys=k=>[...people.get(k).parents,...(people.get(k).variants||[]).flatMap(v=>v.parents)].filter(k=>people.has(k));
  const pages=[];
  let pageKeys=[];
  for(const key of personKeys){
    const candidate=[...pageKeys,key],context=new Set([...candidate,...candidate.flatMap(parentKeys)]);
    if(pageKeys.length && branch.key!=='titans-twelve' && (context.size>12 || candidate.length>8)){pages.push(pageKeys);pageKeys=[];}
    pageKeys.push(key);
  }
  if(pageKeys.length)pages.push(pageKeys);
  for(const [pageIndex,selectedKeys] of pages.entries()) {
    const c=`branch-${branch.key}-${pageIndex+1}`;
    canvas(c,`G${String(branchIndex+1).padStart(2,'0')}.${pageIndex+1} · ${branch.title}`,branch.key.includes('sea')||branch.key==='nereids'?'sea':'parchment');
    heading(c,branch.title,`Arrows: selected parents. Dashed purple: alternative parentage. ${short(branch.intro,180)} ${pages.length>1?`Part ${pageIndex+1} of ${pages.length}.`:''}`);
    const selected=new Set(selectedKeys);
    const externalParents=[...new Set(selectedKeys.flatMap(parentKeys))].filter(k=>!selected.has(k));
    const all=[...externalParents,...selectedKeys];
    const inPage=new Set(all), depths=new Map();
    function depth(k,seen=new Set()) {
      if(depths.has(k))return depths.get(k);
      if(seen.has(k)||!selected.has(k))return 0;
      const next=new Set(seen).add(k), parents=people.get(k).parents.filter(p=>inPage.has(p)&&p!==k);
      const value=parents.length?1+Math.max(...parents.map(p=>depth(p,next))):0;
      depths.set(k,value);return value;
    }
    const levels=new Map();
    for(const k of all){const d=depth(k);if(!levels.has(d))levels.set(d,[]);levels.get(d).push(k);}
    const columns=branch.key==='titans-twelve'?6:4;
    let y=290; const local=new Map(),positions=new Map();
    for(const [d,keys] of [...levels].sort((a,b)=>a[0]-b[0])) {
      for(const [i,k] of keys.entries()) {
        const p=people.get(k),x=50+(i%columns)*350,ny=y+Math.floor(i/columns)*270;
        const nk=canonical.has(k)?`${c}-person-${k}`:`original-person-${k}`;
        const parentNames=p.parents.map(k=>people.get(k)?.name||k).join(' + ');
        const body=short(p.summary,100);
        if(canonical.has(k))node(c,nk,'reference',p.name,'',x,ny,300,220,{target:canonical.get(k),style:{fontSize:15,titleSize:24}});
        else {text(c,nk,p.name,body,x,ny,300,220,{style:{fontSize:15,titleSize:24,fill:canvasPalette.get(c).background==='#122737'?'#284B58':(p.category==='titan'?'#E9D4AF':'#FFFCF4')}});canonical.set(k,nk);}
        local.set(k,nk);positions.set(k,{x:x+150,y:ny});
      }
      y+=Math.ceil(keys.length/columns)*270+45;
    }
    // A parent route leaves its row in a clear band and descends outside all cards.
    // It cannot appear to emerge from an unrelated intervening person's card.
    const parentLanes=[...new Set(selectedKeys.flatMap(parentKeys))];
    const route=(parent,child,variant=false)=>{
      const start=positions.get(parent),target=positions.get(child),lane=parentLanes.indexOf(parent);
      const left=lane%2===0,gutter=left?0-Math.floor(lane/2)*16:columns*350+40+Math.floor(lane/2)*16;
      const sourceY=start.y+220+10+(lane%3)*7,targetY=target.y-12-(lane%3)*7;
      const port=variant?.72:.5,targetX=target.x+(port-.5)*300;
      return {sourceAnchor:{x:.5,y:1},targetAnchor:{x:port,y:0},points:[{x:start.x,y:sourceY},{x:gutter,y:sourceY},{x:gutter,y:targetY},{x:targetX,y:targetY}]};
    };
    for(const k of selectedKeys) {
      const p=people.get(k),target=positions.get(k);
      for(const parent of p.parents)if(local.has(parent)) {
        link(c,`${c}-parent-${parent}-${k}`,local.get(parent),local.get(k),'',{
          ...route(parent,k),style:{width:1.5,dashed:false}});
      }
      for(const [variantIndex,v] of (p.variants||[]).entries())for(const parent of v.parents)if(local.has(parent)&&!p.parents.includes(parent)) {
        link(c,`${c}-variant-${parent}-${k}-${variantIndex}`,local.get(parent),local.get(k),'',{...route(parent,k,true),style:{dashed:true,color:'#927398',width:1.5}});
      }
    }
    const fullNotes=selectedKeys.map(k=>{
      const p=people.get(k);
      return `${p.name} (${p.category})\n${p.summary}\nSelected parents: ${p.parents.map(k=>people.get(k)?.name||k).join(' + ')||'Not specified in the selected account'}\n${sourcesText(p.sourceRefs)}\n${(p.variants||[]).map(v=>`ALTERNATIVE: ${v.note}\nParents: ${v.parents.map(k=>people.get(k)?.name||k).join(' + ')}\n${sourcesText(v.sourceRefs)}`).join('\n\n')}`;
    }).join('\n\n----------------\n\n');
    notes(c,`${c}-notes`,'People, parents & passages',fullNotes,branch.sourceRefs,1070,y+20);
    back(c,branchDestinations.get(branch.key).canvas,50,y+20);
    if(pageIndex>0)launch(c,`${c}-previous`,'← Earlier figures',`branch-${branch.key}-${pageIndex}`,'Previous part of this branch.',360,y+20,300,130);
    if(pageIndex<pages.length-1)launch(c,`${c}-next`,'More figures →',`branch-${branch.key}-${pageIndex+2}`,'Next part of this branch.',710,y+20,300,130);
    branchPages.push({key:c,branch,part:pageIndex+1,total:pages.length,count:selectedKeys.length});
  }
  const shelf=branchDestinations.get(branch.key);
  if(pages.length)launch(shelf.canvas,`genealogy-branch-${branch.key}`,branch.title,`branch-${branch.key}-1`,`${personKeys.length} entries · ${pages.length} map${pages.length>1?'s':''}\n${short(branch.intro,160)}`,50+(shelf.index%3)*445,300+Math.floor(shelf.index/3)*330,405,280,{style:{titleSize:26,fontSize:19}});
}
// Ensure catalog records not selected by a named branch are still accessible and citable.
const uncatalogued=genealogy.persons.filter(p=>!canonical.has(p.key));
for(const [i,group] of chunks(uncatalogued,12).entries()) {
  const c=`genealogy-register-${i+1}`;canvas(c,`G99.${i+1} · Further named figures`);heading(c,'Further named figures','Partner, parent and regional names supporting the attributed genealogies.');
  group.forEach((p,j)=>{const k=`catalog-${p.key}`;text(c,k,p.name,p.summary,50+(j%3)*445,280+Math.floor(j/3)*300,405,265,{style:{fontSize:18,titleSize:27}});canonical.set(p.key,k);});
  notes(c,`${c}-notes`,'Supporting figures and sources',group.map(p=>`${p.name}\n${p.summary}\n${sourcesText(p.sourceRefs)}\n${(p.variants||[]).map(v=>`${v.note}\n${sourcesText(v.sourceRefs)}`).join('\n\n')}`).join('\n\n'),[],1070,1580);
  back(c,'genealogy-index',50,1580);
  if(i>0)launch(c,`${c}-previous`,'← Previous names',`genealogy-register-${i}`,'Earlier supporting figures.',380,1580,340,150);
  if((i+1)*12<uncatalogued.length)launch(c,`${c}-next`,'Further names →',`genealogy-register-${i+2}`,'Continue the supporting register.',740,1580,290,150);
}
const genealogyBottom=640+Math.ceil(branchShelves.length/3)*260;
back('genealogy-index','welcome',50,genealogyBottom);
launch('genealogy-index','genealogy-zeus-index','Zeus: offspring register','zeus-index','Selected and alternative accounts, with mothers and source locators.',420,genealogyBottom,460,180);
if(uncatalogued.length)launch('genealogy-index','genealogy-supporting','Supporting names','genealogy-register-1',`${uncatalogued.length} further parents, partners and regional figures.`,920,genealogyBottom,410,180);

canvas('zeus-index','11 · Zeus · children, mothers & variants','parchment');
heading('zeus-index','The children of Zeus','An attributed register, not a claim that every local tradition agrees. Divine groups are expanded into named figures.');
const zeusChildren=genealogy.persons.filter(p=>p.parents.includes('zeus')||p.variants?.some(v=>v.parents.includes('zeus')));
const zeusCollectives=zeusChildren.filter(p=>p.category==='collective').length;
text('zeus-index','zeus-register-count',`${zeusChildren.length} offspring records in this atlas`,`${zeusChildren.length-zeusCollectives} individual figures and ${zeusCollectives} named collectives. The branch maps distinguish selected parentage from alternatives. Repeated appearances are references, not additional children.`,50,280,840,190,{style:{titleSize:32,fontSize:21}});
node('zeus-index','zeus-original','reference','ZEUS','',1020,270,330,230,{target:'family-zeus',style:{titleSize:38,fontSize:20}});
const zeusBranches=genealogy.branches.filter(b=>b.key.startsWith('zeus-'));
for(const [i,b] of zeusBranches.entries())launch('zeus-index',`zeus-portal-${b.key}`,b.title,`branch-${b.key}-1`,`${b.personKeys.length} entries\n${short(b.intro,140)}`,50+(i%2)*680,550+Math.floor(i/2)*250,625,230,{style:{titleSize:28,fontSize:19}});
const zeusText=zeusChildren.map(p=>`${p.name}\n${p.summary}\n${p.parents.includes('zeus')?'SELECTED: '+p.parents.map(k=>people.get(k)?.name||k).join(' + '):'Zeus parentage is an ALTERNATIVE account.'}\n${sourcesText(p.sourceRefs)}\n${(p.variants||[]).filter(v=>v.parents.includes('zeus')).map(v=>`${v.parents.map(k=>people.get(k)?.name||k).join(' + ')}: ${v.note}\n${sourcesText(v.sourceRefs)}`).join('\n')}`).join('\n\n');
notes('zeus-index','zeus-offspring-register','Complete Zeus register for this atlas',zeusText,[],1040,550+Math.ceil(zeusBranches.length/2)*250);
back('zeus-index','genealogy-index',50,550+Math.ceil(zeusBranches.length/2)*250);

// God dossiers keep stories inside real expandable parent nodes, with connected family appearances.
canvas('god-index','20 · Gods · stories and sources','wine');
heading('god-index','Gods in their stories','Power is expressed through a particular telling. Open a dossier, expand a story, and follow its family context.');
const dossierDestinations=new Map();
for(const [i,group] of gods.groups.entries()) {
  const c=`god-shelf-${i+1}`;canvas(c,`20.${i+1} · ${group.title}`,'wine');heading(c,group.title,'Each dossier combines stories, family appearances, descendants and ancient passages.');
  launch('god-index',`${c}-portal`,group.title,c,group.entries.map(p=>p.title).join(' · '),50+(i%3)*445,300+Math.floor(i/3)*330,405,280,{style:{titleSize:27,fontSize:20}});
  for(const [j,p] of group.entries.entries())dossierDestinations.set(p.key,{canvas:c,index:j});
  back(c,'god-index',50,300+Math.ceil(group.entries.length/3)*330);
}
for(const [i,p] of profiles.entries()) {
  const c=`profile-${p.key}`,palette=i%3===0?'sea':i%3===1?'parchment':'wine';
  canvas(c,`D${String(i+1).padStart(2,'0')} · ${p.title}`,palette);heading(c,p.title,p.summary);
  const shelf=dossierDestinations.get(p.key);
  launch(shelf.canvas,`god-index-${p.key}`,p.title,c,short(p.summary,145),50+(shelf.index%3)*445,290+Math.floor(shelf.index/3)*330,405,280,{style:{titleSize:28,fontSize:20}});
  const art=media.find(m=>m.key===p.key);
  if(art)image(c,`${c}-art`,art.key,art.title,1030,290,340,330);
  if(canonical.has(p.key))node(c,`${c}-family`,'reference','Family context','',50,285,415,240,{target:canonical.get(p.key),style:{titleSize:28,fontSize:17}});
  const children=genealogy.persons.filter(person=>person.parents.includes(p.key)||person.variants?.some(v=>v.parents.includes(p.key)));
  const childPreview=children.length>12?`${children.length} offspring records across selected and alternative accounts.\n\n${children.slice(0,8).map(p=>p.name).join(' · ')}…\n\nThe full register is attached below.`:children.map(p=>p.name).join(' · ');
  text(c,`${c}-children`,'Children and traditions',children.length?childPreview:(['athena','artemis','hestia'].includes(p.key)?'No biological children in the principal traditions used here. Athena’s care of Erichthonius is distinct from biological maternity.':'Consult the genealogy maps for the selected lineage and its alternatives.'),505,285,465,285,{style:{titleSize:26,fontSize:18}});
  if(children.length>12)notes(c,`${c}-children-register`,'Full descendant register',children.map(child=>`${child.name}\n${child.summary}\nSelected parents: ${child.parents.map(k=>people.get(k)?.name||k).join(' + ')}\n${sourcesText(child.sourceRefs)}\n${(child.variants||[]).map(v=>`${v.note}\n${sourcesText(v.sourceRefs)}`).join('\n\n')}`).join('\n\n'),[],50,580);
  const storyY=children.length>12?830:690;
  for(const [j,s] of p.stories.entries()) {
    const x=50+j*690,y=storyY;
    const parent=node(c,`${c}-story-${j}`,'container',s.title,short(s.text,140),x,y,640,480,{style:{titleSize:26,fontSize:18}});
    text(c,`${c}-story-detail-${j}`,'The telling',s.text,x+25,y+180,590,260,{parent,style:{fontSize:19,titleSize:23}});
    link(c,`${c}-story-link-${j}`,`${c}-children`,parent,'related story',{...down,style:{dashed:true}});
  }
  notes(c,`${c}-sources`,'Stories and ancient passages',`${p.summary}\n\n${p.stories.map(s=>`${s.title}\n${s.text}\n${sourcesText(s.sourceIds)}`).join('\n\n')}`,p.sourceIds,1060,storyY+550);
  back(c,shelf.canvas,50,storyY+550);launch(c,`${c}-genealogy`,'Explore the genealogy','genealogy-index','Parents, descendants and alternative accounts.',400,storyY+550,420,160);
}
back('god-index','welcome',50,300+Math.ceil(gods.groups.length/3)*330);

canvas('roman-index','50 · Greek and Roman correspondences','clay');
heading('roman-index','Greek names. Roman worlds.','Identification is a historical relationship, not a rule that two cults or narratives were interchangeable.');
image('roman-index','roman-jupiter-art','jupiter','Zeus or Jupiter',1090,265,285,365);
const romanGroups=chunks(genealogy.romanComparisons,3);
for(const [i,comparisons] of romanGroups.entries()) {
  const c=`roman-comparisons-${i+1}`;canvas(c,`R${i+1} · Roman correspondences`,'clay');heading(c,'Shared attributes, distinct contexts','Read across each pair; the contrast matters as much as the shared iconography.');
  launch('roman-index',`roman-page-${i}`,`Comparisons ${i*3+1}–${i*3+comparisons.length}`,c,comparisons.map(r=>`${r.greek} / ${r.roman}`).join(' · '),50+(i%3)*445,700+Math.floor(i/3)*270,405,230,{style:{titleSize:29,fontSize:20}});
  comparisons.forEach((r,j)=>{
    const y=270+j*355;
    text(c,`${c}-${r.key}-greek`,r.greek,r.summary,50,y,410,300,{style:{fill:'#E2E7DA',titleSize:34,fontSize:19}});
    text(c,`${c}-${r.key}-roman`,r.roman,(r.differences||[]).join('\n\n'),680,y,660,300,{style:{fill:'#F0D8B5',titleSize:34,fontSize:19}});
    link(c,`${c}-${r.key}-identification`,`${c}-${r.key}-greek`,`${c}-${r.key}-roman`,r.title||'compare',{...across,relation:'mutual'});
  });
  notes(c,`${c}-sources`,'Roman comparison source notes',comparisons.map(r=>{
    const earlier=oldHesiod.roman.comparisons.find(p=>p.greek===r.greek.split(' / ')[0]);
    return `${r.greek} / ${r.roman}\n${r.summary}\n${r.differences.join('\n')}\n${sourcesText(r.sourceRefs)}${earlier?`\n\nFURTHER CONTEXT FROM THE ORIGINAL COLLECTION\n${earlier.shared}\n\n${earlier.differences}\n\n${earlier.story}\n${sourcesText(earlier.sourceIds)}`:''}`;
  }).join('\n\n'),[],1030,270+comparisons.length*355);
  back(c,'roman-index',50,270+comparisons.length*355);
}
text('roman-index','roman-method','An equivalent is a starting question','Zeus/Jupiter and Athena/Minerva are familiar identifications. Mars’s agricultural protection, Roman civic triads, household powers, and the absence of a simple Greek counterpart for Janus complicate a name-for-name chart.',50,285,960,310,{style:{fontSize:22,titleSize:31}});
back('roman-index','welcome',50,740+Math.ceil(romanGroups.length/3)*270);

canvas('hesiod-index','60 · Hesiod · cosmos, work and heroic tradition','olive');
heading('hesiod-index','Order in heaven. Work on earth.','Theogony and Works and Days take different routes through justice, labor and divine power. Attribution of the Shield and surviving fragments stays explicit.');
for(const [i,work] of genealogy.hesiodWorks.entries()) {
  const c=`hesiod-${work.key}`;canvas(c,`H${i+1} · ${work.title}`,'olive');heading(c,work.title,work.attribution);
  launch('hesiod-index',`hesiod-entry-${work.key}`,work.title,c,short(work.summary,245),50+(i%2)*680,300+Math.floor(i/2)*330,630,310,{style:{titleSize:32,fontSize:21}});
  text(c,`${c}-summary`,'The work’s argument',work.summary,50,270,1290,270,{style:{titleSize:33,fontSize:22}});
  const sectionPages=chunks(work.sections,4);
  for(const [pageIndex,sections] of sectionPages.entries()) {
    const detail=`${c}-part-${pageIndex+1}`;canvas(detail,`H${i+1}.${pageIndex+1} · ${work.title}`,'olive');heading(detail,work.title,`Reading sequence ${pageIndex*4+1}–${pageIndex*4+sections.length} · ${work.attribution}`);
    launch(c,`${detail}-portal`,`Reading sequence ${pageIndex*4+1}–${pageIndex*4+sections.length}`,detail,sections.map(s=>s.title).join(' · '),50+(pageIndex%2)*680,600+Math.floor(pageIndex/2)*280,630,230,{style:{titleSize:28,fontSize:20}});
    for(const [j,section] of sections.entries()) {
      const x=50+(j%2)*680,y=280+Math.floor(j/2)*430;
      text(detail,`${c}-section-${pageIndex*4+j}`,section.title,`${section.range||''}\n\n${section.summary}`,x,y,630,360,{style:{titleSize:28,fontSize:21}});
      if(j)link(detail,`${detail}-order-${j}`,`${c}-section-${pageIndex*4+j-1}`,`${c}-section-${pageIndex*4+j}`,'',j===2?{...down,points:[{x:1045,y:675},{x:365,y:675}]}:across);
    }
    back(detail,c,50,1140);
    if(pageIndex)sectionPages.length>1&&launch(detail,`${detail}-previous`,'← Earlier sections',`${c}-part-${pageIndex}`,'Previous reading sequence.',400,1140,380,140);
    if(pageIndex<sectionPages.length-1)launch(detail,`${detail}-next`,'Continue reading →',`${c}-part-${pageIndex+2}`,'Next reading sequence.',830,1140,440,140);
  }
  const y=640+Math.ceil(sectionPages.length/2)*280;
  notes(c,`${c}-sources`,'Complete argument and passages',`${work.summary}\n\n${work.sections.map(s=>`${s.range} · ${s.title}\n${s.summary}\n${sourcesText(s.sourceRefs)}`).join('\n\n')}\n\nARGUMENT MAP\n${(work.timeline||[]).map(s=>`${s.order}. ${s.title}\n${s.summary}\n${sourcesText(s.sourceRefs)}`).join('\n\n')}`,work.sourceRefs,1040,y);
  back(c,'hesiod-index',50,y);launch(c,`${c}-read-all`,'Read the complete translation','reading-room','Includes the Hesiodic poems, hymns and surviving epic fragments.',400,y,480,170);
}
back('hesiod-index','welcome',50,340+Math.ceil(genealogy.hesiodWorks.length/2)*330);

// Media gallery and a real Boolean reading aid preserve the broad product-testing purpose.
canvas('media-lab','80 · Media & interaction laboratory','sea');
heading('media-lab','Read. Reveal. Test.','A compact exercise in attachments, inherited rules, references, signals and independent sibling output.');
const fixtureRecords=await json(path.join(corpus,'fixtures.json'));
for(const [i,f] of fixtureRecords.entries()) {
  const bytes=await readFile(path.join(corpus,'media',f.file));const a=asset(`fixture-${i}`,f.file,f.mime,bytes);
  node('media-lab',`media-fixture-${i}`,f.mime.startsWith('image/')&&f.mime!=='image/svg+xml'?'image':'document',f.title,f.description,50+(i%3)*445,270+Math.floor(i/3)*380,405,330,{asset:a,style:{fontSize:15,titleSize:23,imageFit:'contain'}});
}
const pdf=await readFile(path.join(corpus,'media','mythology-companion.pdf'));asset('original-test-guide','first-edition-testing-guide.pdf','application/pdf',pdf);
document('media-lab','media-pdf','PDF preview fixture','The original first-edition testing guide. Its historical counts do not describe this expanded atlas.','original-test-guide',50,1090,405,260);
const gateParent=node('media-lab','lab-logic','container','A reading comparison','Expand the internals. Collapsing this group must not stop its sibling output.',525,1090,800,510,{rules:{nodeKinds:['container','text','input','switch','joint','output'],linkKinds:['visual','functional'],operations:['AND']},style:{titleSize:26,fontSize:18}});
node('media-lab','lab-input-one','input','Read Homer','',550,1280,210,130,{parent:gateParent,value:true,exposed:true});
node('media-lab','lab-input-two','input','Read Hesiod','',550,1430,210,130,{parent:gateParent,value:true,exposed:true});
node('media-lab','lab-and','joint','Compare','',815,1325,190,130,{parent:gateParent,operation:'AND'});
node('media-lab','lab-switch','switch','Connection','',1070,1325,220,135,{parent:gateParent,closed:true,exposed:true});
node('media-lab','lab-output','output','Both sources available','',1370,1240,290,180,{style:{titleSize:24,fontSize:18}});
for(const [i,from,to,port] of [[1,'lab-input-one','lab-and',0],[2,'lab-input-two','lab-and',1],[3,'lab-and','lab-switch',0],[4,'lab-switch','lab-output',0]]) {
  recipe.links.push({key:`lab-wire-${i}`,...old('links',`lab-wire-${i}`),canvas:'media-lab',source:from,target:to,kind:'functional',relation:'causal',targetPort:port});
}
back('media-lab','welcome',50,1640);
// Make every major route visible from the established opening composition.
launch('welcome','route-family','01 / Kinship','family','Primordials, Titans, gods and demigods. Follow the family branches.',50,530,300,190,{style:{fill:'#334B50',titleSize:24,fontSize:18,borderWidth:0}});
launch('welcome','route-odyssey','02 / Odyssey','odyssey-index','All 24 books, narrative frames and the complete text.',380,530,300,190,{style:{fill:'#304365',titleSize:24,fontSize:18,borderWidth:0}});
launch('welcome','route-rome','03 / Rome','roman-index','Greek correspondences and distinct Roman religious worlds.',710,530,300,190,{style:{fill:'#59452F',titleSize:24,fontSize:18,borderWidth:0}});
launch('welcome','route-persephone','Story / Persephone','persephone','An illustrated reading of the Homeric Hymn to Demeter.',1040,530,300,190,{style:{fill:'#503448',titleSize:24,fontSize:18,borderWidth:0}});
canvas('atlas-directory','00 · Complete atlas directory','sea');
heading('atlas-directory','A reference you can move through','Choose a subject, then move from overview to branch, book, source or comparison.');
for(const [i,[target,title,body]] of [
  ['genealogy-index','01 / Families','Primordials, all twelve Titans, Olympians, heroes and alternative parentages.'],
  ['zeus-index','02 / Zeus’s children',`${zeusChildren.length} offspring records: individuals and collectives, with mothers and variants.`],
  ['god-index','03 / Gods & stories','Divine dossiers with expandable narrative detail and connected family appearances.'],
  ['iliad-index','04 / Iliad','All 24 books, from the quarrel to Hector’s funeral; complete translation included.'],
  ['odyssey-index','05 / Odyssey','All 24 books, with journey order, storytelling order and the complete translation.'],
  ['hesiod-index','06 / Hesiod','Cosmogony, justice, labor, heroes and surviving fragments.'],
  ['roman-index','07 / Roman worlds','Greek correspondences, Roman cults, civic roles and important differences.'],
  ['reading-room','08 / Reading room','Complete source texts, ancient passages and museum provenance.'],
  ['media-lab','09 / Media & logic','Documents, images, animation, attachment fallbacks and switch-controlled logic.'],
].entries())launch('atlas-directory',`directory-${target}`,title,target,body,50+(i%3)*445,280+Math.floor(i/3)*270,405,225,{style:{titleSize:29,fontSize:21}});
launch('welcome','welcome-complete-directory','Explore the complete reference atlas','atlas-directory','Families · all 48 epic books · Hesiod · Roman gods · sources · interactive media',50,965,1290,165,{style:{titleSize:34,fontSize:22}});
back('atlas-directory','welcome',50,1120);

canvas('atlas-guide','00 · How to explore the atlas','olive');
heading('atlas-guide','Follow connections. Choose the depth.','Overview → branch or movement → figure or book → full source. Each level has its own reading purpose.');
const guideSteps=[
  ['Overview','Choose a subject from the illustrated entry canvas. The original five compositions remain as visual entrances, now linked to the larger reference.', 'welcome'],
  ['Branch or movement','Family branches distinguish selected parents from alternative accounts. Epic movements group four books without changing Homer’s sequence.', 'genealogy-index'],
  ['Figure or book','Read a short account, then follow its labeled relationship or open an interpretive lens. Expand a story container to expose its nested detail.', 'iliad-01'],
  ['Complete source','Use the attached translations, passage notes and museum records to check the map. Search includes every included TXT and Markdown attachment.', 'reading-room'],
];
guideSteps.forEach(([title,body,target],i)=>{
  launch('atlas-guide',`guide-step-${i}`,`${i+1} / ${title}`,target,body,50+(i%2)*680,290+Math.floor(i/2)*350,625,285,{style:{titleSize:30,fontSize:22}});
  if(i)link('atlas-guide',`guide-sequence-${i}`,`guide-step-${i-1}`,`guide-step-${i}`,'',i===2?{...down,points:[{x:1042,y:605},{x:362,y:605}]}:across);
});
text('atlas-guide','guide-controls','Use Explore, Fit and the wheel','Explore searches across canvases and reveals hidden matches. Select a reference, then use Open original context in Explore. Back and Forward retrace your route. Fit restores an overview; zoom in to read. Viewer changes are temporary. Run starts the media laboratory; Stop affects every canvas in this viewer.',50,1050,1290,260,{style:{titleSize:30,fontSize:22}});
back('atlas-guide','welcome',50,1370);
launch('atlas-directory','directory-reading-guide','How to explore this atlas','atlas-guide','Branches, search, collapse and sources.',400,1120,440,145,{style:{titleSize:24,fontSize:19}});
launch('atlas-directory','directory-compare-epics','Read across both epics','epic-comparisons','Thematic connections with direct links.',890,1120,440,145,{style:{titleSize:24,fontSize:19}});

// Retire only entities owned by an earlier generated expansion. Baseline work and
// entities authored outside this generator remain untouched during local rebuilds.
let previousRecipe;
try {previousRecipe=await json(path.join(out,'visual-atlas.expansion.recipe.json'));}
catch(error){if(error.code!=='ENOENT')throw error;}
if(previousRecipe){
  const baselineAliases=await json(path.join(out,'baseline-v1','visual-atlas.aliases.json'));
  const retired={};
  for(const group of ['canvases','nodes','links','assets']){
    const current=new Set(recipe[group].map(item=>item.key));
    const retainedIds=new Set(recipe[group].map(item=>item.existingId).filter(Boolean));
    retired[group]=new Set((previousRecipe[group]||[]).filter(item=>!current.has(item.key)&&!baselineAliases[group]?.[item.key]).map(item=>oldAliases[group]?.[item.key]).filter(id=>id&&!retainedIds.has(id)));
    for(const [key,id]of Object.entries(oldAliases[group]||{}))if(retired[group].has(id))delete oldAliases[group][key];
  }
  for(const c of base.canvases)if(retired.canvases.has(c.id)){retired.nodes.add(c.rootId);for(const [key,id]of Object.entries(oldAliases.roots))if(id===c.rootId)delete oldAliases.roots[key];}
  for(const id of retiredOriginals)retired.nodes.add(id);
  base.canvases=base.canvases.filter(c=>!retired.canvases.has(c.id));
  base.nodes=base.nodes.filter(n=>!retired.nodes.has(n.id));
  const updatedLinks=new Set(recipe.links.map(l=>l.existingId).filter(Boolean));
  base.links=base.links.filter(l=>updatedLinks.has(l.id)||!retired.links.has(l.id)&&!retired.nodes.has(l.sourceId)&&!retired.nodes.has(l.targetId));
  for(const id of retired.nodes)delete base.snapshots[id];
  const used=new Set(base.nodes.map(n=>n.assetId).filter(Boolean));
  base.assets=base.assets.filter(a=>!retired.assets.has(a.id)||used.has(a.id));
}
const result=applyRecipe(base,recipe);
const project=commitSnapshots(result.project);
assert.deepEqual(validateProject(project),[]);
const aliases=Object.fromEntries(Object.keys(oldAliases).map(group=>[group,{...oldAliases[group],...result.aliases[group]}]));
await writeFile(path.join(out,'visual-atlas.project.json'),JSON.stringify(project));
await writeFile(path.join(out,'visual-atlas.aliases.json'),JSON.stringify(aliases,null,2));
await writeFile(path.join(out,'visual-atlas.expansion.recipe.json'),JSON.stringify(recipe));
const inspection=inspectProject(project);
await writeFile(path.join(out,'visual-atlas.inspection.json'),JSON.stringify(inspection,null,2));
const manifest={title:project.title,projectId:project.id,canvases:project.canvases.length,nodes:project.nodes.length,links:project.links.length,assets:project.assets.length,
  imagePlacements:project.nodes.filter(n=>n.kind==='image').length,containers:project.nodes.filter(n=>n.kind==='container').length,
  epicBooks:48,persons:genealogy.persons.length,namedIndividuals:genealogy.stats.namedIndividuals,collectiveRecords:genealogy.stats.collectiveRecords,romanComparisons:genealogy.romanComparisons.length,zeusChildren:zeusChildren.length,zeusIndividualRecords:zeusChildren.length-zeusCollectives,zeusCollectiveRecords:zeusCollectives,
  originalCollectionHash:originalHash,sourceTexts:['https://www.gutenberg.org/ebooks/2199','https://www.gutenberg.org/ebooks/1727','https://www.gutenberg.org/ebooks/348'],scopeNotes:genealogy.scopeNotes};
await writeFile(path.join(out,'visual-atlas.manifest.json'),JSON.stringify(manifest,null,2));
if(process.argv.includes('--publish')) {
  const [js,css]=await Promise.all(['viewer.js','scene.css'].map(n=>readFile(path.join(root,'dist',n),'utf8')));
  const html=buildPublication(project,js,css,{includeExternalReferences:true});
  assert.equal(readPublication(html).nodes.length,project.nodes.length);
  await writeFile(path.join(out,'visual-atlas.html'),html);
}
assert.equal(hash(await readFile(originalPath)),originalHash,'Original collection preserved');
console.log(JSON.stringify(manifest,null,2));
