import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {cloneProject,addNode,commitSnapshots} from '../src/core/model.ts';
import {navigationIndex} from '../src/core/navigation.ts';
import {arrangeChildren,cellAddress} from '../src/core/grid.ts';
import {validateProject} from '../src/core/rules.ts';

// Read-only preparation. The separately reviewed candidate is committed later
// through the host's revision AND recovery-token guard, never by file overwrite.
const id='3d896c72-36b9-4900-adb6-a68934591233',nodeId='a16146fa-9a05-4949-af87-926dfef9c9df';
const origin='http://127.0.0.1:5240';const out='output/grid-qa';await mkdir(out,{recursive:true});
const savedResponse=await fetch(`${origin}/api/projects/${id}`),saved=await savedResponse.json();
const recoveryResponse=await fetch(`${origin}/api/projects/${id}/recovery`),recovery=recoveryResponse.status===404?null:await recoveryResponse.json();
if(!savedResponse.ok||(!recoveryResponse.ok&&recoveryResponse.status!==404))throw new Error('Cannot inspect saved canvas and recovery.');
if(recovery&&recovery.baseRevision!==saved.revision)throw new Error('Recovery has an older baseline; review it before composing.');
const original=cloneProject(recovery?.project??saved),p=cloneProject(original),nav=navigationIndex(original),parent=p.nodes.find(n=>n.id===nodeId);if(!parent)throw new Error('Requested node is absent.');
const packets=nav.children.get(nodeId)??[],assets=new Map(original.assets.map(a=>[a.id,a]));
const textPacket=node=>{const branch=[node,...nav.descendants(node.id)];const ids=new Set(branch.map(n=>n.id));return `# ${node.title}\n\nPreserved from the existing Meridian canvas. Original node: ${node.id}.\n\n`+branch.map(n=>`## ${n.title||n.kind}\n\n${n.text??''}${n.targetId?`\n\nReference: ${n.targetId}${n.targetCell?'!'+n.targetCell:''}`:''}${n.assetId?`\n\nAttachment: ${assets.get(n.assetId)?.name??n.assetId} (${n.assetId})`:''}\n\nNode: ${n.id}\n`).join('\n')+'\n## Preserved relationships\n\n'+original.links.filter(l=>ids.has(l.sourceId)||ids.has(l.targetId)).map(l=>`- ${nav.nodes.get(l.sourceId)?.title??l.sourceId} → ${nav.nodes.get(l.targetId)?.title??l.targetId}: ${l.label||l.relation} (${l.id})`).join('\n');};
const attach=(name,text)=>{const asset={id:crypto.randomUUID(),name,mime:'text/markdown',base64:Buffer.from(text,'utf8').toString('base64')};p.assets.push(asset);return asset.id;};
const complete=textPacket(nav.nodes.get(nodeId));parent.assetId=attach('Greek-mythology-source-packets.md',complete);
parent.kind='spreadsheet';parent.childLayout='contained';parent.childArrangement='grid';parent.placement='free';parent.collapsed=true;parent.text='An addressed source library. Expand, then zoom into a packet; open its document for the full text. Earlier appearances and original child nodes are preserved.';
parent.grid={columns:8,rows:Math.ceil((packets.length+1)/8),cellWidth:280,cellHeight:230,gap:12,showHeaders:true};parent.width=8*292+54;parent.height=parent.grid.rows*242+90;parent.style={...parent.style,titleSize:23,fontSize:15};
// Place the expanded source register away from the active person branches.
parent.x=2600;parent.y=500;
const guide=addNode(p,parent.canvasId,'document','Read this source library');guide.parentId=nodeId;guide.width=280;guide.height=230;guide.cell={column:1,row:1,name:'source_index'};guide.placement='free';guide.text='Open the complete source document here. Each packet also has its own document and original child nodes.';guide.assetId=parent.assetId;guide.style={fill:'#D9C298',textColor:'#263C3D',titleSize:18,fontSize:14};
const changedKinds=[];
for(const [at,old] of packets.entries()){const node=p.nodes.find(n=>n.id===old.id),number=at+1;node.cell={column:number%8+1,row:Math.floor(number/8)+1,name:`packet_${String(at+1).padStart(3,'0')}`};node.placement='free';node.width=280;node.height=node.kind==='container'?210:180;node.style={...node.style,titleSize:15,fontSize:12};if(node.kind==='container'){node.kind='document';node.assetId=attach(`source-packet-${String(at+1).padStart(3,'0')}.md`,textPacket(old));node.text=`Source document · ${nav.descendants(old.id).length} original nodes preserved.`;node.collapsed=true;node.childLayout='spatial';changedKinds.push(node.id);} }
let candidate=arrangeChildren(p,nodeId,'grid').project;candidate=commitSnapshots(candidate);const issues=validateProject(candidate);if(issues.length)throw new Error(issues.map(i=>i.message).join('\n'));
if(original.nodes.some(n=>!candidate.nodes.some(x=>x.id===n.id))||original.links.some(l=>!candidate.links.some(x=>x.id===l.id)))throw new Error('Original identities were removed.');
const manifest={id,nodeId,expectedRevision:saved.revision,expectedRecoverySavedAt:recovery?.savedAt??null,checkpointName:'Before source grid and document packets',originalNodes:original.nodes.length,nodes:candidate.nodes.length,originalAssets:original.assets.length,assets:candidate.assets.length,sourceCells:packets.length+1,documentPackets:changedKinds.length,grid:parent.grid,sourceAttachmentBytes:Buffer.byteLength(complete),preparedAt:new Date().toISOString()};
await writeFile(`${out}/source-grid-candidate.json`,JSON.stringify(candidate));await writeFile(`${out}/source-grid-manifest.json`,JSON.stringify(manifest,null,2));await writeFile(`${out}/source-grid-before.json`,JSON.stringify(original));console.log(JSON.stringify(manifest,null,2));
