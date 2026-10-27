/**
 * Deterministic, disclosure-stable geometry for an ordinary Meridian node tree.
 *
 * A vertical tree grows DOWN through generations and spreads siblings sideways.
 * A horizontal tree grows RIGHT through generations and spreads siblings vertically.
 * These are authored spatial relationships, not Horizontal/Vertical Display Patterns:
 * those patterns arrange equally sized children in a bounded row or column.
 *
 * Input: flat [{ key, parent?: key | null, width?, height?, scale? }]. Input order
 * determines root/sibling order. Width/height are the final world dimensions when
 * supplied; otherwise base dimensions shrink by depthScale, bounded by minScale.
 * Every subtree reserves its complete cross-axis footprint even when collapsed.
 * Consequently reopening never requires moving unrelated branches. A full census
 * may be broad; let the viewer zoom into detail rather than squeeze labels to fit.
 *
 * Result: nodes (world rectangles), routes (one direct ownership edge per child),
 * forest bounds, level lanes, and a serializable byKey dictionary. Routes contain
 * saved link.points and normalized endpoint anchors, so parent/child relations
 * can share an empty generation channel without crossing unrelated bodies.
 *
 * Native recipe fields for every authored node:
 *   kind: 'media', containerVariant: 'contained', childLayout: 'spatial',
 *   childArrangement: 'free', childPosition: 'manual', placement: 'free',
 *   sizeMode: 'fixed', parent: record.parent, ...rectangle
 *   style: {contentSizing:'fixed', ...authorStyle}
 * Set collapsed deliberately. Keep every branch outside a bounded Container;
 * nesting unbounded Media within one creates an exterior Expansion Tray instead.
 *
 * Mermaid/Figma, a new collection type, and product source edits are not involved.
 */

const positive = (value, label, allowZero = false) => {
  if (!Number.isFinite(value) || (allowZero ? value < 0 : value <= 0)) {
    throw new TypeError(`${label} must be a finite ${allowZero ? 'nonnegative' : 'positive'} number.`);
  }
  return value;
};
const finite = (value, label) => {
  if (!Number.isFinite(value)) throw new TypeError(`${label} must be finite.`);
  return value;
};
const envelope = boxes => {
  if (!boxes.length) return { x: 0, y: 0, width: 0, height: 0 };
  const x = Math.min(...boxes.map(box => box.x)), y = Math.min(...boxes.map(box => box.y));
  return { x, y, width: Math.max(...boxes.map(box => box.x + box.width)) - x,
    height: Math.max(...boxes.map(box => box.y + box.height)) - y };
};

export const PROGRAMMING_TREE_PRESETS = Object.freeze({
  namespaces: Object.freeze({ maxCrossSpan: 10000, rootWidth: 1200, rootHeight: 650, levelGap: 120, rootGap: 160, maxZoom: 1024, promoteBranches: .65, maxBranchEnlargement: 25, localGenerations: true }),
  classes: Object.freeze({ maxCrossSpan: 10000, rootWidth: 2000, rootHeight: 1800, levelGap: 120, rootGap: 160, maxZoom: 1024, promoteBranches: .65, maxBranchEnlargement: 25, localGenerations: true }),
});

export function computeTreeLayout(records, orientation = 'down', options = {}) {
  if (!Array.isArray(records)) throw new TypeError('records must be an array.');
  const direction = ({ down: 'down', vertical: 'down', right: 'right', horizontal: 'right' })[orientation];
  if (!direction) throw new TypeError("orientation must be 'down'/'vertical' or 'right'/'horizontal'.");
  const vertical = direction === 'down';
  const origin = { x: finite(options.origin?.x ?? 0, 'origin.x'), y: finite(options.origin?.y ?? 0, 'origin.y') };
  const baseWidth = positive(options.width ?? 360, 'width');
  const baseHeight = positive(options.height ?? 140, 'height');
  const depthScale = positive(options.depthScale ?? .88, 'depthScale');
  const minScale = positive(options.minScale ?? .42, 'minScale');
  if (depthScale > 1 || minScale > 1) throw new TypeError('depthScale and minScale must not exceed 1.');
  const siblingGap = positive(options.siblingGap ?? 80, 'siblingGap');
  const rootGap = positive(options.rootGap ?? siblingGap * 2, 'rootGap');
  const levelGap = positive(options.levelGap ?? 140, 'levelGap');
  const localGenerations = options.localGenerations ?? false;
  if (typeof localGenerations !== 'boolean') throw new TypeError('localGenerations must be Boolean.');
  const entries = new Map();
  for (const record of records) {
    if (!record || typeof record.key !== 'string' || !record.key.trim()) throw new TypeError('Each record needs a nonempty string key.');
    if (entries.has(record.key)) throw new TypeError(`Duplicate tree key '${record.key}'.`);
    if (record.parent !== undefined && record.parent !== null && typeof record.parent !== 'string') throw new TypeError(`Invalid parent for '${record.key}'.`);
    entries.set(record.key, { key: record.key, parent: record.parent ?? null, input: record, children: [] });
  }
  const roots = [];
  for (const entry of entries.values()) {
    if (entry.parent === null) roots.push(entry);
    else {
      const parent = entries.get(entry.parent);
      if (!parent) throw new TypeError(`Tree parent '${entry.parent}' for '${entry.key}' does not exist.`);
      parent.children.push(entry);
    }
  }
  // Visit all entries, including unreachable cycles, before any recursive layout.
  const visiting = new Set(), visited = new Set();
  const check = entry => {
    if (visiting.has(entry.key)) throw new TypeError(`Tree hierarchy contains a cycle at '${entry.key}'.`);
    if (visited.has(entry.key)) return;
    visiting.add(entry.key); for (const child of entry.children) check(child);
    visiting.delete(entry.key); visited.add(entry.key);
  };
  for (const entry of entries.values()) check(entry);
  const levels = [];
  const measure = (entry, depth) => {
    const scale = positive(entry.input.scale ?? Math.max(minScale, depthScale ** depth), `${entry.key}.scale`);
    entry.depth = depth; entry.scale = scale;
    entry.width = positive(entry.input.width ?? baseWidth * scale, `${entry.key}.width`);
    entry.height = positive(entry.input.height ?? baseHeight * scale, `${entry.key}.height`);
    entry.mainSize = vertical ? entry.height : entry.width;
    entry.crossSize = vertical ? entry.width : entry.height;
    levels[depth] = Math.max(levels[depth] ?? 0, entry.mainSize);
    for (const child of entry.children) measure(child, depth + 1);
    const childrenSpan = entry.children.reduce((sum, child) => sum + child.span, 0) + Math.max(0, entry.children.length - 1) * siblingGap;
    entry.span = Math.max(entry.crossSize, childrenSpan);
  };
  for (const root of roots) measure(root, 0);
  const mainStarts = []; let at = vertical ? origin.y : origin.x;
  for (const mainSize of levels) { mainStarts.push(at); at += mainSize + levelGap; }
  const position = (entry, start, localMain) => {
    entry.crossStart = start;
    const cross = start + (entry.span - entry.crossSize) / 2, main = localGenerations ? localMain : mainStarts[entry.depth];
    entry.x = vertical ? cross : main; entry.y = vertical ? main : cross;
    const childrenSpan = entry.children.reduce((sum, child) => sum + child.span, 0) + Math.max(0, entry.children.length - 1) * siblingGap;
    let childStart = start + (entry.span - childrenSpan) / 2;
    // Global generation lanes remain useful for comparable peers. In a large
    // variable-scale forest a distant oversized cousin must not force a tiny
    // local child hundreds of its own heights away from its parent.
    const childGap = Math.min(levelGap, Math.max(entry.mainSize * .35, ...entry.children.map(child => child.mainSize * .35), 1e-6));
    const childMain = main + entry.mainSize + childGap;
    for (const child of entry.children) { position(child, childStart, childMain); childStart += child.span + siblingGap; }
  };
  let crossAt = vertical ? origin.x : origin.y;
  for (const root of roots) { position(root, crossAt, vertical ? origin.y : origin.x); crossAt += root.span + rootGap; }
  const nodes = [...entries.values()].map(entry => ({ key: entry.key, parent: entry.parent, depth: entry.depth,
    x: entry.x, y: entry.y, width: entry.width, height: entry.height, scale: entry.scale,
    allocation: { crossStart: entry.crossStart, crossEnd: entry.crossStart + entry.span,
      mainStart: localGenerations ? vertical ? entry.y : entry.x : mainStarts[entry.depth],
      mainEnd: localGenerations ? (vertical ? entry.y : entry.x) + entry.mainSize : mainStarts[entry.depth] + levels[entry.depth] } }));
  const byKey = Object.fromEntries(nodes.map(node => [node.key, node]));
  const routes = nodes.filter(node => node.parent !== null).map(node => {
    const parent = byKey[node.parent];
    const channel = localGenerations
      ? vertical ? (parent.y + parent.height + node.y) / 2 : (parent.x + parent.width + node.x) / 2
      : mainStarts[parent.depth] + levels[parent.depth] + levelGap / 2;
    const start = vertical ? { x: parent.x + parent.width / 2, y: parent.y + parent.height }
      : { x: parent.x + parent.width, y: parent.y + parent.height / 2 };
    const end = vertical ? { x: node.x + node.width / 2, y: node.y }
      : { x: node.x, y: node.y + node.height / 2 };
    const points = vertical ? [{ x: start.x, y: channel }, { x: end.x, y: channel }]
      : [{ x: channel, y: start.y }, { x: channel, y: end.y }];
    // A single-child branch is a straight segment; do not add duplicate bends.
    const compact = Math.abs((vertical ? start.x - end.x : start.y - end.y)) < 1e-10 ? [] : points;
    return { source: parent.key, target: node.key, parent: parent.key, start, end, points: compact,
      sourceAnchor: vertical ? { x: .5, y: 1 } : { x: 1, y: .5 },
      targetAnchor: vertical ? { x: .5, y: 0 } : { x: 0, y: .5 } };
  });
  const laneMetadata = levels.map((size, depth) => {
    if (!localGenerations) return { depth, start: mainStarts[depth], size, gapAfter: levelGap };
    const members = nodes.filter(node => node.depth === depth);
    const start = Math.min(...members.map(node => vertical ? node.y : node.x));
    const end = Math.max(...members.map(node => vertical ? node.y + node.height : node.x + node.width));
    // A depth envelope describes the generation; it is not an occupied row.
    return { depth, start, size: end - start, gapAfter: levelGap, local: true, nodeMaxSize: size };
  });
  return { orientation: direction, nodes, byKey, routes, roots: roots.map(root => root.key), bounds: envelope(nodes),
    settings: { origin, width: baseWidth, height: baseHeight, depthScale, minScale, siblingGap, rootGap, levelGap, localGenerations },
    levels: laneMetadata };
}

/**
 * Fit a large reserved forest to a usable root overview without distorting detail.
 *
 * All nonroot rectangles and sibling gaps receive ONE uniform scale factor.
 * Root frames are deliberately promoted to readable rootWidth/rootHeight (or
 * rootSizes[key]) and complete subtree allocations are recomputed around them.
 * Generation lanes and anchors/bends are regenerated; no coordinates are squeezed
 * independently. maxCrossSpan means width for down, height for right. For a down
 * tree boundsWidth is accepted; for a right tree boundsHeight is accepted.
 *
 * This reserves the same all-open geometry when branches are hidden. It does not
 * promise a dense census is readable in the overview: detail is read by zooming.
 * The returned normalization report checks whether a target detail frame can fit
 * within maxZoom, not whether its particular text is legible. Use small scaled
 * padding/borders and contentSizing:'fit' for promoted roots and tiny detail.
 * Optional promoteBranches (or branchFill) in [0,1] enlarges every nonroot branching header uniformly
 * within its existing reserved cross-axis bay. This makes a broad parent readable
 * before zooming into its tiny leaves without consuming a neighboring branch's
 * space. Aspect ratios stay intact and generation lanes are recomputed again.
 */
export function normalizeTreeForest(layout, options = {}) {
  if (!layout || !Array.isArray(layout.nodes) || !['down', 'right'].includes(layout.orientation)) throw new TypeError('Expected a computeTreeLayout result.');
  const vertical = layout.orientation === 'down';
  const maxCrossSpan = positive(options.maxCrossSpan ?? (vertical ? options.boundsWidth : options.boundsHeight) ?? 9000, 'maxCrossSpan');
  const rootWidth = positive(options.rootWidth ?? 1000, 'rootWidth');
  const rootHeight = positive(options.rootHeight ?? 500, 'rootHeight');
  const rootGap = positive(options.rootGap ?? 160, 'rootGap');
  const levelGap = positive(options.levelGap ?? 120, 'levelGap');
  const maxZoom = positive(options.maxZoom ?? 1024, 'maxZoom');
  const focusWidth = positive(options.focusWidth ?? 360, 'focusWidth');
  const focusHeight = positive(options.focusHeight ?? 120, 'focusHeight');
  const siblingGap = positive(options.siblingGap ?? layout.settings?.siblingGap ?? 80, 'siblingGap');
  const branchFill = positive(options.promoteBranches ?? options.branchFill ?? 0, 'promoteBranches', true);
  if (branchFill > 1) throw new TypeError('branchFill must not exceed 1.');
  const maxBranchEnlargement = positive(options.maxBranchEnlargement ?? 25, 'maxBranchEnlargement');
  if (maxBranchEnlargement < 1) throw new TypeError('maxBranchEnlargement must be at least 1.');
  const localGenerations = options.localGenerations ?? layout.settings?.localGenerations ?? false;
  const origin = options.origin ?? layout.settings?.origin ?? { x: layout.bounds.x, y: layout.bounds.y };
  const roots = new Set(layout.roots);
  const parents = new Set(layout.nodes.map(node => node.parent).filter(key => key !== null));
  const render = factor => {
    const records = layout.nodes.map(node => ({ key: node.key, parent: node.parent,
    width: roots.has(node.key) ? positive(options.rootSizes?.[node.key]?.width ?? rootWidth, `${node.key}.rootWidth`) : node.width * factor,
    height: roots.has(node.key) ? positive(options.rootSizes?.[node.key]?.height ?? rootHeight, `${node.key}.rootHeight`) : node.height * factor,
    scale: roots.has(node.key) ? 1 : node.scale * factor }));
    const settings = { origin, siblingGap: siblingGap * factor, rootGap, levelGap, localGenerations };
    const plain = computeTreeLayout(records, layout.orientation, settings);
    if (!branchFill) return plain;
    for (const record of records) if (!roots.has(record.key) && parents.has(record.key)) {
      const node = plain.byKey[record.key], span = node.allocation.crossEnd - node.allocation.crossStart;
      const enlargement = Math.min(maxBranchEnlargement, Math.max(1, span * branchFill / (vertical ? node.width : node.height)));
      record.width *= enlargement; record.height *= enlargement; record.scale *= enlargement;
    }
    return computeTreeLayout(records, layout.orientation, settings);
  };
  const crossExtent = candidate => vertical ? candidate.bounds.width : candidate.bounds.height;
  let factor = 1, normalized = render(factor);
  if (crossExtent(normalized) > maxCrossSpan + 1e-7) {
    let low = 1e-10, high = 1;
    if (crossExtent(render(low)) > maxCrossSpan + 1e-7) throw new RangeError('Promoted root frames and root gaps exceed maxCrossSpan; increase the span or reduce root dimensions/gaps.');
    for (let pass = 0; pass < 60; pass++) {
      const middle = (low + high) / 2;
      if (crossExtent(render(middle)) <= maxCrossSpan) low = middle; else high = middle;
    }
    factor = low; normalized = render(factor);
  }
  const focus = normalized.nodes.map(node => ({ key: node.key, width: node.width, height: node.height,
    requiredZoom: Math.max(focusWidth / node.width, focusHeight / node.height) }));
  const hardest = focus.reduce((worst, node) => !worst || node.requiredZoom > worst.requiredZoom ? node : worst, null);
  normalized.normalization = { factor, sourceBounds: { ...layout.bounds }, maxCrossSpan, crossExtent: crossExtent(normalized),
    rootWidth, rootHeight, rootGap, levelGap, branchFill, maxBranchEnlargement, localGenerations, maxZoom, focusWidth, focusHeight,
    minimumWidth: focus.length ? Math.min(...focus.map(node => node.width)) : 0,
    minimumHeight: focus.length ? Math.min(...focus.map(node => node.height)) : 0,
    largestRequiredZoom: hardest?.requiredZoom ?? 1, hardestNode: hardest?.key ?? null,
    detailFramesReachable: !hardest || hardest.requiredZoom <= maxZoom };
  return normalized;
}

/** Returns recipe fields only; keep returned allocation/scale metadata out of the node schema. */
export function spatialTreeNodeFields(rectangle) {
  return { x: rectangle.x, y: rectangle.y, width: rectangle.width, height: rectangle.height,
    kind: 'media', contentType: 'text', containerVariant: 'contained', childLayout: 'spatial',
    childArrangement: 'free', childPosition: 'manual', placement: 'free', sizeMode: 'fixed' };
}

/** Strict interior intersection: touching intended endpoint boundaries is permitted. */
export function segmentIntersectsRectangle(a, b, box, epsilon = 1e-7) {
  const left = box.x + epsilon, right = box.x + box.width - epsilon;
  const top = box.y + epsilon, bottom = box.y + box.height - epsilon;
  if (left >= right || top >= bottom) return false;
  let t0 = 0, t1 = 1;
  const dx = b.x - a.x, dy = b.y - a.y;
  for (const [p, q] of [[-dx, a.x - left], [dx, right - a.x], [-dy, a.y - top], [dy, bottom - a.y]]) {
    if (Math.abs(p) < 1e-12) { if (q < 0) return false; continue; }
    const t = q / p;
    if (p < 0) t0 = Math.max(t0, t); else t1 = Math.min(t1, t);
    if (t0 > t1) return false;
  }
  return t0 <= t1;
}

/** All-open geometry is a superset of any ordinary collapsed disclosure state. */
export function inspectTreeLayout(layout) {
  const overlaps = [], routeCollisions = [];
  for (const [at, node] of layout.nodes.entries()) for (const other of layout.nodes.slice(at + 1)) {
    if (node.x < other.x + other.width && node.x + node.width > other.x && node.y < other.y + other.height && node.y + node.height > other.y) overlaps.push([node.key, other.key]);
  }
  for (const route of layout.routes) {
    const path = [route.start, ...route.points, route.end];
    for (const node of layout.nodes) if (node.key !== route.source && node.key !== route.target) {
      if (path.slice(1).some((end, at) => segmentIntersectsRectangle(path[at], end, node))) routeCollisions.push({ source: route.source, target: route.target, node: node.key });
    }
  }
  return { overlaps, routeCollisions };
}
