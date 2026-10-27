import type { MeridianProject, Rules, NodeKind } from './core/types.ts';
import { nodeTag } from './core/node-tag.ts';
import { OPERATION_DEFINITIONS } from './core/operations.ts';
import { RULE_KEYS, RULE_VOCABULARY, effectiveInheritedRules, previewRuleChange } from './core/rule-impact.ts';

const nodeLabels: Record<NodeKind, string> = {
  canvas: 'Work root', container: 'Container', spreadsheet: 'Spreadsheet', module: 'Module', media: 'Media', block: 'Block', text: 'Text', image: 'Image',
  document: 'Document', input: 'Boolean input', number: 'Number input', switch: 'Switch', output: 'Output',
  joint: 'Operation joint', junction: 'Route junction', reference: 'Reference', 'canvas-link': 'Work launcher',
};
const categoryLabels = { nodeKinds: 'Node types', linkKinds: 'Connection types', operations: 'Operations' };
const groupLabels = { nodeKinds: 'Allowed node types', linkKinds: 'Allowed connection types', operations: 'Allowed operations' };
const inheritLabels = { nodeKinds: 'Inherit node types from parent', linkKinds: 'Inherit connection types from parent', operations: 'Inherit operations from parent' };
const labelFor = (key: keyof Rules, value: string): string => key === 'nodeKinds' ? nodeLabels[value as NodeKind] ?? value :
  key === 'linkKinds' ? ({ visual: 'Visual', functional: 'Boolean', numeric: 'Numeric' }[value] ?? value) :
  OPERATION_DEFINITIONS.find(item => item.operation === value)?.label ?? value;
const labelsFor = (key: keyof Rules, values: readonly string[]) => values.length ? values.map(value => labelFor(key, value)).join(', ') : 'None';

export function ruleSummary(rules: Rules | undefined): string {
  const explicit = RULE_KEYS.filter(key => rules?.[key] !== undefined);
  return explicit.length ? explicit.map(key => `${categoryLabels[key]}: ${labelsFor(key, rules![key]!)}`).join(' · ') : 'All categories inherit from the parent.';
}

export function showRuleDialog(options: {
  project: MeridianProject; nodeId: string; apply: (rules: Rules | undefined) => void;
}): void {
  const project = options.project, target = project.nodes.find(item => item.id === options.nodeId);
  if (!target) throw new Error('The selected node no longer exists.');
  const node = target;
  const nodes = new Map(project.nodes.map(item => [item.id, item])), links = new Map(project.links.map(item => [item.id, item]));
  const make = <K extends keyof HTMLElementTagNameMap>(tag: K, className = '', text?: string): HTMLElementTagNameMap[K] => {
    const item = document.createElement(tag); item.className = className; if (text !== undefined) item.textContent = text; return item;
  };
  const button = (text: string, action: () => void, className = '') => {
    const item = make('button', className, text); item.type = 'button'; item.onclick = action; return item;
  };
  const limits = effectiveInheritedRules(project, node.id);
  const draft = structuredClone(node.rules ?? {});
  const dialog = make('dialog', 'modal rule-dialog'); dialog.setAttribute('aria-label', 'Edit inherited rules');
  const header = make('header', 'modal-header');
  const close = button('×', () => dialog.close(), 'icon-button'); close.setAttribute('aria-label', 'Close rule editor');
  header.append(make('h2', '', 'Edit inherited rules'), close);
  const body = make('div', 'modal-body');
  body.append(make('p', 'rule-target', nodeTag(node)), make('p', 'muted', 'These permissions govern descendants. Each category can inherit from the parent or narrow its allowed choices. Review the effect before applying.'));
  const layout = make('div', 'rule-layout'), controls = make('div', 'rule-controls'), preview = make('section', 'rule-preview');
  preview.setAttribute('aria-label', 'Rule impact preview'); layout.append(controls, preview); body.append(layout);
  const footer = make('footer', 'modal-footer');
  const error = make('p', 'rule-error'); error.setAttribute('role', 'alert');
  const apply = button('Apply rules', () => {
    try {
      const proposed = proposal(), report = previewRuleChange(project, node.id, proposed);
      if (!report.canApply) return;
      options.apply(proposed); dialog.close();
    } catch (failure) { error.textContent = failure instanceof Error ? failure.message : String(failure); }
  }, 'primary');
  footer.append(error, button('Cancel', () => dialog.close(), 'secondary'), apply);
  dialog.append(header, body, footer);
  let filter = '', shown = 60, conflictFilter = '', conflictsShown = 60;
  function proposal(): Rules | undefined {
    return RULE_KEYS.some(key => draft[key] !== undefined) ? structuredClone(draft) : undefined;
  }
  for (const key of RULE_KEYS) {
    const group = make('fieldset', 'rule-category'); group.append(make('legend', '', groupLabels[key]));
    const inherit = make('input'); inherit.type = 'checkbox'; inherit.checked = draft[key] === undefined;
    const inheritLabel = make('label', 'rule-inherit'); inheritLabel.append(inherit, make('span', '', inheritLabels[key])); group.append(inheritLabel);
    const parent = node.parentId ? project.nodes.find(item => item.id === node.parentId) : undefined;
    group.append(make('p', 'muted rule-ancestor', `${parent ? `Inherited from ${nodeTag(parent)}` : 'Work root allowance'}: ${labelsFor(key, limits[key])}`));
    const choices = make('div', 'rule-options');
    const inputs: HTMLInputElement[] = [];
    let previous = draft[key] === undefined ? [...limits[key]] : [...draft[key]!];
    for (const value of RULE_VOCABULARY[key]) {
      const option = make('label', 'rule-option'), input = make('input'); input.type = 'checkbox'; input.value = value;
      input.checked = (draft[key] ?? limits[key]).includes(value as never);
      const text = make('span', '', labelFor(key, value));
      option.append(input, text);
      if (!limits[key].includes(value as never)) { option.classList.add('rule-blocked'); option.title = 'Excluded by an ancestor rule.'; }
      input.onchange = () => {
        previous = inputs.filter(item => item.checked).map(item => item.value) as typeof previous;
        (draft as Record<string, string[]>)[key] = [...previous]; filter = ''; shown = 60; conflictFilter = ''; conflictsShown = 60; refresh();
      };
      inputs.push(input); choices.append(option);
    }
    function updateChoices(): void {
      for (const input of inputs) {
        input.disabled = inherit.checked || !limits[key].includes(input.value as never);
        input.checked = (inherit.checked ? limits[key] : draft[key] ?? []).includes(input.value as never);
      }
      choices.classList.toggle('is-inherited', inherit.checked);
    }
    inherit.onchange = () => {
      if (inherit.checked) { previous = [...draft[key] ?? []]; delete draft[key]; }
      else (draft as Record<string, string[]>)[key] = [...previous];
      updateChoices(); filter = ''; shown = 60; conflictFilter = ''; conflictsShown = 60; refresh();
    };
    updateChoices(); group.append(choices); controls.append(group);
  }
  function refresh(): void {
    preview.replaceChildren(); error.textContent = '';
    try {
      const report = previewRuleChange(project, node.id, proposal()); apply.disabled = !report.canApply;
      const status = make('p', 'rule-impact-summary'); status.setAttribute('role', 'status');
      status.textContent = `${report.affected.length} of ${report.descendantCount} descendants have changed permissions. ${report.unchangedCount} unchanged.`;
      preview.append(status);
      if (!report.changed) preview.append(make('p', 'muted', 'Choose a different allowance to preview a change.'));
      else if (!report.affected.length) preview.append(make('p', 'muted', 'This changes the local declaration without changing any existing descendant’s permissions. Future children use the new allowance.'));
      const conflictSection = make('section', 'rule-conflicts'); conflictSection.append(make('h3', '', 'Conflicts'));
      if (!report.conflicts.length) conflictSection.append(make('p', 'muted', 'No conflicts. Existing nodes, connections and nested rules remain valid.'));
      else {
        conflictSection.append(make('p', '', `${report.conflicts.length} conflicts. Resolve them or adjust the choices before applying.`));
        const search = make('input', 'rule-search'); search.type = 'search'; search.placeholder = 'Find a conflict…'; search.value = conflictFilter; search.setAttribute('aria-label', 'Find conflicts');
        const results = make('div');
        function renderConflicts(): void {
        results.replaceChildren(); const list = make('ul');
        const matches = report.conflicts.filter(issue => {
          const link = links.get(issue.linkId ?? '');
          const endpoints = link ? [link.sourceId, link.targetId].map(id => nodes.get(id)?.title || id).join(' ') : '';
          return !conflictFilter || `${nodes.get(issue.nodeId ?? '')?.title ?? ''} ${link?.label ?? ''} ${endpoints} ${issue.message} ${issue.nodeId ?? ''} ${issue.linkId ?? ''}`.toLowerCase().includes(conflictFilter.toLowerCase());
        });
        for (const issue of matches.slice(0, conflictsShown)) {
          const row = make('li');
          if (issue.nodeId) row.dataset.ruleNodeId = issue.nodeId;
          if (issue.linkId) row.dataset.ruleLinkId = issue.linkId;
          const affected = nodes.get(issue.nodeId ?? ''), link = links.get(issue.linkId ?? '');
          const endpoints = link ? [link.sourceId, link.targetId].map(id => nodes.get(id)?.title || id).join(' → ') : '';
          row.append(make('strong', '', affected?.title || (link ? link.label || endpoints : 'Work')), make('p', '', issue.message));
          if (affected && /Node kind is excluded/.test(issue.message)) row.append(make('small', '', `${nodeLabels[affected.kind]} is not permitted by the proposed inherited allowance.`));
          if (affected?.operation && /Joint operation is excluded/.test(issue.message)) row.append(make('small', '', `${labelFor('operations', affected.operation)} is not permitted by the proposed inherited allowance.`));
          if (affected?.rules && /cannot widen/.test(issue.message)) {
            const after = affected.id === node.id ? limits : report.affected.find(item => item.nodeId === affected.id)?.after;
            if (after) for (const key of RULE_KEYS) {
              const disallowed = (affected.rules[key] ?? []).filter(value => !after[key].includes(value as never));
              if (disallowed.length) row.append(make('p', '', `${categoryLabels[key]} still declared here: ${labelsFor(key, disallowed)}. Narrow this nested rule or keep those choices allowed above it.`));
            }
          }
          if (link) row.append(make('small', '', `${endpoints} · ${labelFor('linkKinds', link.kind)} connection`));
          list.append(row);
        }
        results.append(list);
        if (!matches.length) results.append(make('p', 'muted', 'No matching conflicts.'));
        if (matches.length > conflictsShown) results.append(button(`Show more conflicts (${matches.length - conflictsShown} remaining)`, () => { conflictsShown += 60; renderConflicts(); }, 'secondary'));
        }
        search.oninput = () => { conflictFilter = search.value; conflictsShown = 60; renderConflicts(); };
        conflictSection.append(search, results); renderConflicts();
      }
      preview.append(conflictSection, make('h3', '', 'Affected descendants'));
      if (report.affected.length) {
        const search = make('input', 'rule-search'); search.type = 'search'; search.placeholder = 'Find a descendant…'; search.value = filter; search.setAttribute('aria-label', 'Find affected descendants');
        search.oninput = () => { filter = search.value; shown = 60; renderAffected(report); }; preview.append(search);
      }
      renderAffected(report);
    } catch (failure) { apply.disabled = true; error.textContent = failure instanceof Error ? failure.message : String(failure); }
  }
  function renderAffected(report: ReturnType<typeof previewRuleChange>): void {
    preview.querySelector('.rule-affected')?.remove();
    const container = make('div', 'rule-affected');
    const rows = report.affected.filter(item => !filter || `${item.title} ${item.kind} ${item.nodeId}`.toLowerCase().includes(filter.toLowerCase()));
    if (!rows.length) container.append(make('p', 'muted', report.affected.length ? 'No matching descendants.' : 'No descendant permissions change.'));
    const list = make('ul', 'rule-impact-list');
    for (const item of rows.slice(0, shown)) {
      const row = make('li'); row.dataset.ruleNodeId = item.nodeId;
      const name = make('div', 'rule-row-title'); name.append(make('strong', '', item.title || nodeLabels[item.kind]), make('span', 'muted', `${nodeLabels[item.kind]}${item.hidden ? ' · Hidden' : ''}`)); row.append(name);
      for (const change of item.changes) {
        row.append(make('p', 'rule-change-label', categoryLabels[change.key]));
        if (change.removed.length) row.append(make('p', 'rule-removed', `Removed: ${labelsFor(change.key, change.removed)}`));
        if (change.added.length) row.append(make('p', 'rule-added', `Added: ${labelsFor(change.key, change.added)}`));
      }
      const compare = make('details'); compare.append(make('summary', '', 'Compare permissions'));
      for (const key of RULE_KEYS) compare.append(make('p', '', `${categoryLabels[key]} before: ${labelsFor(key, item.before[key])}`), make('p', '', `After: ${labelsFor(key, item.after[key])}`));
      compare.append(make('small', '', item.nodeId)); row.append(compare); list.append(row);
    }
    container.append(list);
    if (rows.length > shown) container.append(button(`Show more (${rows.length - shown} remaining)`, () => { shown += 60; renderAffected(report); }, 'secondary'));
    preview.append(container);
  }
  dialog.addEventListener('close', () => dialog.remove()); document.body.append(dialog); refresh(); dialog.showModal();
}
