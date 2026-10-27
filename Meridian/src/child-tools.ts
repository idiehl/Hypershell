import type { AttachSide, MeridianNode, MeridianProject, NodeStyle } from './core/types.ts';
import { NODE_SHAPES, NODE_STYLE_PRESETS, STYLE_LIMITS } from './core/appearance.ts';
import { arrangeChildren, isCellChild, isOrderedContainer, orderedPattern } from './core/grid.ts';
import { revealChildren } from './core/reveal.ts';
import { navigationIndex } from './core/navigation.ts';

export interface ChildToolActions {
  edit: (change: (project: MeridianProject) => void) => boolean | void;
  fail: (error: unknown) => void;
}
const openTools = new Set<string>();
function el<K extends keyof HTMLElementTagNameMap>(tag: K, text?: string): HTMLElementTagNameMap[K] {
  const result = document.createElement(tag); if (text !== undefined) result.textContent = text; return result;
}
function field(label: string, input: HTMLElement): HTMLElement {
  const result = el('label'); result.className = 'field'; input.setAttribute('aria-label', label);
  const caption = el('span', label); caption.className = 'field-label'; result.append(caption, input); return result;
}
function select(options: ReadonlyArray<{ value: string; label: string }>, value: string): HTMLSelectElement {
  const result = el('select'); for (const option of options) result.append(new Option(option.label, option.value)); result.value = value; return result;
}
function number(min: number, max: number): HTMLInputElement {
  const input = el('input'); input.type = 'number'; input.min = String(min); input.max = String(max); input.step = 'any'; input.placeholder = 'Keep existing'; return input;
}
function group(title: string): HTMLFormElement {
  const form = el('form'); form.className = 'child-tools-group'; form.append(el('h3', title)); return form;
}
function apply(form: HTMLFormElement, label: string, action: () => void, actions: ChildToolActions): HTMLButtonElement {
  const submit = el('button', label); submit.type = 'submit'; submit.className = 'secondary full'; submit.setAttribute('aria-label', label);
  form.append(submit); form.onsubmit = event => { event.preventDefault(); if (!form.reportValidity()) return; try { action(); } catch (failure) { actions.fail(failure); } }; return submit;
}
function immediate(project: MeridianProject, id: string): MeridianNode[] {
  if (!project.nodes.some(node => node.id === id)) throw new Error('The parent node is no longer available.');
  return project.nodes.filter(node => node.parentId === id);
}
function rearrangeGrid(project: MeridianProject, id: string): void {
  const parent = project.nodes.find(node => node.id === id)!;
  if (isOrderedContainer(parent)||parent.grid && parent.childArrangement === 'grid') Object.assign(project, arrangeChildren(project, id, orderedPattern(parent)??'grid').project);
}

/** Explicit bulk edits affect immediate children only; every Apply is one normal undoable edit. */
export function childTools(project: MeridianProject, node: MeridianNode, actions: ChildToolActions): HTMLElement {
  const details = el('details'); details.className = 'advanced child-editing-tools'; details.open = openTools.has(node.id);
  details.ontoggle = () => { if (!details.isConnected) return; if (details.open) openTools.add(node.id); else openTools.delete(node.id); };
  details.append(el('summary', 'Child behavior & appearance'));
  const children = immediate(project, node.id), note = el('p', `${children.length} immediate ${children.length === 1 ? 'child' : 'children'}. Choose settings, then Apply. Descendants keep their own settings.`); note.className = 'muted'; details.append(note);
  if (!children.length) return details;
  const remember = () => { if (details.open) openTools.add(node.id); };

  const motionForm = group('Movement'), motion = select([
    { value: 'inherit', label: 'Inherit Work behavior' }, { value: 'sticky', label: 'Sticky' },
    { value: 'align', label: 'Align to edges' }, { value: 'free', label: 'Free' },
  ], 'inherit');
  motionForm.append(field('Child alignment', motion));
  apply(motionForm, 'Apply child alignment', () => { remember(); actions.edit(draft => {
    for (const child of immediate(draft, node.id)) {
      if (motion.value === 'inherit') delete child.snapping;
      else child.snapping = { stickyEdges: motion.value === 'sticky', smartAlign: motion.value === 'align', snapToGrid: false };
    }
  }); }, actions); details.append(motionForm);

  const placementForm = group('Disclosure placement'), placement = select([
    { value: 'preserve', label: 'Keep individual settings' }, { value: 'free', label: 'Keep authored positions' },
    { value: 'near', label: 'Find nearby space' }, ...(node.kind === 'canvas' ? [] : [{ value: 'edge', label: 'Attach to parent edge' }]),
  ], 'preserve');
  const side = select(['auto', 'top', 'right', 'bottom', 'left'].map(value => ({ value, label: value === 'auto' ? 'Any available edge' : value.charAt(0).toUpperCase() + value.slice(1) })), 'auto');
  const sideField = field('Child parent edge', side); sideField.hidden = true;
  placementForm.append(field('Child placement', placement), sideField);
  const placementApply = apply(placementForm, 'Apply child placement', () => { remember(); if (placement.value === 'preserve') return; actions.edit(draft => {
    for (const child of immediate(draft, node.id)) {
      child.placement = placement.value as 'free' | 'near' | 'edge';
      child.childPosition = 'manual';
      if (placement.value === 'edge') child.attachSide = side.value as AttachSide;
      else delete child.attachSide;
    }
    const parent = draft.nodes.find(parent => parent.id === node.id)!;
    if (!parent.collapsed && parent.kind !== 'canvas') Object.assign(draft, revealChildren(draft, node.id, true));
  }); }, actions);
  placementApply.disabled = true; placement.onchange = () => { sideField.hidden = placement.value !== 'edge'; placementApply.disabled = placement.value === 'preserve'; }; if(!isOrderedContainer(node))details.append(placementForm);

  const appearanceForm = group('Appearance'), preset = select([
    { value: 'preserve', label: 'Keep individual styles' }, ...NODE_STYLE_PRESETS.filter(preset => preset.id !== 'label').map(preset => ({ value: preset.id, label: preset.label })),
  ], 'preserve');
  const shape = select([{ value: 'preserve', label: 'Keep individual shapes' }, ...NODE_SHAPES], 'preserve');
  const titleSize = number(STYLE_LIMITS.titleSize.min, STYLE_LIMITS.titleSize.max), bodySize = number(STYLE_LIMITS.fontSize.min, STYLE_LIMITS.fontSize.max);
  appearanceForm.append(field('Child style preset', preset), field('Child shape', shape), field('Child title size', titleSize), field('Child body size', bodySize));
  const appearanceApply = apply(appearanceForm, 'Apply child appearance', () => { remember(); actions.edit(draft => {
    const chosen = NODE_STYLE_PRESETS.find(item => item.id === preset.value);
    for (const child of immediate(draft, node.id).filter(isCellChild)) {
      const style: NodeStyle = chosen ? structuredClone(chosen.style) : { ...child.style };
      if (shape.value !== 'preserve') style.shape = shape.value as NonNullable<NodeStyle['shape']>;
      if (titleSize.value.trim()) style.titleSize = Number(titleSize.value);
      if (bodySize.value.trim()) style.fontSize = Number(bodySize.value);
      if (Object.keys(style).length) child.style = style; else delete child.style;
    }
    rearrangeGrid(draft, node.id);
  }); }, actions);
  const appearanceChanged = () => { appearanceApply.disabled = preset.value === 'preserve' && shape.value === 'preserve' && !titleSize.value.trim() && !bodySize.value.trim(); };
  appearanceApply.disabled = true; preset.onchange = appearanceChanged; shape.onchange = appearanceChanged; titleSize.oninput = appearanceChanged; bodySize.oninput = appearanceChanged;
  details.append(appearanceForm);

  const sizingForm = group('Tile dimensions'), width = number(36, 100000), height = number(36, 100000);
  sizingForm.append(field('Child width', width), field('Child height', height));
  const sizingApply = apply(sizingForm, 'Apply child dimensions', () => { remember(); actions.edit(draft => {
    const parent = draft.nodes.find(parent => parent.id === node.id)!;
    if (isOrderedContainer(parent)) {
      if (!parent.grid) throw new Error('The ordered container has no cell settings.');
      if (width.value.trim()) parent.grid.cellWidth = Number(width.value);
      if (height.value.trim()) parent.grid.cellHeight = Number(height.value);
      rearrangeGrid(draft, node.id); return;
    }
    const index=navigationIndex(draft);
    for (const child of immediate(draft, node.id).filter(isCellChild)) { const current=index.bounds(child);child.width=width.value.trim()?Number(width.value):current.width;child.height=height.value.trim()?Number(height.value):current.height;child.sizeMode='fixed'; }
    rearrangeGrid(draft, node.id);
  }); }, actions);
  const sizingChanged = () => { sizingApply.disabled = !width.value.trim() && !height.value.trim(); }; sizingApply.disabled = true; width.oninput = sizingChanged; height.oninput = sizingChanged;
  if (node.grid && !isOrderedContainer(node)) {
    const fit = el('button', 'Fit child tiles to cells'); fit.type = 'button'; fit.className = 'quiet full'; fit.setAttribute('aria-label', 'Fit child tiles to cells');
    fit.onclick = () => { remember(); try { actions.edit(draft => {
      const parent = draft.nodes.find(parent => parent.id === node.id)!; if (!parent.grid) throw new Error('The parent no longer has a grid.');
      for (const child of immediate(draft, node.id).filter(isCellChild)) { child.width = parent.grid.cellWidth; child.height = parent.grid.cellHeight; child.sizeMode='fixed'; }
      Object.assign(draft, arrangeChildren(draft, node.id, 'grid').project);
    }); } catch (failure) { actions.fail(failure); } }; sizingForm.append(fit);
  }
  if (!isOrderedContainer(node) || ['spreadsheet', 'window'].includes(node.containerVariant ?? '')) details.append(sizingForm);
  return details;
}
