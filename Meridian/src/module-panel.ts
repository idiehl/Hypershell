import type { MeridianProject, StoredModule } from './core/types.ts';
import { navigationIndex } from './core/navigation.ts';
import { inspectorNodeType } from './inspector-panels.ts';
import { nodeTag } from './core/node-tag.ts';
import { workLibraryDisplayText } from './core/work-terminology.ts';
import { moduleStorage, type ModuleSummary } from './storage.ts';

export interface ModulePanelState {
  name: string;
  catalog: ModuleSummary[] | null;
  loading: boolean;
  error: string;
  expanded: Set<string>;
  saving: boolean;
  inserting: Set<string>;
}
export const createModulePanelState = (): ModulePanelState => ({ name: '', catalog: null, loading: false, error: '', expanded: new Set(), saving: false, inserting: new Set() });
interface ModulePanelOptions {
  project: MeridianProject; canvasId: string; selectedIds: readonly string[]; selectedLinkIds: readonly string[];
  active: boolean; state: ModulePanelState;
  onActivate: () => void; onToggle: (id: string) => void; onClear: () => void;
  onCreate: (name: string) => Promise<void>; onInsert: (module: StoredModule) => Promise<void>;
  onRefresh: () => void; onError: (error: unknown) => void;
}
function el<K extends keyof HTMLElementTagNameMap>(tag: K, cls = '', text?: string): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag); if (cls) node.className = cls; if (text !== undefined) node.textContent = text; return node;
}
function action(text: string, run: () => unknown, label = text, cls = 'secondary'): HTMLButtonElement {
  const node = el('button', cls, text); node.type = 'button'; node.title = label; node.setAttribute('aria-label', label);
  node.onclick = () => { void run(); }; return node;
}
async function loadCatalog(options: ModulePanelOptions): Promise<void> {
  const { state } = options; if (state.catalog !== null || state.loading) return;
  state.loading = true; state.error = '';
  try { state.catalog = await moduleStorage.list(); }
  catch (error) { state.catalog = []; state.error = error instanceof Error ? error.message : String(error); }
  finally { state.loading = false; options.onRefresh(); }
}

/** A persistent inspector pane: template authoring is view state, saving uses the existing inert module store. */
export function renderModulePanel(host: HTMLElement, options: ModulePanelOptions): void {
  const { project, state } = options, index = navigationIndex(project), selected = new Set(options.selectedIds);
  host.append(el('h2', 'inspector-title', 'Module'), el('p', 'muted', 'Save an assembly, then add independent copies to any work.'));
  if (!options.active) host.append(action('Select nodes', options.onActivate, 'Activate Create Module', 'secondary full'));
  const selection = el('section', 'module-selection-section');
  selection.append(el('h3', 'inspector-section-title', 'Selection'), el('p', 'module-selection-count', `${selected.size} nodes · ${options.selectedLinkIds.length} links`));
  if (selected.size) {
    // Show the selected paths and their descendants, including unchecked exclusions.
    // Intermediate ancestors retain their tree position without being silently selected.
    const shown = new Set(options.selectedIds);
    for (const id of options.selectedIds) {
      for (const ancestor of index.ancestors(id)) if (ancestor.kind !== 'canvas') shown.add(ancestor.id);
      for (const child of index.descendants(id)) shown.add(child.id);
    }
    const tree = el('ul', 'module-selection-tree'); tree.setAttribute('aria-label', 'Module selection tree');
    const nodes = project.nodes.filter(node => node.canvasId === options.canvasId && shown.has(node.id));
    const children = new Map<string, typeof nodes>();
    for (const node of nodes) { const parent = node.parentId && shown.has(node.parentId) ? node.parentId : ''; const list = children.get(parent) ?? []; list.push(node); children.set(parent, list); }
    const draw = (parent: string, list: HTMLElement) => {
      for (const node of children.get(parent) ?? []) {
        const item = el('li'); item.dataset.moduleSelectionNode = node.id;
        const row = el('label', `module-selection-row${selected.has(node.id) ? ' selected' : ''}`);
        const check = el('input'); check.type = 'checkbox'; check.checked = selected.has(node.id);
        check.setAttribute('aria-label', `Include ${workLibraryDisplayText(project.id,node.id,nodeTag(node))} in module`);
        check.onchange = () => options.onToggle(node.id);
        row.append(check, el('span', 'module-selection-title', workLibraryDisplayText(project.id,node.id,nodeTag(node))), el('small', 'module-selection-type', inspectorNodeType(node))); item.append(row);
        if (children.has(node.id)) { const nested = el('ul'); draw(node.id, nested); item.append(nested); }
        list.append(item);
      }
    };
    draw('', tree); selection.append(tree, action('Clear selection', options.onClear, 'Clear module selection', 'quiet full'));
  } else selection.append(el('p', 'muted', 'Click nodes to include them. Click again to exclude. Children follow their parent unless you explicitly change their selection.'));
  const name = el('input'); name.value = state.name; name.maxLength = 160; name.placeholder = 'Name this module'; name.required = true;
  name.disabled=state.saving;
  name.setAttribute('aria-label', 'Module name'); name.oninput = () => { state.name = name.value; };
  const field = el('label', 'field'); field.append(el('span', 'field-label', 'Name'), name); selection.append(field);
  const create = action('Create From Selection', async () => {
    if(state.saving)return;
    if (!name.reportValidity() || !name.value.trim()) return;
    state.saving=true;create.disabled = true;name.disabled=true;
    try { await options.onCreate(name.value.trim()); }
    catch (error) { options.onError(error); }
    finally { state.saving=false;options.onRefresh(); }
  }, 'Create From Selection', 'primary full'); create.disabled = state.saving||!selected.size;
  selection.append(create); host.append(selection);

  const library = el('section', 'module-library-section'), heading = el('div', 'module-library-heading');
  const reload = action('↻', () => { state.catalog = null; state.error = ''; void loadCatalog(options); }, 'Refresh Module Library', 'quiet');
  heading.append(el('h3', 'inspector-section-title', 'Module Library'), reload); library.append(heading);
  if (state.error) { const error = el('p', 'inspector-error', state.error); error.setAttribute('role', 'alert'); library.append(error); }
  if (state.catalog === null) library.append(el('p', 'muted', 'Loading modules…'));
  else if (!state.catalog.length) library.append(el('p', 'muted', 'Saved modules appear here. Each copy gets its own GUIDs.'));
  for (const stored of state.catalog ?? []) {
    const entry = el('article', 'module-library-card'); entry.dataset.moduleId = stored.id;
    const details = el('details'); details.open = state.expanded.has(stored.id);
    const title = el('summary'); title.append(el('strong', '', stored.name)); details.append(title);
    details.addEventListener('toggle', () => { if (details.open) state.expanded.add(stored.id); else state.expanded.delete(stored.id); });
    details.append(el('p', 'muted', `${stored.nodeCount} nodes · ${stored.linkCount} links`), el('code', 'guid', stored.id));
    const add = action('Add to Work', async () => {
      if(state.inserting.has(stored.id))return;
      state.inserting.add(stored.id);add.disabled = true;
      try { const definition = await moduleStorage.load(stored.id); await options.onInsert(definition); }
      catch (error) { options.onError(error); }
      finally { state.inserting.delete(stored.id);options.onRefresh(); }
    }, `Add ${stored.name} to Work`, 'secondary full');
    add.disabled=state.inserting.has(stored.id);
    entry.append(details, add); library.append(entry);
  }
  host.append(library); void loadCatalog(options);
}
