export type WorkspaceMode = 'select' | 'hand' | 'text' | 'node' | 'link' | 'smart-link' | 'module' | 'delete';
export interface WorkspaceAction { label: string; run: () => unknown; disabled?: boolean }

export interface WorkspaceSelectionTool {
  active: boolean;
  activate: () => void;
  /** Retain the chosen interaction tool while a creation or deletion tool is active. */
  navigationMode?: 'select' | 'hand';
}

const hand = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 12V5a1.5 1.5 0 0 1 3 0v6-7a1.5 1.5 0 0 1 3 0v7-5a1.5 1.5 0 0 1 3 0v6-3a1.5 1.5 0 0 1 3 0v5c0 5-2.5 8-6 8h-1c-2 0-3.5-1-5-3l-4-5a1.5 1.5 0 0 1 2-2l2 2Z" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/></svg>';
const finger = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 13V4.5a1.5 1.5 0 0 1 3 0V11h2a1.5 1.5 0 0 1 1.5 1.5h1a1.5 1.5 0 0 1 1.5 1.5h.5a1.5 1.5 0 0 1 1.5 1.5V17c0 3-2 5-5 5h-2c-2 0-3.5-1-4.5-2.5l-4-5a1.5 1.5 0 0 1 2.3-1.9L9 15" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>';

/** Native popovers supply outside-click dismissal and keep canvas menus above the scene. */
export function workspaceMenu(actions: WorkspaceAction[], location: {x:number;y:number}, label = 'Work actions'): HTMLElement {
  document.querySelectorAll('.workspace-menu').forEach(menu => menu.remove());
  const menu = document.createElement('div'); menu.className = 'workspace-menu'; menu.popover = 'auto';
  menu.setAttribute('role', 'menu'); menu.setAttribute('aria-label', label);
  for (const action of actions) {
    const item = document.createElement('button'); item.type = 'button'; item.textContent = action.label;
    item.setAttribute('role', 'menuitem'); item.disabled = !!action.disabled;
    item.onclick = () => { menu.hidePopover(); menu.remove(); action.run(); };
    menu.append(item);
  }
  document.body.append(menu); menu.showPopover();
  menu.style.left = `${Math.max(8,Math.min(location.x,innerWidth-menu.offsetWidth-8))}px`;
  menu.style.top = `${Math.max(8,Math.min(location.y,innerHeight-menu.offsetHeight-8))}px`;
  menu.addEventListener('toggle', event => { if ((event as ToggleEvent).newState === 'closed') menu.remove(); });
  const items = [...menu.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')];
  items[0]?.focus();
  menu.onkeydown = event => {
    if(event.key==='ArrowDown'||event.key==='ArrowUp') { event.preventDefault(); const at=items.indexOf(document.activeElement as HTMLButtonElement);items[(at+(event.key==='ArrowDown'?1:-1)+items.length)%items.length]?.focus(); }
  };
  return menu;
}

/** Share the toolbar and keyboard menu so object creation has one set of choices. */
export function workspaceCreationMenu(change: (mode: WorkspaceMode) => void, location: {x:number;y:number}): void {
  const menu=workspaceMenu([{label:'Node',run:()=>change('node')},{label:'Link',run:()=>change('link')},{label:'Smart Link',run:()=>change('smart-link')},{label:'Module',run:()=>change('module')}],location,'Create an object');
  menu.dataset.workspaceCreateMenu='';
  const create=document.querySelector<HTMLButtonElement>('.workspace-create');
  create?.setAttribute('aria-expanded','true');create?.classList.add('menu-open');
  menu.addEventListener('toggle',event=>{if((event as ToggleEvent).newState==='closed'){create?.setAttribute('aria-expanded','false');create?.classList.remove('menu-open');}});
}

export function workspaceTools(mode: WorkspaceMode, change: (mode: WorkspaceMode) => void, selection?: WorkspaceSelectionTool): HTMLElement {
  const group = document.createElement('div'); group.className = 'workspace-tools'; group.setAttribute('role','group');group.setAttribute('aria-label','Work tools');
  const creating=mode==='node'||mode==='link'||mode==='smart-link'||mode==='module';
  const create = document.createElement('button'); create.type='button';create.className=`workspace-tool workspace-create ${creating?'active':''}`;
  const createLabel=mode==='module'?'Module':mode==='link'?'New Link':mode==='smart-link'?'Smart Link':'New Node';create.title=createLabel;create.setAttribute('aria-label',createLabel);create.setAttribute('aria-haspopup','menu');create.setAttribute('aria-expanded','false');create.setAttribute('aria-pressed',String(creating));
  create.append(Object.assign(document.createElement('span'),{className:'workspace-plus',textContent:'+'}));
  create.onclick=()=>{const rect=create.getBoundingClientRect();workspaceCreationMenu(change,{x:rect.left,y:rect.bottom+8});};
  const remove=document.createElement('button');remove.type='button';remove.className=`workspace-tool ${mode==='delete'?'active delete-active':''}`;remove.textContent='−';remove.title='Delete objects';remove.setAttribute('aria-label','Delete objects');remove.setAttribute('aria-pressed',String(mode==='delete'));remove.onclick=()=>change(mode==='delete'?'select':'delete');
  const navigationMode=mode==='select'||mode==='hand'?mode:selection?.navigationMode??'select';
  const navigationActive=mode==='select'||mode==='hand';
  const navigate=document.createElement('button');navigate.type='button';navigate.className=`workspace-tool workspace-navigation ${navigationActive?'active':''}`;
  navigate.title=navigationMode==='select'?'Single select':'Select Area';navigate.setAttribute('aria-label',navigationMode==='select'?'Single select':'Select Area');navigate.setAttribute('aria-haspopup','menu');navigate.setAttribute('aria-pressed',String(navigationActive));navigate.innerHTML=navigationMode==='select'?finger:hand;
  navigate.onclick=()=>{const rect=navigate.getBoundingClientRect();workspaceMenu([{label:'Single select',run:()=>change('select')},{label:'Select Area',run:()=>change('hand')}],{x:rect.left,y:rect.bottom+8},'Selection tool');};
  const text=document.createElement('button');text.type='button';text.className=`workspace-tool workspace-text ${mode==='text'?'active':''}`;text.title='Text (T)';text.setAttribute('aria-label','Text');text.setAttribute('aria-pressed',String(mode==='text'));text.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7V4h16v3M12 4v16M8 20h8" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>';text.onclick=()=>change(mode==='text'?'select':'text');
  const smart=document.createElement('button');smart.type='button';smart.className=`workspace-tool workspace-smart-link ${mode==='smart-link'?'active':''}`;
  smart.title='Place Smart Link';smart.setAttribute('aria-label','Place Smart Link');smart.setAttribute('aria-pressed',String(mode==='smart-link'));
  smart.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 3v18M6 5h7m-7 7h7m-7 7h7" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/><rect x="13" y="2" width="7" height="6" rx="1" fill="none" stroke="currentColor" stroke-width="1.4"/><rect x="13" y="9" width="7" height="6" rx="1" fill="none" stroke="currentColor" stroke-width="1.4"/><rect x="13" y="16" width="7" height="6" rx="1" fill="none" stroke="currentColor" stroke-width="1.4"/></svg>';
  smart.onclick=()=>change(mode==='smart-link'?'select':'smart-link');
  group.append(create,smart,remove,navigate,text);
  return group;
}
