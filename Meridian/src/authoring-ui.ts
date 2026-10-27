import type { BackgroundStyle, CanvasStyle, LinkStyle, MeridianAsset, MeridianCanvas, MeridianLink, MeridianNode, NodeStyle, PortalStyle } from './core/types.ts';
import { CANVAS_STYLE_PRESETS, NODE_STYLE_PRESETS, NODE_SHAPES, PORTAL_STYLES, STYLE_LIMITS, linkLineStyle } from './core/appearance.ts';
import { backgroundPaint } from './backgrounds.ts';
import { activeNodeContent, effectiveContentType } from './core/content.ts';
import { createFontPicker } from './font-picker.ts';
import { DEFAULT_CHILD_INSET } from './core/spacing.ts';
import { PORTAL_SWATCHES } from './core/portal-color.ts';
const expanded = new Set<string>();

export interface BackgroundAppearanceContext {
  assets: readonly MeridianAsset[];
  upload: (file: File) => Promise<void>;
  onError?: (error: unknown) => void;
}

const backgroundPresets: ReadonlyArray<{ id: string; label: string; pattern: NonNullable<BackgroundStyle['backgroundPattern']>; scale?: number }> = [
  { id: 'plain', label: 'Plain', pattern: 'none' },
  { id: 'dots-fine', label: 'Dots · fine (16 px)', pattern: 'dots', scale: 16 },
  { id: 'dots-default', label: 'Dots · original (24 px)', pattern: 'dots', scale: 24 },
  { id: 'dots-medium', label: 'Dots · medium (32 px)', pattern: 'dots', scale: 32 },
  { id: 'dots-wide', label: 'Dots · wide (64 px)', pattern: 'dots', scale: 64 },
  { id: 'grid-fine', label: 'Square grid · fine (16 px)', pattern: 'grid', scale: 16 },
  { id: 'grid-medium', label: 'Square grid · medium (32 px)', pattern: 'grid', scale: 32 },
  { id: 'grid-wide', label: 'Square grid · wide (64 px)', pattern: 'grid', scale: 64 },
  { id: 'grid-large', label: 'Square grid · large (128 px)', pattern: 'grid', scale: 128 },
  { id: 'isometric', label: 'Isometric grid', pattern: 'isometric', scale: 48 },
  { id: 'linen', label: 'Linen', pattern: 'linen', scale: 48 },
  { id: 'contours', label: 'Contour map', pattern: 'contours', scale: 160 },
  { id: 'aurora', label: 'Aurora', pattern: 'aurora', scale: 256 },
  { id: 'stars', label: 'Star field', pattern: 'stars', scale: 128 },
];

function control<T extends object>(title: string, value: T, onChange: (style: T | undefined) => void, identity: string) {
  const panel = document.createElement('details'); panel.className = 'advanced appearance-panel';
  panel.open = expanded.has(identity); panel.ontoggle = () => { if(panel.isConnected){if(panel.open)expanded.add(identity);else expanded.delete(identity);} };
  const update = (style:T|undefined) => { if(panel.open)expanded.add(identity);else expanded.delete(identity);onChange(style); };
  const summary = document.createElement('summary'); summary.textContent = title; panel.append(summary);
  const field = (label: string, input: HTMLElement) => {
    input.setAttribute('aria-label', label);
    const row = document.createElement('label'); row.className = 'field';
    const text = document.createElement('span'); text.className = 'field-label'; text.textContent = label;
    row.append(text, input); panel.append(row);
  };
  const change = (key: keyof T, next: unknown) => update({ ...value, [key]: next });
  const choose = (label: string, key: keyof T, fallback: string, entries: [string, string][]) => {
    const input = document.createElement('select');
    for (const [key, text] of entries) { const option = document.createElement('option'); option.value = key; option.textContent = text; input.append(option); }
    input.value = String(value[key] ?? fallback); input.onchange = () => change(key, input.value); field(label, input);
  };
  const number = (label: string, key: keyof T, fallback: number, min: number, max: number) => {
    const input = document.createElement('input'); input.type = 'number'; input.min = String(min); input.max = String(max); input.step='any'; input.value = String(value[key] ?? fallback);
    input.onchange = () => { if (input.reportValidity() && input.value !== '') change(key, Number(input.value)); }; field(label, input);
  };
  const color = (label: string, key: keyof T, fallback: string) => {
    const input = document.createElement('input'); input.type = 'color'; input.value = String(value[key] ?? fallback);
    input.onchange = () => change(key, input.value); field(label, input);
  };
  const checkbox = (label: string, key: keyof T, fallback: boolean) => {
    const input = document.createElement('input'); input.type = 'checkbox'; input.checked = Boolean(value[key] ?? fallback);
    input.onchange = () => change(key, input.checked); field(label, input);
  };
  const presets = (items: ReadonlyArray<{ id: string; label: string; style: T }>) => {
    const select = document.createElement('select');
    select.append(new Option('Choose a preset…', ''));
    for (const preset of items) select.append(new Option(preset.label, preset.id));
    select.onchange = () => { const preset = items.find(item => item.id === select.value); if (preset) update(structuredClone(preset.style)); };
    field('Style preset', select);
  };
  const reset = () => {
    const button = document.createElement('button'); button.type = 'button'; button.className = 'quiet full'; button.textContent = 'Reset appearance'; button.onclick = () => {if(Object.keys(value).length)update(undefined);}; panel.append(button);
  };
  return { panel, choose, number, color, checkbox, presets, reset, field, update };
}

function backgroundControls<T extends BackgroundStyle>(ui: ReturnType<typeof control<T>>, value: T, canvas: boolean, context?: BackgroundAppearanceContext) {
  const pattern = canvas && (value as CanvasStyle).gridVisible === false && !value.backgroundAssetId ? 'none' : value.backgroundPattern ?? (canvas ? 'dots' : 'none');
  const scale = value.backgroundScale ?? 24;
  const preset = document.createElement('select');
  for (const item of backgroundPresets) preset.append(new Option(item.label, item.id));
  if (value.backgroundAssetId) preset.append(new Option('Uploaded image', 'image'));
  const matching = backgroundPresets.find(item => item.pattern === pattern && (item.scale === undefined || item.scale === scale));
  if (!matching && !value.backgroundAssetId) preset.append(new Option(`${pattern.charAt(0).toUpperCase() + pattern.slice(1)} · custom size`, 'custom'));
  preset.value = value.backgroundAssetId ? 'image' : matching?.id ?? 'custom';
  preset.onchange = () => {
    const item = backgroundPresets.find(item => item.id === preset.value); if (!item) return;
    const next = { ...value, backgroundPattern: item.pattern };
    delete next.backgroundAssetId;
    if (item.scale !== undefined) next.backgroundScale = item.scale;
    if (canvas) (next as CanvasStyle).gridVisible = true;
    ui.update(next);
  };
  ui.field('Background preset', preset);
  const preview = document.createElement('div'); preview.className = 'background-preview'; preview.setAttribute('aria-label', 'Background preview');
  preview.style.backgroundColor = (value as NodeStyle).fill ?? (value as CanvasStyle).background ?? '#142132';
  const paint = backgroundPaint({ ...value, backgroundPattern: pattern, backgroundScale: Math.max(8, Math.min(80, scale / 2)) }, context?.assets ?? [], canvas ? 'dots' : 'none', value.backgroundTint ?? (value as CanvasStyle).grid ?? '#4b6575');
  const layer = document.createElement('span'); layer.className = 'background-preview-image';
  Object.assign(layer.style, { backgroundImage: paint.image, backgroundSize: paint.size, backgroundPosition: paint.position, backgroundRepeat: paint.repeat, opacity: String(paint.opacity) });
  const sample = document.createElement('span'); sample.className = 'background-preview-content'; sample.textContent = canvas ? 'Work' : 'Aa';
  sample.style.color = (value as NodeStyle).textColor ?? '#dce5ed'; preview.append(layer, sample); ui.panel.append(preview);
  if (!canvas && ((value as NodeStyle).shape === 'none' || (value as NodeStyle).variant === 'label')) {
    const note = document.createElement('p'); note.className = 'muted background-note'; note.textContent = 'Choose a backdrop shape to display this background on the node.'; ui.panel.append(note);
  }
  const asset = context?.assets.find(item => item.id === value.backgroundAssetId);
  if (asset) {
    const name = document.createElement('p'); name.className = 'background-image-name'; name.textContent = asset.name; ui.panel.append(name);
    ui.choose('Background image fit', 'backgroundFit', 'cover', [['cover', 'Fill and crop'], ['contain', 'Show whole image'], ['tile', 'Repeat image']]);
    const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'quiet full background-remove'; remove.textContent = 'Remove background image';
    remove.onclick = () => { const next = { ...value }; delete next.backgroundAssetId; ui.update(next); }; ui.panel.append(remove);
  }
  if ((pattern !== 'none' && !value.backgroundAssetId) || (value.backgroundAssetId && value.backgroundFit === 'tile')) ui.number('Background size', 'backgroundScale', 24, 8, 512);
  if (pattern !== 'none' && !value.backgroundAssetId) ui.color('Pattern color', 'backgroundTint', (value as CanvasStyle).grid ?? '#4b6575');
  if (pattern !== 'none' || value.backgroundAssetId) ui.number('Background opacity', 'backgroundOpacity', 1, 0, 1);
  if (context) {
    const file = document.createElement('input'); file.type = 'file'; file.accept = '.png,.jpg,.jpeg,.webp,.gif,.avif,.bmp,image/png,image/jpeg,image/webp,image/gif,image/avif,image/bmp';
    const error = document.createElement('p'); error.className = 'background-upload-error'; error.setAttribute('role', 'alert');
    file.onchange = async () => {
      const image = file.files?.[0]; if (!image) return; file.disabled = true; error.textContent = 'Adding image…'; error.classList.add('is-pending');
      try { await context.upload(image); }
      catch (failure) { error.textContent = failure instanceof Error ? failure.message : String(failure); error.classList.remove('is-pending'); if (!ui.panel.isConnected) context.onError?.(failure); }
      finally { file.disabled = false; file.value = ''; }
    };
    ui.field('Upload background image', file); ui.panel.append(error);
    const hint = document.createElement('p'); hint.className = 'muted background-note'; hint.textContent = 'PNG, JPEG, WebP, GIF, AVIF or BMP · up to 16 MiB. Images are stored with your work.'; ui.panel.append(hint);
  }
}

function portalColorControl<T extends { portalColor?: string; portalStyle?: PortalStyle }>(ui: ReturnType<typeof control<T>>, value: T, inherited?: Pick<CanvasStyle, 'portalColor' | 'portalStyle'>, canvas = false) {
  const row = document.createElement('div'); row.className = 'pair portal-color-control';
  const picker = document.createElement('input'); picker.type = 'color'; picker.setAttribute('aria-label', 'Work portal color');
  picker.value = value.portalColor ?? inherited?.portalColor ?? PORTAL_SWATCHES[value.portalStyle ?? inherited?.portalStyle ?? 'doorway'];
  picker.onchange = () => ui.update({ ...value, portalColor: picker.value });
  const reset = document.createElement('button'); reset.type = 'button'; reset.className = 'quiet';
  reset.textContent = canvas ? 'Use original colors' : 'Use Work color'; reset.disabled = value.portalColor === undefined;
  reset.onclick = () => { const next = { ...value }; delete next.portalColor; ui.update(Object.keys(next).length ? next : undefined); };
  row.append(picker, reset); ui.field('Work portal color', row);
}

export function nodeAppearance(node: MeridianNode, update: (style: NodeStyle | undefined) => void, supportsLabel = effectiveContentType(node) === 'text', backgrounds?: BackgroundAppearanceContext, supportsCanvasPortal = node.kind === 'canvas-link', portalDefaults?: Pick<CanvasStyle, 'portalColor' | 'portalStyle'>) {
  const ui = control<NodeStyle>('Appearance', node.style ?? {}, update, node.id);
  if (node.kind === 'junction') {
    const appearance = document.createElement('select');
    appearance.append(new Option('Dot', 'dot'), new Option('Line', 'line'));
    const current = node.style?.shape ?? 'circle';
    if (!['circle', 'rectangle'].includes(current)) appearance.append(new Option('Custom shape', 'custom'));
    appearance.value = current === 'circle' ? 'dot' : current === 'rectangle' ? 'line' : 'custom';
    appearance.onchange = () => {
      if (appearance.value !== 'custom') ui.update({ ...node.style, shape: appearance.value === 'dot' ? 'circle' : 'rectangle', variant: 'card' });
    };
    ui.field('Utility appearance', appearance);
    ui.choose('Shape', 'shape', 'circle', NODE_SHAPES.filter(shape => shape.value !== 'none').map(shape => [shape.value, shape.label]));
    ui.color('Fill color', 'fill', '#78ddc1');
    ui.color('Border color', 'border', '#78ddc1');
    ui.number('Border width', 'borderWidth', 1, 0, 8);
    ui.choose('Border style', 'borderStyle', 'solid', [['solid', 'Solid'], ['dashed', 'Dashed'], ['dotted', 'Dotted'], ['double', 'Double']]);
    ui.number('Corner radius', 'radius', 0, 0, 48);
    ui.reset(); return ui.panel;
  }
  ui.presets(NODE_STYLE_PRESETS.filter(preset=>supportsLabel||preset.style.variant!=='label'));
  if (supportsCanvasPortal) {
    const portal = document.createElement('select');
    portal.append(new Option('Use Work default', ''));
    for (const style of PORTAL_STYLES) portal.append(new Option(style.label, style.value));
    portal.value = node.style?.portalStyle ?? '';
    portal.onchange = () => {
      const next = { ...node.style };
      const style = PORTAL_STYLES.find(style => style.value === portal.value);
      if (style) next.portalStyle = style.value; else delete next.portalStyle;
      ui.update(Object.keys(next).length ? next : undefined);
    };
    ui.field('Work portal style', portal);
    portalColorControl(ui, node.style ?? {}, portalDefaults);
    const hint = document.createElement('p'); hint.className = 'muted'; hint.textContent = 'Choose a style for this work button, or follow the work default.'; ui.panel.append(hint);
  }
  ui.choose('Shape', 'shape', 'rectangle', NODE_SHAPES.map(shape=>[shape.value,shape.label]));
  if(supportsLabel)ui.choose('Presentation', 'variant', 'card', [['card', 'Card'], ['label', 'Unframed text']]);
  const fonts = createFontPicker(node.style?.fontFamily, value => ui.update({ ...node.style, fontFamily: value }));
  ui.field('Font', fonts.button); ui.panel.append(fonts.popup);
  ui.choose('Content sizing', 'contentSizing', 'fit', [['fit', 'Fit to node'], ['fixed', 'Fixed typography']]);
  ui.number('Title size', 'titleSize', 13, 10, 96); ui.number('Body size', 'fontSize', 12, 10, 72);
  ui.choose('Text alignment', 'textAlign', 'center', [['center', 'Center'], ['left', 'Left'], ['right', 'Right']]);
  for (const [key, title] of [['titleRotation', 'Title'], ['textRotation', 'Text']] as const) {
    const field = document.createElement('div'); field.className = 'field';
    const label = document.createElement('label'); label.className = 'field-label'; label.textContent = `${title} rotation (°)`;
    const row = document.createElement('div'); row.className = 'text-rotation-row';
    const angle = document.createElement('input'); angle.type = 'number'; angle.min = '-180'; angle.max = '180'; angle.step = 'any';
    angle.value = String(node.style?.[key] ?? 0); angle.id = `text-rotation-${key}-${crypto.randomUUID()}`; label.htmlFor = angle.id;
    angle.setAttribute('aria-label', label.textContent); angle.onchange = () => { if (angle.value !== '' && angle.reportValidity()) ui.update({ ...node.style, [key]: Number(angle.value) }); };
    row.append(angle);
    for (const [symbol, action, amount] of [['↶', 'left', -90], ['↷', 'right', 90], ['↺', 'reset', 0]] as const) {
      const button = document.createElement('button'); button.type = 'button'; button.textContent = symbol;
      button.title = action === 'reset' ? `Reset ${title.toLowerCase()} rotation` : `Rotate ${title.toLowerCase()} ${action}`; button.setAttribute('aria-label', button.title);
      button.onclick = () => {
        const style = { ...node.style };
        if (action === 'reset') delete style[key];
        else { let next = ((style[key] ?? 0) + amount) % 360; if (next > 180) next -= 360; if (next < -180) next += 360; style[key] = next; }
        ui.update(style);
      }; row.append(button);
    }
    field.append(label, row); ui.panel.append(field);
  }
  const rotationHint = document.createElement('p'); rotationHint.className = 'text-rotation-hint'; rotationHint.textContent = 'Rotates text only. Backdrops and pictures stay upright.'; ui.panel.append(rotationHint);
  const container=node.kind==='container'||node.kind==='canvas';
  ui.color('Card fill', 'fill', container?'#17283e':node.kind==='reference'?'#172538':node.kind==='joint'?'#172c3e':'#142132');
  ui.choose('Background display', 'backgroundDisplay', 'always', [['always', 'Always visible'], ['hover', 'Show on Hover']]);
  const backgroundHint = document.createElement('p'); backgroundHint.className = 'muted'; backgroundHint.textContent = 'Only the background changes. Text and content remain visible.'; ui.panel.append(backgroundHint);
  backgroundControls(ui, node.style ?? {}, false, backgrounds);
  ui.color('Card border', 'border', container?'#314c6c':node.kind==='joint'?'#3c6874':'#34475b');
  ui.color('Text color', 'textColor', '#e5edf6'); ui.color('Accent color', 'accent', '#78ddc1');
  ui.number('Border width', 'borderWidth', 1, 0, 8); ui.number('Corner radius', 'radius', container?15:11, 0, 48);
  ui.choose('Border style', 'borderStyle', 'solid', [['solid','Solid'],['dashed','Dashed'],['dotted','Dotted'],['double','Double']]);
  ui.number('Inner padding', 'padding', 5, 0, 1000);
  ui.number('Content spacing', 'contentGap', 5, 0, 1000);
  ui.number('Text and media spacing', 'textMediaGap', 5, 0, 1000);
  if(['container','spreadsheet','module'].includes(node.kind)) {
    ui.number('Child inset', 'childPadding', DEFAULT_CHILD_INSET, 0, 1000);
    const resetInset = document.createElement('button'); resetInset.type = 'button'; resetInset.className = 'quiet full';
    resetInset.textContent = `Use default child inset (${DEFAULT_CHILD_INSET} px)`; resetInset.disabled = node.style?.childPadding === undefined;
    resetInset.onclick = () => { const next = { ...node.style }; delete next.childPadding; ui.update(Object.keys(next).length ? next : undefined); };
    ui.panel.append(resetInset);
  }
  const activeAsset=activeNodeContent(node).assetId,asset=backgrounds?.assets.find(asset=>asset.id===activeAsset);
  if (node.kind === 'reference' || asset&&/^image\/(png|jpeg|gif|webp|avif|bmp)$/i.test(asset.mime) || !backgrounds&&activeAsset&&node.kind==='image') ui.choose('Image fit', 'imageFit', 'contain', [['contain', 'Show whole image'], ['cover', 'Fill and crop']]);
  ui.reset(); return ui.panel;
}

export function canvasAppearance(canvas: MeridianCanvas, update: (style: CanvasStyle | undefined) => void, backgrounds?: BackgroundAppearanceContext) {
  const ui = control<CanvasStyle>('Work appearance', canvas.style ?? {}, update, canvas.id);
  ui.presets(CANVAS_STYLE_PRESETS);
  ui.choose('Work portal style', 'portalStyle', 'doorway', PORTAL_STYLES.map(style => [style.value, style.label]));
  portalColorControl(ui, canvas.style ?? {}, undefined, true);
  const hint = document.createElement('p'); hint.className = 'muted'; hint.textContent = 'The default for work buttons in this workspace. Individual buttons can use their own style.'; ui.panel.append(hint);
  ui.color('Work background', 'background', '#0b1420');
  backgroundControls(ui, canvas.style ?? {}, true, backgrounds); ui.reset(); return ui.panel;
}

export function linkAppearance(link: MeridianLink, update: (style: LinkStyle | undefined) => void) {
  const ui = control<LinkStyle>('Connection appearance', link.style ?? {}, update, link.id);
  ui.color('Line color', 'color', link.kind === 'visual' ? '#7793ad' : link.kind === 'numeric' ? '#efc673' : '#78ddc1');
  ui.number('Line width', 'width', 2, STYLE_LIMITS.linkWidth.min, STYLE_LIMITS.linkWidth.max);
  ui.choose('Line style', 'lineStyle', linkLineStyle(link.style, link.kind === 'visual' ? 'dashed' : 'solid'), [['solid', 'Solid'], ['dashed', 'Dashed'], ['dotted', 'Dotted']]);
  ui.reset(); return ui.panel;
}
