export interface TextEnvelope { width: number; height: number }
let rotationContext: CanvasRenderingContext2D | null | undefined;

/** The same native/vector text envelope drives automatic sizing and paint. */
export function textRotation(value: number | undefined): number {
  return typeof value === 'number' && Number.isFinite(value) ? Math.max(-180, Math.min(180, value)) : 0;
}
export function rotatedTextEnvelope(width: number, height: number, degrees: number): TextEnvelope {
  const radians = textRotation(degrees) * Math.PI / 180;
  // Cardinal rotations must not accumulate a spare pixel through cos(90°).
  const cosine = Math.abs(Math.cos(radians)), sine = Math.abs(Math.sin(radians));
  const x = cosine < 1e-12 ? 0 : cosine, y = sine < 1e-12 ? 0 : sine;
  return { width: Math.max(0, width) * x + Math.max(0, height) * y,
    height: Math.max(0, width) * y + Math.max(0, height) * x };
}

/** Mark only text surfaces, never the surrounding card, controls or media. */
export function setTextRotation(element: HTMLElement, degrees: number | undefined): void {
  const rotation = textRotation(degrees);
  if (rotation) element.dataset.textRotation = String(rotation);
}

/** A formatted word may cross several inline spans. Measure its whole reading
 * range with each span's actual font rather than flattening it to the parent's
 * font, which would rewrap large passages into clipped rotated envelopes. */
function formattedReadingWidths(element: HTMLElement, context: CanvasRenderingContext2D) {
  const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
  const pieces: Array<{ start: number; end: number; text: string; font: string; spacing: number }> = [];
  let source = '', child: Node | null;
  while ((child = walker.nextNode())) {
    const text = child.textContent ?? '', style = getComputedStyle(child.parentElement!);
    pieces.push({ start: source.length, end: source.length + text.length, text,
      font: `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`, spacing: Number.parseFloat(style.letterSpacing) || 0 });
    source += text;
  }
  const width = (start: number, end: number) => {
    let measured = 0;
    let low = 0, high = pieces.length;
    while (low < high) { const middle = (low + high) >>> 1; if (pieces[middle]!.end <= start) low = middle + 1; else high = middle; }
    for (let at = low; at < pieces.length && pieces[at]!.start < end; at++) {
      const piece = pieces[at]!;
      const text = piece.text.slice(Math.max(0, start - piece.start), Math.min(piece.text.length, end - piece.start));
      context.font = piece.font; measured += context.measureText(text).width + Math.max(0, [...text].length - 1) * piece.spacing;
    }
    return measured;
  };
  let start = 0, longest = 1, wordMinimum = 1;
  for (const newline of source.matchAll(/\r\n?|\n/g)) { longest = Math.max(longest, width(start, newline.index!)); start = newline.index! + newline[0].length; }
  longest = Math.max(longest, width(start, source.length));
  for (const word of source.matchAll(/\S+/gu)) wordMinimum = Math.max(wordMinimum, width(word.index!, word.index! + word[0].length));
  return { longest, wordMinimum };
}

/** Reserve an actual rotated rectangle in normal layout. The text remains
 * native selectable glyphs; it is never rasterized into a preview bitmap.
 * Called again after an intrinsic reading width is assigned during fitting. */
export function layoutTextRotations(root: HTMLElement, readingWidth?: number): void {
  for (const text of root.querySelectorAll<HTMLElement>('[data-text-rotation]')) {
    const degrees = textRotation(Number(text.dataset.textRotation)); if (!degrees) continue;
    let frame = text.parentElement!;
    if (!frame.classList.contains('ms-rotated-text-frame')) {
      frame = document.createElement('span'); frame.className = 'ms-rotated-text-frame';
      if (text.classList.contains('ms-node-title')) frame.classList.add('ms-rotated-title-frame');
      if (text.classList.contains('ms-after-media')) { text.classList.remove('ms-after-media'); frame.classList.add('ms-after-media'); }
      text.before(frame); frame.append(text);
    }
    const parent = frame.parentElement!, parentStyle = getComputedStyle(parent);
    frame.style.alignSelf = parentStyle.textAlign === 'left' ? 'flex-start' : parentStyle.textAlign === 'right' ? 'flex-end' : 'center';
    const sidePadding = (parseFloat(parentStyle.paddingLeft) || 0) + (parseFloat(parentStyle.paddingRight) || 0);
    const marker = text.classList.contains('ms-node-title')
      ? [...parent.children].filter(element => element !== frame).reduce((width, element) => width + (element as HTMLElement).offsetWidth, 0) : 0;
    const available = Math.max(1, (readingWidth ?? parent.clientWidth) - sidePadding - marker);
    // The non-DOM sizing model supplies the ordinary unrotated wrapping basis.
    // Its envelope can be wider than this basis for diagonally rotated text.
    const computed = getComputedStyle(text);
    if (rotationContext === undefined) rotationContext = document.createElement('canvas').getContext('2d');
    const context = rotationContext;
    let longest = available, wordMinimum = 1;
    if (context) {
      const wordLimit = Number(text.dataset.rotationWordLimit);
      if (text.classList.contains('ms-rich-text')) {
        const widths = formattedReadingWidths(text, context); longest = widths.longest;
        if (wordLimit > 0) wordMinimum = Math.min(wordLimit, widths.wordMinimum);
      } else {
        context.font = `${computed.fontStyle} ${computed.fontWeight} ${computed.fontSize} ${computed.fontFamily}`;
        const spacing = Number.parseFloat(computed.letterSpacing) || 0;
        longest = Math.max(1, ...text.textContent!.replace(/\r\n?/g, '\n').split('\n')
          .map(line => context.measureText(line).width + Math.max(0, line.length - 1) * spacing));
        if (wordLimit > 0) wordMinimum = Math.min(wordLimit, Math.max(1, ...(text.textContent!.match(/\S+/gu) ?? [])
          .map(word => context.measureText(word).width + Math.max(0, word.length - 1) * spacing)));
      }
    }
    // A quarter-turned paragraph can be hundreds of reading pixels long while
    // its finished envelope is only one line-height wide. Clamping that reading
    // width to the already-rotated frame would wrap every word again and shrink
    // the resulting composition into an illegible speck.
    const basis = Math.max(wordMinimum, Math.min(Number(text.dataset.rotationReadingWidth) || available, longest));
    Object.assign(text.style, { position: 'relative', left: '0', top: '0', transform: 'none', width: `${basis}px`,
      maxWidth: 'none', minHeight: '0', maxHeight: 'none', height: 'auto', flex: 'none', margin: '0',
      overflow: 'visible', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', display: 'block' });
    const height = Math.max(text.offsetHeight, text.scrollHeight, .001);
    const envelope = rotatedTextEnvelope(basis, height, degrees);
    frame.style.width = `${envelope.width}px`; frame.style.height = `${envelope.height}px`;
    Object.assign(text.style, { position: 'absolute', left: `${(envelope.width - basis) / 2}px`,
      top: `${(envelope.height - height) / 2}px`, height: `${height}px`, transform: `rotate(${degrees}deg)`, transformOrigin: '50% 50%' });
  }
}
