/** Per-window inspection state, separate from authored canvas data. */
interface FocusTarget {
  tag: string;
  label: string;
  outlineNode?: string;
  contentItem?: string;
  occurrence: number;
  selection?: [number, number, 'forward' | 'backward' | 'none'];
}
interface PanelPosition { top: number; left: number }
interface InspectionFocus { context: string; focus?: FocusTarget }

export function createInspectorState(limit = 96) {
  const positions = new Map<string, PanelPosition>();
  const bound = new WeakSet<HTMLElement>();
  let tabDestination: InspectionFocus | undefined;
  const candidates = (host: HTMLElement, target: FocusTarget): HTMLElement[] =>
    [...host.querySelectorAll<HTMLElement>('button,input,textarea,select,a,[tabindex]')].filter(element =>
      element.tagName === target.tag && element.getAttribute('aria-label') === target.label &&
      element.closest<HTMLElement>('[data-outline-node]')?.dataset.outlineNode === target.outlineNode &&
      element.closest<HTMLElement>('[data-content-item]')?.dataset.contentItem === target.contentItem);

  return {
    capture(host: HTMLElement | null): InspectionFocus | undefined {
      const context = host?.dataset.inspectorContext, body = host?.querySelector<HTMLElement>('.inspector-panel-body');
      if (!host || !context || !body) return;
      // A closed inspector has no layout; retain its last open scroll position.
      if (body.clientHeight) {
        positions.delete(context);
        positions.set(context, { top: body.scrollTop, left: body.scrollLeft });
        while (positions.size > limit) positions.delete(positions.keys().next().value!);
      }
      const captured: InspectionFocus = { context };
      if (tabDestination?.context === context) {
        captured.focus = tabDestination.focus;
        tabDestination = undefined;
        return captured;
      }
      const active = document.activeElement;
      if (!(active instanceof HTMLElement) || !host.contains(active)) return captured;
      const label = active.getAttribute('aria-label');
      if (!label) return captured;
      const focus: FocusTarget = { tag: active.tagName, label,
        outlineNode: active.closest<HTMLElement>('[data-outline-node]')?.dataset.outlineNode,
        contentItem: active.closest<HTMLElement>('[data-content-item]')?.dataset.contentItem, occurrence: 0 };
      focus.occurrence = candidates(host, focus).indexOf(active);
      if (active instanceof HTMLTextAreaElement || active instanceof HTMLInputElement) {
        if (active.selectionStart !== null && active.selectionEnd !== null)
          focus.selection = [active.selectionStart, active.selectionEnd, active.selectionDirection ?? 'none'];
      }
      captured.focus = focus;
      return captured;
    },
    /** Restore only the matching panel/entity context; never focus a different node's field. */
    restore(host: HTMLElement, context: string, captured?: InspectionFocus): boolean {
      host.dataset.inspectorContext = context;
      if(!bound.has(host)){bound.add(host);host.addEventListener('keydown', event => {
        if (event.key !== 'Tab' || event.ctrlKey || event.metaKey || event.altKey) return;
        const controls = [...host.querySelectorAll<HTMLElement>('button,input,textarea,select,a[href],[tabindex]')]
          .filter(element => element.tabIndex >= 0 && element.getClientRects().length &&
            !('disabled' in element && element.disabled));
        const at = controls.indexOf(document.activeElement as HTMLElement), next = controls[at + (event.shiftKey ? -1 : 1)];
        const label = next?.getAttribute('aria-label');
        const target: FocusTarget | undefined = next && label ? { tag: next.tagName, label,
          outlineNode: next.closest<HTMLElement>('[data-outline-node]')?.dataset.outlineNode,
          contentItem: next.closest<HTMLElement>('[data-content-item]')?.dataset.contentItem, occurrence: 0 } : undefined;
        if (target) target.occurrence = candidates(host, target).indexOf(next!);
        // A change committed on blur removes the native Tab destination. Carry
        // its identity through that synchronous render without trapping Tab.
        tabDestination = { context: host.dataset.inspectorContext ?? context, focus: target };
        setTimeout(() => { tabDestination = undefined; }, 0);
      }, true);}
      const body = host.querySelector<HTMLElement>('.inspector-panel-body'), position = positions.get(context);
      if (body && position) { body.scrollTop = position.top; body.scrollLeft = position.left; }
      // Removing a focused field can synchronously commit its change and render
      // again. The outer render owns this token so a nested capture cannot erase it.
      const target = captured?.context === context ? captured.focus : undefined;
      if (!target || !host.getClientRects().length) return false;
      const element = candidates(host, target)[target.occurrence];
      if (!element || (element instanceof HTMLButtonElement || element instanceof HTMLInputElement ||
          element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement) && element.disabled) return false;
      element.focus({ preventScroll: true });
      if (target.selection && (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement)) {
        try { element.setSelectionRange(...target.selection); } catch { /* Non-text inputs have no caret. */ }
      }
      return document.activeElement === element;
    },
  };
}
