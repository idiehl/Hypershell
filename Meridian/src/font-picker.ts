import { FONT_CATEGORIES, FONT_FAMILIES, fontFamily, type FontFamily } from './core/fonts.ts';

/** Font names and preview stacks come exclusively from the portable catalog. */
export function createFontPicker(selected: FontFamily | undefined, change: (value: FontFamily) => void) {
  let current = fontFamily(selected);
  const button = document.createElement('button'); button.type = 'button'; button.className = 'font-picker-control';
  button.setAttribute('role', 'combobox'); button.setAttribute('aria-haspopup', 'listbox'); button.setAttribute('aria-expanded', 'false');
  button.style.fontFamily = current.css; button.dataset.fontFamily = current.value;
  const name = document.createElement('span'); name.textContent = current.label;
  const arrow = document.createElement('span'); arrow.className = 'font-picker-arrow'; arrow.textContent = '⌄'; arrow.setAttribute('aria-hidden', 'true');
  button.append(name, arrow);

  const popup = document.createElement('div'); popup.className = 'font-picker-popup'; popup.popover = 'auto';
  popup.id = `font-picker-${crypto.randomUUID()}`; button.setAttribute('aria-controls', popup.id);
  const search = document.createElement('input'); search.type = 'search'; search.placeholder = 'Find a font…';
  search.setAttribute('aria-label', 'Search fonts'); search.autocomplete = 'off';
  const count = document.createElement('p'); count.className = 'font-picker-count'; count.setAttribute('aria-live', 'polite');
  const list = document.createElement('div'); list.className = 'font-picker-list'; list.id = `${popup.id}-list`;
  list.setAttribute('role', 'listbox'); list.setAttribute('aria-label', 'Font families');
  search.setAttribute('aria-controls', list.id);
  const note = document.createElement('p'); note.className = 'font-picker-note'; note.textContent = 'Uses local fonts, with a matching fallback when unavailable.';
  popup.append(search, count, list, note);
  let options: HTMLButtonElement[] = [], active = -1;
  const setValue = (value: FontFamily) => {
    current = fontFamily(value); name.textContent = current.label;
    button.style.fontFamily = current.css; button.dataset.fontFamily = current.value;
  };

  const activate = (index: number) => {
    active = Math.max(0, Math.min(options.length - 1, index));
    for (const [i, option] of options.entries()) option.classList.toggle('is-active', i === active);
    const chosen = options[active];
    if (chosen) { search.setAttribute('aria-activedescendant', chosen.id); chosen.scrollIntoView({ block: 'nearest' }); }
    else search.removeAttribute('aria-activedescendant');
  };
  const choose = (value: FontFamily) => {
    setValue(value); popup.hidePopover(); change(value);
    // Saving appearance replaces the inspector. Restore keyboard access to its
    // new font control instead of focusing the discarded button.
    requestAnimationFrame(() => {
      const replacement = button.isConnected ? button : document.querySelector<HTMLButtonElement>('.font-picker-control');
      replacement?.focus({ preventScroll: true });
    });
  };
  const draw = () => {
    const query = search.value.trim().toLocaleLowerCase(); list.replaceChildren(); options = []; active = -1;
    const matches = FONT_FAMILIES.filter(font => !query || `${font.label} ${font.category}`.toLocaleLowerCase().includes(query));
    count.textContent = `${matches.length} ${matches.length === 1 ? 'font' : 'fonts'}`;
    for (const category of FONT_CATEGORIES) {
      const fonts = matches.filter(font => font.category === category.value); if (!fonts.length) continue;
      const group = document.createElement('div'); group.className = 'font-picker-group'; group.setAttribute('role', 'group'); group.setAttribute('aria-label', category.label);
      const heading = document.createElement('p'); heading.className = 'font-picker-category'; heading.textContent = category.label; group.append(heading);
      for (const font of fonts) {
        const option = document.createElement('button'); option.type = 'button'; option.className = 'font-picker-option'; option.tabIndex = -1;
        option.id = `${popup.id}-${font.value}`; option.setAttribute('role', 'option'); option.setAttribute('aria-label', font.label);
        option.setAttribute('aria-selected', String(font.value === current.value)); option.dataset.fontFamily = font.value;
        const preview = document.createElement('span'); preview.className = 'font-picker-sample'; preview.style.fontFamily = font.css; preview.textContent = 'Aa'; preview.setAttribute('aria-hidden', 'true');
        const label = document.createElement('span'); label.className = 'font-picker-name'; label.style.fontFamily = font.css; label.textContent = font.label;
        const check = document.createElement('span'); check.className = 'font-picker-check'; check.textContent = font.value === current.value ? '✓' : ''; check.setAttribute('aria-hidden', 'true');
        option.append(preview, label, check); option.onclick = () => choose(font.value); group.append(option); options.push(option);
      }
      list.append(group);
    }
    if (!matches.length) { const empty = document.createElement('p'); empty.className = 'font-picker-empty'; empty.textContent = 'No matching fonts.'; list.append(empty); }
    search.removeAttribute('aria-activedescendant');
  };
  const position = () => {
    if (!popup.matches(':popover-open') || !button.isConnected) return;
    const anchor = button.getBoundingClientRect(), width = Math.min(350, window.innerWidth - 16);
    popup.style.width = `${width}px`;
    const height = popup.getBoundingClientRect().height;
    popup.style.left = `${Math.max(8, Math.min(anchor.left, window.innerWidth - width - 8))}px`;
    popup.style.top = `${Math.max(8, Math.min(anchor.bottom + 6, window.innerHeight - height - 8))}px`;
  };
  const open = () => { if (popup.matches(':popover-open')) { popup.hidePopover(); return; } search.value = ''; draw(); popup.showPopover(); position(); search.focus(); };
  button.onclick = open;
  button.onkeydown = event => { if (event.key === 'ArrowDown') { event.preventDefault(); open(); } };
  search.oninput = () => { draw(); position(); };
  popup.addEventListener('keydown', event => {
    // Return Tab to the owning form's normal focus order, including modal traps.
    if (event.key === 'Tab') { popup.hidePopover(); button.focus({ preventScroll: true }); return; }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault(); activate(active < 0 ? event.key === 'ArrowDown' ? 0 : options.length - 1 : active + (event.key === 'ArrowDown' ? 1 : -1));
    } else if (event.key === 'Enter') {
      event.preventDefault(); const font = options[active < 0 ? 0 : active]?.dataset.fontFamily as FontFamily | undefined; if (font) choose(font);
    } else if (event.key === 'Escape') { event.preventDefault(); popup.hidePopover(); button.focus({ preventScroll: true }); }
    // Search/selection keys belong to this chooser, not the canvas shortcuts.
    event.stopPropagation();
  });
  // Removing an open inspector closes its popover without a toggle-close
  // event in Chromium. Observe disconnection only while open so discarded
  // pickers cannot retain their inspector through a window listener.
  const disconnected = new MutationObserver(() => { if (!popup.isConnected || !button.isConnected) release(); });
  const release = () => { window.removeEventListener('resize', position); disconnected.disconnect(); button.setAttribute('aria-expanded', 'false'); };
  popup.addEventListener('toggle', event => {
    const open = (event as ToggleEvent).newState === 'open'; release();
    if (open && popup.isConnected && button.isConnected) {
      button.setAttribute('aria-expanded', 'true'); window.addEventListener('resize', position);
      disconnected.observe(document.body, { childList: true, subtree: true });
    }
  });
  return { button, popup, setValue, get value() { return current.value; } };
}
