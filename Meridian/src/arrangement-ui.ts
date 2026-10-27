import type { ArrangementAction } from './core/arrangement.ts';

export interface ArrangementDialogOptions {
  branchCount: number;
  apply: (action: ArrangementAction) => void;
}

/** Explicit layout commands; selecting or opening this menu does not edit the canvas. */
export function showArrangementDialog(options: ArrangementDialogOptions): void {
  const element = <K extends keyof HTMLElementTagNameMap>(tag: K, cls = '', text = '') => {
    const node = document.createElement(tag); node.className = cls; node.textContent = text; return node;
  };
  const dialog = element('dialog', 'modal arrangement-modal');
  dialog.setAttribute('aria-label', 'Arrange selected nodes');
  const header = element('div', 'modal-header');
  header.append(element('h2', '', 'Arrange selected nodes'));
  const body = element('div', 'modal-body');
  body.append(element('p', 'arrangement-description', `${options.branchCount} separate branches. Children move with their parent, including hidden children.`));
  const error = element('p', 'arrangement-error'); error.setAttribute('role', 'alert');
  const command = (action: ArrangementAction, label: string, name: string, minimum = 2) => {
    const control = element('button', 'secondary', label); control.type = 'button';
    control.setAttribute('aria-label', name); control.title = name;
    control.disabled = options.branchCount < minimum;
    control.onclick = () => {
      try { options.apply(action); dialog.close(); }
      catch (failure) { error.textContent = failure instanceof Error ? failure.message : String(failure); }
    };
    return control;
  };
  const align = element('fieldset', 'arrangement-group');
  align.append(element('legend', '', 'Align tiles'));
  const alignButtons = element('div', 'arrangement-align');
  alignButtons.append(
    command('align-left', 'Left', 'Align left edges'),
    command('align-center-x', 'Center', 'Align horizontal centers'),
    command('align-right', 'Right', 'Align right edges'),
    command('align-top', 'Top', 'Align top edges'),
    command('align-center-y', 'Middle', 'Align vertical centers'),
    command('align-bottom', 'Bottom', 'Align bottom edges'),
  );
  align.append(alignButtons); body.append(align);
  const spacing = element('fieldset', 'arrangement-group');
  spacing.append(element('legend', '', 'Space evenly'));
  const spacingButtons = element('div', 'arrangement-spacing');
  spacingButtons.append(command('space-horizontal', 'Horizontally', 'Space evenly horizontally', 3), command('space-vertical', 'Vertically', 'Space evenly vertically', 3));
  spacing.append(spacingButtons, element('p', 'muted', options.branchCount < 3 ? 'Select at least three separate branches to space them evenly.' : 'Keeps the outer tiles in place and equalizes the gaps between them.'));
  body.append(spacing, error);
  const footer = element('div', 'modal-footer');
  const cancel = element('button', 'secondary', 'Cancel'); cancel.type = 'button'; cancel.onclick = () => dialog.close();
  footer.append(cancel); dialog.append(header, body, footer);
  const previousFocus = document.activeElement;
  dialog.addEventListener('close', () => {
    dialog.remove();
    const target = previousFocus instanceof HTMLElement && previousFocus.isConnected ? previousFocus :
      document.querySelector<HTMLElement>('.selection-bar button[aria-label="Arrange selected nodes"]');
    target?.focus();
  });
  document.body.append(dialog); dialog.showModal();
}
