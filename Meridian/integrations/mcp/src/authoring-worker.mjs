import { applyRecipe, inspectProject } from '../../../src/core/authoring.ts';
import { newProject, commitSnapshots } from '../../../src/core/model.ts';
import { isProjectShape, validateProject } from '../../../src/core/rules.ts';
import { navigationIndex } from '../../../src/core/navigation.ts';
import { reflowSmartLinks, smartLinkGeometries } from '../../../src/core/smart-links.ts';
import { isEnclosingContainer, resolveGridCell } from '../../../src/core/cells.ts';
import { createShapeBoundsIndex, nodeShape, shapeAnchor, shapesOverlap } from '../../../src/core/geometry.ts';
import { NODE_STYLE_KEYS, CANVAS_STYLE_KEYS, LINK_STYLE_KEYS } from '../../../src/core/appearance.ts';
import { inputPorts } from '../../../src/core/operations.ts';
import { readFile, writeFile } from 'node:fs/promises';
import { buildPublication } from '../../../src/publication.ts';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { StringDecoder } from 'node:string_decoder';
import { windowParents, windowSurface } from '../../../src/window-scene.ts';

const routing = node => ['junction', 'joint'].includes(node.kind);
export const CAPTURE_STAGES = Object.freeze(['build-publication', 'write-publication', 'launch-browser', 'create-page', 'configure-page', 'navigate-preview', 'choose-work', 'focus-node', 'fit-view', 'adjust-scale', 'wait-media', 'screenshot', 'inspect-nodes']);
export function captureFailureCode(error) {
  const message = typeof error?.message === 'string' ? error.message : '';
  const network = /net::(ERR_[A-Z0-9_]{1,60})\b/.exec(message);
  if (network) return network[1];
  if (/Executable (?:doesn't|does not) exist/i.test(message)) return 'EXECUTABLE_MISSING';
  if (error?.name === 'TimeoutError' || /Timeout \d+ms exceeded/.test(message)) return 'TIMEOUT';
  if (/browser (?:has been|was|is) closed|Target page, context or browser has been closed/i.test(message)) return 'BROWSER_CLOSED';
  if (/spawn EINVAL/.test(message)) return 'SPAWN_EINVAL';
  return 'BROWSER_ERROR';
}
const shapeValid = project => { if (!isProjectShape(project)) throw new Error('Invalid Meridian project shape.'); return project; };
const boxIntersects = (a, b) => a.x < b.x + b.width - .75 && a.x + a.width > b.x + .75 && a.y < b.y + b.height - .75 && a.y + a.height > b.y + .75;
const intersectBoxes = (a, b) => {
  const x = Math.max(a.x, b.x), y = Math.max(a.y, b.y), right = Math.min(a.x + a.width, b.x + b.width), bottom = Math.min(a.y + a.height, b.y + b.height);
  return right > x && bottom > y ? { x, y, width: right - x, height: bottom - y } : null;
};
const clipSegment = (a, b, box) => {
  let lo = 0, hi = 1; const dx = b.x - a.x, dy = b.y - a.y;
  for (const [p, q] of [[-dx, a.x - box.x], [dx, box.x + box.width - a.x], [-dy, a.y - box.y], [dy, box.y + box.height - a.y]]) {
    if (Math.abs(p) < 1e-12) { if (q < 0) return null; }
    else { const at = q / p; if (p < 0) lo = Math.max(lo, at); else hi = Math.min(hi, at); if (lo > hi) return null; }
  }
  return [{ x: a.x + lo * dx, y: a.y + lo * dy }, { x: a.x + hi * dx, y: a.y + hi * dy }];
};
const segmentHits = (a, b, box) => {
  const left = box.x + .75, right = box.x + box.width - .75, top = box.y + .75, bottom = box.y + box.height - .75;
  if (right <= left || bottom <= top) return false;
  const dx = b.x - a.x, dy = b.y - a.y;
  let lo = 0, hi = 1;
  for (const [p, q] of [[-dx, a.x - left], [dx, right - a.x], [-dy, a.y - top], [dy, bottom - a.y]]) {
    if (Math.abs(p) < 1e-12) { if (q < 0) return false; }
    else { const ratio = q / p; if (p < 0) lo = Math.max(lo, ratio); else hi = Math.min(hi, ratio); if (lo > hi) return false; }
  }
  return hi > lo + 1e-8;
};

export function geometryDiagnostics(source, workId) {
  const project = structuredClone(source);
  for (const node of project.nodes) if (node.canvasId === workId) {
    if (node.collapsed && node.containerSizes?.expanded) Object.assign(node, node.containerSizes.expanded);
    node.collapsed = false;
  }
  // Disclosure is a layout event for managed chains. Check the expanded
  // presentation the shared viewer would produce, not saved compact positions.
  reflowSmartLinks(project, workId);
  const nav = navigationIndex(project), diagnostics = [], seen = new Set();
  const windows = new Map(), visibleBounds = new Map();
  const windowClip = parent => {
    if (!windows.has(parent.id)) { const { geometry, width, height } = windowSurface(parent, project); windows.set(parent.id, { x: geometry.left, y: geometry.top, width, height }); }
    return windows.get(parent.id);
  };
  const presentedBounds = node => {
    if (visibleBounds.has(node.id)) return visibleBounds.get(node.id);
    let bounds = nav.bounds(node);
    // Match the shared renderer at initial scroll offsets. windowParents stops
    // at an exterior tray, so escaped branches are not clipped to source cells.
    for (const parent of windowParents(nav, node.id)) { bounds = intersectBoxes(bounds, windowClip(parent)); if (!bounds) break; }
    // Exterior trays themselves hide when their source tile is clipped away.
    if (bounds && nav.ancestors(node.id).some(parent => parent.expansionTray && !isEnclosingContainer(parent) && !presentedBounds(parent))) bounds = null;
    visibleBounds.set(node.id, bounds); return bounds;
  };
  const boxes = project.nodes.filter(n => n.canvasId === workId && n.kind !== 'canvas' && !routing(n) && nav.isNodeVisible(n.id))
    .flatMap(node => { const bounds = presentedBounds(node); return bounds ? [{ ...nodeShape(node, bounds), id: node.id, node, fullShape: nodeShape(node, nav.bounds(node)) }] : []; });
  const query = createShapeBoundsIndex(boxes);
  // Routing utilities are excluded as obstacle surfaces, but their endpoint
  // ancestry still determines which enclosing container a route belongs to.
  const ancestors = new Map(project.nodes.filter(node => node.canvasId === workId)
    .map(node => [node.id, new Set(nav.ancestors(node.id).map(n => n.id))]));
  const withinAncestor = (a, b) => (ancestors.get(a.id)?.has(b.id) && isEnclosingContainer(b.node)) || (ancestors.get(b.id)?.has(a.id) && isEnclosingContainer(a.node));
  const add = issue => { if (diagnostics.length < 500) diagnostics.push(issue); };
  overlapChecks: for (const box of boxes) for (const other of query(box)) {
    const pair = [box.id, other.id].sort().join(':');
    if (box.id === other.id || seen.has(pair)) continue;
    seen.add(pair);
    if (!withinAncestor(box, other) && boxIntersects(box, other) && shapesOverlap(box.fullShape, other.fullShape)) add({ code: 'expanded-node-overlap', severity: 'error', workId, nodeIds: [box.id, other.id], message: 'These unrelated node surfaces overlap when expanded. Move the branches or change their frames.' });
    if (diagnostics.length >= 500) break overlapChecks;
  }
  const allNodes = new Map(project.nodes.map(node => [node.id, node]));
  const point = (node, anchor, toward) => {
    const box = nav.bounds(node), center = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
    if (node.kind === 'junction') return center;
    if (anchor) {
      const sides = [{ distance: anchor.x, x: 0, y: anchor.y }, { distance: 1 - anchor.x, x: 1, y: anchor.y }, { distance: anchor.y, x: anchor.x, y: 0 }, { distance: 1 - anchor.y, x: anchor.x, y: 1 }].sort((a, b) => a.distance - b.distance);
      return shapeAnchor(nodeShape(node, box), (sides[0].x - .5) * box.width, (sides[0].y - .5) * box.height);
    }
    return shapeAnchor(nodeShape(node, box), toward.x - center.x, toward.y - center.y);
  };
  routeChecks: for (const link of project.links.filter(link => link.canvasId === workId && nav.isLinkVisible(link))) {
    if (diagnostics.length >= 500) break;
    const sourceNode = allNodes.get(link.sourceId), targetNode = allNodes.get(link.targetId);
    if (!sourceNode || !targetNode || sourceNode.id === targetNode.id) continue;
    if (!presentedBounds(sourceNode) || !presentedBounds(targetNode)) continue;
    const sourceWindows = windowParents(nav, sourceNode.id), targetWindows = new Set(windowParents(nav, targetNode.id).map(parent => parent.id));
    const commonWindow = sourceWindows.find(parent => targetWindows.has(parent.id));
    let routeClip;
    if (commonWindow) for (const parent of [commonWindow, ...windowParents(nav, commonWindow.id)]) { routeClip = routeClip === undefined ? windowClip(parent) : intersectBoxes(routeClip, windowClip(parent)); if (!routeClip) break; }
    if (routeClip === null) continue;
    const sourceBox = nav.bounds(sourceNode), targetBox = nav.bounds(targetNode);
    const start = point(sourceNode, link.sourceAnchor, { x: targetBox.x + targetBox.width / 2, y: targetBox.y + targetBox.height / 2 });
    const ports = inputPorts(targetNode);
    const targetAnchor = link.targetAnchor ?? (link.kind !== 'visual' && ports.length ? { x: 0, y: ((link.targetPort ?? 0) + 1) / (ports.length + 1) } : undefined);
    const end = point(targetNode, targetAnchor, { x: sourceBox.x + sourceBox.width / 2, y: sourceBox.y + sourceBox.height / 2 });
    const points = [start, ...(link.points ?? []), end], struck = new Set();
    for (let i = 1; i < points.length; i++) {
      const segment = routeClip ? clipSegment(points[i - 1], points[i], routeClip) : [points[i - 1], points[i]];
      if (!segment) continue;
      const [a, b] = segment;
      const region = { x: Math.min(a.x, b.x) - 1, y: Math.min(a.y, b.y) - 1, width: Math.abs(a.x - b.x) + 2, height: Math.abs(a.y - b.y) + 2 };
      for (const box of query(region)) {
        if (box.id === link.sourceId || box.id === link.targetId || struck.has(box.id)) continue;
        if (isEnclosingContainer(box.node) && (ancestors.get(sourceNode.id)?.has(box.id) || ancestors.get(targetNode.id)?.has(box.id))) continue;
        if (segmentHits(a, b, box)) { struck.add(box.id); add({ code: 'link-crosses-node', severity: 'error', workId, nodeId: box.id, linkId: link.id, message: 'This link crosses an unrelated node body. Reroute it with saved joints or move the branch.' }); }
        if (diagnostics.length >= 500) break routeChecks;
      }
    }
  }
  let checkedSmartLinks = 0, checkedSmartSegments = 0;
  if (diagnostics.length < 500) smartRouteChecks: for (const [railId, geometry] of smartLinkGeometries(project, workId, {}, nav)) {
    const rail = allNodes.get(railId), struck = new Set();
    const ignored = new Set([railId, ...ancestors.get(railId) ?? []]);
    let routeClip;
    for (const parent of windowParents(nav, railId)) { routeClip = routeClip === undefined ? windowClip(parent) : intersectBoxes(routeClip, windowClip(parent)); if (!routeClip) break; }
    if (routeClip === null || !presentedBounds(rail)) continue;
    checkedSmartLinks++;
    if (geometry.blockedNodeIds?.length) add({ code: 'smart-link-blocked', severity: 'error', workId, nodeId: railId,
      nodeIds: geometry.blockedNodeIds, message: 'This Smart Link cannot reach all members safely. Move its handle clear of the reported nodes.' });
    for (const segment of geometry.segments) {
      checkedSmartSegments++;
      const clipped = routeClip ? clipSegment(segment.from, segment.to, routeClip) : [segment.from, segment.to];
      if (!clipped) continue;
      const [a, b] = clipped, target = segment.nodeId ? allNodes.get(segment.nodeId) : undefined;
      const region = { x: Math.min(a.x, b.x) - 1, y: Math.min(a.y, b.y) - 1, width: Math.abs(a.x - b.x) + 2, height: Math.abs(a.y - b.y) + 2 };
      for (const box of query(region)) {
        if (ignored.has(box.id) || struck.has(box.id) || box.id === target?.id || target && ancestors.get(target.id)?.has(box.id)) continue;
        // A bounded target's clipped interior belongs to the endpoint. Escaped
        // exterior trays have no such presentation ancestor and remain checked.
        if (target && isEnclosingContainer(target) && nav.presentationAncestors(box.id).some(parent => parent.id === target.id)) continue;
        if (segmentHits(a, b, box)) { struck.add(box.id); add({ code: 'smart-link-crosses-node', severity: 'error', workId,
          nodeId: box.id, nodeIds: [railId, ...(target ? [target.id] : [])], message: 'This Smart Link segment crosses an unrelated node body. Move its handle or branch clear.' }); }
        if (diagnostics.length >= 500) break smartRouteChecks;
      }
    }
    if (diagnostics.length >= 500) break;
  }
  return { diagnostics, checkedNodes: boxes.length, checkedSmartLinks, checkedSmartSegments, diagnosticLimitReached: diagnostics.length >= 500,
    scope: 'Expanded effective surfaces after Smart Link reflow at initial Window scroll offsets; ordinary and Smart Link routes are checked. Fully clipped rows and hidden-source trays are excluded, visible portions and exterior trays are checked. Intentional enclosing ancestors are exempt. Gallery pages are mutually exclusive; curved self-links and browser text clipping require visual review.' };
}

export async function executeWorker(input) {
  const { command, project, workId } = input;
  if (command === 'schema') return {
    version: 1, compatibilityNames: 'Recipes retain canvases/canvasId. The product term is Work.',
    recipeFields: ['version', 'bindings', 'canvases', 'nodes', 'assets', 'links', 'routes', 'layouts', 'remove'],
    nodeKinds: ['media', 'container', 'spreadsheet', 'module', 'text', 'image', 'document', 'input', 'number', 'switch', 'output', 'joint', 'junction', 'reference', 'canvas-link'],
    nodeFields: ['key', 'existingId', 'unset', 'canvas', 'parent', 'kind', 'title', 'tag', 'showTitle', 'contentItems', 'smartLink', 'x', 'y', 'width', 'height', 'sizeMode', 'positionLocked', 'utilityVariant', 'contentType', 'text', 'titleRuns', 'textRuns', 'asset', 'backgroundAsset', 'attachments', 'hyperlinks', 'target', 'canvasTarget', 'containerVariant', 'childLayout', 'childArrangement', 'childPosition', 'childOffset', 'collapsed', 'containerSizes', 'expansionTray', 'placement', 'attachSide', 'grid', 'cell', 'style', 'value', 'valueType', 'operation', 'dependencies', 'remoteLinks', 'rules', 'snapping'],
    smartLinks: { role: "A Utility node (kind:'junction', utilityVariant:'joint') with smartLink:{direction:'auto'|'right'|'down'|'left'|'up',gap:48}. Immediate children are its ordered members, in project node order. The member chain spreads in both directions along the rail axis around its center. Parent-owned chains center on their parent along that axis: right/left rails use both horizontal sides while children grow below/above; down/up rails use both vertical sides while children grow right/left. Expanded visible footprints reserve space, hidden descendants do not. Position locks and obstacles may limit exact symmetry. This is visual layout, not functional wiring.", fields: ['direction', 'gap', 'resolvedDirection'], gapRange: [8, 10000], persistence: 'Membership uses ordinary parent IDs, so copies/Modules/publications retain the graph. Preserve normal links for semantic relationships. Centering is runtime layout behavior and needs no new stored field.' },
    nodeHub: { titleVisibility: 'showTitle defaults true and never erases title.', firstSlot: 'Only contentItems[0] appears on the Work. Remaining items belong to the Node Hub. [] explicitly has no display item. Omission retains legacy content.', itemTypes: [{ id: 'summary', type: 'text', text: 'Concise visible content' }, { id: 'portrait', type: 'asset', assetId: 'asset alias or existing ID' }, { id: 'source', type: 'link', url: 'https://example.com/', label: 'Source' }], identity: 'Keep each item id stable when editing/reordering; assetId resolves a recipe asset alias. Text optionally supports textRuns. Maximum 1000 items per node.' },
    linkFields: ['key', 'existingId', 'canvas', 'parent', 'source', 'target', 'kind', 'relation', 'direction', 'points', 'sourceAnchor', 'targetAnchor', 'label', 'style'],
    styles: { node: NODE_STYLE_KEYS, work: CANVAS_STYLE_KEYS, link: LINK_STYLE_KEYS },
    nodeDefaults: { style: { textLayout: 'balanced', textAlign: 'center', fontSize: 22, titleSize: 32 }, sizing: 'New automatic text frames use about 65% vertical text composition and fit a complete visible title. Explicit typography, manual frames and allocated cells remain authoritative. Existing unmarked authored nodes retain previous presentation. Hover magnification is transient viewer behavior, never saved geometry.' },
    layoutKinds: ['row', 'column', 'grid', 'radial'],
    containerPatterns: ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'gallery', 'window'],
    updateContract: 'Use existingId or returned bindings to retain identity. Omitted fields stay unchanged. Styles merge; unset explicitly resets fields. remove.nodes deletes the selected branch; unrelated Works and existing assets cannot change.',
    limitations: ['Static row/column/grid/radial layouts do not reserve dynamic subtrees; Smart Links reserve current visible branches.', 'Validation cannot establish aesthetic quality. Review both an overview and readable detail.'],
  };
  if (command === 'new') return newProject(input.title);
  shapeValid(project);
  if (command === 'apply') return applyRecipe(project, input.recipe);
  if (command === 'inspect') {
    const nav = navigationIndex(project);
    const works = project.canvases.map(work => ({ ...work }));
    const entity = input.entity ?? 'nodes', query = (input.query ?? '').toLocaleLowerCase(), offset = input.offset ?? 0, limit = input.limit ?? 25;
    let items;
    if (entity === 'works') items = works;
    else if (entity === 'links') items = project.links.filter(link => !workId || link.canvasId === workId).map(link => ({ ...link }));
    else if (entity === 'assets') items = project.assets.map(asset => ({ id: asset.id, name: asset.name, mime: asset.mime, bytes: Math.floor(asset.base64.length * 3 / 4) }));
    else items = project.nodes.filter(node => (!workId || node.canvasId === workId) && (!input.nodeId || node.id === input.nodeId));
    items = items.filter(item => !query || [item.id, item.title, item.tag, item.name, item.label].some(value => typeof value === 'string' && value.toLocaleLowerCase().includes(query)));
    const page = []; let pageBytes = 0;
    for (const item of items.slice(offset, offset + limit)) {
      let value = entity === 'nodes' ? { ...item, effectiveBounds: nav.bounds(item), childCount: nav.contents(item.id).nodes.length, visible: nav.isNodeVisible(item.id) } : item;
      if (Buffer.byteLength(JSON.stringify(value)) > 64 * 1024) {
        const truncatedFields = [];
        const bounded = (field, path = '') => {
          if (typeof field === 'string' && field.length > 8000) { truncatedFields.push({ path, originalLength: field.length }); return field.slice(0, 8000); }
          if (Array.isArray(field)) { if (field.length > 100) truncatedFields.push({ path, originalLength: field.length }); return field.slice(0, 100).map((entry, index) => bounded(entry, `${path}[${index}]`)); }
          if (field && typeof field === 'object') return Object.fromEntries(Object.entries(field).map(([key, entry]) => [key, bounded(entry, path ? `${path}.${key}` : key)]));
          return field;
        };
        value = bounded(value);
        value = { ...value, truncatedFields, inspectionNote: 'These fields are previews. Preserve omitted fields in recipes; do not replace long content with this partial text.' };
      }
      const bytes = Buffer.byteLength(JSON.stringify(value));
      if (page.length && pageBytes + bytes > 512 * 1024) break;
      page.push(value); pageBytes += bytes;
    }
    return { project: { id: project.id, title: project.title, revision: project.revision }, workId, entity,
      items: page, nextOffset: offset + page.length < items.length ? offset + page.length : null, total: items.length };
  }
  if (command === 'validate') {
    const report = inspectProject(project), geometry = geometryDiagnostics(project, workId);
    const diagnostics = report.warnings.filter(issue => !issue.canvasId || issue.canvasId === workId).map(issue => ({ ...issue }));
    diagnostics.push(...geometry.diagnostics);
    for (const node of project.nodes.filter(node => node.canvasId === workId && !routing(node) && node.kind !== 'canvas')) {
      if ((node.title ?? '').split(/\s+/).length > 12) diagnostics.push({ code: 'long-label', severity: 'warning', nodeId: node.id, message: 'This title is long. Prefer the object identifier or a short descriptive label.' });
      if (/\b(?:how to (?:read|navigate)|choose a task|\d+ (?:nodes|groups|subgroups)|complete (?:type )?index|download the complete)\b/i.test(`${node.title}\n${node.text ?? ''}`)) diagnostics.push({ code: 'visible-meta-content', severity: 'warning', nodeId: node.id, message: 'Visible content appears to describe the Work or its navigation. Keep that material in supporting metadata or documents.' });
    }
    return { validProject: validateProject(project).length === 0, diagnostics: diagnostics.slice(0, 600), diagnosticLimitReached: diagnostics.length > 600 || geometry.diagnosticLimitReached,
      geometry: { ...geometry, diagnostics: undefined }, scope: { workId }, warningsAreAdvisory: true };
  }
  if (command === 'commit') {
    const saved = commitSnapshots(project);
    const requiredSources = new Set();
    for (const reference of project.nodes.filter(node => node.canvasId === workId && node.kind === 'reference' && node.targetId)) {
      requiredSources.add(reference.targetId);
      if (reference.targetCell) {
        const cell = resolveGridCell(project, reference.targetId, reference.targetCell);
        if (cell) requiredSources.add(cell.id);
      }
    }
    for (const [id, snapshot] of Object.entries(project.snapshots)) if (snapshot.node.canvasId !== workId) saved.snapshots[id] = snapshot;
    for (const [id, snapshot] of Object.entries(saved.snapshots)) if (snapshot.node.canvasId !== workId && !Object.hasOwn(project.snapshots, id) && !requiredSources.has(id)) delete saved.snapshots[id];
    return saved;
  }
  if (command === 'publication') {
    const root = input.viewerAssetsRoot ?? resolve(fileURLToPath(new URL('../../../', import.meta.url)), 'output', 'app', 'wwwroot');
    const [js, css] = await Promise.all(['viewer.js', 'scene.css'].map(file => readFile(resolve(root, file), 'utf8')));
    return { html: buildPublication(project, js, css) };
  }
  if (command === 'capture') {
    let captureStage = 'build-publication';
    try {
    const { html } = await executeWorker({ ...input, command: 'publication' });
    const path = resolve(input.directory, 'preview.html');
    captureStage = 'write-publication';
    await writeFile(path, html, { mode: 0o600 });
    const { chromium } = await import('@playwright/test');
    captureStage = 'launch-browser';
    const browser = await chromium.launch({ headless: true, ...(input.browserExecutablePath ? { executablePath: input.browserExecutablePath } : {}) });
    captureStage = 'create-page';
    const page = await browser.newPage({ viewport: { width: 1600, height: 1000 }, deviceScaleFactor: 1 });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    captureStage = 'configure-page';
    await page.route(/^https?:/, route => route.abort());
    try {
      captureStage = 'navigate-preview';
      await page.goto(pathToFileURL(path).href);
      captureStage = 'choose-work';
      await page.getByLabel('Choose Work', { exact: true }).selectOption(workId);
      if (input.nodeId) {
        captureStage = 'focus-node';
        await page.getByRole('button', { name: 'Explore project', exact: true }).click();
        await page.getByRole('searchbox', { name: 'Search project', exact: true }).fill(input.nodeId);
        await page.locator('[data-navigation-node]').evaluateAll((items, id) => {
          const item = items.find(element => element.getAttribute('data-navigation-node') === id);
          if (!(item instanceof HTMLButtonElement)) throw new Error('Focused node is absent from the published outline.');
          item.click();
        }, input.nodeId);
        await page.getByRole('button', { name: 'Close project navigation', exact: true }).click();
        captureStage = 'fit-view';
        if (input.scope === 'branch') await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click();
      } else { captureStage = 'fit-view'; await page.getByRole('button', { name: 'Fit Work', exact: true }).click(); }
      if (input.scale !== undefined) {
        captureStage = 'adjust-scale';
        const current = await page.locator('.ms-world').evaluate(world => Number(getComputedStyle(world).getPropertyValue('--ms-camera-scale')));
        const viewport = await page.locator('.ms-viewport').boundingBox();
        await page.mouse.move(viewport.x + viewport.width / 2, viewport.y + viewport.height / 2);
        await page.mouse.wheel(0, -Math.log(input.scale / current) / 0.0013);
        await page.waitForFunction(scale => {
          const value = Number(getComputedStyle(document.querySelector('.ms-world')).getPropertyValue('--ms-camera-scale'));
          return Math.abs(value - scale) < Math.max(1e-8, scale * .0001);
        }, input.scale);
      }
      captureStage = 'wait-media';
      await page.evaluate(async () => { await document.fonts.ready; await Promise.all([...document.images].map(image => image.decode().catch(() => {}))); });
      const screenshot = 'preview.png';
      captureStage = 'screenshot';
      await page.screenshot({ path: resolve(input.directory, screenshot) });
      captureStage = 'inspect-nodes';
      const nodes = await page.locator('.ms-node,.ms-gallery-item').evaluateAll(cards => cards.map(card => {
        const title = card.querySelector('.ms-node-title,.ms-gallery-item-title');
        const body = card.querySelector('.ms-node-body') ?? card;
        return { id: card.getAttribute('data-node-id') ?? card.getAttribute('data-gallery-item-id'),
          titleClipped: !!title && (title.scrollWidth > title.clientWidth + 2 || title.scrollHeight > title.clientHeight + 2),
          bodyRequiresScrolling: body.scrollHeight > body.clientHeight + 2 || body.scrollWidth > body.clientWidth + 2,
          clippedContent: [...card.querySelectorAll('.ms-text,.ms-caption,.ms-text-preview')].filter(element => element.scrollHeight > element.clientHeight + 2 || element.scrollWidth > element.clientWidth + 2).map(element => element.tagName),
        };
      }));
      if (errors.length) throw new Error('The shared viewer reported a browser error.');
      return { browserErrors: errors, canvases: [{ id: workId, screenshot, nodes }] };
    } finally { await browser.close(); }
    } catch (error) {
      if (error && typeof error === 'object' && Object.isExtensible(error)) {
        Object.defineProperty(error, 'workerStage', { value: captureStage, configurable: true });
        Object.defineProperty(error, 'previewDiagnosticCode', { value: captureFailureCode(error), configurable: true });
      }
      throw error;
    }
  }
  throw new Error('Unsupported internal authoring operation.');
}

if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) {
  let bytes = 0, text = ''; const decoder = new StringDecoder('utf8');
  for await (const chunk of process.stdin) { bytes += chunk.length; if (bytes > 100 * 1024 * 1024) throw new Error('Worker input is oversized.'); text += decoder.write(chunk); }
  text += decoder.end();
  try { process.stdout.write(JSON.stringify({ ok: true, value: await executeWorker(JSON.parse(text)) })); }
  catch (error) { process.stdout.write(JSON.stringify({ ok: false, error: { code: 'authoring-rejected', message: error.message, diagnostics: error.diagnostics ?? [],
    ...(CAPTURE_STAGES.includes(error.workerStage) ? { workerStage: error.workerStage, previewDiagnosticCode: captureFailureCode(error) } : {}) } })); process.exitCode = 1; }
}
