import type { ContainerVariant, LinkRelation, MeridianLink, MeridianNode, MeridianProject, NodeKind, UtilityVariant } from './core/types.ts';
import { nodeTag, NODE_TAG_MAX_LENGTH } from './core/node-tag.ts';
import { workLibraryDisplayText, nodeKindLabel } from './core/work-terminology.ts';
import { createProjectSearch, linkDirection, navigationIndex } from './core/navigation.ts';
import { resolveSnapping } from './core/snapping.ts';
import { CONTAINER_DESCRIPTIONS, CONTAINER_CHOICES } from './container-descriptions.ts';

export type InspectorPanel = 'canvas' | 'properties' | 'appearance' | 'content' | 'relations' | 'links' | 'module';
export type AlignmentMode = 'inherit' | 'free' | 'align' | 'sticky';
type Action = () => void | Promise<void>;
const panels: ReadonlyArray<{ value: InspectorPanel; label: string }> = [
  { value: 'canvas', label: 'Work' }, { value: 'properties', label: 'Properties' },
  { value: 'appearance', label: 'Appearance' },
  { value: 'relations', label: 'Relations' }, { value: 'links', label: 'Links' }, { value: 'module', label: 'Module' },
];
const storageKey = 'meridian.inspector-panel';
const outlineState = new Map<string, Set<string>>();
const outlineDetails = new Map<string, Set<string>>();
const outlineSelection = new Map<string, string | null | undefined>();
const outlineSearchBranches = new Map<string, { query: string; closed: Set<string> }>();
const outlineQuery = new Map<string, string>();
const outlineScope = new Map<string, 'canvas' | 'all'>();
const OUTLINE_PAGE_SIZE = 100;
const relations: ReadonlyArray<{ value: LinkRelation; label: string }> = [
  { value: 'generic', label: 'Generic' }, { value: 'causal', label: 'Causal' },
  { value: 'chronological', label: 'Chronological' }, { value: 'mutual', label: 'Mutual' },
  { value: 'cyclical', label: 'Cyclical' },
];

export interface InspectorPanelOptions {
  project: MeridianProject; canvasId: string; nodeId?: string | null; linkId?: string | null;
  panel: InspectorPanel;
  onPanelChange: (panel: InspectorPanel) => void;
  onJumpToNode: (id: string) => void | Promise<void>;
  onSelectNode?: (id: string) => void | Promise<void>;
  /** A library-wide tier can continue through canvas links; standalone outlines fall back to their local depth. */
  getNodeTier?: (id: string) => number;
  onDeleteNode: (id: string) => void | Promise<void>;
  onInspectLink: (id: string) => void | Promise<void>;
  onDeleteLink?: (id: string) => void | Promise<void>;
  onCreateLink?: (nodeId: string) => void | Promise<void>;
  onChangeNodeType?: (nodeId: string, type: { kind: NodeKind; containerVariant?: ContainerVariant; utilityVariant?: UtilityVariant }) => void | Promise<void>;
  onChangeNodeTag?: (nodeId: string, tag: string) => void | Promise<void>;
  onToggleCluster?: (nodeId: string) => void | Promise<void>;
  onChangeAlignment?: (nodeId: string, mode: AlignmentMode) => void | Promise<void>;
  onRenameNode?: (nodeId: string, newId: string) => void | Promise<void>;
  onCreateRemoteLink?: (nodeId: string, link: { targetId: string; relation: LinkRelation; label?: string }) => void | Promise<void>;
  onDeleteRemoteLink?: (nodeId: string, remoteLinkId: string) => void | Promise<void>;
  /** Content, Appearance, Relations, and a selected connection's editor use the same shell. */
  renderPanel?: (panel: InspectorPanel, body: HTMLElement) => void;
  onClose?: () => void;
}

export function loadInspectorPanel(fallback: InspectorPanel = 'canvas'): InspectorPanel {
  const safeFallback = fallback === 'content' ? 'properties' : fallback;
  try { const saved = localStorage.getItem(storageKey); if (saved === 'content') { saveInspectorPanel('properties'); return 'properties'; } return panels.some(panel => panel.value === saved) ? saved as InspectorPanel : safeFallback; }
  catch { return safeFallback; }
}
export function saveInspectorPanel(panel: InspectorPanel): void {
  try { localStorage.setItem(storageKey, panel === 'content' ? 'properties' : panel); } catch { /* Storage is optional for the panel choice. */ }
}
function element<K extends keyof HTMLElementTagNameMap>(tag: K, className = '', text?: string): HTMLElementTagNameMap[K] {
  const result = document.createElement(tag); result.className = className; if (text !== undefined) result.textContent = text; return result;
}
function button(text: string, label: string, action: Action, className = ''): HTMLButtonElement {
  const result = element('button', className, text); result.type = 'button'; result.title = label; result.setAttribute('aria-label', label);
  result.onclick = () => { void action(); }; return result;
}
/** A paired edge control: point toward the edge to close, toward the canvas to reopen. */
export function createInspectorToggleButton(open: boolean, onToggle: () => void): HTMLButtonElement {
  const toggle = button('', open ? 'Close side panel' : 'Show side panel', onToggle, 'inspector-panel-toggle');
  toggle.setAttribute('aria-expanded', String(open));
  toggle.setAttribute('aria-controls', 'meridian-side-panel');
  const arrow = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  arrow.setAttribute('viewBox', '0 0 24 24');
  arrow.setAttribute('aria-hidden', 'true');
  arrow.setAttribute('focusable', 'false');
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute('d', open ? 'M20 12H4m7-7-7 7 7 7' : 'M4 12h16m-7-7 7 7-7 7');
  arrow.append(path); toggle.append(arrow);
  return toggle;
}
function field(label: string, control: HTMLElement): HTMLElement {
  const row = element('label', 'field'); control.setAttribute('aria-label', label); row.append(element('span', 'field-label', label), control); return row;
}
function select(items: ReadonlyArray<{ value: string; label: string }>, value: string): HTMLSelectElement {
  const result = element('select'); for (const item of items) result.append(new Option(item.label, item.value)); result.value = value; return result;
}
function errorMessage(error: unknown): string { return error instanceof Error ? error.message : String(error); }
function createActionBoundary(host: HTMLElement): (action: Action) => Promise<void> {
  const error = element('p', 'inspector-error'); error.setAttribute('role', 'alert'); host.append(error);
  return async action => { error.textContent = ''; try { await action(); } catch (failure) { error.textContent = errorMessage(failure); } };
}
export function inspectorNodeType(node: MeridianNode): string {
  if (node.cluster) return 'Cluster';
  if (node.kind === 'canvas' || node.kind === 'canvas-link') return nodeKindLabel(node.kind);
  if (['media', 'block', 'image', 'text', 'document'].includes(node.kind)) return 'Media';
  if (node.kind === 'container' || node.kind === 'spreadsheet') return 'Container';
  if (node.kind === 'junction') return node.utilityVariant === 'pivot' ? 'Utility · Pivot' : 'Utility · Joint';
  if (node.kind === 'joint') return 'Operation';
  return node.kind.charAt(0).toUpperCase() + node.kind.slice(1);
}
function relationGlyph(link: MeridianLink): string {
  if (link.snap) return link.snap.locked ? '🔒' : '⋈';
  if (link.kind === 'functional') return 'ƒ';
  if (link.kind === 'numeric') return '#';
  if (link.relation === 'chronological') return '⇥';
  if (link.relation === 'cyclical') return '⟳';
  const direction = linkDirection(link); return direction === 'both' ? '↔' : direction === 'reverse' ? '←' : direction === 'none' ? '—' : '→';
}
function associatedLinkIndex(project: MeridianProject): Map<string, MeridianLink[]> {
  const index = navigationIndex(project), result = new Map<string, MeridianLink[]>();
  for (const link of project.links) for (const id of new Set([link.sourceId, link.targetId, index.linkParent(link)?.id].filter((id): id is string => !!id))) {
    const links = result.get(id) ?? []; links.push(link); result.set(id, links);
  }
  return result;
}

/** Small vector controls keep their proportions at every panel width. */
function outlineIcon(kind: 'target' | 'plus' | 'chain' | 'children' | 'expandable' | 'tier' | 'delete'): SVGSVGElement {
  const ns = 'http://www.w3.org/2000/svg', svg = document.createElementNS(ns, 'svg');
  svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('aria-hidden', 'true'); svg.setAttribute('focusable', 'false');
  const shape = (tag: 'path' | 'circle' | 'rect', attrs: Record<string, string>) => {
    const item = document.createElementNS(ns, tag); for (const [name, value] of Object.entries(attrs)) item.setAttribute(name, value); svg.append(item);
  };
  if (kind === 'target') {
    shape('circle', { cx: '12', cy: '12', r: '7' }); shape('circle', { cx: '12', cy: '12', r: '3' });
    shape('path', { d: 'M12 2v4m0 12v4M2 12h4m12 0h4' });
  } else if (kind === 'plus') {
    shape('path', { d: 'M12 5v14M5 12h14' });
  } else if (kind === 'chain') {
    shape('path', { d: 'm9 15 6-6m-7 5-1 1a4.25 4.25 0 0 1-6-6l4-4a4.25 4.25 0 0 1 6 0m2 5 1-1a4.25 4.25 0 0 1 6 6l-4 4a4.25 4.25 0 0 1-6 0', transform: 'translate(1 1)' });
  } else if (kind === 'children') {
    shape('rect', { x: '2.5', y: '2.5', width: '19', height: '19', rx: '2' });
    for (const [x, y] of [[6, 6], [14, 6], [10, 14]]) shape('rect', { x: String(x), y: String(y), width: '4', height: '4', rx: '.5' });
  } else if (kind === 'expandable') {
    shape('rect', { x: '3', y: '3', width: '18', height: '18', rx: '1' });
    shape('rect', { x: '6', y: '12', width: '6', height: '6', rx: '.5' });
  } else if (kind === 'tier') {
    shape('path', { d: 'M6 3v18m0-14h7l5-4M6 14h7l5 4' });
    for (const [cx, cy] of [[6, 3], [18, 3], [18, 18]]) shape('circle', { cx: String(cx), cy: String(cy), r: '1.5' });
  } else {
    shape('rect', { x: '3', y: '3', width: '18', height: '18', rx: '2' });
    shape('path', { d: 'm8 8 8 8m0-8-8 8', class: 'inspector-delete-cross' });
  }
  return svg;
}

/** All workspaces share one inspector. Searching and outlining are confined to Canvas. */
export function renderInspectorPanels(host: HTMLElement, options: InspectorPanelOptions): void {
  // Content now belongs to the selected node's Hub. Keep old saved panel choices
  // and callers readable without exposing a duplicate Content tab.
  if (options.panel === 'content') options = { ...options, panel: 'properties' };
  host.id = 'meridian-side-panel';
  const shell = element('div', 'inspector-panel-shell');
  const header = element('div', 'inspector-panel-header');
  const choice = select(panels, options.panel); choice.className = 'inspector-panel-choice'; choice.setAttribute('aria-label', 'Side panel');
  choice.onchange = () => { const panel = choice.value as InspectorPanel; saveInspectorPanel(panel); options.onPanelChange(panel); };
  if (options.onClose) header.append(createInspectorToggleButton(true, options.onClose));
  header.append(choice);
  const body = element('section', `inspector-panel-body inspector-panel-${options.panel}`);
  body.setAttribute('aria-label', `${panels.find(panel => panel.value === options.panel)?.label ?? 'Node'} panel`);
  shell.append(header, body); host.append(shell);
  if (options.panel === 'canvas') { renderCanvasPanel(body, options); return; }
  const node = options.project.nodes.find(node => node.id === options.nodeId);
  if (options.panel === 'properties') { renderPropertiesPanel(body, node, options); options.renderPanel?.('properties', body); return; }
  if (options.panel === 'links') {
    if (options.linkId && options.project.links.some(link => link.id === options.linkId)) { options.renderPanel?.('links', body); if (body.childNodes.length) return; }
    renderLinksPanel(body, node, options); return;
  }
  options.renderPanel?.(options.panel, body);
  if (options.panel === 'appearance') for (const details of body.querySelectorAll<HTMLDetailsElement>('.appearance-panel')) details.open = true;
  if (!body.childNodes.length) body.append(element('p', 'inspector-empty', `Select a node to edit its ${options.panel}.`));
}

function renderCanvasPanel(host: HTMLElement, options: InspectorPanelOptions): void {
  const { project, canvasId } = options, index = navigationIndex(project);
  // Ownership controls disclosure; only endpoints describe this node's links.
  const linksByNode = new Map<string, MeridianLink[]>();
  for (const link of project.links) for (const id of new Set([link.sourceId, link.targetId])) {
    const incident = linksByNode.get(id) ?? []; incident.push(link); linksByNode.set(id, incident);
  }
  const remoteByNode = new Map<string, Array<{ source: MeridianNode; link: NonNullable<MeridianNode['remoteLinks']>[number] }>>();
  for (const source of project.nodes) for (const link of source.remoteLinks ?? []) for (const id of new Set([source.id, link.targetId])) {
    const incident = remoteByNode.get(id) ?? []; incident.push({ source, link }); remoteByNode.set(id, incident);
  }
  // A plain hierarchy needs no document search index. Build it only when the user searches.
  let search: ReturnType<typeof createProjectSearch> | undefined;
  const canvas = project.canvases.find(canvas => canvas.id === canvasId), root = index.nodes.get(canvas?.rootId ?? '');
  const key = `${project.id}:${canvasId}`;
  let expanded = outlineState.get(key);
  if (!expanded) { expanded = new Set(root ? [root.id] : []); outlineState.set(key, expanded); }
  if (options.nodeId && outlineSelection.get(key) !== options.nodeId) for (const ancestor of index.ancestors(options.nodeId)) expanded.add(ancestor.id);
  outlineSelection.set(key, options.nodeId);
  let details = outlineDetails.get(key); if (!details) { details = new Set(); outlineDetails.set(key, details); }
  let searchBranches = outlineSearchBranches.get(key); if (!searchBranches) { searchBranches = { query: '', closed: new Set() }; outlineSearchBranches.set(key, searchBranches); }
  const input = element('input', 'canvas-node-search'); input.type = 'search'; input.placeholder = 'Search a GUID or Name';
  input.setAttribute('aria-label', 'Search Work nodes'); input.value = outlineQuery.get(key) ?? '';
  const scope = select([{ value: 'canvas', label: 'This Work' }, { value: 'all', label: 'All Works' }], outlineScope.get(key) ?? 'canvas');
  scope.className = 'canvas-search-scope'; scope.setAttribute('aria-label', 'Search scope');
  const controls = element('div', 'canvas-search-controls'); controls.append(input, scope);
  const count = element('p', 'inspector-outline-announcement'); count.setAttribute('role', 'status');
  const tree = element('div', 'inspector-canvas-tree'); tree.setAttribute('role', 'tree'); tree.setAttribute('aria-label', 'Work node hierarchy');
  const action = createActionBoundary(host);
  let limit = OUTLINE_PAGE_SIZE;
  const more = button('Show more', 'Show more Work nodes', () => { limit += OUTLINE_PAGE_SIZE; draw(); }, 'quiet full inspector-outline-more');
  host.append(controls, count, tree, more);
  const draw = () => {
    const active = document.activeElement as HTMLElement | null, focusedRow = active?.closest<HTMLElement>('[data-outline-node]');
    const focusedId = tree.contains(focusedRow ?? null) ? focusedRow?.dataset.outlineNode : undefined;
    const focusedControl = active?.hasAttribute('data-outline-more') ? '[data-outline-more]' : active?.hasAttribute('data-jump-to-node') ? '[data-jump-to-node]' : '.inspector-tree-title';
    const scroll = host.scrollTop;
    tree.replaceChildren(); const query = input.value.trim(); outlineQuery.set(key, input.value);
    const queryKey = `${scope.value}:${query}`;
    if (searchBranches!.query !== queryKey) { searchBranches!.query = queryKey; searchBranches!.closed.clear(); }
    scope.hidden = !query; if (!query) scope.value = 'canvas'; outlineScope.set(key, scope.value as 'canvas' | 'all');
    const all = !!query && scope.value === 'all';
    const nodes = project.nodes.filter(node => all || node.canvasId === canvasId);
    const results = query ? (search ??= createProjectSearch(project))(query, project.nodes.length).filter(result => all || result.canvasId === canvasId) : [];
    const resultById = new Map(results.map(result => [result.nodeId, result]));
    const matches = query ? new Set(resultById.keys()) : undefined;
    const included = new Set(matches);
    if (matches) for (const id of matches) for (const ancestor of index.ancestors(id)) if (all || ancestor.canvasId === canvasId) included.add(ancestor.id);
    const seen = new Set<string>(); let entries = 0;
    const add = (node: MeridianNode, depth: number): MeridianNode[] => {
      if (seen.has(node.id) || !all && node.canvasId !== canvasId || matches && !included.has(node.id)) return [];
      seen.add(node.id);
      const directChildren = (index.children.get(node.id) ?? []).filter(child => child.canvasId === node.canvasId);
      const children = directChildren.filter(child => !matches || included.has(child.id));
      const hasChildren = directChildren.length > 0, isExpanded = hasChildren && (matches ? !searchBranches!.closed.has(node.id) : expanded!.has(node.id));
      entries++; if (entries > limit) return isExpanded ? children : [];
      const outside = node.canvasId !== canvasId;
      const row = element('div', 'inspector-tree-row'); row.dataset.outlineNode = node.id; row.style.setProperty('--outline-depth', String(Math.min(depth, 12))); row.classList.toggle('is-nested', depth > 0);
      row.dataset.searchMatch = String(matches?.has(node.id) ?? false);
      row.classList.toggle('is-selected', options.nodeId === node.id); row.classList.toggle('is-hidden', !index.isNodeVisible(node.id));
      row.setAttribute('role', 'treeitem'); row.setAttribute('aria-level', String(depth + 1)); row.setAttribute('aria-selected', String(options.nodeId === node.id));
      if (hasChildren) row.setAttribute('aria-expanded', String(isExpanded));
      const name = workLibraryDisplayText(project.id, node.id, nodeTag(node)), type = inspectorNodeType(node);
      const chooseNode = () => {
        if (hasChildren) {
          if (matches) { if (isExpanded) searchBranches!.closed.add(node.id); else searchBranches!.closed.delete(node.id); }
          else if (isExpanded) expanded!.delete(node.id); else expanded!.add(node.id);
        }
        draw(); tree.querySelector<HTMLButtonElement>(`[data-outline-node="${CSS.escape(node.id)}"] .inspector-tree-title`)?.focus({ preventScroll: true });
        void action(() => options.onSelectNode?.(node.id));
      };
      const cell = element('div', 'inspector-tree-cell');
      const choose = button('', `Node: ${name}`, chooseNode, 'inspector-tree-title'); choose.title = name;
      if (hasChildren) choose.setAttribute('aria-expanded', String(isExpanded));
      const typeLabel = element('span', 'inspector-tree-type', type); typeLabel.title = type;
      const nameGroup = element('span', 'inspector-tree-label'); nameGroup.append(element('span', 'inspector-tree-name', name));
      if (hasChildren) { const expandable = element('span', 'inspector-tree-expandable'); expandable.title = 'Contains sub-nodes'; expandable.dataset.outlineExpandable = node.id; expandable.append(outlineIcon('expandable')); nameGroup.append(expandable); }
      choose.append(nameGroup, typeLabel);
      const actions = element('div', 'inspector-tree-actions');
      const jump = button('', `Jump to Node: ${name}`, () => action(() => options.onJumpToNode(node.id)), 'inspector-tree-jump');
      jump.dataset.jumpToNode = node.id; jump.append(outlineIcon('target'));
      const detailId = `outline-details-${node.id}`;
      const propertiesOpen = details!.has(node.id);
      const info = button('', `More: ${name}`, () => { if (details!.has(node.id)) details!.delete(node.id); else details!.add(node.id); draw(); }, 'inspector-tree-more');
      info.append(outlineIcon('plus'));
      info.title = 'Details';
      info.dataset.outlineMore = node.id; info.setAttribute('aria-expanded', String(propertiesOpen)); if (propertiesOpen) info.setAttribute('aria-controls', detailId);
      actions.append(jump, info); cell.append(choose, actions); row.append(cell);
      row.onclick = event => { if (!(event.target as Element).closest('button,a,input,textarea,select,[data-outline-details]')) chooseNode(); };
      const result = resultById.get(node.id); if (result) row.dataset.searchSource = result.source;
      if (propertiesOpen) {
        const drawer = element('div', 'inspector-tree-details'); drawer.dataset.outlineDetails = node.id; drawer.id = detailId;
        drawer.setAttribute('role', 'group'); drawer.setAttribute('aria-label', `Properties: ${name}`);
        if (!outside) {
          const remove = button('', `Delete node: ${name}`, () => action(() => options.onDeleteNode(node.id)), 'inspector-tree-delete');
          remove.dataset.deleteNode = node.id; remove.append(outlineIcon('delete')); remove.disabled = node.kind === 'canvas';
          if (remove.disabled) remove.title = 'The work root cannot be deleted.';
          drawer.append(remove);
        }
        const identity = element('div', 'inspector-tree-identity');
        identity.append(element('span', 'inspector-tree-detail-label', 'GUID'), element('code', 'inspector-tree-guid', node.id)); drawer.append(identity);
        const connections = linksByNode.get(node.id) ?? [], remote = remoteByNode.get(node.id) ?? [];
        const stats = element('div', 'inspector-tree-stats');
        const count = (kind: 'children' | 'chain', value: number, label: string) => { const stat = element('span', 'inspector-tree-stat'); stat.title = `${value} ${label}`; stat.setAttribute('aria-label', stat.title); stat.append(outlineIcon(kind), element('span', '', `${value} ${label}`)); return stat; };
        const subnodes = count('children', (index.children.get(node.id) ?? []).length, 'sub-nodes'); subnodes.dataset.subnodeCount = String((index.children.get(node.id) ?? []).length);
        const linkCount = count('chain', connections.length + remote.length, 'links'); linkCount.dataset.linkCount = String(connections.length + remote.length);
        const tierValue = options.getNodeTier?.(node.id) ?? depth + 1;
        const tier = Number.isFinite(tierValue) && tierValue >= 1 ? Math.floor(tierValue) : depth + 1;
        const tierStat = element('span', 'inspector-tree-stat inspector-tree-tier'); tierStat.title = `Tier ${tier}`; tierStat.setAttribute('aria-label', tierStat.title); tierStat.dataset.nodeTier = String(tier); tierStat.append(outlineIcon('tier'), element('span', '', String(tier)));
        stats.append(subnodes, linkCount, tierStat); drawer.append(stats);
        row.append(drawer);
      }
      tree.append(row);
      return isExpanded ? children : [];
    };
    const walk = (node: MeridianNode, depth: number) => {
      const pending = [{ node, depth }];
      while (pending.length) {
        const next = pending.pop()!, children = add(next.node, next.depth);
        for (let at = children.length - 1; at >= 0; at--) pending.push({ node: children[at]!, depth: next.depth + 1 });
      }
    };
    if (all) for (const item of project.canvases) { const itemRoot = index.nodes.get(item.rootId); if (itemRoot) walk(itemRoot, 0); }
    else if (root) walk(root, 0);
    // Preserve access to disconnected imported nodes without inventing another canvas.
    for (const node of nodes) if (!seen.has(node.id) && (!node.parentId || !index.nodes.has(node.parentId))) walk(node, 0);
    if (!tree.childNodes.length) tree.append(element('p', 'inspector-empty', matches ? all ? 'No matching nodes in the included works.' : 'No matching nodes on this work.' : 'This work has no nodes.'));
    count.textContent = matches ? `${matches.size} ${matches.size === 1 ? 'matching node' : 'matching nodes'}` : `${nodes.filter(node => node.id !== root?.id).length} nodes · includes hidden contents`;
    if (entries > limit) count.textContent += ` · showing ${limit} of ${entries} outline entries`;
    more.hidden = entries <= limit;
    if (focusedId) {
      const row = tree.querySelector<HTMLElement>(`[data-outline-node="${CSS.escape(focusedId)}"]`);
      row?.querySelector<HTMLButtonElement>(focusedControl)?.focus({ preventScroll: true });
    }
    host.scrollTop = scroll;
  };
  input.oninput = () => { limit = OUTLINE_PAGE_SIZE; draw(); };
  scope.onchange = () => { limit = OUTLINE_PAGE_SIZE; draw(); };
  input.onkeydown = event => {
    if (event.key === 'Enter' || event.key === 'ArrowDown') { const match = tree.querySelector<HTMLButtonElement>(input.value.trim() ? `[data-search-match="true"] ${event.key === 'Enter' ? '[data-jump-to-node]' : '.inspector-tree-title'}` : event.key === 'Enter' ? '[data-jump-to-node]' : '.inspector-tree-title'); if (match) { event.preventDefault(); event.key === 'Enter' ? match.click() : match.focus(); } }
    if (event.key === 'Escape' && input.value) { event.preventDefault(); event.stopPropagation(); input.value = ''; limit = OUTLINE_PAGE_SIZE; draw(); }
  };
  tree.onkeydown = event => {
    const row = (event.target as HTMLElement).closest<HTMLElement>('[data-outline-node]');
    if (!row || (event.target as Element).closest('[data-outline-details]') || !['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault(); const rows = [...tree.querySelectorAll<HTMLElement>('[data-outline-node]')], at = rows.indexOf(row);
    const focus = (next: HTMLElement | undefined) => next?.querySelector<HTMLButtonElement>('.inspector-tree-title')?.focus();
    if (event.key === 'ArrowUp') at === 0 ? input.focus() : focus(rows[at - 1]);
    else if (event.key === 'ArrowDown') focus(rows[Math.min(rows.length - 1, at + 1)]);
    else if (event.key === 'Home') focus(rows[0]);
    else if (event.key === 'End') focus(rows.at(-1));
    else if (event.key === 'ArrowRight') {
      if (row.getAttribute('aria-expanded') === 'false') { const id = row.dataset.outlineNode!; if(input.value.trim())searchBranches!.closed.delete(id);else expanded!.add(id); draw(); focus(tree.querySelector<HTMLElement>(`[data-outline-node="${CSS.escape(id)}"]`) ?? undefined); }
      else if (row.getAttribute('aria-expanded') === 'true') focus(rows[at + 1]);
    } else if (row.getAttribute('aria-expanded') === 'true') {
      const id = row.dataset.outlineNode!; if(input.value.trim())searchBranches!.closed.add(id);else expanded!.delete(id); draw(); focus(tree.querySelector<HTMLElement>(`[data-outline-node="${CSS.escape(id)}"]`) ?? undefined);
    } else {
      const level = Number(row.getAttribute('aria-level'));
      for (let previous = at - 1; previous >= 0; previous--) if (Number(rows[previous]!.getAttribute('aria-level')) < level) { focus(rows[previous]); break; }
    }
  };
  draw();
}

function renderPropertiesPanel(host: HTMLElement, node: MeridianNode | undefined, options: InspectorPanelOptions): void {
  if (!node) { host.append(element('p', 'inspector-empty', 'Select a node to edit its properties.')); return; }
  const action = createActionBoundary(host);
  const tag = element('input'); tag.type = 'text'; tag.value = nodeTag(node); tag.required = true; tag.maxLength = NODE_TAG_MAX_LENGTH; tag.autocomplete = 'off';
  tag.disabled = !options.onChangeNodeTag;
  tag.onchange = () => { void action(() => options.onChangeNodeTag?.(node.id, tag.value)); };
  host.append(field('Tag', tag), element('p', 'muted', 'The node’s name in navigation and search. Its visible title is separate.'));
  if (node.cluster) {
    const label = node.collapsed ? 'Expand Cluster' : 'Collapse Cluster';
    const toggle = button(label, label, () => action(() => options.onToggleCluster?.(node.id)), 'secondary full');
    toggle.disabled = !options.onToggleCluster; host.append(toggle);
  }
  const types = [
    { value: 'canvas-link', label: 'Work' }, { value: 'container', label: 'Container' }, { value: 'utility', label: 'Utility' }, { value: 'joint', label: 'Operation' },
    { value: 'media', label: 'Media' },
    { value: 'reference', label: 'Reference' }, { value: 'number', label: 'Number' },
    { value: 'input', label: 'Input' }, { value: 'switch', label: 'Switch' }, { value: 'output', label: 'Output' },
  ];
  const kind = node.kind === 'spreadsheet' ? 'container' : node.kind === 'junction' ? 'utility' : ['block','image','text','document'].includes(node.kind) ? 'media' : node.kind;
  const type = select(node.kind === 'canvas' ? [{ value: 'canvas', label: 'Work' }] : node.cluster ? [{ value: 'container', label: 'Cluster' }] : node.kind === 'module' ? [{ value: 'module', label: 'Module' }] : types, kind);
  type.disabled = node.kind === 'canvas' || node.kind === 'module' || !!node.cluster || !!node.smartLink || !options.onChangeNodeType;
  if (node.kind === 'canvas') type.title = 'This node is the work root.';
  if (node.kind === 'module') type.title = 'Modules are created from selected nodes in the Module tool.';
  type.onchange = () => { void action(() => options.onChangeNodeType?.(node.id, type.value === 'utility' ? { kind: 'junction', utilityVariant: 'pivot' } : { kind: type.value as NodeKind })); };
  host.append(field('Type', type));
  if (node.kind === 'junction' && !node.smartLink) {
    const subtype = select([{ value: 'pivot', label: 'Pivot' }, { value: 'joint', label: 'Joint' }], node.utilityVariant ?? 'joint');
    subtype.disabled = !options.onChangeNodeType;
    subtype.onchange = () => { void action(() => options.onChangeNodeType?.(node.id, { kind: 'junction', utilityVariant: subtype.value as UtilityVariant })); };
    host.append(field('Utility role', subtype), element('p', 'muted', subtype.value === 'pivot' ? 'A draggable turning point in a link path.' : 'A shared meeting point for incoming and outgoing links.'));
  }
  if (node.kind !== 'canvas' && !node.smartLink) {
    const selectedVariant = node.containerVariant === 'spatial' ? 'contained' : node.containerVariant ?? (node.kind === 'spreadsheet' ? 'spreadsheet' : 'contained');
    const subtype = select(CONTAINER_CHOICES, selectedVariant);
    for(const category of ['Unordered','Ordered'] as const){const group=document.createElement('optgroup');group.label=category;for(const choice of CONTAINER_CHOICES.filter(choice=>choice.category===category)){const option=[...subtype.options].find(option=>option.value===choice.value);if(option)group.append(option);}subtype.append(group);}subtype.value=selectedVariant;
    subtype.disabled = !!node.cluster || !options.onChangeNodeType;
    host.append(element('p','muted',`${['container','spreadsheet','module'].includes(node.kind)?'Bounded · children inside':'Unbounded · children outside'} · ${CONTAINER_CHOICES.find(choice=>choice.value===selectedVariant)?.category??'Unordered'}`));
    host.append(element('p','muted',CONTAINER_DESCRIPTIONS[subtype.value as ContainerVariant]));
    subtype.onchange = () => { const variant = subtype.value as ContainerVariant; void action(() => options.onChangeNodeType?.(node.id, { kind: node.kind, containerVariant: variant })); };
    host.append(field('Display Pattern', subtype));
    if (node.cluster) host.append(element('p', 'muted', 'A Cluster keeps its members’ positions and collapses into one button. Choose Square or Circle in Appearance.'));
  }
  const canvasRoot = options.project.canvases.find(canvas => canvas.rootId === node.id);
  const settings = canvasRoot ? canvasRoot.snapping : node.snapping;
  const effective = resolveSnapping(canvasRoot ?? options.project.canvases.find(canvas => canvas.id === node.canvasId), canvasRoot ? undefined : node);
  const mode: AlignmentMode = !settings || Object.values(settings).every(value => value === undefined) ? 'inherit' : effective.stickyEdges ? 'sticky' : effective.smartAlign || effective.snapToGrid ? 'align' : 'free';
  const alignment = select([
    { value: 'inherit', label: canvasRoot ? 'Default · Sticky' : 'Inherit Work behavior' }, { value: 'sticky', label: 'Sticky' },
    { value: 'align', label: 'Align to edges' }, { value: 'free', label: 'Free' },
  ], mode);
  alignment.disabled = !options.onChangeAlignment;
  alignment.onchange = () => { void action(() => options.onChangeAlignment?.(node.id, alignment.value as AlignmentMode)); };
  host.append(field('Alignment behavior', alignment));
  const guid = element('input'); guid.type = 'text'; guid.value = node.id; guid.maxLength = 96; guid.required = true; guid.pattern = '[A-Za-z0-9][A-Za-z0-9_-]{0,95}';
  guid.autocomplete = 'off'; guid.spellcheck = false; guid.setAttribute('aria-label', 'GUID');
  const identity = element('form', 'inspector-guid-editor');
  const row = element('div', 'inspector-guid-row');
  const apply = element('button', 'secondary inspector-guid-apply', 'Apply'); apply.type = 'submit'; apply.title = 'Apply GUID'; apply.setAttribute('aria-label', 'Apply GUID'); apply.disabled = !options.onRenameNode;
  row.append(guid, apply); identity.append(element('label', 'field-label', 'GUID'), row);
  identity.onsubmit = event => { event.preventDefault(); if (identity.reportValidity() && guid.value !== node.id) { void action(() => options.onRenameNode?.(node.id, guid.value)); } };
  host.append(identity);
}

function renderLinksPanel(host: HTMLElement, node: MeridianNode | undefined, options: InspectorPanelOptions): void {
  if (!node) { host.append(element('p', 'inspector-empty', 'Select a node to inspect its links.')); return; }
  const index = navigationIndex(options.project), action = createActionBoundary(host), links = associatedLinkIndex(options.project).get(node.id) ?? [];
  host.append(element('h3', 'inspector-section-title', 'Work links'));
  const list = element('div', 'inspector-links-list'); list.setAttribute('aria-label', 'Associated Work links');
  for (const link of links) {
    const row = element('div', 'inspector-link-row'); row.dataset.associatedLink = link.id;
    const source = index.nodes.get(link.sourceId), target = index.nodes.get(link.targetId), owner = index.linkParent(link);
    const title = `${source ? workLibraryDisplayText(options.project.id, source.id, nodeTag(source)) : link.sourceId} ${relationGlyph(link)} ${target ? workLibraryDisplayText(options.project.id, target.id, nodeTag(target)) : link.targetId}`;
    const inspect = button(link.label || title, `Inspect link: ${title}`, () => action(() => options.onInspectLink(link.id)), 'inspector-link-title');
    const status = index.isLinkRevealed(link) ? link.snap ? link.snap.locked ? 'Locked Cluster · no line' : 'Snapped edge · no line' : index.isEmbeddedLink(link) ? 'Embedded · no line' : 'Revealed' : owner?.collapsed ? `Hidden · ${workLibraryDisplayText(options.project.id, owner.id, nodeTag(owner))} collapsed` : 'Hidden · endpoint or owner concealed';
    row.append(inspect, element('span', 'inspector-link-meta', `${link.snap ? 'Snap' : link.kind === 'visual' ? link.relation : link.kind} · ${status}`));
    const roles = [link.sourceId === node.id ? 'source' : '', link.targetId === node.id ? 'destination' : '', owner?.id === node.id ? 'contains this link' : ''].filter(Boolean);
    row.append(element('span', 'inspector-link-meta', roles.join(' · ')), element('code', 'inspector-link-guid', link.id));
    if (options.onDeleteLink) row.append(button('−', `Delete link: ${title}`, () => action(() => options.onDeleteLink!(link.id)), 'inspector-link-remove'));
    list.append(row);
  }
  if (!links.length) list.append(element('p', 'inspector-empty', 'No work links associated with this node.'));
  host.append(list);
  if (options.onCreateLink) host.append(button('+ Work link', 'Create a Work link from this node', () => action(() => options.onCreateLink!(node.id)), 'secondary full'));
  host.append(element('h3', 'inspector-section-title', 'Remote links'));
  const remoteList = element('div', 'inspector-links-list'); remoteList.setAttribute('aria-label', 'Remote conceptual links');
  const remoteLinks = options.project.nodes.flatMap(source => (source.remoteLinks ?? []).filter(link => source.id === node.id || link.targetId === node.id).map(link => ({ source, link })));
  for (const { source, link } of remoteLinks) {
    const row = element('div', 'inspector-link-row inspector-remote-row'); row.dataset.remoteLink = link.id;
    const incoming = source.id !== node.id, target = index.nodes.get(link.targetId);
    const snapshot = Object.hasOwn(options.project.snapshots, link.targetId) ? options.project.snapshots[link.targetId] : undefined;
    const destination = target ?? snapshot?.node;
    const destinationTitle = incoming ? workLibraryDisplayText(options.project.id, source.id, nodeTag(source)) : destination ? workLibraryDisplayText(options.project.id, destination.id, nodeTag(destination)) : 'Remote node';
    const destinationId = incoming ? source.id : link.targetId;
    row.append(element('strong', 'inspector-link-title', link.label || destinationTitle),
      element('span', 'inspector-link-meta', `${link.relation} · ${incoming ? 'Incoming · ' : 'Outgoing · '}${target ? 'Available in this document' : snapshot ? 'Saved reference snapshot' : 'External GUID'}`),
      element('code', 'inspector-link-guid', destinationId));
    if (target || incoming) row.append(button('↗', `Jump to Node: ${destinationTitle}`, () => action(() => options.onJumpToNode(destinationId)), 'inspector-remote-jump'));
    if (options.onDeleteRemoteLink) row.append(button('−', `Remove remote link ${incoming ? 'from' : 'to'} ${destinationTitle === 'Remote node' ? link.targetId : destinationTitle}`, () => action(() => options.onDeleteRemoteLink!(source.id, link.id)), 'inspector-link-remove'));
    remoteList.append(row);
  }
  if (!remoteLinks.length) remoteList.append(element('p', 'inspector-empty', 'No remote links.'));
  host.append(remoteList);
  if (!options.onCreateRemoteLink) return;
  const form = element('form', 'inspector-remote-form');
  const target = element('input'); target.type = 'text'; target.placeholder = 'Target node GUID'; target.maxLength = 96; target.pattern = '[A-Za-z0-9][A-Za-z0-9_-]{0,95}'; target.required = true; target.autocomplete = 'off'; target.spellcheck = false;
  const relation = select(relations, 'generic'), label = element('input'); label.type = 'text'; label.maxLength = 160; label.placeholder = 'Optional label';
  const submit = element('button', 'secondary full', '+ Remote link'); submit.type = 'submit'; submit.setAttribute('aria-label', 'Add remote link');
  form.append(field('Target GUID', target), field('Relationship', relation), field('Label', label), submit,
    element('p', 'inspector-remote-hint', 'Remote links are conceptual connections. They draw no line on the work.'));
  form.onsubmit = event => {
    event.preventDefault(); if (!form.reportValidity()) return;
    void action(async () => { submit.disabled = true; try { await options.onCreateRemoteLink!(node.id, { targetId: target.value.trim(), relation: relation.value as LinkRelation, ...(label.value.trim() ? { label: label.value.trim() } : {}) }); } finally { submit.disabled = false; } });
  };
  host.append(form);
}
