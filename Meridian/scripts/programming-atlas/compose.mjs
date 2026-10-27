/** Authored programming collections. Produces local recipes and reviewed drafts;
 * no live host writes or application deployment. */
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { applyRecipe, inspectProject } from '../../src/core/authoring.ts';
import { newProject, commitSnapshots } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';
import { buildPublication } from '../../src/publication.ts';

const ROOT = fileURLToPath(new URL('../../', import.meta.url));
const OUT = path.join(ROOT, 'output/programming-layouts-20261020');
const json = async file => JSON.parse((await readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const save = async (file, value) => { await mkdir(path.dirname(file), { recursive: true }); await writeFile(file, typeof value === 'string' ? value : JSON.stringify(value, null, 2) + '\n'); };
const themes = {
  cpp: { bg: '#0D1825', panel: '#172B3B', soft: '#132433', ink: '#E0EDF3', muted: '#A3BDCD', accent: '#6CCFC5', line: '#5D9BA6', portal: 'passage' },
  csharp: { bg: '#171727', panel: '#282440', soft: '#211E36', ink: '#EEE9FF', muted: '#C0B6DA', accent: '#BEA3F4', line: '#9680C7', portal: 'doorway' },
};
const csv = table => [table.columns, ...table.rows].map(row => row.map(value => '"' + String(value ?? '').replaceAll('"', '""') + '"').join(',')).join('\r\n') + '\r\n';
function packet(data) {
  const lines = [`# ${data.title} — language and layout atlas`, '', data.versionScope, '', data.subtitle,
    '', '## Scope', '', 'A curated, expandable map of the core language, standard library / BCL, representative APIs, and selected ecosystem tools. It is not an exhaustive enumeration of all overloads or third-party packages. Follow the official indexes for complete, version-specific references.', ''];
  for (const key of ['horizontal', 'vertical', 'radial', 'library', 'api', 'ecosystem']) {
    const section = data[key]; lines.push(`## ${section.title}`, '', section.subtitle, '');
    for (const group of section.stages ?? section.groups ?? section.families) {
      lines.push(`### ${group.title}`, '', group.summary, '');
      for (const item of group.details ?? group.items) {
        lines.push(`#### ${item.title}`, '', item.text, '');
        if (item.code) lines.push('```' + (data.slug === 'cpp' ? 'cpp' : 'csharp'), item.code, '```', '');
        const source = data.sources.find(source => source.id === item.sourceId);
        if (source) lines.push(`Reference: ${source.title} — ${source.url}`, '');
      }
    }
  }
  lines.push('## Primary sources', '');
  for (const source of data.sources) lines.push(`- ${source.id}: ${source.title} — ${source.url}`);
  return lines.join('\n') + '\n';
}

export async function composeLanguage(data) {
  const t = themes[data.slug]; if (!t) throw new Error('Unknown language');
  const directory = path.join(OUT, data.slug); await mkdir(directory, { recursive: true });
  const baseFile = path.join(directory, 'base.json');
  let base; try { base = await json(baseFile); } catch (error) { if (error.code !== 'ENOENT') throw error; base = newProject(`${data.title} · Language & Layout Atlas`); await save(baseFile, base); }
  // Recompose using the previous aliases so QA refinements keep identities.
  let prior; try { prior = { project: await json(path.join(directory, 'draft.json')), aliases: await json(path.join(directory, 'aliases.json')) }; } catch (error) { if (error.code !== 'ENOENT') throw error; }
  const recipe = { version: 1, title: `${data.title} · Language & Layout Atlas`, entryCanvas: 'overview', canvases: [], nodes: [], assets: [], links: [], routes: [], ...(prior ? { bindings: prior.aliases } : {}) };
  const common = { fontFamily: 'sans', fontSize: 18, titleSize: 26, fill: t.panel, border: t.line, textColor: t.ink, accent: t.accent, shape: 'rectangle', radius: 0, textAlign: 'left', padding: 16, childPadding: 8, contentGap: 10, contentSizing: 'fixed' };
  const add = (work, key, title, text, x, y, width, height, extra = {}) => {
    const node = { key: `${work}/${key}`, canvas: work, kind: 'media', title, tag: `${data.title} · ${work} · ${title || key}`.slice(0, 250), text, contentType: 'text', x, y, width, height, sizeMode: 'fixed', childPosition: 'manual', style: { ...common, ...extra.style }, ...extra };
    node.style = { ...common, ...extra.style }; recipe.nodes.push(node); return node.key;
  };
  const label = (work, key, title, text, x, y, width, height, sizes = {}) => add(work, key, title, text, x, y, width, height, { style: { ...sizes, shape: 'none', variant: 'label', padding: 0, borderWidth: 0 } });
  const portal = (work, key, title, target, x, y, width = 280, height = 185) => add(work, key, title, '', x, y, width, height, { kind: 'canvas-link', target, style: { titleSize: 22, textAlign: 'center', portalStyle: t.portal, portalColor: t.accent, fill: t.soft } });
  const heading = (work, title, subtitle, width = 1800) => {
    label(work, 'eyebrow', `${data.title.toUpperCase()} / FIELD ATLAS`, '', 80, 40, 1200, 35, { titleSize: 14, textColor: t.accent });
    label(work, 'heading', title, subtitle, 80, 95, width, 155, { titleSize: 46, fontSize: 20 });
    if (work !== 'overview') {
      portal(work, 'back', 'Collection', 'overview', width + 150, 70, 180, 150);
      if (work !== 'sources') portal(work, 'sources', 'Sources & downloads', 'sources', width + 150, 265, 180, 150);
    }
  };
  const itemNode = (work, parent, item, index, key) => {
    const code = item.code ? `\n\n${item.code}` : '';
    const text = `${item.text}${code}\n\nSOURCE · ${item.sourceId}`;
    const runs = item.code ? [{ start: item.text.length + 2, end: item.text.length + 2 + item.code.length, style: { fontFamily: 'mono', fontSize: 16, color: t.accent } }] : undefined;
    return add(work, key, item.title, text, 0, 0, 720, 340, { parent, cell: { column: 1, row: index + 1 }, childPosition: 'auto', ...(runs ? { textRuns: runs } : {}), style: { titleSize: 25, fontSize: 18, fill: t.soft, padding: 18 } });
  };
  const disclosure = (work, key, group, x, y, width = 320, height = 210, offset = { x: 0, y: 40 }) => {
    const items = group.details ?? group.items;
    const parent = add(work, key, group.title, group.summary, x, y, width, height, {
      collapsed: true, childLayout: 'spatial', containerVariant: 'window', childArrangement: 'grid', childOffset: offset,
      grid: { columns: 1, rows: Math.max(1, items.length), cellWidth: 720, cellHeight: 340, windowColumns: 1, windowRows: 1, scrollDirection: 'vertical', showHeaders: false },
      style: { titleSize: 27, fontSize: 18, fill: t.panel },
    });
    items.forEach((item, index) => itemNode(work, parent, item, index, `${key}/note-${index + 1}`));
    return parent;
  };
  for (const work of ['overview', 'horizontal', 'vertical', 'radial', 'library', 'api', 'ecosystem', 'sources']) {
    const title = work === 'overview' ? `${data.title} · Start here` : work === 'sources' ? `${data.title} · Sources & downloads` : `${data.title} · ${data[work].title}`;
    recipe.canvases.push({ key: work, ...(work === 'overview' ? { existingId: base.entryCanvasId } : {}), title,
      snapping: { stickyEdges: false, smartAlign: false, snapToGrid: false }, style: { background: t.bg, gridVisible: false, portalStyle: t.portal, portalColor: t.accent } });
  }
  heading('overview', `${data.title} — a language in space`, data.subtitle, 1420);
  label('overview', 'scope', 'A practical atlas, with explicit scope', data.slug === 'cpp' ? 'Portable C++20, with marked C++23 additions. Core language, standard-library families, representative APIs, and selected ecosystem tools. Check implementation support; the full reading packet explains the version boundary.' : 'C# 14 / .NET 10. Core language, the base class library, representative APIs, and selected frameworks. Engine runtime compatibility is called out separately. Full signatures and version details belong to the official references.', 80, 285, 660, 230, { titleSize: 28, fontSize: 19 });
  label('overview', 'legend', 'Space carries meaning', 'HORIZONTAL · steps in a transformation\nVERTICAL · layers of responsibility\nRADIAL · alternatives around one question\n\nDouble-click a topic tile to reveal its scrolling notes. Single-click a portal to enter a Work.', 820, 285, 650, 230, { titleSize: 28, fontSize: 19 });
  ['horizontal', 'vertical', 'radial'].forEach((key, index) => portal('overview', `pattern-${key}`, `${index + 1}. ${key[0].toUpperCase() + key.slice(1)}\n${data[key].title}`, key, 80 + index * 490, 565, 420, 230));
  label('overview', 'reference-heading', 'Then use the reference desks', 'Search for a type or operation in Explore. Every SOURCE identifier resolves in Sources & downloads.', 80, 865, 1450, 100, { titleSize: 30 });
  portal('overview', 'library', 'Library atlas', 'library', 80, 1020, 370, 230);
  portal('overview', 'api', 'Functions & API field guide', 'api', 510, 1020, 500, 230);
  portal('overview', 'ecosystem', 'Ecosystem & tools', 'ecosystem', 1070, 1020, 370, 230);
  portal('overview', 'sources', 'Sources & downloadable catalogs', 'sources', 80, 1320, 620, 190);
  label('overview', 'design-note', 'Three patterns. One coherent visual language.', 'The same typography, palette, evidence notation and disclosure controls carry across this collection. Layout changes when the relationship changes.', 820, 1320, 620, 190, { titleSize: 26, fontSize: 19 });

  heading('horizontal', data.horizontal.title, data.horizontal.subtitle, 2300);
  label('horizontal', 'direction', 'READ LEFT → RIGHT', 'Each connection means “produces input for the next stage.” The upper route highlights a cross-stage contract without passing through another tile.', 80, 270, 2200, 80, { titleSize: 16, fontSize: 17, textColor: t.muted });
  const horizontal = data.horizontal.stages.map((group, index) => disclosure('horizontal', `stage-${index + 1}`, { ...group, title: `${String(index + 1).padStart(2, '0')} · ${group.title.replace(/^\d+\s*[·•:.-]\s*/, '')}` }, 80 + index * 410, 430, 320, 250, { x: 0, y: 40 + index % 2 * 400 }));
  horizontal.forEach(key => {
    const node = recipe.nodes.find(node => node.key === key);
    node.expansionTray = { offset: { x: 0, y: 300 }, size: { width: 390, height: 600 }, layoutVersion: 3 };
    delete node.childOffset;
  });
  for (let index = 1; index < horizontal.length; index++) recipe.links.push({ key: `horizontal/flow-${index}`, canvas: 'horizontal', source: horizontal[index - 1], target: horizontal[index], relation: 'chronological', direction: 'forward', sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: .5 }, style: { color: t.line, width: 2 } });
  if (horizontal.length >= 5) recipe.routes.push({ key: 'horizontal/contract', canvas: 'horizontal', source: horizontal[1], target: horizontal[4], relation: 'generic', direction: 'forward', label: data.slug === 'cpp' ? 'symbol + ABI contract' : 'metadata + runtime contract', sourceAnchor: { x: .5, y: 0 }, targetAnchor: { x: .5, y: 0 }, style: { color: t.accent, width: 2 }, via: [{ key: 'horizontal/contract-out', x: 650, y: 380, variant: 'pivot' }, { key: 'horizontal/contract-in', x: 1880, y: 380, variant: 'pivot' }] });

  heading('vertical', data.vertical.title, data.vertical.subtitle, 1500);
  label('vertical', 'direction', 'READ TOP → BOTTOM', 'Each layer adds a responsibility. The spine describes a learning path, not a claim that every type inherits from the one above it.', 80, 275, 650, 120, { titleSize: 16, fontSize: 18, textColor: t.muted });
  label('vertical', 'disclosure-note', 'A reading pane opens to the right', 'Each layer keeps its own place on the spine. Its notes are an adjacent scrolling surface, rather than a growing wall of text.', 810, 300, 660, 100, { titleSize: 20, fontSize: 18, textColor: t.muted });
  const vertical = data.vertical.stages.map((group, index) => {
    const key = disclosure('vertical', `layer-${index + 1}`, { ...group, title: `${String(index + 1).padStart(2, '0')} · ${group.title.replace(/^\d+\s*[·•:.-]\s*/, '')}` }, 100, 450 + index * 360, 580, 240, { x: 760, y: -268 });
    const node = recipe.nodes.find(node => node.key === key), grid = { ...node.grid };
    for (const field of ['containerVariant', 'childArrangement', 'grid', 'childOffset', 'expansionTray']) delete node[field];
    node.unset = ['containerVariant', 'childArrangement', 'grid', 'childOffset', 'expansionTray'];
    const pane = add('vertical', `layer-${index + 1}/pane`, '', '', 860, 450 + index * 360, 740, 350, { parent: key, kind: 'container', collapsed: false, childLayout: 'contained', childArrangement: 'grid', containerVariant: 'window', grid, childPosition: 'manual', placement: 'free', style: { padding: 5, childPadding: 5, fill: t.soft } });
    recipe.nodes.filter(child => child.parent === key && child.key !== pane).forEach(child => { child.parent = pane; });
    recipe.links.push({ key: `vertical/notes-${index + 1}`, canvas: 'vertical', source: key, target: pane, parent: key, relation: 'generic', direction: 'none', sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: .5 }, style: { color: t.line, width: 2 } });
    return key;
  });
  for (let index = 1; index < vertical.length; index++) recipe.links.push({ key: `vertical/progression-${index}`, canvas: 'vertical', source: vertical[index - 1], target: vertical[index], relation: 'chronological', direction: 'forward', sourceAnchor: { x: .5, y: 1 }, targetAnchor: { x: .5, y: 0 }, style: { color: t.line, width: 2 } });

  heading('radial', data.radial.title, data.radial.subtitle, 1900);
  const center = { x: 1300, y: 1470 }, radius = 900;
  const hub = add('radial', 'hub', 'Choose by behavior', `Ownership · access\nordering · mutation\n${data.slug === 'cpp' ? 'extent · locality' : 'concurrency'}\n\nFamilies, not ranks.`, center.x - 190, center.y - 190, 380, 380, { style: { shape: 'circle', textAlign: 'center', padding: 16, titleSize: 28, fontSize: 19 } });
  data.radial.groups.forEach((group, index, groups) => {
    const angle = -Math.PI / 2 + index * Math.PI * 2 / groups.length;
    const x = center.x + Math.cos(angle) * radius - 300, y = center.y + Math.sin(angle) * radius - 180;
    const key = disclosure('radial', `family-${index + 1}`, group, x, y, 600, 360);
    const node = recipe.nodes.find(node => node.key === key);
    Object.assign(node, { kind: 'container', childLayout: 'contained', containerSizes: { collapsed: { width: 600, height: 360, sizeMode: 'fixed' }, expanded: { width: 600, height: 360, sizeMode: 'fixed' } } });
    delete node.childOffset;
    recipe.links.push({ key: `radial/classification-${index + 1}`, canvas: 'radial', source: hub, target: key, relation: 'generic', direction: 'none', style: { color: t.line, width: 2 } });
  });
  label('radial', 'comparison-note', 'A classification map, not an inheritance tree', `Expand a family to inspect its types in place, then scroll within it. ${data.slug === 'cpp' ? 'Sequence containers, associative owners, access adaptors, and non-owning views' : 'Sequence, associative, concurrent, immutable, and view abstractions'} answer different questions; avoid a universal complexity ranking.`, 660, 2670, 1280, 140, { titleSize: 25, fontSize: 19 });

  for (const work of ['library', 'api']) {
    const section = data[work]; heading(work, section.title, section.subtitle, 1850);
    const families = section.families;
    label(work, 'index-title', 'READING INDEX', 'The numbered families organize the scrolling reference desk. Explore searches every original note.', 80, 300, 340, 110, { titleSize: 17, fontSize: 17, textColor: t.muted });
    families.forEach((family, index) => label(work, `index-${index + 1}`, `${String(index + 1).padStart(2, '0')}  ${family.title}`, '', 80, 445 + index * 67, 340, 58, { titleSize: 20, textColor: t.muted }));
    const entries = families.flatMap((family, index) => family.items.map(item => ({ ...item, title: `${String(index + 1).padStart(2, '0')} / ${item.title}` })));
    const panel = add(work, 'desk', '', '', 480, 315, 1480, 850, { kind: 'container', collapsed: false, childLayout: 'contained', childArrangement: 'grid', containerVariant: 'window', grid: { columns: 2, rows: Math.ceil(entries.length / 2), cellWidth: 720, cellHeight: 400, windowColumns: 2, windowRows: 2, scrollDirection: 'vertical', showHeaders: false }, style: { padding: 8, childPadding: 8, fill: t.soft } });
    entries.forEach((item, index) => {
      const key = itemNode(work, panel, item, index, `entry-${index + 1}`), node = recipe.nodes.find(node => node.key === key);
      node.cell = { column: index % 2 + 1, row: Math.floor(index / 2) + 1 }; node.width = 720; node.height = 400;
    });
    label(work, 'scroll-note', 'SCROLL INSIDE THE DESK', `${entries.length} detailed notes · ${families.length} families. For dense lookup, use the full CSV catalogs in Sources & downloads. Scrollbars reveal the remaining rows; the Work stays compact.`, 480, 1240, 1480, 115, { titleSize: 18, fontSize: 18, textColor: t.muted });
  }

  heading('ecosystem', data.ecosystem.title, data.ecosystem.subtitle, 1800);
  data.ecosystem.families.forEach((family, index) => {
    const column = index % 3, row = Math.floor(index / 3);
    const key = disclosure('ecosystem', `domain-${index + 1}`, family, 80 + column * 620, 340 + row * 800, 480, 235);
    const parent = recipe.nodes.find(node => node.key === key); parent.grid.cellWidth = 520; parent.grid.cellHeight = 380;
  });
  label('ecosystem', 'boundary', 'Selected ecosystem, explicit boundaries', 'These are representative tools and libraries, not a claim to catalog every package. Check the official documentation for supported platforms, licensing, compatibility and installation details.', 80, 340 + Math.ceil(data.ecosystem.families.length / 3) * 800, 1800, 125, { titleSize: 24, fontSize: 19, textColor: t.muted });

  heading('sources', 'Sources & downloadable catalogs', `Primary references, full reading packet, and searchable tabular indexes. ${data.versionScope}`, 1850);
  const fullText = packet(data); await save(path.join(directory, `${data.slug}-reading-packet.md`), fullText);
  recipe.assets.push({ key: 'reading-packet', name: `${data.title} — full reading packet.md`, mime: 'text/markdown', base64: Buffer.from(fullText).toString('base64') });
  add('sources', 'reading-packet', 'Full reading packet', '', 80, 320, 780, 360, { kind: 'document', asset: 'reading-packet', contentType: 'asset', style: { titleSize: 28, fontSize: 16, fill: t.soft } });
  label('sources', 'coverage', 'Coverage and evidence', 'SOURCE identifiers on the notes match the primary links in the reference desk. The CSVs are dense lookup tools; the diagrams demonstrate relationships. Code examples are illustrative snippets, not complete production programs or universal performance promises.', 80, 745, 780, 200, { titleSize: 28, fontSize: 20 });
  const catalogs = add('sources', 'catalog-gallery', '', '', 80, 1020, 780, 480, { kind: 'container', containerVariant: 'window', childLayout: 'contained', childArrangement: 'grid', collapsed: false, grid: { columns: 1, rows: data.tables.length, cellWidth: 760, cellHeight: 450, windowColumns: 1, windowRows: 1, scrollDirection: 'vertical', showHeaders: false }, style: { childPadding: 8, padding: 8, fill: t.soft } });
  for (const [index, table] of data.tables.entries()) {
    const bytes = csv(table), assetKey = `catalog-${index + 1}`;
    recipe.assets.push({ key: assetKey, name: table.filename, mime: 'text/csv', base64: Buffer.from(bytes).toString('base64') });
    add('sources', assetKey, `${table.title} · ${table.rows.length} rows`, '', 90, 1030, 760, 450, { parent: catalogs, cell: { column: 1, row: index + 1 }, childPosition: 'auto', kind: 'document', asset: assetKey, contentType: 'asset', style: { titleSize: 25, fontSize: 16 } });
    await save(path.join(directory, table.filename), bytes);
  }
  label('sources', 'catalog-note', 'SCROLL THROUGH THE CATALOG DESK', 'Scroll within the desk to choose a CSV. Open its table preview or download the original file for every row; the on-Work surface stays bounded.', 80, 1560, 780, 125, { titleSize: 18, fontSize: 18, textColor: t.muted });
  label('sources', 'primary-title', 'PRIMARY REFERENCE DESK', 'Open the official documentation. Keep version and implementation caveats. Scroll inside this desk for the remaining sources.', 960, 320, 920, 120, { titleSize: 30, fontSize: 19 });
  const references = add('sources', 'reference-desk', '', '', 960, 485, 920, 1000, { kind: 'container', collapsed: false, childLayout: 'contained', childArrangement: 'grid', containerVariant: 'window', grid: { columns: 1, rows: data.sources.length, cellWidth: 900, cellHeight: 195, windowColumns: 1, windowRows: 5, scrollDirection: 'vertical', showHeaders: false }, style: { padding: 8, childPadding: 8, fill: t.soft } });
  data.sources.forEach((source, index) => add('sources', `primary-${index + 1}`, `${source.id} · ${source.title}`, '', 0, 0, 900, 195, { parent: references, cell: { column: 1, row: index + 1 }, childPosition: 'auto', contentType: 'link', hyperlinks: [{ url: source.url, label: 'Open official reference' }], style: { titleSize: 22, fontSize: 17, fill: t.ink, textColor: t.bg } }));
  const result = applyRecipe(prior?.project ?? base, recipe), project = commitSnapshots(result.project);
  const problems = validateProject(project); if (problems.length) throw new Error(JSON.stringify(problems));
  const [viewer, css] = await Promise.all(['viewer.js', 'scene.css'].map(file => readFile(path.join(ROOT, 'dist', file), 'utf8')));
  await Promise.all([save(path.join(directory, 'recipe.json'), recipe), save(path.join(directory, 'draft.json'), project), save(path.join(directory, 'aliases.json'), result.aliases), save(path.join(directory, 'inspection.json'), inspectProject(project)), save(path.join(directory, 'publication.html'), buildPublication(project, viewer, css)), save(path.join(directory, 'sources.json'), data.sources)]);
  const summary = { language: data.title, projectId: project.id, works: project.canvases.length, nodes: project.nodes.length, links: project.links.length, assets: project.assets.length, sources: data.sources.length, catalogRows: data.tables.reduce((sum, table) => sum + table.rows.length, 0), directory };
  await save(path.join(directory, 'summary.json'), summary); return summary;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  for (const slug of process.argv.slice(2).length ? process.argv.slice(2) : ['cpp', 'csharp']) {
    const data = await json(path.join(ROOT, 'content/programming-atlas', `${slug}.json`));
    console.log(JSON.stringify(await composeLanguage(data)));
  }
}
