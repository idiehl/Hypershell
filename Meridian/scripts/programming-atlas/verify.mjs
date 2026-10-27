/**
 * Read-only structural and geometry review for the programming atlases.
 * This is a clearance check, never a substitute for opening viewer captures.
 * Usage: node --experimental-strip-types scripts/programming-atlas/verify.mjs
 *   --input draft.json --aliases aliases.json --out geometry-review.json
 */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { isProjectShape, validateProject } from '../../src/core/rules.ts';
import { inspectProject } from '../../src/core/authoring.ts';
import { navigationIndex } from '../../src/core/navigation.ts';
import { nodeShape, shapeVertices, shapeAnchor, shapesOverlap } from '../../src/core/geometry.ts';
import { isEnclosingContainer } from '../../src/core/cells.ts';
import { isIntentionalContainment } from '../../src/core/collision.ts';
import { revealChildren } from '../../src/core/reveal.ts';
import { visibleExpansionTrays } from '../../src/core/expansion-tray.ts';
import { windowParents, windowSurface, displayNodeBounds, prepareWindowOffsets } from '../../src/window-scene.ts';

const EPS = 1e-6;
const cross = (a, b) => a.x * b.y - a.y * b.x;
const subtract = (a, b) => ({ x: a.x - b.x, y: a.y - b.y });
const center = box => ({ x: box.x + box.width / 2, y: box.y + box.height / 2 });
const lerp = (a, b, t) => ({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t });
const readJson = async filename => JSON.parse((await readFile(filename, 'utf8')).replace(/^\uFEFF/, ''));

function clipPolygon(polygon, rectangle) {
  let result = polygon;
  const sides = [
    [point => point.x - rectangle.x, 'x', rectangle.x],
    [point => rectangle.x + rectangle.width - point.x, 'x', rectangle.x + rectangle.width],
    [point => point.y - rectangle.y, 'y', rectangle.y],
    [point => rectangle.y + rectangle.height - point.y, 'y', rectangle.y + rectangle.height],
  ];
  for (const [inside, axis, coordinate] of sides) {
    const before = result; result = [];
    for (let at = 0; at < before.length; at++) {
      const a = before[at], b = before[(at + 1) % before.length];
      const first = inside(a) >= -EPS, second = inside(b) >= -EPS;
      if (first) result.push(a);
      if (first !== second) result.push(lerp(a, b, (coordinate - a[axis]) / (b[axis] - a[axis])));
    }
    if (!result.length) break;
  }
  return result;
}

function polygonArea(polygon) {
  return Math.abs(polygon.reduce((area, point, at) => area + cross(point, polygon[(at + 1) % polygon.length]), 0)) / 2;
}

/** Strict interior: mere contact with a border is not a crossing. */
function insidePolygon(point, polygon) {
  if (polygon.length < 3) return false;
  let direction;
  for (let at = 0; at < polygon.length; at++) {
    const a = polygon[at], b = polygon[(at + 1) % polygon.length];
    if (Math.hypot(b.x - a.x, b.y - a.y) <= EPS) continue;
    const side = cross(subtract(b, a), subtract(point, a));
    if (Math.abs(side) <= EPS) return false;
    if (direction === undefined) direction = side > 0;
    else if (direction !== (side > 0)) return false;
  }
  return true;
}

/** Works with the convex shared backdrop silhouettes, including clipped Windows. */
export function segmentCrossesPolygon(a, b, polygon) {
  if (polygon.length < 3 || polygonArea(polygon) <= EPS) return false;
  const minimumX = Math.min(...polygon.map(point => point.x)), maximumX = Math.max(...polygon.map(point => point.x));
  const minimumY = Math.min(...polygon.map(point => point.y)), maximumY = Math.max(...polygon.map(point => point.y));
  if (Math.max(a.x, b.x) <= minimumX + EPS || Math.min(a.x, b.x) >= maximumX - EPS ||
      Math.max(a.y, b.y) <= minimumY + EPS || Math.min(a.y, b.y) >= maximumY - EPS) return false;
  const cuts = [0, 1], ray = subtract(b, a);
  for (let at = 0; at < polygon.length; at++) {
    const p = polygon[at], q = polygon[(at + 1) % polygon.length], edge = subtract(q, p), denominator = cross(ray, edge);
    if (Math.abs(denominator) <= EPS) continue;
    const t = cross(subtract(p, a), edge) / denominator, u = cross(subtract(p, a), ray) / denominator;
    if (t >= 0 && t <= 1 && u >= -EPS && u <= 1 + EPS) cuts.push(t);
  }
  cuts.sort((first, second) => first - second);
  return cuts.slice(0, -1).some((t, at) => cuts[at + 1] - t > EPS && insidePolygon(lerp(a, b, (t + cuts[at + 1]) / 2), polygon));
}

/** Same endpoint vocabulary as scene.ts; junctions intentionally meet at center. */
function endpoint(node, toward, anchor) {
  if (node.kind === 'junction') return center(node);
  if (!anchor) return shapeAnchor(nodeShape(node), toward.x + toward.width / 2 - node.x - node.width / 2, toward.y + toward.height / 2 - node.y - node.height / 2);
  const x = Math.max(0, Math.min(1, anchor.x)), y = Math.max(0, Math.min(1, anchor.y));
  const sides = [{ distance: x, x: 0, y }, { distance: 1 - x, x: 1, y }, { distance: y, x, y: 0 }, { distance: 1 - y, x, y: 1 }];
  sides.sort((a, b) => a.distance - b.distance);
  return shapeAnchor(nodeShape(node), (sides[0].x - .5) * node.width, (sides[0].y - .5) * node.height);
}

export function auditScene(project, canvasId, { focusId } = {}) {
  const offsets = new Map(), provisional = navigationIndex(project);
  prepareWindowOffsets(project, provisional, offsets, focusId);
  const index = navigationIndex(project), cards = [], issues = [];
  const isEnclosingAncestor = (ancestor, id) => isEnclosingContainer(ancestor) && index.presentationAncestors(id).some(parent => parent.id === ancestor.id);
  for (const node of project.nodes) {
    if (node.canvasId !== canvasId || node.kind === 'canvas' || !index.isNodeVisible(node.id)) continue;
    const bounds = displayNodeBounds(index, node, offsets), shape = nodeShape(node, bounds);
    // "none" still owns its title/content frame; transparent labels cannot be crossed.
    let polygon = shapeVertices(shape, 192);
    for (const parent of windowParents(index, node.id)) {
      const surface = windowSurface(parent, project), parentOffset = windowParents(index, parent.id).reduce((total, ancestor) => {
        const at = offsets.get(ancestor.id) ?? { x: 0, y: 0 }; return { x: total.x + at.x, y: total.y + at.y };
      }, { x: 0, y: 0 });
      polygon = clipPolygon(polygon, { x: surface.geometry.left - parentOffset.x, y: surface.geometry.top - parentOffset.y, width: surface.width, height: surface.height });
    }
    if (polygon.length < 3 || polygonArea(polygon) <= EPS) continue;
    cards.push({ id: node.id, node, bounds, shape, polygon });
  }
  const rendered = new Map(cards.map(card => [card.node.id, card]));
  const trays = visibleExpansionTrays(project).filter(tray => index.nodes.get(tray.sourceId)?.canvasId === canvasId && rendered.has(tray.sourceId));
  const trayCards = trays.map(tray => {
    const node = index.nodes.get(tray.sourceId), shape = { ...tray, id: `tray:${node.id}`, shape: 'rectangle' };
    return { id: shape.id, node, bounds: tray, shape, polygon: shapeVertices(shape), traySource: node.id };
  });
  const obstacles = [...cards, ...trayCards];
  const trayOwns = (tray, other) => tray.traySource && !other.traySource && index.ancestors(other.node.id).some(parent => parent.id === tray.traySource);
  for (let at = 0; at < obstacles.length; at++) for (let next = at + 1; next < obstacles.length; next++) {
    const a = obstacles[at], b = obstacles[next];
    if ((!a.traySource && !b.traySource && isIntentionalContainment(index, a.node.id, b.node.id)) || trayOwns(a, b) || trayOwns(b, a) || !shapesOverlap(a.shape, b.shape)) continue;
    // A Window can clip a node before the full backdrop would meet its peer.
    const clippedOverlap = a.polygon.some(point => insidePolygon(point, b.polygon)) || b.polygon.some(point => insidePolygon(point, a.polygon)) ||
      a.polygon.some((point, edge) => segmentCrossesPolygon(point, a.polygon[(edge + 1) % a.polygon.length], b.polygon)) ||
      insidePolygon(center(a.bounds), b.polygon) && insidePolygon(center(a.bounds), a.polygon) ||
      insidePolygon(center(b.bounds), a.polygon) && insidePolygon(center(b.bounds), b.polygon);
    if (clippedOverlap) issues.push({ code: a.traySource || b.traySource ? 'tray-frame-overlap' : 'node-overlap', nodeIds: [a.id, b.id], titles: [a.node.tag || a.node.title, b.node.tag || b.node.title] });
  }
  let visibleLinkCount = 0;
  for (const link of project.links.filter(item => item.canvasId === canvasId && index.isLinkVisible(item))) {
    const a = rendered.get(link.sourceId), b = rendered.get(link.targetId);
    if (!a || !b) continue;
    visibleLinkCount++;
    if (link.sourceId === link.targetId || link.relation === 'cyclical' && link.direction === undefined && !link.points?.length) {
      issues.push({ code: 'curve-needs-render-review', linkId: link.id, label: link.label }); continue;
    }
    const effectiveA = { ...a.node, ...a.bounds }, effectiveB = { ...b.node, ...b.bounds };
    const first = endpoint(effectiveA, effectiveB, link.sourceAnchor), last = endpoint(effectiveB, effectiveA, link.targetAnchor);
    const commonWindow = windowParents(index, a.node.id).find(parent => windowParents(index, b.node.id).some(other => other.id === parent.id));
    const scroll = commonWindow ? [commonWindow, ...windowParents(index, commonWindow.id)].reduce((total, parent) => {
      const at = offsets.get(parent.id) ?? { x: 0, y: 0 }; return { x: total.x + at.x, y: total.y + at.y };
    }, { x: 0, y: 0 }) : { x: 0, y: 0 };
    const route = [first, ...(link.points ?? []).map(point => ({ x: point.x - scroll.x, y: point.y - scroll.y })), last];
    for (const obstacle of obstacles) {
      if ([link.sourceId, link.targetId].includes(obstacle.id) || !obstacle.traySource && (isEnclosingAncestor(obstacle.node, link.sourceId) || isEnclosingAncestor(obstacle.node, link.targetId)) || obstacle.traySource && [link.sourceId, link.targetId].some(id => index.ancestors(id).some(parent => parent.id === obstacle.traySource))) continue;
      const segments = route.slice(0, -1).flatMap((point, at) => segmentCrossesPolygon(point, route[at + 1], obstacle.polygon) ? [at] : []);
      if (segments.length) issues.push({ code: 'route-crosses-node', linkId: link.id, label: link.label ?? '', nodeId: obstacle.id, title: obstacle.node.tag || obstacle.node.title, segments });
    }
  }
  // Match the connector geometry in expansion-tray-scene.ts. Necessary ancestor
  // border crossings are permitted; unrelated covers, labels and notes are not.
  for (const tray of trays) {
    const original = index.nodes.get(tray.sourceId), sourceCard = rendered.get(original.id), source = { ...original, ...sourceCard.bounds }, target = center(tray);
    let outer;
    for (const ancestor of index.ancestors(original.id)) if (isEnclosingContainer(ancestor) && rendered.has(ancestor.id)) outer = rendered.get(ancestor.id).bounds;
    let route;
    if (outer && (tray.x >= outer.x + outer.width || tray.x + tray.width <= outer.x)) {
      const below = target.y >= outer.y + outer.height / 2, y = below ? outer.y + outer.height + 16 : outer.y - 16;
      const start = shapeAnchor(nodeShape(source), 0, below ? 1 : -1), right = tray.x >= outer.x + outer.width, x = right ? tray.x - 16 : tray.x + tray.width + 16;
      const end = { x: right ? tray.x : tray.x + tray.width, y: Math.max(tray.y + 8, Math.min(tray.y + tray.height - 8, y)) };
      route = [start, { x: start.x, y }, { x, y }, { x, y: end.y }, end];
    } else {
      const start = shapeAnchor(nodeShape(source), target.x - source.x - source.width / 2, target.y - source.y - source.height / 2);
      const end = shapeAnchor({ ...tray, shape: 'rectangle' }, start.x - target.x, start.y - target.y); route = [start, end];
    }
    for (const obstacle of obstacles) {
      if (obstacle.id === original.id || obstacle.traySource === original.id || !obstacle.traySource && isEnclosingAncestor(obstacle.node, original.id) || obstacle.traySource && index.ancestors(original.id).some(parent => parent.id === obstacle.traySource)) continue;
      const segments = route.slice(0, -1).flatMap((point, at) => segmentCrossesPolygon(point, route[at + 1], obstacle.polygon) ? [at] : []);
      if (segments.length) issues.push({ code: 'tray-tether-crosses-node', sourceId: original.id, nodeId: obstacle.id, title: obstacle.node.tag || obstacle.node.title, segments });
    }
  }
  return { canvasId, visibleNodes: cards.length, visibleLinks: visibleLinkCount, visibleTrayFrames: trays.length, ...(focusId ? { focusId } : {}), issues };
}

function verifyAliases(project, aliases) {
  const maps = { canvases: new Set(project.canvases.map(item => item.id)), roots: new Set(project.canvases.map(item => item.rootId)), nodes: new Set(project.nodes.map(item => item.id)), links: new Set(project.links.map(item => item.id)), assets: new Set(project.assets.map(item => item.id)) };
  let count = 0;
  for (const [kind, values] of Object.entries(aliases)) {
    assert.ok(maps[kind], `Unknown alias map ${kind}`);
    for (const [key, id] of Object.entries(values)) { assert.ok(maps[kind].has(id), `Stale ${kind} alias ${key}: ${id}`); count++; }
  }
  return count;
}

export function verifyAtlas(project, aliases) {
  assert.ok(isProjectShape(project), 'Invalid project shape');
  assert.deepEqual(validateProject(project), [], 'Invalid project graph');
  const aliasCount = verifyAliases(project, aliases), index = navigationIndex(project), states = [], failures = new Map();
  const add = (state, label) => {
    states.push({ state: label, ...state, issues: undefined, issueCount: state.issues.length });
    for (const issue of state.issues) {
      const key = JSON.stringify({ canvasId: state.canvasId, ...issue });
      const existing = failures.get(key); if (existing) existing.states.push(label);
      else failures.set(key, { canvasId: state.canvasId, ...issue, states: [label] });
    }
  };
  for (const canvas of project.canvases) add(auditScene(project, canvas.id), 'initial');
  const disclosure = project.nodes.filter(node => node.kind !== 'canvas' && node.collapsed && index.children.get(node.id)?.length);
  for (const node of disclosure) {
    let candidate = project;
    try {
      for (const target of [...index.ancestors(node.id)].reverse().filter(parent => parent.kind !== 'canvas' && parent.collapsed).concat(node))
        candidate = revealChildren(candidate, target.id, true, { preserveValidPositions: true });
      assert.deepEqual(candidate.nodes.map(item => item.id), project.nodes.map(item => item.id), 'Disclosure changed node identity');
      assert.deepEqual(candidate.links.map(item => item.id), project.links.map(item => item.id), 'Disclosure changed link identity');
      add(auditScene(candidate, node.canvasId, { focusId: node.id }), `expanded:${node.id}`);
    } catch (error) {
      add({ canvasId: node.canvasId, visibleNodes: 0, visibleLinks: 0, issues: [{ code: 'disclosure-failed', nodeId: node.id, title: node.tag || node.title, message: String(error.message ?? error) }] }, `expanded:${node.id}`);
    }
  }
  for (const canvas of project.canvases) {
    let candidate = project;
    try {
      const targets = disclosure.filter(node => node.canvasId === canvas.id).sort((a, b) => index.ancestors(a.id).length - index.ancestors(b.id).length);
      for (const target of targets) if (candidate.nodes.find(node => node.id === target.id)?.collapsed) candidate = revealChildren(candidate, target.id, true, { preserveValidPositions: true });
      assert.deepEqual(candidate.nodes.map(item => item.id), project.nodes.map(item => item.id), 'Disclosure changed node identity');
      assert.deepEqual(candidate.links.map(item => item.id), project.links.map(item => item.id), 'Disclosure changed link identity');
      add(auditScene(candidate, canvas.id), 'all-groups-expanded');
    } catch (error) {
      add({ canvasId: canvas.id, visibleNodes: 0, visibleLinks: 0, issues: [{ code: 'all-disclosure-failed', message: String(error.message ?? error) }] }, 'all-groups-expanded');
    }
  }
  const inspection = inspectProject(project), report = {
    projectId: project.id, title: project.title, revision: project.revision,
    sha256: createHash('sha256').update(JSON.stringify(project)).digest('hex'),
    graphValid: true, aliasCount, counts: { works: project.canvases.length, nodes: project.nodes.length, links: project.links.length, assets: project.assets.length },
    geometry: { passed: failures.size === 0, states, failures: [...failures.values()] },
    inspectionWarningCounts: inspection.warnings.reduce((counts, item) => ({ ...counts, [item.code]: (counts[item.code] ?? 0) + 1 }), {}),
    limits: [
      'This report checks graph identity, effective visible silhouettes, straight route and tray-tether clearance, individual branch disclosure, and a state with all groups expanded in each Work; it does not judge composition quality or source accuracy.',
      'Window checks use the saved initial scroll position or the focused disclosed branch. Viewer captures and actual scrolling must still be reviewed for remaining pages.',
      'Curved self/cyclical routes require rendered review. Link labels, stroke thickness, browser typography, text clipping, media decoding, and complete interaction behavior require viewer inspection.',
      'Transparent labels retain a content rectangle for clearance. Enclosing ancestors may contain descendants and their routes; unrelated node content never becomes an allowed obstruction.',
    ],
  };
  return report;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const args = new Map();
  for (let at = 2; at < process.argv.length; at += 2) {
    const key = process.argv[at]?.replace(/^--/, ''), value = process.argv[at + 1];
    assert.ok(['input', 'aliases', 'out'].includes(key) && value && !value.startsWith('--') && !args.has(key), 'Use --input FILE --aliases FILE --out FILE'); args.set(key, value);
  }
  for (const key of ['input', 'aliases', 'out']) assert.ok(args.has(key), `Missing --${key}`);
  const project = await readJson(args.get('input')), aliases = await readJson(args.get('aliases'));
  const report = verifyAtlas(project, aliases), out = path.resolve(args.get('out'));
  await mkdir(path.dirname(out), { recursive: true }); await writeFile(out, JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify({ file: out, graphValid: report.graphValid, ...report.counts, checkedStates: report.geometry.states.length, uniqueFailures: report.geometry.failures.length, passed: report.geometry.passed }));
  if (!report.geometry.passed) process.exitCode = 1;
}
