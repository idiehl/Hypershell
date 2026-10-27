import type { BackgroundStyle, MeridianProject, MeridianNode, MeridianLink, Operation, Point, UtilityVariant, StoredModule } from './core/types.ts';
import { cloneProject, forkProject, commitSnapshots } from './core/model.ts';
import { ensureNodeTags, nodeTag, isNodeTag } from './core/node-tag.ts';
import { workLibraryDisplayText } from './core/work-terminology.ts';
import { normalizeRetiredBlocks } from './core/retired-block.ts';
import { rememberContainerSize, transitionContainerSize, isSizeRememberingContainer } from './core/container-size.ts';
import { OPERATION_DEFINITIONS, inputPorts, isComputedLink, valueTypeForLink } from './core/operations.ts';
import { createLinkPath, splitLinkWithJunction } from './core/connections.ts';
import { axisLockedPoint, orthogonalFinish, linkPivotObstacles, draftPivotObstacles, constrainDraftRoute } from './core/link-axis.ts';
import { displayNodeBounds, routeWindowOffset } from './window-scene.ts';
import { revealChildren, isEdgeAttached } from './core/reveal.ts';
import { evaluate } from './core/engine.ts';
import { validateProject, validateChange, reparentNode, isProjectShape } from './core/rules.ts';
import { renderScene, prepareSceneMediaForRemount, sceneGestureActive, openAssetPreview, getAsset, type SceneView } from './scene.ts';
import { navigationIndex } from './core/navigation.ts';
import { resolveSnapping } from './core/snapping.ts';
import { buildPublication, readPublication, createPublicationPayload } from './publication.ts';
import { storage, libraryStorage, moduleStorage, StorageError, type Recovery } from './storage.ts';
import { applyRecipe, inspectProject, type AuthoringRecipe, type InspectionReport } from './core/authoring.ts';
import { nodeAppearance, canvasAppearance, linkAppearance } from './authoring-ui.ts';
import { rebaseTextRuns } from './core/rich-text.ts';
import { createRichTextEditor, captureRichTextFocus, restoreRichTextFocus, flushRichTextEditors, openRichTextDialog, type RichTextEditor, type RichTextValue } from './rich-text-editor.ts';
import { mountCanvasTextEditing, type CanvasTextEditing } from './canvas-text-editing.ts';
import { canMoveNodeBranches, setNodePositionLocked } from './core/position-lock.ts';
import { normalizeLibraryDocument, reconcileLibraryCanvas, libraryDocumentFromProject, type LibraryCanvasDocument, type LibraryCatalogItem } from './core/library.ts';
import { createNodeDialog, createLinkDialog } from './creation-ui.ts';
import { createNodeFromSpec, createLinkFromSpec } from './core/creation.ts';
import { workspaceTools, workspaceMenu, workspaceCreationMenu, type WorkspaceMode, type WorkspaceAction } from './workspace-tools.ts';
import type { SceneAuthorTarget } from './scene.ts';
import { instantiateModule, createModuleFromSelection } from './core/modules.ts';
import { emptyModuleSelection, toggleModuleSelection, resolveModuleSelection } from './core/module-selection.ts';
import { createModulePanelState, renderModulePanel } from './module-panel.ts';
import { lockedClusterNodeIds, setSnapLock, reconcileSnapLinks } from './core/clusters.ts';
import { selectionGraph, moveSelection, deleteSelectionGraph, groupSelection } from './core/selection.ts';
import { createDisclosureCluster, setDisclosureClusterShape } from './core/disclosure-clusters.ts';
import { copyNodes, pasteNodes, type NodeClipboard } from './core/clipboard.ts';
import { ruleSummary, showRuleDialog } from './rule-ui.ts';
import { moduleLibraryDialog, saveModuleDialog, nodeDependenciesDialog, renderDependencySummary } from './module-ui.ts';
import { applicationMenu, bindApplicationChrome, hasNativeChrome, recordCanvasAddress, rememberCanvasHistory, type FileCommand, type ToolCommand, type HistoryDirection } from './application-chrome.ts';
import { arrangeSelection } from './core/arrangement.ts';
import { showArrangementDialog } from './arrangement-ui.ts';
import { gridInspector, showCellEditor } from './grid-ui.ts';
import { arrangeChildren, reflowContainedChildren, resolveReferenceTarget, isOrderedContainer, isEnclosingContainer, normalizeContainerLayouts } from './core/grid.ts';
import { createAutomaticSave, saveChangesScene } from './automatic-save.ts';
import { renderInspectorPanels, createInspectorToggleButton, loadInspectorPanel, saveInspectorPanel, inspectorNodeType, type InspectorPanel } from './inspector-panels.ts';
import { addRemoteLink, removeRemoteLink, renameNode, updateNodeType } from './core/node-editing.ts';
import { nodeSizingMode, constrainChildGeometry, containingFixedInterior } from './core/sizing.ts';
import { resolveCanvasCollisions, collisionFreeResize, collisionNodeShapes, isIntentionalContainment } from './core/collision.ts';
import { nodeShape, shapesOverlap } from './core/geometry.ts';
import { effectiveInheritedRules } from './core/rule-impact.ts';
import { childTools } from './child-tools.ts';
import { bootMediaWindow } from './media-window.ts';
import { isGallery, galleryOwner } from './core/gallery.ts';
import { moveExpansionTray, resizeExpansionTray } from './core/expansion-tray.ts';
import { renderGalleryContents } from './gallery-ui.ts';
import { isBackgroundAsset } from './core/appearance.ts';
import { activeNodeContent, effectiveContentType, nodeContentItems, withNodeContentItems } from './core/content.ts';
import { renderNodeHub, type HubContentChange } from './node-hub.ts';
import { applyHubContent, resolveHubView } from './core/hub-source.ts';
import { documentNodeAsset, documentNodeExpanded, rememberDocumentNodeSize, revealDocumentNode } from './core/document-node.ts';
import { documentPreviewKind } from './core/document-preview.ts';
import { nestNodeBranches } from './core/nesting.ts';
import { createInspectorState } from './inspector-state.ts';
import { createSmartLink, configureSmartLink, addSmartLinkMember, detachSmartLinkMember, reflowSmartLinks, smartLinkGeometry } from './core/smart-links.ts';
import { smartLinkInsertionIndex } from './smart-link-scene.ts';
import { buildTierIndex, parseTierContext, rememberCanvasTier, type CanvasTierAnchor, type CanvasTierIndex } from './core/tiers.ts';
import { bindWebsiteLinks } from './website-links.ts';

bindWebsiteLinks();
const app = document.querySelector<HTMLElement>('#app')!;
let libraryDocument: LibraryCanvasDocument | null = null;
let libraryTargets: LibraryCanvasDocument['canvasTargets'] = {};
let canvasCatalog = new Map<string, LibraryCatalogItem>();
function rememberCanvasCatalog(catalog: LibraryCatalogItem[]): void { canvasCatalog = new Map(catalog.map(item => [item.id, item])); }
function canvasDestinationTitles(source: MeridianProject): Record<string, string> {
  const titles: Record<string, string> = {};
  // Pinned references can retain their canonical launcher only in a snapshot.
  // Live originals take precedence over those saved observations.
  const originals = new Map(Object.values(source.snapshots).map(snapshot => [snapshot.node.id, snapshot.node]));
  for (const node of source.nodes) originals.set(node.id, node);
  for (const node of originals.values()) {
    if (node.kind !== 'canvas-link') continue;
    const target = node.canvasTarget ?? (!node.targetId ? libraryTargets[node.id] : undefined);
    if (!target) continue;
    const saved = canvasCatalog.get(target.projectId), canvasId = target.canvasId ?? saved?.entryCanvasId;
    const canvas = saved?.canvases.find(canvas => canvas.id === canvasId)
      ?? saved?.canvases.find(canvas => canvas.id === saved.entryCanvasId);
    titles[node.id] = workLibraryDisplayText(target.projectId, canvas?.rootId ?? saved?.id ?? node.id, canvas?.title ?? saved?.title ?? (node.title.trim() || displayNodeTag(node, source)));
  }
  return titles;
}
let authorMode: WorkspaceMode = 'select';
let creationMark: Point | null = null;
let canvasPointer: Point | null = null;
let linkDraft: { projectId: string; canvasId: string; sourceId: string; points: Point[]; directions: ('x'|'y'|undefined)[] } | null = null;
let project: MeridianProject | null = null;
let canvasId = '', selected: string | null = null, selectedLink: string | null = null;
let selectionIds: string[] = [];
let redrawScene: (()=>void)|undefined;
let inlineText: CanvasTextEditing | undefined;
let rendering = false;
let moduleSelection = emptyModuleSelection();
let moduleSelectionScope = '';
const modulePanelState = createModulePanelState();
let nodeClipboard: NodeClipboard | null = null;
let selectionMode: 'pan' | 'region' = 'pan';
let regionIntersection: 'contained' | 'intersecting' = 'contained';
let selectedLinkPoint: Point | undefined, selectedLinkSegment: number | undefined;
let view: SceneView = { x: 45, y: 45, scale: 1 };
const canvasViews = new Map<string, SceneView>();
const galleryActive = new Map<string, string>();
const windowOffsets=new Map<string,Point>();
// Normalize managed layout once when entering each Work, without treating a
// renderer-version change as an authored save or repeating it on selection.
const smartEntryLayouts=new Set<string>();
let focusNodeId: string | null = null, focusBranch = false;
// Explicit Work entry fits its visible content once; ordinary redraws retain pan.
let fitWorkspaceOnRender = false;
let restoreBootstrapCamera = (performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined)?.type === 'back_forward';
let inspectorPanel: InspectorPanel = loadInspectorPanel('canvas'), panelOpen = true;
let inspectorWidth = 310;
try { panelOpen=localStorage.getItem('meridian.inspector-open')!=='false';const width=Number(localStorage.getItem('meridian.inspector-width'));if(Number.isFinite(width)&&width>=260&&width<=520)inspectorWidth=width; } catch {}
let linkContextNode: string | null = null;
const descendantLimits = new Map<string, number>();
const backlinkLimits = new Map<string, number>();
const inspectorState = createInspectorState();
const hubState = createInspectorState();
let mountedNodeHub: ReturnType<typeof renderNodeHub> | undefined;
const visualRelations=['causal','chronological','mutual','generic','cyclical'] as const;
let undoStack: MeridianProject[] = [], redoStack: MeridianProject[] = [];
let dirty = false, editVersion = 0, savedRevision = 0, saving = false;
let afterSaveRenderTimer: ReturnType<typeof setTimeout> | undefined;
let saveFailure: 'conflict' | 'offline' | null = null;
const automaticSave = createAutomaticSave({ commit: commitSave });
const localRecoveryPrefix = 'meridian.recovery.v1.';
// window.name survives this window's reload and is independent of an opener's
// cloned sessionStorage. Each canvas window owns its own fallback draft.
if (!window.name.startsWith('meridian-editor:')) window.name = `meridian-editor:${crypto.randomUUID()}`;
const localRecoveryOwner = window.name.slice('meridian-editor:'.length);
rememberCanvasHistory();
const tierContextKey = 'meridian.canvas-tiers.v1';
let tierContext = parseTierContext(null);
try { tierContext = parseTierContext(sessionStorage.getItem(tierContextKey)); } catch {}
let tierLibrary: LibraryCanvasDocument | null = null;
let tierMemo: { project: MeridianProject; home: MeridianProject; context: typeof tierContext; index: CanvasTierIndex } | undefined;
let navigationPending = false;
let restoringHistory = false;
const windowViewsKey = `meridian.canvas-views.${window.name}`;
let savedWindowViews: Record<string, SceneView> = {};
try { const saved=JSON.parse(sessionStorage.getItem(windowViewsKey)??'{}');if(saved&&typeof saved==='object'&&!Array.isArray(saved))savedWindowViews=saved; } catch {}
const historyEntryId=()=>typeof history.state?.meridianHistoryId==='string'?history.state.meridianHistoryId as string:'';
let activeHistoryEntry=historyEntryId();
// The same Work can occur several times in the browser history, each with its
// own camera. Keep the departing entry separate while popstate selects another.
function cacheView(){if(!project)return;canvasViews.set(canvasId,{...view});savedWindowViews[`${project.id}/${canvasId}`]={...view};if(activeHistoryEntry)savedWindowViews[`history:${activeHistoryEntry}/${project.id}/${canvasId}`]={...view};}
function rememberView(){cacheView();try{sessionStorage.setItem(windowViewsKey,JSON.stringify(savedWindowViews));}catch{}}
function rememberedView(id:string,p=project):SceneView {const remembered=p?savedWindowViews[`${p.id}/${id}`]:undefined;return remembered&&Number.isFinite(remembered.x)&&Number.isFinite(remembered.y)&&Number.isFinite(remembered.scale)&&remembered.scale>0?{...remembered}:{x:45,y:45,scale:1};}
function rememberedHistoryView(id:string,p=project):SceneView {const remembered=p?savedWindowViews[`history:${activeHistoryEntry}/${p.id}/${id}`]:undefined;return remembered&&Number.isFinite(remembered.x)&&Number.isFinite(remembered.y)&&Number.isFinite(remembered.scale)&&remembered.scale>0?{...remembered}:rememberedView(id,p);}
function currentTiers(): CanvasTierIndex {
  const active = project!, home = libraryDocument ? active : tierLibrary?.project ?? active;
  if (tierMemo?.project === active && tierMemo.home === home && tierMemo.context === tierContext) return tierMemo.index;
  const index = buildTierIndex(home.id === active.id ? [active] : [home, active], {
    home: { projectId: libraryDocument || tierLibrary ? home.id : '', canvasId: home.entryCanvasId }, anchors: tierContext.anchors,
  });
  tierMemo = { project: active, home, context: tierContext, index }; return index;
}
function rememberTierAncestry(extra?: CanvasTierAnchor): void {
  if (!project) return;
  const anchors = currentTiers().anchors();
  for (const anchor of anchors) tierContext = rememberCanvasTier(tierContext, anchor);
  if (extra) tierContext = rememberCanvasTier(tierContext, extra);
  try { sessionStorage.setItem(tierContextKey, JSON.stringify(tierContext)); } catch {}
}
async function readTierLibrary(): Promise<void> {
  try {
    const [stored, catalog] = await Promise.all([libraryStorage.load(), libraryStorage.catalog()]);
    rememberCanvasCatalog(catalog);
    tierLibrary = reconcileLibraryCanvas(normalizeLibraryDocument(stored, catalog), catalog);
  } catch { tierLibrary = null; }
  tierMemo = undefined;
}
let localRecoveryKey: string | null = null;
let recoveryTimer: ReturnType<typeof setTimeout> | undefined;
let recoveryWrites = Promise.resolve();
let recoveryToken: string | null = null;
let recoveryNeedsFork = false;
let recoveryBase = 0;
let recoveryBlocked = false;
let savedSnapshots: MeridianProject['snapshots'] = {};
let toastTimer: ReturnType<typeof setTimeout> | undefined;
let toastText = '', toastError = false;
let runtime = true;
let viewerAssets: Promise<[string,string]> | null = null;
const operations = OPERATION_DEFINITIONS;

function element<K extends keyof HTMLElementTagNameMap>(tag:K, cls='', text?:string):HTMLElementTagNameMap[K] {
  const e = document.createElement(tag); if(cls)e.className=cls; if(text!==undefined)e.textContent=text; return e;
}
function button(label:string, action:()=>unknown, cls='', title=label):HTMLButtonElement {
  const b=element('button',cls,label); b.type='button'; b.title=title; b.setAttribute('aria-label',title);
  b.onclick=()=>{Promise.resolve(action()).catch(fail);}; return b;
}
function field(label:string, control:HTMLElement) {const wrapper=element('label','field');control.setAttribute('aria-label',label);wrapper.append(element('span','field-label',label),control);return wrapper;}
function input(value:string, change:(value:string)=>void, type='text') {const i=element('input');i.type=type;i.value=value;i.onchange=()=>change(i.value);return i;}
function select(values:{value:string;label:string}[], value:string, change:(value:string)=>void) {const s=element('select'); for(const v of values){const o=element('option','',v.label);o.value=v.value;s.append(o);}s.value=value;s.onchange=()=>change(s.value);return s;}
function check(label:string, value:boolean, change:(value:boolean)=>void){const l=element('label','check-row');const c=element('input');c.type='checkbox';c.checked=value;c.onchange=()=>change(c.checked);l.append(c,element('span','',label));return l;}
function fail(e:unknown){notice(e instanceof Error?e.message:String(e),true);}
function notice(text:string,error=false){toastText=text;toastError=error;document.querySelector('.toast')?.remove();const t=element('div',`toast ${error?'error':''}`,text);t.setAttribute('role',error?'alert':'status');document.body.append(t);clearTimeout(toastTimer);toastTimer=setTimeout(()=>{t.remove();toastText='';},error?12000:5000);}
function download(blob:Blob,name:string){const a=element('a');const url=URL.createObjectURL(blob);a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),20000);}
function safeName(name:string){return name.replace(/[<>:"/\\|?*\u0000-\u001f]/g,'_').trim()||'Meridian';}

function modal(title:string):{dialog:HTMLDialogElement;body:HTMLElement;footer:HTMLElement;close:()=>void}{
  const dialog=element('dialog','modal');dialog.setAttribute('aria-label',title);const header=element('div','modal-header');header.append(element('h2','',title),button('×',()=>dialog.close(),'icon-button','Close dialog'));
  const body=element('div','modal-body'), footer=element('div','modal-footer');dialog.append(header,body,footer);document.body.append(dialog);
  dialog.addEventListener('close',()=>dialog.remove());dialog.showModal();return {dialog,body,footer,close:()=>dialog.close()};
}
async function askName(title:string,initial:string,onAccept?:()=>void):Promise<string|null>{return new Promise(resolve=>{const m=modal(title);const i=input(initial,()=>{});m.body.append(field('Name',i));let result:string|null=null;const accept=()=>{if(!i.value.trim())return;i.reportValidity();try{onAccept?.();}catch(error){fail(error);return;}result=i.value.trim();m.close();};m.footer.append(button('Cancel',m.close,'secondary'),button('Continue',accept,'primary'));m.dialog.addEventListener('close',()=>resolve(result));i.addEventListener('keydown',e=>{if(e.key==='Enter'&&i.value.trim()){e.preventDefault();accept();}});i.focus();i.select();});}
async function confirmAction(title:string,message:string,yes='Continue'):Promise<boolean>{return new Promise(resolve=>{const m=modal(title);m.body.append(element('p','',message));let result=false;m.footer.append(button('Cancel',m.close,'secondary'),button(yes,()=>{result=true;m.close();},'primary'));m.dialog.addEventListener('close',()=>resolve(result));});}

function currentNode(){return project?.nodes.find(n=>n.id===selected);}
function currentSelection(): string[] { return project && authorMode === 'module' ? resolveModuleSelection(project,moduleSelection,canvasId).nodeIds : selected ? selectionIds.includes(selected) ? selectionIds : [selected] : []; }
function selectNodes(ids: string[]): void { selectionIds=[...new Set(ids)];selected=selectionIds.at(-1)??null;selectedLink=null;if(ids.length)panelOpen=true;render(); }
function selectModuleNode(id:string):void {
  if(!project||project.nodes.find(node=>node.id===id)?.kind==='canvas')return;
  authorMode='module';
  moduleSelection=toggleModuleSelection(project,moduleSelection,id);
  const ids=resolveModuleSelection(project,moduleSelection,canvasId).nodeIds;
  selectionIds=ids;selected=ids.includes(id)?id:ids.at(-1)??null;selectedLink=null;
  panelOpen=true;inspectorPanel='module';saveInspectorPanel(inspectorPanel);render();
}
async function saveSelectedModule(name:string):Promise<void> {
  if(!project)return;
  const source=cloneProject(project),ids=resolveModuleSelection(source,moduleSelection,canvasId).nodeIds;
  if(!ids.length)throw new Error('Select at least one node to create a module.');
  for(const node of source.nodes)if(node.kind==='canvas-link'&&!node.canvasTarget&&!node.targetId&&libraryTargets[node.id])node.canvasTarget=structuredClone(libraryTargets[node.id]);
  const sourceCanvas=canvasId,stored=await moduleStorage.save(createModuleFromSelection(source,ids,name));
  modulePanelState.catalog=null;modulePanelState.expanded.add(stored.id);
  if(project?.id===source.id&&canvasId===sourceCanvas&&authorMode==='module')finishCanvasTool(false);
  render();notice(`Saved ${stored.name} to your Module Library.`);
}
async function addStoredModule(stored:StoredModule,sourceId:string,sourceCanvas:string,sourceVersion:number):Promise<void> {
  if(!project||project.id!==sourceId||canvasId!==sourceCanvas||editVersion!==sourceVersion)throw new Error('The work changed. Choose Add to Work again on the current work.');
  const parentId=currentCanvas()!.rootId,index=navigationIndex(project),siblings=project.nodes.filter(node=>node.parentId===parentId);
  const x=(80-view.x)/view.scale,y=Math.max((100-view.y)/view.scale,...siblings.map(node=>{const bounds=index.bounds(node);return bounds.y+bounds.height+24;}));
  const result=instantiateModule(project,stored,{canvasId,parentId,x,y});
  if(!edit(next=>Object.assign(next,result.project),`Added ${stored.name}. This copy has independent GUIDs.`,{preserveLayout:true}))return;
  selected=result.rootId;selectedLink=null;selectionIds=[result.rootId];focusNodeId=result.rootId;focusBranch=true;
  if(authorMode==='module'){moduleSelection=emptyModuleSelection();moduleSelection.overrides[result.rootId]=true;}
  render();
}
function lockSnappedEdge(sourceId:string,targetId:string,locked:boolean,linkId?:string):void {
  edit(next=>Object.assign(next,setSnapLock(next,sourceId,targetId,locked,linkId)),locked?'Cluster locked. Drag either node to move the assembly.':'Cluster edge unlocked.');
}
function copySelectedNodes(): void {
  if(!project)return;
  const ids=currentSelection().filter(id=>project!.nodes.find(node=>node.id===id)?.kind!=='canvas');
  if(!ids.length)return;
  const source=cloneProject(project);
  for(const node of source.nodes)if(node.kind==='canvas-link'&&!node.canvasTarget&&!node.targetId&&libraryTargets[node.id])node.canvasTarget=structuredClone(libraryTargets[node.id]);
  nodeClipboard=copyNodes(source,ids);
  const count=selectionGraph(source,ids).rootIds.length;
  notice(`Copied ${count===1?'node':`${count} nodes`}. Press Ctrl+V to paste.`);
}
function pasteCopiedNodes(): void {
  if(!project||!nodeClipboard)return;
  const result=pasteNodes(project,nodeClipboard,canvasId);
  if(!edit(next=>Object.assign(next,result.project)))return;
  authorMode='select';selectionMode='pan';linkDraft=null;creationMark=null;focusNodeId=null;focusBranch=false;
  const viewport=document.querySelector<HTMLElement>('.ms-viewport'),nav=navigationIndex(project),boxes=result.rootIds.map(id=>nav.bounds(nav.nodes.get(id)!));
  if(viewport&&boxes.length&&!boxes.some(box=>box.x*view.scale+view.x<viewport.clientWidth&&(box.x+box.width)*view.scale+view.x>0&&box.y*view.scale+view.y<viewport.clientHeight&&(box.y+box.height)*view.scale+view.y>0)){
    const left=Math.min(...boxes.map(box=>box.x)),right=Math.max(...boxes.map(box=>box.x+box.width)),top=Math.min(...boxes.map(box=>box.y)),bottom=Math.max(...boxes.map(box=>box.y+box.height));
    view={...view,x:viewport.clientWidth/2-(left+right)/2*view.scale,y:viewport.clientHeight/2-(top+bottom)/2*view.scale};canvasViews.set(canvasId,{...view});
  }
  selectNodes(result.rootIds);
}
function selectVisible(): void { if(!project)return;const nav=navigationIndex(project);selectNodes(project.nodes.filter(n=>n.canvasId===canvasId&&n.kind!=='canvas'&&nav.isNodeVisible(n.id)).map(n=>n.id)); }
function moveSelected(ids: string[], dx: number, dy: number): void { if(!dx&&!dy)return;if(project&&!canMoveNodeBranches(project,ids)){notice('Unlock Position before moving this node or a branch that contains it.');return;}edit(p=>{const moved=lockedClusterNodeIds(p,canvasId,ids);Object.assign(p,moveSelection(p,ids,dx,dy));for(const id of selectionGraph(p,moved).rootIds){const node=p.nodes.find(node=>node.id===id);if(node&&node.kind!=='canvas')node.childPosition='manual';}}); }
function nestSelected(ids:string[],parentId:string,dx:number,dy:number):void {if(!project)return;const roots=selectionGraph(project,ids).rootIds;if(edit(next=>Object.assign(next,nestNodeBranches(next,ids,parentId,{x:dx,y:dy})))){authorMode='select';selectionMode='pan';selectNodes(roots);}}
function arrangeSelected(): void {
  if(!project)return;const ids=currentSelection(),source=project.id,version=editVersion;
  const graph=selectionGraph(project,ids);
  showArrangementDialog({branchCount:graph.rootIds.length,apply:action=>{
    if(!project||project.id!==source||editVersion!==version)throw new Error('The work changed. Close this menu and select the nodes again.');
    const result=arrangeSelection(project,ids,action);
    if(!result.movedRootIds.length){notice('The selected tiles already have this arrangement.');return;}
    edit(p=>Object.assign(p,result.project),'Arranged the selected branches. Undo restores their previous positions.');
  }});
}
async function groupSelected(): Promise<void> {
  if(!project)return;const ids=currentSelection(),source=project.id,version=editVersion;
  const graph=selectionGraph(project,ids);if(graph.rootIds.length<2){notice('Select at least two separate branches to group.');return;}
  const nodes=new Map(project.nodes.map(node=>[node.id,node]));if(new Set(graph.rootIds.map(id=>nodes.get(id)?.parentId)).size!==1){notice('Group branches with the same parent so their inherited context stays intact.');return;}
  const name=await askName('Group selected nodes','New group');if(!name)return;
  if(!project||project.id!==source||editVersion!==version){notice('The work changed. Select the nodes again before grouping.',true);return;}
  edit(p=>{const result=groupSelection(p,ids,{title:name});Object.assign(p,result.project);selected=result.rootId;selectionIds=[result.rootId];selectedLink=null;},'Grouped the selected branches. Undo restores their previous parents.');
}
function createSelectedCluster(): void {
  commitFocusedField();
  if(!project)return;const ids=currentSelection(),source=project.id,version=editVersion;
  const graph=selectionGraph(project,ids);
  if(graph.rootIds.length<2){notice('Select at least two separate branches to create a Cluster.');return;}
  const parents=new Set(graph.rootIds.map(id=>project!.nodes.find(node=>node.id===id)?.parentId));
  if(parents.size!==1){notice('Choose branches with the same parent to create a Cluster while preserving their inherited context.');return;}
  const m=modal('Create Cluster'),name=input('New Cluster',()=>{}),shape=select([{value:'square',label:'Square'},{value:'circle',label:'Circle'}],'square',()=>{});
  name.required=true;name.maxLength=240;
  m.body.append(field('Name',name),field('Collapsed button shape',shape),element('p','muted','Keep these branches together and collapse them into one animated button. Their edges do not need to touch.'));
  const create=()=>{
    if(!name.reportValidity()||!name.value.trim())return;
    if(!project||project.id!==source||editVersion!==version){notice('The Work changed. Close this dialog and select the nodes again.',true);return;}
    if(edit(next=>{const result=createDisclosureCluster(next,ids,{name:name.value.trim(),buttonShape:shape.value as 'square'|'circle'});Object.assign(next,result.project);selected=result.rootId;selectionIds=[result.rootId];selectedLink=null;authorMode='select';selectionMode='pan';moduleSelection=emptyModuleSelection();panelOpen=true;inspectorPanel='properties';saveInspectorPanel(inspectorPanel);},'Created a Cluster. Collapse it to a single button; Undo restores the original grouping.',{preserveLayout:true}))m.close();
  };
  m.footer.append(button('Cancel',m.close,'secondary'),button('Create Cluster',create,'primary'));
  name.addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();create();}});name.focus();name.select();
}
function currentCanvas(){return project?.canvases.find(c=>c.id===canvasId);}
function displayNodeTag(node:MeridianNode,source:MeridianProject|null=project):string{return workLibraryDisplayText(source?.id??'',node.id,nodeTag(node));}
function displayWorkTitle(source:MeridianProject,targetId=canvasId):string{const canvas=source.canvases.find(canvas=>canvas.id===targetId);return workLibraryDisplayText(source.id,canvas?.rootId??source.id,canvas?.title??source.title);}
function editInheritedRules(nodeId:string):void {
  if(!project)return;const source=project.id,version=editVersion;
  showRuleDialog({project:cloneProject(project),nodeId,apply:rules=>{
    if(!project||project.id!==source||editVersion!==version)throw new Error('The work changed while this preview was open. Close it and review the current rules again.');
    edit(p=>{const node=p.nodes.find(item=>item.id===nodeId);if(!node)throw new Error('The selected node no longer exists.');if(rules)node.rules=structuredClone(rules);else delete node.rules;},'Applied inherited rules. Undo restores the previous allowances.');
  }});
}
function inheritedRuleSettings(node:MeridianNode):HTMLElement {
  const panel=element('details','advanced inherited-rule-settings');panel.append(element('summary','','Inherited rules'),element('p','muted',ruleSummary(node.rules)),button('Edit inherited rules',()=>editInheritedRules(node.id),'secondary full'));return panel;
}
function showCompositionTools(){
  if(!project)return;
  const base=cloneProject(project),baseVersion=editVersion,baseCanvas=canvasId,baseSavedRevision=savedRevision;
  const m=modal('Composition tools');m.dialog.classList.add('composition-modal');
  m.body.append(element('p','lead-small','Compose connected works, then inspect the result.'),element('p','muted','Recipes can add nodes and media, label relationships, apply styles, and arrange groups. Preview uses the published renderer. Apply adds one undo step to your draft.'));
  const overview=element('div','composition-report');m.body.append(overview);
  const describe=(report:InspectionReport,inspectedCanvas=baseCanvas)=>{
    overview.replaceChildren();
    const c=report.canvases.find(c=>c.id===inspectedCanvas);
    if(c)overview.append(element('p','',`${workLibraryDisplayText(base.id,base.canvases.find(canvas=>canvas.id===c.id)?.rootId??c.id,c.title)}: ${c.nodeCount} nodes · ${c.linkCount} links · ${c.imageCount} images · ${c.styles.styledNodeCount} styled nodes`));
    const warnings=report.warnings.filter(w=>!w.canvasId||w.canvasId===inspectedCanvas);
    overview.append(element('p','muted',warnings.length?`${warnings.length} review notes on this work. Geometry and text estimates are hints; review the rendered composition.`:'No composition warnings on this work. Review the rendered result before applying.'));
    if(warnings.length){const details=element('details');details.append(element('summary','', 'Review notes'));for(const warning of warnings.slice(0,30))details.append(element('p','muted',warning.message));if(warnings.length>30)details.append(element('p','muted','Download the inspection report for all notes.'));overview.append(details);}
  };
  const initialReport=inspectProject(base);describe(initialReport);
  const recipe=element('textarea','composition-recipe');recipe.rows=8;recipe.spellcheck=false;recipe.setAttribute('aria-label','Composition recipe');recipe.placeholder='Load a .json recipe or paste one here.';
  const load=button('Load recipe…',()=>{const file=element('input');file.type='file';file.accept='.json,application/json';file.onchange=async()=>{try{if(!file.files?.[0])return;recipe.value=await file.files[0].text();invalidate();}catch(e){fail(e);}};file.click();},'secondary');
  const downloadReport=button('Download inspection',()=>download(new Blob([JSON.stringify(inspectProject(candidate??base),null,2)],{type:'application/json'}),`${safeName(base.title)}-inspection.json`),'quiet');
  const actions=element('div','composition-actions');actions.append(load,downloadReport);m.body.append(actions,field('Composition recipe',recipe));
  const message=element('p','muted');message.setAttribute('role','status');m.body.append(message);
  const preview=element('div','composition-preview');m.body.append(preview);
  let candidate:MeridianProject|null=null,url:string|null=null,generation=0;
  const apply=button('Apply to draft',()=>{
    if(!candidate||project?.id!==base.id||editVersion!==baseVersion||savedRevision!==baseSavedRevision||project.revision!==base.revision||saving){notice('The draft or saved revision changed. Reopen composition tools to preview against the latest draft.',true);return;}
    const result=candidate;m.close();selected=null;selectedLink=null;
    if(result.entryCanvasId!==base.entryCanvasId)canvasId=result.entryCanvasId;
    edit(p=>Object.assign(p,result),'Composition applied. Undo restores the previous draft.');
    document.querySelector<HTMLButtonElement>('[aria-label="Fit Work"]')?.click();
  },'primary');apply.disabled=true;
  function invalidate(){candidate=null;apply.disabled=true;generation++;if(url)URL.revokeObjectURL(url);url=null;preview.replaceChildren();message.className='muted';message.textContent='Preview the recipe before applying it.';}
  recipe.addEventListener('input',invalidate);
  m.dialog.addEventListener('close',()=>{generation++;if(url)URL.revokeObjectURL(url);});
  m.footer.append(button('Close',m.close,'secondary'),button('Preview recipe',async()=>{
    invalidate();const ticket=generation;
    try{
      const result=applyRecipe(base,JSON.parse(recipe.value) as AuthoringRecipe);
      const [js,css]=await publicationAssets();if(ticket!==generation||!m.dialog.open)return;
      candidate=result.project;
      const display=cloneProject(candidate);if(display.entryCanvasId===base.entryCanvasId)display.entryCanvasId=baseCanvas;
      describe(result.report,display.entryCanvasId);
      url=URL.createObjectURL(new Blob([buildPublication(display,js,css)],{type:'text/html'}));
      const frame=element('iframe','composition-frame');frame.title='Composition preview';frame.sandbox.add('allow-scripts');frame.src=url;preview.append(frame);
      message.textContent=`Ready: ${candidate.canvases.length-base.canvases.length} Works, ${candidate.nodes.length-base.nodes.length} nodes, and ${candidate.links.length-base.links.length} links added. Nothing has been saved.`;apply.disabled=false;
    }catch(e){message.textContent=e instanceof Error?e.message:String(e);message.className='composition-error';}
  },'secondary'),apply);
}

async function showModules(){
  if(!project)return;
  const sourceId=project.id,sourceVersion=editVersion,sourceCanvas=canvasId;
  const parent=currentNode()??project.nodes.find(node=>node.id===currentCanvas()?.rootId)!;
  const parentId=parent.id;
  await moduleLibraryDialog({destination:parent.kind==='canvas'?displayWorkTitle(project):displayNodeTag(parent),
    insert:stored=>{
      if(!project||project.id!==sourceId||canvasId!==sourceCanvas||editVersion!==sourceVersion)throw new Error('The work changed. Reopen the module library before inserting.');
      const parentNode=project.nodes.find(node=>node.id===parentId);if(!parentNode)throw new Error('The destination is no longer available.');
      const siblings=project.nodes.filter(node=>node.parentId===parentId),index=navigationIndex(project);
      const x=parentNode.kind==='canvas'?Math.max(45,(100-view.x)/view.scale):parentNode.x+24;
      const y=parentNode.kind==='canvas'?Math.max(70,(100-view.y)/view.scale,...siblings.map(node=>node.y+index.bounds(node).height+32)):Math.max(parentNode.y+116,...siblings.map(node=>node.y+index.bounds(node).height+24));
      const result=instantiateModule(project,stored,{canvasId:sourceCanvas,parentId,x,y});
      const target=result.project.nodes.find(node=>node.id===parentId)!,root=result.project.nodes.find(node=>node.id===result.rootId)!;
      transitionContainerSize(target,true,undefined,{preserveCurrentRecord:!!containingFixedInterior(result.project,target)});
      if(target.kind!=='canvas'&&target.childLayout==='spatial'){root.placement='near';Object.assign(result.project,revealChildren(result.project,target.id,true));}
      else if(target.kind!=='canvas')Object.assign(result.project,reflowContainedChildren(result.project,target.id));
      selected=result.rootId;selectedLink=null;focusNodeId=result.rootId;focusBranch=true;
      edit(p=>Object.assign(p,result.project),`Inserted ${stored.name}. This copy is independent; Undo removes the entire insertion.`);
    },
    openDefinition:async stored=>{
      if(!await leaveCurrent())return false;
      const copy=forkProject(stored.project);copy.title=`${stored.name} · Definition copy`;const created=await storage.create(commitSnapshots(copy));setProject(created);repairVisibleOverlaps();return true;
    }
  });
}

function normalizeSelection(){if(!project)return;if(linkDraft&&(linkDraft.projectId!==project.id||linkDraft.canvasId!==canvasId||!navigationIndex(project).isNodeVisible(linkDraft.sourceId)))linkDraft=null;selectionIds=selectionIds.filter(id=>project!.nodes.some(n=>n.id===id&&n.canvasId===canvasId));if(!project.canvases.some(c=>c.id===canvasId))canvasId=project.entryCanvasId;if(selected&&!project.nodes.some(n=>n.id===selected&&n.canvasId===canvasId))selected=null;if(selectedLink&&!project.links.some(l=>l.id===selectedLink&&l.canvasId===canvasId))selectedLink=null;}
function descendants(p:MeridianProject,id:string):Set<string>{const result=new Set([id]);let changed=true;while(changed){changed=false;for(const n of p.nodes)if(n.parentId&&result.has(n.parentId)&&!result.has(n.id)){result.add(n.id);changed=true;}}return result;}
function localDraftKey(id:string){return `${localRecoveryPrefix}${id}.${localRecoveryOwner}`;}
function storeLocalRecovery():boolean {
  if(!project||!dirty)return true;
  try {
    const copy=cloneProject(project);copy.revision=recoveryRevision();copy.snapshots={};
    const draft:Recovery={project:copy,baseRevision:recoveryRevision(),savedAt:new Date().toISOString()};
    const content=JSON.stringify(draft);
    // The host stores full-size drafts. Browser fallback is bounded and never
    // evicts another window's work to make room for a large attachment.
    if(content.length>2_000_000)return false;
    localRecoveryKey??=localDraftKey(project.id);localStorage.setItem(localRecoveryKey,content);return true;
  } catch { return false; }
}
function clearLocalRecovery(){if(!localRecoveryKey)return;try{localStorage.removeItem(localRecoveryKey);}catch{}localRecoveryKey=null;}
function readLocalRecovery(id:string):{key:string;recovery:Recovery}|null {
  try {
    const keys=[...Array(localStorage.length)].map((_,index)=>localStorage.key(index)!).filter(key=>key.startsWith(`${localRecoveryPrefix}${id}.`));
    const drafts=keys.flatMap(key=>{try{const recovery=JSON.parse(localStorage.getItem(key)!) as Recovery;return isProjectShape(recovery.project)&&recovery.project.id===id&&Number.isInteger(recovery.baseRevision)&&typeof recovery.savedAt==='string'&&!validateProject(recovery.project).length?[{key,recovery}]:[];}catch{return [];}});
    return drafts.find(item=>item.key===localDraftKey(id))??drafts.sort((a,b)=>b.recovery.savedAt.localeCompare(a.recovery.savedAt))[0]??null;
  } catch { return null; }
}
function storeRecovery(copy:MeridianProject,base:number){copy.revision=base;const id=copy.id,token=recoveryToken;const write=recoveryWrites.then(async()=>{const result=libraryDocument?await libraryStorage.autosave(libraryDocumentFromProject({...libraryDocument,canvasTargets:libraryTargets},copy),base,project?.id===id?recoveryToken:token):await storage.autosave(copy,base,project?.id===id?recoveryToken:token);if(project?.id===id)recoveryToken=result.savedAt;});recoveryWrites=write.catch(e=>{if(project?.id===id){if(e instanceof StorageError&&e.status===409)recoveryBlocked=true;const fallback=storeLocalRecovery();notice(`Recovery draft could not be stored: ${e.message}. ${fallback?'A backup remains in this browser.':'Keep this window open and save a copy or download the work.'}`,true);}});return write;}
function recoveryRevision(){return recoveryNeedsFork||recoveryToken!==null?recoveryBase:savedRevision;}
function scheduleRecovery(){clearTimeout(recoveryTimer);if(recoveryBlocked){storeLocalRecovery();return;}recoveryTimer=setTimeout(()=>{if(project&&dirty&&!saving)void storeRecovery(cloneProject(project),recoveryRevision()).catch(()=>{});},700);}
function changedDraft(){dirty=true;editVersion++;if(localRecoveryKey)storeLocalRecovery();scheduleRecovery();automaticSave.changed();updateSaveState();}
function updateSaveState(){
  const control=document.querySelector<HTMLButtonElement>('[data-save-button]');
  if(control){control.textContent=saving?'Saving…':'Save';control.disabled=saving;control.title=saveFailure==='offline'?'Retry saving changes':'Save pending changes now';}
}
function renderAfterSave(){
  clearTimeout(afterSaveRenderTimer);afterSaveRenderTimer=undefined;
  if(inlineText?.active){updateSaveState();return;}
  // Saving must not destroy a field the user is currently typing in. Its next
  // committed edit performs the normal scene render, including new snapshots.
  const editing=(document.activeElement as HTMLElement)?.closest('input,textarea,select,[contenteditable]');
  if(editing&&app.contains(editing)){updateSaveState();return;}
  const scene=app.querySelector<HTMLElement>('.canvas-stage');
  if(scene&&sceneGestureActive(scene)){
    // Save the committed draft normally, but let the gesture keep pointer
    // capture and its preview until release or cancellation. Its own edit
    // normally renders first; a no-op/cancel still refreshes saved snapshots.
    updateSaveState();const id=project?.id,canvas=canvasId;
    afterSaveRenderTimer=setTimeout(()=>{afterSaveRenderTimer=undefined;if(project?.id===id&&canvasId===canvas)renderAfterSave();},50);
    return;
  }
  render();
}
function commitFocusedField(){flushRichTextEditors(true);const active=document.activeElement as HTMLElement|null;if(active===inlineText?.surface)return;if(active&&app.contains(active)&&active.matches('input,textarea,select,[contenteditable]'))active.blur();}
function edit(action:(p:MeridianProject)=>void, message?:string,options?:{preserveLayout?:boolean;richText?:boolean}){
  if(!options?.richText)inlineText?.flush(true);
  // Every action receives its own draft. The previous immutable graph already
  // is the undo snapshot; cloning it again duplicates the entire saved Work.
  if(!project)return false;const before=project, next=cloneProject(project);
  const old=new Map(before.nodes.map(node=>[node.id,node]));
  try{
    action(next);
    Object.assign(next,normalizeRetiredBlocks(next));
    if(!options?.richText)for(const node of next.nodes){const prior=old.get(node.id);if(!prior)continue;for(const [textKey,runKey]of [['title','titleRuns'],['text','textRuns']]as const)if(node[textKey]!==prior[textKey]&&JSON.stringify(node[runKey])===JSON.stringify(prior[runKey])){const runs=rebaseTextRuns(prior[textKey]??'',node[textKey]??'',prior[runKey]);if(runs.length)node[runKey]=runs;else delete node[runKey];}}
    Object.assign(next,reconcileSnapLinks(next,canvasId,undefined,{createContacts:false}));
    const resized=next.nodes.filter(node=>{const prior=old.get(node.id);return !prior||node.text!==prior.text||node.title!==prior.title||node.showTitle!==prior.showTitle||JSON.stringify(node.contentItems?.[0])!==JSON.stringify(prior.contentItems?.[0])||JSON.stringify(node.textRuns)!==JSON.stringify(prior.textRuns)||JSON.stringify(node.titleRuns)!==JSON.stringify(prior.titleRuns)||node.assetId!==prior.assetId||node.contentType!==prior.contentType||JSON.stringify(node.attachments)!==JSON.stringify(prior.attachments)||JSON.stringify(node.hyperlinks)!==JSON.stringify(prior.hyperlinks)||node.sizeMode!==prior.sizeMode||node.width!==prior.width||node.height!==prior.height||node.collapsed!==prior.collapsed||JSON.stringify(node.style)!==JSON.stringify(prior.style);}).map(node=>node.id);
    if(!options?.preserveLayout)for(const id of resized){
      const node=next.nodes.find(n=>n.id===id);
      if(node&&!node.collapsed&&(nodeSizingMode(node)==='auto'||isEnclosingContainer(node)))Object.assign(next,reflowContainedChildren(next,id));
      reflowGridParents(next,id);
    }
    for(const node of next.nodes){const prior=old.get(node.id);if(!prior||node.width!==prior.width||node.height!==prior.height||node.sizeMode!==prior.sizeMode)rememberDocumentNodeSize(next,node);if(isSizeRememberingContainer(node)&&(!prior||node.width!==prior.width||node.height!==prior.height||node.sizeMode!==prior.sizeMode))rememberContainerSize(node,undefined,{preserveExisting:!!containingFixedInterior(next,node)});}
    const resizedSet=new Set(resized);
    const changed=next.nodes.filter(node=>{const prior=old.get(node.id);return !prior||resizedSet.has(node.id)||node.x!==prior.x||node.y!==prior.y||node.width!==prior.width||node.height!==prior.height||node.style?.shape!==prior.style?.shape||node.style?.radius!==prior.style?.radius||node.style?.variant!==prior.style?.variant||node.kind!==prior.kind;}).map(node=>node.id);
    if(next.links.some(link=>link.canvasId===canvasId&&link.snap?.locked)){
      const index=navigationIndex(next),checked=new Set<string>();
      for(const link of next.links.filter(link=>link.canvasId===canvasId&&link.snap?.locked)){
        const members=lockedClusterNodeIds(next,canvasId,[link.sourceId]),key=[...members].sort().join('/');if(checked.has(key))continue;checked.add(key);
        const visible=members.map(id=>index.nodes.get(id)!).filter(node=>index.isNodeVisible(node.id));
        for(let a=0;a<visible.length;a++)for(let b=a+1;b<visible.length;b++)if(!isIntentionalContainment(index,visible[a]!.id,visible[b]!.id)&&shapesOverlap(nodeShape(visible[a]!,index.bounds(visible[a]!)),nodeShape(visible[b]!,index.bounds(visible[b]!))))throw new Error('This change would overlap nodes inside a locked Cluster. Unlock their edge before changing this geometry.');
      }
    }
    const smartOwners=next.nodes.filter(node=>node.canvasId===canvasId&&node.smartLink);
    if(!options?.preserveLayout){
      let ordinaryChanges=changed;
      if(smartOwners.length){const nav=navigationIndex(next),managed=new Set<string>(),owners=new Set(smartOwners.map(node=>node.id)),pending=[...owners];while(pending.length){const id=pending.pop()!;if(managed.has(id))continue;managed.add(id);for(const child of nav.children.get(id)??[])pending.push(child.id);}ordinaryChanges=changed.filter(id=>{if(!managed.has(id))return true;if(!owners.has(id))return false;const prior=old.get(id),node=nav.nodes.get(id)!;return !prior||node.x!==prior.x||node.y!==prior.y;});}
      if(ordinaryChanges.length)Object.assign(next,resolveCanvasCollisions(next,canvasId,ordinaryChanges));
    }
    const smartChanged=smartOwners.length&&(changed.length||before.nodes.length!==next.nodes.length||next.nodes.some((node,i)=>{const prior=old.get(node.id);return node.parentId!==prior?.parentId||node.value!==prior?.value||node.id!==before.nodes[i]?.id||JSON.stringify(node.smartLink)!==JSON.stringify(prior?.smartLink);}));
    const smartLayout=smartChanged?reflowSmartLinks(next,canvasId):{movedNodeIds:[]};
    Object.assign(next,reconcileSnapLinks(next,canvasId,[...new Set([...changed,...smartLayout.movedNodeIds])],{createContacts:!options?.preserveLayout&&changed.length>0}));
    const issues=validateChange(before,next);if(issues.length)throw new Error(issues.map(i=>i.message).join('\n'));
  }
  catch(e){fail(e);return false;}
  undoStack.push(before);if(undoStack.length>100)undoStack.shift();redoStack=[];project=next;changedDraft();if(options?.richText&&inlineText?.active){/* The adopted glyph surface owns its caret until editing finishes. */}else if(options?.richText&&redrawScene)redrawScene();else render();if(message)notice(message);return true;
}
function undo(){flushRichTextEditors(true);if(!project||!undoStack.length)return;redoStack.push(cloneProject(project));project=resolveCanvasCollisions(undoStack.pop()!,canvasId);project.revision=savedRevision;project.snapshots=structuredClone(savedSnapshots);changedDraft();render();}
function redo(){flushRichTextEditors(true);if(!project||!redoStack.length)return;undoStack.push(cloneProject(project));project=resolveCanvasCollisions(redoStack.pop()!,canvasId);project.revision=savedRevision;project.snapshots=structuredClone(savedSnapshots);changedDraft();render();}
async function save(){
  commitFocusedField();
  if(!project)return;
  if(recoveryNeedsFork){notice('This recovered draft is based on an older revision. Use File → Save as to preserve both versions.',true);return;}
  if(saveFailure==='conflict'){notice('This work changed in another window. Save a separate copy or reopen the saved work; automatic saving is paused to preserve both versions.',true);return;}
  if(!dirty&&!saving){notice(`All changes are saved · r${savedRevision}.`);return;}
  if(await automaticSave.flush(true)&&!dirty)notice(`All changes are saved · r${savedRevision}.`);
}
async function commitSave():Promise<boolean>{
  if(!project||!dirty)return true;
  if(recoveryNeedsFork||saveFailure==='conflict')return false;
  const id=project.id,version=editVersion,candidate=commitSnapshots(project),expectedRevision=savedRevision;
  let refreshAfterCommit=true;
  saving=true;clearTimeout(recoveryTimer);updateSaveState();
  try{await recoveryWrites;const ownedRecoveryToken=recoveryToken;let saved:MeridianProject;if(libraryDocument){const document=await libraryStorage.save(libraryDocumentFromProject({...libraryDocument,canvasTargets:libraryTargets},candidate),expectedRevision);libraryDocument=document;saved=document.project;}else saved=await storage.save(candidate,expectedRevision);
    if(project?.id!==id)return true;savedRevision=saved.revision;savedSnapshots=structuredClone(saved.snapshots);saveFailure=null;
    if(editVersion===version){refreshAfterCommit=saveChangesScene(project,saved);project=saved;dirty=false;clearLocalRecovery();}else{refreshAfterCommit=saveChangesScene(project,{...project,snapshots:saved.snapshots});project.revision=saved.revision;project.snapshots=structuredClone(saved.snapshots);}
    try {
      const remainingRecovery=libraryDocument?await libraryStorage.recover():await storage.recover(id);
      if(!remainingRecovery){recoveryToken=null;recoveryBase=savedRevision;recoveryBlocked=false;}
      else if(ownedRecoveryToken!==null&&remainingRecovery.savedAt===ownedRecoveryToken){
        // This window's earlier recovery is superseded by its committed save. The host
        // uses both token and base revision so a newer draft can never be deleted.
        if(libraryDocument)await libraryStorage.clearRecovery(remainingRecovery);else await storage.clearRecovery(id,remainingRecovery);recoveryToken=null;recoveryBase=savedRevision;recoveryBlocked=false;
      }
      else if(remainingRecovery.savedAt===recoveryToken)recoveryBase=remainingRecovery.baseRevision;
    } catch(error) {
      // A failed cleanup does not turn an acknowledged commit into a failed save.
      // Keep the guarded token so another window's draft is never adopted.
      notice(`Changes were saved. Recovery cleanup will be checked on reopening: ${error instanceof Error?error.message:String(error)}`,true);
    }
    if(dirty&&localRecoveryKey)storeLocalRecovery();
    return true;
  }catch(e){
    if(project?.id!==id)return false;
    const conflict=e instanceof StorageError&&e.status===409;saveFailure=conflict?'conflict':'offline';automaticSave.pause();
    const fallback=storeLocalRecovery();
    notice(conflict?'This work changed in another window. Automatic saving is paused. Your draft remains open; save a separate copy or reopen the saved work.':`Automatic saving paused: ${e instanceof Error?e.message:String(e)}. ${fallback?'Your draft has a browser backup.':'Keep this window open.'} Use Save to retry.`,true);
    return false;
  }
  finally{if(project?.id===id){saving=false;if(dirty)scheduleRecovery();if(refreshAfterCommit)renderAfterSave();else updateSaveState();}}
}
async function saveCopy(duplicate=false){
  commitFocusedField();if(!project)return;const sourceId=project.id;
  const name=duplicate?`${currentCanvas()?.title??project.title} copy`:await askName('Save as',`${currentCanvas()?.title??project.title} copy`);if(!name)return;
  await automaticSave.flush();await recoveryWrites;if(!project||project.id!==sourceId)return;
  const source=cloneProject(project),at=source.canvases.findIndex(canvas=>canvas.id===canvasId);
  for(const node of source.nodes)if(libraryTargets[node.id]&&!node.canvasTarget)node.canvasTarget=structuredClone(libraryTargets[node.id]!);
  const copy=forkProject(source),active=copy.canvases[Math.max(0,at)]!;copy.title=name;copy.entryCanvasId=active.id;active.title=name;const copyRoot=copy.nodes.find(node=>node.id===active.rootId)!;const renamedRuns=rebaseTextRuns(copyRoot.title,name,copyRoot.titleRuns);copyRoot.title=name;if(renamedRuns.length)copyRoot.titleRuns=renamedRuns;else delete copyRoot.titleRuns;
  const saved=await storage.create(commitSnapshots(copy));setProject(saved,active.id);repairVisibleOverlaps();notice(duplicate?'Work duplicated.':'Independent work saved.');
}
function normalizeSmartEntry():void { if(!project||smartEntryLayouts.has(canvasId))return;reflowSmartLinks(project,canvasId);smartEntryLayouts.add(canvasId); }
function setProject(p:MeridianProject,initialCanvasId?:string,home?:LibraryCanvasDocument){const previous=project?.id;if(!restoringHistory)rememberView();if(previous!==p.id){galleryActive.clear();windowOffsets.clear();}p=normalizeRetiredBlocks(ensureNodeTags(normalizeContainerLayouts(p)));smartEntryLayouts.clear();automaticSave.reset();saving=false;saveFailure=null;localRecoveryKey=null;libraryDocument=home??null;libraryTargets=home?structuredClone(home.canvasTargets):{};canvasViews.clear();for(const canvas of p.canvases)if(savedWindowViews[`${p.id}/${canvas.id}`])canvasViews.set(canvas.id,rememberedView(canvas.id,p));selectionIds=[];selectionMode='pan';authorMode='select';linkDraft=null;creationMark=null;focusNodeId=null;focusBranch=false;clearTimeout(recoveryTimer);recoveryToken=null;recoveryNeedsFork=false;recoveryBlocked=false;recoveryBase=p.revision;project=p;savedRevision=p.revision;savedSnapshots=structuredClone(p.snapshots);canvasId=initialCanvasId&&p.canvases.some(c=>c.id===initialCanvasId)?initialCanvasId:p.entryCanvasId;selected=null;selectedLink=null;undoStack=[];redoStack=[];dirty=false;editVersion=0;view=restoringHistory||restoreBootstrapCamera?rememberedHistoryView(canvasId):rememberedView(canvasId);fitWorkspaceOnRender=!restoringHistory&&!restoreBootstrapCamera;restoreBootstrapCamera=false;runtime=true;recordCanvasAddress(!home&&!initialCanvasId?'/?project='+encodeURIComponent(p.id):workspaceAddress(),restoringHistory||!previous||previous===p.id);activeHistoryEntry=historyEntryId();render();}
function workspaceAddress(id=canvasId){return libraryDocument?id===project?.entryCanvasId?'/':'/?libraryCanvas='+encodeURIComponent(id):'/?project='+encodeURIComponent(project?.id??'')+'&canvas='+encodeURIComponent(id);}

async function flushBeforeNavigation():Promise<boolean>{
  commitFocusedField();
  if((dirty||saving)&&(!await automaticSave.flush()||dirty)){
    notice('Save these changes or use File → Save as before navigating. Your draft remains in this window.',true);return false;
  }
  return true;
}
async function navigateAddress(address:string):Promise<void>{
  if(navigationPending||restoringHistory||document.querySelector('dialog[open]'))return;navigationPending=true;
  try{if(!await flushBeforeNavigation())return;rememberView();const destination=new URL(address,location.origin);if(destination.origin!==location.origin)throw new Error('Work navigation must remain within Meridian.');if(destination.href!==location.href)location.assign(destination.href);else{focusNodeId=null;focusBranch=false;fitWorkspaceOnRender=true;render();}}
  finally{navigationPending=false;}
}
async function navigateHistory(direction:HistoryDirection):Promise<void>{
  if(navigationPending||restoringHistory||document.querySelector('dialog[open]'))return;navigationPending=true;
  try{if(!await flushBeforeNavigation())return;rememberView();direction==='back'?history.back():history.forward();}finally{navigationPending=false;}
}
async function createCheckpoint():Promise<void>{
  commitFocusedField();if(!project)return;const id=project.id,name=await askName('Name this checkpoint','Working arrangement');if(!name)return;
  await automaticSave.flush();if(!project||project.id!==id)return;
  if(libraryDocument)await libraryStorage.checkpoint(libraryDocumentFromProject({...libraryDocument,canvasTargets:libraryTargets},project),name);else await storage.checkpoint(project,name);
  notice('Checkpoint saved.');
}
async function fileCommand(command:FileCommand):Promise<void>{
  if(document.querySelector('dialog[open]'))return;commitFocusedField();
  switch(command){case 'save':await save();break;case 'save-as':await saveCopy();break;case 'checkpoint':await createCheckpoint();break;case 'duplicate':await saveCopy(true);break;case 'import':chooseImport();break;case 'history':await showHistory();break;case 'preview':await publish(true);break;case 'publish':await publish(false);break;case 'export':await exportProject();break;}
}
async function toolCommand(command:ToolCommand):Promise<void>{
  if(document.querySelector('dialog[open]'))return;commitFocusedField();
  if(command==='modules')await showModules();else if(command==='composition')showCompositionTools();
}

async function leaveCurrent(){
  commitFocusedField();
  
  if(!dirty&&!saving)return true;
  if(!saveFailure&&!recoveryNeedsFork&&await automaticSave.flush()&&!dirty)return true;
  const ok=await confirmAction('Leave this draft?','Saving is paused. Keep a recovery draft before returning to the library? The saved work will remain unchanged.','Keep draft and leave');
  if(ok&&project){clearTimeout(recoveryTimer);automaticSave.pause();try{await storeRecovery(cloneProject(project),recoveryRevision());}catch{if(!storeLocalRecovery())return false;}}
  return ok;
}
async function openProject(id:string,targetCanvasId?:string){
  if(!await leaveCurrent())return;const [loaded]=await Promise.all([storage.load(id),readTierLibrary()]);setProject(loaded,targetCanvasId);rememberTierAncestry();
  const hostRecovery=await storage.recover(id),local=readLocalRecovery(id);
  const localIsOwn=local?.key===localDraftKey(id),useLocal=!!local&&(localIsOwn||!hostRecovery);
  const recovery=useLocal?local!.recovery:hostRecovery;
  if(!recovery){repairVisibleOverlaps();return;}
  if(!useLocal){recoveryToken=recovery.savedAt;recoveryBase=recovery.baseRevision;}
  const use=await confirmAction('Recovery draft available',`A ${useLocal?'browser backup':'draft'} from ${new Date(recovery.savedAt).toLocaleString()} is available. Restore it into the editor? Older-base drafts remain paused and can be saved as a separate copy.`,'Restore draft');
  if(use){
    recoveryNeedsFork=recovery.baseRevision!==savedRevision;recoveryBase=recovery.baseRevision;project=normalizeRetiredBlocks(ensureNodeTags(recovery.project));smartEntryLayouts.clear();project.revision=savedRevision;project.snapshots=structuredClone(savedSnapshots);
    if(useLocal)localRecoveryKey=localDraftKey(id);
    if(!project.canvases.some(c=>c.id===canvasId))canvasId=project.entryCanvasId;
    if(recoveryNeedsFork){saveFailure='conflict';automaticSave.pause();}
    changedDraft();render();notice(recoveryNeedsFork?'Recovered an older-base draft. Save a separate copy to preserve newer saved content.':'Recovery draft restored. Changes will save automatically.');
  }else if(!useLocal){await storage.clearRecovery(id,recovery);recoveryToken=null;}
  else if(localIsOwn){localRecoveryKey=local!.key;clearLocalRecovery();}
  repairVisibleOverlaps();
}
async function showLibrary(initialCanvasId?:string){
  if(!await leaveCurrent())return;
  const [stored,catalog,recovery]=await Promise.all([libraryStorage.load(),libraryStorage.catalog(),libraryStorage.recover()]);
  rememberCanvasCatalog(catalog);
  const normalized=normalizeLibraryDocument(stored,catalog),document=reconcileLibraryCanvas(normalized,catalog);
  tierLibrary=document;tierMemo=undefined;setProject(document.project,initialCanvasId,document);rememberTierAncestry();
  // Discovery stays provisional until recovery is reviewed. Saving it while the
  // prompt is open would make a same-base draft stale before the user restores it.
  const local=readLocalRecovery(document.project.id);
  const backup=local?.key===localDraftKey(document.project.id)?local.recovery:recovery??local?.recovery;
  if(backup){
    const useHost=!!recovery&&backup===recovery;
    const restore=await confirmAction('Recovery draft available','An unsaved library work draft is available. Restore it? Older drafts remain paused to preserve the newer saved work.','Restore draft');
    if(restore){
      // The host draft owns its complete discovery history. Reconcile that
      // envelope, rather than marking fresh catalog entries seen in an older draft.
      const restored=reconcileLibraryCanvas(useHost?normalizeLibraryDocument(recovery!.library,catalog):libraryDocumentFromProject(normalized,backup.project),catalog);
      project=normalizeRetiredBlocks(ensureNodeTags(cloneProject(restored.project)));smartEntryLayouts.clear();project.revision=savedRevision;project.snapshots=structuredClone(savedSnapshots);
      libraryDocument=libraryDocumentFromProject(restored,project);libraryTargets=structuredClone(libraryDocument.canvasTargets);
      recoveryBase=backup.baseRevision;recoveryNeedsFork=backup.baseRevision!==savedRevision;
      if(useHost)recoveryToken=recovery!.savedAt;else localRecoveryKey=localDraftKey(project.id);
      if(recoveryNeedsFork){saveFailure='conflict';automaticSave.pause();}
      changedDraft();render();repairVisibleOverlaps();return;
    }
    // Only discard the backup the prompt displayed; other windows retain theirs.
    if(useHost)await libraryStorage.clearRecovery(recovery!);
    else if(local?.key===localDraftKey(document.project.id)){localRecoveryKey=local.key;clearLocalRecovery();}
  }
  if(stored.schemaVersion===1||JSON.stringify(document)!==JSON.stringify(stored))changedDraft();
  repairVisibleOverlaps();
}

function repairVisibleOverlaps(){if(!project)return;try{const repaired=resolveCanvasCollisions(project,canvasId);if(repaired!==project)edit(next=>Object.assign(next,repaired),'Separated overlapping tiles to prevent collisions.');}catch(error){fail(error);}}

function chooseImport(){const i=element('input');i.type='file';i.accept='.meridian,.html,.htm';i.onchange=()=>{if(i.files?.[0])void importFile(i.files[0]).catch(fail);};i.click();}
async function importFile(file:File){await importFileData(file);}

async function importFileData(file:File){if(!await leaveCurrent())return;let p:MeridianProject;const published=/\.html?$/i.test(file.name);if(published){p=readPublication(await file.text());p.title=`${p.title} · imported`;}else{p=await storage.importFile(file);const existing=(await storage.list()).find(item=>item.id===p.id);if(existing){const asCopy=await confirmAction('This work is already in your library','Create an independent copy from this file? Cancel keeps the existing work unchanged.','Import a copy');if(!asCopy)return;p=forkProject(p);p.title+= ' · copy';}else p.revision=0;}const issues=validateProject(p);if(issues.length)throw new Error(`Cannot import: ${issues.map(i=>i.message).join('; ')}`);const saved=await storage.create(p);setProject(saved);repairVisibleOverlaps();notice(published?'Imported as an independent editable work.':'Work file opened with its content and saved snapshots intact.');}
function revealNode(id:string,focus=false,recordHistory=true){
  if(!project)return;const navigation=navigationIndex(project), n=navigation.nodes.get(id);if(!n)return;
  const oldCanvas=canvasId;rememberView();canvasId=n.canvasId;view={...(canvasViews.get(canvasId)??rememberedView(canvasId))};selected=id;selectionIds=[];selectedLink=null;panelOpen=true;
  if(oldCanvas!==canvasId&&recordHistory){recordCanvasAddress(workspaceAddress(),false);activeHistoryEntry=historyEntryId();}
  const ancestors=new Set(navigation.ancestors(id).map(parent=>parent.id));
  activateGalleryPath(id);
  const hidden=project.nodes.some(node=>ancestors.has(node.id)&&node.collapsed), bounds=navigation.bounds(navigation.visibleNode(id)??n), stage=document.querySelector<HTMLElement>('.canvas-stage');
  const onscreen=stage&&bounds.x*view.scale+view.x<stage.clientWidth&&(bounds.x+bounds.width)*view.scale+view.x>0&&bounds.y*view.scale+view.y<stage.clientHeight&&(bounds.y+bounds.height)*view.scale+view.y>0;
  if(n.kind==='canvas'&&oldCanvas!==canvasId&&!restoringHistory){fitWorkspaceOnRender=true;focusNodeId=null;focusBranch=false;}
  else if(focus||hidden||oldCanvas!==canvasId||!onscreen){focusNodeId=id;focusBranch=n.kind==='canvas';}
  if(hidden)edit(p=>{for(const ancestor of navigation.ancestors(id).reverse()){if(ancestor.kind==='canvas')continue;Object.assign(p,revealChildren(p,ancestor.id,true));}reflowGridParents(p,id);});else{repairVisibleOverlaps();render();}
}
function setPanelOpen(open:boolean){panelOpen=open;try{localStorage.setItem('meridian.inspector-open',String(open));}catch{}render();}
function inspectorDivider(shell:HTMLElement){const divider=element('div','inspector-divider');divider.setAttribute('role','separator');divider.setAttribute('aria-label','Resize side panel');divider.setAttribute('aria-orientation','vertical');divider.tabIndex=0;const update=(width:number)=>{inspectorWidth=Math.max(260,Math.min(520,width));shell.style.setProperty('--inspector-width',inspectorWidth+'px');divider.setAttribute('aria-valuenow',String(inspectorWidth));try{localStorage.setItem('meridian.inspector-width',String(inspectorWidth));}catch{}};divider.setAttribute('aria-valuemin','260');divider.setAttribute('aria-valuemax','520');update(inspectorWidth);divider.onpointerdown=event=>{if(event.button!==0)return;event.preventDefault();divider.setPointerCapture(event.pointerId);divider.onpointermove=move=>update(move.clientX-shell.getBoundingClientRect().left);const finish=()=>{divider.onpointermove=null;divider.onpointerup=null;divider.onpointercancel=null;if(divider.hasPointerCapture(event.pointerId))divider.releasePointerCapture(event.pointerId);render();};divider.onpointerup=finish;divider.onpointercancel=finish;};divider.ondblclick=()=>{update(310);render();};divider.onkeydown=event=>{if(event.key==='ArrowLeft'||event.key==='ArrowRight'){event.preventDefault();update(inspectorWidth+(event.key==='ArrowRight'?20:-20));}else if(event.key==='Home'){event.preventDefault();update(310);}};return divider;}
function nodeSelected(id:string|null){if(!project)return;selectionIds=[];selected=id;selectedLink=null;if(id){panelOpen=true;if(inspectorPanel==='canvas'){inspectorPanel='properties';saveInspectorPanel(inspectorPanel);}}render();}

function operationSelect(value:Operation,change:(value:Operation)=>void){const control=element('select');for(const family of ['Boolean','Arithmetic','Comparison','Routing']){const group=element('optgroup');group.label=family;for(const definition of operations.filter(o=>o.family===family)){const option=element('option','',`${definition.symbol}  ${definition.label}`);option.value=definition.operation;group.append(option);}control.append(group);}control.value=value;control.onchange=()=>change(control.value as Operation);return control;}
function insertJunction(id:string,utilityVariant:UtilityVariant='joint'){
  if(!project)return;
  edit(next=>{
    const link=next.links.find(item=>item.id===id)!,index=navigationIndex(next),a=index.bounds(index.nodes.get(link.sourceId)!),b=index.bounds(index.nodes.get(link.targetId)!);
    const route=[{x:a.x+a.width/2,y:a.y+a.height/2},...(link.points??[]),{x:b.x+b.width/2,y:b.y+b.height/2}],middle=Math.floor((route.length-1)/2),from=route[middle]!,to=route[middle+1]!;
    const point=selectedLinkPoint??{x:(from.x+to.x)/2,y:(from.y+to.y)/2};
    const result=splitLinkWithJunction(next,id,point,{utilityVariant,segmentIndex:selectedLinkSegment??middle});
    Object.assign(next,result.project);selected=result.nodeId;selectionIds=[selected];selectedLink=null;selectedLinkPoint=undefined;selectedLinkSegment=undefined;panelOpen=true;inspectorPanel='properties';saveInspectorPanel(inspectorPanel);
  });
}
function selectLink(id:string,position?:Point,segmentIndex?:number){if(!project)return;linkContextNode=selected??project.links.find(link=>link.id===id)?.targetId??null;inspectorPanel='links';saveInspectorPanel(inspectorPanel);panelOpen=true;selectedLink=id;selectedLinkPoint=position;selectedLinkSegment=segmentIndex;selected=null;render();}

function reflowGridParents(p:MeridianProject,id:string){let parentId=p.nodes.find(n=>n.id===id)?.parentId;const seen=new Set<string>();while(parentId&&!seen.has(parentId)){seen.add(parentId);const parent=p.nodes.find(n=>n.id===parentId);if(!parent)break;if(!isGallery(parent)&&!parent.collapsed)Object.assign(p,reflowContainedChildren(p,parent.id));parentId=parent.parentId;}}
function toggleChildren(id:string,expanded:boolean){edit(p=>{Object.assign(p,revealDocumentNode(p,id,expanded));reflowGridParents(p,id);});}
function activateGalleryPath(id:string){if(!project)return;let child=id;for(const parent of navigationIndex(project).ancestors(id)){if(isGallery(parent))galleryActive.set(parent.id,child);child=parent.id;}}
function selectChild(id:string){activateGalleryPath(id);selectNodes([id]);}
function selectOutlineNode(id:string){
  if(!project?.nodes.some(node=>node.id===id&&node.canvasId===canvasId))return;
  focusNodeId=null;focusBranch=false;selectNodes([id]);
}
function inspectGalleryItem(id:string){commitFocusedField();activateGalleryPath(id);selectNodes([id]);}
function pageGallery(parentId:string,itemId:string){commitFocusedField();if(!project)return;galleryActive.set(parentId,itemId);if(selected&&navigationIndex(project).ancestors(selected).some(parent=>parent.id===parentId)){selected=itemId;selectionIds=[itemId];}render();}
function parentRelationship(p:MeridianProject,n:MeridianNode){return p.links.find(l=>l.kind==='visual'&&(l.sourceId===n.parentId&&l.targetId===n.id||l.targetId===n.parentId&&l.sourceId===n.id));}
function setParentRelationship(id:string,relation:string){edit(p=>{const child=p.nodes.find(n=>n.id===id)!;if(!child.parentId)return;const existing=parentRelationship(p,child);if(!relation){p.links=p.links.filter(l=>!(l.kind==='visual'&&(l.sourceId===child.parentId&&l.targetId===id||l.targetId===child.parentId&&l.sourceId===id)));return;}if(existing){existing.relation=relation as MeridianLink['relation'];delete existing.direction;}else p.links.push({id:crypto.randomUUID(),canvasId:child.canvasId,sourceId:child.parentId,targetId:id,kind:'visual',relation:relation as MeridianLink['relation']});});}
function moveNode(id:string,x:number,y:number){if(!project)return;const node=project.nodes.find(node=>node.id===id);if(node)moveSelected([id],x-node.x,y-node.y);}
function resizeNode(id:string,width:number,height:number,x?:number,y?:number){
  edit(p=>{
    const node=p.nodes.find(n=>n.id===id)!;
    const index=navigationIndex(p,{galleryItems:Object.fromEntries(galleryActive)}),bounds=index.bounds(node),branch=descendants(p,id);
    const canTranslate=canMoveNodeBranches(p,[id]);
    const requested=constrainChildGeometry(p,node,{x:canTranslate?(x??node.x):node.x,y:canTranslate?(y??node.y):node.y,width,height},{context:{galleryItems:Object.fromEntries(galleryActive)}});
    const obstacles=p.nodes.filter(other=>other.canvasId===canvasId&&other.kind!=='canvas'&&other.id!==id&&index.isNodeVisible(other.id)&&!isIntentionalContainment(index,id,other.id)).flatMap(other=>collisionNodeShapes(p,other,index.bounds(other)));
    const geometry=collisionFreeResize(nodeShape(node,bounds),proposal=>obstacles.map(obstacle=>branch.has(obstacle.id!)?{...obstacle,x:obstacle.x+proposal.x-bounds.x,y:obstacle.y+proposal.y-bounds.y}:obstacle),{...nodeShape(node,bounds),...requested});
    const dx=geometry.x-node.x,dy=geometry.y-node.y,ids=descendants(p,id);
    Object.assign(node,{width:geometry.width,height:geometry.height,sizeMode:'fixed'});
    rememberContainerSize(node);
    rememberDocumentNodeSize(p,node,{authored:true});
    if(dx||dy){
      for(const child of p.nodes)if(ids.has(child.id)){child.x+=dx;child.y+=dy;}
      for(const link of p.links)if(ids.has(link.sourceId)&&ids.has(link.targetId)&&link.points)link.points=link.points.map(point=>({x:point.x+dx,y:point.y+dy}));
    }
  });
}
function deleteSelection(){if(!project)return;if(selectedLink){const id=selectedLink;selectedLink=null;edit(p=>{p.links=p.links.filter(l=>l.id!==id);});return;}const ids=currentSelection().filter(id=>project!.nodes.find(n=>n.id===id)?.kind!=='canvas');if(!ids.length)return;edit(p=>{Object.assign(p,deleteSelectionGraph(p,ids));selected=null;selectionIds=[];});}
function openAsset(id:string){if(!project)return;const a=getAsset(project,id);if(!a){notice('This attachment is not included in this work.',true);return;}if(documentPreviewKind(a)||/^video\/(mp4|webm|ogg)$/i.test(a.mime)){openAssetPreview(project,id);return;}const bytes=Uint8Array.from(atob(a.base64),c=>c.charCodeAt(0)),blob=new Blob([bytes],{type:a.mime});const url=URL.createObjectURL(blob);const m=modal(a.name);m.dialog.classList.add('document-modal');m.dialog.addEventListener('close',()=>URL.revokeObjectURL(url));if(a.mime==='text/plain'||a.mime==='text/markdown'||/\.(txt|md)$/i.test(a.name)){const pre=element('pre','document-text',new TextDecoder().decode(bytes));m.body.append(pre);}else if(['image/png','image/jpeg','image/webp'].includes(a.mime)){const img=element('img','document-image');img.src=url;img.alt=a.name;m.body.append(img);}else if(a.mime==='application/pdf'){const frame=element('iframe','document-frame');frame.src=url;frame.title=a.name;m.body.append(frame,element('p','muted','If your browser cannot preview this PDF, open or download the original.'));}else m.body.append(element('p','','This file is preserved in full. Download it to open in its usual application.'));
  const link=element('a','button secondary','Download original');link.href=url;link.download=a.name;m.footer.append(link,button('Close',m.close,'primary'));
}
function openInspectorSearch(){commitFocusedField();inspectorPanel='canvas';saveInspectorPanel(inspectorPanel);panelOpen=true;render();const input=document.querySelector<HTMLInputElement>('[aria-label="Search Work nodes"]');input?.focus();input?.select();}
function inspectCanvasContents(){const root=currentCanvas()?.rootId;if(root)selectChild(root);}
function changeAuthorMode(mode:WorkspaceMode){
  commitFocusedField();inlineText?.finish(false);linkDraft=null;
  if(mode==='module'){
    if(authorMode!=='module'&&!Object.keys(moduleSelection.overrides).length){moduleSelection=emptyModuleSelection();for(const id of currentSelection())moduleSelection.overrides[id]=true;}
    inspectorPanel='module';saveInspectorPanel(inspectorPanel);panelOpen=true;
  }
  authorMode=mode;if(mode==='select'||mode==='hand'||mode==='text')selectionMode=mode==='hand'?'region':'pan';render();
}
/** Completing an action always restores the default interaction, not the prior tool. */
function finishCanvasTool(redraw=true){authorMode='select';selectionMode='pan';linkDraft=null;if(redraw)render();}
function resetCanvasView(){commitFocusedField();focusNodeId=null;focusBranch=false;fitWorkspaceOnRender=true;render();}
function dismissCanvasMenus(){document.querySelectorAll<HTMLElement>('.workspace-menu').forEach(menu=>{if(menu.matches(':popover-open'))menu.hidePopover();menu.remove();});}
function toggleCreationMenu(){
  const creating=authorMode==='node'||authorMode==='link'||authorMode==='smart-link'||authorMode==='module',open=!!document.querySelector('[data-workspace-create-menu]:popover-open');
  dismissCanvasMenus();
  if(creating||open){changeAuthorMode('select');return;}
  const stage=document.querySelector<HTMLElement>('.canvas-stage')?.getBoundingClientRect();
  workspaceCreationMenu(changeAuthorMode,canvasPointer??{x:stage?stage.x+stage.width/2:innerWidth/2,y:stage?stage.y+stage.height/2:innerHeight/2});
}
async function createAt(target:SceneAuthorTarget,initialType?:Parameters<typeof createNodeDialog>[1]['initialType'],initialUtilityVariant:UtilityVariant='pivot'){
  if(!project||document.querySelector('dialog[open]'))return;
  linkDraft=null;
  const route=target.linkId?project.links.find(link=>link.id===target.linkId):undefined;
  const smartRail=target.nodeId?project.nodes.find(node=>node.id===target.nodeId&&node.smartLink):undefined;
  const smartIndex=smartRail?smartLinkInsertionIndex(project,smartRail.id,target.point):undefined;
  const source=project.id,version=editVersion,sourceCanvas=canvasId,parentId=route?navigationIndex(project).linkParent(route)?.id??currentCanvas()!.rootId:target.nodeId??currentCanvas()!.rootId;
  creationMark=target.point;render();
  try{
    const spec=await createNodeDialog(cloneProject(project),{canvasId:sourceCanvas,parentId,x:target.point.x,y:target.point.y,initialType:route?'utility':initialType,initialUtilityVariant,utilityOnly:!!route});
    if(!spec)return;
    if(!project||project.id!==source||canvasId!==sourceCanvas||editVersion!==version)throw new Error('The work changed while this form was open. Place the node again to use the current work.');
    if(route){
      const insertion=splitLinkWithJunction(project,route.id,target.point,{utilityVariant:spec.utilityVariant??'pivot',segmentIndex:target.segmentIndex,width:spec.width,height:spec.height,style:spec.style,title:spec.title});
      if(edit(next=>{Object.assign(next,insertion.project);selected=insertion.nodeId;selectionIds=[selected];selectedLink=null;panelOpen=true;inspectorPanel='properties';saveInspectorPanel(inspectorPanel);}))finishCanvasTool(false);
      return;
    }
    const placed=spec.type==='utility'||spec.type==='junction'?{...spec,x:target.point.x-spec.width/2,y:target.point.y-spec.height/2}:spec;
    const result=createNodeFromSpec(project,placed);
    if(edit(p=>{Object.assign(p,result.project);const parent=p.nodes.find(n=>n.id===parentId)!,node=p.nodes.find(n=>n.id===result.nodeId)!;parent.collapsed=false;if(smartRail)addSmartLinkMember(p,smartRail.id,node.id,smartIndex);else if(!isGallery(parent)&&parent.grid)Object.assign(p,arrangeChildren(p,parent.id,'grid').project);else if(node.placement==='edge'&&parent.kind!=='canvas')Object.assign(p,revealChildren(p,parent.id,true,{preserveValidPositions:true}));reflowGridParents(p,node.id);selected=result.nodeId;selectionIds=[result.nodeId];selectedLink=null;panelOpen=true;if(isGallery(parent))galleryActive.set(parent.id,node.id);}))finishCanvasTool(false);
  }finally{creationMark=null;render();}
}
async function createConnection(target:SceneAuthorTarget){
  if(!project||document.querySelector('dialog[open]'))return;
  const source=project.id,version=editVersion,sourceCanvas=canvasId;
  const route=target.linkId?project.links.find(link=>link.id===target.linkId):undefined;
  const junction=route?.kind==='visual'?splitLinkWithJunction(project,route.id,target.point):undefined;
  const spec=await createLinkDialog(junction?.project??cloneProject(project),{canvasId:sourceCanvas,sourceId:target.nodeId??(junction?selected??undefined:undefined),targetId:junction?.nodeId});
  if(!spec)return;
  if(!project||project.id!==source||canvasId!==sourceCanvas||editVersion!==version)throw new Error('The work changed while this form was open. Create the link again using the current nodes.');
  // An acknowledged save may refresh revisions and observations while the form
  // is open without changing authored content or editVersion. Retain that
  // baseline when committing the prepared route split.
  const base=junction&&(spec.targetId===junction.nodeId||spec.sourceId===junction.nodeId)?{...junction.project,revision:project.revision,snapshots:structuredClone(project.snapshots)}:project;
  const result=createLinkFromSpec(base,spec);if(edit(p=>{Object.assign(p,result.project);selected=null;selectionIds=[];selectedLink=result.linkId;panelOpen=true;}))finishCanvasTool();
}
/** A pending path is presentation only until its destination commits one graph edit. */
function traceConnection(target:SceneAuthorTarget){
  if(!project||document.querySelector('dialog[open]'))return;
  const clicked=target.nodeId?project.nodes.find(node=>node.id===target.nodeId):undefined;
  if(!linkDraft){
    if(!clicked||clicked.kind==='canvas'){notice('Click a node to begin a link.');return;}
    linkDraft={projectId:project.id,canvasId,sourceId:clicked.id,points:[],directions:[]};
    selected=clicked.id;selectionIds=[clicked.id];selectedLink=null;render();return;
  }
  if(linkDraft.projectId!==project.id||linkDraft.canvasId!==canvasId){linkDraft=null;render();return;}
  const index=navigationIndex(project,{galleryItems:Object.fromEntries(galleryActive)});
  const bounds=(node:MeridianNode)=>displayNodeBounds(index,node,windowOffsets);
  const sourceBounds=bounds(index.nodes.get(linkDraft.sourceId)!);
  const origin=linkDraft.points.at(-1)??{x:sourceBounds.x+sourceBounds.width/2,y:sourceBounds.y+sourceBounds.height/2};
  if(!clicked&&!target.linkId){const obstacles=[...linkPivotObstacles(project,canvasId,linkDraft.sourceId,undefined,bounds),...draftPivotObstacles(linkDraft.points)];const direction=target.shiftKey?(Math.abs(target.point.x-origin.x)>=Math.abs(target.point.y-origin.y)?'x':'y'):undefined;linkDraft.points.push(target.shiftKey?axisLockedPoint(origin,target.point,obstacles,direction):{...target.point});linkDraft.directions.push(direction);render();return;}
  if(clicked?.id===linkDraft.sourceId){notice('Choose a different destination, or press Escape to cancel the path.');return;}
  const draft=linkDraft;
  // Clicking an existing line joins the new path at an explicit utility node.
  const existing=target.linkId?project.links.find(link=>link.id===target.linkId):undefined;
  const existingScroll=existing?routeWindowOffset(index,existing.sourceId,existing.targetId,windowOffsets):{x:0,y:0};
  const insertion=target.linkId?splitLinkWithJunction(project,target.linkId,{x:target.point.x+existingScroll.x,y:target.point.y+existingScroll.y},{utilityVariant:'joint',segmentIndex:target.segmentIndex}):undefined;
  const destination=clicked?.id??insertion?.nodeId;
  if(!destination)return;
  const base=insertion?.project??project,finishedIndex=navigationIndex(base,{galleryItems:Object.fromEntries(galleryActive)});
  const finishedBounds=(node:MeridianNode)=>displayNodeBounds(finishedIndex,node,windowOffsets);
  const destinationNode=finishedIndex.nodes.get(destination)!,destinationBounds=finishedBounds(destinationNode);
  const destinationCenter={x:destinationBounds.x+destinationBounds.width/2,y:destinationBounds.y+destinationBounds.height/2};
  const obstacles=linkPivotObstacles(base,canvasId,draft.sourceId,destination,finishedBounds);
  const resolved=constrainDraftRoute({x:sourceBounds.x+sourceBounds.width/2,y:sourceBounds.y+sourceBounds.height/2},draft.points,draft.directions,obstacles);
  const finishOrigin=resolved.at(-1)??{x:sourceBounds.x+sourceBounds.width/2,y:sourceBounds.y+sourceBounds.height/2};
  const displayedPoints=target.shiftKey?[...resolved,...orthogonalFinish(finishOrigin,destinationCenter,[...obstacles,...draftPivotObstacles(resolved)])]:resolved;
  const scroll=routeWindowOffset(finishedIndex,draft.sourceId,destination,windowOffsets);
  const points=displayedPoints.map(point=>({x:point.x+scroll.x,y:point.y+scroll.y}));
  const result=createLinkPath(base,{canvasId,sourceId:draft.sourceId,targetId:destination,kind:'visual',relation:'generic',direction:'none',style:{color:'#78ddc1',width:2,lineStyle:'solid'}},points);
  if(edit(next=>{Object.assign(next,result.project);linkContextNode=draft.sourceId;selected=null;selectionIds=[];selectedLink=result.linkId;panelOpen=true;inspectorPanel='links';saveInspectorPanel(inspectorPanel);}))finishCanvasTool();
}
function removeAt(target:SceneAuthorTarget){
  if(!project)return;
  let removed=false;
  if(target.linkId&&project.links.some(link=>link.id===target.linkId))removed=edit(p=>{p.links=p.links.filter(link=>link.id!==target.linkId);if(selectedLink===target.linkId)selectedLink=null;});
  else if(target.nodeId&&target.nodeId!==currentCanvas()?.rootId&&project.nodes.some(node=>node.id===target.nodeId))removed=edit(p=>{Object.assign(p,deleteSelectionGraph(p,[target.nodeId!]));if(selected&&!p.nodes.some(node=>node.id===selected)){selected=null;selectionIds=[];}if(selectedLink&&!p.links.some(link=>link.id===selectedLink))selectedLink=null;});
  if(removed&&authorMode==='delete')finishCanvasTool();
}
function createSmartAt(target:SceneAuthorTarget):void {
  if(!project)return;
  const clicked=target.nodeId?project.nodes.find(node=>node.id===target.nodeId):undefined;
  const parentId=clicked&&!isEnclosingContainer(clicked)&&!isOrderedContainer(clicked)&&!isGallery(clicked)?clicked.id:currentCanvas()!.rootId;
  const bounds=clicked?navigationIndex(project).bounds(clicked):undefined,point=bounds?{x:bounds.x+bounds.width+80,y:bounds.y+bounds.height/2}:target.point;
  if(edit(next=>{const rail=createSmartLink(next,canvasId,point,{parentId});const parent=next.nodes.find(node=>node.id===parentId)!;parent.collapsed=false;selected=rail.id;selectionIds=[rail.id];selectedLink=null;panelOpen=true;inspectorPanel='properties';saveInspectorPanel(inspectorPanel);},'Smart Link placed. Choose Create Node, then click its line to add a member.'))finishCanvasTool();
}
function smartLinkFromSelection(ids:string[]):void {
  if(!project)return;const graph=selectionGraph(project,ids),roots=graph.rootIds.map(id=>project!.nodes.find(node=>node.id===id)!).filter(node=>node.kind!=='canvas');
  if(!roots.length||new Set(roots.map(node=>node.parentId)).size!==1){notice('Choose branches with the same parent for a Smart Link.',true);return;}
  const owner=project.nodes.find(node=>node.id===roots[0]!.parentId);
  if(owner&&isOrderedContainer(owner)){notice('Grid and Window cells keep their allocated layout. Add their parent as a Smart Link member instead.',true);return;}
  roots.sort((a,b)=>a.y-b.y||a.x-b.x);const navigation=navigationIndex(project),frames=roots.map(node=>navigation.bounds(node));
  const at={x:Math.min(...frames.map(box=>box.x))-96,y:(Math.min(...frames.map(box=>box.y))+Math.max(...frames.map(box=>box.y+box.height)))/2};
  edit(next=>{const rail=createSmartLink(next,canvasId,at,{parentId:roots[0]!.parentId!,direction:'down'});for(const node of roots)addSmartLinkMember(next,rail.id,node.id);selected=rail.id;selectionIds=[rail.id];selectedLink=null;panelOpen=true;inspectorPanel='properties';saveInspectorPanel(inspectorPanel);},'The selected branches now use a Smart Link. Undo restores their previous grouping.');
}
function authorClick(target:SceneAuthorTarget){if(authorMode==='node')void createAt(target).catch(fail);else if(authorMode==='smart-link'){try{createSmartAt(target);}catch(error){fail(error);}}else if(authorMode==='link'){try{traceConnection(target);}catch(error){fail(error);}}else if(authorMode==='delete')removeAt(target);}
function contextActions(target:SceneAuthorTarget){
  const node=project?.nodes.find(n=>n.id===target.nodeId),link=project?.links.find(l=>l.id===target.linkId);
  const actions:WorkspaceAction[]=[{label:node?'Create Child Node':'Create Node',run:()=>void createAt(target).catch(fail)},{label:'Create Link',run:()=>void createConnection(target).catch(fail)},{label:'Place Smart Link',run:()=>createSmartAt(target)}];
  if(currentSelection().length>1)actions.push({label:'Create Smart Link from selection',run:()=>smartLinkFromSelection(currentSelection())});
  if(node){actions.push({label:'Inspect Node',run:()=>selectChild(node.id)});if(documentNodeAsset(project!,node))actions.push({label:documentNodeExpanded(node)?'Collapse document':'Read document',run:()=>toggleChildren(node.id,!documentNodeExpanded(node))});if(isSizeRememberingContainer(node)||project?.nodes.some(n=>n.parentId===node.id)||project?.links.some(l=>navigationIndex(project!).linkParent(l)?.id===node.id))actions.push({label:node.cluster?node.collapsed?'Expand Cluster':'Collapse Cluster':node.collapsed?'Expand Contents':'Collapse Contents',run:()=>toggleChildren(node.id,!!node.collapsed)});if(node.kind==='canvas-link')actions.push({label:'Open Work',run:()=>void openCanvasNode(node.id).catch(fail)});}
  else if(link)actions.push({label:'Create Pivot',run:()=>void createAt(target,'utility','pivot').catch(fail)},{label:'Create Joint',run:()=>void createAt(target,'utility','joint').catch(fail)},{label:'Inspect Link',run:()=>selectLink(link.id,target.point,target.segmentIndex)});
  else actions.push({label:'Inspect Work',run:inspectCanvasContents});
  if(node&&node.kind!=='canvas')actions.push({label:node.positionLocked?'Unlock Position':'Lock Position',run:()=>{commitFocusedField();edit(next=>Object.assign(next,setNodePositionLocked(next,node.id,!node.positionLocked)));}});
  if(project&&currentSelection().length>1){const graph=selectionGraph(project,currentSelection()),parents=new Set(graph.rootIds.map(id=>project!.nodes.find(node=>node.id===id)?.parentId));actions.push({label:'Create Cluster',run:createSelectedCluster,disabled:graph.rootIds.length<2||parents.size!==1},{label:'Group selected nodes',run:()=>void groupSelected().catch(fail),disabled:graph.rootIds.length<2||parents.size!==1},{label:'Arrange selected nodes',run:arrangeSelected,disabled:graph.rootIds.length<2});}
  if(selectionMode==='region')actions.push({label:regionIntersection==='contained'?'Include touching tiles':'Include only fully enclosed tiles',run:()=>{regionIntersection=regionIntersection==='contained'?'intersecting':'contained';render();}});
  workspaceMenu([...actions,{label:'Delete',run:()=>node||link?removeAt(target):deleteSelection(),disabled:node?.id===currentCanvas()?.rootId||!(node||link||currentSelection().length||selectedLink)},{label:'Single select',run:()=>changeAuthorMode('select')},{label:'Select Area',run:()=>changeAuthorMode('hand')}],{x:target.clientX,y:target.clientY});
}
async function openCanvasNode(id:string){
  if(!project)return;const appearance=project.nodes.find(n=>n.id===id);if(!appearance)return;const node=appearance.kind==='reference'?resolveReferenceTarget(project,appearance)?.node:appearance;if(!node)return;
  const external=node.canvasTarget??(!node.targetId?libraryTargets[id]:undefined),target=project.canvases.find(c=>c.id===node.targetId||c.rootId===node.targetId);
  if(!external&&!target){notice('This work workspace is not included.',true);return;}
  const address=external?`/?project=${encodeURIComponent(external.projectId)}${external.canvasId?'&canvas='+encodeURIComponent(external.canvasId):''}`:workspaceAddress(target!.id);
  const tier=currentTiers().nodeTier(project.id,appearance.id)??1;
  if(external)rememberTierAncestry({projectId:external.projectId,canvasId:external.canvasId,tier});
  else rememberTierAncestry({projectId:project.id,canvasId:target!.id,tier});
  await navigateAddress(address);
}
async function openLocalCanvas(id:string){const node=project?.nodes.find(n=>n.kind==='canvas-link'&&project!.canvases.find(c=>c.id===id)?.rootId===n.targetId);if(node)await openCanvasNode(node.id);}
function render(){
  if(rendering)return;
  rendering=true;
  try{inlineText?.dispose();inlineText=undefined;renderWorkspace();}finally{rendering=false;}
}
function renderWorkspace(){clearTimeout(afterSaveRenderTimer);afterSaveRenderTimer=undefined;if(!project)return;normalizeSelection();normalizeSmartEntry();const p=project;document.title=`${displayWorkTitle(p)} · Atlas Meridian`;const activeCard=(document.activeElement as HTMLElement)?.closest<HTMLElement>('.ms-node')?.dataset.nodeId;const activeViewport=(document.activeElement as HTMLElement)?.classList.contains('ms-viewport');const richFocus=captureRichTextFocus(app);const inspectionFocus=inspectorState.capture(app.querySelector<HTMLElement>('.inspector'));const hubFocus=hubState.capture(app.querySelector<HTMLElement>('.node-hub'));mountedNodeHub?.dispose();mountedNodeHub=undefined;const retainedScene=app.querySelector<HTMLElement>('#scene');if(retainedScene)prepareSceneMediaForRemount(retainedScene);app.replaceChildren();const shell=element('div',`workspace is-node-hub-layout ${panelOpen?'':'is-inspector-closed'}`);shell.style.setProperty('--inspector-width',inspectorWidth+'px');
  const moduleScope=`${p.id}/${canvasId}`;if(moduleSelectionScope!==moduleScope){moduleSelectionScope=moduleScope;moduleSelection=emptyModuleSelection();}
  if(!hasNativeChrome())shell.append(applicationMenu(command=>void fileCommand(command).catch(fail),direction=>void navigateHistory(direction).catch(fail),commitFocusedField,command=>void toolCommand(command).catch(fail)));
  const header=element('header','app-header');const brand=button('',()=>navigateAddress('/'),'brand-mark','Work Library');const logo=element('img');logo.src='/brand/meridian.png';logo.alt='Meridian';brand.append(logo);header.append(brand,element('h1','application-title','Atlas Meridian'));shell.append(header);
  const center=element('main','canvas-column');const toolbar=element('div','canvas-toolbar');
  const interfaceTools=workspaceTools(authorMode,changeAuthorMode,{navigationMode:selectionMode==='region'?'hand':'select',active:authorMode==='hand',activate:()=>changeAuthorMode(authorMode==='hand'?'select':'hand')});
  interfaceTools.append(button('⊙',resetCanvasView,'workspace-tool','Reset view'));toolbar.append(interfaceTools);
  const history=element('div','tool-group history-tools');history.setAttribute('role','group');history.setAttribute('aria-label','Undo and redo');const ub=button('↶',undo,'tool','Undo'),rb=button('↷',redo,'tool','Redo');ub.disabled=!undoStack.length;rb.disabled=!redoStack.length;history.append(ub,rb);toolbar.append(history);
  if(currentSelection().length>1){const graph=selectionGraph(p,currentSelection()),parents=new Set(graph.rootIds.map(id=>p.nodes.find(node=>node.id===id)?.parentId)),create=button('Create Cluster',createSelectedCluster,'secondary','Create Cluster');create.disabled=graph.rootIds.length<2||parents.size!==1;toolbar.append(create);}
  if(!panelOpen){const panelTools=element('div','canvas-panel-tools');const show=createInspectorToggleButton(false,()=>{setPanelOpen(true);document.querySelector<HTMLSelectElement>('[aria-label="Side panel"]')?.focus();});show.classList.add('panel-toggle');panelTools.append(show);toolbar.append(panelTools);}center.append(toolbar);
  const scene=retainedScene??element('div','canvas-stage');scene.id='scene';scene.setAttribute('aria-label','Work workspace');center.append(scene);const status=element('div','canvas-status');const canvasName=element('span','',`${displayWorkTitle(p)}  /  ${p.nodes.filter(n=>n.canvasId===canvasId&&n.kind!=='canvas').length} nodes`);canvasName.dataset.currentCanvas='';status.append(canvasName,element('span','',authorMode==='smart-link'?'Click to place a Smart Link · Drag to pan':authorMode==='node'?'Click to place a node · Click a Smart Link to join its chain':authorMode==='link'?linkDraft?'Click empty space for a pivot · Click a destination to finish · Escape cancels':'Click a source node to begin a link · Drag to pan':authorMode==='delete'?'Click an object to delete · Drag to pan':authorMode==='text'?'Click a node title or body to edit · Escape returns to Single Select':selectionMode==='region'?'Drag to select tiles · Shift adds to selection':'Drag nodes to move · Double-click to expand or collapse · Drag background to pan'),button(runtime?'■ Stop All':'▶ Run',()=>{runtime=!runtime;render();},'runtime-button',runtime?'Stop All':'Run'));center.append(status);shell.append(center);
  const inspector=element('aside',`inspector ${panelOpen?'is-open':''}`);inspector.setAttribute('aria-label','Work side panel');
  drawInspector(inspector);shell.append(inspector,inspectorDivider(shell));const hub=drawNodeHub(shell);app.append(shell);updateSaveState();
  inlineText=mountCanvasTextEditing(scene,()=>project!,{
    onChange:(id,title,value)=>nodeTextChange(p.id,id,title,value),
    onFinish:()=>{if(!rendering)renderAfterSave();},onEscape:clearCanvasSelection
  });
  redrawScene=()=>{if(!project)return;const p=project;const evaluation=runtime?evaluate(p):{values:{},errors:{}};const fit=fitWorkspaceOnRender;fitWorkspaceOnRender=false;
  renderScene(scene,p,canvasId,{galleryItems:Object.fromEntries(galleryActive),onGalleryPage:pageGallery,onGalleryInspect:inspectGalleryItem,windowOffsets:Object.fromEntries(windowOffsets),onWindowScroll:(id,at)=>windowOffsets.set(id,at),selection:selected??selectedLink,selections:currentSelection(),selectionMode,regionIntersection,evaluation,running:runtime,readonly:false,connecting:false,linkDraft:linkDraft??undefined,authoringMode:authorMode,onAuthorClick:authorClick,onContextMenu:contextActions,canvasNodeTitles:canvasDestinationTitles(p),onCanvasNodeOpen:id=>void openCanvasNode(id).catch(fail),view,fit,focusNodeId,focusBranch,
    canEditText:()=>authorMode==='text',canMoveNodes:ids=>canMoveNodeBranches(project!,ids),
    onTextEdit:(id,title,surface)=>{
      if(authorMode!=='text')return;
      selected=id;selectionIds=[id];selectedLink=null;linkContextNode=null;
      if(!inlineText?.start(surface,id,title))return;
      flushRichTextEditors(true);inlineText.sync();
      // Native pointer focus first commits any sidebar field being left. Keep
      // that field alive through its blur, and leave the new glyph surface intact.
      setTimeout(()=>{if(inlineText?.surface===surface&&surface.isConnected){const focus=inspectorState.capture(inspector);drawInspector(inspector);inspectorState.restore(inspector,JSON.stringify([project!.id,canvasId,inspectorPanel,inspectorPanel==='canvas'?'outline':selectedLink??selected??currentCanvas()?.rootId]),focus);const oldHub=hubState.capture(shell.querySelector('.node-hub')),nextHub=drawNodeHub(shell);if(nextHub)hubState.restore(nextHub,JSON.stringify([project!.id,selected]),oldHub);}},0);
    },
    onSelection:nodeSelected,onSelections:selectNodes,onModuleSelect:selectModuleNode,onSnapLock:lockSnappedEdge,onMoveSelection:moveSelected,onNestSelection:nestSelected,onMove:moveNode,onResize:resizeNode,
    onTrayMove:(sourceId,dx,dy)=>edit(next=>Object.assign(next,moveExpansionTray(next,sourceId,dx,dy)),undefined,{preserveLayout:true}),
    onTrayResize:(sourceId,frame)=>edit(next=>Object.assign(next,resizeExpansionTray(next,sourceId,frame,{galleryItems:Object.fromEntries(galleryActive)})),undefined,{preserveLayout:true}),
    onToggle:(id,key,value)=>{if(key==='collapsed')toggleChildren(id,!value);else edit(p=>{p.nodes.find(n=>n.id===id)![key]=value;});},onValue:(id,value)=>edit(p=>{p.nodes.find(n=>n.id===id)!.value=value;}),
    onCellOpen:(parentId,address)=>showCellEditor(p,parentId,address,{edit,select:selectChild,fail}),onCanvasOpen:id=>void openLocalCanvas(id).catch(fail),onAssetOpen:openAsset,onViewChange:v=>{view=v;cacheView();},onLinkSelect:selectLink,onRouteEdit:(id,points)=>edit(p=>{p.links.find(l=>l.id===id)!.points=points;})});};redrawScene();
  if(creationMark){const marker=element('div','creation-mark');marker.style.left=creationMark.x+'px';marker.style.top=creationMark.y+'px';marker.setAttribute('aria-hidden','true');scene.querySelector('.ms-world')?.append(marker);}
  focusNodeId=null;focusBranch=false;
  const inspectorContext=JSON.stringify([p.id,canvasId,inspectorPanel,inspectorPanel==='canvas'?'outline':selectedLink??selected??currentCanvas()?.rootId]);
  const restoredInspector=inspectorState.restore(inspector,inspectorContext,inspectionFocus);
  const restoredHub=hub?hubState.restore(hub,JSON.stringify([p.id,hub.dataset.hubNode]),hubFocus):false;
  restoreRichTextFocus(richFocus);
  if(!restoredInspector&&!restoredHub){if(activeCard)[...scene.querySelectorAll<HTMLElement>('.ms-node')].find(node=>node.dataset.nodeId===activeCard)?.focus({preventScroll:true});else if(activeViewport)scene.querySelector<HTMLElement>('.ms-viewport')?.focus({preventScroll:true});}
}

function drawNodeHub(shell:HTMLElement):HTMLElement|null {
  mountedNodeHub?.dispose();mountedNodeHub=undefined;shell.querySelector('.node-hub')?.remove();
  const p=project,node=p&&selected&&!selectedLink&&currentSelection().length===1?p.nodes.find(node=>node.id===selected):undefined;
  shell.classList.toggle('has-node-hub',!!node);if(!p||!node)return null;
  const sourceId=p.id,nodeId=node.id,hub=element('aside','node-hub'),hubView=resolveHubView(p,node);
  const apply=(change:HubContentChange)=>{if(project?.id!==sourceId||!project.nodes.some(node=>node.id===nodeId)){notice('The selected Work changed. Add content to the current node.',true);return;}edit(next=>Object.assign(next,applyHubContent(next,nodeId,change.items,change.assets,hubView)));};
  mountedNodeHub=renderNodeHub(hub,hubView.project,hubView.node,{
    onTitleChange:title=>{const current=project?.id===sourceId?project.nodes.find(node=>node.id===nodeId):undefined;if(current)nodeTextChange(sourceId,nodeId,true,{text:title,runs:rebaseTextRuns(current.title,title,current.titleRuns??[])});},
    onShowTitleChange:visible=>{if(project?.id===sourceId)edit(next=>{next.nodes.find(node=>node.id===nodeId)!.showTitle=visible;});},
    onChange:apply,onClose:clearCanvasSelection,onError:fail,inherited:hubView.inherited,beforeAction:commitFocusedField,current:()=>{const current=project?.id===sourceId?project.nodes.find(node=>node.id===nodeId):undefined;return current&&project?resolveHubView(project,current):null;},
  });shell.append(hub);return hub;
}

function drawInspector(inspector:HTMLElement){
  if(!project)return;const p=project;
  inspector.replaceChildren();
  renderInspectorPanels(inspector,{project:p,canvasId,nodeId:selected??(!selectedLink?currentCanvas()?.rootId:null),linkId:selectedLink,panel:inspectorPanel,
    onPanelChange:panel=>{commitFocusedField();inspectorPanel=panel;saveInspectorPanel(panel);render();document.querySelector<HTMLSelectElement>('[aria-label="Side panel"]')?.focus();},
    onJumpToNode:id=>revealNode(id,true),onSelectNode:selectOutlineNode,getNodeTier:id=>currentTiers().nodeTier(project!.id,id)??1,
    onDeleteNode:id=>removeAt({nodeId:id,point:{x:0,y:0},clientX:0,clientY:0}),
    onInspectLink:selectLink,onDeleteLink:id=>{edit(next=>{next.links=next.links.filter(link=>link.id!==id);if(selectedLink===id)selectedLink=null;});},
    onCreateLink:id=>createConnection({nodeId:id,point:{x:0,y:0},clientX:0,clientY:0}),
    onChangeNodeType:(id,type)=>{const candidate=updateNodeType(project!,id,type);edit(next=>Object.assign(next,candidate));},
    onChangeNodeTag:(id,value)=>{const tag=value.trim();if(!tag||!isNodeTag(tag))throw new Error('Enter a valid Tag for the node.');edit(next=>{const node=next.nodes.find(node=>node.id===id)!;node.tag=tag;const canvas=next.canvases.find(canvas=>canvas.rootId===id);if(canvas){canvas.title=tag;if(canvas.id===next.entryCanvasId)next.title=tag;}});},
    onToggleCluster:id=>{const node=project!.nodes.find(node=>node.id===id);if(node?.cluster)toggleChildren(id,!!node.collapsed);},
    onChangeAlignment:(id,mode)=>{edit(next=>{const node=next.nodes.find(node=>node.id===id)!;const canvas=next.canvases.find(canvas=>canvas.rootId===id);const target=canvas??node;if(mode==='inherit')delete target.snapping;else target.snapping={stickyEdges:mode==='sticky',smartAlign:mode==='align',snapToGrid:false};if(canvas)delete node.snapping;});},
    onRenameNode:(id,newId)=>{const candidate=renameNode(project!,id,newId),wasSelected=selected===id,previousSelection=[...selectionIds];if(edit(next=>Object.assign(next,candidate))){if(wasSelected)selected=newId;selectionIds=previousSelection.map(value=>value===id?newId:value);if(focusNodeId===id)focusNodeId=newId;if(linkContextNode===id)linkContextNode=newId;if(libraryTargets[id]){libraryTargets[newId]=libraryTargets[id]!;delete libraryTargets[id];}render();}},
    onCreateRemoteLink:(id,link)=>{const candidate=addRemoteLink(project!,id,link);edit(next=>Object.assign(next,candidate));},
    onDeleteRemoteLink:(id,linkId)=>{const candidate=removeRemoteLink(project!,id,linkId);edit(next=>Object.assign(next,candidate));},
    renderPanel:(panel,body)=>renderInspectorDetails(body,panel),onClose:()=>{setPanelOpen(false);document.querySelector<HTMLButtonElement>('[aria-label="Show side panel"]')?.focus();}
  });
}
function clearCanvasSelection(){
  commitFocusedField();
  linkDraft=null;creationMark=null;selectionIds=[];selected=null;selectedLink=null;linkContextNode=null;
  moduleSelection=emptyModuleSelection();authorMode='select';selectionMode='pan';
  render();document.querySelector<HTMLElement>('.ms-viewport')?.focus({preventScroll:true});
}

function renderSelectionInspector(host:HTMLElement): void {
  if(!project)return;const ids=currentSelection(),p=project,graph=selectionGraph(p,ids),nodes=new Map(p.nodes.map(n=>[n.id,n]));
  host.append(element('div','sidebar-heading','SELECTION'),element('h2','inspector-title',`${ids.length} nodes selected`));
  host.append(element('p','muted',`${graph.rootIds.length} separate branches. Moving or deleting includes each branch’s contained children, even when hidden.`));
  host.append(element('p','selection-hint','Shift-click or Ctrl-click to add or remove a tile. Drag any selected tile to move the branches together.'));
  const list=element('div','selection-members');
  for(const id of ids.slice(0,60)){const node=nodes.get(id);if(node)list.append(button(displayNodeTag(node),()=>selectNodes([id]),'quiet',`Inspect selected ${displayNodeTag(node)}`));}
  if(ids.length>60)list.append(element('p','muted',`…and ${ids.length-60} more selected nodes.`));host.append(list);
  const parentIds=new Set(graph.rootIds.map(id=>nodes.get(id)?.parentId));
  const group=button('Group in a container',groupSelected,'secondary full','Group selected nodes');group.disabled=graph.rootIds.length<2||parentIds.size!==1;host.append(group);
  const cluster=button('Create Cluster',createSelectedCluster,'secondary full');cluster.disabled=graph.rootIds.length<2||parentIds.size!==1;host.append(cluster);
  const smart=button('Create Smart Link',()=>smartLinkFromSelection(ids),'secondary full');smart.disabled=graph.rootIds.length<2||parentIds.size!==1||!!nodes.get([...parentIds][0]??'')&&isOrderedContainer(nodes.get([...parentIds][0]??'')!);host.append(smart);
  if(parentIds.size!==1)host.append(element('p','muted','Grouping requires branches with the same parent, so their inherited context stays intact.'));
  const arrange=button('Arrange',arrangeSelected,'secondary full','Arrange selected nodes');arrange.disabled=graph.rootIds.length<2;host.append(arrange);
  host.append(button('Delete selected branches',deleteSelection,'danger full'));
}

const textEditors=new Map<string,RichTextEditor>();
function nodeTextChange(sourceId:string,nodeId:string,title:boolean,value:RichTextValue){
  if(project?.id!==sourceId||!project.nodes.some(node=>node.id===nodeId))return false;
  return edit(next=>{const node=next.nodes.find(node=>node.id===nodeId)!;if(title){node.title=value.text;if(value.runs.length)node.titleRuns=value.runs;else delete node.titleRuns;if(node.kind==='canvas'){const canvas=next.canvases.find(canvas=>canvas.rootId===node.id)!;canvas.title=node.title;if(canvas.id===next.entryCanvasId)next.title=node.title;}}
    else{const hubView=resolveHubView(next,node),items=nodeContentItems(hubView.node),first=items[0];if(first?.type==='text'){first.text=value.text;if(value.runs.length)first.textRuns=value.runs;else delete first.textRuns;}else items.unshift({id:crypto.randomUUID(),type:'text',text:value.text,...(value.runs.length?{textRuns:value.runs}:{})});Object.assign(next,applyHubContent(next,nodeId,items,[],hubView));}},undefined,{richText:true});
}
function bodyTextEditor(node:MeridianNode){
  const sourceId=project!.id,key=JSON.stringify([sourceId,node.id]);let editor=textEditors.get(key);
  if(!editor){editor=createRichTextEditor({label:'Text body',text:node.text??'',runs:node.textRuns??[],base:node.style,onChange:value=>nodeTextChange(sourceId,node.id,false,value)});textEditors.set(key,editor);}
  else editor.sync(node.text??'',node.textRuns,node.style);
  if(textEditors.size>16)for(const [oldKey,oldEditor]of textEditors){if(oldKey!==key&&!oldEditor.root.isConnected){oldEditor.dispose();textEditors.delete(oldKey);if(textEditors.size<=16)break;}}
  return editor;
}
function formatNodeText(nodeId:string,title=false){
  flushRichTextEditors();const node=project?.nodes.find(node=>node.id===nodeId);if(!project||!node)return;const sourceId=project.id;
  openRichTextDialog({label:title?'Title text':'Text body',title,text:title?node.title:node.text??'',runs:(title?node.titleRuns:node.textRuns)??[],base:node.style,maxLength:title?240:1_000_000,onChange:value=>nodeTextChange(sourceId,nodeId,title,value)},title?'Format title':'Edit text',render);
}
function renderInspectorDetails(host:HTMLElement,panel:InspectorPanel){
  if(panel==='module'){
    if(!project)return;const sourceId=project.id,sourceCanvas=canvasId,version=editVersion,selection=resolveModuleSelection(project,moduleSelection,canvasId);
    renderModulePanel(host,{project,canvasId,selectedIds:selection.nodeIds,selectedLinkIds:selection.linkIds,active:authorMode==='module',state:modulePanelState,
      onActivate:()=>changeAuthorMode('module'),onToggle:selectModuleNode,onClear:()=>{moduleSelection=emptyModuleSelection();selectionIds=[];selected=null;selectedLink=null;render();},
      onCreate:saveSelectedModule,onInsert:stored=>addStoredModule(stored,sourceId,sourceCanvas,version),onRefresh:()=>{if(project?.id===sourceId&&canvasId===sourceCanvas&&inspectorPanel==='module')render();},onError:fail});return;
  }
  if(!project)return;const p=project,n=currentNode()??p.nodes.find(node=>node.id===currentCanvas()?.rootId),l=p.links.find(link=>link.id===selectedLink),index=navigationIndex(p);
  const nodeName=(id:string)=>{const node=index.nodes.get(id);return node?displayNodeTag(node):id;};
  if(currentSelection().length>1&&panel==='relations'){renderSelectionInspector(host);return;}
  if(l){
    if(l.snap){
      host.append(element('h2','inspector-title','Snapped edge'),element('p','muted',`${nodeName(l.sourceId)} ⋈ ${nodeName(l.targetId)}`),element('code','guid',l.id),
        element('p','muted',l.snap.locked?'Locked into a Cluster. Dragging either node moves the connected assembly.':'The touching edges form a relationship with no line. Lock them to move together.'),
        button(l.snap.locked?'Unlock edge':'Lock edge',()=>lockSnappedEdge(l.sourceId,l.targetId,!l.snap!.locked,l.id),'secondary full'),button('Remove connection',deleteSelection,'danger full'));return;
    }
    if(panel==='appearance'){host.append(linkAppearance(l,style=>edit(next=>{const link=next.links.find(link=>link.id===l.id)!;if(style)link.style=style;else delete link.style;})));return;}
    if(panel!=='links'){host.append(element('p','muted','Choose Links to inspect this connection, or select a node.'));return;}
    host.append(button('← Associated links',()=>{selectedLink=null;selected=linkContextNode&&p.nodes.some(node=>node.id===linkContextNode)?linkContextNode:l.targetId;selectionIds=[];render();},'quiet full','Back to associated links'),element('h2','inspector-title',l.kind==='numeric'?'Number connection':l.kind==='functional'?'Logic connection':'Visual relationship'),element('p','muted',`${nodeName(l.sourceId)} → ${nodeName(l.targetId)}`),element('code','guid',l.id));
    const owner=index.linkParent(l);
    host.append(field('Contained in',select([{value:'',label:`Automatic · ${nodeName(index.linkParent({...l,parentId:undefined})?.id??currentCanvas()?.rootId??'Work')}`},...p.nodes.filter(node=>node.canvasId===l.canvasId).map(node=>({value:node.id,label:displayNodeTag(node)}))],l.parentId??'',value=>edit(next=>{const link=next.links.find(link=>link.id===l.id)!;if(value)link.parentId=value;else delete link.parentId;}))),element('p','muted',`Revealed when ${owner?displayNodeTag(owner):'its owner'} is expanded and both endpoints are visible.`));
    host.append(field('Connection label',input(l.label??'',value=>edit(next=>{const link=next.links.find(link=>link.id===l.id)!;if(value)link.label=value;else delete link.label;}))));
    if(l.kind==='visual')host.append(field('Relationship',select(visualRelations.map(value=>({value,label:value})),l.relation,value=>edit(next=>{next.links.find(link=>link.id===l.id)!.relation=value as MeridianLink['relation'];}))),field('Arrow direction',select([{value:'',label:'Relationship default'},{value:'forward',label:'Source → destination'},{value:'reverse',label:'Source ← destination'},{value:'both',label:'Both directions'},{value:'none',label:'Plain line'}],l.direction??'',value=>edit(next=>{const link=next.links.find(link=>link.id===l.id)!;if(value)link.direction=value as NonNullable<MeridianLink['direction']>;else delete link.direction;}))));
    if(isComputedLink(l.kind))host.append(field('Destination input',select(inputPorts(index.nodes.get(l.targetId)!).map((port,i)=>({value:String(i),label:`${port.name} · ${port.type}`})),String(l.targetPort??0),value=>edit(next=>{next.links.find(link=>link.id===l.id)!.targetPort=Number(value);}))))
    const route=element('details','advanced');route.append(element('summary','','Attachment points and route'));
    for(const side of ['sourceAnchor','targetAnchor'] as const){const box=element('div','pair');for(const axis of ['x','y'] as const){const anchor=l[side]??(side==='sourceAnchor'?{x:1,y:.5}:{x:0,y:.5});box.append(field(`${side==='sourceAnchor'?'Source':'Destination'} ${axis.toUpperCase()} %`,input(String(Math.round(anchor[axis]*100)),value=>edit(next=>{const link=next.links.find(link=>link.id===l.id)!;link[side]={...(link[side]??anchor),[axis]:Math.max(0,Math.min(1,Number(value)/100))};}),'number')));}route.append(box);}
    route.append(button('Insert pivot',()=>insertJunction(l.id,'pivot'),'secondary full'),button('Insert joint',()=>insertJunction(l.id,'joint'),'secondary full'));host.append(route,button('Appearance',()=>{inspectorPanel='appearance';saveInspectorPanel(inspectorPanel);render();},'quiet full','Edit link appearance'),button('Remove connection',deleteSelection,'danger full'));return;
  }
  if(!n)return;
  const update=(fn:(node:MeridianNode)=>void)=>edit(next=>fn(next.nodes.find(node=>node.id===n.id)!));
  if(panel==='appearance'){
    host.append(element('h2','inspector-title',displayNodeTag(n)));
    if(n.cluster)host.append(field('Collapsed button shape',select([{value:'square',label:'Square'},{value:'circle',label:'Circle'}],n.cluster.buttonShape,value=>edit(next=>Object.assign(next,setDisclosureClusterShape(next,n.id,value as 'square'|'circle'))))));
    const appearanceAssets=[...p.assets,...Object.values(p.snapshots).flatMap(snapshot=>snapshot.backgroundAsset?[snapshot.backgroundAsset]:[])];
    if(n.kind==='canvas'){const c=currentCanvas();if(c)host.append(canvasAppearance(c,style=>edit(next=>{const canvas=next.canvases.find(canvas=>canvas.id===c.id)!;if(style)canvas.style=style;else delete canvas.style;}),{assets:appearanceAssets,upload:file=>attachBackgroundFile(c.id,file,true),onError:fail}));return;}
    const orderedOwner=n.parentId?index.nodes.get(n.parentId):undefined,sharedTile=n.kind!=='junction'&&!!orderedOwner&&isOrderedContainer(orderedOwner);
    const display=resolveReferenceTarget(p,n)?.node,appearance=nodeAppearance(n,style=>update(node=>{
      if(node.kind==='junction'&&style?.shape!==node.style?.shape){
        const center={x:node.x+node.width/2,y:node.y+node.height/2};
        if(style?.shape==='rectangle'){node.width=32;node.height=6;}
        else if(!style||style.shape==='circle'){node.width=12;node.height=12;}
        node.x=center.x-node.width/2;node.y=center.y-node.height/2;node.sizeMode='fixed';
      }
      if(style)node.style=style;else delete node.style;
    }),n.kind!=='block'&&effectiveContentType(n.kind==='reference'&&display?display:n)==='text',{assets:appearanceAssets,upload:file=>attachBackgroundFile(n.id,file,false),onError:fail},(display??n).kind==='canvas-link',currentCanvas()?.style);
    if(sharedTile){for(const label of appearance.querySelectorAll('label'))if(label.textContent?.startsWith('Shape')){const control=label.querySelector('select');if(control)control.disabled=true;}host.append(element('p','muted',`Tile dimensions and shape are shared by ${displayNodeTag(orderedOwner!)}. Edit its ordered contents in Relations.`));}host.append(appearance);
    const displayed=navigationIndex(p,{galleryItems:Object.fromEntries(galleryActive)}).bounds(n);
    if(n.kind!=='junction'){
      const sizingField=field('Sizing',select([{value:'auto',label:isSizeRememberingContainer(n)&&!n.collapsed?'Automatic · preserve workspace frame':'Automatic · fit visible contents'},{value:'fixed',label:'Fixed dimensions'}],nodeSizingMode(n),value=>update(node=>{if(value==='fixed'){node.width=displayed.width;node.height=displayed.height;}node.sizeMode=value as 'auto'|'fixed';rememberContainerSize(node);})));if(sharedTile)sizingField.querySelector('select')!.disabled=true;host.append(sizingField,element('p','muted',isSizeRememberingContainer(n)?'Open and closed sizes are saved independently. Expanded frames keep their dimensions; ordered children fit inside. Resizing this mode sets its fixed dimensions.':'Automatic sizing fits the visible content. Resizing an edge or editing Width or Height switches this node to fixed dimensions.'));
    }
    const geometry=element('div','geometry');for(const [key,label] of [['x','X'],['y','Y'],['width','Width'],['height','Height']] as const)geometry.append(field(label,input(String(Math.abs(displayed[key])<10?Number(displayed[key].toFixed(6)):Math.round(displayed[key])),value=>{
      const number=Number(value);if(!value.trim()||!Number.isFinite(number))throw new Error('Enter a finite dimension.');
      if(key==='x'||key==='y')moveNode(n.id,key==='x'?number:n.x,key==='y'?number:n.y);
      else {
        const proposed={...displayed,[key]:Math.max(n.kind==='junction'||n.kind==='joint'?2:(n.parentId ? .001 : 1),number)};
        if(n.kind==='junction'){
          if((n.style?.shape??'circle')==='circle'||n.style?.shape==='square')proposed.width=proposed.height=proposed[key];
          proposed.x=displayed.x+(displayed.width-proposed.width)/2;proposed.y=displayed.y+(displayed.height-proposed.height)/2;
        }
        resizeNode(n.id,proposed.width,proposed.height,proposed.x,proposed.y);
      }
    },'number')));if(sharedTile)for(const control of geometry.querySelectorAll('input'))control.disabled=true;host.append(geometry);return;
  }
  if(panel==='properties'){
    if(n.smartLink){
      const settings=element('section','smart-link-settings');settings.append(element('h3','','Smart Link'));
      settings.append(field('Smart Link direction',select([{value:'auto',label:'Automatic'},{value:'down',label:'Down'},{value:'right',label:'Right'},{value:'up',label:'Up'},{value:'left',label:'Left'}],n.smartLink.direction,value=>edit(next=>configureSmartLink(next,n.id,{direction:value as NonNullable<MeridianNode['smartLink']>['direction']})))));
      settings.append(field('Smart Link gap',input(String(n.smartLink.gap),value=>edit(next=>configureSmartLink(next,n.id,{gap:Number(value)})),'number')));
      settings.append(element('p','', 'Create Node → click this line. Members spread both ways from the center. Open branches reserve space; closing them compacts the chain.'));
      if(smartLinkGeometry(p,n.id)?.blockedNodeIds?.length)settings.append(element('p','smart-link-warning','A route is blocked. Move the Smart Link handle clear of nearby nodes.'));
      host.append(settings);
    }else if(n.parentId&&index.nodes.get(n.parentId)?.smartLink){
      host.append(button('Detach from Smart Link',()=>edit(next=>detachSmartLinkMember(next,n.id)),'secondary full'));
    }
    const smartChildren=index.children.get(n.id)??[];
    if(!n.smartLink&&!isOrderedContainer(n)&&smartChildren.length>1)host.append(button('Arrange children with Smart Link',()=>smartLinkFromSelection(smartChildren.map(child=>child.id)),'secondary full'));
    if(n.kind==='input')host.append(check('Value is true',n.value===true,value=>update(node=>{node.value=value;})));
    if(n.kind==='number')host.append(field('Number value',input(String(n.value??0),value=>{if(value.trim()&&Number.isFinite(Number(value)))update(node=>{node.value=Number(value);});else notice('Enter a finite number.',true);},'number')));
    if(!n.smartLink&&(['switch','output','junction'].includes(n.kind)||n.kind==='joint'&&n.operation==='IDENTITY'))host.append(field('Value type',select([...(n.kind==='junction'?[{value:'',label:'Visual only'}]:[]),{value:'boolean',label:'Boolean'},{value:'number',label:'Number'}],n.valueType??(n.kind==='junction'?'':'boolean'),value=>update(node=>{if(value)node.valueType=value as 'boolean'|'number';else delete node.valueType;}))));
    if(n.kind==='switch')host.append(check('Circuit closed',n.closed??true,value=>update(node=>{node.closed=value;})));
    if(n.kind==='joint')host.append(field('Operation',operationSelect(n.operation??'AND',value=>update(node=>{node.operation=value;}))));
    if(['input','number','switch'].includes(n.kind))host.append(check('Expose control in viewer',n.exposed??false,value=>update(node=>{node.exposed=value;})));
    if(n.kind==='reference'){
      host.append(field('Original node',select([{value:'',label:'Choose original…'},...p.nodes.filter(node=>node.id!==n.id).map(node=>({value:node.id,label:displayNodeTag(node)}))],n.targetId??'',value=>update(node=>{if(value)node.targetId=value;else delete node.targetId;}))),field('Original cell (optional)',input(n.targetCell??'',value=>update(node=>{if(value.trim())node.targetCell=value.trim();else delete node.targetCell;}))));
      const original=index.original(n.id);if(original)host.append(button('Jump to original',()=>revealNode(original.id,true),'quiet full'));else if(n.targetId)host.append(element('p','muted','The original is outside this document; the last saved observation is used.'));
    }
    if(n.kind==='canvas-link'){const external=n.canvasTarget??(!n.targetId?libraryTargets[n.id]:undefined);if(!external)host.append(field('Opens Work',select([{value:'',label:'Choose Work…'},...p.canvases.map(canvas=>({value:canvas.rootId,label:displayWorkTitle(p,canvas.id)}))],n.targetId??'',value=>update(node=>{if(value)node.targetId=value;else delete node.targetId;}))));if(external||n.targetId)host.append(button('Open Work',()=>openCanvasNode(n.id),'secondary full','Open Work'));}
    return;
  }
  if(panel!=='relations')return;
  host.append(element('h2','inspector-title',displayNodeTag(n)));
  const ancestors=index.ancestors(n.id).reverse();host.append(element('p','inspector-hierarchy-path',[...ancestors.map(node=>displayNodeTag(node)),displayNodeTag(n)].join(' / ')));
  renderReferencingContexts(host,p,n,index);
  if(n.kind!=='canvas'){const invalid=descendants(p,n.id);host.append(field('Parent',select(p.nodes.filter(node=>node.canvasId===n.canvasId&&!invalid.has(node.id)).map(node=>({value:node.id,label:displayNodeTag(node)})),n.parentId??'',value=>edit(next=>reparentNode(next,n.id,value)))));}
  const inherited=element('details','advanced');inherited.append(element('summary','','Inherited from ancestors'),element('p','muted',ruleSummary(effectiveInheritedRules(p,n.id))),element('p','muted',`Work movement: ${resolveSnapping(currentCanvas(),n).stickyEdges?'Sticky':resolveSnapping(currentCanvas(),n).smartAlign?'Align to edges':'Free'}`));host.append(inherited,inheritedRuleSettings(n));
  const children=index.children.get(n.id)??[];
  if(isGallery(n)){
    host.append(button(n.collapsed?'Reveal gallery':'Hide gallery',()=>toggleChildren(n.id,!!n.collapsed),'secondary full','Toggle contents from relations'));
    renderGalleryContents(host,{project:p,nodeId:n.id,activeItemId:galleryActive.get(n.id),onPage:id=>pageGallery(n.id,id),onInspect:inspectGalleryItem,onAddChild:()=>createAt({nodeId:n.id,point:{x:n.x,y:n.y},clientX:0,clientY:0}),onReorder:(id,step)=>reorderChild(n.id,id,step)});
    host.append(button('Save as module',()=>saveModuleDialog(cloneProject(p),n.id,stored=>notice(`Saved ${stored.name} to your module library.`)),'quiet full'),button('Dependencies',()=>nodeDependenciesDialog(cloneProject(p),n.id,ids=>update(node=>{if(ids.length)node.dependencyIds=ids;else delete node.dependencyIds;})),'quiet full','Edit dependencies'));return;
  }
  if(n.kind!=='canvas')host.append(element('p','muted',`${['container','module','spreadsheet'].includes(n.kind)?'Bounded: children replace this node’s content inside its border.':'Unbounded: children appear outside this node’s border.'} Choose the Display Pattern in Properties.`));
  if(documentNodeAsset(project!,n))host.append(button(documentNodeExpanded(n)?'Collapse document':'Read document',()=>toggleChildren(n.id,!documentNodeExpanded(n)),'secondary full'));
  if(children.length||(index.linksByParent.get(n.id)?.length??0)>0||n.kind==='container'||n.kind==='module'||n.kind==='spreadsheet')host.append(button(n.cluster?n.collapsed?'Expand Cluster':'Collapse Cluster':n.collapsed?'Reveal contents':'Hide contents',()=>toggleChildren(n.id,!!n.collapsed),'secondary full',n.cluster?n.collapsed?'Expand Cluster':'Collapse Cluster':'Toggle contents from relations'));
  host.append(gridInspector(p,n,{edit,select:selectChild,fail}),childTools(p,n,{edit,fail}));
  if(n.kind!=='canvas'){
    const parent=n.parentId?index.nodes.get(n.parentId):undefined;
    if(isGallery(parent))host.append(element('p','muted','This object is displayed inside its gallery. Its collection order controls when it appears.'));
    if(parent&&parent.kind!=='canvas'&&!isGallery(parent)){
      if(!isOrderedContainer(parent)){
        host.append(field('Placement when revealed',select([{value:'auto',label:'Automatic pattern'},{value:'free',label:'Keep my position'},{value:'near',label:'Find nearby space'},{value:'edge',label:'Attach to parent edge'}],n.childPosition==='auto'?'auto':n.placement??'free',value=>edit(next=>{
          const child=next.nodes.find(node=>node.id===n.id)!;child.childPosition=value==='auto'?'auto':'manual';child.placement=value==='auto'?'free':value as NonNullable<MeridianNode['placement']>;
          if(!parent.collapsed)Object.assign(next,revealChildren(next,parent.id,true));
        }))));
        if(n.childPosition!=='auto'&&n.placement==='edge')host.append(field('Preferred edge',select(['auto','top','right','bottom','left'].map(value=>({value,label:value})),n.attachSide??'auto',value=>edit(next=>{next.nodes.find(node=>node.id===n.id)!.attachSide=value as NonNullable<MeridianNode['attachSide']>;if(!parent.collapsed)Object.assign(next,revealChildren(next,parent.id,true));}))));
      }
      const parentLink=parentRelationship(p,n);host.append(field('Link to parent',select([{value:'',label:'No line'},...visualRelations.map(value=>({value,label:value}))],parentLink?.relation??'',value=>setParentRelationship(n.id,value))));
    }
  }
  const descendantsList=index.descendants(n.id),limit=descendantLimits.get(n.id)??100;host.append(element('h3','',`Descendants · ${descendantsList.length}`));
  const list=element('div','inspector-descendants');for(const child of descendantsList.slice(0,limit)){const row=element('div','inspector-descendant');row.style.setProperty('--descendant-depth',String(Math.min(8,index.ancestors(child.id).length-index.ancestors(n.id).length-1)));row.append(button(displayNodeTag(child),()=>selectChild(child.id),'child-select',`Inspect descendant ${displayNodeTag(child)}`),element('small','muted',`${inspectorNodeType(child)} · ${index.isNodeVisible(child.id)?'revealed':'hidden'}`),button('⌖',()=>revealNode(child.id,true),'quiet',`Jump to Node: ${displayNodeTag(child)}`));if(child.parentId===n.id){const at=children.findIndex(node=>node.id===child.id);const up=button('↑',()=>reorderChild(n.id,child.id,-1),'quiet',`Move ${displayNodeTag(child)} earlier`),down=button('↓',()=>reorderChild(n.id,child.id,1),'quiet',`Move ${displayNodeTag(child)} later`);up.disabled=at===0;down.disabled=at===children.length-1;row.append(up,down);}list.append(row);}host.append(list);
  if(descendantsList.length>limit)host.append(button('Show more descendants',()=>{descendantLimits.set(n.id,limit+100);render();},'quiet full'));
  host.append(button('+ New Node',()=>createAt({nodeId:n.id,point:n.kind==='canvas'?{x:(180-view.x)/view.scale,y:(150-view.y)/view.scale}:{x:n.x+n.width+28,y:n.y},clientX:0,clientY:0}),'secondary full','Create child node'));
  if(n.kind!=='canvas')host.append(button('Save as module',()=>saveModuleDialog(cloneProject(p),n.id,stored=>notice(`Saved ${stored.name} to your module library.`)),'quiet full'),button('Dependencies',()=>nodeDependenciesDialog(cloneProject(p),n.id,ids=>update(node=>{if(ids.length)node.dependencyIds=ids;else delete node.dependencyIds;})),'quiet full','Edit dependencies'));
}
function reorderChild(parentId:string,childId:string,step:number){edit(next=>{const parent=next.nodes.find(node=>node.id===parentId)!,children=next.nodes.filter(node=>node.parentId===parentId),at=children.findIndex(node=>node.id===childId),other=children[at+step];if(!other)return;const child=children[at]!;const left=next.nodes.indexOf(child),right=next.nodes.indexOf(other);[next.nodes[left],next.nodes[right]]=[other,child];if(!isGallery(parent)&&parent.grid&&child.cell&&other.cell)[child.cell,other.cell]=[other.cell,child.cell];if(!isGallery(parent)&&parent.kind!=='canvas')Object.assign(next,reflowContainedChildren(next,parentId));});}
/** Backlinks are navigation to existing appearances, never a clone or writeback. */
function renderReferencingContexts(host:HTMLElement,p:MeridianProject,n:MeridianNode,index:ReturnType<typeof navigationIndex>){
  if(n.kind==='reference'){
    const original=index.original(n.id);
    if(original)host.append(button('Open original context',()=>revealNode(original.id,true),'quiet full','Open original context'));
    else if(n.targetId)host.append(element('p','muted','The original context is unavailable in this document. Any saved observation remains read-only.'));
  }
  const references=index.backlinks(n.id);
  if(!references.length)return;
  const key=`${p.id}:${n.id}`,limit=backlinkLimits.get(key)??20;
  const group=element('section','inspector-backlinks');group.setAttribute('aria-label','Where this node is referenced');
  group.append(element('h3','',`Referenced by · ${references.length}`));
  for(const reference of references.slice(0,limit)){
    const row=element('div','inspector-backlink');row.dataset.backlinkNode=reference.id;
    const title=displayNodeTag(reference),canvas=p.canvases.find(canvas=>canvas.id===reference.canvasId);
    row.append(button(title,()=>revealNode(reference.id,true),'child-select',`Jump to reference: ${title}`),element('small','muted',`${canvas?displayWorkTitle(p,canvas.id):'Work'} · ${inspectorNodeType(reference)}${index.isNodeVisible(reference.id)?'':' · hidden'}`));
    group.append(row);
  }
  if(references.length>limit)group.append(button('Show more references',()=>{backlinkLimits.set(key,limit+20);render();},'quiet full'));
  host.append(group);
}

function renderContentAttachments(host:HTMLElement,p:MeridianProject,n:MeridianNode){
  const active=activeNodeContent(n),primary=p.assets.find(asset=>asset.id===active.assetId);
  if(primary){const row=element('div','inspector-attachment-row');row.append(button(primary.name,()=>openAsset(primary.id),'quiet','Open content file'),button('−',()=>edit(next=>{const node=next.nodes.find(node=>node.id===n.id)!;delete node.assetId;delete node.attachments;node.contentType='asset';}),'quiet','Remove content file'));host.append(row);if(/^image\/(png|jpeg|webp|gif|avif|bmp)$/i.test(primary.mime)){const preview=element('img','inspector-content-preview');preview.src=`data:${primary.mime};base64,${primary.base64}`;preview.alt=primary.name;host.append(preview);}}
  const file=element('input');file.type='file';file.onchange=()=>{if(file.files?.[0])void attachContentFile(n.id,file.files[0]).catch(fail);};host.append(field(primary?'Replace content file':'Content file',file),element('p','muted','An image, video, audio, or document. Up to 16 MiB.'));
}
/** Uploads merge into the current style, and cannot follow a changed selection or replace a newer background choice. */
async function attachBackgroundFile(targetId:string,file:File,canvas:boolean){
  if(!project)return;
  if(file.size>16*1024*1024)throw new Error('Background images must be 16 MiB or smaller.');
  if(!file.size)throw new Error('Choose an image that contains data.');
  const sourceProject=project.id,sourceCanvas=canvasId,sourceNode=currentNode()?.id??currentCanvas()?.rootId;
  const target=canvas?project.canvases.find(item=>item.id===targetId):project.nodes.find(item=>item.id===targetId);
  if(!target)throw new Error('The background destination is no longer available.');
  const signature=(style?:BackgroundStyle)=>JSON.stringify([style?.backgroundAssetId,style?.backgroundPattern,style?.backgroundScale,style?.backgroundTint,style?.backgroundFit,style?.backgroundOpacity]);
  const initialBackground=signature(target.style);
  const extension=file.name.split('.').at(-1)?.toLowerCase(),fallback:Record<string,string>={png:'image/png',jpg:'image/jpeg',jpeg:'image/jpeg',webp:'image/webp',gif:'image/gif',avif:'image/avif',bmp:'image/bmp'};
  const mime=(file.type||fallback[extension??'']||'').toLowerCase();
  if(!/^image\/(png|jpeg|webp|gif|avif|bmp)$/.test(mime))throw new Error('Choose a PNG, JPEG, WebP, GIF, AVIF or BMP image.');
  const bytes=new Uint8Array(await file.arrayBuffer());
  const url=URL.createObjectURL(new Blob([bytes],{type:mime}));
  try {const image=new Image();image.src=url;await image.decode();if(!image.naturalWidth||!image.naturalHeight)throw new Error('This image cannot be displayed.');}
  catch {throw new Error('This image cannot be displayed. Choose a valid image file.');}
  finally {URL.revokeObjectURL(url);}
  const currentTarget=canvas?project?.canvases.find(item=>item.id===targetId):project?.nodes.find(item=>item.id===targetId);
  if(!project||project.id!==sourceProject||canvasId!==sourceCanvas||(currentNode()?.id??currentCanvas()?.rootId)!==sourceNode||!currentTarget)throw new Error('The selected work or node changed. Select the destination again before uploading.');
  if(signature(currentTarget.style)!==initialBackground)throw new Error('The background changed while this image was loading. Upload it again to replace the current background.');
  let binary='';for(let at=0;at<bytes.length;at+=32768)binary+=String.fromCharCode(...bytes.subarray(at,at+32768));
  const asset={id:crypto.randomUUID(),name:file.name,mime,base64:btoa(binary)};
  if(!isBackgroundAsset(asset))throw new Error('This file does not contain a supported image. Choose a valid image file.');
  if(!edit(next=>{const destination=canvas?next.canvases.find(item=>item.id===targetId)!:next.nodes.find(item=>item.id===targetId)!;next.assets.push(asset);destination.style={...destination.style,backgroundAssetId:asset.id,backgroundFit:'cover',backgroundOpacity:destination.style?.backgroundOpacity??(canvas ? .45 : .35)};}))throw new Error('The background image could not be applied.');
}
async function attachContentFile(nodeId:string,file:File){
  if(file.size>16*1024*1024)throw new Error('Attachments must be 16 MiB or smaller.');const projectId=project?.id,contentSignature=(node:MeridianNode)=>JSON.stringify([node.contentType,node.text,node.assetId,node.attachments,node.hyperlinks]),initial=project?.nodes.find(node=>node.id===nodeId);if(!initial)throw new Error('The node is no longer available.');const original=contentSignature(initial),bytes=new Uint8Array(await file.arrayBuffer());let binary='';for(let at=0;at<bytes.length;at+=32768)binary+=String.fromCharCode(...bytes.subarray(at,at+32768));const asset={id:crypto.randomUUID(),name:file.name,mime:file.type||'application/octet-stream',base64:btoa(binary)};
  const current=project?.nodes.find(node=>node.id===nodeId);if(current&&contentSignature(current)!==original)throw new Error('The content changed while this file was loading. Choose the file again to replace the current content.');
  if(!project||project.id!==projectId||!project.nodes.some(node=>node.id===nodeId))throw new Error('The node is no longer available.');edit(next=>{const node=next.nodes.find(node=>node.id===nodeId)!;next.assets.push(asset);node.contentType='asset';node.assetId=asset.id;delete node.text;delete node.attachments;delete node.hyperlinks;});
}

async function exportProject(){
  if(!project)return;const p=cloneProject(project),m=modal('Download editable Work');
  m.body.append(element('p','lead-small','Review what travels with this work.'));
  const include=element('input');include.type='checkbox';include.checked=p.dependencies.some(d=>d.required);
  const choice=element('label','check-row');choice.append(include,element('span','','Download External References?'));m.body.append(choice);
  const contents=element('p','muted'),report=element('div');m.body.append(contents,report);
  const refresh=()=>{const payload=createPublicationPayload(p,{includeExternalReferences:include.checked});contents.textContent=`This package includes: ${payload.project.canvases.length} Works · ${payload.project.nodes.length} nodes · ${attachmentCount(payload.project)} attachments · ${Object.keys(payload.project.snapshots).length} saved reference snapshots`;renderDependencySummary(report,p,{includeExternalReferences:include.checked});};include.onchange=refresh;refresh();
  m.body.append(element('p','module-policy','All local works are included. External snapshots follow your choice; unavailable works are listed as requirements and are not fetched.'));
  const submit=button('Download Work',async()=>{submit.disabled=true;try{const portable=createPublicationPayload(p,{includeExternalReferences:include.checked}).project;download(await storage.exportFile(portable),`${safeName(p.title)}.meridian`);m.close();notice('Editable work exported.');}finally{submit.disabled=false;}},'primary');
  m.footer.append(button('Cancel',m.close,'secondary'),submit);
}
function attachmentCount(p:MeridianProject){return new Set([...p.assets.map(asset=>asset.id),...Object.values(p.snapshots).flatMap(snapshot=>[snapshot.asset?.id,snapshot.backgroundAsset?.id,...snapshot.assets?.map(asset=>asset.id)??[]].filter((id):id is string=>!!id))]).size;}
function publicationAssets(){return viewerAssets??=Promise.all([fetch('/viewer.js').then(r=>{if(!r.ok)throw new Error('Viewer bundle unavailable');return r.text();}),fetch('/scene.css').then(r=>{if(!r.ok)throw new Error('Viewer styles unavailable');return r.text();})]);}
async function publish(preview:boolean){if(!project)return;const p=cloneProject(project);const m=modal(preview?'Preview published Work':'Publish a portable Work');m.dialog.classList.toggle('publish-modal',!preview);const include=element('input');include.type='checkbox';include.checked=p.dependencies.some(d=>d.required);const label=element('label','check-row');label.append(include,element('span','','Download External References?'));const packageCounts=element('p','muted');m.body.append(element('p','lead-small','One HTML file. Its viewer, logic, and included media travel with it.'),packageCounts,label);
  m.body.append(element('p','muted','Required external works are selected by default. Unavailable or excluded requirements will be listed in the publication.'));
  if(p.dependencies.length){for(const d of p.dependencies)m.body.append(element('p','dependency',`${d.required?'Required':'Optional'} · ${d.title}`));}
  const dependencyReport=element('div');const drawDependencies=()=>{const payload=createPublicationPayload(p,{includeExternalReferences:include.checked});packageCounts.textContent=`This package includes: ${payload.project.canvases.length} Works · ${payload.project.nodes.length} nodes · ${attachmentCount(payload.project)} attachments · ${Object.keys(payload.project.snapshots).length} saved reference snapshots`;renderDependencySummary(dependencyReport,p,{includeExternalReferences:include.checked});};drawDependencies();include.onchange=drawDependencies;m.body.append(dependencyReport);
  m.footer.append(button('Cancel',m.close,'secondary'),button(preview?'Open preview':'Publish HTML',async()=>{const [js,css]=await publicationAssets();const html=buildPublication(p,js,css,{includeExternalReferences:include.checked});m.close();if(preview){const panel=modal('Published preview');panel.dialog.classList.add('preview-modal');const frame=element('iframe','preview-frame');frame.title='Published Meridian preview';frame.sandbox.add('allow-scripts','allow-downloads','allow-popups');const url=URL.createObjectURL(new Blob([html],{type:'text/html'}));frame.src=url;panel.body.append(frame);panel.dialog.addEventListener('close',()=>URL.revokeObjectURL(url));panel.footer.append(button('Close',panel.close,'secondary'));}else{const name=`${safeName(p.title)}.html`;download(new Blob([html],{type:'text/html'}),name);const result=modal('Your work is ready');result.body.append(element('p','','Open the HTML file in a browser, even offline. Upload it to your website to embed it.'));const snippet=element('textarea');snippet.readOnly=true;snippet.value=`<iframe src="${encodeURI(name)}" title="Meridian work" width="100%" height="640" style="border:0" allowfullscreen></iframe>`;snippet.rows=4;result.body.append(field('Website embed',snippet));result.footer.append(button('Done',result.close,'primary'));}},'primary'));
}
async function showHistory(){if(!project)return;const id=project.id,home=libraryDocument;const m=modal('History & checkpoints');m.body.append(element('p','muted','Checkpoints preserve an arrangement. Restoring one changes your draft; save when ready.'));const list=element('div','checkpoint-list');m.body.append(list);const refresh=async()=>{list.replaceChildren();const cps=home?await libraryStorage.checkpoints():await storage.checkpoints(id);if(!cps.length)list.append(element('p','','No named checkpoints yet.'));for(const cp of cps){const row=element('div','checkpoint-row');row.append(element('div','',`${cp.name} · ${new Date(cp.createdAt).toLocaleString()}`),button('Restore',async()=>{const checkpoint=home?await libraryStorage.loadCheckpoint(cp.id):undefined;const old=checkpoint?.project??await storage.loadCheckpoint(id,cp.id);if(checkpoint)libraryTargets={...libraryTargets,...checkpoint.canvasTargets};m.close();edit(p=>{const rev=p.revision;Object.assign(p,old);p.revision=rev;p.snapshots=structuredClone(savedSnapshots);});if(project&&!project.canvases.some(c=>c.id===canvasId))canvasId=project.entryCanvasId;selected=null;render();},'secondary'));list.append(row);}};await refresh();m.footer.append(button('Close',m.close,'secondary'),button('New checkpoint',async()=>{const name=await askName('Name this checkpoint','Working arrangement');if(name&&project){if(home)await libraryStorage.checkpoint(libraryDocumentFromProject({...libraryDocument!,canvasTargets:libraryTargets},project),name);else await storage.checkpoint(project,name);await refresh();notice('Checkpoint saved.');}},'primary'));
}

document.addEventListener('keydown',e=>{
  const editing=(e.target as HTMLElement)?.closest('input,textarea,select,[contenteditable]'),modifier=e.ctrlKey||e.metaKey,key=e.key.toLowerCase();
  if((e.target as Element)?.closest?.('.mdv-viewer'))return;
  if(e.isComposing||e.defaultPrevented||document.querySelector('dialog[open],.font-picker-popup:popover-open'))return;
  const menu=document.querySelector<HTMLElement>('.workspace-menu:popover-open');
  if(menu&&!menu.hasAttribute('data-workspace-create-menu'))return;
  if(e.key==='Escape'){if(inlineText?.active&&inlineText.owns(e.target as Node)){e.preventDefault();inlineText.finish(false);}else{const stage=app.querySelector<HTMLElement>('.canvas-stage');if(stage&&sceneGestureActive(stage))return;}e.preventDefault();dismissCanvasMenus();clearCanvasSelection();return;}
  if(modifier&&key==='f'&&project){e.preventDefault();openInspectorSearch();return;}
  if(modifier&&key==='s'){e.preventDefault();void save();return;}
  if(editing)return;
  if(!modifier&&!e.altKey&&project&&['a','s','d','f','t'].includes(key)){
    e.preventDefault();if(e.repeat)return;
    if(key==='a')toggleCreationMenu();
    else if(key==='s'){dismissCanvasMenus();changeAuthorMode(authorMode==='hand'?'select':'hand');}
    else if(key==='d'){dismissCanvasMenus();changeAuthorMode(authorMode==='delete'?'select':'delete');}
    else if(key==='t'){dismissCanvasMenus();changeAuthorMode(authorMode==='text'?'select':'text');}
    else{dismissCanvasMenus();resetCanvasView();}
    return;
  }
  if(menu)return;
  if(modifier&&!e.altKey&&!e.shiftKey&&!e.defaultPrevented&&key==='c'&&project&&currentSelection().some(id=>project!.nodes.find(node=>node.id===id)?.kind!=='canvas')){
    e.preventDefault();if(!e.repeat)try{copySelectedNodes();}catch(error){fail(error);}return;
  }
  if(modifier&&!e.altKey&&!e.shiftKey&&!e.defaultPrevented&&key==='v'&&project&&nodeClipboard){
    e.preventDefault();if(!e.repeat)try{pasteCopiedNodes();}catch(error){fail(error);}return;
  }
  if(modifier&&key==='z'){e.preventDefault();e.shiftKey?redo():undo();}
  else if(modifier&&(key==='x'||key==='y')){e.preventDefault();redo();}
  else if(modifier&&key==='a'&&project){e.preventDefault();selectVisible();}
  else if(modifier&&key==='g'&&project){e.preventDefault();void groupSelected().catch(fail);}
  else if(e.key==='Delete'||e.key==='Backspace'){e.preventDefault();deleteSelection();}

});
// Keep the keyboard creation menu at the user's last canvas location even when
// the toolbar or sidebar currently owns focus. Native popovers clamp it onscreen.
document.addEventListener('pointermove',event=>{if((event.target as Element)?.closest('.canvas-stage'))canvasPointer={x:event.clientX,y:event.clientY};},{passive:true});
// Navigation commits a focused field in its click handler. Letting mouse-down
// blur it first can replace the clicked button before that handler runs.
document.addEventListener('mousedown',event=>{
  const active=document.activeElement as HTMLElement|null;
  if(event.button===0&&active&&app.contains(active)&&active.matches('input,textarea,select,[contenteditable]')&&(event.target as HTMLElement)?.closest('.application-file,.application-navigation,.brand-mark,.ms-canvas-open,.ms-gallery button,.inspector-gallery button,.nh-add,.nh-item-actions button,.nh-close,.nh-show-title input'))event.preventDefault();
},true);
window.addEventListener('beforeunload',e=>{commitFocusedField();rememberView();if(dirty||saving){if(dirty)storeLocalRecovery();e.preventDefault();e.returnValue='';}});
document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='hidden'&&dirty){storeLocalRecovery();void automaticSave.flush();}});
window.addEventListener('pagehide',()=>{if(dirty)storeLocalRecovery();});
async function boot(){if(bootMediaWindow())return;const parameters=new URL(location.href).searchParams;if(parameters.get('window')==='waiting'){document.title='Opening Work · Atlas Meridian';const waiting=element('main','library-workspace');waiting.append(element('p','library-loading','Opening Work…'));app.replaceChildren(waiting);return;}const bootCanvas=parameters.get('canvas');try{const id=parameters.get('project');if(id){await openProject(id,bootCanvas??undefined);}else await showLibrary(parameters.get('libraryCanvas')??undefined);}catch(e){await showLibrary();fail(e);}}
async function restoreHistoryContext():Promise<void>{
  if(restoringHistory)return;restoringHistory=true;
  try{
    fitWorkspaceOnRender=false;
    rememberView();activeHistoryEntry=historyEntryId();
    const parameters=new URL(location.href).searchParams,id=parameters.get('project');
    const targetCanvas=id?parameters.get('canvas')??project?.entryCanvasId:parameters.get('libraryCanvas')??(libraryDocument?project?.entryCanvasId:undefined);
    if(project&&((id===project.id&&!libraryDocument)||(!id&&libraryDocument))){
      const target=project.canvases.find(canvas=>canvas.id===targetCanvas);
      if(target){canvasId=target.id;view=rememberedHistoryView(canvasId);selected=null;selectedLink=null;selectionIds=[];focusNodeId=null;focusBranch=false;}
      render();
    }else await boot();
  }finally{restoringHistory=false;}
}
bindApplicationChrome({command:command=>void fileCommand(command).catch(fail),tool:command=>void toolCommand(command).catch(fail),navigate:direction=>void navigateHistory(direction).catch(fail),changed:render,historyChanged:()=>void restoreHistoryContext().catch(fail)});
document.addEventListener('keydown',event=>{if(!event.defaultPrevented&&event.altKey&&(event.key==='ArrowLeft'||event.key==='ArrowRight')&&!document.querySelector('dialog[open],.workspace-menu:popover-open')){event.preventDefault();void navigateHistory(event.key==='ArrowLeft'?'back':'forward').catch(fail);}});
void boot();


