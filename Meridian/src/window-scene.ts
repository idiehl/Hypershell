import type { MeridianNode, MeridianProject, Point } from './core/types.ts';
import { gridGeometry, isEnclosingContainer } from './core/cells.ts';
import { navigationIndex } from './core/navigation.ts';

export type WindowOffsets = Map<string, Point>;
export function windowParents(index:ReturnType<typeof navigationIndex>,id:string):MeridianNode[]{
  const result:MeridianNode[]=[];
  for(const parent of index.ancestors(id)){
    if(parent.containerVariant==='window'&&!parent.collapsed&&parent.grid)result.push(parent);
    // A tray is an exterior presentation scope. Its own Window may scroll,
    // while Windows around the source tile cannot clip or translate its branch.
    if(parent.expansionTray&&!isEnclosingContainer(parent))break;
  }
  return result;
}
export function windowOffset(index:ReturnType<typeof navigationIndex>,id:string,offsets:WindowOffsets):Point{
  return windowParents(index,id).reduce((total,parent)=>{const at=offsets.get(parent.id);return{x:total.x+(at?.x??0),y:total.y+(at?.y??0)};},{x:0,y:0});
}
/** Displayed canvas coordinates account for each enclosing scrolling Window;
 * authored positions and the transient view offsets remain untouched. */
export function displayNodeBounds(index:ReturnType<typeof navigationIndex>,node:MeridianNode,offsets:WindowOffsets){
  const bounds=index.bounds(node),offset=windowOffset(index,node.id,offsets);
  return {...bounds,x:bounds.x-offset.x,y:bounds.y-offset.y};
}
/** A path owned by the innermost shared Window uses that Window's content
 * coordinates, including every outer Window and its own scroll position. */
export function routeWindowOffset(index:ReturnType<typeof navigationIndex>,sourceId:string,targetId:string,offsets:WindowOffsets):Point{
  const targets=new Set(windowParents(index,targetId).map(parent=>parent.id));
  const owner=windowParents(index,sourceId).find(parent=>targets.has(parent.id));
  if(!owner)return{x:0,y:0};
  const outer=windowOffset(index,owner.id,offsets),own=offsets.get(owner.id);
  return{x:outer.x+(own?.x??0),y:outer.y+(own?.y??0)};
}
export function windowSurface(parent:MeridianNode,project:MeridianProject){
  const geometry=gridGeometry(parent,project),columns=Math.min(geometry.columns,parent.grid?.windowColumns??3),rows=Math.min(geometry.rows,parent.grid?.windowRows??3);
  return {geometry,columns,rows,width:columns*geometry.cellWidth,height:rows*geometry.cellHeight,fullWidth:geometry.columns*geometry.cellWidth,fullHeight:geometry.rows*geometry.cellHeight};
}
/** View offsets are transient; coordinates and authored graph never change while browsing. */
export function prepareWindowOffsets(project:MeridianProject,index:ReturnType<typeof navigationIndex>,offsets:WindowOffsets,focusId?:string|null):void{
  for(const parent of project.nodes.filter(node=>node.containerVariant==='window'&&node.grid)){
    const surface=windowSurface(parent,project),prior=offsets.get(parent.id)??{x:0,y:0};
    offsets.set(parent.id,{x:Math.max(0,Math.min(prior.x,surface.fullWidth-surface.width)),y:Math.max(0,Math.min(prior.y,surface.fullHeight-surface.height))});
  }
  if(!focusId)return;
  const revealFocus=(id:string)=>{
  let child=index.nodes.get(id);
  for(const parent of index.ancestors(id)){
    if(parent.containerVariant==='window'&&parent.grid&&child?.cell){
      const {geometry,width,height}=windowSurface(parent,project),at=offsets.get(parent.id)??{x:0,y:0};
      const x=(child.cell.column-1)*geometry.cellWidth,y=(child.cell.row-1)*geometry.cellHeight;
      if(x<at.x)at.x=x;else if(x+geometry.cellWidth>at.x+width)at.x=x+geometry.cellWidth-width;
      if(y<at.y)at.y=y;else if(y+geometry.cellHeight>at.y+height)at.y=y+geometry.cellHeight-height;
      offsets.set(parent.id,at);
    }child=parent;
    if(parent.expansionTray&&!isEnclosingContainer(parent)){revealFocus(parent.id);break;}
  }
  };revealFocus(focusId);
}

/** Move the existing scene tiles into a clipped scrolling surface without duplicating controls. */
export function mountWindowSurfaces(project:MeridianProject,index:ReturnType<typeof navigationIndex>,cards:Map<string,HTMLElement>,offsets:WindowOffsets,changed:(id:string,at:Point)=>void,scale:()=>number):void{
  const windows=project.nodes.filter(node=>node.containerVariant==='window'&&node.grid&&!node.collapsed&&cards.has(node.id)).sort((a,b)=>index.ancestors(a.id).length-index.ancestors(b.id).length);
  const surfaces=new Map<string,{viewport:HTMLElement;content:HTMLElement;left:number;top:number}>();
  for(const parent of windows){
    const card=cards.get(parent.id)!,{geometry,width,height,fullWidth,fullHeight}=windowSurface(parent,project);
    const viewport=document.createElement('div');viewport.className='ms-window-viewport';viewport.tabIndex=0;viewport.dataset.windowId=parent.id;
    viewport.setAttribute('role','region');viewport.setAttribute('aria-label',`${parent.title} scrolling window`);viewport.title=`Scroll ${parent.grid!.scrollDirection??'vertical'}ly to browse. Shift-wheel scrolls the other direction; arrow keys also scroll.`;
    const enclosing = isEnclosingContainer(parent);
    Object.assign(viewport.style,{left:`${geometry.left-(enclosing?parent.x:0)}px`,top:`${geometry.top-(enclosing?parent.y:0)}px`,width:`${width}px`,height:`${height}px`});
    const content=document.createElement('div');content.className='ms-window-content';Object.assign(content.style,{width:`${fullWidth}px`,height:`${fullHeight}px`});viewport.append(content);(enclosing ? card : card.parentElement!).append(viewport);
    if(!enclosing)viewport.dataset.externalWindow=parent.id;
    surfaces.set(parent.id,{viewport,content,left:geometry.left,top:geometry.top});
    viewport.addEventListener('scroll',()=>{const at={x:viewport.scrollLeft,y:viewport.scrollTop};offsets.set(parent.id,at);changed(parent.id,at);});
    viewport.addEventListener('wheel',event=>{
      if(event.ctrlKey||event.metaKey)return;
      const target=event.target as Element;if(target.closest('textarea,input,select'))return;
      const text=target.closest<HTMLElement>('.ms-text,.ms-text-preview');if(text&&text.scrollHeight>text.clientHeight+1)return;
      event.preventDefault();event.stopPropagation();
      const unit=event.deltaMode===1?16:event.deltaMode===2?height:1,amount=(event.deltaY||event.deltaX)*unit/Math.max(.15,scale());
      const horizontal=(parent.grid!.scrollDirection==='horizontal')!==event.shiftKey;
      if(horizontal)viewport.scrollLeft+=amount;else viewport.scrollTop+=amount;
    },{passive:false});
    viewport.addEventListener('keydown',event=>{
      if(event.target!==viewport||event.ctrlKey||event.metaKey||event.altKey)return;
      const stepX=geometry.cellWidth,stepY=geometry.cellHeight;
      if(event.key==='ArrowLeft')viewport.scrollLeft-=stepX;else if(event.key==='ArrowRight')viewport.scrollLeft+=stepX;
      else if(event.key==='ArrowUp')viewport.scrollTop-=stepY;else if(event.key==='ArrowDown')viewport.scrollTop+=stepY;
      else if(event.key==='PageDown'||event.key==='PageUp'){const sign=event.key==='PageDown'?1:-1;if(parent.grid!.scrollDirection==='horizontal')viewport.scrollLeft+=width*sign;else viewport.scrollTop+=height*sign;}
      else if(event.key==='Home'){viewport.scrollLeft=0;viewport.scrollTop=0;}else if(event.key==='End'){viewport.scrollLeft=fullWidth;viewport.scrollTop=fullHeight;}else return;
      event.preventDefault();event.stopPropagation();
    });
    // Blank cells belong to the parent tile: its shared pointer gesture handles
    // selection, disclosure, creation and dragging just like any other node.
  }
  for(const [id,card]of cards){
    const parent=windowParents(index,id)[0],surface=parent&&surfaces.get(parent.id);if(!surface)continue;
    const node=index.nodes.get(id)!,bounds=index.bounds(node);Object.assign(card.style,{left:`${bounds.x-surface.left}px`,top:`${bounds.y-surface.top}px`});surface.content.append(card);
  }
  // An exterior window inside another window shares the enclosing viewport's
  // coordinate system rather than escaping its clip when its parent tile moves.
  for(const parent of windows.filter(node=>!isEnclosingContainer(node))){
    const own=surfaces.get(parent.id)!,outer=windowParents(index,parent.id)[0],surface=outer&&surfaces.get(outer.id);
    if(surface){Object.assign(own.viewport.style,{left:`${own.left-surface.left}px`,top:`${own.top-surface.top}px`});surface.content.append(own.viewport);}
  }
  for(const [id,{viewport}]of surfaces){const at=offsets.get(id);viewport.scrollLeft=at?.x??0;viewport.scrollTop=at?.y??0;}
}
