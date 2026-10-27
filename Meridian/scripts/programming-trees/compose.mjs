/** Append native subject trees to the saved programming collections. Local only. */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { applyRecipe, inspectProject } from '../../src/core/authoring.ts';
import { commitSnapshots } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';
import { buildPublication } from '../../src/publication.ts';
import { computeTreeLayout, normalizeTreeForest, inspectTreeLayout, spatialTreeNodeFields, PROGRAMMING_TREE_PRESETS } from './tree-layout.mjs';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const OUT = path.join(ROOT, 'output/programming-trees-20261021');
const CONTENT = path.join(ROOT, 'content/programming-trees-20261021');
const readJson = async filename => JSON.parse((await readFile(filename, 'utf8')).replace(/^\uFEFF/, ''));
const hash = value => createHash('sha256').update(typeof value === 'string' || Buffer.isBuffer(value) ? value : JSON.stringify(value)).digest('hex');
const save = async (dir, name, value) => { await mkdir(dir, { recursive: true }); await writeFile(path.join(dir, name), typeof value === 'string' ? value : JSON.stringify(value, null, 2) + '\n'); };
const THEMES = {
  csharp: { bg: '#171727', panel: '#282440', soft: '#211E36', ink: '#EEE9FF', muted: '#C0B6DA', accent: '#BEA3F4', line: '#9680C7', portal: 'doorway' },
  cpp: { bg: '#0D1825', panel: '#172B3B', soft: '#132433', ink: '#E0EDF3', muted: '#A3BDCD', accent: '#6CCFC5', line: '#5D9BA6', portal: 'passage' },
};
const copy = value => structuredClone(value);
function wrap(value, limit = 24) {
  if (value.length <= limit) return value;
  const candidates = [...value.matchAll(/(?<=[a-z])(?=[A-Z])|::|_/g)].map(match => match.index + (match[0] === '::' ? 2 : 0));
  const cut = candidates.filter(at => at > value.length * .25 && at < value.length * .75).sort((a, b) => Math.abs(a - value.length / 2) - Math.abs(b - value.length / 2))[0];
  return cut ? value.slice(0, cut) + '\n' + value.slice(cut) : value;
}
function csharpRecords(data) {
  const rank = name => name === 'System' ? 0 : name === 'Microsoft' ? 1 : 2;
  return [...data.namespaceEntries].sort((a, b) => (a.parent ? 1 : 0) - (b.parent ? 1 : 0) || (!a.parent && !b.parent ? rank(a.name) - rank(b.name) : 0) || a.name.localeCompare(b.name)).map(entry => ({
    key: `namespace/${entry.name}`, parent: entry.parent ? `namespace/${entry.parent}` : null,
    title: wrap(entry.segment), tag: entry.name, name: entry.name, prefixOnly: entry.prefixOnly,
    text: `${entry.name}\n${entry.description}\n${entry.availability}\n${entry.publicTypeCount} public type names · ${entry.children.length} namespace branches${entry.representativeTypes.length ? '\nExamples: ' + entry.representativeTypes.slice(0, 3).map(type => type.name).join(', ') : ''}`,
    url: entry.prefixOnly ? null : entry.documentationUrl,
    linkLabel: `${entry.name} · ${entry.availability} · Open namespace reference`,
    source: entry,
  }));
}
function cppRecords(data) {
  const records = [{ key: 'topic/std', parent: null, title: 'C++ standard library', tag: 'C++ standard library · std', name: 'std', text: 'Choose a task, then a family, then a named type.\nC++23 · 13 categories · 62 subgroups\nClassification by use; this is not inheritance.', url: null }];
  const byId = new Map(data.types.map(type => [type.id, type]));
  for (const category of data.categories) {
    const categoryKey = `category/${category.id}`;
    records.push({ key: categoryKey, parent: 'topic/std', title: wrap(category.title), tag: `C++ · ${category.title}`, name: category.title, text: `${category.purpose}\n${category.count} types · ${category.children.length} families`, url: null });
    for (const group of category.children) {
      const groupKey = `group/${group.id}`;
      records.push({ key: groupKey, parent: categoryKey, title: wrap(group.title), tag: `C++ · ${category.title} · ${group.title}`, name: group.title, text: `${group.purpose}\n${group.typeIds.length} types`, url: null });
      for (const id of group.typeIds) {
        const type = byId.get(id); assert(type, `Missing C++ type ${id}`);
        records.push({ key: `type/${type.id}`, parent: groupKey, title: wrap(type.qualifiedName, 28), tag: type.qualifiedName, name: type.qualifiedName,
          text: `${type.primaryHeader} · ${type.kind}\n${type.purpose}\n${type.status}${type.availability ? '\n' + type.availability : ''}`,
          url: type.sourceUrl, linkLabel: `${type.primaryHeader} · ${type.kind} · ${type.status} · Source declaration`, source: type });
      }
    }
  }
  assert.equal(records.filter(record => record.key.startsWith('type/')).length, 965);
  assert.equal(new Set(records.map(record => record.key)).size, records.length);
  return records;
}

export async function compose(slug) {
  const t = THEMES[slug]; assert(t, 'Choose csharp or cpp.');
  const dir = path.join(OUT, slug), baseline = await readJson(path.join(dir, 'baseline.json'));
  let prior;
  try { prior = { project: await readJson(path.join(dir, 'draft.json')), aliases: await readJson(path.join(dir, 'aliases.json')) }; }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  const filename = slug === 'csharp' ? 'csharp-namespaces.json' : 'cpp-types.json';
  const sourceBytes = await readFile(path.join(CONTENT, filename)), data = JSON.parse(sourceBytes.toString('utf8'));
  const records = slug === 'csharp' ? csharpRecords(data) : cppRecords(data);
  const orientation = slug === 'csharp' ? 'down' : 'right';
  const raw = computeTreeLayout(records, orientation, { width: 720, height: 280, depthScale: .82, minScale: .38, siblingGap: 110, levelGap: 160 });
  const origin = slug === 'csharp' ? { x: 80, y: 1450 } : { x: 3200, y: 80 };
  const layout = normalizeTreeForest(raw, { ...PROGRAMMING_TREE_PRESETS[slug === 'csharp' ? 'namespaces' : 'classes'], origin });
  const clearance = inspectTreeLayout(layout);
  assert.equal(clearance.overlaps.length, 0); assert.equal(clearance.routeCollisions.length, 0);
  assert(layout.normalization.detailFramesReachable, 'Deepest detail must be reachable within native zoom bounds.');
  const workTitle = slug === 'csharp' ? 'C# namespaces' : 'C++ classes and types';
  const entry = baseline.canvases.find(work => work.id === baseline.entryCanvasId); assert(entry);
  const recipe = { version: 1, bindings: { ...prior?.aliases, canvases: { ...prior?.aliases.canvases, collection: entry.id }, roots: { ...prior?.aliases.roots, collection: entry.rootId } },
    canvases: [{ key: 'collection', existingId: entry.id }, { key: 'tree', title: workTitle, style: { background: t.bg, gridVisible: false, portalStyle: t.portal, portalColor: t.accent }, snapping: { stickyEdges: false, snapToGrid: false, smartAlign: false } }], nodes: [], links: [], assets: [] };
  const common = { fill: t.panel, border: t.line, textColor: t.ink, accent: t.accent, fontFamily: 'sans', titleSize: 44, fontSize: 18, shape: 'rectangle', radius: 0, contentSizing: 'fit', textAlign: 'center', padding: 0, contentGap: 5, childPadding: 0 };
  const add = (key, title, text, x, y, width, height, extra = {}) => { recipe.nodes.push({ key, canvas: 'tree', kind: 'media', title, tag: title.replaceAll('\n', ' '), text, contentType: 'text', x, y, width, height, sizeMode: 'fixed', childPosition: 'manual', ...extra, style: { ...common, ...extra.style } }); return key; };
  const rootKeys = new Set(layout.roots), hasChildren = new Set(records.filter(record => record.parent).map(record => record.parent));
  const sampleExpanded = slug === 'csharp' ? new Set(['namespace/System', 'namespace/System.Text', 'namespace/System.Text.Json']) : new Set(['topic/std', 'category/containers', 'category/memory']);
  for (const record of records) {
    const rect = layout.byKey[record.key];
    // Glyphs scale with the frame. Detail remains vector text and searchable by full name.
    const frameScale = Math.min(rect.width / 300, rect.height / 180);
    const body = record.text.replace(/`(\d+)/g, (_match, arity) => `<${Array.from({ length: Number(arity) }, (_, index) => Number(arity) === 1 ? 'T' : `T${index + 1}`).join(', ')}>`);
    add(record.key, record.title, body, rect.x, rect.y, rect.width, rect.height, {
      ...spatialTreeNodeFields(rect), ...(record.parent ? { parent: record.parent } : {}), tag: record.tag,
      collapsed: hasChildren.has(record.key) && !sampleExpanded.has(record.key),
      ...(record.url ? { hyperlinks: [{ url: record.url, label: (!hasChildren.has(record.key) ? `${body.slice(0, 132)}${body.length > 132 ? '…' : ''}\nReference ↗` : record.linkLabel).slice(0, 160) }], ...(!hasChildren.has(record.key) ? { contentType: 'link' } : {}) } : {}),
      style: { titleSize: rootKeys.has(record.key) ? (record.title.length > 12 ? 36 : 64) : hasChildren.has(record.key) ? 32 : 25, fontSize: 17,
        fill: rootKeys.has(record.key) ? t.soft : t.panel, borderWidth: Math.max(.002, Math.min(2, frameScale * .7)), padding: Math.min(4, Math.min(rect.width, rect.height) * .03), contentGap: 3,
        ...(!hasChildren.has(record.key) && record.url ? { fill: t.ink, textColor: t.bg } : {}),
        ...(record.source?.legacy && !record.source?.modern ? { borderStyle: 'dashed' } : {}) },
    });
  }

  // Real shared Joints form each family bus. A namespace/classification edge is
  // not inheritance. Every utility and connection belongs to its original parent.
  const families = new Map();
  for (const route of layout.routes) { const family = families.get(route.source) ?? []; family.push(route); families.set(route.source, family); }
  for (const [parent, routes] of families) {
    const first = routes[0];
    if (routes.length === 1 && first.points.length === 0) {
      recipe.links.push({ key: `relationship/${first.target}`, canvas: 'tree', parent, source: parent, target: first.target, kind: 'visual', relation: 'generic', direction: 'forward', sourceAnchor: first.sourceAnchor, targetAnchor: first.targetAnchor, style: { color: t.line, width: .6 } });
      continue;
    }
    const vertical = orientation === 'down', parentRect = layout.byKey[parent];
    const channel = first.points[0] ? (vertical ? first.points[0].y : first.points[0].x) : vertical ? (first.start.y + first.end.y) / 2 : (first.start.x + first.end.x) / 2;
    const center = vertical ? first.start.x : first.start.y;
    // The same intended bus point can differ by a few floating-point ulps after
    // subtree normalization. Merge it rather than draw overlapping Joint nodes.
    const EPS = 1e-7;
    const values = [];
    for (const value of [center, ...routes.map(route => vertical ? route.end.x : route.end.y)].sort((a, b) => a - b)) {
      if (!values.length || Math.abs(value - values.at(-1)) > EPS) values.push(value);
      else if (Math.abs(value - center) <= EPS) values[values.length - 1] = center;
    }
    const busIndex = value => { const index = values.findIndex(entry => Math.abs(entry - value) <= EPS); assert(index >= 0); return index; };
    const size = Math.max(.002, Math.min(6, ...routes.map(route => Math.min(layout.byKey[route.target].width, layout.byKey[route.target].height) * .08)));
    const jointKeys = values.map((value, index) => {
      const key = `joint/${parent}/${index}`;
      add(key, '', '', (vertical ? value : channel) - size / 2, (vertical ? channel : value) - size / 2, size, size,
        { parent, kind: 'junction', utilityVariant: 'joint', tag: `Branch Joint · ${parent} · ${index + 1}`, style: { fill: t.line, borderWidth: 0, shape: 'circle', padding: 0 } });
      return key;
    });
    const centerIndex = busIndex(center);
    recipe.links.push({ key: `bus/${parent}/entry`, canvas: 'tree', parent, source: parent, target: jointKeys[centerIndex], kind: 'visual', relation: 'generic', direction: 'none', sourceAnchor: first.sourceAnchor, style: { color: t.line, width: .6 } });
    for (let i = 1; i < values.length; i++) recipe.links.push({ key: `bus/${parent}/${i}`, canvas: 'tree', parent, source: jointKeys[i - 1], target: jointKeys[i], kind: 'visual', relation: 'generic', direction: 'none', style: { color: t.line, width: .6 } });
    for (const route of routes) recipe.links.push({ key: `relationship/${route.target}`, canvas: 'tree', parent, source: jointKeys[busIndex(vertical ? route.end.x : route.end.y)], target: route.target, kind: 'visual', relation: 'generic', direction: 'forward', targetAnchor: route.targetAnchor, style: { color: t.line, width: .6 } });
  }

  // A readable front door and original downloads stay beside, not across, the tree.
  const side = slug === 'csharp' ? { x: 80, y: 80, width: 10000 } : { x: 80, y: 80, width: 2700 };
  add('guide/heading', workTitle, slug === 'csharp' ? 'Namespaces branch downward. Double-click System, then Text, then Json. Full namespace names remain searchable in the Work panel.' : 'Choose a task on the left, reveal a family to its right, then inspect its named types. These branches classify use; they do not assert class inheritance.', side.x, side.y, side.width, slug === 'csharp' ? 500 : 650, { style: { shape: 'none', variant: 'label', titleSize: 54, fontSize: 20, textAlign: 'left', borderWidth: 0 } });
  add('guide/scope', 'Scope & reading', slug === 'csharp' ? '701 API namespaces + 35 marked prefixes. .NET 10.0.12, ASP.NET Core, Windows desktop, and .NET Framework 4.8.1. Dashed borders mark Framework-only entries. The dotted-name tree is not class inheritance.\n\nZoom into a branch before opening deeper levels. Search its complete name and use Target to center it. Deep detail uses smaller vector frames; full original files are below.' : '965 named C++23 standard-library types: classes, structs, aliases and enums. 13 task categories and 62 families. Deprecated facilities are marked. This catalogue does not claim the third-party ecosystem or every member alias.\n\nZoom into a branch to read its detail. Search std::vector or a full qualified name and use Target. Complete headers, purpose and source links are in the downloadable catalogue.', side.x, side.y + (slug === 'csharp' ? 540 : 710), side.width * (slug === 'csharp' ? .70 : 1), slug === 'csharp' ? 360 : 750, { style: { shape: 'none', variant: 'label', titleSize: 28, fontSize: 18, textAlign: 'left', borderWidth: 0 } });
  const home = add('guide/home', 'Collection', '', slug === 'csharp' ? 8280 : 80, slug === 'csharp' ? 650 : 1720, slug === 'csharp' ? 800 : 1150, slug === 'csharp' ? 560 : 650,
    { kind: 'canvas-link', target: 'collection', style: { portalStyle: t.portal, portalColor: t.accent, titleSize: 40 } });
  const csvName = slug === 'csharp' ? 'CSharp-namespace-type-index.csv' : 'Cpp-standard-library-types.csv';
  const csvBytes = await readFile(path.join(CONTENT, csvName));
  const guide = [`# ${workTitle}`, '', slug === 'csharp' ? data.scope : data.scope.claim, '', '## How to explore', '', 'Use native parent disclosure and search the full name in the Work panel. The tree retains its original nodes, saved positions, and relationships through collapse/reopen. Its orientation describes generations, not an ordered Display Pattern.', '', '## Catalogue', '', ...records.flatMap(record => [`### ${record.name}`, record.text, ...(record.url ? [record.url] : []), ''])].join('\n');
  recipe.assets.push({ key: 'catalogue-csv', name: csvName, mime: 'text/csv', base64: csvBytes.toString('base64') }, { key: 'reading-guide', name: `${workTitle} — complete guide.md`, mime: 'application/octet-stream', base64: Buffer.from(guide).toString('base64') });
  const downloadX = slug === 'csharp' ? 80 : 80, downloadY = slug === 'csharp' ? 1050 : 2500;
  add('guide/type-index', 'Complete type index · original CSV', '', downloadX, downloadY, slug === 'csharp' ? 3200 : 2700, slug === 'csharp' ? 320 : 1400, { kind: 'document', asset: 'catalogue-csv', contentType: 'asset', style: { titleSize: 28, fontSize: 16 } });
  add('guide/reading-guide', 'Download the complete guide', '', slug === 'csharp' ? 3500 : 80, slug === 'csharp' ? downloadY : 4100, slug === 'csharp' ? 3500 : 2700, slug === 'csharp' ? 320 : 700, { kind: 'document', asset: 'reading-guide', contentType: 'asset', style: { titleSize: 28, fontSize: 16 } });
  if (slug === 'csharp') {
    const apiEntries = records.filter(record => record.url);
    add('guide/references', 'Namespace references', 'Official API pages for every catalogue namespace. Open this Window, then scroll within it.', 7200, 1050, 2800, 320, {
      kind: 'container', collapsed: true, childLayout: 'contained', childArrangement: 'grid', containerVariant: 'window',
      grid: { columns: 2, rows: Math.ceil(apiEntries.length / 2), cellWidth: 1370, cellHeight: 130, windowColumns: 2, windowRows: 2, scrollDirection: 'vertical', showHeaders: false }, style: { childPadding: 3, padding: 3, titleSize: 32 },
    });
    apiEntries.forEach((record, index) => add(`reference/${record.name}`, record.name, '', 0, 0, 1370, 130, {
      parent: 'guide/references', contentType: 'link', hyperlinks: [{ url: record.url, label: `${record.source.availability} · Open official namespace reference` }],
      cell: { column: index % 2 + 1, row: Math.floor(index / 2) + 1 }, childPosition: 'auto', style: { fill: t.ink, textColor: t.bg, titleSize: 24, fontSize: 18, padding: 4, textAlign: 'left' },
    }));
  }
  // Append a clear entry without changing any existing collection objects.
  // Use larger world units so the installed renderer's minimum 1px link stroke
  // remains fine relative to the smallest detail frame. Vector content and the
  // camera retain the same visual proportions. No arrows imply type inheritance.
  const WORLD = 20;
  for (const node of recipe.nodes) {
    for (const key of ['x', 'y', 'width', 'height']) node[key] *= WORLD;
    // Fitted text spacing is expressed in logical reading units; do not multiply
    // its gaps into huge text insets when magnifying the world coordinates.
    if (node.style.padding !== undefined) node.style.padding = Math.min(5, node.style.padding * WORLD);
    if (node.style.childPadding !== undefined) node.style.childPadding = Math.min(1000, node.style.childPadding * WORLD);
    if (node.style.borderWidth !== undefined) node.style.borderWidth = Math.min(8, node.style.borderWidth * WORLD);
    if (node.grid) {
      node.grid.cellWidth = Math.min(10000, node.grid.cellWidth * WORLD);
      node.grid.cellHeight = Math.min(10000, node.grid.cellHeight * WORLD);
      node.width = node.grid.cellWidth * node.grid.windowColumns + 2 * (node.style.padding ?? 0) + 2 * (node.style.childPadding ?? 0);
    }
  }
  const familyWidths = new Map([...families].map(([owner, routes]) => [owner, Math.max(1, Math.min(32, Math.max(...routes.map(route => Math.min(layout.byKey[route.target].width, layout.byKey[route.target].height))) * WORLD / 80))]));
  for (const link of recipe.links) { link.direction = 'none'; link.style = { ...link.style, width: familyWidths.get(link.parent) ?? 1, lineStyle: 'solid' }; }
  for (const node of layout.nodes) {
    for (const key of ['x', 'y', 'width', 'height']) node[key] *= WORLD;
    for (const key of Object.keys(node.allocation)) node.allocation[key] *= WORLD;
  }
  layout.byKey = Object.fromEntries(layout.nodes.map(node => [node.key, node]));
  for (const route of layout.routes) for (const point of [route.start, ...route.points, route.end]) { point.x *= WORLD; point.y *= WORLD; }
  for (const key of ['x', 'y', 'width', 'height']) layout.bounds[key] *= WORLD;
  for (const level of layout.levels) for (const key of ['start', 'size', 'gapAfter']) level[key] *= WORLD;
  layout.worldUnitScale = WORLD;
  layout.normalization.minimumWidth *= WORLD; layout.normalization.minimumHeight *= WORLD;
  layout.normalization.largestRequiredZoom /= WORLD;
  const bottom = Math.max(...baseline.nodes.filter(node => node.parentId === entry.rootId).map(node => node.y + node.height));
  recipe.nodes.push({ key: 'collection/new-tree', canvas: 'collection', kind: 'canvas-link', title: workTitle, tag: workTitle, target: 'tree', collapsed: true, x: 80, y: bottom + 160, width: 720, height: 300, sizeMode: 'fixed', childPosition: 'manual', style: { ...common, titleSize: 30, fontSize: 18, portalStyle: t.portal, portalColor: t.accent, padding: 12, contentSizing: 'fixed' } });
  if (prior) {
    const nodeKeys = new Set(recipe.nodes.map(node => node.key)), linkKeys = new Set(recipe.links.map(link => link.key));
    const removeNodes = Object.keys(prior.aliases.nodes).filter(key => !nodeKeys.has(key));
    const removeLinks = Object.keys(prior.aliases.links).filter(key => !linkKeys.has(key));
    if (removeNodes.length || removeLinks.length) recipe.remove = { nodes: removeNodes, links: removeLinks };
  }
  const result = applyRecipe(prior?.project ?? baseline, recipe);
  const project = commitSnapshots(result.project);
  project.snapshots = { ...project.snapshots, ...copy(baseline.snapshots) };
  assert.deepEqual(validateProject(project), []);
  for (const kind of ['canvases', 'nodes', 'links', 'assets']) for (const original of baseline[kind]) assert.deepEqual(project[kind].find(item => item.id === original.id), original, `Existing ${kind} changed: ${original.id}`);
  for (const [id, snapshot] of Object.entries(baseline.snapshots)) assert.deepEqual(project.snapshots[id], snapshot);
  assert.equal(project.id, baseline.id); assert.equal(project.revision, baseline.revision);
  const [viewer, css] = await Promise.all(['viewer.js', 'scene.css'].map(name => readFile(path.join(ROOT, 'dist', name), 'utf8')));
  const manifest = { slug, title: workTitle, orientation, projectId: project.id, baselineRevision: baseline.revision, baselineSHA256: hash(baseline), draftSHA256: hash(project), sourceFile: filename, sourceSHA256: hash(sourceBytes), newWorkId: result.aliases.canvases.tree,
    records: records.map(record => ({ key: record.key, parent: record.parent, name: record.name, tag: record.tag, url: record.url, nodeId: result.aliases.nodes[record.key] })), layout, clearance, counts: { newRecords: records.length, newNativeJoints: recipe.nodes.filter(node => node.kind === 'junction').length, worksBefore: baseline.canvases.length, worksAfter: project.canvases.length, nodes: project.nodes.length, links: project.links.length, assets: project.assets.length }, preservedOriginals: true, csvName, csvSHA256: hash(csvBytes), initiallyExpanded: [...sampleExpanded] };
  await Promise.all([save(dir, 'draft.json', project), save(dir, 'aliases.json', result.aliases), save(dir, 'recipe.json', recipe), save(dir, 'manifest.json', manifest), save(dir, 'inspection.json', inspectProject(project)), save(dir, 'publication.html', buildPublication(project, viewer, css)), save(dir, 'guide.md', guide)]);
  console.log(JSON.stringify({ slug, ...manifest.counts, hash: manifest.draftSHA256, treeBounds: layout.bounds, normalization: layout.normalization, directory: dir }));
}
for (const slug of process.argv.slice(2).length ? process.argv.slice(2) : ['csharp', 'cpp']) await compose(slug);
