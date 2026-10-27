/** Visual mythology refinement. Offline recipes only; no host or live storage writes. */
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {applyRecipe, inspectProject} from '../../src/core/authoring.ts';
import {commitSnapshots} from '../../src/core/model.ts';
import {validateProject} from '../../src/core/rules.ts';
import {geometryDiagnostics} from '../../integrations/mcp/src/authoring-worker.mjs';
import {buildPublication} from '../../src/publication.ts';

const ROOT='I:/dev/Meridian', OUT=ROOT+'/output/mythology-refinement-20261023/visual';
const base=JSON.parse(await fs.readFile(ROOT+'/output/mythology-refinement-20261023/baseline/e35cba82-f644-416a-ac14-12645893ed88.json','utf8'));
const byId=new Map(base.nodes.map(n=>[n.id,n]));
const recipe={version:1,canvases:[],nodes:[],assets:[],links:[]};
const entries=new Map(), used=new Set(), changes=[];
const palettes=[
 {bg:'#101F29',fill:'#1D3540',border:'#5C7577',ink:'#F0E4CC',accent:'#D9B675'},
 {bg:'#F1E8D6',fill:'#FAF5E9',border:'#B99A69',ink:'#342B24',accent:'#986838'},
 {bg:'#11222D',fill:'#203B49',border:'#64858D',ink:'#E6EEEB',accent:'#D7B779'},
 {bg:'#F1E7D7',fill:'#FAF5E9',border:'#BA9C70',ink:'#392B24',accent:'#93613D'},
 {bg:'#20232D',fill:'#343944',border:'#8B7F86',ink:'#F0E7DC',accent:'#D6B077'},
];
const pFor=id=>palettes[base.canvases.findIndex(c=>c.id===(byId.get(id)?.canvasId??id))];
const style=(p,more={})=>({fill:p.fill,border:p.border,textColor:p.ink,accent:p.accent,fontFamily:'garamond',fontSize:22,titleSize:32,contentSizing:'fixed',textAlign:'center',shape:'rectangle',radius:0,borderWidth:1,padding:14,childPadding:12,contentGap:12,imageFit:'contain',...more});
function edit(id,props={}){const n=byId.get(id);assert(n,id);used.add(id);let e=entries.get(id);if(!e){e={key:'n/'+id,existingId:id,unset:['style','titleRuns','textRuns','cell','childOffset','expansionTray','containerSizes'],sizeMode:'fixed',childPosition:'manual',placement:'free',style:style(pFor(id))};entries.set(id,e);}Object.assign(e,props);if(props.title!==undefined)e.tag=props.title||n.tag;if(props.style)e.style=style(pFor(id),props.style);return e;}
function tile(id,title,x,y,width=300,height=130,more={}){return edit(id,{title,text:'',x,y,width,height,...more});}
function label(id,title,x,y,width=1000,height=90,more={}){return tile(id,title,x,y,width,height,{style:{shape:'none',borderWidth:0,padding:0,titleSize:48},...more});}
function media(id,title,x,y,width,height,more={}){return tile(id,title,x,y,width,height,{contentType:'asset',style:{shape:'none',borderWidth:0,padding:0,titleSize:28},...more});}
function freeContainer(id,title,x,y,width,height,collapsed=false,small={width:340,height:130}){return edit(id,{title,text:'',x,y,width:collapsed?small.width:width,height:collapsed?small.height:height,collapsed,containerVariant:'contained',childLayout:'contained',childArrangement:'free',containerSizes:{expanded:{width,height,sizeMode:'fixed'},collapsed:{...small,sizeMode:'fixed'}},unset:['style','titleRuns','textRuns','grid','cell','childOffset','expansionTray'],style:{shape:'rectangle',borderWidth:1,fill:pFor(id).bg,childPadding:14}});}
function gallery(id,title,x,y,width=500,height=650){return edit(id,{title,text:'',x,y,width,height,collapsed:false,containerVariant:'gallery',childLayout:'contained',childArrangement:'free',unset:['style','titleRuns','textRuns','grid','cell','childOffset','expansionTray','containerSizes'],style:{fill:pFor(id).bg,borderWidth:1,childPadding:12}});}
function pivot(id,x,y){return edit(id,{title:'',x:x-6,y:y-6,width:12,height:12,style:{shape:'circle',fill:pFor(id).accent,borderWidth:0,padding:0}});}
function edge(id,props={}){const l=base.links.find(l=>l.id===id);assert(l,id);const e={key:'l/'+id,existingId:id,unset:['sourceAnchor','targetAnchor','points','style'],style:{color:pFor(l.canvasId).accent,width:2,lineStyle:l.style?.dashed?'dashed':'solid'},...props};recipe.links.push(e);return e;}
function addNode(key,canvas,props){const p=pFor(canvas);recipe.nodes.push({key,canvas,...props,sizeMode:'fixed',childPosition:'manual',placement:'free',style:style(p,props.style)});}
function addEdge(key,canvas,source,target,props={}){recipe.links.push({key,canvas,source,target,kind:'visual',relation:'generic',style:{color:pFor(canvas).accent,width:2,lineStyle:'solid'},...props});}
const c=base.canvases;
const titles=['Myths in Relation','Divine Family','Odyssey','Greek & Roman Gods','Persephone'];
for(let i=0;i<c.length;i++)recipe.canvases.push({key:'work/'+i,existingId:c[i].id,title:titles[i],style:{background:palettes[i].bg,gridVisible:false,backgroundPattern:'none',portalStyle:'doorway',portalColor:palettes[i].accent}});

// 1: a subject constellation. The central subject, not introductory copy, owns its four branches.
const main='9ca61bc8-e4a3-4743-b4da-437668cc288e';freeContainer(main,'Greek Mythology',0,0,1450,1050);edit(main,{style:{borderWidth:0,shape:'none',fill:palettes[0].bg}});
edit(main,{title:'',tag:'Greek Mythology'});
label('248f015d-3130-4be6-8ae3-335ffa2eab26','Greek Mythology',540,445,370,115,{parent:main,style:{shape:'none',borderWidth:0,padding:0,titleSize:46}});
media('d9ff3180-61a6-41bd-a3e1-86da91040ead','Odysseus & Circe',555,100,340,310,{parent:main});
const portals=[['2293cb55-e5b2-4dc4-9f6a-4c65f166efa8','Divine Family',55,445],['62407efd-648d-4287-af87-6a9dc5d02a6b','Odyssey',1095,445],['cd873827-258c-40e4-b310-7800393de8c3','Greek & Roman',330,790],['1724149d-9477-4ca5-bc49-981422052005','Persephone',820,790]];
for(const [id,title,x,y] of portals){tile(id,title,x,y,300,160,{parent:main,style:{portalColor:palettes[0].accent,titleSize:30,backgroundDisplay:'hover'}});addEdge('subject/'+title.replace(/[^a-z0-9]+/gi,'-'),c[0].id,'248f015d-3130-4be6-8ae3-335ffa2eab26',id,{parent:main,direction:'none'});}
gallery('005fe68a-bc30-4b11-af1d-4dadec799b1c','Zeus & Jupiter',1590,70,500,720);
const zeusIds=['aa276759-3f35-4289-9dc0-0cdc98bf21e1','9bdd84dc-667c-4acb-8708-e3815d8e9e27','3a911d72-09f6-4d03-abfc-963bc35b1ba7','eaac40b1-d25d-464c-aebc-8c61cf5650ad','fb51f319-64bc-4329-8d64-565d066ab2df'];
const zeusNames=['Probable Zeus','Zeus & Giant','Jupiter · Roman Bronze','Zeus & Ganymede · Palumba','Jupiter · Renaissance Bronze'];
zeusIds.forEach((id,i)=>media(id,zeusNames[i],1602,82,476,630));
// Original generic notes tethers remain owned by the closed Notes surface; route them outside the subject graph.
edge('28cced08-813f-4b04-9be7-411cac73ef65',{parent:'a629a20d-cff4-4dec-afed-28e3b00473f4',sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5},points:[{x:2500,y:1240},{x:2500,y:1650},{x:-150,y:1650},{x:-150,y:525}]});
edge('a50f1775-b28a-4bd5-8192-e756872cf95b',{parent:'a629a20d-cff4-4dec-afed-28e3b00473f4',sourceAnchor:{x:1,y:.5},targetAnchor:{x:1,y:.1},points:[{x:1490,y:255},{x:1490,y:920},{x:2500,y:920},{x:2500,y:1036}]});

// 2: actual descent, with separated lower lineage routes and artwork as a margin, not an oversized header.
const fam='e3bdb991-aea2-45c9-944a-c1397d59cc76';freeContainer(fam,'Divine Family',0,0,1800,1150);edit(fam,{style:{borderWidth:0,shape:'none',fill:palettes[1].bg}});
edit(fam,{title:'',tag:'Divine Family'});
label('049fd97d-ee90-4d2d-962b-fe53a1d4a1ff','Children of Cronus',300,-130,1200,100);
tile('0999b750-f695-4e94-8fb7-70fe7ca6886b','Cronus',540,80,300,130);
tile('ffe4d976-9134-468d-a204-497e2de5278d','Rhea',1000,80,300,130);
label('aac9aed1-7b0f-41ce-9dba-6ae532a9a9be','Divine Children',730,295,370,70,{style:{shape:'none',borderWidth:0,titleSize:30,padding:0}});
pivot('1d4950f7-34cd-49cb-a60e-5248fe65c0a4',915,435);
const children=['5fc36447-6d46-4599-8ff5-c6809f36879d','58ae61be-8983-4a2f-ba11-dba51db5bf66','fbf51c6e-21d7-4d23-9d24-e9705c7baf56','6ce7811a-e85b-458c-ae1d-d7f4c5dfaebc','83fe4ee0-2d14-40a6-a8ae-14f306dcb454','8f0ac7ac-22ef-400a-821f-0bc98a5cf563'];
children.forEach((id,i)=>tile(id,byId.get(id).title,35+i*290,550,245,130));
tile('1201ccca-3d77-4fa7-9f30-db853a35819c','Persephone',1000,940,340,130);
tile('402ef00c-503e-4f7a-affb-0d869a0e405a','Aphrodite',70,900,620,180,{text:'Sea foam in Hesiod; daughter of Zeus and Dione in the Iliad.',style:{backgroundDisplay:'hover',titleSize:32,fontSize:24}});
media('30ee0a87-4540-4742-9e0e-b760bcebe6a4','Probable Zeus',1920,30,330,400,{parent:c[1].rootId});
media('62af0d51-bbf6-4314-9a32-4eb56fe70e5a','Possible Demeter',1920,560,330,400);
tile('97ae0ecf-2bb8-4369-8724-46321bb28a2e','Myths in Relation',30,1270,320,160,{style:{backgroundDisplay:'hover',portalColor:palettes[1].accent}});
tile('662394dc-21bb-431e-88cc-49b14e903af6','Persephone',460,1270,320,160,{style:{backgroundDisplay:'hover',portalColor:palettes[1].accent}});
for(const id of ['60ac119c-0f5c-4be8-b5af-058c8e36c303','de8d2aea-1ac0-43f7-b18d-7f1bc434014e'])edge(id,{sourceAnchor:{x:.5,y:1},targetAnchor:{x:.5,y:0},label:'parent'});
edge('75cfa875-8d55-4d5c-afab-e45521c4689a',{sourceAnchor:{x:.5,y:1}});
for(const id of ['e30d8edf-7443-475e-826f-ce4ac7090803','21cf98b3-944a-49af-9d8f-2e612a73df48','767798a8-cc1d-4660-85ce-c7a878bfd306','fcad28d5-2326-4076-850d-fbbc3a076d8b','af07641e-4c79-4262-a407-e30426b93551','ee2eabe1-a4e0-497e-84be-0a676579198f'])edge(id,{targetAnchor:{x:.5,y:0}});
pivot('7ac76766-6b81-4879-ad32-def368fd6ee9',447.5,780);pivot('e1e7e01b-4d4a-4ec8-99d7-4f311e8c7617',1070,780);
pivot('abc6d971-255b-4afe-b99e-90c92dffd7b6',1607.5,850);pivot('863b7ab3-1e74-4c94-996e-2e94cf47b499',1270,850);
edge('af4e40bd-e7d5-462d-a0e6-51388f94a7d7',{sourceAnchor:{x:.5,y:1}});edge('b41baa11-7c62-415f-ab98-7ef76b5c0f58');edge('8255a682-8000-4bcc-80a0-c75c074327ee',{targetAnchor:{x:.2,y:0}});
edge('6dfee3eb-b958-4ed7-b39c-abcb1644a221',{sourceAnchor:{x:.5,y:1}});edge('01ff3c75-020e-4cbe-9b04-1de610c2daad');edge('9c0b541b-d82c-489a-8028-580d39ea8ee7',{targetAnchor:{x:.8,y:0}});
edge('763b0b0d-ea44-4f4b-9042-73acdea96283',{parent:'6e59c9b6-6739-4d34-8820-cfc210ac333c',sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5},points:[{x:2850,y:1480},{x:2850,y:1900},{x:-130,y:1900},{x:-130,y:575}]});

// 3: a chronological spine and a consistently right-growing flashback branch.
label('13f0f326-e8a1-4485-ba20-995fdbc999d9','Odyssey',0,-130,1200,100);
const stages=[['9c6c0ed5-c050-4513-95b5-9cb4e54e1e72','Telemachus',90,'Books 1–4'],['bb0df6df-fc03-4c74-a89d-140a64927ef2','Phaeacia',440,'Books 5–8'],['6f691c29-49ca-4ac3-b03f-44c7463f2bea','Odysseus Speaks',790,'Books 9–12'],['2e0b0f5b-38c3-4fa9-8045-c352c2fed7be','Ithaca',1260,'Books 13–24']];
stages.forEach(([id,t,y,text])=>tile(id,t,0,y,330,150,{text}));
for(const id of ['0ddf80ef-a591-484a-9153-ab391c6b060d','e4e337f2-425e-4867-9b83-e06570417c03','23f7bf50-093a-43a0-bc7b-a321b97c3b4d'])edge(id,{sourceAnchor:{x:.5,y:1},targetAnchor:{x:.5,y:0}});
const voyage='cbf37a78-5d51-470c-bc9b-2db3525744bd';freeContainer(voyage,'Remembered Voyage',550,775,1600,320,false);
edit(voyage,{title:'',tag:'Remembered Voyage'});
const episodes=[['75ea4050-aab9-4ae9-8602-eb0bd4559105','Polyphemus'],['26989b25-4020-453c-b895-ea9c6eb727e0','Circe'],['a91bfb08-fd20-4660-b20e-170217affdc9','The Dead'],['3b90c6db-cbd4-44ac-a4bb-8beb2433d7b6','Sirens & Shipwreck']];
episodes.forEach(([id,t],i)=>tile(id,t,580+i*400,845,320,155));
for(const id of ['99b8e422-ef90-41c7-b908-a81784c24195','bf94a600-e76e-4850-ad58-d799a0140a30','143af979-75f8-4477-9764-41c3b50946b5'])edge(id,{sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5},label:'then'});
media('3f5f17fa-efd6-4af6-a6fb-f85405274dc5','Circe',550,30,510,570,{parent:c[2].rootId});
media('4d9c1585-fffe-4c7e-a343-a8110e2e9c1b','Muses & Sirens',1200,130,950,420);
label('95f44b12-5a4e-4b63-94d4-3b8ba9892d81','Remembered Voyage',550,690,1600,55,{style:{shape:'none',borderWidth:0,padding:0,titleSize:34}});
pivot('406151cd-b5c5-45a2-9ce8-9c48e19c7ef6',440,865);pivot('f8872160-2953-4689-a700-e06bd418c366',440,835);
edge('3dc7f67c-203c-4572-ae91-68c845e46c3f',{sourceAnchor:{x:1,y:.5}});edge('7121a4de-b905-45ef-8023-1f02c5f12851');edge('70937122-ebd9-4316-a160-807a9f91b66c',{targetAnchor:{x:0,y:.2},label:'remembers'});
tile('b2c19888-3507-4eb8-b069-75794b42bad9','Myths in Relation',0,1580,330,160,{style:{backgroundDisplay:'hover'}});
const notes3='614e01f9-e50b-487a-b8cd-1ff9ab94228e';

// 4: aligned comparisons. Correspondence is expressed by relationships, not introductory commentary.
label('f2392784-751c-4776-9474-58ff800610d2','Greek & Roman Gods',400,-130,1150,100);
media('f3c80aef-552d-4fb8-8188-be8de8c90b81','Probable Zeus',0,25,300,390);
tile('5f846fcc-6093-4f21-8fe4-545bee215554','Zeus',450,130,310,140);
tile('db9dfbb1-4570-4e63-9659-d8345d01f078','Jupiter',1190,130,310,140);
media('51692c49-a5c8-482f-a68e-f8fe21098292','Zeus or Jupiter',1640,25,300,390);
media('c953917c-d162-4ce8-b1df-cd3a3745d001','Athena',0,600,300,390);
tile('d6b7dc44-32b6-4dad-a7a0-a3e618d0dfe1','Athena',450,710,310,140);
tile('6a6a012b-9dc5-4bdd-94c2-e3c8d663caa5','Minerva',1190,710,310,140);
tile('2d7d4cad-8a71-4ffa-9131-e5043c2dc154','Mars',1190,1200,310,140);
addNode('comparison/ares',c[3].id,{kind:'media',title:'Ares',contentType:'text',text:'',x:450,y:1200,width:310,height:140});
addEdge('comparison/ares-mars',c[3].id,'comparison/ares','2d7d4cad-8a71-4ffa-9131-e5043c2dc154',{relation:'mutual',label:'identified with',sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.5}});
pivot('50dcd595-3f4c-4892-a2c0-38dabd6b3090',975,200);pivot('de0ad6ca-4428-4088-b23c-8bf4cc2c5dba',975,780);
edge('413f6de7-3d09-4b3c-a7a9-0345b95007cf',{sourceAnchor:{x:1,y:.5}});edge('3c13f9a3-85b7-4c0f-b999-29b0b3f771b0',{targetAnchor:{x:0,y:.5}});
edge('5eeb8198-c447-4b26-88b9-0515b0f69c91',{sourceAnchor:{x:1,y:.5}});edge('8b3c453c-2875-457b-bfe8-f74b5978b502',{targetAnchor:{x:0,y:.5}});
gallery('c8abea95-2f95-49d9-a7b1-02cf417c472d','Myth & Reception',2130,50,560,900);
const romanArt=[['c3cf3a0c-e866-489c-b1cd-104936874be9','Perseus & Andromeda'],['ba8ba7e5-ca77-455c-98c4-d25a8e58150c','Paris · Turchi, c. 1640'],['e50e59cf-a629-4b90-a2ea-8d73430ae3f4','Paris · Allori, 1583']];romanArt.forEach(([id,t])=>media(id,t,2142,62,536,810));
tile('0c0c66c8-275c-45f8-87e9-580615f7cf4e','Myths in Relation',0,1510,330,160,{style:{backgroundDisplay:'hover'}});

// 5: causal ring. Preserve the abduction and divided-year qualifications; no romance/modern six-seed claim.
label('f4856111-d7bf-4bbe-9f5c-cfd8e8e1c0fe','Persephone',480,-130,700,100);
tile('149cd7a5-5bcb-4369-9bd6-5c6d0ff27e85','Demeter',40,140,340,150);
tile('2f03c006-3bbb-4b9f-9213-87020c76149e','Hades',1280,140,340,150,{text:'Abduction authorized by Zeus.'});
media('6915bda4-5658-40fb-ab3d-5b2f69bb6c96','Persephone',570,120,520,520);
tile('1f862f8f-77f4-4536-86e0-c9fd934780cc','Famine',40,690,340,160,{text:'Demeter withholds grain.'});
tile('c08c1ac1-7461-4544-8471-59fe4d61f91b','Hermes',1280,690,340,160,{text:'Zeus commands the return.'});
addNode('persephone/year',c[4].id,{kind:'media',title:'A Divided Year',text:'One third below · Two thirds above',x:550,y:1180,width:560,height:160});
gallery('e463d153-80cf-4007-841d-70e4ebf40ac2','Demeter & Eleusis',1780,50,560,820);
media('1c08d8ee-e65e-4659-a78d-f6eb372fbcb7','Possible Demeter',1792,62,536,730);media('82838f32-d1ff-4a0e-9c23-ae8326ef66ce','Eleusinian Reconstruction',1792,62,536,730);
tile('f313a23b-fe20-4bc5-bbad-b7b6e74d356c','Myths in Relation',40,1450,340,160,{style:{backgroundDisplay:'hover'}});
edge('706a7e5a-ee06-4d05-b337-4e6209b01ee6',{sourceAnchor:{x:.5,y:1},targetAnchor:{x:.5,y:0}});
edge('18d5fcff-aabe-4482-a6ff-ea7cc1b51a28',{sourceAnchor:{x:1,y:.5},targetAnchor:{x:0,y:.2}});
edge('791e87a6-237f-4b58-a9f6-b0da31463aa8',{sourceAnchor:{x:0,y:.5},targetAnchor:{x:1,y:.2}});
pivot('546e6d08-62cd-4871-bff8-c28dc6678f18',210,1020);pivot('a0e7a71d-c58c-4ece-8434-b81c77520a1d',1450,1020);
pivot('45b22d95-c3b6-454a-9999-252be7854319',1210,1100);pivot('a5cd96cb-7402-47c7-bd09-cb319fabb2c6',1210,1260);
edge('3a2c0b60-d2a7-46fe-aae3-a91c61b8725f',{sourceAnchor:{x:.5,y:1}});edge('52b808eb-e7b1-40d8-a037-56895485694b');edge('104e8411-7e7c-4663-93ec-a8e06d00998e',{targetAnchor:{x:.5,y:1}});
edge('9da03cff-bb22-459d-9fa0-7cb5d63344f8',{sourceAnchor:{x:0,y:1},points:[{x:1210,y:930}]});edge('01deecd5-013f-44ad-9e7b-cee9b85c0a0d');edge('12a86360-8513-4967-b1f6-bf86bed15ddb',{target:'persephone/year',targetAnchor:{x:1,y:.5},label:'a divided year'});

// A single supporting Notes window per Work retains every original text and all original image/source records.
const noteSpecs=[['a629a20d-cff4-4dec-afed-28e3b00473f4',1590,1000],['6e59c9b6-6739-4d34-8820-cfc210ac333c',1920,1200],['614e01f9-e50b-487a-b8cd-1ff9ab94228e',1900,1320],['47a3b22e-81c0-42d3-b14d-9b5f48951809',2130,1350],['3150a6b9-2bd6-403e-a7d4-5c2b1219387d',1780,1250]];
const archives=[];
for(let i=0;i<c.length;i++){
 const work=c[i], [notesId,x,y]=noteSpecs[i], p=palettes[i];
 const originals=base.nodes.filter(n=>n.canvasId===work.id);
 const archiveText=`${titles[i]}\n\n`+originals.map(n=>`${n.title||n.tag||n.id}\n${n.text||''}\n\nOriginal record\n${JSON.stringify(n,null,2)}`).join('\n\n────────────────────────\n\n')+`\n\nOriginal relationships\n${JSON.stringify(base.links.filter(l=>l.canvasId===work.id),null,2)}`;
 const assetKey='archive/'+i;recipe.assets.push({key:assetKey,name:titles[i]+' Notes.txt',mime:'text/plain',base64:Buffer.from(archiveText).toString('base64')});
 const archived=originals.filter(n=>n.kind!=='canvas'&&n.id!==notesId&&!used.has(n.id)).sort((a,b)=>Number(b.kind==='document')-Number(a.kind==='document'));
 // Retain the original numeric tile and value without exposing a misleading interactive zero on the myth graph.
 for(const n of archived){const row=archived.indexOf(n)+1;edit(n.id,{parent:notesId,x:x+10,y:y+10+row*540,width:800,height:540,cell:{column:1,row:row+1},collapsed:false,style:{textAlign:'left',fontSize:22,titleSize:28,backgroundDisplay:'always',...(['junction','joint'].includes(n.kind)?{shape:'none',borderWidth:0}: {})}});}
 const rows=archived.length+1;
 edit(notesId,{title:'Notes',text:'',x,y,width:320,height:130,collapsed:true,containerVariant:'window',childLayout:'contained',childArrangement:'grid',containerSizes:{collapsed:{width:320,height:130,sizeMode:'fixed'},expanded:{width:820,height:560,sizeMode:'fixed'}},grid:{columns:1,rows,cellWidth:800,cellHeight:540,windowColumns:1,windowRows:1,scrollDirection:'vertical',showHeaders:false,gap:0,fillOrder:'row'},style:{fontSize:22,titleSize:30,childPadding:10}});
 addNode('archive-node/'+i,work.id,{parent:notesId,kind:'document',title:'Reading Notes',text:'',asset:assetKey,x:x+10,y:y+10,width:800,height:540,cell:{column:1,row:1},style:{fontSize:22,titleSize:28,textAlign:'left'}});
 archives.push({workId:work.id,notesId,assetKey,originalNodeCount:originals.length,archiveText});
}
// Any untouched original links retain identity and semantic fields while taking the unified palette.
for(const l of base.links)if(!recipe.links.some(e=>e.existingId===l.id))edge(l.id);
// These legacy snap contacts only bound reading/navigation chrome to presentation
// groups. They have no subject relationship and cannot remain contacts after the
// notes and art move into independent, accessible reading surfaces.
const removedContacts=['28cced08-813f-4b04-9be7-411cac73ef65','a50f1775-b28a-4bd5-8192-e756872cf95b','763b0b0d-ea44-4f4b-9042-73acdea96283','c6c3f6fe-a4e1-4cb4-99a3-f83b2f1a689a','2b43461d-babe-416f-9934-1755b4f19612','141380a6-617c-4d6d-8bc5-3548a6edf468','87b7ec6d-b4c6-420d-80cc-10310a84a8b0'];
recipe.links=recipe.links.filter(l=>!removedContacts.includes(l.existingId));recipe.remove={links:removedContacts};
recipe.nodes.unshift(...entries.values());
const {project,aliases}=applyRecipe(base,recipe);
const candidate=commitSnapshots(project);
// Commit updates the one cross-Work Reference source, preserving its original identity.
assert.deepEqual(validateProject(candidate),[]);
for(const n of base.nodes){const q=candidate.nodes.find(m=>m.id===n.id);assert(q,'Lost node '+n.id);for(const field of ['kind','canvasId','assetId','attachments','hyperlinks','remoteLinks','targetId','canvasTarget','targetCell','dependencyIds','operation','value','valueType','closed','exposed','rules','retiredBlockContent'])assert.deepEqual(q[field],n[field],`Preservation ${n.id}.${field}`);}
for(const a of base.assets)assert.deepEqual(candidate.assets.find(b=>b.id===a.id),a,'Asset bytes or identity changed');
for(const l of base.links){if(removedContacts.includes(l.id))continue;const q=candidate.links.find(m=>m.id===l.id);assert(q,'Lost link '+l.id);for(const f of ['kind','relation','direction','targetPort','snap'])assert.deepEqual(q[f],l[f],`Link ${l.id}.${f}`);assert.equal(q.sourceId,l.sourceId);if(l.id!=='12a86360-8513-4967-b1f6-bf86bed15ddb')assert.equal(q.targetId,l.targetId);}
for(const a of archives)for(const n of base.nodes.filter(n=>n.canvasId===a.workId))for(const f of ['title','text'])if(n[f])assert(a.archiveText.includes(n[f]),`Original ${f} missing in archive ${n.id}`);
await fs.mkdir(OUT,{recursive:true});
const write=(file,v)=>fs.writeFile(path.join(OUT,file),JSON.stringify(v,null,2)+'\n');
await write('recipe.json',recipe);await write('draft.json',candidate);await write('aliases.json',aliases);await write('inspection.json',inspectProject(candidate));
const geometry=candidate.canvases.map(w=>({id:w.id,title:w.title,...geometryDiagnostics(candidate,w.id)}));await write('geometry.json',geometry);
await write('preservation.json',{passed:true,projectId:base.id,originalRevision:base.revision,originalNodesRetained:base.nodes.length,originalLinksRetained:base.links.length-removedContacts.length,removedNonsemanticContacts:removedContacts,removedContactReason:'Legacy generic undirected snap contacts binding notes/navigation/art chrome to presentation groups; no subject relationships. Original complete records retained in Notes.',originalAssetsRetainedByteExact:base.assets.length,fullOriginalTextArchived:archives.map(({archiveText,...a})=>a),semanticRetarget:{linkId:'12a86360-8513-4967-b1f6-bf86bed15ddb',reason:'Original year-settlement subject was incorrectly functional Number value0; retained that original in Notes and display identical settlement as Media. Same visual relationship and original link ID retained.'},originalWorkTitles:base.canvases.map(w=>w.title),refinedWorkTitles:titles,sha256:createHash('sha256').update(JSON.stringify(candidate)).digest('hex')});
const [js,css]=await Promise.all(['viewer.js','scene.css'].map(f=>fs.readFile(ROOT+'/output/app/wwwroot/'+f,'utf8')));await fs.writeFile(OUT+'/publication.html',buildPublication(candidate,js,css));
console.log(JSON.stringify({out:OUT,works:titles,nodes:candidate.nodes.length,links:candidate.links.length,assets:candidate.assets.length,geometry:geometry.map(w=>({title:w.title,diagnostics:w.diagnostics.length,codes:w.diagnostics.map(d=>d.code)}))},null,2));
