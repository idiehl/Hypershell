import type { MeridianProject } from './core/types.ts';
import { navigationIndex, type SearchResult } from './core/navigation.ts';
import { workLibraryDisplayText } from './core/work-terminology.ts';

export type SearchScope = 'all' | 'canvas';

/** Discovery is contextual to the inspector, never a second permanent canvas list. */
export function inspectorSearch(project: MeridianProject, canvasId: string, state: {
  query: string; scope: SearchScope;
  search: (query: string, limit?: number) => SearchResult[];
  change: (query: string, scope: SearchScope) => void;
  reveal: (nodeId: string) => void;
}): HTMLElement {
  const section = document.createElement('section');
  section.className = 'inspector-search';
  section.setAttribute('aria-label', 'Search nodes and attachments');
  const heading = document.createElement('div');
  heading.className = 'search-heading';
  const label = document.createElement('label');
  label.htmlFor = 'node-search'; label.textContent = 'SEARCH';
  const scope = document.createElement('select');
  scope.setAttribute('aria-label', 'Search scope');
  for (const [value, text] of [['all', 'All Works'], ['canvas', 'This Work']]) {
    const option = document.createElement('option'); option.value = value!; option.textContent = text!; scope.append(option);
  }
  scope.value = state.scope; heading.append(label, scope);
  const input = document.createElement('input');
  input.type = 'search'; input.id = 'node-search'; input.className = 'outline-search';
  input.setAttribute('aria-label', 'Find a node');
  input.placeholder = 'Text, media, cell name or node ID…'; input.value = state.query;
  const results = document.createElement('div');
  results.className = 'search-results'; results.setAttribute('aria-label', 'Search results');
  const count = document.createElement('p'); count.className = 'search-count'; count.setAttribute('role', 'status');
  const navigation = navigationIndex(project);
  const draw = () => {
    const query = input.value.trim();
    state.change(input.value, scope.value as SearchScope); results.replaceChildren();
    if (!query) { count.textContent = 'Search includes contents hidden by collapse.'; return; }
    // Filter before truncating so matches in another canvas cannot crowd out this canvas.
    const matches = state.search(query, project.nodes.length).filter(result => scope.value === 'all' || result.canvasId === canvasId);
    count.textContent = matches.length ? `${matches.length} ${matches.length === 1 ? 'result' : 'results'}${matches.length > 100 ? ' · showing first 100' : ''}` : 'No matching nodes or attachments.';
    for (const result of matches.slice(0, 100)) {
      const button = document.createElement('button'); button.type = 'button';
      const displayTitle = workLibraryDisplayText(project.id,result.nodeId,result.title);
      const displayWork = workLibraryDisplayText(project.id,project.canvases.find(canvas => canvas.id === result.canvasId)?.rootId ?? '',result.canvasTitle);
      button.className = 'outline-node search-result'; button.dataset.navigationNode = result.nodeId;
      button.setAttribute('aria-label', `Select ${displayTitle}`); button.title = `Reveal ${displayTitle} on Work`;
      const title = document.createElement('strong'); title.textContent = displayTitle;
      const context = document.createElement('small');
      context.textContent = `${displayWork} · ${result.source}${navigation.isNodeVisible(result.nodeId) ? '' : ' · hidden'}`;
      const snippet = document.createElement('span'); snippet.textContent = workLibraryDisplayText(project.id,result.nodeId,result.snippet);
      button.append(title, context, snippet); button.onclick = () => state.reveal(result.nodeId); results.append(button);
    }
  };
  input.oninput = draw; scope.onchange = draw;
  input.onkeydown = event => {
    if (event.key === 'ArrowDown' || event.key === 'Enter') {
      const first = results.querySelector<HTMLButtonElement>('button');
      if (first) { event.preventDefault(); event.key === 'Enter' ? first.click() : first.focus(); }
    } else if (event.key === 'Escape' && input.value) { event.preventDefault(); event.stopPropagation(); input.value = ''; draw(); }
  };
  results.onkeydown = event => {
    if (!(event.target instanceof HTMLButtonElement)) return;
    const buttons = [...results.querySelectorAll<HTMLButtonElement>('button')], at = buttons.indexOf(event.target);
    const next = event.key === 'ArrowDown' ? Math.min(at + 1, buttons.length - 1) : event.key === 'ArrowUp' ? at - 1 : event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : undefined;
    if (next !== undefined) { event.preventDefault(); next < 0 ? input.focus() : buttons[next]?.focus(); }
  };
  section.append(heading, input, count, results); draw(); return section;
}
