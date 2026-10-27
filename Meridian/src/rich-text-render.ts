import type { TextRun } from './core/types.ts';
import { textSegments } from './core/rich-text.ts';
import { fontFamily } from './core/fonts.ts';

/** Persisted text is always literal data. Only validated formatting properties
 * become native inline styles, so publications never interpret authored HTML. */
export function renderRichText(element: HTMLElement, text: string, runs?: readonly TextRun[]): void {
  element.classList.remove('ms-rich-text');
  if (!runs?.length) { element.textContent = text; return; }
  const segments = textSegments(text, runs);
  if (!segments.some(segment => Object.keys(segment.style).length)) { element.textContent = text; return; }
  element.replaceChildren(); element.classList.add('ms-rich-text');
  for (const segment of segments) {
    const span = document.createElement('span'); span.className = 'ms-text-run'; span.textContent = segment.text;
    span.dataset.textStart = String(segment.start); span.dataset.textEnd = String(segment.end);
    const style = segment.style;
    if (style.fontFamily !== undefined) span.style.fontFamily = fontFamily(style.fontFamily).css;
    if (style.fontSize !== undefined) span.style.fontSize = `${style.fontSize}px`;
    if (style.color !== undefined) span.style.color = style.color;
    if (style.bold !== undefined) span.style.fontWeight = style.bold ? '700' : '400';
    if (style.italic !== undefined) span.style.fontStyle = style.italic ? 'italic' : 'normal';
    if (style.underline !== undefined) span.style.textDecoration = style.underline ? 'underline' : 'none';
    element.append(span);
  }
}
