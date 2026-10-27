import fs from 'node:fs';
import path from 'node:path';
import { applyRecipe, inspectProject } from '../../src/core/authoring.ts';
import { navigationIndex } from '../../src/core/navigation.ts';
import { evaluate } from '../../src/core/engine.ts';
const ROOT='I:/dev/Meridian';
const OUT=ROOT+'/output/mythology-refinement-20261023/gods';
const base=JSON.parse(fs.readFileSync(ROOT+'/output/mythology-refinement-20261023/baseline/3c8176d0-884a-4ac9-9a99-a0d5548de629.json','utf8'));
fs.mkdirSync(OUT,{recursive:true});
const recipe={version:1,canvases:[],nodes:[],assets:[],links:[],routes:[],bindings:{nodes:{},canvases:{},links:{}}};
const updates=new Map(), added=new Map(), workPlans=[];
const allNodes=new Map(base.nodes.map(n=>[n.id,n]));
const nodeIndices=new Map(base.nodes.map((n,i)=>[n.id,i]));
const ckey=c=>'w'+base.canvases.indexOf(c), nkey=n=>'n'+nodeIndices.get(n.id), lkey=l=>'l'+base.links.indexOf(l);
for(const c of base.canvases)recipe.bindings.canvases[ckey(c)]=c.id;
for(const n of base.nodes)if(n.kind!=='canvas')recipe.bindings.nodes[nkey(n)]=n.id;
for(const l of base.links)recipe.bindings.links[lkey(l)]=l.id;
const titles=['Greek Mythology','Origins','Olympian Family','Variant Traditions','First Powers','Earth and Sky','Titans','Divine Succession','Atlas and Prometheus','Hecate','Zeus','Hera','Poseidon','Athena','Apollo','Artemis','Hermes','Hephaestus','Aphrodite','Ares','Dionysus','Demeter','Persephone','Hades','Hestia','Iliad','Iliad · Books 1–4','Iliad · Books 5–8','Iliad · Books 9–12','Iliad · Books 13–16','Iliad · Books 17–20','Iliad · Books 21–24','Odyssey','Odyssey · Books 1–4','Odyssey · Books 5–8','Odyssey · Books 9–12','Odyssey · Books 13–16','Odyssey · Books 17–20','Odyssey · Books 21–24','Theogony','Works and Days','Roman Counterparts','Roman Worship','Museum Objects','Media Studies','Logic Lab','Reading Register','Gods'];
function palette(c){const bg=c.style?.background??'#F3EDDF',dark=bg.startsWith('#1')||bg.startsWith('#2');return{bg,fill:dark?(bg==='#112932'?'#183943':'#39303D'):'#FCF8EF',ink:dark?'#F2E8D9':'#253D42',border:dark?(bg==='#112932'?'#47776D':'#756175'):'#CCBFA8',accent:c.style?.portalColor??'#A57543'};}
function style(c,extra={}){const p=palette(c);return{variant:'card',shape:'rectangle',radius:0,fill:p.fill,border:p.border,textColor:p.ink,accent:p.accent,fontFamily:'garamond',titleSize:26,fontSize:20,textAlign:'center',contentSizing:'fixed',padding:14,contentGap:8,childPadding:18,borderWidth:1.2,...extra};}
function label(c,extra={}){return style(c,{variant:'label',shape:'none',borderWidth:0,padding:0,backgroundDisplay:'always',...extra});}
function put(n,fields){let u=updates.get(n.id)??{key:nkey(n)};Object.assign(u,fields);updates.set(n.id,u);return u;}
function make(c,key,fields){let n={key:ckey(c)+'/'+key,canvas:ckey(c),kind:'text',title:'',x:0,y:0,width:280,height:100,childPosition:'manual',sizeMode:'fixed',style:style(c),...fields};added.set(n.key,n);return n;}
function ref(n){return n.id?nkey(n):n.key;}
function effective(n){return {...n,...(n.id?updates.get(n.id):added.get(n.key))};}
function place(n,x,y,w,h,extra={}){let frame={};if(n.kind==='container'){let closed=extra.collapsed??n.collapsed??false;frame={containerVariant:'contained',childArrangement:'free',childLayout:'contained',containerSizes:{collapsed:{width:w,height:h,sizeMode:'fixed'},expanded:{width:w,height:h,sizeMode:'fixed'}}};}return put(n,{x,y,width:w,height:h,childPosition:'manual',sizeMode:'fixed',...frame,...extra});}
function clearRuns(n){put(n,{unset:[...new Set([...(updates.get(n.id)?.unset??[]),'titleRuns','textRuns'])]});}
function clean(text){return(text??'').replace(/\n\nSources:[\s\S]*$/,'').replace(/\n\nSelected descent:[\s\S]*$/,'').trim();}
function brief(text,max=230){let s=clean(text);if(s.length<=max)return s;let a=s.match(/[^.!?]+[.!?](?:\s|$)/g)??[];let t='';for(let x of a){if((t+x).length>max)break;t+=x;}return t.trim()||s.slice(0,max).replace(/\s+\S*$/,'')+'…';}
function shorten(t){return t.replace(/^\d+\s*·\s*/,'').replace(/\s*·\s*(?:saved Greek identity|saved ancestor|family reference|variants|saved|live mirror).*$/,'').replace(/^Open (?:the )?/,'').replace(/^Read (?:the )?/,'').replace(/\s*·\s*(?:TXT|PDF|JSON|PNG|JPEG|WebP)$/,'').trim();}
function flat(n,c){put(n,{parent:ckey(c),placement:'free',childPosition:'manual',unset:[...new Set([...(updates.get(n.id)?.unset??[]),'cell','grid','childOffset','expansionTray','titleRuns','textRuns'])]});}
function collapse(n,c,x,y,w,h,title){put(n,{parent:ckey(c),title,text:'',containerVariant:'contained',childLayout:'contained',childArrangement:'free',collapsed:true,width:w,height:h,x,y,containerSizes:{collapsed:{width:w,height:h,sizeMode:'fixed'},expanded:{width:w,height:520,sizeMode:'fixed'}},style:style(c,{titleSize:27}),sizeMode:'fixed',childPosition:'manual',unset:['grid','cell','childOffset','expansionTray','titleRuns','textRuns']});}
function edge(c,key,a,b,labelText='',relation='generic',extra={}){recipe.links.push({key:ckey(c)+'/'+key,canvas:ckey(c),parent:ckey(c),source:ref(a),target:ref(b),kind:'visual',relation,direction:relation==='chronological'?'forward':'none',label:labelText,style:{color:palette(c).accent,width:1.6,lineStyle:'solid'},...extra});}
function rootNodes(c){return base.nodes.filter(n=>n.canvasId===c.id&&n.kind!=='canvas');}
const support=new Map();
function archive(c,x=40,y=1000,existing){const ns=rootNodes(c);let original='# '+titles[base.canvases.indexOf(c)]+'\n\n';for(let n of ns){original+='## '+(n.title||n.tag||n.kind)+'\n\n'+(n.text??'')+'\n\n';original+='Original identity: '+n.id+'\n\n';if(n.assetId)original+='Attachment: '+n.assetId+'\n\n';if(n.hyperlinks?.length)original+=n.hyperlinks.map(h=>'['+(h.label??h.url)+']('+h.url+')').join('\n')+'\n\n';}
  const assetKey=ckey(c)+'/reading-asset';recipe.assets.push({key:assetKey,name:titles[base.canvases.indexOf(c)].replace(/[^\p{L}\p{N} -]/gu,'')+' Reading.md',mime:'text/markdown',base64:Buffer.from(original).toString('base64')});
  let box=existing??make(c,'reading',{kind:'container'});if(existing)collapse(existing,c,x,y,330,110,'Reading');else Object.assign(box,{kind:'container',title:'Reading',x,y,width:330,height:110,containerVariant:'contained',childLayout:'contained',childArrangement:'free',collapsed:true,containerSizes:{collapsed:{width:330,height:110,sizeMode:'fixed'},expanded:{width:820,height:650,sizeMode:'fixed'}},style:style(c)});
  if(existing)put(existing,{containerSizes:{collapsed:{width:330,height:110,sizeMode:'fixed'},expanded:{width:820,height:650,sizeMode:'fixed'}}});
  make(c,'reading-document',{kind:'document',parent:ref(box),title:'Collected Reading',asset:assetKey,contentType:'asset',x:x+20,y:y+25,width:760,height:570,collapsed:true,style:style(c,{titleSize:26,fontSize:19})});
  support.set(c.id,{box,x,y,count:0});return box;}
function stash(n,c){let s=support.get(c.id);let idx=s.count++;let frame=n.kind==='container'?{containerVariant:'contained',childLayout:'contained',childArrangement:'free',containerSizes:{collapsed:{width:350,height:165,sizeMode:'fixed'},expanded:{width:350,height:165,sizeMode:'fixed'}}}:{};put(n,{parent:ref(s.box),title:shorten(n.title),x:s.x+30+(idx%2)*375,y:s.y+680+Math.floor(idx/2)*190,width:350,height:165,collapsed:true,style:style(c,{fontSize:17,titleSize:21}),childPosition:'manual',sizeMode:'fixed',...frame,unset:[...new Set([...(updates.get(n.id)?.unset??[]),'grid','cell','titleRuns','textRuns','expansionTray'])]});}
function header(c,n,title=titles[base.canvases.indexOf(c)],x=60,y=30,w=1200){flat(n,c);place(n,x,y,w,90,{title,text:'',style:label(c,{titleSize:44,textAlign:'left'})});}
function ownChildren(n){return base.nodes.filter(x=>x.parentId===n.id);}
function portal(n,c,x,y,title){flat(n,c);place(n,x,y,240,120,{title:title??shorten(n.title),text:'',style:style(c,{titleSize:23,portalColor:palette(c).accent,backgroundDisplay:'hover'})});}
function misc(c){for(let n of rootNodes(c)){let u=updates.get(n.id);if(!u||Object.keys(u).every(k=>k==='key'||k==='unset')){stash(n,c);}}}
for(let [i,c] of base.canvases.entries()){recipe.canvases.push({key:ckey(c),title:titles[i],style:{...c.style,gridVisible:false}});for(let n of rootNodes(c)){clearRuns(n);}}

// Profiles: a figure, two particular stories, and a museum object form the
// primary constellation. Full readings and provenance remain one disclosure away.
const storyTitles=[['First Beings','Two Erotes'],['The Sickle','Aphrodite’s Birth'],['Titan Families','Sun and Apollo'],['The Hidden Child','Titanomachy'],['Mecone','Stolen Fire'],['Divine Honors','Demeter’s Torch'],['Metis','Divine Shares'],['Leto’s Labor','Hephaestus’s Birth'],['The Sea','Athens'],['Athena’s Birth','The Olive'],['Delos','Delphi'],['The Chorus','Actaeon'],['The Lyre','Reconciliation'],['The Workshop','Achilles’s Shield'],['Anchises','Dione'],['Diomedes','Divine Restraint'],['Semele','The Pirates'],['Eleusis','The Famine'],['The Meadow','The Return'],['The Marriage','Orpheus'],['Virginity','Libation']];
for(let ix=4;ix<=24;ix++){
 const c=base.canvases[ix], ns=rootNodes(c), subject=ns.find(n=>n.kind==='text'&&n.parentId===c.rootId), outer=ns.find(n=>n.kind==='container'&&n.parentId===c.rootId), stories=ns.filter(n=>n.kind==='container'&&n.parentId===outer.id);
 archive(c,620,1110,outer);flat(subject,c);place(subject,620,330,340,150,{title:titles[ix],text:'',style:style(c,{titleSize:40,shape:'ellipse',fill:palette(c).fill})});
 stories.forEach((s,j)=>{const x=j?1130:30,y=190;collapse(s,c,x,y,420,150,storyTitles[ix-4][j]);let body=ownChildren(s).find(n=>n.kind==='text');if(body){place(body,x+22,y+28,376,440,{title:'',text:clean(body.text),style:label(c,{fontSize:20,textAlign:'left'})});}edge(c,'story-'+j,subject,s,'', 'generic',{sourceAnchor:{x:j?1:0,y:.5},targetAnchor:{x:j?0:1,y:.5}});});
 let image=ns.find(n=>['image','media'].includes(n.kind)&&n.assetId);if(image?.assetId==='c3be4302-be7b-4796-867b-d70e968c057c'){stash(image,c);ownChildren(image).forEach(n=>stash(n,c));image=null;}if(image){flat(image,c);const ratio=image.height/image.width;place(image,625,555,330,Math.min(420,330*ratio),{title:/probably/i.test(image.title)?image.title:'',collapsed:true,style:label(c,{imageFit:'contain',titleSize:18,padding:0})});
 let notes=ns.filter(n=>n.kind==='text'&&n.id!==subject.id&&!ownChildren(stories[0]).includes(n)&&!ownChildren(stories[1]).includes(n)&&!/^ATLAS|TWO TELLINGS|Read across/.test(n.title));notes.forEach((n,j)=>{put(n,{parent:ref(image),x:1660+j*540,y:570,width:500,height:420,title:'Object Record',text:n.text,collapsed:true,style:style(c,{textAlign:'left',fontSize:19})});});}
 ns.filter(n=>n.kind==='canvas-link').forEach((n,j)=>portal(n,c,520+j*285,image?1040:780,n.title==='Collection directory'?'Greek Mythology':'Family'));
 misc(c);workPlans.push({workId:c.id,title:titles[ix],family:'figure-constellation',focus:subject.id,expanded:stories.map(n=>n.id)});
}

// Epic overviews follow the poem's telling order, not a fabricated travel map.
for(let ix of [25,32]){
 const c=base.canvases[ix],ns=rootNodes(c),subject=ns.find(n=>n.kind==='text'&&n.parentId===c.rootId),outer=ns.find(n=>n.kind==='container');archive(c,50,800,outer);header(c,subject);
 const chapters=ns.filter(n=>n.kind==='canvas-link'&&/^Books /.test(n.title)),frames=ns.filter(n=>n.title==='Narrative frame');
 const names=ix===25?['The Quarrel','Troy’s Defenders','The Embassy','Patroclus','Achilles Returns','Hector’s Funeral']:['Telemachus','The Phaeacians','The Wanderings','Ithaca','The Stranger','Recognition'];
 chapters.forEach((n,j)=>{flat(n,c);place(n,50+j*410,225,330,165,{title:names[j],text:'',style:style(c,{titleSize:29})});let f=frames[j];flat(f,c);place(f,50+j*410,460,330,150,{title:'',text:brief(f.text,235),style:label(c,{fontSize:21,textAlign:'left'})});});
 // Retain all original chronology IDs and pivots, positioned at the same
 // inter-stage channels. The old serpentine reading order is straightened.
 const joints=ns.filter(n=>n.kind==='junction');joints.forEach((n,j)=>{flat(n,c);place(n,420+j*410,299,10,10,{title:'',utilityVariant:'pivot',style:{fill:palette(c).accent,border:palette(c).accent}});});
 let currentLinks=base.links.filter(l=>l.canvasId===c.id);for(let l of currentLinks){let a=allNodes.get(l.sourceId),b=allNodes.get(l.targetId);if(chapters.includes(a)||joints.includes(a)){recipe.links.push({key:lkey(l),parent:ckey(c),sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5},label:'',direction:joints.includes(b)?'none':'forward',relation:'chronological',unset:['points'],style:{color:palette(c).accent,width:1.7}});}else recipe.links.push({key:lkey(l),label:shorten(l.label??'')});}
 const people=ns.filter(n=>n.parentId===outer.id&&n.kind==='text'&&!/naming|interpretation/.test(n.title));people.forEach((n,j)=>{flat(n,c);place(n,50+j%7*355,1090+Math.floor(j/7)*270,290,155,{title:n.title.split(' · ')[0],text:brief(n.text,150),style:style(c,{titleSize:24,fontSize:19,textAlign:'left'}),collapsed:true});});
 // A second band makes characters contextual branches of the narrative.
 people.slice(0,6).forEach((n,j)=>edge(c,'character-'+j,chapters[j],n,'', 'generic',{sourceAnchor:{x:.5,y:1},targetAnchor:{x:.5,y:0}}));
 ns.filter(n=>n.kind==='canvas-link'&&!chapters.includes(n)).forEach(n=>portal(n,c,2190,800,'Greek Mythology'));
 misc(c);workPlans.push({workId:c.id,title:titles[ix],family:'epic-sequence',focus:chapters[2].id});
}

// Four-book studies: actual book order is a horizontal sequence. Each book
// opens its full narrative in a separately reserved rectangle.
for(let ix of [...Array.from({length:6},(_,j)=>26+j),...Array.from({length:6},(_,j)=>33+j)]){
 const c=base.canvases[ix],ns=rootNodes(c),subject=ns.find(n=>n.kind==='text'&&n.parentId===c.rootId),outer=ns.find(n=>n.kind==='container'&&/^Read the four/.test(n.title));archive(c,45,900,outer);header(c,subject);
 const books=ns.filter(n=>n.kind==='container'&&/^Book \d/.test(n.title));books.forEach((b,j)=>{let body=ownChildren(b).find(n=>n.kind==='text'),x=50+j*620;let title=body?.title.replace(/^Book (\d+)\s*·\s*/,'')??b.title;let bits=title.split(/\s+(?:and|that|with)\s+/);let name=bits[0];if(name.length>28)name=title.split(' ').slice(0,4).join(' ');collapse(b,c,x,240,520,145,'Book '+b.title.match(/\d+/)[0]);put(b,{text:name,style:style(c,{titleSize:32,fontSize:23}),containerSizes:{collapsed:{width:520,height:145,sizeMode:'fixed'},expanded:{width:520,height:570,sizeMode:'fixed'}}});if(body)place(body,x+24,275,472,490,{title:'',text:clean(body.text),style:label(c,{fontSize:20,textAlign:'left'})});});
 const joints=ns.filter(n=>n.kind==='junction');joints.forEach((n,j)=>{flat(n,c);place(n,603+j*620,235,10,10,{title:'',utilityVariant:'pivot',style:{fill:palette(c).accent,border:palette(c).accent}});});
 for(let l of base.links.filter(l=>l.canvasId===c.id)){recipe.links.push({key:lkey(l),parent:ckey(c),label:'',direction:books.some(b=>b.id===l.targetId)?'forward':'none',sourceAnchor:{x:1,y:0},targetAnchor:{x:0,y:0},unset:['points'],style:{color:palette(c).accent,width:1.7}});}
 ns.filter(n=>n.kind==='canvas-link').forEach((n,j)=>portal(n,c,1800+j*285,900,n.title.includes('overview')?(/Iliad/.test(n.title)?'Iliad':'Odyssey'):n.targetId?shorten(base.canvases.find(w=>w.rootId===n.targetId)?.title??n.title):shorten(n.title)));
 const note=ns.find(n=>n.kind==='container'&&/^Reading questions/.test(n.title));if(note){collapse(note,c,1000,900,360,110,'Interpretation');ownChildren(note).forEach((n,j)=>place(n,1025,940+j*360,310,310,{title:n.title==='Read the ancient text'?'Passages':shorten(n.title),text:clean(n.text),style:style(c,{textAlign:'left',fontSize:18,titleSize:23})}));}
 misc(c);workPlans.push({workId:c.id,title:titles[ix],family:'four-book-sequence',focus:books[0].id,expanded:books.map(n=>n.id)});
}

// Genealogical layers preserve the selected parent relationships exactly.
for(let ix of [1,2]){
 const c=base.canvases[ix],ns=rootNodes(c),subject=ns.find(n=>n.kind==='text'&&n.parentId===c.rootId),figs=ns.filter(n=>n.kind==='text'&&n.text?.includes('Selected descent:'));
 archive(c,50,70);header(c,subject,titles[ix],460,40,1100);
 const levelMap=new Map(),figIds=new Set(figs.map(n=>n.id)),geneLinks=base.links.filter(l=>l.canvasId===c.id&&l.relation==='causal');
 function figureParents(id,seen=new Set()){if(seen.has(id))return[];seen.add(id);let result=[];for(let l of geneLinks.filter(l=>l.targetId===id)){let p=allNodes.get(l.sourceId);if(figIds.has(p.id))result.push(p.id);else if(p.kind==='junction')result.push(...figureParents(p.id,seen));}return result;}
 function generation(id,stack=new Set()){if(levelMap.has(id))return levelMap.get(id);if(stack.has(id))return 0;stack.add(id);let ps=figureParents(id),g=ps.length?1+Math.max(...ps.map(p=>generation(p,new Set(stack)))):0;levelMap.set(id,g);return g;}
 for(let n of figs)generation(n.id);const oldLevels=[...new Set(levelMap.values())].sort((a,b)=>a-b);
 for(let lev of oldLevels){let row=figs.filter(n=>levelMap.get(n.id)===lev).sort((a,b)=>a.x-b.x);row.forEach((n,j)=>{flat(n,c);place(n,450+j*320,240+lev*560,260,95,{title:shorten(n.title),text:'',collapsed:true,style:style(c,{titleSize:26})});});}
 // Existing reference ancestors occupy their own upper-right lane.
 ns.filter(n=>n.kind==='reference').forEach((n,j)=>{flat(n,c);place(n,450+(figs.filter(f=>levelMap.get(f.id)===0).length+j)*320,240,260,95,{title:shorten(n.title),style:style(c,{titleSize:24}),text:'',collapsed:true});});
 let portalCounter=0;for(let n of ns.filter(n=>n.kind==='canvas-link')){
  let parent=allNodes.get(n.parentId);if(figs.includes(parent)){if(ix===1){stash(n,c);put(n,{title:shorten(parent.title),tag:shorten(parent.title)+' · '+(titles[base.canvases.findIndex(w=>w.rootId===n.targetId)]??'Traditions')});}else{let e=effective(parent),idx=ownChildren(parent).filter(n=>n.kind==='canvas-link').indexOf(n);place(n,e.x,e.y+155+idx*100,260,80,{title:titles[base.canvases.findIndex(w=>w.rootId===n.targetId)]??'Traditions',style:style(c,{titleSize:22}),collapsed:true});}}
  else if(n.title==='Collection directory')portal(n,c,50,250,'Greek Mythology');else if(portalCounter++===0)portal(n,c,50,430,'Variants');else stash(n,c);
 }
 // Routing joints preserve their saved identities and semantic incidence.
 let joints=ns.filter(n=>n.kind==='junction');for(let [j,n] of joints.entries()){let incoming=base.links.find(l=>l.canvasId===c.id&&l.targetId===n.id),a=allNodes.get(incoming?.sourceId),ea=a?effective(a):null;flat(n,c);place(n,(ea?.x??450)+275,(ea?.y??240)+110+(j%4)*8,10,10,{title:'',utilityVariant:n.utilityVariant??'joint',style:{fill:palette(c).accent,border:palette(c).accent}});}
 if(ix===1){let gy=1335,gw=Math.max(...figs.map(n=>effective(n).x+280))-430,gh=Math.max(...figs.map(n=>effective(n).y+450))-gy;let family=make(c,'later-generations',{kind:'container',title:'Divine Families',x:430,y:gy,width:480,height:130,collapsed:true,containerVariant:'contained',childLayout:'contained',childArrangement:'free',containerSizes:{collapsed:{width:480,height:130,sizeMode:'fixed'},expanded:{width:gw,height:gh,sizeMode:'fixed'}},style:style(c,{titleSize:31})});for(let f of figs.filter(n=>levelMap.get(n.id)>=2))put(f,{parent:family.key});for(let j of joints.filter(n=>(effective(n).y)>=gy))put(j,{parent:family.key});}
 misc(c);workPlans.push({workId:c.id,title:titles[ix],family:'genealogy',focus:figs[Math.floor(figs.length/2)].id,expanded:figs.map(n=>n.id)});
}

// Variant accounts: comparable subject disclosures rather than a long wall
// of cautions. The alternatives themselves remain fully readable on opening.
{
 const c=base.canvases[3],ns=rootNodes(c),subject=ns.find(n=>n.kind==='text'&&n.parentId===c.rootId),outer=ns.find(n=>n.kind==='container');archive(c,40,1100,outer);header(c,subject);
 const vars=ns.filter(n=>n.kind==='text'&&/variants$/.test(n.title));vars.forEach((n,j)=>{flat(n,c);place(n,50+j%5*420,210+Math.floor(j/5)*285,340,220,{title:shorten(n.title),text:brief(n.text,180),collapsed:true,style:style(c,{titleSize:27,fontSize:20,textAlign:'left'})});});
 ns.filter(n=>n.kind==='canvas-link').forEach((n,j)=>portal(n,c,450+j*300,1800,n.title==='Collection directory'?'Greek Mythology':shorten(n.title)));misc(c);workPlans.push({workId:c.id,title:titles[3],family:'variant-comparison',focus:vars[3].id});
}

// Hesiod's argument has an ordered spine with thematic branches. The original
// long readings are retained in the supporting document rather than squeezed.
for(let ix of [39,40]){
 const c=base.canvases[ix],ns=rootNodes(c),subject=ns.find(n=>n.kind==='text'&&n.parentId===c.rootId),outer=ns.find(n=>n.kind==='container');archive(c,40,950,outer);header(c,subject);
 const seq=ns.filter(n=>n.parentId===outer.id&&n.kind==='text');seq.forEach((n,j)=>{flat(n,c);let x=50+j%5*480,y=250+Math.floor(j/5)*300;let name=n.title;const renames={'The Muses and the poet':'The Muses','Before the Olympians':'First Beings','Gaia against Uranus':'Earth and Sky','A branching divine world':'Divine Families','Hecate keeps her honors':'Hecate','Cronus and the hidden child':'Cronus','The sons of Iapetus':'Iapetids','Sacrifice and stolen fire':'Prometheus','The unnamed first woman':'The First Woman','War and the winning allies':'Titanomachy','Boundaries below the earth':'Tartarus','The challenge of Typhoeus':'Typhoeus','Marriage and succession':'Zeus’s Household','One poem, many traditions':'Traditions','Author and date':'Hesiod','Reading power critically':'Divine Power','Zeus, Perses and the dispute':'Perses','Two kinds of Strife':'Strife','Why livelihood is difficult':'Livelihood','Pandora and the two brothers':'Pandora','What remains in the jar?':'Elpis','Gold and silver generations':'Gold and Silver','Bronze and the heroic race':'Bronze and Heroes','The speaker\'s iron present':'Iron','The hawk and nightingale':'Hawk and Nightingale','Dikē and the crooked verdict':'Dikē','Work without predatory gain':'Honest Labor','The farming year':'Farming','A reluctant guide to sailing':'Sailing','Household conduct and days':'Household','Pandora is not every woman':'Pandora’s Traditions','Races are not racial groups':'Generations','Instruction, not reportage':'The Speaker'};name=renames[name]??shorten(name);place(n,x,y,350,165,{title:name,text:brief(n.text,120),collapsed:true,style:style(c,{fontSize:19,titleSize:27,textAlign:'left'})});});
 // Row transitions use actual ordered argument edges; routing is applied below.
 seq.slice(0,ix===39?13:14).forEach((n,j,a)=>{if(j)edge(c,'argument-'+j,a[j-1],n,'','chronological');});
 ns.filter(n=>n.kind==='canvas-link').forEach(n=>portal(n,c,2050,1200,'Greek Mythology'));misc(c);workPlans.push({workId:c.id,title:titles[ix],family:'poem-argument',focus:seq[0].id});
}

// Roman correspondences are pairings in particular traditions, never an
// assertion that a Roman deity is only a renamed Greek figure.
{
 const c=base.canvases[41],ns=rootNodes(c),subject=ns.find(n=>n.kind==='text'&&n.parentId===c.rootId),outer=ns.find(n=>n.kind==='container');archive(c,40,1600,outer);header(c,subject);const pairs=ns.filter(n=>n.kind==='text'&&n.title.includes('↔')),refs=ns.filter(n=>n.kind==='reference');
 pairs.forEach((n,j)=>{flat(n,c);let x=50+(j%3)*900,y=230+Math.floor(j/3)*225;let [roman,greek]=n.title.split(' ↔ ');place(n,x,y,330,120,{title:roman,text:'',collapsed:true,style:style(c,{titleSize:29})});const r=refs[j];flat(r,c);place(r,x+460,y,330,120,{title:greek,text:'',collapsed:true,style:style(c,{titleSize:27})});edge(c,'pair-'+j,n,r,'associated','mutual',{sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5}});});
 ns.filter(n=>n.kind==='canvas-link').forEach((n,j)=>portal(n,c,1800+j*300,1640,n.title==='Collection directory'?'Greek Mythology':'Roman Worship'));misc(c);workPlans.push({workId:c.id,title:titles[41],family:'paired-comparison',focus:pairs[4].id});
}

// Roman worship uses a subject constellation, not mandatory Greek equivalents.
{
 const c=base.canvases[42],ns=rootNodes(c),subject=ns.find(n=>n.kind==='text'&&n.parentId===c.rootId);archive(c,50,1230);header(c,subject);const topics=ns.filter(n=>n.kind==='text'&&n.id!==subject.id&&!n.title.startsWith('ATLAS'));
 const names=['Janus','Quirinus','Penates','Lares','Correspondence','Ovid','Roman Traditions'];topics.forEach((n,j)=>{flat(n,c);let angle=-Math.PI/2+j*Math.PI*2/topics.length,x=950+Math.cos(angle)*740,y=700+Math.sin(angle)*440;place(n,x,y,340,170,{title:names[j],text:brief(n.text,105),collapsed:true,style:style(c,{titleSize:29,fontSize:19})});});
 let centre=make(c,'roman-worship',{title:'Roman Worship',x:920,y:630,width:390,height:190,style:style(c,{shape:'ellipse',titleSize:35})});topics.forEach((n,j)=>edge(c,'context-'+j,centre,n));ns.filter(n=>n.kind==='canvas-link').forEach((n,j)=>portal(n,c,450+j*300,1500,n.title==='Collection directory'?'Greek Mythology':'Counterparts'));misc(c);workPlans.push({workId:c.id,title:titles[42],family:'ritual-constellation',focus:centre.key});
}

// The collection entrance is itself a relationship map: origins, divine
// figures, poems and Roman reception are separate, useful branches.
{
 const c=base.canvases[0],ns=rootNodes(c),subject=ns.find(n=>n.kind==='text'),groups=ns.filter(n=>n.kind==='container');archive(c,40,1050);flat(subject,c);place(subject,1080,700,420,180,{title:'Greek Mythology',text:'',style:style(c,{shape:'ellipse',titleSize:42})});
 const positions=[[50,240],[1750,120],[1750,1100]],names=['Divine Families','Epic and Hesiod','Roman Reception'];groups.forEach((g,j)=>{let[x,y]=positions[j];collapse(g,c,x,y,500,170,names[j]);put(g,{containerSizes:{collapsed:{width:500,height:170,sizeMode:'fixed'},expanded:{width:900,height:760,sizeMode:'fixed'}}});ownChildren(g).forEach((n,k)=>place(n,x+25+k%2*420,y+40+Math.floor(k/2)*170,390,145,{title:shorten(n.title).replace(/^Hesiod: /,''),style:style(c,{titleSize:25}),text:''}));edge(c,'subject-'+j,subject,g);});
 const input=ns.find(n=>n.kind==='input');if(input)stash(input,c);misc(c);workPlans.push({workId:c.id,title:titles[0],family:'collection-map',focus:subject.id,expanded:groups.map(n=>n.id)});
}

// Dossier directory is a thematic branch tree with readable portal leaves.
{
 const c=base.canvases[47],ns=rootNodes(c),subject=ns.find(n=>n.kind==='text'),outer=ns.find(n=>n.kind==='container'&&/Browse/.test(n.title));archive(c,40,1600);header(c,subject);let gs=ns.filter(n=>n.kind==='container'&&n.parentId===outer.id);let names=['First Gods','Sovereignty','Craft and Wild','Desire and War','Life and Death'];
 // Flatten obsolete packing frame into the reading surface while preserving ID.
 stash(outer,c);gs.forEach((g,j)=>{collapse(g,c,50+j*570,240,460,160,names[j]);put(g,{containerSizes:{collapsed:{width:460,height:160,sizeMode:'fixed'},expanded:{width:460,height:1260,sizeMode:'fixed'}}});let ps=[];for(let n of ownChildren(g)){if(n.kind==='canvas-link')ps.push(n);else if(n.kind==='container'){ps.push(...ownChildren(n).filter(c=>c.kind==='canvas-link'));stash(n,c);}}ps.forEach((n,k)=>{place(n,75+j*570,290+k*180,410,150,{parent:ref(g),title:shorten(n.title),text:'',style:style(c,{titleSize:25})});});ownChildren(g).filter(n=>n.kind==='text').forEach(n=>stash(n,c));});
 ns.filter(n=>n.kind==='canvas-link'&&n.parentId===c.rootId).forEach(n=>portal(n,c,2450,1640,'Greek Mythology'));misc(c);workPlans.push({workId:c.id,title:titles[47],family:'thematic-directory',focus:gs[1].id,expanded:gs.map(n=>n.id)});
}

// Supporting Works keep their subject matter, but use purposeful display
// patterns rather than a deep scroll surface containing unrelated card sizes.
for(let ix of [43,44,46]){
 const c=base.canvases[ix],ns=rootNodes(c),subject=ns.find(n=>n.kind==='text'&&n.parentId===c.rootId),outer=ns.find(n=>n.kind==='container'&&n.parentId===c.rootId);archive(c,40,1250);header(c,subject);
 let prim=ns.filter(n=>n.assetId&&n.kind!=='canvas'&&n.parentId!==c.rootId||n.assetId&&n.parentId===c.rootId);if(ix===46)prim=ns.filter(n=>n.kind==='text'&&n.id!==subject.id&&!n.title.startsWith('ATLAS')||n.kind==='document');
 if(ix===43){const gallery=ns.find(n=>n.kind==='container'&&n.containerVariant==='gallery');prim=ns.filter(n=>n.assetId);prim.forEach((n,j)=>{flat(n,c);let x=50+j%4*530,y=210+Math.floor(j/4)*1100;place(n,x,y,420,510,{title:shorten(n.title).replace(/ · (?:ca\.|[\d]).*$/,''),collapsed:true,style:label(c,{imageFit:'contain',titleSize:22,padding:10})});ownChildren(n).forEach(child=>{place(child,x,y+565,420,470,{title:'Object Record',text:child.text,style:style(c,{textAlign:'left',fontSize:19})});});});}
 else{prim.forEach((n,j)=>{flat(n,c);place(n,50+j%3*550,230+Math.floor(j/3)*350,450,240,{title:shorten(n.title).replace(/^Complete /,'').replace(/^Whole collection reader/,'Collected Reading').replace(/^Reader and test companion/,'Companion').replace(/^Editable /,''),text:n.kind==='text'?brief(n.text,220):'',collapsed:true,style:style(c,{fontSize:20,titleSize:27,textAlign:n.kind==='text'?'left':'center'})});});}
 ns.filter(n=>n.kind==='canvas-link').forEach(n=>portal(n,c,2300,230,'Greek Mythology'));misc(c);workPlans.push({workId:c.id,title:titles[ix],family:ix===43?'museum-gallery':'reading-collection',focus:prim[0]?.id});
}

// The Lab is deliberately preserved as a functional system. Its introduction
// and test instructions belong here, outside the mythology diagrams.
{
 const c=base.canvases[45],ns=rootNodes(c),subject=ns.find(n=>n.kind==='text');archive(c,40,1600);header(c,subject,'Logic Lab');
 const groups=ns.filter(n=>n.kind==='container');groups.forEach((g,j)=>{let x=50+j%3*900,y=240+Math.floor(j/3)*500,old={x:g.x,y:g.y};let members=base.nodes.filter(n=>n.parentId===g.id);place(g,x,y,800,380,{title:g.title.replace(/ · (?:collapse me|truth-table exercise)/,''),collapsed:false,containerVariant:'contained',childLayout:'contained',childArrangement:'free',containerSizes:{expanded:{width:800,height:380,sizeMode:'fixed'},collapsed:{width:400,height:130,sizeMode:'fixed'}},style:style(c,{titleSize:27}),unset:['grid','cell']});members.forEach((n,k)=>{let px=x+30+(k%3)*250,py=y+70+Math.floor(k/3)*145;place(n,px,py,n.kind==='joint'?65:210,n.kind==='joint'?65:110,{style:style(c,{titleSize:20,fontSize:18}),text:n.text??''});});});
 const others=ns.filter(n=>n.parentId===c.rootId&&n.id!==subject.id&&!groups.includes(n));others.forEach((n,j)=>{flat(n,c);place(n,50+j%4*650,2000+Math.floor(j/4)*320,540,240,{title:shorten(n.title),style:style(c,{titleSize:24,fontSize:20,textAlign:'left'}),text:n.text??''});});workPlans.push({workId:c.id,title:titles[45],family:'functional-lab',focus:groups[0].id});
}

// Materialize node updates, then route visual links around unrelated boxes.
// Reserve a dedicated lower band for the complete readings. Its expanded
// frame includes the historical supporting identities, not the primary graph.
for(let c of base.canvases){const s=support.get(c.id);if(!s)continue;const boxRef=ref(s.box),items=[...rootNodes(c).map(n=>effective(n)),...[...added.values()].filter(n=>n.canvas===ckey(c))],byRef=new Map(items.map(n=>[n.id?nkey(n):n.key,n]));function below(n){let par=n.parent??(n.parentId?ref(allNodes.get(n.parentId)):'');let seen=new Set();while(par&&!seen.has(par)){if(par===boxRef)return true;seen.add(par);let p=byRef.get(par);par=p?.parent??(p?.parentId?ref(allNodes.get(p.parentId)):'');}return false;}
 const primary=items.filter(n=>ref(n)!==boxRef&&!below(n)),maxY=Math.max(500,...primary.map(n=>n.y+(n.kind==='container'?(n.containerSizes?.expanded?.height??n.height):n.height)));
 const ix=base.canvases.indexOf(c);let newX=50,newY=maxY+100;if(ix>=4&&ix<=24){newX=1200;newY=rootNodes(c).some(n=>n.assetId&&n.assetId!=='c3be4302-be7b-4796-867b-d70e968c057c')?1040:780;}else if((ix>=26&&ix<=31)||(ix>=33&&ix<=38)){newX=50;newY=900;}else if(ix===1||ix===2){newX=-850;newY=240;}else if(ix===25||ix===32){newX=2700;newY=225;}
 const dx=newX-s.x,dy=newY-s.y;for(let n of items.filter(below)){if(n.id)put(allNodes.get(n.id),{x:n.x+dx,y:n.y+dy});else{let a=added.get(n.key);a.x=n.x+dx;a.y=n.y+dy;}}
 let frame={x:newX,y:newY,width:330,height:110,containerSizes:{collapsed:{width:330,height:110,sizeMode:'fixed'},expanded:{width:820,height:Math.max(650,700+Math.ceil(s.count/2)*190),sizeMode:'fixed'}}};if(s.box.id)put(s.box,frame);else Object.assign(added.get(s.box.key),frame);
 if(ix===1){const count=items.filter(n=>(n.parent??(n.parentId?ref(allNodes.get(n.parentId)):''))===boxRef).length;Object.assign(added.get(s.box.key),{collapsed:false,width:820,height:650,containerVariant:'window',childLayout:'contained',childArrangement:'grid',grid:{columns:2,rows:Math.ceil(count/2),cellWidth:360,cellHeight:165,windowColumns:2,windowRows:3,showHeaders:false,gap:12,fillOrder:'row'},containerSizes:{collapsed:{width:330,height:110,sizeMode:'fixed'},expanded:{width:820,height:650,sizeMode:'fixed'}}});}
}
// Historical snap ties bound decorative headings to packing surfaces. Retain
// their identities and binding behavior without leaving author commentary on
// the graph or violating same-space constraints after the reorganization.
for(let l of base.links.filter(l=>l.snap)){
 let a=allNodes.get(l.sourceId),b=allNodes.get(l.targetId),ua=updates.get(a.id),ub=updates.get(b.id),c=base.canvases.find(c=>c.id===l.canvasId);
 for(let meta of [a,b].filter(n=>/ATLAS|SELECTED GENEALOGY|FOLLOW A|How to read and test/.test(n.title))){flat(meta,c);place(meta,30+base.nodes.indexOf(meta)%8*24,8,14,14,{title:'',text:'',style:label(c,{titleSize:10,fontSize:10})});recipe.links.push({key:lkey(l),style:{color:palette(c).bg,width:1}});}
 ua=updates.get(a.id);ub=updates.get(b.id);
 if(ua?.parent!==ub?.parent){let ornamental=[a,b].find(n=>n.assetId==='c3be4302-be7b-4796-867b-d70e968c057c');if(ornamental){flat(ornamental,c);place(ornamental,250,8,14,14,{contentType:'text',title:'',text:'',style:label(c,{titleSize:10,fontSize:10})});recipe.links.push({key:lkey(l),style:{color:palette(c).bg,width:1}});}}
 ua=updates.get(a.id);ub=updates.get(b.id);
 if(ua?.parent!==ub?.parent){let meta=/ATLAS|SELECTED GENEALOGY|FOLLOW A/.test(a.title)?a:/ATLAS|SELECTED GENEALOGY|FOLLOW A/.test(b.title)?b:null;if(meta){flat(meta,c);place(meta,30+(meta===a?0:24),8,14,14,{title:'',text:'',style:label(c,{titleSize:10,fontSize:10})});recipe.links.push({key:lkey(l),style:{color:palette(c).bg,width:1}});}}
}
recipe.nodes=[...updates.values(),...added.values()];
recipe.links=[...recipe.links.reduce((m,l)=>m.set(l.key,{...m.get(l.key),...l}),new Map()).values()];
let first=applyRecipe(base,recipe);
const candidate=first.project;
// Orthogonal visibility-graph router. It uses all effective node bounds and
// routes through saved Pivots rather than opaque polyline points.
function routeOrthogonal(a,b,obstacles,preferVertical=false){
 const reverse=b.x+b.width/2<a.x+a.width/2;
 let sourceAnchor=preferVertical?{x:.5,y:1}:{x:reverse?0:1,y:.5},targetAnchor=preferVertical?{x:.5,y:0}:{x:reverse?1:0,y:.5};
 if(a.containerSizes)sourceAnchor={x:a.containerSizes.collapsed?.width!==a.containerSizes.expanded?.width?0:sourceAnchor.x,y:0};
 if(b.containerSizes)targetAnchor={x:b.containerSizes.collapsed?.width!==b.containerSizes.expanded?.width?0:targetAnchor.x,y:0};
 const actualStart=a.kind==='junction'?{x:a.x+a.width/2,y:a.y+a.height/2}:{x:a.x+a.width*sourceAnchor.x,y:a.y+a.height*sourceAnchor.y};
 const actualEnd=b.kind==='junction'?{x:b.x+b.width/2,y:b.y+b.height/2}:{x:b.x+b.width*targetAnchor.x,y:b.y+b.height*targetAnchor.y};
 const start=a.kind==='junction'?actualStart:a.containerSizes?{x:actualStart.x,y:actualStart.y-20}:preferVertical?{x:actualStart.x,y:actualStart.y+20}:{x:actualStart.x+(reverse?-20:20),y:actualStart.y};
 const end=b.kind==='junction'?actualEnd:b.containerSizes?{x:actualEnd.x,y:actualEnd.y-20}:preferVertical?{x:actualEnd.x,y:actualEnd.y-20}:{x:actualEnd.x+(reverse?20:-20),y:actualEnd.y};
 const pad=16, obs=obstacles.filter(n=>n.kind!=='junction').map(n=>({x:n.x-pad,y:n.y-pad,r:n.x+n.width+pad,b:n.y+n.height+pad}));
 function lineClear(p,q){return !obs.some(r=>p.x===q.x?p.x>r.x&&p.x<r.r&&Math.max(p.y,q.y)>r.y&&Math.min(p.y,q.y)<r.b:p.y>r.y&&p.y<r.b&&Math.max(p.x,q.x)>r.x&&Math.min(p.x,q.x)<r.r);}
 const mid=preferVertical?(start.y+end.y)/2:(start.x+end.x)/2;
 let simple=preferVertical?[start,{x:start.x,y:mid},{x:end.x,y:mid},end]:[start,{x:mid,y:start.y},{x:mid,y:end.y},end];
 if(simple.every((p,i)=>!i||lineClear(simple[i-1],p)))return{start,end,points:[start,...simple.slice(1,-1),end],sourceAnchor,targetAnchor};
 const xs=[start.x,end.x,...obs.flatMap(r=>[r.x-8,r.r+8])],ys=[start.y,end.y,...obs.flatMap(r=>[r.y-8,r.b+8])];
 const ux=[...new Set(xs)].sort((a,b)=>a-b),uy=[...new Set(ys)].sort((a,b)=>a-b);let si=ux.indexOf(start.x),sj=uy.indexOf(start.y),ti=ux.indexOf(end.x),tj=uy.indexOf(end.y),W=ux.length;
 const id=(x,y)=>y*W+x, dist=new Map([[id(si,sj),0]]),prev=new Map(),open=[{x:si,y:sj,g:0,f:0}],closed=new Set();let targetId=id(ti,tj);
 while(open.length){open.sort((a,b)=>b.f-a.f);let cur=open.pop(),ci=id(cur.x,cur.y);if(closed.has(ci))continue;if(ci===targetId){let pts=[];for(let at=ci;at!==undefined;at=prev.get(at))pts.push({x:ux[at%W],y:uy[Math.floor(at/W)]});pts.reverse();let slim=pts.filter((p,i)=>i===0||i===pts.length-1||!((pts[i-1].x===p.x&&p.x===pts[i+1].x)||(pts[i-1].y===p.y&&p.y===pts[i+1].y)));return{start,end,points:slim,sourceAnchor,targetAnchor};}closed.add(ci);for(let [dx,dy]of[[1,0],[-1,0],[0,1],[0,-1]]){let x=cur.x+dx,y=cur.y+dy;if(x<0||y<0||x>=ux.length||y>=uy.length)continue;let ni=id(x,y),p={x:ux[cur.x],y:uy[cur.y]},q={x:ux[x],y:uy[y]};if(!lineClear(p,q))continue;let g=cur.g+Math.abs(p.x-q.x)+Math.abs(p.y-q.y)+10;if(g<(dist.get(ni)??Infinity)){dist.set(ni,g);prev.set(ni,ci);open.push({x,y,g,f:g+Math.abs(q.x-end.x)+Math.abs(q.y-end.y)});}}}
 return{start,end,points:[start,...simple.slice(1,-1),end],sourceAnchor,targetAnchor};
}
const routing={version:1,bindings:first.aliases,nodes:[],links:[],routes:[]};
const newById=new Map(candidate.nodes.map(n=>[n.id,n]));
const expanded=structuredClone(candidate);for(let n of expanded.nodes){if(n.collapsed&&n.containerSizes?.expanded)Object.assign(n,n.containerSizes.expanded);n.collapsed=false;}const expandedNav=navigationIndex(expanded),expandedNodes=new Map(expanded.nodes.map(n=>[n.id,{...n,...expandedNav.bounds(n)}]));
for(let c of candidate.canvases){let ci=base.canvases.findIndex(x=>x.id===c.id),nodes=expanded.nodes.filter(n=>n.canvasId===c.id&&n.kind!=='canvas').map(n=>expandedNodes.get(n.id)),links=candidate.links.filter(l=>l.canvasId===c.id);
 for(let l of links){if(l.snap||l.kind!=='visual'&&ci!==45)continue;let a=expandedNodes.get(l.sourceId),b=expandedNodes.get(l.targetId);if(!a||!b)continue;
  if((ci>=26&&ci<=31)||(ci>=33&&ci<=38))continue;
  const ancestor=(n,id)=>{while(n?.parentId){if(n.parentId===id)return true;n=newById.get(n.parentId);}return false;};
  let obstacles=nodes.filter(n=>!ancestor(a,n.id)&&!ancestor(b,n.id));
  // Collapsed container frame is not a valid obstacle for its own interior.
  const vertical=ci===1||ci===2;let r=routeOrthogonal(a,b,obstacles,vertical),alias=Object.entries(first.aliases.links).find(([k,id])=>id===l.id)?.[0]??'existing-'+l.id;
  if(!first.aliases.links[alias])routing.bindings.links[alias]=l.id;
  let points=r.points.filter((p,i,a)=>!(i&&p.x===a[i-1].x&&p.y===a[i-1].y));
  const endpoint=(n,anchor)=>n.kind==='junction'?{x:n.x+n.width/2,y:n.y+n.height/2}:{x:n.x+n.width*anchor.x,y:n.y+n.height*anchor.y};let pathPoints=[endpoint(a,r.sourceAnchor),...points,endpoint(b,r.targetAnchor)],slim=[];for(let p of pathPoints){if(slim.length&&p.x===slim.at(-1).x&&p.y===slim.at(-1).y)continue;while(slim.length>1&&((slim.at(-2).x===slim.at(-1).x&&slim.at(-1).x===p.x)||(slim.at(-2).y===slim.at(-1).y&&slim.at(-1).y===p.y)))slim.pop();slim.push(p);}points=slim.slice(1,-1);
  if(!points.length){routing.links.push({key:alias,sourceAnchor:r.sourceAnchor,targetAnchor:r.targetAnchor,unset:['points']});continue;}
  let owner=l.parentId??c.rootId;if(ci===1){let family=Object.entries(first.aliases.nodes).find(([key])=>key===ckey(base.canvases[ci])+'/later-generations')?.[1];if(family&&(ancestor(a,family)||ancestor(b,family)))owner=family;}
  routing.routes.push({key:alias,canvas:c.id,parent:owner,source:l.sourceId,target:l.targetId,kind:l.kind,relation:l.relation,direction:l.direction??(l.relation==='chronological'?'forward':'none'),label:l.label??'',sourceAnchor:r.sourceAnchor,targetAnchor:r.targetAnchor,style:l.style,via:points.map((p,j)=>({key:alias+'/bend-'+j,x:p.x,y:p.y,variant:'pivot',width:6,height:6,style:{fill:palette(c).accent,border:palette(c).accent}}))});
 }
}
let second=applyRecipe(candidate,routing);
// Remove arrowheads and repeated labels from the inbound routing segments.
const tidy={version:1,bindings:second.aliases,nodes:[],links:Object.entries(second.aliases.links).filter(([k])=>k.includes('/segment/')).map(([key])=>({key,direction:'none',label:''}))};
tidy.nodes.push({key:'w1/reading',collapsed:true,width:330,height:110,sizeMode:'fixed'});
const labId=base.canvases[45].id,labFrames=candidate.nodes.filter(n=>n.canvasId===labId&&n.kind==='container').map(n=>({...n,...expandedNav.bounds(n)}));
for(let f of labFrames)if(f.rules?.nodeKinds&&!f.rules.nodeKinds.includes('junction'))tidy.nodes.push({key:nkey(f),rules:{nodeKinds:[...f.rules.nodeKinds,'junction']}});
for(let [key,id] of Object.entries(second.aliases.nodes)){let n=second.project.nodes.find(n=>n.id===id);if(n?.canvasId!==labId||n.kind!=='junction'||base.nodes.some(b=>b.id===id))continue;let centre={x:n.x+n.width/2,y:n.y+n.height/2},frame=labFrames.filter(f=>centre.x>=f.x&&centre.x<=f.x+f.width&&centre.y>=f.y&&centre.y<=f.y+f.height).sort((a,b)=>a.width*a.height-b.width*b.height)[0];if(frame)tidy.nodes.push({key,parent:frame.id});}
let final=applyRecipe(second.project,tidy);
const beforeEvaluation=evaluate(base),afterEvaluation=evaluate(final.project),outputs=base.nodes.filter(n=>n.kind==='output').map(n=>({id:n.id,title:n.title,before:beforeEvaluation.values?.[n.id]??beforeEvaluation.values?.get?.(n.id),after:afterEvaluation.values?.[n.id]??afterEvaluation.values?.get?.(n.id)}));fs.writeFileSync(OUT+'/functional-equivalence.json',JSON.stringify({before:beforeEvaluation,after:afterEvaluation,outputs},null,2));
fs.writeFileSync(OUT+'/recipe.json',JSON.stringify(recipe,null,2));fs.writeFileSync(OUT+'/routing.json',JSON.stringify(routing,null,2));fs.writeFileSync(OUT+'/tidy.json',JSON.stringify(tidy,null,2));
fs.writeFileSync(OUT+'/draft.json',JSON.stringify(final.project));fs.writeFileSync(OUT+'/aliases.json',JSON.stringify(final.aliases,null,2));fs.writeFileSync(OUT+'/inspection.json',JSON.stringify(inspectProject(final.project),null,2));fs.writeFileSync(OUT+'/review-plan.json',JSON.stringify(workPlans,null,2));
fs.writeFileSync(OUT+'/inventory.json',JSON.stringify(final.project.canvases.map(c=>({id:c.id,title:c.title,nodes:final.project.nodes.filter(n=>n.canvasId===c.id).map(({id,kind,title,text,x,y,width,height,parentId,collapsed,assetId,targetId})=>({id,kind,title,text,x,y,width,height,parentId,collapsed,assetId,targetId})),links:final.project.links.filter(l=>l.canvasId===c.id)})),null,2));
console.log(JSON.stringify({works:final.project.canvases.length,nodes:final.project.nodes.length,links:final.project.links.length,assets:final.project.assets.length,warnings:final.report.warnings?.length,out:OUT}));



