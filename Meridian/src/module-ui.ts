import type { MeridianProject, StoredModule, NodeKind } from './core/types.ts';
import { createModule, validateModule } from './core/modules.ts';
import { summarizeDependencies } from './core/dependencies.ts';
import { renderScene } from './scene.ts';
import { nodeTag } from './core/node-tag.ts';
import { nodeKindLabel, workLibraryDisplayText } from './core/work-terminology.ts';
import { moduleStorage, type ModuleSummary } from './storage.ts';

function el<K extends keyof HTMLElementTagNameMap>(tag: K, cls = '', text?: string) {
  const node = document.createElement(tag); node.className = cls;
  if (text !== undefined) node.textContent = text;
  return node;
}
function action(title: string, run: () => void | Promise<void>, cls = 'secondary') {
  const button = el('button', cls, title); button.type = 'button';
  button.onclick = () => { void run(); }; return button;
}
function dialog(title: string, cls = '') {
  const root = el('dialog', `modal module-dialog ${cls}`); root.setAttribute('aria-label', title);
  const header = el('div', 'modal-header'); const close = action('×', () => root.close(), 'icon-button');
  close.setAttribute('aria-label', 'Close dialog'); header.append(el('h2', '', title), close);
  const body = el('div', 'modal-body'), footer = el('div', 'modal-footer');
  const error = el('p', 'module-error'); error.setAttribute('role', 'alert');
  root.append(header, body, error, footer); document.body.append(root);
  root.addEventListener('close', () => root.remove()); root.showModal();
  return { root, body, footer, error, close: () => root.close() };
}
function message(error: unknown) { return error instanceof Error ? error.message : String(error); }
function label(title: string, input: HTMLElement) { const field = el('label', 'field'); field.append(el('span', 'field-label', title), input); return field; }
function contentCounts(project: MeridianProject) {
  const counts = new Map<NodeKind, number>();
  for (const node of project.nodes) if (node.kind !== 'canvas') counts.set(node.kind, (counts.get(node.kind) ?? 0) + 1);
  return [...counts].map(([kind, count]) => ({ kind, count }));
}
function modulePreview(host: HTMLElement, project: MeridianProject) {
  const preview = structuredClone(project);
  for (const node of preview.nodes) if (node.kind === 'input' || node.kind === 'switch') node.exposed = false;
  const draw = () => renderScene(host, preview, preview.entryCanvasId, {
    readonly: true, running: false, fit: true,
    onToggle: (id, field, value) => { if (field === 'collapsed') { preview.nodes.find(node => node.id === id)!.collapsed = value; draw(); } },
  });
  draw();
}

/** Shared, bounded preflight. A saved value never stands in for a complete canvas. */
export function renderDependencySummary(host: HTMLElement, project: MeridianProject, options: { rootIds?: string[]; includeExternalReferences?: boolean } = {}) {
  const report = summarizeDependencies(project, options);
  host.replaceChildren(); host.classList.add('dependency-summary');
  const unresolved = report.entries.filter(entry => entry.status !== 'local');
  host.append(el('h3', '', 'Dependencies'), el('p', 'muted', unresolved.length
    ? `${unresolved.length} requirement${unresolved.length === 1 ? '' : 's'} need review. Saved snapshots preserve an observed value, not the full operating context.`
    : report.entries.length ? 'All identified node requirements are present in this work.' : 'No declared or referenced outside requirements.'));
  if (!report.entries.length) return;
  const details = el('details', 'dependency-details'); details.open = unresolved.length > 0;
  details.append(el('summary', '', `Inspect ${report.entries.length} requirement${report.entries.length === 1 ? '' : 's'}`));
  const list = el('div', 'dependency-rows');
  const statusNames = { local: 'Included', snapshot: 'Snapshot only', missing: 'Not included', excluded: 'Excluded' };
  for (const entry of [...unresolved, ...report.entries.filter(entry => entry.status === 'local')].slice(0, 150)) {
    const row = el('div', `dependency-row status-${entry.status}`);
    const heading = el('div', 'dependency-row-heading'); heading.append(el('strong', '', workLibraryDisplayText(project.id,entry.id,entry.title || entry.id)), el('span', 'dependency-status', statusNames[entry.status]));
    row.append(heading, el('code', 'guid', entry.id), el('small', 'muted', entry.reasons.join(' · ')));
    if (entry.note) row.append(el('p', 'muted', entry.note));
    const sources = entry.sourceIds.map(id => workLibraryDisplayText(project.id,id,project.nodes.find(node => node.id === id)?.title ?? id));
    if (sources.length) row.append(el('p', 'dependency-source', `Required by: ${sources.join(', ')}`));
    list.append(row);
  }
  if (report.entries.length > 150) list.append(el('p', 'muted', `Showing 150 of ${report.entries.length} requirements. The package preserves the complete declarations.`));
  details.append(list); host.append(details);
}

function showContents(host: HTMLElement, project: MeridianProject) {
  const nodes = project.nodes.filter(node => node.kind !== 'canvas');
  const summary = el('div', 'module-contents');
  summary.append(el('h3', '', 'This package includes:'), el('p', 'module-stats', `${nodes.length} nodes · ${project.links.length} connections · ${project.assets.length} attachments`));
  const chips = el('div', 'module-chips');
  for (const item of contentCounts(project)) chips.append(el('span', '', `${item.count} ${nodeKindLabel(item.kind).replace('-', ' ')}`));
  summary.append(chips);
  const contents = el('details', 'module-node-contents'); contents.append(el('summary', '', 'Browse node contents'));
  let populated = false;
  contents.addEventListener('toggle', () => {
    if (!contents.open || populated) return; populated = true;
    const list = el('div', 'module-node-list');
    for (const node of nodes) {
      const row = el('div'); row.append(el('strong', '', workLibraryDisplayText(project.id,node.id,nodeTag(node))), el('span', '', nodeKindLabel(node.kind).replace('-', ' '))); list.append(row);
    }
    contents.append(list);
  });
  summary.append(contents);
  const interfaces = nodes.filter(node => node.kind === 'input' || node.kind === 'output' || node.kind === 'switch');
  if (interfaces.length) {
    const details = el('details', 'module-interfaces'); details.append(el('summary', '', 'Inputs, outputs & controls'));
    for (const node of interfaces) details.append(el('p', '', `${node.kind === 'input' ? 'IN' : node.kind === 'output' ? 'OUT' : 'SWITCH'} · ${nodeTag(node)}`));
    summary.append(details);
  }
  host.append(summary);
}

export function saveModuleDialog(project: MeridianProject, nodeId: string, saved: (module: StoredModule) => void) {
  const original = project.nodes.find(node => node.id === nodeId);
  if (!original || original.kind === 'canvas') return;
  const m = dialog('Save reusable module', 'module-save');
  m.body.append(el('p', 'lead-small', 'Keep an assembly you can use again.'), el('p', 'muted', 'This node and all of its children are included. Internal connections, media, layout and named controls travel together. Each insertion gets fresh identities.'));
  const name = el('input'); name.value = nodeTag(original).slice(0, 160); name.maxLength = 160; name.required = true;
  m.body.append(label('Name', name));
  let candidate: StoredModule;
  try { candidate = createModule(project, nodeId, name.value); }
  catch (error) { m.error.textContent = message(error); m.footer.append(action('Close', m.close)); return; }
  showContents(m.body, candidate.project);
  const preview = el('div', 'module-preview'); preview.setAttribute('aria-label', 'Module preview'); m.body.append(preview);
  modulePreview(preview, candidate.project);
  const dependencies = el('div'); renderDependencySummary(dependencies, candidate.project, { includeExternalReferences: false }); m.body.append(dependencies);
  m.body.append(el('p', 'module-policy', 'Outside connections and other works are not copied. Their requirements remain listed for review. Saving preserves configured controls; it does not capture a running simulation.'));
  const save = action('Save module', async () => {
    if (!name.reportValidity() || !name.value.trim()) return;
    save.disabled = true; m.error.textContent = '';
    try {
      candidate.name = name.value.trim(); candidate.project.title = candidate.name;
      candidate.project.nodes.find(node => node.id === candidate.rootId)!.title = candidate.name;
      const stored = await moduleStorage.save(candidate); m.close(); saved(stored);
    } catch (error) { m.error.textContent = message(error); save.disabled = false; }
  }, 'primary');
  m.footer.append(action('Cancel', m.close), save); name.focus(); name.select();
}

export async function moduleLibraryDialog(options: {
  destination: string;
  insert: (module: StoredModule) => void | Promise<void>;
  openDefinition: (module: StoredModule) => boolean | void | Promise<boolean | void>;
}) {
  const m = dialog('Module library', 'module-library');
  m.body.append(el('p', 'lead-small', 'Build once. Make it your own, anywhere.'), el('p', 'muted', `Insert an independent assembly into ${options.destination}. Your saved definitions stay unchanged.`));
  const search = el('input', 'module-search'); search.type = 'search'; search.placeholder = 'Find a name or paste a module ID'; search.setAttribute('aria-label', 'Search modules'); m.body.append(search);
  const workspace = el('div', 'module-browser'), list = el('div', 'module-list'), detail = el('div', 'module-detail');
  list.setAttribute('aria-label', 'Stored modules'); workspace.append(list, detail); m.body.append(workspace);
  let catalog: ModuleSummary[] = [], selected: StoredModule | null = null, generation = 0, busy = false;
  const insert = action('Insert module', async () => {
    if (!selected || busy) return; busy = true; syncButtons(); m.error.textContent = '';
    try { await options.insert(selected); m.close(); }
    catch (error) { m.error.textContent = message(error); }
    finally { busy = false; syncButtons(); }
  }, 'primary');
  const open = action('Open definition copy', async () => {
    if (!selected || busy) return; busy = true; syncButtons(); m.error.textContent = '';
    // Leave the modal open if the user cancels the draft save/recovery prompt.
    try { if (await options.openDefinition(selected) !== false) m.close(); }
    catch (error) { m.error.textContent = message(error); }
    finally { busy = false; syncButtons(); }
  });
  // Keep the initiating canvas protected until a pending create/insert has settled.
  m.root.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
  function syncButtons() {
    m.root.querySelectorAll<HTMLButtonElement>('.modal-header button,.modal-footer button,.module-library-item').forEach(button => { button.disabled = busy; });
    insert.disabled = open.disabled = !selected || busy; search.disabled = busy;
  }
  m.footer.append(action('Close', m.close), open, insert); syncButtons();
  async function choose(summary: ModuleSummary) {
    if (busy) return;
    const ticket = ++generation; selected = null; syncButtons(); m.error.textContent = ''; detail.replaceChildren(el('p', 'muted', 'Loading definition…'));
    list.querySelectorAll('button').forEach(button => button.classList.toggle('selected', button.dataset.moduleId === summary.id));
    try {
      const module = await moduleStorage.load(summary.id);
      if (ticket !== generation || !m.root.open) return;
      const errors = validateModule(module); if (errors.length) throw new Error(errors.join(' '));
      selected = module; detail.replaceChildren(el('span', 'module-eyebrow', 'SAVED DEFINITION'), el('h3', 'module-name', module.name), el('code', 'guid module-id', module.id));
      showContents(detail, module.project);
      const preview = el('div', 'module-preview'); preview.setAttribute('aria-label', 'Module preview'); detail.append(preview);
      modulePreview(preview, module.project);
      const dependencies = el('div'); renderDependencySummary(dependencies, module.project, { includeExternalReferences: false }); detail.append(dependencies);
      detail.append(el('p', 'module-policy', 'Insert adds a fresh copy to this work. Open definition copy starts a separate editable work; neither action changes the saved module.'));
    } catch (error) { if (ticket === generation) { detail.replaceChildren(); m.error.textContent = message(error); } }
    syncButtons();
  }
  function draw() {
    list.replaceChildren(); const query = search.value.trim().toLocaleLowerCase();
    const matches = catalog.filter(item => `${item.name} ${item.id} ${item.contents.map(entry => entry.kind).join(' ')}`.toLocaleLowerCase().includes(query));
    for (const item of matches) {
      const row = action('', () => choose(item), `module-library-item${selected?.id === item.id ? ' selected' : ''}`);
      row.dataset.moduleId = item.id; row.setAttribute('aria-label', `Select module ${item.name}`);
      row.append(el('span', 'module-item-mark', '▧'), el('strong', '', item.name), el('small', '', `${item.nodeCount} nodes · ${item.linkCount} connections`)); list.append(row);
    }
    if (!matches.length) list.append(el('p', 'module-empty', catalog.length ? 'No modules match that name or ID.' : 'Your module library is ready for its first assembly. Select a node on the work, then choose Save as module.'));
  }
  search.oninput = draw;
  try {
    catalog = await moduleStorage.list(); if (!m.root.open) return; draw();
    if (catalog[0]) await choose(catalog[0]); else detail.append(el('div', 'module-empty-preview', '▧'), el('h3', '', 'Reusable by design'), el('p', 'muted', 'A module holds ordinary nodes, connections and media. Add a Module node to your work and build inside it, or save an existing branch.'));
  } catch (error) { m.error.textContent = message(error); }
  search.focus();
}

export function nodeDependenciesDialog(project: MeridianProject, nodeId: string, apply: (ids: string[]) => void) {
  const node = project.nodes.find(node => node.id === nodeId); if (!node) return;
  const m = dialog('Node dependencies', 'node-dependencies'); let ids = [...(node.dependencyIds ?? [])];
  m.body.append(el('p', 'lead-small', workLibraryDisplayText(project.id,node.id,nodeTag(node))), el('p', 'muted', 'Declare the nodes or context this node needs. A declaration records a requirement; it does not copy content or create a connection.'));
  const declared = el('div', 'declared-dependencies'); m.body.append(declared);
  const picker = el('select'); picker.setAttribute('aria-label', 'Available node dependency');
  const empty = el('option', '', 'Choose a node from this Work…'); empty.value = ''; picker.append(empty);
  for (const target of project.nodes.filter(item => item.id !== nodeId)) {
    const option = el('option', '', `${workLibraryDisplayText(project.id,project.canvases.find(canvas => canvas.id === target.canvasId)?.rootId ?? '',project.canvases.find(canvas => canvas.id === target.canvasId)?.title ?? 'Work')} / ${workLibraryDisplayText(project.id,target.id,target.title)}`); option.value = target.id; picker.append(option);
  }
  const guid = el('input'); guid.placeholder = 'Or paste an outside node GUID'; guid.setAttribute('aria-label', 'Dependency GUID'); guid.maxLength = 96;
  const add = action('Add dependency', () => {
    const id = guid.value.trim() || picker.value; m.error.textContent = '';
    if (!id || !/^[A-Za-z0-9][A-Za-z0-9_-]{0,95}$/.test(id)) { m.error.textContent = 'Choose a node or enter a valid node GUID.'; return; }
    if (id === nodeId) { m.error.textContent = 'A node cannot declare itself as a requirement.'; return; }
    if (ids.includes(id)) { m.error.textContent = 'That requirement is already declared.'; return; }
    if (ids.length >= 1000) { m.error.textContent = 'A node can declare at most 1,000 requirements.'; return; }
    ids.push(id); guid.value = ''; picker.value = ''; draw();
  });
  m.body.append(label('Add a requirement', picker), guid, add);
  const report = el('div'); m.body.append(report);
  function draw() {
    declared.replaceChildren();
    if (!ids.length) declared.append(el('p', 'muted', 'No explicit requirements yet. References and logic inputs are also tracked below.'));
    for (const id of ids) {
      const dependency = project.nodes.find(node => node.id === id) ?? project.snapshots[id]?.node;
      const row = el('div', 'declared-dependency'); const text = el('div'); text.append(el('strong', '', dependency ? workLibraryDisplayText(project.id,dependency.id,nodeTag(dependency)) : 'Outside node'), el('code', 'guid', id));
      const remove = action('Remove', () => { ids = ids.filter(value => value !== id); draw(); }, 'quiet'); remove.setAttribute('aria-label', `Remove dependency ${id}`); row.append(text, remove); declared.append(row);
    }
    const draft = structuredClone(project); draft.nodes.find(node => node.id === nodeId)!.dependencyIds = ids;
    renderDependencySummary(report, draft, { rootIds: [nodeId], includeExternalReferences: true });
  }
  draw(); m.footer.append(action('Cancel', m.close), action('Apply dependencies', () => { try { apply(ids); m.close(); } catch (error) { m.error.textContent = message(error); } }, 'primary'));
}
