import type { MeridianProject, Point } from './core/types.ts';
import { navigationIndex } from './core/navigation.ts';
import { smartLinkGeometry, smartLinkGeometries } from './core/smart-links.ts';
import type { SizingContext } from './core/sizing.ts';

const NS='http://www.w3.org/2000/svg';
export interface SmartRailOptions {
  selectedId?: string|null;
  navigating: boolean;
  navigation?:ReturnType<typeof navigationIndex>;
  context?:SizingContext;
  onSelect?: (id:string)=>void;
  onActivate?: (id:string,event:KeyboardEvent)=>void;
}
/** One SVG path per rail, with a constant screen-space hit stroke. All members
 * remain real nodes; this surface is only the owner's layout spine. */
export function renderSmartLinkRails(layer:SVGSVGElement,project:MeridianProject,canvasId:string,options:SmartRailOptions):void {
  layer.replaceChildren();
  const navigation=options.navigation??navigationIndex(project,options.context);
  const geometries=smartLinkGeometries(project,canvasId,options.context,navigation);
  for(const owner of project.nodes){
    if(owner.canvasId!==canvasId||!owner.smartLink||owner.collapsed||!navigation.isNodeVisible(owner.id))continue;
    const geometry=geometries.get(owner.id);
    if(!geometry)continue;
    const group=document.createElementNS(NS,'g');group.classList.add('ms-smart-rail');
    group.classList.toggle('is-selected',options.selectedId===owner.id);
    group.classList.toggle('is-blocked',!!geometry.blockedNodeIds?.length);
    group.dataset.nodeId=owner.id;group.dataset.smartLinkId=owner.id;
    group.setAttribute('role','button');group.setAttribute('tabindex','0');
    group.setAttribute('aria-label',`Smart Link: ${owner.tag??owner.title??'chain'}`);
    const title=document.createElementNS(NS,'title');title.textContent=geometry.blockedNodeIds?.length?'Smart Link route blocked. Move its handle clear of the surrounding nodes.':'Smart Link';group.append(title);
    const segments=geometry.segments;
    const d=segments.map(segment=>`M${segment.from.x} ${segment.from.y}L${segment.to.x} ${segment.to.y}`).join('');
    for(const className of ['ms-smart-rail-line','ms-smart-rail-hit']){
      const path=document.createElementNS(NS,'path');path.setAttribute('d',d);path.setAttribute('class',className);
      path.setAttribute('fill','none');path.setAttribute('vector-effect','non-scaling-stroke');group.append(path);
    }
    group.addEventListener('pointerdown',event=>{
      if(!options.navigating||event.button!==0)return;
      event.preventDefault();event.stopPropagation();options.onSelect?.(owner.id);
    });
    group.addEventListener('keydown',event=>{
      if(event.key!=='Enter'&&event.key!==' ')return;
      event.preventDefault();event.stopPropagation();
      if(options.navigating)options.onSelect?.(owner.id);else options.onActivate?.(owner.id,event);
    });
    layer.append(group);
  }
}

export function smartLinkInsertionIndex(project:MeridianProject,railId:string,point:Point):number {
  const navigation=navigationIndex(project),geometry=smartLinkGeometry(project,railId,{},navigation);if(!geometry)return 0;
  const vertical=geometry.direction==='down'||geometry.direction==='up',sign=geometry.direction==='up'||geometry.direction==='left'?-1:1;
  const position=(vertical?point.y:point.x)*sign;
  const index=geometry.memberIds.findIndex(id=>{const bounds=navigation.bounds(navigation.nodes.get(id)!);return (vertical?bounds.y+bounds.height/2:bounds.x+bounds.width/2)*sign>position;});
  return index<0?geometry.memberIds.length:index;
}
