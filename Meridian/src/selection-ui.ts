import type { MeridianProject } from './core/types.ts';
import { createSnippet } from './core/selection.ts';
import { nodeTag } from './core/node-tag.ts';
import { nodeKindLabel } from './core/work-terminology.ts';

export interface SnippetDialogOptions {
  project: MeridianProject;
  selectedIds: readonly string[];
  sourceNote?: string;
  download: (project: MeridianProject, includeExternalReferences: boolean) => Promise<void> | void;
}

/** Shared editor/viewer preflight. Selection boundaries are choices, not hidden export policy. */
export function showSnippetDialog(options: SnippetDialogOptions): void {
  const element = <K extends keyof HTMLElementTagNameMap>(tag: K, text = '') => {
    const node = document.createElement(tag); node.textContent = text; return node;
  };
  if (!document.getElementById('meridian-selection-dialog-style')) {
    const style = element('style'); style.id = 'meridian-selection-dialog-style';
    style.textContent = `.ms-selection-dialog{box-sizing:border-box;width:min(640px,94vw);max-height:90dvh;padding:0;background:#17232f;color:#e1ecf2;border:1px solid #4a6071;border-radius:12px;font:14px/1.55 Inter,Segoe UI,system-ui,sans-serif;box-shadow:0 24px 80px #0007}.ms-selection-dialog::backdrop{background:#08111bcc}.ms-selection-dialog header,.ms-selection-dialog footer{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:17px 22px;border-bottom:1px solid #35495b}.ms-selection-dialog footer{border:0;border-top:1px solid #35495b;justify-content:flex-end}.ms-selection-dialog h2{font-size:19px;margin:0}.ms-selection-dialog .ms-selection-body{padding:18px 22px;max-height:65dvh;overflow:auto}.ms-selection-dialog p{margin:0 0 13px}.ms-selection-dialog label{display:flex;gap:10px;align-items:center;margin:14px 0}.ms-selection-dialog .ms-selection-name{display:grid;gap:6px}.ms-selection-dialog input[type=text]{box-sizing:border-box;width:100%;padding:9px;border:1px solid #4a6071;border-radius:6px;background:#101923;color:inherit;font:inherit}.ms-selection-dialog input[type=checkbox]{width:17px;height:17px;accent-color:#7bddc0}.ms-selection-dialog button{font:inherit;color:inherit;background:#263848;border:1px solid #455a6b;border-radius:6px;padding:8px 13px;cursor:pointer}.ms-selection-dialog button.primary{background:#7bddc0;color:#122d29}.ms-selection-dialog button:disabled{opacity:.5;cursor:default}.ms-selection-dialog button:focus-visible,.ms-selection-dialog input:focus-visible,.ms-selection-dialog summary:focus-visible{outline:2px solid #7bddc0;outline-offset:2px}.ms-selection-dialog details{padding:10px 0;border-top:1px solid #35495b}.ms-selection-dialog summary{cursor:pointer}.ms-selection-dialog ul{padding-left:21px;max-height:210px;overflow:auto}.ms-selection-dialog li{margin:7px 0;overflow-wrap:anywhere}.ms-selection-dialog small{color:#9fb2c3}.ms-selection-summary{padding:13px;background:#0f1a24;border-radius:7px;margin:16px 0}.ms-selection-error{color:#ffb2b2}`;
    document.head.append(style);
  }
  const dialog = element('dialog'); dialog.className = 'ms-selection-dialog'; dialog.setAttribute('aria-label', 'Download selection');
  const header = element('header'), title = element('h2', 'Download selection');
  const close = element('button', '×'); close.type = 'button'; close.setAttribute('aria-label', 'Close selection download'); close.onclick = () => dialog.close();
  header.append(title, close);
  const body = element('div'); body.className = 'ms-selection-body';
  body.append(element('p', 'Create an independent Work from these nodes. The source Work stays unchanged.'));
  if (options.sourceNote) body.append(element('p', options.sourceNote));
  const name = element('input'); name.type = 'text'; name.value = `${options.project.title} · Selection`; name.maxLength = 300;
  const nameLabel = element('label', 'Work name'); nameLabel.className = 'ms-selection-name'; nameLabel.append(name); body.append(nameLabel);
  const descendants = element('input'); descendants.type = 'checkbox'; descendants.checked = true;
  const childrenLabel = element('label', 'Include contained children, including hidden children'); childrenLabel.prepend(descendants); body.append(childrenLabel);
  const external = element('input'); external.type = 'checkbox'; external.checked = false;
  const externalLabel = element('label', 'Download External References?'); externalLabel.prepend(external); body.append(externalLabel);
  body.append(element('small', 'External inclusion uses available saved observations. It does not fetch or reconstruct another Work.'));
  const summary = element('div'); summary.className = 'ms-selection-summary'; summary.setAttribute('role', 'status'); body.append(summary);
  const contents = element('div'); body.append(contents);
  const boundary = element('p', 'Only connections with both endpoints in the copy are included. If their owner is outside the copy, they belong to the new Work. Nodes and connection routes keep their authored coordinates and relative spacing.'); body.append(boundary);
  const error = element('p'); error.className = 'ms-selection-error'; error.setAttribute('role', 'alert'); body.append(error);
  const footer = element('footer'), cancel = element('button', 'Cancel'), download = element('button', 'Download snippet');
  cancel.type = download.type = 'button'; cancel.onclick = () => dialog.close(); download.className = 'primary'; footer.append(cancel, download);
  dialog.append(header, body, footer); document.body.append(dialog);
  let candidate: ReturnType<typeof createSnippet> | null = null;
  function preview(): void {
    candidate = null; error.textContent = ''; download.disabled = true; contents.replaceChildren();
    try {
      candidate = createSnippet(options.project, [...options.selectedIds], { includeDescendants: descendants.checked, includeExternalReferences: external.checked, title: name.value.trim() || 'Selection' });
      const r = candidate.report;
      const count = (value: number, singular: string, plural = singular + 's') => `${value} ${value === 1 ? singular : plural}`;
      summary.replaceChildren(element('strong', `${count(r.copiedNodes, 'node')} · ${count(r.copiedLinks, 'connection')} · ${count(r.copiedAssets, 'attachment')}`), element('p', `${count(r.includedDescendants, 'contained child', 'contained children')} included; ${r.omittedDescendants} omitted (${r.omittedHiddenDescendants} hidden). ${count(r.crossingLinks, 'connection')} crossing the selection omitted.`));
      if (r.reassignedLinkOwners) summary.append(element('small', `${r.reassignedLinkOwners} connections will belong to the new Work because their original owner is outside the selection.`));
      const included = element('details'); included.append(element('summary', 'Included nodes'));
      const list = element('ul'); for (const node of candidate.project.nodes.filter(n => n.kind !== 'canvas').slice(0, 80)) list.append(element('li', `${nodeTag(node)} · ${nodeKindLabel(node.kind)}`));
      if (r.copiedNodes > 80) list.append(element('li', `…and ${r.copiedNodes - 80} more nodes.`)); included.append(list); contents.append(included);
      if (r.externalRequirements.length) {
        const details = element('details'); details.open = true; details.append(element('summary', `External requirements · ${r.externalRequirements.length}`));
        const requirements = element('ul');
        for (const item of r.externalRequirements) { const row = element('li', `${item.title} — ${item.status === 'snapshot' ? 'saved observation included' : item.status === 'excluded' ? 'excluded' : 'unavailable'}`); row.append(element('br'), element('small', `${item.id} · ${item.note}`)); requirements.append(row); }
        details.append(requirements); contents.append(details);
      }
      if (r.missingAssets.length) contents.append(element('p', `Missing attachments: ${r.missingAssets.join(', ')}`));
      download.disabled = false;
    } catch (failure) { summary.textContent = 'This selection cannot be packaged yet.'; error.textContent = failure instanceof Error ? failure.message : String(failure); }
  }
  descendants.onchange = external.onchange = preview;
  download.onclick = async () => {
    if (!candidate) return;
    download.disabled = true; candidate.project.title = name.value.trim() || 'Selection';
    candidate.project.canvases[0]!.title = candidate.project.title;
    candidate.project.nodes.find(node => node.kind === 'canvas')!.title = candidate.project.title;
    try { await options.download(candidate.project, external.checked); dialog.close(); }
    catch (failure) { error.textContent = failure instanceof Error ? failure.message : String(failure); download.disabled = false; }
  };
  dialog.addEventListener('close', () => dialog.remove()); preview();
  const requiredCanvases = new Set(options.project.dependencies.filter(item => item.required).map(item => item.canvasId));
  // Only author-marked requirements touched by this selection set its initial policy.
  const initial = candidate as ReturnType<typeof createSnippet> | null;
  if (initial?.report.externalRequirements.some(item => requiredCanvases.has(item.id))) { external.checked = true; preview(); }
  dialog.showModal();
}
