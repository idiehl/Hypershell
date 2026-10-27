import { workspaceMenu } from './workspace-tools.ts';

export type FileCommand = 'save' | 'save-as' | 'checkpoint' | 'duplicate' | 'import' | 'history' | 'preview' | 'publish' | 'export';
export type ToolCommand = 'modules' | 'composition';
export type HistoryDirection = 'back' | 'forward';
export const fileCommands: readonly { command: FileCommand; label: string }[] = [
  { command: 'save', label: 'Save' }, { command: 'save-as', label: 'Save as' },
  { command: 'checkpoint', label: 'Create checkpoint' }, { command: 'duplicate', label: 'Duplicate' },
  { command: 'import', label: 'Import' }, { command: 'history', label: 'History' },
  { command: 'preview', label: 'Preview' }, { command: 'publish', label: 'Publish' },
  { command: 'export', label: 'Export Work' },
];
export const toolCommands: readonly { command: ToolCommand; label: string }[] = [
  { command: 'modules', label: 'Modules' }, { command: 'composition', label: 'Composition tools' },
];

interface WindowHistory { entries: string[]; cursor: number }
let historyRecord: WindowHistory = { entries: [], cursor: -1 };
const historyKey = () => `meridian.window-history.${window.name}`;
const historyState = () => history.state && typeof history.state === 'object' ? history.state : {};

/** Keep the browser fallback's arrows in sync with this window's canvas history. */
export function rememberCanvasHistory(): void {
  try {
    const saved = JSON.parse(sessionStorage.getItem(historyKey()) ?? 'null') as WindowHistory | null;
    if (saved && Array.isArray(saved.entries) && saved.entries.every(id => typeof id === 'string') && Number.isInteger(saved.cursor)) historyRecord = saved;
  } catch { /* History remains usable if browser storage is unavailable. */ }
  const state = historyState(), id = typeof state.meridianHistoryId === 'string' ? state.meridianHistoryId : crypto.randomUUID();
  const at = historyRecord.entries.indexOf(id);
  if (at >= 0) historyRecord.cursor = at;
  else {
    historyRecord.entries = historyRecord.entries.slice(0, historyRecord.cursor + 1);
    historyRecord.entries.push(id); historyRecord.cursor = historyRecord.entries.length - 1;
  }
  history.replaceState({ ...state, meridianHistoryId: id }, '', location.href);
  try { sessionStorage.setItem(historyKey(), JSON.stringify(historyRecord)); } catch {}
}

export function recordCanvasAddress(address: string, replace: boolean): void {
  if (replace) history.replaceState(historyState(), '', address);
  else if (new URL(address, location.href).href !== location.href) history.pushState({ meridianHistoryId: crypto.randomUUID() }, '', address);
  rememberCanvasHistory();
}

export function hasNativeChrome(): boolean {
  return (window as Window & { __meridianNativeChrome?: boolean }).__meridianNativeChrome === true;
}

const arrow = (direction: HistoryDirection) => `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${direction === 'back' ? 'M19 12H5m6-6-6 6 6 6' : 'M5 12h14m-6-6 6 6-6 6'}" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>`;

/** Browsers get the same controls; MyNet supplies them in its native title bar. */
export function applicationMenu(onCommand: (command: FileCommand) => unknown, onNavigate: (direction: HistoryDirection) => unknown, prepareMenu: () => void, onTool: (command: ToolCommand) => unknown): HTMLElement {
  const bar = document.createElement('nav'); bar.className = 'application-menubar'; bar.setAttribute('aria-label', 'Application menu');
  for (const direction of ['back', 'forward'] as const) {
    const button = document.createElement('button'); button.type = 'button'; button.className = 'application-navigation';
    button.title = direction === 'back' ? 'Back' : 'Forward'; button.setAttribute('aria-label', button.title); button.innerHTML = arrow(direction);
    button.disabled = direction === 'back' ? historyRecord.cursor <= 0 : historyRecord.cursor >= historyRecord.entries.length - 1;
    button.onclick = () => onNavigate(direction); bar.append(button);
  }
  const file = document.createElement('button'); file.type = 'button'; file.textContent = 'File'; file.className = 'application-file';
  file.setAttribute('aria-haspopup', 'menu'); file.setAttribute('aria-label', 'File');
  file.onclick = () => { const bounds = file.getBoundingClientRect(); prepareMenu(); workspaceMenu(fileCommands.map(item => ({ label: item.label, run: () => onCommand(item.command) })), { x: bounds.left, y: bounds.bottom }, 'File'); };
  const tools = document.createElement('button'); tools.type = 'button'; tools.textContent = 'Tools'; tools.className = 'application-tools';
  tools.setAttribute('aria-haspopup', 'menu'); tools.setAttribute('aria-label', 'Tools');
  tools.onclick = () => { const bounds = tools.getBoundingClientRect(); prepareMenu(); workspaceMenu(toolCommands.map(item => ({ label: item.label, run: () => onTool(item.command) })), { x: bounds.left, y: bounds.bottom }, 'Tools'); };
  bar.append(file, tools); return bar;
}

export function bindApplicationChrome(options: { command: (command: FileCommand) => unknown; tool: (command: ToolCommand) => unknown; navigate: (direction: HistoryDirection) => unknown; changed: () => void; historyChanged: () => void }): void {
  window.addEventListener('meridian:file-command', event => {
    const command = (event as CustomEvent).detail?.command;
    if (fileCommands.some(item => item.command === command)) options.command(command);
  });
  window.addEventListener('meridian:navigate', event => {
    const direction = (event as CustomEvent).detail?.direction;
    if (direction === 'back' || direction === 'forward') options.navigate(direction);
  });
  window.addEventListener('meridian:tool-command', event => {
    const command = (event as CustomEvent).detail?.command;
    if (toolCommands.some(item => item.command === command)) options.tool(command);
  });
  window.addEventListener('meridian:native-chrome', options.changed);
  window.addEventListener('popstate', () => { rememberCanvasHistory(); options.historyChanged(); });
  document.addEventListener('keydown', event => {
    if (!hasNativeChrome() && event.altKey && ['f', 't'].includes(event.key.toLowerCase()) && !document.querySelector('dialog[open]')) {
      event.preventDefault(); document.querySelector<HTMLButtonElement>(event.key.toLowerCase() === 'f' ? '.application-file' : '.application-tools')?.click();
    }
  });
}
