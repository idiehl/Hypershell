import type { MeridianNode, MeridianProject, PublicationPayload, Point } from './core/types.ts';
import { revealChildren } from './core/reveal.ts';
import { revealDocumentNode, documentNodeAsset, documentNodeExpanded } from './core/document-node.ts';
import { evaluate } from './core/engine.ts';
import { cloneProject } from './core/model.ts';
import { arrangeChildren, normalizeContainerLayouts } from './core/grid.ts';
import { createProjectSearch, navigationIndex } from './core/navigation.ts';
import { openAssetPreview, renderScene, type SceneView } from './scene.ts';
import { createPublicationPayload } from './publication.ts';
import { showSnippetDialog } from './selection-ui.ts';
import { bootMediaWindow } from './media-window.ts';
import { isGallery, galleryOwner } from './core/gallery.ts';
import { resolveCanvasCollisions } from './core/collision.ts';
import { nodeTag } from './core/node-tag.ts';
import { nodeKindLabel, workLibraryDisplayText } from './core/work-terminology.ts';
import { appendReadonlyNodeHub, type HubContentMount } from './hub-content-view.ts';
import { reflowSmartLinks } from './core/smart-links.ts';
import { bindWebsiteLinks } from './website-links.ts';

function element<K extends keyof HTMLElementTagNameMap>(tag: K, className: string, text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag); node.className = className; if (text !== undefined) node.textContent = text; return node;
}
function control(text: string, title: string, action: () => void): HTMLButtonElement {
  const button = element('button', '', text); button.type = 'button'; button.title = title; button.setAttribute('aria-label', title); button.addEventListener('click', action); return button;
}

/** A publication keeps authored data immutable; all controls operate on a runtime clone. */
export function mountViewer(host: HTMLElement, payload: PublicationPayload): void {
  bindWebsiteLinks();
  const authored = cloneProject(payload.project);
  let runtime: MeridianProject = cloneProject(authored);
  const displayTag = (node: MeridianNode): string => workLibraryDisplayText(runtime.id, node.id, nodeTag(node));
  const workTitle = (id: string): string => workLibraryDisplayText(runtime.id, id, runtime.canvases.find(canvas => canvas.id === id)?.title ?? 'Work');
  let canvasId = runtime.entryCanvasId;
  let running = false;
  let selection: string | null = null;
  let selections: string[] = [];
  let selectionMode: 'pan' | 'region' = 'pan';
  let inspectorTimer: ReturnType<typeof setTimeout> | undefined;
  let focusNodeId: string | null = null, focusBranch = false;
  let fitOnEntry = true;
  const views = new Map<string, SceneView>();
  const galleryActive = new Map<string, string>();
  const windowOffsets=new Map<string,Point>();
  let selectedHub: HubContentMount | undefined;
  const normalizedCanvases = new Set<string>();
  function normalizeCanvas(id: string): void {
    if (normalizedCanvases.has(id)) return;
    // Legacy dimensions may have left neighbors inside a newly fitted tile.
    // Reserve every Gallery object's envelope once, independent of the viewed
    // page. Paging and camera changes must never move the normalized layout.
    runtime = resolveCanvasCollisions(normalizeContainerLayouts(runtime), id);
    reflowSmartLinks(runtime,id);
    normalizedCanvases.add(id);
  }
  function activateGalleryPath(id:string){let child=id;for(const parent of navigationIndex(runtime).ancestors(id)){if(isGallery(parent))galleryActive.set(parent.id,child);child=parent.id;}}
  const searchProject = createProjectSearch(authored);
  const revealGridChildren=(id:string,expanded:boolean)=>{runtime=revealDocumentNode(runtime,id,expanded);let parentId=runtime.nodes.find(n=>n.id===id)?.parentId;while(parentId){const parent=runtime.nodes.find(n=>n.id===parentId);if(!parent)break;if(!isGallery(parent)&&parent.grid&&parent.childArrangement==='grid')runtime=arrangeChildren(runtime,parent.id,'grid').project;parentId=parent.parentId;}};
  type Place = { canvasId: string; selection: string | null; view?: SceneView };
  const currentPlace = (): Place => ({ canvasId, selection, ...(views.has(canvasId) ? { view: { ...views.get(canvasId)! } } : {}) });
  const past: Place[] = [], future: Place[] = [];
  const shell = element('div', 'mv-shell');
  const header = element('header', 'mv-header');
  header.append(element('span', 'mv-brand', 'ATLAS MERIDIAN'));
  const title = element('div', 'mv-title', authored.title); title.title = authored.title; header.append(title);
  const controls = element('div', 'mv-controls');
  const back = control('←', 'Back', () => travel(past, future));
  const forward = control('→', 'Forward', () => travel(future, past));
  const explore = control('☷ Explore', 'Explore project', () => {
    clearTimeout(inspectorTimer);
    panel.hidden = !panel.hidden; explore.setAttribute('aria-expanded', String(!panel.hidden));
    if (!panel.hidden) search.focus();
  });
  explore.setAttribute('aria-expanded', 'false'); explore.setAttribute('aria-controls', 'meridian-navigation');
  const state = element('span', 'mv-status', 'Stopped'); state.setAttribute('role', 'status');
  const run = control('▶ Run', 'Run simulation', () => { running = true; render(); }); run.className = 'mv-run';
  const stop = control('■ Stop', 'Stop simulation', () => { running = false; render(); });
  const reset = control('↺', 'Reset to authored state', () => { runtime = cloneProject(authored); normalizedCanvases.clear(); normalizeCanvas(canvasId); galleryActive.clear(); windowOffsets.clear(); running = false; selection = null; selections = []; selectionMode = 'pan'; render(); });
  const canvasSelect = element('select', 'mv-canvas-select'); canvasSelect.setAttribute('aria-label', 'Choose Work');
  for (const canvas of authored.canvases) { const option = element('option', '', workLibraryDisplayText(authored.id, canvas.id, canvas.title)); option.value = canvas.id; canvasSelect.append(option); }
  canvasSelect.value = canvasId;
  canvasSelect.addEventListener('change', () => navigate(canvasSelect.value));
  controls.append(back, forward, explore, state, run, stop, reset, canvasSelect); header.append(controls);
  const selectionTools = element('div', 'mv-selection-tools');
  Object.assign(selectionTools.style, { display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: '8px', flexBasis: '100%', fontSize: '12px' });
  const area = control('▧ Select area', 'Select area', () => {
    selectionMode = selectionMode === 'region' ? 'pan' : 'region'; clearTimeout(inspectorTimer);
    if (selectionMode === 'region') { panel.hidden = true; explore.setAttribute('aria-expanded', 'false'); }
    render();
  });
  const intersection = element('select', 'mv-region-intersection'); intersection.setAttribute('aria-label', 'Region selection boundary');
  for (const [value, label] of [['contained', 'Fully enclosed'], ['intersecting', 'Touching']] as const) { const option = element('option', '', label); option.value = value; intersection.append(option); }
  intersection.addEventListener('change', render);
  const selectionCount = element('span', 'mv-selection-count', '0 selected'); selectionCount.setAttribute('aria-live', 'polite');
  const clearSelection = control('Clear', 'Clear selected nodes', () => { clearTimeout(inspectorTimer); selections = []; selection = null; render(); });
  const downloadSelection = control('Download selection ↓', 'Download selected nodes', () => {
    const source = cloneProject(authored), currentNodes = new Map(runtime.nodes.map(node => [node.id, node]));
    // Reuse the explored layout and disclosure while keeping authored values/content.
    for (const node of source.nodes) { const current = currentNodes.get(node.id); if (current) { node.x = current.x; node.y = current.y; node.collapsed = current.collapsed; } }
    const currentLinks = new Map(runtime.links.map(link => [link.id, link]));
    for (const link of source.links) { const current = currentLinks.get(link.id); if (current) link.points = current.points ? structuredClone(current.points) : undefined; }
    showSnippetDialog({ project: source, selectedIds: selections,
      sourceNote: 'Copies authored content and input values with the positions and expanded or collapsed state you are viewing. Temporary simulation changes are not copied.',
      download: (candidate, includeExternalReferences) => downloadPublication(createPublicationPayload(candidate, { includeExternalReferences })),
    });
  });
  const selectionHint = element('span', 'mv-selection-hint'); selectionHint.style.color = '#9fb2c3';
  selectionTools.append(area, intersection, selectionCount, clearSelection, downloadSelection, selectionHint); header.append(selectionTools);
  const body = element('div', 'mv-body');
  const scene = element('section', 'mv-scene'); scene.setAttribute('aria-label', 'Published Work');
  const panel = element('aside', 'mv-navigation'); panel.id = 'meridian-navigation'; panel.hidden = true; panel.setAttribute('aria-label', 'Project navigation');
  const panelHeader = element('div', 'mv-navigation-heading');
  panelHeader.append(element('strong', '', 'Explore this project'), control('×', 'Close project navigation', () => { clearTimeout(inspectorTimer); panel.hidden = true; explore.setAttribute('aria-expanded', 'false'); explore.focus(); }));
  const search = element('input', 'mv-search'); search.type = 'search'; search.placeholder = 'Search all Works and attachments…'; search.setAttribute('aria-label', 'Search project');
  const list = element('div', 'mv-navigation-list'); list.setAttribute('aria-label', 'Project outline and search results');
  const context = element('div', 'mv-node-context');
  panel.append(panelHeader, search, list, context); body.append(scene, panel);
  search.addEventListener('input', drawNavigation);
  panel.addEventListener('keydown', event => {
    if (event.key === 'Escape') { panel.hidden = true; explore.setAttribute('aria-expanded', 'false'); explore.focus(); return; }
    if (!(event.target instanceof HTMLButtonElement) || !list.contains(event.target)) return;
    const buttons = [...list.querySelectorAll<HTMLButtonElement>('button')];
    const index = buttons.indexOf(event.target);
    const next = event.key === 'ArrowDown' ? Math.min(index + 1, buttons.length - 1) : event.key === 'ArrowUp' ? Math.max(0, index - 1) : event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : -1;
    if (next >= 0) { event.preventDefault(); buttons[next]?.focus(); }
  });
  const footer = element('footer', 'mv-footer');
  const note = element('span', payload.missing.length ? 'mv-note mv-missing' : 'mv-note', payload.missing.length ? `Missing dependencies: ${payload.missing.join(', ')}` : 'Explore the Work. Interactive controls do not change the published original.'); note.title = note.textContent ?? '';
  const download = control('Save a copy ↓', 'Download this published Work for viewing or importing into the Meridian editor', () => {
    // Rebuild from the original inert payload: current switch/collapse state is deliberately omitted.
    downloadPublication(payload);
  });
  function downloadPublication(copy: PublicationPayload): void {
    const html = document.documentElement.cloneNode(true) as HTMLElement;
    html.querySelector('#meridian-viewer')?.replaceChildren();
    const data = html.querySelector('#meridian-data');
    if (data) data.textContent = JSON.stringify(copy).replace(/[<>&\u2028\u2029]/g, character => `\\u${character.charCodeAt(0).toString(16).padStart(4, '0')}`);
    const documentTitle = html.querySelector('title'); if (documentTitle) documentTitle.textContent = `${copy.project.title} · Atlas Meridian`;
    html.querySelectorAll('dialog').forEach(dialog => dialog.remove());
    const url = URL.createObjectURL(new Blob([`<!doctype html>\n${html.outerHTML}`], { type: 'text/html;charset=utf-8' }));
    const anchor = document.createElement('a'); anchor.href = url; anchor.download = `${copy.project.title.replace(/[\\/:*?"<>|]/g, '_') || 'Meridian'}.meridian.html`; anchor.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  footer.append(note, element('span', 'mv-foot-brand', 'A WORK FOR CONNECTED IDEAS'), download);
  shell.append(header, body, footer); host.replaceChildren(shell);

  function navigate(targetCanvas: string, nodeId: string | null = null, record = true): void {
    if (!runtime.canvases.some(canvas => canvas.id === targetCanvas)) return;
    if (record && (targetCanvas !== canvasId || nodeId !== selection)) { past.push(currentPlace()); future.length = 0; }
    clearTimeout(inspectorTimer);
    // Entering a Work through its portal or selector always presents all of its
    // currently visible content. Inspection and history retain their own camera.
    fitOnEntry = record && nodeId === null;
    canvasId = targetCanvas; selection = nodeId; selections = nodeId && runtime.nodes.some(node => node.id === nodeId && node.kind !== 'canvas') ? [nodeId] : []; selectionMode = 'pan'; focusNodeId = record ? nodeId : null; focusBranch = false;
    if (nodeId) for (const parent of navigationIndex(runtime).ancestors(nodeId).reverse()) {
      if (parent.kind !== 'canvas') revealGridChildren(parent.id, true);
    }
    if(nodeId)activateGalleryPath(nodeId);
    normalizeCanvas(canvasId);
    render();
  }
  function travel(from: Place[], to: Place[]): void {
    const place = from.pop(); if (!place) return;
    to.push(currentPlace());
    if (place.view) views.set(place.canvasId, { ...place.view });
    navigate(place.canvasId, place.selection, false);
  }
  function inspect(id: string): void {
    activateGalleryPath(id);
    selection = id; selections = runtime.nodes.some(node => node.id === id && node.kind !== 'canvas') ? [id] : []; render();
  }
  function drawNavigation(): void {
    selectedHub?.dispose(); selectedHub = undefined;
    const activeNode = list.contains(document.activeElement) ? (document.activeElement as HTMLElement).dataset.navigationNode : undefined;
    const index = navigationIndex(runtime); list.replaceChildren(); context.replaceChildren();
    const query = search.value.trim();
    if (query) {
      const results = searchProject(query);
      list.append(element('p', 'mv-navigation-note', results.length ? `${results.length}${results.length === 100 ? '+' : ''} results · all Works` : 'No matching nodes or attachments.'));
      for (const result of results) {
        const item = control('', `Go to ${result.title} in ${result.canvasTitle}`, () => navigate(result.canvasId, result.nodeId));
        item.dataset.navigationNode = result.nodeId; item.className = 'mv-search-result'; item.append(element('strong', '', result.title), element('small', '', `${result.canvasTitle} · ${result.source}`), element('span', '', result.snippet)); list.append(item);
      }
    } else {
      list.append(element('p', 'mv-navigation-note', `${workTitle(canvasId)} · outline`));
      for (const { node, depth } of index.outline(canvasId, true)) {
        const hidden = index.ancestors(node.id).some(parent => parent.collapsed);
        const item = control(`${node.collapsed ? '▸ ' : ''}${displayTag(node)}${hidden ? ' · hidden' : ''}`, `Go to ${displayTag(node)}`, () => navigate(node.canvasId, node.id));
        item.dataset.navigationNode = node.id; item.className = `mv-outline-node${selection === node.id ? ' is-selected' : ''}`; item.style.paddingLeft = `${10 + Math.min(depth, 8) * 12}px`; list.append(item);
      }
    }
    if (activeNode) [...list.querySelectorAll<HTMLButtonElement>('button')].find(button => button.dataset.navigationNode === activeNode)?.focus();
    const selected = selection ? index.nodes.get(selection) : undefined;
    if (!selected) {
      const connection=runtime.links.find(link=>link.id===selection);if(!connection)return;
      context.append(element('strong','',connection.label||'Connection'),element('p','mv-navigation-note',`${connection.kind} · ${connection.relation} · ${index.isLinkRevealed(connection)?index.isEmbeddedLink(connection)?'embedded · no line':'revealed':'hidden'}`));
      const owner=index.linkParent(connection);if(owner)context.append(control(`Contained in ${displayTag(owner)}`,`Inspect containing node ${displayTag(owner)}`,()=>inspect(owner.id)));
      for(const endpointId of [connection.sourceId,connection.targetId]){const endpoint=index.nodes.get(endpointId);if(endpoint)context.append(control(displayTag(endpoint),`Inspect endpoint ${displayTag(endpoint)}`,()=>inspect(endpoint.id)));}
      return;
    }
    context.append(element('strong', '', displayTag(selected)));
    const path = index.ancestors(selected.id).reverse().map(node => displayTag(node)).join(' › ');
    context.append(element('p', 'mv-navigation-note', path));
    if (selected.kind === 'reference' || selected.kind === 'canvas-link') {
      const original = index.original(selected.id);
      if (original) context.append(control('Open original context ↗', 'Open original context', () => navigate(original.canvasId, original.id)));
      else context.append(element('p', 'mv-navigation-note', 'Original context is not included in this publication.'));
    }
    if(selected.kind !== 'block') selectedHub = appendReadonlyNodeHub(context, runtime, selected);
    const {nodes:childNodes,links:connections}=index.contents(selected.id);
    context.append(element('strong','mv-contents-heading',`Contents · ${childNodes.length} ${childNodes.length===1?'node':'nodes'} · ${connections.length} ${connections.length===1?'connection':'connections'}`));
    for(const child of childNodes){const row=element('div','mv-child-content');row.append(control(displayTag(child),`Select child ${displayTag(child)}`,()=>inspect(child.id)),element('small','',`${nodeKindLabel(child.kind)} · ${index.visibleNode(child.id)?.id===child.id?'revealed':'hidden'}`));if(child.kind !== 'block' && child.text)row.append(element('p','',child.text));context.append(row);}
    if(connections.length){context.append(element('strong','mv-contents-heading','Connections'));for(const connection of connections){const source=index.nodes.get(connection.sourceId),target=index.nodes.get(connection.targetId),title=connection.label||`${source ? displayTag(source) : 'Source'} / ${target ? displayTag(target) : 'Destination'}`;const row=element('div','mv-child-content');row.append(control(title,`Select connection ${title}`,()=>inspect(connection.id)),element('small','',`${connection.relation} · ${index.isLinkRevealed(connection)?index.isEmbeddedLink(connection)?'embedded · no line':'revealed':'hidden'}`));context.append(row);}}
    if(documentNodeAsset(runtime,selected))context.append(control(documentNodeExpanded(selected)?'Collapse document':'Read document','Toggle document reader',()=>{revealGridChildren(selected.id,!documentNodeExpanded(selected));render();}));
    if(selected.kind!=='canvas'&&(childNodes.length||connections.length))context.append(control(selected.collapsed?'Reveal contents':'Hide contents','Toggle contents from properties',()=>{revealGridChildren(selected.id,!!selected.collapsed);render();}));
    const references = index.backlinks(selected.id);
    context.append(element('p', 'mv-navigation-note', `Referenced here · ${references.length}`));
    for (const reference of references) context.append(control(`${displayTag(reference)} · ${workTitle(reference.canvasId)}`, `Open reference ${displayTag(reference)}`, () => navigate(reference.canvasId, reference.id)));
  }
  function render(): void {
    const index = navigationIndex(runtime);
    selections = selections.filter(id => { const node = index.nodes.get(id); return node && node.canvasId === canvasId && node.kind !== 'canvas' && (index.isNodeVisible(id)||galleryOwner(runtime,id)); });
    state.textContent = running ? 'Running' : 'Stopped'; state.className = `mv-status${running ? ' is-running' : ''}`;
    run.disabled = running; stop.disabled = !running; canvasSelect.value = canvasId; back.disabled = !past.length; forward.disabled = !future.length;
    area.setAttribute('aria-pressed', String(selectionMode === 'region')); intersection.hidden = selectionMode !== 'region';
    selectionCount.textContent = `${selections.length} selected`; clearSelection.disabled = downloadSelection.disabled = !selections.length;
    selectionHint.textContent = selectionMode === 'region' ? 'Drag on empty Work to select. Shift-drag adds to the selection.' : 'Shift-click or Ctrl-click to select several nodes.';
    renderScene(scene, runtime, canvasId, {
      windowOffsets:Object.fromEntries(windowOffsets),onWindowScroll:(id,at)=>windowOffsets.set(id,at),
      galleryItems:Object.fromEntries(galleryActive),onGalleryPage:(parentId,itemId)=>{galleryActive.set(parentId,itemId);if(selection&&navigationIndex(runtime).ancestors(selection).some(parent=>parent.id===parentId)){selection=itemId;selections=[itemId];}render();},onGalleryInspect:id=>{inspect(id);panel.hidden=false;explore.setAttribute('aria-expanded','true');},
      readonly: true, running, selection, selections, selectionMode, regionIntersection: intersection.value as 'contained' | 'intersecting', evaluation: running ? evaluate(runtime) : undefined, view: views.get(canvasId), fit: fitOnEntry || !views.has(canvasId), focusNodeId, focusBranch,
      onViewChange: view => views.set(canvasId, view),
      onSelections: (ids, anchor) => {
        clearTimeout(inspectorTimer); selections = ids; selection = ids.at(-1) ?? null; render();
        const id = selection;
        if (id && ids.length === 1 && selectionMode === 'pan' && panel.hidden) {
          const show = () => { if (selection === id) { panel.hidden = false; explore.setAttribute('aria-expanded', 'true'); } };
          // The overlay must not steal the second click of a disclosure gesture.
          // Keyboard inspection opens immediately; pointer selection is immediate,
          // with the closed drawer appearing after the shared 500 ms click window.
          if (anchor) inspectorTimer = setTimeout(show, 550); else show();
        }
      },
      onLinkSelect: id => { clearTimeout(inspectorTimer); inspect(id); },
      onToggle: (id, field, value) => {
        const node = runtime.nodes.find(item => item.id === id); if (!node || (field === 'closed' && (node.kind !== 'switch' || !node.exposed))) return;
        if(field==='collapsed'){revealGridChildren(id,!value);reflowSmartLinks(runtime,canvasId);}else node[field]=value; render();
      },
      onValue: (id, value) => {
        const node = runtime.nodes.find(item => item.id === id);
        if (!node?.exposed || !(node.kind === 'input' && typeof value === 'boolean' || node.kind === 'number' && typeof value === 'number' && Number.isFinite(value))) return;
        node.value = value; render();
      },
      onCanvasOpen: target => navigate(target),
      onAssetOpen: id => openAssetPreview(runtime, id),
    });
    fitOnEntry = false; focusNodeId = null; focusBranch = false; drawNavigation();
  }
  normalizeCanvas(canvasId);
  render();
}

const host = document.getElementById('meridian-viewer');
const data = document.getElementById('meridian-data');
if (!bootMediaWindow() && host && data) {
  try {
    const payload = JSON.parse(data.textContent ?? '') as PublicationPayload;
    if (payload.format !== 'atlas-meridian-publication' || payload.version !== 1 || payload.project?.schemaVersion !== 1) throw new Error('This publication version is not supported.');
    mountViewer(host, payload);
  } catch (error) {
    host.replaceChildren(element('p', 'mv-error', error instanceof Error ? error.message : 'This publication could not be opened.'));
  }
}
