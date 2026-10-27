import type { FontFamily, NodeStyle, TextFormat, TextRun } from './core/types.ts';
import { applyTextFormat, clampTextOffset, isTextRuns, normalizeTextRuns, replaceTextRange, textSegments, textStyleAt, TEXT_FORMAT_KEYS } from './core/rich-text.ts';
import { fontFamily } from './core/fonts.ts';
import { createFontPicker } from './font-picker.ts';
import { renderRichText } from './rich-text-render.ts';

export interface RichTextValue { text: string; runs: TextRun[] }
export interface TextSelection { start: number; end: number; backward?: boolean }
interface HistoryEntry extends RichTextValue { selection: TextSelection; typing?: TextFormat }
export interface RichTextOptions extends RichTextValue {
  label: string; title?: boolean; base?: NodeStyle; maxLength?: number;
  /** Adopt a native scene text surface without moving it or replacing its fitting styles. */
  inlineSurface?: HTMLElement;
  onSelectionChange?: (selection: TextSelection) => void;
  onRender?: () => void;
  onChange: (value: RichTextValue) => boolean | void;
}
const editors = new Set<RichTextEditor>();
const instances = new WeakMap<HTMLElement, RichTextEditor>();
const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);
const copy = <T>(value: T): T => structuredClone(value);
const mime = 'application/x-meridian-text';
const make = <K extends keyof HTMLElementTagNameMap>(tag: K, className = '') => {
  const element = document.createElement(tag); element.className = className; return element;
};

/** All persisted content is plain text plus typed marks. DOM markup is transient. */
export class RichTextEditor {
  readonly root = make('div', 'rich-text-editor');
  readonly editable: HTMLElement;
  private value: RichTextValue;
  private committed: RichTextValue;
  private base: NodeStyle;
  private selection: TextSelection = { start: 0, end: 0 };
  private typing: TextFormat | undefined;
  private typingAt: number | undefined;
  private undo: HistoryEntry[] = [];
  private redo: HistoryEntry[] = [];
  private timer: ReturnType<typeof setTimeout> | undefined;
  private composing = false;
  private inputTarget: { selection: TextSelection; type: string } | undefined;
  private feedback: string | undefined;
  private lastTyping = 0;
  private lastSelectionEnd = -1;
  private readonly font;
  private readonly size = make('input');
  private readonly color = make('input');
  private readonly toggles = new Map<'bold' | 'italic' | 'underline', HTMLButtonElement>();
  private readonly undoButton: HTMLButtonElement;
  private readonly redoButton: HTMLButtonElement;
  private readonly status = make('span', 'rich-text-status');
  private abort = new AbortController();
  private inlineAttributes: Map<string, string | null> | undefined;

  constructor(private options: RichTextOptions) {
    this.editable = options.inlineSurface ?? make('div', 'rich-text-input');
    if (options.inlineSurface) {
      this.inlineAttributes = new Map(['contenteditable', 'role', 'aria-label', 'aria-multiline', 'spellcheck', 'tabindex', 'data-rich-text'].map(name => [name, this.editable.getAttribute(name)]));
      this.editable.classList.add('canvas-inline-text');
      this.root.classList.add('canvas-inline-toolbar');
    }
    this.value = { text: options.text, runs: normalizeTextRuns(options.text, options.runs) };
    this.committed = copy(this.value); this.base = options.base ?? {};
    this.root.setAttribute('role', 'group'); this.root.setAttribute('aria-label', `${options.label} editor`);
    const toolbar = make('div', 'rich-text-toolbar'); toolbar.setAttribute('role', 'toolbar'); toolbar.setAttribute('aria-label', `${options.label} formatting`);
    this.font = createFontPicker(this.base.fontFamily, family => {
      this.format({ fontFamily: family });
      // The shared picker restores keyboard focus to its trigger after saving
      // an inspector. On-canvas formatting instead returns to the native text
      // range, keeping the selected passage visible and ready for typing.
      if (options.inlineSurface) requestAnimationFrame(() => requestAnimationFrame(() => {
        if (this.root.isConnected && this.owns(document.activeElement)) this.restoreSelection();
      }));
    });
    this.font.button.setAttribute('aria-label', 'Selection font'); this.font.button.title = 'Font for selected text';
    this.size.type = 'number'; this.size.min = '1'; this.size.max = '512'; this.size.step = 'any'; this.size.setAttribute('aria-label', 'Selection size'); this.size.title = 'Font size for selected text';
    this.size.onchange = () => { if (this.size.value && this.size.reportValidity()) this.format({ fontSize: Number(this.size.value) }); };
    this.size.onkeydown = event => { if (event.key === 'Enter') { event.preventDefault(); this.size.dispatchEvent(new Event('change')); } };
    this.color.type = 'color'; this.color.setAttribute('aria-label', 'Selection color'); this.color.title = 'Color for selected text'; this.color.oninput = () => this.format({ color: this.color.value }, false); this.color.onchange = () => this.flush();
    toolbar.append(this.font.button, this.size, this.color, this.font.popup);
    const actions = make('div', 'rich-text-actions');
    for (const [key, label, glyph] of [['bold', 'Bold', 'B'], ['italic', 'Italic', 'I'], ['underline', 'Underline', 'U']] as const) {
      const button = this.action(label, glyph, () => this.toggle(key)); button.classList.add(`rich-${key}`); button.title = `${label} · Ctrl+${glyph}`;
      this.toggles.set(key, button); actions.append(button);
    }
    actions.append(this.action('Clear formatting', 'Tx', () => this.format(Object.fromEntries(TEXT_FORMAT_KEYS.map(key => [key, undefined])))));
    const spacer = make('span', 'rich-text-action-space');
    this.undoButton = this.action('Undo text edit', '↶', () => this.history(false)); this.redoButton = this.action('Redo text edit', '↷', () => this.history(true));
    actions.append(spacer, this.undoButton, this.redoButton);
    this.editable.contentEditable = 'true'; this.editable.setAttribute('role', 'textbox'); this.editable.setAttribute('aria-label', options.label);
    this.editable.setAttribute('aria-multiline', String(!options.title)); this.editable.spellcheck = true; this.editable.tabIndex = 0;
    this.editable.dataset.richText = options.title ? 'title' : 'body';
    const hint = make('div', 'rich-text-footer'); this.status.setAttribute('aria-live', 'polite'); hint.append(this.status);
    this.root.append(toolbar, actions);
    if (!options.inlineSurface) this.root.append(this.editable);
    this.root.append(hint); instances.set(this.root, this); instances.set(this.editable, this); editors.add(this);
    for (const group of [toolbar, actions]) group.addEventListener('pointerdown', event => {
      this.captureSelection(); if ((event.target as HTMLElement).closest('button') && !(event.target as HTMLElement).closest('.font-picker-control,.font-picker-popup')) event.preventDefault();
    }, { signal: this.abort.signal });
    this.editable.addEventListener('beforeinput', event => {
      if (this.composing || event.isComposing) return;
      this.captureSelection();
      const target = event.getTargetRanges?.()[0];
      const range = target && this.editable.contains(target.startContainer) && this.editable.contains(target.endContainer)
        ? { start: this.offset(target.startContainer, target.startOffset), end: this.offset(target.endContainer, target.endOffset) } : { ...this.selection };
      this.inputTarget = { selection: range, type: event.inputType };
      // Chromium can delete a complete adopted SPAN title selection and then
      // abandon native insertion without an input event. Canonical insertion
      // keeps this inline editing host intact and retains passage marks.
      if (options.inlineSurface && options.title && ['insertText', 'insertReplacementText'].includes(event.inputType) && event.data !== null) {
        event.preventDefault(); this.inputTarget = undefined;
        this.protect(() => this.insertValue(event.data!, undefined, event.inputType === 'insertText', range)); return;
      }
      if (options.inlineSurface && options.title && event.inputType.startsWith('delete')) {
        event.preventDefault(); this.inputTarget = undefined;
        let removal = range;
        // TargetRanges supplies native grapheme/word deletion boundaries. Older
        // engines without it retain safe code-point deletion as a fallback.
        if (removal.start === removal.end) {
          if (event.inputType.endsWith('Backward')) removal = { start: clampTextOffset(this.value.text, Math.max(0, removal.start - 1)), end: removal.end };
          else if (event.inputType.endsWith('Forward')) removal = { start: removal.start, end: clampTextOffset(this.value.text, removal.end + 1, 'forward') };
        }
        this.protect(() => this.insertValue('', undefined, true, removal)); return;
      }
      if (event.inputType === 'insertParagraph' || event.inputType === 'insertLineBreak') { event.preventDefault(); if (!options.title) this.insert('\n'); }
      if (event.inputType === 'historyUndo' || event.inputType === 'historyRedo') { event.preventDefault(); this.history(event.inputType === 'historyRedo'); }
    }, { signal: this.abort.signal });
    this.editable.addEventListener('input', event => { if (!this.composing && !(event as InputEvent).isComposing) this.readInput(); }, { signal: this.abort.signal });
    this.editable.addEventListener('compositionstart', () => { this.captureSelection(); this.inputTarget = { selection: { ...this.selection }, type: 'insertCompositionText' }; this.composing = true; }, { signal: this.abort.signal });
    this.editable.addEventListener('compositionend', () => { this.composing = false; this.readInput(); }, { signal: this.abort.signal });
    this.editable.addEventListener('keydown', event => {
      if (event.isComposing || event.altKey || !(event.ctrlKey || event.metaKey)) return;
      const key = event.key.toLowerCase();
      if (['b', 'i', 'u'].includes(key)) { event.preventDefault(); event.stopPropagation(); this.toggle(key === 'b' ? 'bold' : key === 'i' ? 'italic' : 'underline'); }
      if (key === 'z' || key === 'y') { event.preventDefault(); event.stopPropagation(); this.history(key === 'y' || event.shiftKey); }
    }, { signal: this.abort.signal });
    this.editable.addEventListener('copy', event => this.copy(event, false), { signal: this.abort.signal }); this.editable.addEventListener('cut', event => this.copy(event, true), { signal: this.abort.signal });
    this.editable.addEventListener('paste', event => {
      event.preventDefault(); event.stopPropagation(); const data = event.clipboardData; if (!data) return;
      const text = data.getData('text/plain').replace(/\r\n?/g, '\n');
      let runs: TextRun[] | undefined;
      try { const value = JSON.parse(data.getData(mime)); if (value.version === 1 && value.text === text && isTextRuns(text, value.runs)) runs = value.runs; } catch { /* External rich HTML never becomes document markup. */ }
      this.insert(text, runs);
    }, { signal: this.abort.signal });
    this.editable.addEventListener('dragover', event => event.preventDefault(), { signal: this.abort.signal });
    this.editable.addEventListener('drop', event => { event.preventDefault(); event.stopPropagation(); const text = event.dataTransfer?.getData('text/plain'); if (text) this.insert(text.replace(/\r\n?/g, '\n')); }, { signal: this.abort.signal });
    const flushBlur = () => setTimeout(() => { if (this.root.isConnected && !this.owns(document.activeElement)) this.flush(); }, 0);
    this.root.addEventListener('focusout', flushBlur, { signal: this.abort.signal });
    if (options.inlineSurface) this.editable.addEventListener('focusout', flushBlur, { signal: this.abort.signal });
    document.addEventListener('selectionchange', () => { if (!this.composing && this.root.isConnected && this.captureSelection()) this.refresh(); }, { signal: this.abort.signal });
    // Keep the text hit by the initiating pointerdown alive until the browser
    // has placed its caret/range. Scene text is already rendered canonically.
    if (!options.inlineSurface || this.editable.textContent !== this.value.text) this.paint();
    this.refresh();
  }

  private action(label: string, glyph: string, action: () => void) {
    const button = make('button'); button.type = 'button'; button.textContent = glyph; button.setAttribute('aria-label', label); button.title = label; button.onclick = action; return button;
  }
  private defaults(): Required<TextFormat> {
    return { fontFamily: this.base.fontFamily ?? 'sans', fontSize: this.options.title ? this.base.titleSize ?? 13 : this.base.fontSize ?? 12,
      color: this.base.textColor ?? '#dce5ed', bold: !!this.options.title, italic: false, underline: false };
  }
  private currentStyles(): Required<TextFormat>[] {
    const fallback = this.defaults(), { start, end } = this.selection;
    if (start === end) return [{ ...fallback, ...(this.typing ?? textStyleAt(this.value.runs, Math.max(0, start - 1))) }];
    return textSegments(this.value.text, this.value.runs).filter(segment => segment.end > start && segment.start < end).map(segment => ({ ...fallback, ...segment.style }));
  }
  private refresh() {
    const styles = this.currentStyles(); if (!styles.length) styles.push(this.defaults());
    const common = <K extends keyof TextFormat>(key: K) => styles.every(style => style[key] === styles[0]![key]) ? styles[0]![key] : undefined;
    const family = common('fontFamily'); this.font.setValue(family ?? this.defaults().fontFamily);
    if (!family) this.font.button.querySelector('span')!.textContent = 'Mixed fonts';
    const size = common('fontSize'); if (document.activeElement !== this.size) this.size.value = size === undefined ? '' : String(size); this.size.placeholder = 'Mixed';
    const color = common('color'); if (document.activeElement !== this.color) this.color.value = color ?? this.defaults().color;
    this.color.classList.toggle('is-mixed', color === undefined);
    for (const [key, button] of this.toggles) button.setAttribute('aria-pressed', common(key) === undefined ? 'mixed' : String(common(key)));
    this.undoButton.disabled = !this.undo.length; this.redoButton.disabled = !this.redo.length;
    this.status.textContent = this.feedback ?? (this.selection.start === this.selection.end ? 'Formatting applies as you type.' : `${this.selection.end - this.selection.start} characters selected`);
    this.status.classList.toggle('is-error', !!this.feedback);
    this.options.onSelectionChange?.({ ...this.selection });
  }
  private paint() {
    renderRichText(this.editable, this.value.text, this.value.runs);
    if (!this.options.inlineSurface) {
      this.editable.style.fontFamily = fontFamily(this.base.fontFamily).css;
      this.editable.style.fontSize = `${this.defaults().fontSize}px`; this.editable.style.color = this.defaults().color;
      this.editable.style.fontWeight = this.options.title ? '600' : '400';
    }
    if (!this.value.text || this.value.text.endsWith('\n')) { const placeholder = make('br'); placeholder.dataset.richPlaceholder = ''; this.editable.append(placeholder); }
    this.options.onRender?.();
  }
  private offset(node: Node, offset: number): number {
    const range = document.createRange(); range.selectNodeContents(this.editable);
    try { range.setEnd(node, offset); return range.toString().length; } catch { return this.value.text.length; }
  }
  captureSelection(): boolean {
    // Toolbar/popup focus can collapse Chrome's DOM selection to the host root.
    // The last editor selection remains authoritative while controls own focus.
    if (document.activeElement !== this.editable || this.composing) return false;
    const selection = document.getSelection(); if (!selection?.anchorNode || !selection.focusNode || !this.editable.contains(selection.anchorNode) || !this.editable.contains(selection.focusNode)) return false;
    const anchor = this.offset(selection.anchorNode, selection.anchorOffset), focus = this.offset(selection.focusNode, selection.focusOffset);
    const point = clampTextOffset(this.value.text, anchor);
    const next = anchor === focus ? { start: point, end: point } : { start: clampTextOffset(this.value.text, Math.min(anchor, focus)), end: clampTextOffset(this.value.text, Math.max(anchor, focus), 'forward'), backward: anchor > focus };
    if (this.typingAt !== undefined && (next.start !== next.end || next.start !== this.typingAt)) { this.typing = undefined; this.typingAt = undefined; }
    this.selection = next; return true;
  }
  restoreSelection(focus = true) {
    if (!this.editable.isConnected) return;
    if (!focus && document.activeElement !== this.editable) return;
    const point = (position: number): [Node, number] => {
      const walker = document.createTreeWalker(this.editable, NodeFilter.SHOW_TEXT); let node: Node | null, left = position, last: Node | undefined;
      while ((node = walker.nextNode())) { last = node; const length = node.textContent?.length ?? 0; if (left <= length) return [node, left]; left -= length; }
      return last ? [last, last.textContent!.length] : [this.editable, 0];
    };
    if (focus) this.editable.focus({ preventScroll: true }); const selection = document.getSelection(); if (!selection) return;
    const start = point(this.selection.start), end = point(this.selection.end);
    selection.setBaseAndExtent(...(this.selection.backward ? end : start), ...(this.selection.backward ? start : end));
  }
  private snapshot(): HistoryEntry { return { ...copy(this.value), selection: { ...this.selection }, ...(this.typing !== undefined ? { typing: copy(this.typing) } : {}) }; }
  private remember(typing = false) {
    const now = performance.now();
    if (!typing || now - this.lastTyping > 750 || this.selection.start !== this.lastSelectionEnd || this.selection.start !== this.selection.end || this.redo.length) {
      this.undo.push(this.snapshot()); if (this.undo.length > 100) this.undo.shift();
    }
    this.redo = []; this.lastTyping = typing ? now : 0;
  }
  private readPlain() {
    if (!this.editable.textContent && this.editable.querySelector('br')) return '';
    const walk = (node: Node): string => {
      if (node.nodeType === Node.TEXT_NODE) return node.textContent ?? '';
      if (!(node instanceof HTMLElement)) return '';
      if (node.tagName === 'BR') return node.hasAttribute('data-rich-placeholder') ? '' : '\n';
      return [...node.childNodes].map((child, index) => (index && child instanceof HTMLElement && ['DIV', 'P'].includes(child.tagName) ? '\n' : '') + walk(child)).join('');
    };
    return walk(this.editable).replace(/\r\n?/g, '\n');
  }
  private readInput() {
    this.protect(() => this.readInputValue());
  }
  private readInputValue() {
    const text = this.readPlain(), selection = document.getSelection();
    const caret = selection?.focusNode && this.editable.contains(selection.focusNode) ? this.offset(selection.focusNode, selection.focusOffset) : text.length;
    if (text === this.value.text) return;
    if (text.length > (this.options.maxLength ?? 1_000_000) || this.options.title && /[\r\n]/.test(text)) { this.paint(); this.restoreSelection(); this.feedback = 'This text is too long or contains an unsupported title line break.'; this.refresh(); return; }
    const previous = this.value.text, target = this.inputTarget; this.inputTarget = undefined;
    let start = target?.selection.start ?? this.selection.start, oldEnd = target?.selection.end ?? this.selection.end;
    if (start === oldEnd && target?.type.startsWith('delete')) {
      const deleted = Math.max(0, previous.length - text.length);
      if (target.type.endsWith('Backward')) start -= deleted; else oldEnd += deleted;
    }
    start = clampTextOffset(previous, start); oldEnd = clampTextOffset(previous, oldEnd, 'forward');
    let newEnd = start + text.length - (previous.length - (oldEnd - start));
    // Use the native edit target first. A global string diff is ambiguous for
    // repeated words/letters and can attach marks to an entirely different edit.
    if (newEnd < start || previous.slice(0, start) + text.slice(start, newEnd) + previous.slice(oldEnd) !== text) {
      start = 0; oldEnd = previous.length; newEnd = text.length;
      while (start < oldEnd && start < newEnd && previous[start] === text[start]) start++;
      start = clampTextOffset(previous, start);
      while (oldEnd > start && newEnd > start && previous[oldEnd - 1] === text[newEnd - 1]) { oldEnd--; newEnd--; }
      oldEnd = clampTextOffset(previous, oldEnd, 'forward'); newEnd = clampTextOffset(text, newEnd, 'forward');
    }
    this.remember(true); const marks = this.typing ?? textStyleAt(this.value.runs, this.selection.start === this.selection.end ? Math.max(0, this.selection.start - 1) : this.selection.start);
    this.value = replaceTextRange(previous, this.value.runs, start, oldEnd, text.slice(start, newEnd), marks);
    this.selection = { start: clampTextOffset(this.value.text, caret), end: clampTextOffset(this.value.text, caret) };
    this.typing = copy(marks); this.typingAt = this.selection.end; this.lastSelectionEnd = this.selection.end;
    this.paint(); this.restoreSelection(); this.refresh(); this.schedule();
  }
  private schedule() { clearTimeout(this.timer); this.timer = setTimeout(() => this.flush(), 150); }
  private insert(text: string, pasted?: TextRun[]) {
    this.protect(() => this.insertValue(text, pasted));
  }
  private insertValue(text: string, pasted?: TextRun[], typing = false, selection?: TextSelection) {
    if (selection) this.selection = { ...selection }; else this.captureSelection();
    if (this.options.title) {
      let normalized = { text, runs: pasted ?? [] };
      for (const match of [...text.matchAll(/[\r\n]+/g)].reverse()) normalized = replaceTextRange(normalized.text, normalized.runs, match.index!, match.index! + match[0].length, ' ', {});
      text = normalized.text; if (pasted) pasted = normalized.runs;
    }
    if (this.value.text.length - (this.selection.end - this.selection.start) + text.length > (this.options.maxLength ?? 1_000_000)) { this.feedback = 'The inserted text exceeds this field’s length limit.'; this.refresh(); return; }
    this.remember(typing); const at = this.selection.start, marks = this.typing ?? textStyleAt(this.value.runs, at === this.selection.end ? Math.max(0, at - 1) : at);
    this.value = replaceTextRange(this.value.text, this.value.runs, at, this.selection.end, text, pasted ? {} : marks);
    if (pasted) for (const run of pasted) this.value.runs = applyTextFormat(this.value.text, this.value.runs, at + run.start, at + run.end, run.style);
    this.selection = { start: at + text.length, end: at + text.length }; this.typing = typing ? copy(marks) : undefined; this.typingAt = typing ? this.selection.end : undefined;
    if (typing) this.lastSelectionEnd = this.selection.end;
    this.paint(); this.restoreSelection(); this.refresh(); if (typing) this.schedule(); else this.flush();
  }
  private copy(event: ClipboardEvent, cut: boolean) {
    this.captureSelection(); if (this.selection.start === this.selection.end || !event.clipboardData) return;
    event.preventDefault(); event.stopPropagation(); const { start, end } = this.selection, text = this.value.text.slice(start, end);
    const runs = normalizeTextRuns(text, textSegments(this.value.text, this.value.runs).filter(run => run.end > start && run.start < end).map(run => ({ start: Math.max(0, run.start - start), end: Math.min(end, run.end) - start, style: { ...this.defaults(), ...copy(run.style) } })));
    event.clipboardData.setData('text/plain', text); event.clipboardData.setData(mime, JSON.stringify({ version: 1, text, runs })); if (cut) this.insert('');
  }
  private format(patch: Partial<TextFormat>, focus = true) {
    this.protect(() => this.formatValue(patch, focus));
  }
  private formatValue(patch: Partial<TextFormat>, focus: boolean) {
    this.captureSelection(); this.remember();
    if (this.selection.start === this.selection.end) {
      const style = { ...(this.typing ?? textStyleAt(this.value.runs, Math.max(0, this.selection.start - 1))), ...patch };
      for (const key of TEXT_FORMAT_KEYS) if (style[key] === undefined) delete style[key];
      this.typing = style; this.typingAt = this.selection.end;
    } else {
      this.value.runs = applyTextFormat(this.value.text, this.value.runs, this.selection.start, this.selection.end, patch);
      this.typing = undefined; this.typingAt = undefined; this.paint();
    }
    if (focus) this.restoreSelection(); this.refresh(); if (focus) this.flush(); else this.schedule();
  }
  private toggle(key: 'bold' | 'italic' | 'underline') { this.captureSelection(); const enabled = this.currentStyles().every(style => style[key]); this.format({ [key]: !enabled }); }
  private history(forward: boolean) {
    this.captureSelection(); const from = forward ? this.redo : this.undo, to = forward ? this.undo : this.redo, entry = from.pop(); if (!entry) return;
    to.push(this.snapshot()); this.value = { text: entry.text, runs: copy(entry.runs) }; this.selection = { ...entry.selection }; this.typing = entry.typing;
    this.typingAt = this.typing ? this.selection.end : undefined; this.lastTyping = 0; this.paint(); this.restoreSelection(); this.refresh(); this.flush();
  }
  private protect(action: () => void) {
    const before = this.snapshot(), committed = copy(this.committed), undo = [...this.undo], redo = [...this.redo];
    try { this.feedback = undefined; action(); }
    catch (error) {
      clearTimeout(this.timer); this.value = { text: before.text, runs: before.runs }; this.committed = committed;
      this.selection = before.selection; this.typing = before.typing; this.undo = undo; this.redo = redo;
      this.feedback = error instanceof Error ? error.message : 'This edit could not be applied.'; this.paint(); this.restoreSelection(); this.refresh();
      if (!same(this.value, this.committed)) this.schedule();
    }
  }
  flush(finishComposition = false) {
    if (this.composing && finishComposition) { this.composing = false; this.readInput(); }
    clearTimeout(this.timer); this.timer = undefined; if (this.composing || same(this.value, this.committed)) return;
    const before = copy(this.committed); this.committed = copy(this.value);
    if (this.options.onChange(copy(this.value)) === false) { this.value = before; this.committed = copy(before); this.feedback = 'This edit could not be applied. Your previous text is preserved.'; this.paint(); this.restoreSelection(); this.refresh(); }
  }
  sync(text: string, runs: TextRun[] | undefined, base: NodeStyle | undefined) {
    const baseChanged = !same(this.base, base ?? {}), focused = document.activeElement === this.editable;
    this.base = base ?? {};
    const next = { text, runs: normalizeTextRuns(text, runs) };
    if (!same(next, this.committed) && !same(next, this.value)) { this.value = next; this.committed = copy(next); this.undo = []; this.redo = []; this.typing = undefined; this.selection = { start: Math.min(this.selection.start, text.length), end: Math.min(this.selection.end, text.length) }; this.paint(); }
    else if (baseChanged) { this.captureSelection(); this.paint(); if (focused) this.restoreSelection(); }
    this.refresh();
  }
  getValue(): RichTextValue { return copy(this.value); }
  getSelection(): TextSelection { return { ...this.selection }; }
  /** Range coordinates stay in screen space even inside fitted/rotated text. */
  selectionBounds(): DOMRect | undefined {
    if (this.selection.start === this.selection.end || !this.editable.isConnected) return;
    const point = (position: number): [Node, number] => {
      const walker = document.createTreeWalker(this.editable, NodeFilter.SHOW_TEXT); let child: Node | null, left = position;
      while ((child = walker.nextNode())) { const length = child.textContent?.length ?? 0; if (left <= length) return [child, left]; left -= length; }
      return [this.editable, this.editable.childNodes.length];
    };
    const range = document.createRange(); range.setStart(...point(this.selection.start)); range.setEnd(...point(this.selection.end));
    const bounds = range.getBoundingClientRect(); return bounds.width || bounds.height ? bounds : undefined;
  }
  owns(target: Node | null): boolean { return !!target && (this.root.contains(target) || this.editable.contains(target)); }
  dispose() {
    clearTimeout(this.timer); this.abort.abort(); editors.delete(this);
    if (this.inlineAttributes) {
      for (const [name, value] of this.inlineAttributes) { if (value === null) this.editable.removeAttribute(name); else this.editable.setAttribute(name, value); }
      this.editable.classList.remove('canvas-inline-text');
    }
    instances.delete(this.root); instances.delete(this.editable);
  }
}

export function createRichTextEditor(options: RichTextOptions) { return new RichTextEditor(options); }
export function flushRichTextEditors(finishComposition = false) { for (const editor of editors) if (editor.root.isConnected) editor.flush(finishComposition); }
export function captureRichTextFocus(host: HTMLElement) {
  const active = document.activeElement as HTMLElement | null, root = active?.closest<HTMLElement>('.rich-text-editor');
  if (!root || !host.contains(root)) return;
  const editor = instances.get(root); if (!editor || active !== editor.editable) return;
  editor.captureSelection(); return editor;
}
export function restoreRichTextFocus(editor: RichTextEditor | undefined) { editor?.restoreSelection(); }

export function openRichTextDialog(options: RichTextOptions, title = 'Edit text', onClose?: () => void) {
  const restore = document.activeElement as HTMLElement | null, dialog = make('dialog', 'modal rich-text-dialog'); dialog.setAttribute('aria-label', title);
  const heading = make('header', 'modal-header'), name = make('h2'); name.textContent = title;
  const close = make('button', 'icon-button'); close.type = 'button'; close.setAttribute('aria-label', 'Close text editor'); close.textContent = '×';
  const editor = createRichTextEditor(options), footer = make('footer', 'modal-footer'), done = make('button', 'primary'); done.type = 'button'; done.textContent = 'Done';
  heading.append(name, close); footer.append(done); dialog.append(heading, editor.root, footer); document.body.append(dialog);
  close.onclick = done.onclick = () => { editor.flush(true); dialog.close(); };
  dialog.addEventListener('cancel', () => editor.flush(true));
  dialog.addEventListener('close', () => { editor.flush(true); editor.dispose(); dialog.remove(); onClose?.(); if (restore?.isConnected) restore.focus({ preventScroll: true }); });
  dialog.showModal(); editor.editable.focus(); return editor;
}
