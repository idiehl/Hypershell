import type { MeridianAsset } from './core/types.ts';
import { documentDelimiter, documentMarkdownBlocks, documentPreviewKind, hasDocumentPdfSignature, prepareDocumentPreview, safeDocumentLink, type DocumentTable, type MarkdownBlock } from './core/document-preview.ts';

export interface DocumentViewerState { page: number; columnPage: number; query: string; firstRowHeader: boolean; scrollLeft: number; scrollTop: number }
export interface DocumentViewerOptions { onClose?: () => void; state?: Partial<DocumentViewerState> }
export interface DocumentViewer { element: HTMLElement; captureState: () => DocumentViewerState; dispose: () => void }
const make = <K extends keyof HTMLElementTagNameMap>(tag: K, className = '', text?: string): HTMLElementTagNameMap[K] => {
  const element = document.createElement(tag); element.className = className; if (text !== undefined) element.textContent = text; return element;
};
const button = (text: string, label: string, action: () => void) => { const control = make('button', '', text); control.type = 'button'; control.setAttribute('aria-label', label); control.title = label; control.onclick = action; return control; };

/** Shared icon controls retain full accessible names and native tooltips. */
export function documentActionButton(action: 'read' | 'reading-view', label: string, invoke: () => void): HTMLButtonElement {
  const control = button('', label, invoke); control.className = 'ms-document-icon-button'; control.dataset.documentAction = action;
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  for (const [name, value] of Object.entries({ viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', 'stroke-width': '1.7', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true', focusable: 'false' })) svg.setAttribute(name, value);
  const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  path.setAttribute('d', action === 'read'
    ? 'M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8l-5-5Zm0 0v5h5M8 12h8M8 15h8M8 18h5'
    : 'M14 3h7v7M21 3l-10 10M10 5H5a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-5');
  svg.append(path); control.append(svg); return control;
}

/** A deliberately small, safe inline Markdown renderer. It creates only inert
 * DOM, allows user-activated http(s)/mailto links, and never creates image tags. */
function inlineMarkdown(host: HTMLElement, text: string, depth = 0): void {
  if (depth > 4) { host.append(document.createTextNode(text)); return; }
  let at = 0, plain = 0;
  const flush = (end: number) => { if (end > plain) host.append(document.createTextNode(text.slice(plain, end))); };
  while (at < text.length) {
    if (text.startsWith('https://', at) || text.startsWith('http://', at)) {
      let end = at; while (end < text.length && !/\s/.test(text[end]!) && text[end] !== '>') end++;
      const target = text.slice(at, end), href = safeDocumentLink(target);
      if (href) { const angled = at > plain && text[at - 1] === '<' && text[end] === '>'; flush(angled ? at - 1 : at); const anchor = make('a', '', target); anchor.href = href; anchor.target = '_blank'; anchor.rel = 'noopener noreferrer'; host.append(anchor); at = end + (angled ? 1 : 0); plain = at; continue; }
    }
    const image = text.startsWith('![', at), link = text[at] === '[';
    if (image || link) {
      const from = at + (image ? 2 : 1), close = text.indexOf('](', from);
      // Skip a failed opener as one literal span, rather than rescanning a long
      // unmatched bracket sequence once per character.
      if (close < 0) break;
      if (close - from > 8192) { at = close + 2; continue; }
      let end = close + 2, nesting = 1;
      for (; end < text.length && end - close <= 4098; end++) { if (text[end] === '(') nesting++; else if (text[end] === ')' && --nesting === 0) break; }
      if (nesting) { at = Math.min(text.length, end + 1); continue; }
      if (end - close > 4098) { at = end + 1; continue; }
      flush(at); const label = text.slice(from, close), target = text.slice(close + 2, end).trim();
      if (image) host.append(make('span', 'mdv-image-note', `[Image: ${label || 'unlabeled'}]`));
      else { const href = safeDocumentLink(target); if (href) { const anchor = make('a'); anchor.href = href; anchor.target = '_blank'; anchor.rel = 'noopener noreferrer'; inlineMarkdown(anchor, label, depth + 1); host.append(anchor); } else host.append(document.createTextNode(label)); }
      at = end + 1; plain = at; continue;
    }
    let marker = '';
    if (text[at] === '`') { let end = at + 1; while (text[end] === '`') end++; marker = text.slice(at, end); }
    else if (text.startsWith('**', at) || text.startsWith('__', at)) marker = text.slice(at, at + 2);
    else if (text[at] === '*' || text[at] === '_') marker = text[at]!;
    // C++/C# identifiers contain underscores. CommonMark does not use an
    // intraword underscore as an emphasis delimiter; preserve those names.
    if (marker[0] === '_' && at > 0 && /[\p{L}\p{N}_]/u.test(text[at - 1]!)) marker = '';
    if (marker) {
      const end = text.indexOf(marker, at + marker.length);
      if (end < 0) { at += marker.length; continue; }
      if (marker[0] === '_' && /[\p{L}\p{N}_]/u.test(text[end + marker.length] ?? '')) { at = end + marker.length; continue; }
      if (end === at + marker.length) { at = end + marker.length; continue; }
      flush(at); const code = marker[0] === '`', part = make(code ? 'code' : marker.length === 2 ? 'strong' : 'em');
      const value = text.slice(at + marker.length, end); if (code) part.textContent = value; else inlineMarkdown(part, value, depth + 1);
      host.append(part); at = end + marker.length; plain = at; continue;
    }
    at++;
  }
  flush(text.length);
}

function markdownBlock(block: MarkdownBlock): HTMLElement {
  if (block.type === 'rule') return make('hr');
  if (block.type === 'code') { const pre = make('pre', 'mdv-source'), code = make('code', '', block.text); if (block.language) pre.setAttribute('aria-label', `${block.language} code`); pre.append(code); return pre; }
  if (block.type === 'list') {
    const wrapper = make('div', 'mdv-list'), lines = block.text.split('\n'); let page = 0;
    const draw = () => {
      const list = make(block.ordered ? 'ol' : 'ul'); if (list instanceof HTMLOListElement) list.start = page * 100 + 1;
      for (const line of lines.slice(page * 100, (page + 1) * 100)) { const item = make('li'); inlineMarkdown(item, line); list.append(item); }
      wrapper.replaceChildren(list);
      if (lines.length > 100) { const controls = make('nav', 'mdv-list-pagination'), previous = button('←', 'Previous list items', () => { page--; draw(); }), next = button('→', 'Next list items', () => { page++; draw(); }); previous.disabled = !page; next.disabled = (page + 1) * 100 >= lines.length; controls.append(previous, make('span', '', `${page * 100 + 1}–${Math.min(lines.length, (page + 1) * 100)} of ${lines.length} items`), next); wrapper.append(controls); }
    };
    draw(); return wrapper;
  }
  const element = make(block.type === 'heading' ? `h${block.level ?? 1}` as 'h1' : block.type === 'quote' ? 'blockquote' : 'p');
  inlineMarkdown(element, block.text); return element;
}

/** A read-only component shared by inline attachments and their reading dialog.
 * The owner must call dispose when replacing it; bytes and model stay untouched. */
export function createDocumentViewer(asset: MeridianAsset, options: DocumentViewerOptions = {}): DocumentViewer {
  const bounded = (value: unknown, maximum: number) => typeof value === 'number' && Number.isFinite(value) ? Math.max(0, Math.min(maximum, value)) : 0;
  const initial = options.state ?? {}, initialLeft = bounded(initial.scrollLeft, 1_000_000_000), initialTop = bounded(initial.scrollTop, 1_000_000_000);
  let rememberedLeft = initialLeft, rememberedTop = initialTop;
  const element = make('section', 'mdv-viewer'); element.tabIndex = 0; element.setAttribute('role', 'region'); element.setAttribute('aria-label', `Document viewer: ${asset.name}`);
  const toolbar = make('div', 'mdv-toolbar'); toolbar.setAttribute('role', 'toolbar'); toolbar.setAttribute('aria-label', 'Document controls');
  const heading = make('strong', 'mdv-name', asset.name), format = make('span', 'mdv-format'); toolbar.append(heading, format);
  const download = make('a', 'mdv-download', 'Download original'); download.download = asset.name || 'document'; download.setAttribute('aria-label', `Download original ${asset.name}`); toolbar.append(download);
  if (options.onClose) toolbar.append(button('Collapse', 'Collapse document viewer', options.onClose));
  const search = make('input', 'mdv-find'); search.type = 'search'; search.placeholder = 'Find in document'; search.setAttribute('aria-label', 'Find in document'); search.autocomplete = 'off';
  search.maxLength = 4096; search.value = typeof initial.query === 'string' ? initial.query.slice(0, 4096) : '';
  const firstHeader = make('input'); firstHeader.type = 'checkbox'; firstHeader.checked = typeof initial.firstRowHeader === 'boolean' ? initial.firstRowHeader : true;
  const headerLabel = make('label', 'mdv-header-option', 'First row is a header'); headerLabel.prepend(firstHeader);
  const controls = make('div', 'mdv-search-controls'); controls.append(search);
  const scroll = make('div', 'mdv-scroll'); scroll.tabIndex = 0; scroll.setAttribute('aria-label', 'Document contents');
  const status = make('span', 'mdv-status'); status.setAttribute('role', 'status'); status.setAttribute('aria-live', 'polite');
  const pagination = make('nav', 'mdv-pagination'); pagination.setAttribute('aria-label', 'Document pages');
  let page = Math.trunc(bounded(initial.page, 16_777_216)), columnPage = Math.trunc(bounded(initial.columnPage, 16_777_216)), pageCount = 1, disposed = false, timer: ReturnType<typeof setTimeout> | undefined, blobURL: string | undefined, restoreFrame: number | undefined;
  let table: DocumentTable | undefined, source = '', kind = documentPreviewKind(asset), draw = () => {};
  const previous = button('←', 'Previous document page', () => { page = Math.max(0, page - 1); draw(); }), next = button('→', 'Next document page', () => { page = Math.min(pageCount - 1, page + 1); draw(); });
  const pageInput = make('input', 'mdv-page-number'); pageInput.type = 'number'; pageInput.min = '1'; pageInput.step = '1'; pageInput.setAttribute('aria-label', 'Document page');
  const pageTotal = make('span', 'mdv-page-total');
  pageInput.onchange = () => { page = Math.max(0, Math.min(pageCount - 1, Math.trunc(Number(pageInput.value) || 1) - 1)); draw(); };
  pagination.append(previous, pageInput, pageTotal, next, status); element.append(toolbar, controls, scroll, pagination);
  const abort = new AbortController();
  const rememberScroll = () => { if (scroll.isConnected) { rememberedLeft = scroll.scrollLeft; rememberedTop = scroll.scrollTop; } };
  // Editor redraw can detach the old scene before asking its owner to dispose.
  // Detached elements report zero layout/scroll, so retain the last live values.
  scroll.addEventListener('scroll', rememberScroll, { passive: true, signal: abort.signal });
  for (const type of ['pointerdown', 'pointermove', 'pointerup', 'click', 'dblclick', 'wheel', 'contextmenu'] as const) element.addEventListener(type, event => event.stopPropagation(), { signal: abort.signal });
  element.addEventListener('keydown', event => {
    event.stopPropagation();
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f' && kind !== 'pdf') { event.preventDefault(); search.focus(); search.select(); }
    else if (event.key === 'Escape' && search.value) { event.preventDefault(); search.value = ''; page = 0; draw(); }
  }, { signal: abort.signal });
  element.addEventListener('keyup', event => event.stopPropagation(), { signal: abort.signal });
  const finishPage = (total: number, pageSize: number, unit: string, query: string) => {
    pageCount = Math.max(1, Math.ceil(total / pageSize)); page = Math.min(page, pageCount - 1);
    previous.disabled = page === 0; next.disabled = page + 1 >= pageCount; pageInput.value = String(page + 1); pageInput.max = String(pageCount); pageTotal.textContent = `/ ${pageCount}`;
    status.textContent = total ? `${page * pageSize + 1}–${Math.min(total, (page + 1) * pageSize)} of ${total} ${query ? `matching ${unit}` : unit}` : query ? 'No matching content' : 'Empty document';
    scroll.scrollTop = 0; if (scroll.isConnected) { rememberedTop = 0; rememberedLeft = scroll.scrollLeft; }
  };
  try {
    const prepared = prepareDocumentPreview(asset), bytes = prepared.bytes;
    blobURL = URL.createObjectURL(new Blob([bytes], { type: kind === 'pdf' ? 'application/pdf' : asset.mime || 'application/octet-stream' })); download.href = blobURL;
    if (!kind) throw new Error('This attachment format does not have an embedded reader. Download its original file.');
    if (prepared.error) throw new Error(prepared.error);
    element.dataset.documentFormat = kind;
    format.textContent = kind === 'csv' ? documentDelimiter(asset) === '\t' ? 'TSV' : 'CSV' : kind === 'markdown' ? 'Markdown' : kind === 'pdf' ? 'PDF' : 'Text';
    if (kind === 'pdf') {
      if (!hasDocumentPdfSignature(bytes)) throw new Error('This attachment does not contain a valid PDF header. Download the original file to inspect it.');
      controls.hidden = pagination.hidden = true;
      const object = make('object', 'mdv-pdf'); object.type = 'application/pdf'; object.data = blobURL; object.setAttribute('aria-label', `PDF document: ${asset.name}`);
      object.append(make('p', '', 'The embedded PDF reader is unavailable in this browser. Download the original PDF using the document toolbar.')); scroll.append(object);
    } else {
      source = prepared.source;
      if (kind === 'csv') {
        table = prepared.table; controls.append(headerLabel);
        const columns = make('nav', 'mdv-column-pagination'); columns.setAttribute('aria-label', 'Document column pages');
        const columnLabel = make('span'), before = button('← columns', 'Previous document columns', () => { columnPage--; draw(); }), after = button('columns →', 'Next document columns', () => { columnPage++; draw(); }); columns.append(before, columnLabel, after); controls.append(columns);
        draw = () => {
          if (disposed || !table) return;
          const query = search.value.trim().toLocaleLowerCase(), startRow = firstHeader.checked && table.rowCount ? 1 : 0, pageSize = 50, columnSize = 32;
          const totalColumns = table.columnCount; columnPage = Math.max(0, Math.min(columnPage, Math.max(0, Math.ceil(totalColumns / columnSize) - 1)));
          const fromColumn = columnPage * columnSize, width = Math.min(columnSize, totalColumns - fromColumn), rows: number[] = []; let total = 0;
          if (!query) { total = Math.max(0, table.rowCount - startRow); page = Math.min(page, Math.max(0, Math.ceil(total / pageSize) - 1)); for (let i = startRow + page * pageSize; i < Math.min(table.rowCount, startRow + (page + 1) * pageSize); i++) rows.push(i); }
          else { for (const index of table.matchingRows(query, startRow)) { if (total >= page * pageSize && total < (page + 1) * pageSize) rows.push(index); total++; } if (page * pageSize >= total && page) { page = Math.max(0, Math.ceil(total / pageSize) - 1); draw(); return; } }
          const view = make('table', 'mdv-table'), head = make('thead'), header = make('tr'), corner = make('th', 'mdv-row-number', '#'); corner.scope = 'col'; header.append(corner);
          const labels = firstHeader.checked ? table.row(0, fromColumn, width) : [];
          for (let i = 0; i < width; i++) { const cell = make('th', '', labels[i] || `Column ${fromColumn + i + 1}`); cell.scope = 'col'; header.append(cell); } head.append(header); view.append(head);
          const body = make('tbody'); for (const index of rows) { const row = make('tr'), number = make('th', 'mdv-row-number', String(index + 1)); number.scope = 'row'; row.append(number); const cells = table.row(index, fromColumn, width); for (let i = 0; i < width; i++) row.append(make('td', '', cells[i] ?? '')); body.append(row); } view.append(body); view.setAttribute('aria-label', asset.name); scroll.replaceChildren(view);
          columns.hidden = totalColumns <= columnSize; before.disabled = !columnPage; after.disabled = (columnPage + 1) * columnSize >= totalColumns; columnLabel.textContent = `Columns ${fromColumn + 1}–${fromColumn + width} of ${totalColumns}`;
          finishPage(total, pageSize, 'rows', query);
        };
        firstHeader.onchange = () => { page = 0; draw(); };
      } else if (kind === 'markdown') {
        draw = () => {
          if (disposed) return; const query = search.value.trim().toLocaleLowerCase(), pageSize = 60, selected: MarkdownBlock[] = []; let total = 0;
          for (const block of prepared.blocks ?? documentMarkdownBlocks(source)) { if (query && !block.text.toLocaleLowerCase().includes(query)) continue; if (total >= page * pageSize && total < (page + 1) * pageSize) selected.push(block); total++; }
          if (page * pageSize >= total && page) { page = Math.max(0, Math.ceil(total / pageSize) - 1); draw(); return; }
          const article = make('article', 'mdv-prose'); for (const block of selected) article.append(markdownBlock(block)); scroll.replaceChildren(article); finishPage(total, pageSize, 'sections', query);
        };
      } else {
        // Text/source previews paginate full lines. HTML, XML and scripts remain
        // text nodes; syntax and original line breaks are never executed.
        const lines = prepared.lines!;
        draw = () => {
          if (disposed) return; const query = search.value.trim().toLocaleLowerCase(), pageSize = 200, selected: string[] = []; let total = 0;
          if (!query) { total = lines.lineCount; page = Math.min(page, Math.max(0, Math.ceil(total / pageSize) - 1)); for (let i = page * pageSize; i < Math.min(total, (page + 1) * pageSize); i++) selected.push(lines.line(i)); }
          else for (let i = 0; i < lines.lineCount; i++) { const line = lines.line(i); if (!line.toLocaleLowerCase().includes(query)) continue; if (total >= page * pageSize && total < (page + 1) * pageSize) selected.push(line); total++; }
          if (page * pageSize >= total && page) { page = Math.max(0, Math.ceil(total / pageSize) - 1); draw(); return; }
          const pre = make('pre', 'mdv-source', selected.join('')); pre.setAttribute('aria-label', 'Document source text'); scroll.replaceChildren(pre); finishPage(source ? total : 0, pageSize, 'lines', query);
        };
      }
      search.oninput = () => { page = 0; clearTimeout(timer); timer = setTimeout(() => draw(), 140); };
      draw();
    }
  } catch (error) {
    controls.hidden = pagination.hidden = true; const message = make('p', 'mdv-error', error instanceof Error ? error.message : 'This attachment could not be previewed.'); message.setAttribute('role', 'alert'); scroll.replaceChildren(message);
    if (!blobURL) { download.removeAttribute('href'); download.setAttribute('aria-disabled', 'true'); }
  }
  restoreFrame = requestAnimationFrame(() => { restoreFrame = undefined; if (!disposed) { scroll.scrollLeft = initialLeft; scroll.scrollTop = initialTop; rememberScroll(); } });
  return { element, captureState: () => { if (restoreFrame === undefined) rememberScroll(); return { page, columnPage, query: search.value.slice(0, 4096), firstRowHeader: firstHeader.checked, scrollLeft: rememberedLeft, scrollTop: rememberedTop }; }, dispose: () => { if (disposed) return; disposed = true; clearTimeout(timer); if (restoreFrame !== undefined) cancelAnimationFrame(restoreFrame); abort.abort(); if (blobURL) URL.revokeObjectURL(blobURL); element.replaceChildren(); } };
}
