import type { AttachSide, LinkDirection, LinkKind, LinkRelation, MeridianAsset, MeridianNode, MeridianProject, NodeShape, NodeStyle, Operation, TextRun, UtilityVariant, ValueType } from './core/types.ts';
import { NODE_SHAPES, NODE_STYLE_PRESETS, STYLE_LIMITS, DEFAULT_NODE_FONT_SIZE, DEFAULT_NODE_TITLE_SIZE } from './core/appearance.ts';
import { fontFamily as fontDefinition } from './core/fonts.ts';
import { createFontPicker } from './font-picker.ts';
import { createRichTextEditor, openRichTextDialog, type RichTextEditor, type RichTextValue } from './rich-text-editor.ts';
import { rebaseTextRuns } from './core/rich-text.ts';
import { renderRichText } from './rich-text-render.ts';
import { nodeTag, NODE_TAG_MAX_LENGTH } from './core/node-tag.ts';
import { workLibraryDisplayText } from './core/work-terminology.ts';
import { navigationIndex } from './core/navigation.ts';
import { isEnclosingContainer, isOrderedContainer } from './core/cells.ts';
import { isGallery } from './core/gallery.ts';
import { DEFAULT_CHILD_INSET } from './core/spacing.ts';
import { CONTAINER_DESCRIPTIONS, CONTAINER_CHOICES } from './container-descriptions.ts';
import { availableLinkPorts, createLinkFromSpec, createNodeFromSpec, creationOperations, creationPermissions, creationTypes } from './core/creation.ts';
import type { ContainerVariant, CreationType, LinkCreationSpec, NodeBehavior, NodeCreationSpec } from './core/creation.ts';

type Choice = { value: string; label: string };
const displayNodeTag = (project: MeridianProject, node: MeridianNode) => workLibraryDisplayText(project.id, node.id, nodeTag(node));
const titleCase = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);
function el<K extends keyof HTMLElementTagNameMap>(tag: K, className = '', text?: string): HTMLElementTagNameMap[K] {
  const element = document.createElement(tag); element.className = className; if (text !== undefined) element.textContent = text; return element;
}
function field(label: string, control: HTMLElement, hint?: string): HTMLLabelElement {
  control.setAttribute('aria-label', label);
  const result = el('label', 'creation-field'); result.append(el('span', 'creation-label', label), control);
  if (hint) { const description = el('small', 'creation-hint', hint); description.id = `creation-hint-${crypto.randomUUID()}`; control.setAttribute('aria-describedby', description.id); result.append(description); } return result;
}
function select(choices: readonly Choice[], selected = ''): HTMLSelectElement {
  const result = el('select'); for (const choice of choices) { const option = el('option', '', choice.label); option.value = choice.value; result.append(option); } result.value = selected; return result;
}
function textInput(value = '', type = 'text'): HTMLInputElement { const result = el('input'); result.type = type; result.value = value; return result; }
function numberInput(value: number, min: number, max: number, step = 1): HTMLInputElement {
  const result = textInput(String(value), 'number'); result.min = String(min); result.max = String(max); result.step = String(step); result.required = true; return result;
}
function textarea(value = '', rows = 5): HTMLTextAreaElement { const result = el('textarea'); result.rows = rows; result.value = value; return result; }
function button(label: string, className = 'secondary'): HTMLButtonElement { const result = el('button', className, label); result.type = 'button'; return result; }
function pair(...children: HTMLElement[]): HTMLElement { const result = el('div', 'creation-pair'); result.append(...children); return result; }
function block(title: string): { root: HTMLFieldSetElement; body: HTMLDivElement } {
  const root = el('fieldset', 'creation-block'); root.append(el('legend', '', title)); const body = el('div', 'creation-block-body'); root.append(body); return { root, body };
}
function shell(title: string, subtitle: string) {
  const restore = document.activeElement instanceof HTMLElement && document.activeElement !== document.body ? document.activeElement : document.querySelector<HTMLElement>('#scene .ms-viewport');
  const dialog = el('dialog', 'modal creation-dialog'); dialog.setAttribute('aria-label', title);
  const header = el('header', 'modal-header'); const heading = el('div'), description = el('p', 'creation-subtitle', subtitle); description.id = `creation-description-${crypto.randomUUID()}`; dialog.setAttribute('aria-describedby', description.id); heading.append(el('h2', '', title), description);
  const close = button('×', 'icon-button'); close.setAttribute('aria-label', 'Close dialog'); close.onclick = () => dialog.close(); header.append(heading, close);
  const form = el('form', 'creation-form'); const body = el('div', 'creation-body'), footer = el('footer', 'modal-footer'); const error = el('p', 'creation-error'); error.setAttribute('role', 'alert');
  const cancel = button('Cancel'); cancel.onclick = () => dialog.close(); const submit = button(title === 'New Link' ? 'Create link' : 'Create node', 'primary'); submit.type = 'submit';
  footer.append(error, cancel, submit); form.append(body, footer); dialog.append(header, form); document.body.append(dialog);
  dialog.addEventListener('keydown', event => {
    if (event.key !== 'Tab' || event.ctrlKey || event.metaKey || event.altKey) return;
    const controls = [...dialog.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href], [tabindex]:not([tabindex="-1"])')].filter(control => control.getClientRects().length > 0 && !control.closest('[hidden]'));
    if (!controls.length) { event.preventDefault(); dialog.focus(); return; }
    event.preventDefault();
    const at = controls.indexOf(document.activeElement as HTMLElement);
    controls[at < 0 ? event.shiftKey ? controls.length - 1 : 0 : (at + (event.shiftKey ? -1 : 1) + controls.length) % controls.length]!.focus();
  });
  dialog.addEventListener('close', () => { dialog.remove(); if (restore?.isConnected) restore.focus({ preventScroll: true }); });
  return { dialog, form, body, error, submit };
}
export interface NodeDialogOptions {
  canvasId: string; parentId: string; x: number; y: number; initialType?: CreationType;
  initialUtilityVariant?: UtilityVariant;
  /** Inserting on a link creates a route utility, rather than an unrelated tile. */
  utilityOnly?: boolean;
}

/** Returns inert data. No canvas or storage changes occur before the caller commits. */
export async function createNodeDialog(project: MeridianProject, options: NodeDialogOptions): Promise<NodeCreationSpec | null> {
  return new Promise(resolve => {
    const parent = project.nodes.find(node => node.id === options.parentId), permitted = creationTypes(project, options.parentId).filter(type => type.value !== 'module'), allowed = options.utilityOnly ? permitted.filter(type => type.value === 'utility') : permitted;
    const m = shell('New Node', options.utilityOnly ? 'Insert a utility node on this link.' : parent?.kind === 'canvas' ? 'Create at the marked position on this work.' : `Create a child of ${parent ? displayNodeTag(project, parent) : 'the selected node'}.`);
    let result: NodeCreationSpec | null = null, pending = false;
    const layout = el('div', 'creation-layout'), fields = el('div', 'creation-fields'), previewPanel = el('aside', 'creation-preview-panel');
    const typeSelect = select(allowed.map(type => ({ value: type.value, label: type.label })), allowed.some(type => type.value === options.initialType) ? options.initialType : allowed[0]?.value ?? '');
    const typeHint = el('p', 'creation-type-hint'); typeHint.id = `creation-type-description-${crypto.randomUUID()}`; typeSelect.setAttribute('aria-describedby', typeHint.id); fields.append(field('Type', typeSelect), typeHint);
    const tag = textInput(''); tag.required = !options.utilityOnly; tag.maxLength = NODE_TAG_MAX_LENGTH; tag.placeholder = 'Name this node for navigation and search';
    let tagEdited = false; tag.addEventListener('input', () => { tagEdited = true; });
    fields.append(field('Tag', tag, 'The node’s name in navigation and search. It is not displayed inside the node.'));
    const title = textInput(''); title.maxLength = 240; title.placeholder = 'Optional text displayed on the Work';
    let titleRuns:TextRun[]=[],previousTitle='';
    const formatTitle=button('Format title','quiet');formatTitle.setAttribute('aria-label','Format title');formatTitle.onclick=()=>openRichTextDialog({label:'Title text',title:true,text:title.value,runs:titleRuns,maxLength:240,base:{fontFamily:fontFamily.value,titleSize:Number(titleSize.value),textColor:color.value},onChange:value=>{title.value=previousTitle=value.text;titleRuns=value.runs;previewUpdate();}},'Format title');
    const titleField = field('Title', title); fields.append(titleField,formatTitle); const details = el('div', 'creation-type-options'); fields.append(details);
    const appearance = block('Appearance'); const preset = select(NODE_STYLE_PRESETS.map(item => ({ value: item.id, label: item.label })), 'default');
    const shape = select(NODE_SHAPES, 'rectangle');
    const width = numberInput(280, 1, 1000000), height = numberInput(180, 1, 1000000);
    const sizing=select([{value:'auto',label:'Automatic · fit contents'},{value:'fixed',label:'Fixed dimensions'}],'auto');
    const dimensions=pair(field('Width',width),field('Height',height));
    const syncSizing=()=>{dimensions.hidden=sizing.value==='auto';width.disabled=height.disabled=sizing.value==='auto';};sizing.onchange=syncSizing;syncSizing();
    const fontFamily = createFontPicker('sans', () => previewUpdate());
    const fontSize = numberInput(DEFAULT_NODE_FONT_SIZE, 10, 72), titleSize = numberInput(DEFAULT_NODE_TITLE_SIZE, 10, 96);
    const fill = textInput('#1c2935', 'color'), border = textInput('#58706b', 'color'), color = textInput('#e1ebe9', 'color');
    const colors = el('div', 'creation-colors'); colors.append(field('Fill', fill), field('Border', border), field('Text color', color));
    const borderWidth=numberInput(1,0,8,.5),borderStyle=select([{value:'solid',label:'Solid'},{value:'dashed',label:'Dashed'},{value:'dotted',label:'Dotted'},{value:'double',label:'Double'}],'solid');
    const padding=numberInput(5,0,1000),contentGap=numberInput(5,0,1000),textMediaGap=numberInput(5,0,1000),childPadding=numberInput(DEFAULT_CHILD_INSET,0,1000);
    appearance.body.append(pair(field('Style', preset), field('Shape', shape)),field('Sizing',sizing,'Fit the visible contents automatically, or choose a deliberately sized surface.'),dimensions, colors,
      field('Font', fontFamily.button), fontFamily.popup, pair(field('Content font size', fontSize), field('Title font size', titleSize)),
      pair(field('Border width',borderWidth),field('Border style',borderStyle)),pair(field('Inner padding',padding),field('Content spacing',contentGap)),pair(field('Text and media spacing',textMediaGap),field('Child inset',childPadding)));
    const movement = block('Behavior'); const behavior = select([{ value: 'inherit', label: 'Work default' }, { value: 'free', label: 'Free' }, { value: 'align', label: 'Align to edges' }, { value: 'sticky', label: 'Sticky' }], 'inherit');
    const side = select(['auto', 'top', 'right', 'bottom', 'left'].map(value => ({ value, label: value === 'auto' ? 'Nearest available edge' : titleCase(value) })), 'auto');
    const sideField = field('Parent edge', side); const movementHint = el('p', 'creation-hint'); movementHint.id = `creation-behavior-description-${crypto.randomUUID()}`; behavior.setAttribute('aria-describedby', movementHint.id); movement.body.append(field('Behavior', behavior), sideField, movementHint); fields.append(appearance.root, movement.root);
    const preview = el('div', 'creation-preview-tile'), previewTitle = el('strong', 'creation-preview-title'), previewText = el('p', 'creation-preview-text'); preview.append(previewTitle, previewText);
    previewPanel.append(el('span', 'creation-preview-label', 'PREVIEW'), preview, el('p', 'creation-hint', 'Appearance at a reduced scale. Dimensions are measured in work units.'));
    layout.append(fields, previewPanel); m.body.append(layout);
    let variant: HTMLSelectElement | undefined, bodyText: RichTextEditor | undefined, file: HTMLInputElement | undefined, contentType: HTMLSelectElement | undefined, hyperlinkUrl: HTMLInputElement | undefined, hyperlinkLabel: HTMLInputElement | undefined;
    let columns: HTMLInputElement | undefined, rows: HTMLInputElement | undefined, cellWidth: HTMLInputElement | undefined, cellHeight: HTMLInputElement | undefined, gap: HTMLInputElement | undefined, csv: HTMLTextAreaElement | undefined;
    let fillOrder:HTMLSelectElement|undefined,windowColumns:HTMLInputElement|undefined,windowRows:HTMLInputElement|undefined,scrollDirection:HTMLSelectElement|undefined;
    let operation: HTMLSelectElement | undefined, valueType: HTMLSelectElement | undefined, value: HTMLInputElement | HTMLSelectElement | undefined, closed: HTMLSelectElement | undefined;
    let target: HTMLSelectElement | undefined, targetCell: HTMLInputElement | undefined, canvasTarget: HTMLSelectElement | undefined, imageFit: HTMLSelectElement | undefined;
    let utilityVariant: HTMLSelectElement | undefined, utilityAppearance: HTMLSelectElement | undefined;
    let previousTile: { width: string; height: string; sizing: string; shape: string; fill: string; border: string; padding: string; behavior: string } | undefined;
    const bodyMemory = new Map<string, RichTextValue>();
    function previewUpdate() {
      const utility = typeSelect.value === 'utility';
      preview.dataset.shape = shape.value; preview.style.setProperty('--preview-fill', fill.value); preview.style.setProperty('--preview-border', border.value); preview.style.color = color.value;
      previewTitle.hidden = utility || !title.value.trim(); previewText.hidden = utility;
      preview.style.width = utility ? `${Math.max(16, Math.min(160, Number(width.value) * 2))}px` : '';
      preview.style.height = utility ? `${Math.max(6, Math.min(160, Number(height.value) * 2))}px` : '';
      preview.style.minHeight = utility ? '0' : ''; preview.style.padding = utility ? '0' : ''; preview.style.justifySelf = utility ? 'center' : '';
      if(previousTitle!==title.value){titleRuns=rebaseTextRuns(previousTitle,title.value,titleRuns);previousTitle=title.value;}
      renderRichText(previewTitle,title.value,titleRuns);
      const bodyValue=bodyText?.getValue();const previewBody=bodyValue?.text.slice(0,160) ?? ''; previewText.hidden ||= !previewBody.trim();
      renderRichText(previewText,previewBody,bodyValue?.runs.filter(run=>run.start<160).map(run=>({...run,end:Math.min(160,run.end)})));
      if(bodyText&&bodyValue)bodyText.sync(bodyValue.text,bodyValue.runs,{fontFamily:fontFamily.value,fontSize:Number(fontSize.value),textColor:color.value});
      const ratio = Math.max(.5, Math.min(1.65, Number(width.value) / Number(height.value))); preview.style.aspectRatio = String(ratio);
      preview.style.fontFamily = fontDefinition(fontFamily.value).css;
      preview.style.textAlign = 'center'; preview.style.justifyContent = 'center';
      previewTitle.style.fontSize = `${Math.max(12, Math.min(28, Number(titleSize.value) * .75))}px`; previewText.style.fontSize = `${Math.max(10, Math.min(18, Number(fontSize.value) * .8))}px`;
    }
    function syncBehavior() {
      const enclosed=!!parent&&isEnclosingContainer(parent),ordered=!!parent&&isOrderedContainer(parent),gallery=isGallery(parent),canvasParent=parent?.kind==='canvas';
      sideField.hidden = enclosed||ordered||gallery||canvasParent||behavior.value === 'free' || behavior.value === 'inherit';
      movementHint.textContent=gallery?'The parent’s Gallery displays its child objects one at a time.':ordered?'The parent’s ordered cells control this tile’s position and dimensions.':enclosed?'Place this child inside the parent enclosure. Its alignment behavior applies when moving among neighboring children.':behavior.value === 'inherit' ? 'Use this work’s alignment behavior. Sticky edges are the initial work default.' : behavior.value === 'free' ? canvasParent?'Keep the marked position and move independently.':'Use automatic Freeform placement and move independently.' : canvasParent?'Align with neighboring tiles on this work.':behavior.value === 'align' ? 'Place beside the parent and align with nearby edges when moved.' : 'Attach beside the parent and keep edges together when moved.';
    }
    function typeOptions() {
      const wasUtility = details.dataset.type === 'utility';
      if (bodyText) {bodyText.flush(true);bodyMemory.set(details.dataset.type ?? '', bodyText.getValue());bodyText.dispose();}
      variant = undefined; bodyText = undefined; file = undefined; contentType = undefined; hyperlinkUrl = undefined; hyperlinkLabel = undefined; columns = undefined; rows = undefined; cellWidth = undefined; cellHeight = undefined; gap = undefined; csv = undefined;
      fillOrder=undefined;windowColumns=undefined;windowRows=undefined;scrollDirection=undefined;
      operation = undefined; valueType = undefined; value = undefined; closed = undefined; target = undefined; targetCell = undefined; canvasTarget = undefined; imageFit = undefined;
      utilityVariant = undefined; utilityAppearance = undefined;
      details.replaceChildren(); details.dataset.type = typeSelect.value; const type = typeSelect.value as CreationType;
      const utility = type === 'utility';
      if (utility && !wasUtility) {
        previousTile = { width: width.value, height: height.value, sizing: sizing.value, shape: shape.value, fill: fill.value, border: border.value, padding: padding.value, behavior: behavior.value };
        width.value = height.value = '12'; sizing.value = 'fixed'; shape.value = 'circle'; fill.value = border.value = '#78ddc1'; padding.value = '0'; behavior.value = 'free';
      } else if (!utility && wasUtility && previousTile) {
        for (const [key, control] of Object.entries({ width, height, sizing, shape, fill, border, padding, behavior })) control.value = previousTile[key as keyof typeof previousTile];
        previousTile = undefined;
      }
      tag.required = !utility; title.required = false; titleField.hidden = formatTitle.hidden = false;
      sizing.options[0]!.textContent = ['container','module'].includes(type) ? 'Automatic · fit content when closed' : 'Automatic · fit contents';
      const utilityFields = new Set(['Width', 'Height', 'Fill', 'Border', 'Border width', 'Border style']);
      for (const row of appearance.body.querySelectorAll<HTMLElement>('.creation-field')) row.hidden = utility && !utilityFields.has(row.querySelector<HTMLElement>('[aria-label]')?.getAttribute('aria-label') ?? '');
      for (const group of appearance.body.querySelectorAll<HTMLElement>(':scope > .creation-pair, :scope > .creation-colors')) group.hidden = utility && [...group.children].every(child => (child as HTMLElement).hidden);
      colors.style.gridTemplateColumns = utility ? 'repeat(2,minmax(0,1fr))' : '';
      syncSizing(); syncBehavior();
      typeHint.textContent = allowed.find(item => item.value === type)?.description ?? 'This parent currently excludes all supported node types.';
      if (type === 'canvas') {
        const roots = new Set(project.canvases.map(canvas => canvas.rootId));
        const targets = new Map(project.nodes.filter(node => node.kind === 'canvas' && roots.has(node.id)).map(node => [node.id, node]));
        canvasTarget = select([{ value: '', label: 'Create a new Work' }, ...[...targets.values()].map(node => ({ value: node.id, label: displayNodeTag(project, node) }))], '');
        details.append(field('Work workspace', canvasTarget, 'Open works in this window. Existing works can have more than one launcher.'));
      }
      if (type !== 'utility') {
        const limits = creationPermissions(project, options.parentId).nodeKinds;
        const variants = CONTAINER_CHOICES.filter(item => type !== 'container' || !limits || limits.includes(item.value === 'spreadsheet' ? 'spreadsheet' : type));
        variant = select(variants, variants[0]?.value ?? ''); const variantHint = el('p', 'creation-hint'); variantHint.id = `creation-variant-description-${crypto.randomUUID()}`; variant.setAttribute('aria-describedby', variantHint.id); details.append(field('Display Pattern', variant), variantHint);
        const gridFields = el('div', 'creation-grid-options'); columns = numberInput(3, 1, 512); rows = numberInput(3, 1, 512); cellWidth = numberInput(200, 36, 10000); cellHeight = numberInput(100, 36, 10000); gap = numberInput(0, 0, 1000); csv = textarea('', 4); csv.placeholder = 'Name,Description\nFirst entry,Details';
        fillOrder=select([{value:'row',label:'Fill by row'},{value:'column',label:'Fill by column'}],'row');
        windowColumns=numberInput(3,1,512);windowRows=numberInput(5,1,512);scrollDirection=select([{value:'vertical',label:'Vertical'},{value:'horizontal',label:'Horizontal'}],'vertical');
        const ratioFields=pair(field('Columns', columns), field('Rows', rows)),pixelFields=pair(field('Cell width', cellWidth), field('Cell height', cellHeight));
        const windowFields=el('div','creation-window-options');windowFields.append(pair(field('Window columns',windowColumns),field('Window rows',windowRows)),field('Scroll direction',scrollDirection));
        gridFields.append(ratioFields,pixelFields,field('Fill order',fillOrder),windowFields,field('Initial cells (CSV)',csv,'Optional. Cells retain their GUIDs and can be referenced as X:Y.'));
        const updateGrid = () => { const tiled=['horizontal','vertical','grid','spreadsheet','window'].includes(variant!.value),explicit=['spreadsheet','window'].includes(variant!.value),windowed=variant!.value==='window';gridFields.hidden=!tiled;ratioFields.hidden=pixelFields.hidden=!explicit;windowFields.hidden=!windowed;for(const control of [columns,rows,cellWidth,cellHeight])if(control)control.disabled=!explicit;for(const control of [windowColumns,windowRows,scrollDirection])if(control)control.disabled=!windowed;fillOrder!.disabled=csv!.disabled=!tiled;variantHint.textContent=CONTAINER_DESCRIPTIONS[variant!.value as ContainerVariant];previewUpdate(); }; variant.onchange = updateGrid; details.append(gridFields); updateGrid();
      }
      if (type === 'utility') {
        utilityVariant = select([{ value: 'pivot', label: 'Pivot' }, { value: 'joint', label: 'Joint' }], options.initialUtilityVariant ?? 'pivot');
        utilityAppearance = select([{ value: 'dot', label: 'Dot' }, { value: 'line', label: 'Line' }], 'dot');
        const hint = el('p', 'creation-hint');
        const updateUtility = () => { hint.textContent = utilityVariant!.value === 'pivot' ? 'A turning point in a link path. Drag it to change the connected route.' : 'A meeting point where several links can share one outgoing path.'; previewUpdate(); };
        utilityVariant.onchange = updateUtility;
        utilityAppearance.onchange = () => { const dot = utilityAppearance!.value === 'dot'; shape.value = dot ? 'circle' : 'rectangle'; width.value = dot ? '12' : '32'; height.value = dot ? '12' : '6'; previewUpdate(); };
        details.append(field('Utility role', utilityVariant), field('Utility appearance', utilityAppearance), hint); updateUtility();
      } else if (type === 'joint') {
        const operations = creationOperations(project, options.parentId); operation = select(operations.map(item => ({ value: item.operation, label: `${item.label} · ${item.family}` })), operations[0]?.operation ?? ''); operation.required = true;
        valueType = select([{ value: 'boolean', label: 'Boolean' }, { value: 'number', label: 'Number' }], 'boolean'); const routing = field('Value type', valueType), ports = el('p', 'creation-hint');
        const updateOperation = () => { const definition = operations.find(item => item.operation === operation!.value); routing.hidden = operation!.value !== 'IDENTITY'; ports.textContent = definition ? `${definition.inputs.map(input => `${input.name}: ${input.type === 'boolean' ? 'Boolean' : 'Number'}`).join(' · ')} → ${definition.output === 'boolean' ? 'Boolean' : 'Number'}` : 'No operations are permitted by this parent.'; }; operation.onchange = updateOperation;
        details.append(field('Operation', operation), routing, ports); updateOperation();
      } else if (type === 'reference') {
        const targets = new Map([...project.nodes, ...Object.values(project.snapshots).map(snapshot => snapshot.node)].map(node => [node.id, node])); target = select([{ value: '', label: 'Choose a node…' }, ...[...targets.values()].map(node => ({ value: node.id, label: `${displayNodeTag(project, node)} · ${workLibraryDisplayText(project.id, project.canvases.find(canvas => canvas.id === node.canvasId)?.rootId ?? '', project.canvases.find(canvas => canvas.id === node.canvasId)?.title ?? 'Saved external Work')}` }))], ''); target.required = true;
        targetCell = textInput(); targetCell.placeholder = '1:1 or a named cell'; details.append(field('Referenced node', target), field('Cell address or name', targetCell, 'Optional. Cross-work references read the saved version.'));
      } else if (type === 'number') { value = textInput('0', 'number'); value.step = 'any'; value.required = true; details.append(field('Value', value)); }
      else if (type === 'input') { value = select([{ value: 'false', label: 'False' }, { value: 'true', label: 'True' }], 'false'); details.append(field('Value', value)); }
      else if (type === 'switch' || type === 'output' || type === 'junction') {
        valueType = select([...(type === 'junction' ? [{ value: '', label: 'Visual route' }] : []), { value: 'boolean', label: 'Boolean' }, { value: 'number', label: 'Number' }], type === 'junction' ? '' : 'boolean'); details.append(field('Value type', valueType));
        if (type === 'switch') { closed = select([{ value: 'true', label: 'Closed · pass the value' }, { value: 'false', label: 'Open · stop the value' }], 'true'); details.append(field('State', closed)); }
      }
      if (!options.utilityOnly) {
        contentType = select([{value:'text',label:'Text'},{value:'asset',label:'Media or document'},{value:'link',label:'Hyperlink'}],'text');
        const contentFields = el('div','creation-content-options');
        details.append(field('Content',contentType,'One content item per node. The title is separate.'),contentFields);
        const updateContent = () => {
          if(bodyText){bodyText.flush(true);bodyMemory.set(type,bodyText.getValue());bodyText.dispose();}
          bodyText=undefined;file=undefined;hyperlinkUrl=undefined;hyperlinkLabel=undefined;imageFit=undefined;contentFields.replaceChildren();
          if(contentType!.value==='text') {const prior=bodyMemory.get(type);bodyText=createRichTextEditor({label:'Text body',text:prior?.text??'',runs:prior?.runs??[],base:{fontFamily:fontFamily.value,fontSize:Number(fontSize.value),textColor:color.value},onChange:()=>previewUpdate()});contentFields.append(el('span','creation-label','Text body'),bodyText.root);}
          else if(contentType!.value==='asset') {
            file=textInput('','file');file.required=true;file.onchange=()=>{if(!tagEdited&&!tag.value.trim()&&file?.files?.[0])tag.value=file.files[0].name;previewUpdate();};
            imageFit=select([{value:'contain',label:'Show complete image'},{value:'cover',label:'Fill the tile'}],'contain');
            contentFields.append(field('Content file',file,'An image, video, audio, or document. The complete file is preserved. Up to 16 MiB.'),field('Image fit',imageFit));
          } else {hyperlinkUrl=textInput('','url');hyperlinkUrl.required=true;hyperlinkUrl.placeholder='https://…';hyperlinkLabel=textInput();contentFields.append(field('Hyperlink URL',hyperlinkUrl),field('Hyperlink label',hyperlinkLabel));}
          previewUpdate();
        };contentType.onchange=updateContent;updateContent();
      }
      previewUpdate();
    }
    typeSelect.onchange = () => { m.error.textContent = ''; typeOptions(); };
    preset.onchange = () => { const style = NODE_STYLE_PRESETS.find(item => item.id === preset.value)?.style ?? {}; fill.value = style.fill ?? '#1c2935'; border.value = style.border ?? '#58706b'; color.value = style.textColor ?? '#e1ebe9'; fontFamily.setValue(style.fontFamily ?? 'sans'); fontSize.value = String(style.fontSize ?? DEFAULT_NODE_FONT_SIZE); titleSize.value = String(style.titleSize ?? DEFAULT_NODE_TITLE_SIZE); if (style.variant === 'label') shape.value = 'none'; previewUpdate(); };
    shape.onchange = () => { if (shape.value === 'circle' || shape.value === 'square') height.value = width.value; previewUpdate(); };
    for (const control of [title, fill, border, color, fontSize, titleSize]) control.addEventListener('input', previewUpdate);
    title.addEventListener('input', () => { if (!tagEdited && title.value.trim()) tag.value = title.value.trim(); });
    width.addEventListener('input', () => { if (shape.value === 'circle' || shape.value === 'square') height.value = width.value; previewUpdate(); });
    height.addEventListener('input', () => { if (shape.value === 'circle' || shape.value === 'square') width.value = height.value; previewUpdate(); });
    behavior.onchange = syncBehavior; syncBehavior(); typeOptions(); m.submit.disabled = !allowed.length;
    m.form.onsubmit = async event => {
      event.preventDefault(); if (pending || !m.form.reportValidity()) return; pending = true; m.submit.disabled = true; m.error.textContent = '';
      try {
        const type = typeSelect.value as CreationType; const style = { ...(NODE_STYLE_PRESETS.find(item => item.id === preset.value)?.style ?? {}), shape: shape.value as NodeShape, variant: shape.value === 'none' ? 'label' : 'card', fill: fill.value, border: border.value, textColor: color.value, fontFamily: fontFamily.value as NodeStyle['fontFamily'], fontSize: Number(fontSize.value), titleSize: Number(titleSize.value),borderWidth:Number(borderWidth.value),borderStyle:borderStyle.value as NodeStyle['borderStyle'],padding:Number(padding.value),contentGap:Number(contentGap.value),textMediaGap:Number(textMediaGap.value),childPadding:Number(childPadding.value), ...(imageFit ? { imageFit: imageFit.value as 'contain' | 'cover' } : {}) } as NodeStyle; style.textLayout='balanced'; style.textAlign??='center';
        const spec: NodeCreationSpec = { canvasId: options.canvasId, parentId: options.parentId, x: options.x, y: options.y, type, tag: tag.value.trim() || (type === 'utility' ? titleCase(utilityVariant?.value ?? 'pivot') : ''), title: title.value, width: Number(width.value), height: Number(height.value), sizeMode:sizing.value as 'auto'|'fixed', style, behavior: behavior.value as NodeBehavior, attachSide: side.value as AttachSide };
        if (utilityVariant) spec.utilityVariant = utilityVariant.value as UtilityVariant;
        if (bodyText) {bodyText.flush(true);const value=bodyText.getValue();spec.text=value.text;if(value.runs.length)spec.textRuns=value.runs;}
        if(titleRuns.length&&title.value.trim())spec.titleRuns=titleRuns;
        if (contentType) spec.contentType = contentType.value as 'text'|'asset'|'link';
        if (hyperlinkUrl) spec.hyperlink = {url:hyperlinkUrl.value.trim(),...(hyperlinkLabel?.value.trim()?{label:hyperlinkLabel.value.trim()}:{})};
        if (variant) spec.variant = variant.value as ContainerVariant;
        if(fillOrder&&!fillOrder.disabled){spec.grid={columns:Number(columns!.value),rows:Number(rows!.value),cellWidth:Number(cellWidth!.value),cellHeight:Number(cellHeight!.value),gap:0,showHeaders:false,fillOrder:fillOrder.value as 'row'|'column',...(variant?.value==='window'?{windowColumns:Number(windowColumns!.value),windowRows:Number(windowRows!.value),scrollDirection:scrollDirection!.value as 'vertical'|'horizontal'}:{})};if(csv?.value.trim())spec.csv=csv.value;}
        if (operation) spec.operation = operation.value as Operation;
        if (valueType?.value) spec.valueType = valueType.value as ValueType;
        if (value) spec.value = type === 'number' ? Number(value.value) : value.value === 'true';
        if (closed) spec.closed = closed.value === 'true';
        if (target) spec.targetId = target.value;
        if (targetCell?.value.trim()) spec.targetCell = targetCell.value.trim();
        if (canvasTarget?.value) spec.canvasTargetId = canvasTarget.value;
        const attachment = file?.files?.[0]; if (attachment) spec.asset = await readAttachment(attachment);
        if (!m.dialog.open) return;
        createNodeFromSpec(project, spec); result = spec; m.dialog.close();
      } catch (error) { m.error.textContent = error instanceof Error ? error.message : String(error); }
      finally { pending = false; m.submit.disabled = !allowed.length; }
    };
    m.dialog.addEventListener('close', () => {bodyText?.dispose();resolve(result);}); m.dialog.showModal(); typeSelect.focus();
  });
}
function assetFromBytes(name: string, mime: string, bytes: Uint8Array): MeridianAsset {
  let binary = ''; for (let offset = 0; offset < bytes.length; offset += 32768) binary += String.fromCharCode(...bytes.subarray(offset, offset + 32768));
  return { id: crypto.randomUUID(), name, mime, base64: btoa(binary) };
}
async function readAttachment(file: File): Promise<MeridianAsset> {
  if (file.size > 16 * 1024 * 1024) throw new Error('Attachments must be 16 MiB or smaller.');
  const extension = file.name.split('.').at(-1)?.toLowerCase(); const fallback: Record<string, string> = { png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif', webp: 'image/webp', avif: 'image/avif', bmp: 'image/bmp', mp4: 'video/mp4', webm: 'video/webm', ogv: 'video/ogg', pdf: 'application/pdf', txt: 'text/plain', md: 'text/markdown', csv: 'text/csv', tsv: 'text/tab-separated-values' };
  return assetFromBytes(file.name, file.type || fallback[extension ?? ''] || 'application/octet-stream', new Uint8Array(await file.arrayBuffer()));
}

export interface LinkDialogOptions { canvasId: string; sourceId?: string; targetId?: string }
export async function createLinkDialog(project: MeridianProject, options: LinkDialogOptions): Promise<LinkCreationSpec | null> {
  return new Promise(resolve => {
    const m = shell('New Link', 'Connect two nodes and choose how their relationship appears.'); let result: LinkCreationSpec | null = null;
    const nodes = project.nodes.filter(node => node.canvasId === options.canvasId && node.kind !== 'canvas'), nav = navigationIndex(project);
    const nodeChoices = [{ value: '', label: 'Choose a node…' }, ...nodes.map(node => ({ value: node.id, label: displayNodeTag(project, node) }))];
    const source = select(nodeChoices, options.sourceId ?? ''), target = select(nodeChoices, options.targetId ?? ''); source.required = true; target.required = true;
    const kind = select([{ value: 'visual', label: 'Visual' }, { value: 'functional', label: 'Boolean' }, { value: 'numeric', label: 'Numeric' }], 'visual');
    const relation = select(['generic', 'causal', 'chronological', 'mutual', 'cyclical'].map(value => ({ value, label: titleCase(value) })), 'generic');
    const direction = select([{ value: 'none', label: 'Plain line' }, { value: 'forward', label: 'Source → Destination' }, { value: 'reverse', label: 'Source ← Destination' }, { value: 'both', label: 'Source ↔ Destination' }], 'none');
    const label = textInput(); label.maxLength = 160; label.placeholder = 'Describe the relationship'; const port = select([], ''), portField = field('Destination input', port);
    const owner = select([{ value: '', label: 'Automatic · nearest shared parent' }, ...project.nodes.filter(node => node.canvasId === options.canvasId).map(node => ({ value: node.id, label: displayNodeTag(project, node) }))], '');
    const ownershipHint = el('p', 'creation-hint'); ownershipHint.id = `creation-owner-description-${crypto.randomUUID()}`; owner.setAttribute('aria-describedby', ownershipHint.id); const color = textInput('#78ddc1', 'color'), width = numberInput(2, STYLE_LIMITS.linkWidth.min, STYLE_LIMITS.linkWidth.max, .5), line = select([{ value: 'solid', label: 'Solid' }, { value: 'dashed', label: 'Dashed' }, { value: 'dotted', label: 'Dotted' }], 'solid');
    const fields = el('div', 'creation-link-fields'); fields.append(field('Type', kind), pair(field('Source', source), field('Destination', target)), pair(field('Relationship', relation), field('Direction', direction)), portField, field('Label', label));
    const style = block('Appearance'); style.body.append(pair(field('Color', color), field('Line width', width)), field('Line style', line));
    const behavior = block('Visibility'); behavior.body.append(field('Contained in', owner), ownershipHint); fields.append(style.root, behavior.root); m.body.append(fields);
    function permissions(nodeId: string) { const node = project.nodes.find(node => node.id === nodeId); return node?.parentId ? creationPermissions(project, node.parentId).linkKinds : undefined; }
    function update() {
      for (const option of kind.options) { const sourceKinds = permissions(source.value), targetKinds = permissions(target.value); option.disabled = !!(sourceKinds && !sourceKinds.includes(option.value as LinkKind) || targetKinds && !targetKinds.includes(option.value as LinkKind)); }
      if (kind.selectedOptions[0]?.disabled) kind.value = [...kind.options].find(option => !option.disabled)?.value ?? '';
      const computed = kind.value !== 'visual'; direction.disabled = computed; if (computed) direction.value = 'forward'; portField.hidden = !computed;
      port.replaceChildren(); for (const item of availableLinkPorts(project, source.value, target.value, kind.value as LinkKind)) { const option = el('option', '', item.label); option.value = String(item.value); port.append(option); } port.required = computed; port.disabled = !computed;
      const sourceNode = nav.nodes.get(source.value), targetNode = nav.nodes.get(target.value); const inferred = sourceNode && targetNode ? nav.linkParent({ id: 'preview', canvasId: options.canvasId, sourceId: source.value, targetId: target.value, kind: kind.value as LinkKind, relation: relation.value as LinkRelation }) : undefined;
      const owningNode = owner.value ? nav.nodes.get(owner.value) : inferred;
      ownershipHint.textContent = `The link appears when ${owningNode?.kind === 'canvas' ? 'the work' : (owningNode ? workLibraryDisplayText(project.id, owningNode.id, owningNode.title) : '') || 'its containing node'} is expanded and both endpoints are revealed.`;
      m.error.textContent = computed && source.value && target.value && !port.options.length ? 'No available destination input matches this value type. Choose another node or link type.' : '';
    }
    source.onchange = update; target.onchange = update; kind.onchange = update; owner.onchange = update;
    relation.onchange = () => { if (kind.value === 'visual') direction.value = relation.value === 'mutual' ? 'both' : relation.value === 'generic' ? 'none' : 'forward'; update(); };
    m.form.onsubmit = event => {
      event.preventDefault(); if (!m.form.reportValidity()) return;
      try {
        const spec: LinkCreationSpec = { canvasId: options.canvasId, sourceId: source.value, targetId: target.value, kind: kind.value as LinkKind, relation: relation.value as LinkRelation, direction: direction.value as LinkDirection, ...(owner.value ? { parentId: owner.value } : {}), ...(label.value.trim() ? { label: label.value.trim() } : {}), style: { color: color.value, width: Number(width.value), lineStyle: line.value as 'solid' | 'dashed' | 'dotted' }, ...(kind.value === 'visual' ? {} : { targetPort: Number(port.value) }) };
        createLinkFromSpec(project, spec); result = spec; m.dialog.close();
      } catch (error) { m.error.textContent = error instanceof Error ? error.message : String(error); }
    };
    update(); m.dialog.addEventListener('close', () => resolve(result)); m.dialog.showModal(); (source.value ? target : source).focus();
  });
}
