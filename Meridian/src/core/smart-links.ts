import type { MeridianNode, MeridianProject, Point, SmartLinkDirection, SmartLinkSettings } from './types.ts';
import { addNode } from './model.ts';
import { navigationIndex } from './navigation.ts';
import type { SizingContext } from './sizing.ts';
import { gridGeometry, isEnclosingContainer } from './cells.ts';
import { DEFAULT_SMART_LINK_GAP, isSmartLink, isSmartLinkSettings } from './smart-link-state.ts';
export { DEFAULT_SMART_LINK_GAP, isSmartLink, isSmartLinkSettings } from './smart-link-state.ts';

type Cardinal = Exclude<SmartLinkDirection, 'auto'>;
interface Box extends Point { width: number; height: number }
interface IndexedBox extends Box { id: string }
export interface SmartLinkGeometry {
  /** The handle is the center attachment; both signed halves remain usable. */
  origin: Point; start: Point; end: Point; direction: Cardinal; memberIds: string[];
  segments: Array<{ from: Point; to: Point; nodeId?: string }>;
  /** A rail handle placed inside unrelated content has no valid visible exit. */
  blockedNodeIds?: string[];
}
export interface SmartLinkReflowResult {
  movedNodeIds: string[]; railIds: string[]; obstacleChecks: number; pinnedNodeIds: string[];
}
const finite = (value: number) => Number.isFinite(value) && Math.abs(value) <= 1e9;
const translated = (box: Box, by: Point): Box => ({ ...box, x: box.x + by.x, y: box.y + by.y });
const intersects = (a: Box, b: Box) => a.x < b.x + b.width - 1e-6 && b.x < a.x + a.width - 1e-6 && a.y < b.y + b.height - 1e-6 && b.y < a.y + a.height - 1e-6;
const union = (a: Box, b: Box): Box => { const x = Math.min(a.x,b.x), y = Math.min(a.y,b.y); return { x,y,width:Math.max(a.x+a.width,b.x+b.width)-x,height:Math.max(a.y+a.height,b.y+b.height)-y }; };
const clipBox=(box:Box,clip:Box|null|undefined):Box|null=>{if(clip===null)return null;if(!clip)return box;const x=Math.max(box.x,clip.x),y=Math.max(box.y,clip.y),right=Math.min(box.x+box.width,clip.x+clip.width),bottom=Math.min(box.y+box.height,clip.y+clip.height);return right>x&&bottom>y?{x,y,width:right-x,height:bottom-y}:null;};
/** Presentation caps are distinct from logical disclosure: nested rails still
 * lay out inside an enclosure, while its hidden cells never obstruct the Work. */
function presentationIndex(project:MeridianProject,nav:ReturnType<typeof navigationIndex>){
 const ownBoxes=new Map<string,Box>(),caps=new Map<string,Box>(),incoming=new Map<string,Box|null|undefined>(),childClips=new Map<string,Box|null|undefined>(),covers=new Map<string,string|undefined>();
 const ownsSurface=(node:MeridianNode)=>isEnclosingContainer(node)||node.containerVariant==='window'||node.containerVariant==='gallery';
 const hasCaps=project.nodes.some(ownsSurface);
 const own=(node:MeridianNode):Box=>{let box=ownBoxes.get(node.id);if(!box){const b=nav.bounds(node);box={x:b.x,y:b.y,width:b.width,height:b.height};ownBoxes.set(node.id,box);}return box;};
 const footprint=(node:MeridianNode):Box=>{if(!ownsSurface(node))return own(node);let box=caps.get(node.id);if(!box){box=!isEnclosingContainer(node)&&['window','gallery'].includes(node.containerVariant??'')?nav.branchBounds(node):own(node);caps.set(node.id,box);}return box;};
 const prepare=(node:MeridianNode)=>{const chain:MeridianNode[]=[],seen=new Set<string>();let cursor:MeridianNode|undefined=node;while(cursor&&!incoming.has(cursor.id)&&!seen.has(cursor.id)){chain.push(cursor);seen.add(cursor.id);cursor=nav.nodes.get(cursor.parentId??'');}for(const item of chain.reverse()){const parent=nav.nodes.get(item.parentId??''),before=parent?childClips.get(parent.id):undefined;incoming.set(item.id,before);covers.set(item.id,parent?(ownsSurface(parent)?parent.id:covers.get(parent.id)):undefined);let after=before;if(isEnclosingContainer(item))after=clipBox(own(item),after);if(item.containerVariant==='window'&&item.grid&&!item.collapsed){const grid=gridGeometry(item,project);after=clipBox({x:grid.left,y:grid.top,width:grid.visibleColumns*grid.cellWidth,height:grid.visibleRows*grid.cellHeight},after);}childClips.set(item.id,after);}};
 return{own,footprint,capped:ownsSurface,cover(node:MeridianNode){if(!hasCaps)return;prepare(node);return covers.get(node.id);},clip(node:MeridianNode,box:Box=footprint(node)){if(!hasCaps)return box;prepare(node);return clipBox(box,incoming.get(node.id));}};
}
const orientation = (direction: Cardinal) => direction === 'down' ? { u:{x:0,y:1},v:{x:1,y:0} }
  : direction === 'up' ? { u:{x:0,y:-1},v:{x:-1,y:0} }
  : direction === 'left' ? { u:{x:-1,y:0},v:{x:0,y:-1} } : { u:{x:1,y:0},v:{x:0,y:1} };
const projection = (box: Box, vector: Point): [number,number] => {
  const first=box.x*vector.x+box.y*vector.y, second=(box.x+box.width)*vector.x+(box.y+box.height)*vector.y;
  return [Math.min(first,second),Math.max(first,second)];
};

/** Editing-clone operation. It adds a normal Utility identity, so all existing
 * branch copies, Modules, native exports and publications carry its membership. */
export function createSmartLink(project: MeridianProject, canvasId: string, at: Point,
  options: { parentId?: string; direction?: SmartLinkDirection; gap?: number } = {}): MeridianNode {
  if (![at.x,at.y].every(finite)) throw new Error('Smart Link coordinates must be finite.');
  const settings: SmartLinkSettings = { direction:options.direction ?? 'auto',gap:options.gap ?? DEFAULT_SMART_LINK_GAP };
  if (!isSmartLinkSettings(settings)) throw new Error('Choose a valid Smart Link direction and gap from 8 to 10000.');
  const parent = options.parentId ? project.nodes.find(node=>node.id===options.parentId && node.canvasId===canvasId) : undefined;
  if (options.parentId && !parent) throw new Error('A Smart Link needs a parent in its own Work.');
  const node=addNode(project,canvasId,'junction','Smart Link');
  Object.assign(node,{x:at.x-6,y:at.y-6,width:12,height:12,sizeMode:'fixed',utilityVariant:'joint',smartLink:settings,collapsed:false,childLayout:'spatial',childArrangement:'free',childPosition:'manual',placement:'free',showTitle:false,style:{shape:'circle',padding:0}});
  if(parent)node.parentId=parent.id;
  return node;
}
export function configureSmartLink(project: MeridianProject,id:string,patch:Partial<Pick<SmartLinkSettings,'direction'|'gap'>>):void {
  const node=project.nodes.find(node=>node.id===id); if(!isSmartLink(node))throw new Error('Choose a Smart Link.');
  if(Object.keys(patch).some(key=>!['direction','gap'].includes(key)))throw new Error('Only direction and gap may be configured.');
  const settings={...node.smartLink,...patch}; if(patch.direction!==undefined)delete settings.resolvedDirection;
  if(!isSmartLinkSettings(settings))throw new Error('Choose a valid Smart Link direction and gap from 8 to 10000.');
  node.smartLink=settings;
}
export function smartLinkMembers(project:MeridianProject,id:string):MeridianNode[] {
  return project.nodes.filter(node=>node.parentId===id);
}
export function addSmartLinkMember(project:MeridianProject,railId:string,nodeId:string,index?:number):void {
  const byId=new Map(project.nodes.map(node=>[node.id,node])),rail=byId.get(railId),node=byId.get(nodeId);
  if(!isSmartLink(rail)||!node||node.kind==='canvas'||node.canvasId!==rail.canvasId||node.id===rail.id)throw new Error('Choose a Smart Link and a node in the same Work.');
  if(node.cell)throw new Error('Move an addressed cell out of its table explicitly before adding it to a Smart Link.');
  for(let cursor:MeridianNode|undefined=rail,seen=new Set<string>();cursor&&!seen.has(cursor.id);cursor=byId.get(cursor.parentId??'')){if(cursor.id===node.id)throw new Error('A Smart Link cannot be added inside its own descendant.');seen.add(cursor.id);}
  const siblings=smartLinkMembers(project,railId).filter(member=>member.id!==node.id);
  if(index!==undefined&&(!Number.isInteger(index)||index<0||index>siblings.length))throw new Error('Choose a valid Smart Link insertion position.');
  node.parentId=rail.id;node.childPosition='manual';node.placement='free';
  if(index!==undefined){project.nodes.splice(project.nodes.indexOf(node),1);const before=siblings[index];project.nodes.splice(before?project.nodes.indexOf(before):project.nodes.length,0,node);}
}
export function detachSmartLinkMember(project:MeridianProject,nodeId:string):void {
  const byId=new Map(project.nodes.map(node=>[node.id,node])),node=byId.get(nodeId),rail=byId.get(node?.parentId??'');
  if(!node||!isSmartLink(rail))throw new Error('Choose a Smart Link member.');
  node.parentId=rail.parentId;node.childPosition='manual';node.placement='free';
}

/** Dynamic broad phase with bounded cells. Huge branches use an overflow list,
 * rather than allocating millions of grid keys. Entries are actual visible
 * tiles; empty space between independent rails does not become an obstacle. */
class ObstacleIndex {
  private boxes=new Map<string,IndexedBox>(); private cells=new Map<string,Set<string>>(); private large=new Set<string>();
  private keys(box:Box):string[]|undefined {const a=Math.floor(box.x/512),b=Math.floor((box.x+box.width)/512),c=Math.floor(box.y/512),d=Math.floor((box.y+box.height)/512);if((b-a+1)*(d-c+1)>512)return;const keys:string[]=[];for(let x=a;x<=b;x++)for(let y=c;y<=d;y++)keys.push(`${x}:${y}`);return keys;}
  insert(box:IndexedBox){this.boxes.set(box.id,box);const keys=this.keys(box);if(!keys){this.large.add(box.id);return;}for(const key of keys){const set=this.cells.get(key)??new Set<string>();set.add(box.id);this.cells.set(key,set);}}
  query(region:Box):IndexedBox[]{const keys=this.keys(region);const ids=keys?new Set([...this.large,...keys.flatMap(key=>[...this.cells.get(key)??[]])]):new Set(this.boxes.keys());return [...ids].flatMap(id=>{const box=this.boxes.get(id)!;return intersects(region,box)?[box]:[];});}
}

/** Reflow only opted-in rails. Positions are applied once after the bottom-up
 * plan, and internal authored route bends receive exactly the same translation
 * as their endpoints. No whole-project clone or historical-layout migration. */
export function reflowSmartLinks(project:MeridianProject,canvasId?:string,options:{context?:SizingContext}={}):SmartLinkReflowResult {
  const rails=project.nodes.filter((node):node is MeridianNode & {smartLink:SmartLinkSettings}=>isSmartLink(node)&&(!canvasId||node.canvasId===canvasId));
  const result:SmartLinkReflowResult={movedNodeIds:[],railIds:rails.map(node=>node.id),obstacleChecks:0,pinnedNodeIds:[]};if(!rails.length)return result;
  const nav=navigationIndex(project,options.context),byId=nav.nodes,children=nav.children,presentation=presentationIndex(project,nav);
  const workIds=new Set(rails.map(node=>node.canvasId));
  const ordered:MeridianNode[]=[],visible=new Set<string>(),managed=new Set<string>(),topRail=new Map<string,string>(),tin=new Map<string,number>(),tout=new Map<string,number>();
  const traverse=(root:MeridianNode)=>{const pending:Array<{node:MeridianNode;visible:boolean;owner?:string;exit?:boolean}>=[{node:root,visible:true}];while(pending.length){const entry=pending.pop()!,node=entry.node;if(entry.exit){tout.set(node.id,ordered.length);continue;}if(tin.has(node.id))continue;tin.set(node.id,ordered.length);ordered.push(node);if(entry.visible)visible.add(node.id);const owner=entry.owner??(isSmartLink(node)?node.id:undefined);if(owner){managed.add(node.id);topRail.set(node.id,owner);}pending.push({...entry,exit:true});const shown=entry.visible&&!node.collapsed&&node.containerVariant!=='gallery';for(const child of [...children.get(node.id)??[]].reverse())pending.push({node:child,visible:shown,owner});}};
  for(const canvas of project.canvases)if(workIds.has(canvas.id)){const root=byId.get(canvas.rootId);if(root)traverse(root);}
  const belongs=(id:string,root:string)=>tin.has(id)&&tin.has(root)&&tin.get(id)!>=tin.get(root)!&&tin.get(id)!<tout.get(root)!;
  const boxes=new Map<string,Box>(),branch=new Map<string,Box>(),pinned=new Map<string,boolean>(),plans=new Map<string,Point>();
  const locked=new Set(project.links.filter(link=>link.snap?.locked).flatMap(link=>[link.sourceId,link.targetId]));
  // Hidden descendants travel with their branch but consume no layout space;
  // do not decode their media or measure thousands of invisible text surfaces.
  for(const node of ordered){const bounds=visible.has(node.id)?presentation.own(node):node;boxes.set(node.id,{x:bounds.x,y:bounds.y,width:bounds.width,height:bounds.height});}
  for(const node of [...ordered].reverse())pinned.set(node.id,!!node.positionLocked||locked.has(node.id)||(children.get(node.id)??[]).some(child=>pinned.get(child.id)));
  const obstacles=new Map<string,ObstacleIndex>();for(const id of workIds)obstacles.set(id,new ObstacleIndex());
  for(const node of ordered)if(node.kind!=='canvas'&&visible.has(node.id)&&(!managed.has(node.id)||pinned.get(node.id))){const box=presentation.clip(node);if(box)obstacles.get(node.canvasId)!.insert({id:node.id,...box});}
  const obstacleHits=(box:Box,node:MeridianNode)=>obstacles.get(node.canvasId)!.query(box).filter(item=>{result.obstacleChecks++;return !belongs(item.id,node.id)&&!belongs(node.id,item.id);});
  const place=(desired:Box,node:MeridianNode,direction:Cardinal,minimumAlong=-Infinity,external=true,preserveAlong=false):{box:Box;cost:number}=>{
    const {u,v}=orientation(direction),pending:Point[]=[{x:0,y:0}],seen=new Set<string>();let best={x:0,y:0};
    const hitsAt=(box:Box)=>external?obstacleHits(box,node):[];
    for(let attempt=0;pending.length&&attempt<64;attempt++){
      pending.sort((a,b)=>a.x*a.x+a.y*a.y-(b.x*b.x+b.y*b.y)||a.y-b.y||a.x-b.x);const delta=pending.shift()!,key=`${delta.x.toFixed(4)}:${delta.y.toFixed(4)}`;if(seen.has(key))continue;seen.add(key);best=delta;
      const candidate=translated(desired,delta);if(projection(candidate,u)[0]<minimumAlong-1e-6)continue;const hits=hitsAt(candidate);if(!hits.length)return {box:candidate,cost:delta.x*delta.x+delta.y*delta.y};
      for(const hit of hits.slice(0,8))for(const axis of preserveAlong?[v]:[u,v]){const a=projection(candidate,axis),b=projection(hit,axis),distance=b[1]-a[0]+12;pending.push({x:delta.x+axis.x*distance,y:delta.y+axis.y*distance});if(axis===u){const reverse=b[0]-a[1]-12;pending.push({x:delta.x+axis.x*reverse,y:delta.y+axis.y*reverse});}}
    }
    // Dense fixed obstacles cannot cause oscillation or an unbounded relaxation.
    // Escape monotonically along the rail until its finite cross-section clears.
    const shortfall=minimumAlong-projection(translated(desired,best),u)[0];if(shortfall>0)best={x:best.x+u.x*shortfall,y:best.y+u.y*shortfall};
    const escape=preserveAlong?v:u;
    for(let attempt=0;attempt<ordered.length+1;attempt++){const candidate=translated(desired,best),hits=hitsAt(candidate);if(!hits.length)return {box:candidate,cost:best.x*best.x+best.y*best.y};const distance=Math.max(...hits.map(hit=>projection(hit,escape)[1]-projection(candidate,escape)[0]+12));best={x:best.x+escape.x*distance,y:best.y+escape.y*distance};}
    throw new Error('Smart Link spacing could not find a finite free position.');
  };
  const layout=(rail:MeridianNode,direction:Cardinal,dryRun:boolean)=>{
    const {u,v}=orientation(direction),raw={x:rail.x+rail.width/2,y:rail.y+rail.height/2},gap=rail.smartLink!.gap,parent=byId.get(rail.parentId??'');
    let anchor={x:0,y:0};const centeredOwner=!!parent&&parent.kind!=='canvas'&&parent.kind!=='junction'&&!presentation.capped(parent)&&!pinned.get(rail.id);
    // A semantic parent's center anchors its chain. Bounded interior rails and
    // free-standing rails retain their deliberately placed handle coordinates.
    if(centeredOwner){
      const parentBox=boxes.get(parent.id)!,along=projection(parentBox,u),across=projection(parentBox,v),du=(along[0]+along[1])/2-(raw.x*u.x+raw.y*u.y),dv=Math.max(0,across[1]+24-(raw.x*v.x+raw.y*v.y));
      anchor={x:u.x*du+v.x*dv,y:u.y*du+v.y*dv};
    }
    const origin={x:raw.x+anchor.x,y:raw.y+anchor.y},targetAlong=origin.x*u.x+origin.y*u.y,targetAcross=origin.x*v.x+origin.y*v.y+gap,owner=topRail.get(rail.id),external=owner===rail.id||!!pinned.get(owner??'');
    const members=children.get(rail.id)??[],ideal:Array<{node:MeridianNode;footprint:Box;own:Box;delta:Point}>=[];
    let cursor=0,first=Infinity,last=-Infinity,cost=0;
    for(const node of members){const footprint=branch.get(node.id)??boxes.get(node.id)!,own=boxes.get(node.id)!,along=projection(footprint,u),across=projection(own,v),delta={x:u.x*(cursor-along[0])+v.x*(targetAcross-across[0]),y:u.y*(cursor-along[0])+v.y*(targetAcross-across[0])};
      const ownAlong=projection(translated(own,delta),u);first=Math.min(first,ownAlong[0]);last=Math.max(last,ownAlong[1]);ideal.push({node,footprint,own,delta});cursor+=along[1]-along[0]+gap;
    }
    // Center the direct bodies, rather than an asymmetric descendant envelope.
    // Descendant footprints still determine every reserved gap between peers.
    const shift=ideal.length?targetAlong-(first+last)/2:0,placements:Array<{node:MeridianNode;delta:Point}>=[];cursor=-Infinity;
    for(const item of ideal){const {node,footprint}=item;
      if(pinned.get(node.id)){cursor=Math.max(cursor,projection(footprint,u)[1]+gap);if(!dryRun)result.pinnedNodeIds.push(node.id);continue;}
      const requested={x:item.delta.x+u.x*shift,y:item.delta.y+u.y*shift},desired=translated(footprint,requested),advance=Math.max(0,cursor-projection(desired,u)[0]);desired.x+=u.x*advance;desired.y+=u.y*advance;requested.x+=u.x*advance;requested.y+=u.y*advance;
      // Nested plans are still in pre-ancestor coordinates. Only the outer
      // managed branch can compare them with unrelated world-space obstacles;
      // pinned owners are already anchored and retain their local checks.
      const fit=place(desired,node,direction,cursor,external,centeredOwner),delta={x:requested.x+fit.box.x-desired.x,y:requested.y+fit.box.y-desired.y};
      placements.push({node,delta});cursor=projection(fit.box,u)[1]+gap;cost+=fit.cost;
    }
    // Keep an already useful automatic direction stable; free-space ties favor
    // downward branch spines, then rightward sequences, then mirrored variants.
    if(direction!==rail.smartLink!.resolvedDirection&&rail.smartLink!.resolvedDirection)cost+=gap*gap;
    if(!dryRun){
      if(anchor.x||anchor.y){plans.set(rail.id,anchor);boxes.set(rail.id,translated(boxes.get(rail.id)!,anchor));}
      for(const {node,delta}of placements){plans.set(node.id,{x:delta.x-anchor.x,y:delta.y-anchor.y});branch.set(node.id,translated(branch.get(node.id)??boxes.get(node.id)!,delta));}
    }
    return cost;
  };
  const insertManagedBranch=(root:MeridianNode)=>{const start=tin.get(root.id)!,end=tout.get(root.id)!;const pending:Array<{node:MeridianNode;delta:Point}>=[{node:root,delta:{x:0,y:0}}];while(pending.length){const {node,delta}=pending.pop()!,local=plans.get(node.id)??{x:0,y:0},total={x:delta.x+local.x,y:delta.y+local.y},cover=presentation.cover(node);if(visible.has(node.id)&&node.kind!=='canvas'&&(!cover||!belongs(cover,root.id))){const box=presentation.clip(node,translated(presentation.footprint(node),total));if(box)obstacles.get(node.canvasId)!.insert({id:node.id,...box});}for(const child of children.get(node.id)??[])if(tin.get(child.id)!>=start&&tin.get(child.id)!<end)pending.push({node:child,delta:total});}};
  // Sibling and nested rail plans are independent. A branch's current visible
  // extent is aggregated once, not re-walked for each ancestor or neighbor.
  for(const node of [...ordered].reverse()){
    let own=boxes.get(node.id)!;
    if(isSmartLink(node)&&visible.has(node.id)&&!node.collapsed){
      let direction:Cardinal=node.smartLink.direction==='auto'?(node.smartLink.resolvedDirection??'down'):node.smartLink.direction;
      if(node.smartLink.direction==='auto'){
        let score=Infinity;for(const candidate of [direction,...(['down','right','up','left'] as Cardinal[]).filter(item=>item!==direction)]){const value=layout(node,candidate,true);if(value<score){score=value;direction=candidate;}}
        node.smartLink.resolvedDirection=direction;
      }
      layout(node,direction,false);
      own=boxes.get(node.id)!;
    }
    if(presentation.capped(node))own=presentation.footprint(node);
    else if(!node.collapsed)for(const child of children.get(node.id)??[]){const box=branch.get(child.id);if(box)own=union(own,box);}
    branch.set(node.id,own);
    if(isSmartLink(node)&&topRail.get(node.id)===node.id&&visible.has(node.id))insertManagedBranch(node);
  }
  const deltas=new Map<string,Point>();
  for(const node of ordered){const inherited=node.parentId?deltas.get(node.parentId):undefined,own=plans.get(node.id),delta={x:(inherited?.x??0)+(own?.x??0),y:(inherited?.y??0)+(own?.y??0)};if(delta.x||delta.y){if(!finite(node.x+delta.x)||!finite(node.y+delta.y))throw new Error('Smart Link movement exceeds finite Work coordinates.');deltas.set(node.id,delta);}}
  for(const node of ordered){const delta=deltas.get(node.id);if(delta){node.x+=delta.x;node.y+=delta.y;result.movedNodeIds.push(node.id);if(plans.has(node.id)){node.childPosition='manual';node.placement='free';}}}
  for(const link of project.links){const a=deltas.get(link.sourceId),b=deltas.get(link.targetId);if(link.points&&a&&b&&a.x===b.x&&a.y===b.y)link.points=link.points.map(point=>({x:point.x+a.x,y:point.y+a.y}));}
  return result;
}

/** Viewer/editor geometry of the rail, independent of zoom and document mutation. */
export function smartLinkGeometry(project:MeridianProject,railId:string,context:SizingContext={},navigation?:ReturnType<typeof navigationIndex>):SmartLinkGeometry|null {
  const nav=navigation??navigationIndex(project,context),rail=nav.nodes.get(railId);return isSmartLink(rail)?geometry(rail,nav,routingContext(project,nav,new Set([rail.canvasId]))):null;
}
/** One shared navigation/measurement index for every rail in a scene render. */
export function smartLinkGeometries(project:MeridianProject,canvasId?:string,context:SizingContext={},navigation?:ReturnType<typeof navigationIndex>):Map<string,SmartLinkGeometry> {
  const rails=project.nodes.filter((node):node is MeridianNode & {smartLink:SmartLinkSettings}=>isSmartLink(node)&&(!canvasId||node.canvasId===canvasId));
  if(!rails.length)return new Map();const nav=navigation??navigationIndex(project,context);
  const routing=routingContext(project,nav,new Set(rails.map(node=>node.canvasId)));
  return new Map(rails.filter(node=>nav.isNodeVisible(node.id)).map(rail=>[rail.id,geometry(rail,nav,routing)]));
}
interface RoutingContext {indices:Map<string,ObstacleIndex>;boxes:Map<string,IndexedBox>}
function routingContext(project:MeridianProject,nav:ReturnType<typeof navigationIndex>,canvasIds:Set<string>):RoutingContext {
  const indices=new Map<string,ObstacleIndex>(),boxes=new Map<string,IndexedBox>(),presentation=presentationIndex(project,nav);for(const id of canvasIds)indices.set(id,new ObstacleIndex());
  for(const node of project.nodes)if(canvasIds.has(node.canvasId)&&node.kind!=='canvas'&&nav.isNodeVisible(node.id)){
    const bounds=presentation.clip(node);if(!bounds)continue;const pad=node.smartLink?3:8,box={id:node.id,x:bounds.x-pad,y:bounds.y-pad,width:bounds.width+pad*2,height:bounds.height+pad*2};
    boxes.set(node.id,box);indices.get(node.canvasId)!.insert(box);
  }
  return {indices,boxes};
}
/** Direct routes cost one indexed lookup. Only obstructed routes build a small
 * rectilinear visibility grid; newly encountered obstacles add their edges on
 * the next bounded pass. Every accepted edge checks the complete shared index. */
function orthogonalRoute(from:Point,to:Point,index:ObstacleIndex,ignore:Set<string>):{points:Point[];blocked:string[]} {
  const region=(a:Point,b:Point):Box=>({x:Math.min(a.x,b.x)-.0001,y:Math.min(a.y,b.y)-.0001,width:Math.abs(a.x-b.x)+.0002,height:Math.abs(a.y-b.y)+.0002});
  const hits=(a:Point,b:Point)=>index.query(region(a,b)).filter(box=>!ignore.has(box.id));
  const first=hits(from,to),straight=from.x===to.x||from.y===to.y;
  if(!first.length)return {points:straight?[from,to]:[from,{x:to.x,y:from.y},to],blocked:[]};
  if(!straight)for(const elbow of[{x:to.x,y:from.y},{x:from.x,y:to.y}])if(!hits(from,elbow).length&&!hits(elbow,to).length)return {points:[from,elbow,to],blocked:[]};
  const inside=(p:Point,b:Box)=>p.x>b.x+1e-6&&p.x<b.x+b.width-1e-6&&p.y>b.y+1e-6&&p.y<b.y+b.height-1e-6;
  const terminal=first.filter(box=>inside(from,box)||inside(to,box));if(terminal.length)return {points:[],blocked:terminal.map(box=>box.id)};
  const obstacles=new Map(first.map(box=>[box.id,box]));
  type State={at:number;score:number};
  for(let round=0;round<8&&obstacles.size<=96;round++){
    const xs=[...new Set([from.x,to.x,...[...obstacles.values()].flatMap(b=>[b.x-.01,b.x+b.width+.01])])].sort((a,b)=>a-b);
    const ys=[...new Set([from.y,to.y,...[...obstacles.values()].flatMap(b=>[b.y-.01,b.y+b.height+.01])])].sort((a,b)=>a-b),height=ys.length,total=xs.length*height;
    const start=xs.indexOf(from.x)*height+ys.indexOf(from.y),finish=xs.indexOf(to.x)*height+ys.indexOf(to.y),distance=new Float64Array(total);distance.fill(Infinity);distance[start]=0;
    const previous=new Int32Array(total);previous.fill(-1);const closed=new Uint8Array(total),heap:State[]=[];
    const push=(state:State)=>{heap.push(state);let i=heap.length-1;while(i){const parent=(i-1)>>1;if(heap[parent].score<=state.score)break;heap[i]=heap[parent];i=parent;}heap[i]=state;};
    const pop=()=>{const first=heap[0],last=heap.pop()!;if(heap.length){let i=0;while(i*2+1<heap.length){let child=i*2+1;if(child+1<heap.length&&heap[child+1].score<heap[child].score)child++;if(heap[child].score>=last.score)break;heap[i]=heap[child];i=child;}heap[i]=last;}return first;};
    push({at:start,score:Math.abs(to.x-from.x)+Math.abs(to.y-from.y)});let visited=0,added=false;
    while(heap.length&&visited++<20000){const current=pop().at;if(closed[current])continue;closed[current]=1;if(current===finish){const path:Point[]=[];for(let at=current;at!==-1;at=previous[at])path.push({x:xs[Math.floor(at/height)],y:ys[at%height]});path.reverse();const compact:Point[]=[];for(const point of path){const a=compact.at(-2),b=compact.at(-1);if(a&&b&&(a.x===b.x&&b.x===point.x||a.y===b.y&&b.y===point.y))compact[compact.length-1]=point;else compact.push(point);}return {points:compact,blocked:[]};}
      const x=Math.floor(current/height),y=current%height,a={x:xs[x],y:ys[y]};
      for(const [nx,ny]of[[x-1,y],[x+1,y],[x,y-1],[x,y+1]]){if(nx<0||ny<0||nx>=xs.length||ny>=ys.length)continue;const at=nx*height+ny;if(closed[at])continue;const b={x:xs[nx],y:ys[ny]},blocked=hits(a,b);if(blocked.length){for(const box of blocked)if(!obstacles.has(box.id)){obstacles.set(box.id,box);added=true;}continue;}const next=distance[current]+Math.abs(a.x-b.x)+Math.abs(a.y-b.y);if(next<distance[at]){distance[at]=next;previous[at]=current;push({at,score:next+Math.abs(b.x-to.x)+Math.abs(b.y-to.y)});}}
    }
    if(!added)break;
  }
  // Never draw a segment through unrelated content merely to hide a routing
  // failure. The selected rail reports the fixed blockers for an explicit edit.
  return {points:[],blocked:[...obstacles.keys()]};
}
function geometry(rail:MeridianNode & {smartLink:SmartLinkSettings},nav:ReturnType<typeof navigationIndex>,routing:RoutingContext):SmartLinkGeometry {
  const direction:Cardinal=rail.smartLink.direction==='auto'?rail.smartLink.resolvedDirection??'down':rail.smartLink.direction;
  const {u,v}=orientation(direction),origin={x:rail.x+rail.width/2,y:rail.y+rail.height/2};
  const members=rail.collapsed?[]:nav.children.get(rail.id)??[],segments:SmartLinkGeometry['segments']=[];
  let minimum=members.length?-24:-80,maximum=members.length?24:80;
  for(const member of members){const box=nav.bounds(member),center={x:box.x+box.width/2,y:box.y+box.height/2},along=(center.x-origin.x)*u.x+(center.y-origin.y)*u.y;
    const from={x:origin.x+u.x*along,y:origin.y+u.y*along};const to={x:center.x-v.x*box.width/2,y:center.y-v.y*box.height/2};segments.push({from,to,nodeId:member.id});minimum=Math.min(minimum,along-24);maximum=Math.max(maximum,along+24);}
  const ignoreRoot=new Set([rail.id,...nav.ancestors(rail.id).map(node=>node.id)]),index=routing.indices.get(rail.canvasId)!;
  const endpoint=(distance:number,sign:number)=>{let end={x:origin.x+u.x*distance,y:origin.y+u.y*distance};for(let attempt=0;attempt<32;attempt++){const blockers=index.query({x:end.x-.001,y:end.y-.001,width:.002,height:.002}).filter(box=>!ignoreRoot.has(box.id));if(!blockers.length)break;const exit=sign>0?Math.max(...blockers.map(box=>projection(box,u)[1]))+12:Math.min(...blockers.map(box=>projection(box,u)[0]))-12;distance=exit-(origin.x*u.x+origin.y*u.y);end={x:origin.x+u.x*distance,y:origin.y+u.y*distance};}return end;};
  const start=endpoint(minimum,-1),end=endpoint(maximum,1),routed:SmartLinkGeometry['segments']=[],blocked=new Set<string>(),trunkPoints:Array<[Point,Point]>=[];
  // Route from the central attachment in both directions, so an obstacle detour
  // cannot strand the ordinary parent tether away from the actual spine.
  for(const endpoint of [start,end]){const path=orthogonalRoute(origin,endpoint,index,ignoreRoot);path.blocked.forEach(id=>blocked.add(id));for(let i=1;i<path.points.length;i++){const from=path.points[i-1],to=path.points[i];routed.push({from,to});trunkPoints.push([from,to]);}}
  for(const segment of segments){
    // A detoured spine is the real attachment surface. Join it rather than
    // leaving a stub aimed at a point now inside the bypassed obstacle.
    let join=segment.from,score=Infinity;
    for(const [a,b] of trunkPoints){const point={x:Math.max(Math.min(a.x,b.x),Math.min(Math.max(a.x,b.x),segment.to.x)),y:Math.max(Math.min(a.y,b.y),Math.min(Math.max(a.y,b.y),segment.to.y))},distance=Math.abs(point.x-segment.to.x)+Math.abs(point.y-segment.to.y);if(distance<score){score=distance;join=point;}}
    const ignore=new Set(ignoreRoot);if(segment.nodeId){
      ignore.add(segment.nodeId);for(const ancestor of nav.ancestors(segment.nodeId))ignore.add(ancestor.id);
      const target=nav.nodes.get(segment.nodeId);
      // The attachment terminates on an enclosing target's frame. Its clipped
      // interior is part of that endpoint, not an unrelated padded obstacle.
      // Presentation ancestry deliberately excludes escaped exterior trays;
      // ordinary unbounded Media descendants remain independent obstacles.
      if(target&&isEnclosingContainer(target))for(const child of nav.descendants(target.id))
        if(routing.boxes.has(child.id)&&nav.presentationAncestors(child.id).some(parent=>parent.id===target.id))ignore.add(child.id);
    }
    const path=orthogonalRoute(join,segment.to,index,ignore);path.blocked.forEach(id=>blocked.add(id));for(let i=1;i<path.points.length;i++)routed.push({from:path.points[i-1],to:path.points[i],...(segment.nodeId?{nodeId:segment.nodeId}:{})});
  }
  return{origin,start,end,direction,segments:routed,memberIds:members.map(node=>node.id),...(blocked.size?{blockedNodeIds:[...blocked]}:{})};
}
