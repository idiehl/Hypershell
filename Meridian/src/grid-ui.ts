import type { MeridianNode, MeridianProject, NodeKind } from './core/types.ts';
import { addNode } from './core/model.ts';
import { nodeTag, NODE_TAG_MAX_LENGTH } from './core/node-tag.ts';
import { transitionContainerSize } from './core/container-size.ts';
import { arrangeChildren, cellAddress, parseCellAddress, resolveGridCell, parseCsv, gridGeometry, isOrderedContainer, orderedPattern } from './core/grid.ts';
import { navigationIndex } from './core/navigation.ts';
import { CONTAINER_CHOICES } from './container-descriptions.ts';
import { isOrderedContainer as isOrdered } from './core/cells.ts';
import { containingFixedInterior } from './core/sizing.ts';

const el = <K extends keyof HTMLElementTagNameMap>(tag:K, text?:string) => {const e=document.createElement(tag);if(text!==undefined)e.textContent=text;return e;};
function action(text:string,run:()=>void){const b=el('button',text);b.type='button';b.className='secondary full';b.onclick=run;return b;}
function field(label:string,input:HTMLElement){const l=el('label');l.className='field';input.setAttribute('aria-label',label);l.append(el('span',label),input);return l;}
export interface GridActions { edit:(change:(p:MeridianProject)=>void)=>boolean|void; select:(id:string)=>void; fail:(error:unknown)=>void }

/** Grid metadata is ordinary node data: every control commits one undoable edit. */
export function gridInspector(project:MeridianProject,node:MeridianNode,actions:GridActions):HTMLElement {
 const section=el('details');section.className='grid-inspector';section.open=!!node.grid||!!node.cell||isOrderedContainer(node);section.append(el('summary',isOrderedContainer(node)?'Ordered contents':'Child arrangement'));
 const children=project.nodes.filter(n=>n.parentId===node.id);
 const tiled=isOrderedContainer(node),explicit=node.kind==='spreadsheet'||node.containerVariant==='spreadsheet'||node.containerVariant==='window';
 if(tiled)section.append(el('p',`${children.length} equal-sized tiles · coordinates X:Y. Change Display Pattern in Properties to choose a different layout.`));
 if(!tiled&&node.kind!=='canvas'&&node.containerVariant!=='gallery'){
  const manual=children.filter(child=>child.childPosition!=='auto').length;
  section.append(el('p',`Freeform begins at 3 o’clock, then alternates below and above around the parent in expanding rings. ${manual?`${manual} manually positioned ${manual===1?'child keeps its position':'children keep their positions'}.`:'Drag a child to keep a manual position.'}`),action('Reset to automatic rings',()=>actions.edit(p=>{for(const child of p.nodes)if(child.parentId===node.id)child.childPosition='auto';Object.assign(p,arrangeChildren(p,node.id,'radial').project);}))); 
 }
 if(node.grid){
  if(explicit)for(const [key,title] of [['columns','Columns'],['rows','Rows'],['cellWidth','Cell width'],['cellHeight','Cell height']] as const){const i=el('input');i.type='number';i.value=String(node.grid[key]);i.min=key==='columns'||key==='rows'?'1':'36';i.max=key==='columns'||key==='rows'?'512':'10000';i.onchange=()=>actions.edit(p=>{const n=p.nodes.find(n=>n.id===node.id)!;n.grid={...n.grid!,[key]:Number(i.value)};Object.assign(p,arrangeChildren(p,node.id,orderedPattern(n)??'grid').project);});section.append(field(title,i));}
  if(tiled){const order=el('select');order.append(new Option('Fill by row','row'),new Option('Fill by column','column'));order.value=node.grid.fillOrder??'row';order.onchange=()=>actions.edit(p=>{const n=p.nodes.find(n=>n.id===node.id)!;n.grid!.fillOrder=order.value as 'row'|'column';Object.assign(p,arrangeChildren(p,n.id,orderedPattern(n)??'grid').project);});section.append(field('Fill order',order));}
  if(node.containerVariant==='window'){for(const [key,title]of [['windowColumns','Window columns'],['windowRows','Window rows']]as const){const i=el('input');i.type='number';i.min='1';i.max='512';i.value=String(node.grid[key]??3);i.onchange=()=>actions.edit(p=>{const n=p.nodes.find(n=>n.id===node.id)!;n.grid![key]=Number(i.value);Object.assign(p,arrangeChildren(p,n.id,'grid').project);});section.append(field(title,i));}const direction=el('select');direction.append(new Option('Vertical','vertical'),new Option('Horizontal','horizontal'));direction.value=node.grid.scrollDirection??'vertical';direction.onchange=()=>actions.edit(p=>{p.nodes.find(n=>n.id===node.id)!.grid!.scrollDirection=direction.value as 'vertical'|'horizontal';});section.append(field('Scroll direction',direction),el('p','Scroll inside the window to browse. Shift-wheel scrolls the other axis; focus the window to use arrow keys or Page Up / Page Down.'));}
  section.append(action('Add a cell…',()=>showCellEditor(project,node.id,undefined,actions)),action('Embed a spreadsheet document…',()=>importSheet(project,node.id,undefined,actions)));
  section.append(el('p','Cells can contain text, numbers, attachments or another spreadsheet. Select a cell tile to edit its content.'));
 }
 if(node.parentId&&project.nodes.find(n=>n.id===node.parentId)?.grid){
  const address=el('input');address.value=node.cell?cellAddress(node.cell):'';address.placeholder='1:1';address.onchange=()=>actions.edit(p=>{const n=p.nodes.find(n=>n.id===node.id)!,cell=parseCellAddress(address.value);if(!cell)throw new Error('Enter a coordinate such as 1:1.');n.cell={...cell,...(n.cell?.name?{name:n.cell.name}:{})};Object.assign(p,arrangeChildren(p,n.parentId!,'grid').project);});section.append(field('Cell address',address));
  const name=el('input');name.value=node.cell?.name??'';name.placeholder='Optional, e.g. source_index';name.maxLength=64;name.onchange=()=>actions.edit(p=>{const n=p.nodes.find(n=>n.id===node.id)!;if(!n.cell)throw new Error('Assign a cell address first.');if(name.value.trim())n.cell.name=name.value.trim();else delete n.cell.name;});section.append(field('Cell name',name));
  const code=el('code',`${node.parentId}!${node.cell?.name||address.value}`);code.className='cell-reference-address';section.append(code);
 }
 if(children.some(n=>n.cell)){const list=el('div');list.className='cell-directory';for(const n of children.filter(n=>n.cell).slice(0,100)){const b=action(`${cellAddress(n.cell!)}${n.cell?.name?' · '+n.cell.name:''} — ${nodeTag(n)}`,()=>actions.select(n.id));b.className='quiet full';list.append(b);}section.append(list);}
 return section;
}
function emptyCell(project:MeridianProject,parent:MeridianNode){
 const grid=parent.grid!,columnFirst=grid.fillOrder==='column';for(let at=0;at<grid.columns*grid.rows;at++){const column=columnFirst?Math.floor(at/grid.rows)+1:at%grid.columns+1,row=columnFirst?at%grid.rows+1:Math.floor(at/grid.columns)+1,cell={column,row};if(!resolveGridCell(project,parent.id,cellAddress(cell)))return cell;}
 if(['grid','horizontal','vertical'].includes(parent.containerVariant??'')){const count=project.nodes.filter(node=>node.parentId===parent.id).length,side=Math.ceil(Math.sqrt(count+1)),columns=parent.containerVariant==='horizontal'?count+1:parent.containerVariant==='vertical'?1:side,rows=parent.containerVariant==='vertical'?count+1:parent.containerVariant==='horizontal'?1:side;return{column:columnFirst?Math.floor(count/rows)+1:count%columns+1,row:columnFirst?count%rows+1:Math.floor(count/columns)+1};}
 throw new Error('This spreadsheet is full. Increase its rows or columns first.');
}
export function showCellEditor(project:MeridianProject,parentId:string,address:string|undefined,actions:GridActions):void {
 try {
  const parent=project.nodes.find(n=>n.id===parentId);if(!parent?.grid)throw new Error('Enable a grid on the parent first.');
  const existing=address?resolveGridCell(project,parentId,address):undefined;if(existing){actions.select(existing.id);return;}
  const position=address?parseCellAddress(address):emptyCell(project,parent);if(!position)throw new Error('Invalid cell coordinate.');
  const d=el('dialog');d.className='modal grid-cell-modal';d.setAttribute('aria-label','Add grid cell');const body=el('form');body.className='modal-body';const tag=el('input');tag.value=`Cell ${cellAddress(position)}`;tag.required=true;tag.maxLength=NODE_TAG_MAX_LENGTH;const title=el('input');const coordinate=el('input');coordinate.value=cellAddress(position);coordinate.required=true;
  const kind=el('select');for(const [value,label]of [['media','Media'],['number','Number'],['container','Container']])kind.append(new Option(label,value));
  const pattern=el('select');for(const item of CONTAINER_CHOICES)pattern.append(new Option(item.label,item.value));pattern.value='contained';
  const content=el('select');content.append(new Option('Text','text'),new Option('Media or document','asset'),new Option('Hyperlink','link'));
  const text=el('textarea');text.rows=5;const textField=field('Text body',text),file=el('input');file.type='file';file.required=true;const fileField=field('Content file',file);
  const url=el('input');url.type='url';url.required=true;url.placeholder='https://…';const urlField=field('Hyperlink URL',url),linkLabel=el('input'),linkLabelField=field('Hyperlink label',linkLabel);
  const value=el('input');value.type='number';value.step='any';value.value='0';value.required=true;const valueField=field('Number value',value);
  const titleField=field('Title',title),contentField=field('Content',content);
  const sync=()=>{const empty=kind.value==='block',type=content.value;titleField.hidden=title.disabled=contentField.hidden=content.disabled=empty;textField.hidden=text.disabled=empty||type!=='text';fileField.hidden=file.disabled=empty||type!=='asset';urlField.hidden=url.disabled=linkLabelField.hidden=linkLabel.disabled=empty||type!=='link';valueField.hidden=value.disabled=kind.value!=='number';};content.onchange=sync;kind.onchange=()=>{pattern.value=kind.value==='container'?'spreadsheet':'contained';sync();};sync();
  const footer=el('div');footer.className='modal-footer';const cancel=action('Cancel',()=>d.close());let pending=false;
  const save=action('Add cell',()=>{void(async()=>{if(pending||!body.reportValidity())return;pending=true;save.disabled=true;try{
   const target=parseCellAddress(coordinate.value);if(!target)throw new Error('Enter a coordinate such as 1:1.');
   const selectedKind=kind.value,selectedPattern=pattern.value as NonNullable<MeridianNode['containerVariant']>,selectedContent=content.value as 'text'|'asset'|'link',selectedText=text.value,selectedUrl=url.value.trim(),selectedLabel=linkLabel.value.trim(),selectedTitle=selectedKind==='block'?'':title.value,selectedTag=tag.value.trim(),selectedValue=Number(value.value);
   if(!selectedTag)throw new Error('Give this node a Tag.');
   let asset:{name:string;mime:string;base64:string}|undefined;const f=selectedKind!=='block'&&selectedContent==='asset'?file.files?.[0]:undefined;if(selectedKind!=='block'&&selectedContent==='asset'&&!f)throw new Error('Choose one content file.');if(f){if(f.size>16*1024*1024)throw new Error('Attachments must be 16 MiB or smaller.');asset={name:f.name,mime:f.type||'application/octet-stream',base64:encodeBytes(new Uint8Array(await f.arrayBuffer()))};}
   if(!d.open)return;let id='';const accepted=actions.edit(p=>{
    const owner=p.nodes.find(n=>n.id===parentId);if(!owner?.grid)throw new Error('The parent grid is no longer available.');if(resolveGridCell(p,parentId,cellAddress(target)))throw new Error('That cell is occupied.');
    const n=addNode(p,owner.canvasId,selectedKind==='container'&&selectedPattern==='spreadsheet'?'spreadsheet':selectedKind as NodeKind,selectedTitle);id=n.id;Object.assign(n,{parentId,cell:target,placement:'free',childPosition:'auto',contentType:selectedContent,containerVariant:selectedPattern,childLayout:selectedKind==='container'?'contained':'spatial',childArrangement:selectedPattern==='horizontal'?'row':selectedPattern==='vertical'?'column':['grid','spreadsheet','window'].includes(selectedPattern)?'grid':selectedPattern==='gallery'?'free':'radial'});
    n.tag=selectedTag;
    if(selectedKind!=='block'&&selectedContent==='text')n.text=selectedText;else delete n.text;if(asset){const a={id:crypto.randomUUID(),...asset};p.assets.push(a);n.assetId=a.id;}if(selectedKind!=='block'&&selectedContent==='link')n.hyperlinks=[{url:selectedUrl,...(selectedLabel?{label:selectedLabel}:{})}];
    if(n.kind==='number'){if(!Number.isFinite(selectedValue))throw new Error('Enter a finite number for this cell.');n.value=selectedValue;}
    if(isOrdered(n))n.grid??={columns:3,rows:3,cellWidth:200,cellHeight:100,gap:0,showHeaders:false,fillOrder:'row',...(selectedPattern==='window'?{windowColumns:3,windowRows:3,scrollDirection:'vertical' as const}:{})};
    transitionContainerSize(owner,true,undefined,{preserveCurrentRecord:!!containingFixedInterior(p,owner)});n.collapsed=true;n.width=owner.grid.cellWidth;n.height=owner.grid.cellHeight;n.style={fontSize:13,titleSize:14};Object.assign(p,arrangeChildren(p,parentId,'grid').project);
   });if(accepted===false)return;d.close();actions.select(id);
  }catch(e){actions.fail(e);}finally{pending=false;save.disabled=false;}})();});
  body.onsubmit=event=>{event.preventDefault();save.click();};body.append(field('Address',coordinate),field('Type',kind),field('Tag',tag),field('Display Pattern',pattern),titleField,contentField,textField,fileField,urlField,linkLabelField,valueField,el('p','Tag names the node. Title and content are optional Work content.'));footer.append(cancel,save);d.append(el('h2',`Add cell in ${nodeTag(parent)}`),body,footer);document.body.append(d);d.addEventListener('close',()=>d.remove(),{once:true});d.showModal();tag.focus();
 }catch(e){actions.fail(e);}
}
function encodeBytes(bytes:Uint8Array){let binary='';for(let at=0;at<bytes.length;at+=32768)binary+=String.fromCharCode(...bytes.subarray(at,at+32768));return btoa(binary);}
function importSheet(project:MeridianProject,parentId:string,address:string|undefined,actions:GridActions){const input=el('input');input.type='file';input.accept='.csv,.tsv,.xlsx,.xls,.ods';input.onchange=()=>{void(async()=>{try{const file=input.files?.[0],parent=project.nodes.find(n=>n.id===parentId);if(!file||!parent?.grid)return;if(file.size>16*1024*1024)throw new Error('Attachments must be 16 MiB or smaller.');const bytes=new Uint8Array(await file.arrayBuffer()),csv=/\.(csv|tsv)$/i.test(file.name);const data=csv?parseCsv(new TextDecoder().decode(bytes),/\.tsv$/i.test(file.name)?'\t':','):undefined;if(data&&data.reduce((n,r)=>n+r.length,0)>2000)throw new Error('Import up to 2,000 cells at a time, or attach the original file.');let id='';const accepted=actions.edit(p=>{const current=p.nodes.find(n=>n.id===parentId)!;const cell=address?parseCellAddress(address):emptyCell(p,current);if(!cell)throw new Error('Invalid cell.');if(resolveGridCell(p,parentId,cellAddress(cell)))throw new Error('That cell is occupied.');const a={id:crypto.randomUUID(),name:file.name,mime:csv?'text/csv':file.type||'application/octet-stream',base64:encodeBytes(bytes)};p.assets.push(a);const sheet=addNode(p,current.canvasId,data?'spreadsheet':'media',file.name);id=sheet.id;sheet.parentId=parentId;sheet.cell=cell;sheet.assetId=a.id;sheet.contentType='asset';delete sheet.text;sheet.childPosition='auto';sheet.collapsed=true;sheet.placement='free';if(data){sheet.grid={columns:Math.max(1,...data.map(r=>r.length)),rows:Math.max(1,data.length),cellWidth:220,cellHeight:95,gap:0,showHeaders:true};for(const [ri,row] of data.entries())for(const [ci,value] of row.entries()){const n=addNode(p,current.canvasId,'media',`${cellAddress({row:ri+1,column:ci+1})}`);n.parentId=sheet.id;n.cell={row:ri+1,column:ci+1};n.text=value;n.contentType='text';n.childPosition='auto';n.width=220;n.height=95;n.placement='free';n.style={fontSize:13,titleSize:11};}Object.assign(p,arrangeChildren(p,sheet.id,'grid').project);}else{sheet.width=220;sheet.height=150;}current.collapsed=false;Object.assign(p,arrangeChildren(p,parentId,'grid').project);});if(accepted!==false)actions.select(id);}catch(e){actions.fail(e);}})();};input.click();}
