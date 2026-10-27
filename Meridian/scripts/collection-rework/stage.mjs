/** Stage an identity-preserving, content-preserving editorial containment pass.
 * Reads the captured originals, never contacts or writes the live host.
 */
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { applyRecipe, inspectProject } from '../../src/core/authoring.ts';
import { commitSnapshots } from '../../src/core/model.ts';
import { validateProject } from '../../src/core/rules.ts';
import { navigationIndex } from '../../src/core/navigation.ts';
import { splitLinkWithJunction } from '../../src/core/connections.ts';
import { numbersDemo } from './numbers-demo.mjs';

const directory = 'output/collection-rework';
const inventory = JSON.parse(await readFile(`${directory}/inventory.json`, 'utf8'));
const summaries = [];
for (const entry of inventory) {
  const folder = `${directory}/${entry.id}`;
  const saved = JSON.parse(await readFile(`${folder}/saved.json`, 'utf8'));
  const recovery = entry.recovery ? JSON.parse(await readFile(`${folder}/recovery.json`, 'utf8')) : null;
  const base = recovery?.project ?? saved;
  const draft = structuredClone(base), changes = [], nodes = new Map(draft.nodes.map(n => [n.id, n]));
  let serial = 0;
  const made = new Set();
  const descendants = id => {
    const result = [], queue = [id];
    while (queue.length) { const parent = queue.pop(); for (const n of draft.nodes) if (n.parentId === parent) { result.push(n); queue.push(n.id); } }
    return result;
  };
  const allBranch = list => [...new Map(list.flatMap(n => [n, ...descendants(n.id)]).map(n => [n.id, n])).values()];
  const bbox = list => ({ x: Math.min(...list.map(n => n.x)), y: Math.min(...list.map(n => n.y)), right: Math.max(...list.map(n => n.x + n.width)), bottom: Math.max(...list.map(n => n.y + n.height)) });
  const move = (list, dx, dy) => {
    const group = allBranch(list), ids = new Set(group.map(n => n.id));
    for (const n of group) { n.x += dx; n.y += dy; }
    for (const l of draft.links) if (l.points && ids.has(l.sourceId) && ids.has(l.targetId)) l.points = l.points.map(p => ({ x: p.x + dx, y: p.y + dy }));
  };
  const place = (n, x, y) => move([n], x - n.x, y - n.y);
  const add = (c, kind, title, x, y, width, height, extra = {}) => {
    const id = `rework-${++serial}`;
    const n = { id, canvasId: c.id, parentId: c.rootId, kind, title, x, y, width, height, ...extra };
    draft.nodes.push(n); nodes.set(id, n); made.add(id); return n;
  };
  const palette = c => {
    const originals = base.nodes.filter(n => n.canvasId === c.id);
    const style = originals.find(n => n.kind === 'container')?.style ?? originals.find(n => n.style?.fill && n.style?.variant !== 'label')?.style ?? {};
    return { fill: style.fill ?? '#203D4D', border: style.accent ?? '#7DACA7', accent: style.accent ?? '#D4B36C', textColor: style.textColor ?? '#F2EADF', titleSize: 23, fontSize: 17, fontFamily: 'sans', radius: 14 };
  };
  const frame = (c, title, members, { collapsed = false, x, y, text = '', width, padding = 28, head = 116 } = {}) => {
    if (!members.length) return null;
    const b = bbox(allBranch(members));
    const nx = x ?? b.x - padding, ny = y ?? b.y - head;
    move(members, nx + padding - b.x, ny + head - b.y);
    const after = bbox(allBranch(members));
    const outer = add(c, 'container', title, nx, ny, width ?? after.right - nx + padding, after.bottom - ny + padding, { collapsed, text, style: palette(c) });
    for (const n of members) n.parentId = outer.id;
    return outer;
  };
  const rootNodes = c => draft.nodes.filter(n => n.parentId === c.rootId);
  const note = (c, strategy) => changes.push({ canvasId: c.id, title: c.title, strategy });
  const isBack = n => !/^\d+\s*\//.test(n.title) && /back|directory|overview|previous|next|continue|visual atlas/i.test(n.title);
  const addPreviewChildren = (c, launcher, wanted) => {
    const targets = wanted.map(pattern => draft.canvases.find(t => pattern.test(t.title))).filter(Boolean);
    if (!targets.length || descendants(launcher.id).length) return;
    const originalHeight = launcher.height;
    launcher.collapsed = false; launcher.width = Math.max(launcher.width, 390);
    launcher.height = 180 + targets.length * 205;
    targets.forEach((target, i) => add(c, 'canvas-link', target.title, launcher.x + 24, launcher.y + 180 + i * 205, launcher.width - 48, 180, { parentId: launcher.id, targetId: target.rootId, text: 'Open this reading canvas.', style: { ...palette(c), titleSize: 22 } }));
    return originalHeight;
  };

  for (const c of draft.canvases) {
    const original = base.nodes.filter(n => n.canvasId === c.id && n.kind !== 'canvas');
    let roots = rootNodes(c);
    // A compact diagram stays visible; long prose, sources and related reading are
    // independently expandable. Each expanded branch owns a separate column.
    if (/^[IO]\d\d ·/.test(c.title)) {
      const events = roots.filter(n => /^\d$/.test(n.title));
      const account = roots.find(n => n.title === 'The account');
      const people = roots.find(n => n.title === 'People and powers');
      const lens = roots.find(n => n.title === 'Interpretive lens');
      const source = roots.find(n => n.kind === 'document');
      const navigation = roots.filter(n => n.kind === 'canvas-link' && isBack(n));
      const related = roots.filter(n => n.kind === 'canvas-link' && !navigation.includes(n));
      navigation.forEach((n, i) => { place(n, 50 + i * 360, 267); n.width = 335; n.height = 155; });
      events.forEach((n, i) => { place(n, 78, 555 + i * 150); n.width = 310; n.height = 125; });
      frame(c, 'Follow the events', events, { x: 50, y: 439, width: 366, text: 'The sequence remains visible while you explore the other branches.' });
      const reading = [account, source, ...related].filter(Boolean);
      place(account, 498, 605); account.width = 704; account.height = 730;
      if (source) { place(source, 498, 1380); source.width = 704; source.height = 255; }
      related.forEach((n, i) => { place(n, 498 + i * 365, 1680); n.width = 339; n.height = 180; });
      frame(c, 'Read the account', reading, { x: 470, y: 439, head: 166, collapsed: true, text: 'Expand for the full account, the original book and connections to other readings.' });
      place(people, 1290, 555); people.width = 340; people.height = 205;
      place(lens, 1290, 805); lens.collapsed = true;
      frame(c, 'People & interpretation', [people, lens], { x: 1262, y: 439, width: 396, text: 'Keep people, themes and narrative position separate from the event sequence.' });
      note(c, 'event sequence + independent account/source and interpretation branches');
      continue;
    }
    // Preserve attributed parentage exactly. Formal containment makes the whole
    // family branch movable and collapsible without turning kinship into ownership.
    if (/^G\d+\.\d+ ·/.test(c.title)) {
      const graph = roots.filter(n => n.kind === 'reference' || (n.kind === 'text' && n.y >= 260));
      if (graph.length) {
        const oldRows = [...new Set(graph.map(n => n.y))].sort((a, b) => a - b);
        const newRows = new Map(); let cursor = oldRows[0];
        for (const row of oldRows) {
          newRows.set(row, cursor);
          const cards = graph.filter(n => n.y === row);
          for (const n of cards) { n.height = n.title.length > 24 ? 180 : 150; n.style = { ...n.style, fontSize: 18, titleSize: 23 }; }
          cursor += Math.max(...cards.map(n => n.height)) + 85;
        }
        const ymap = y => {
          if (y <= oldRows[0]) return y;
          for (let i = 1; i < oldRows.length; i++) if (y < oldRows[i]) return newRows.get(oldRows[i - 1]) + (y - oldRows[i - 1]) / (oldRows[i] - oldRows[i - 1]) * (newRows.get(oldRows[i]) - newRows.get(oldRows[i - 1]));
          return newRows.get(oldRows.at(-1)) + (y - oldRows.at(-1)) * .72;
        };
        const ids = new Set(graph.map(n => n.id));
        for (const l of draft.links) if (l.points && ids.has(l.sourceId) && ids.has(l.targetId)) l.points = l.points.map(p => ({ x: p.x, y: ymap(p.y) }));
        for (const n of graph) n.y = newRows.get(n.y);
        const group = frame(c, 'Selected family relationships', graph, { x: -45, y: 265, padding: 95, head: 126, text: '' });
        const footer = roots.filter(n => !graph.includes(n) && n.y >= 260);
        footer.forEach(n => place(n, n.x, group.y + group.height + 35));
        const docs = footer.filter(n => n.kind === 'document');
        if (docs.length) frame(c, 'Sources & variants', docs, { x: 1050, y: group.y + group.height + 35, collapsed: true, text: 'Open the attributed passages and alternative accounts.' });
        note(c, 'formal family graph + collapsible attributed source branch');
      }
      continue;
    }
    // Dossiers already had story containers, but no outer chapter or separate
    // source shelf. Preserve their individual expansion state under the new parent.
    if (/^D\d\d ·/.test(c.title)) {
      const stories = roots.filter(n => n.kind === 'container');
      if (stories.length) {
        stories.forEach(n => { n.collapsed = true; });
        const group = frame(c, 'Stories & alternative tellings', stories, { x: 25, y: 850, text: 'Expand one telling at a time. Each story retains its own source and detail.' });
        const sources = roots.filter(n => n.kind === 'document');
        sources.forEach((n, i) => place(n, 80 + i * 470, group.y + group.height + 175));
        frame(c, 'Source shelf', sources, { x: 50, y: group.y + group.height + 35, collapsed: true, head: 140, text: 'Descendant registers and ancient passages remain attached in full.' });
        roots.filter(n => n.kind === 'canvas-link').forEach((n, i) => place(n, 50 + i * 470, 680));
        note(c, 'nested story containers + source shelf');
      }
      continue;
    }
    // Greek/Roman pairs remain horizontal comparisons, with each pair owning its
    // children and relation. Readers can isolate a pair independently.
    if (/^R\d+ ·/.test(c.title)) {
      const pairs = roots.filter(n => n.kind === 'text' && n.y >= 260);
      const rows = [...new Set(pairs.map(n => n.y))].sort((a, b) => a - b);
      rows.forEach((row, i) => {
        const members = pairs.filter(n => n.y === row);
        const group = frame(c, members.map(n => n.title).join(' / '), members, { x: 25, y: 280 + i * 485, collapsed: false });
        group.style = { ...group.style, titleSize: 25 };
      });
      const footerY = 280 + rows.length * 485;
      roots.filter(n => n.kind === 'canvas-link').forEach(n => place(n, 1450, 280));
      const comparisonSources = roots.filter(n => n.kind === 'document');
      if (comparisonSources.length) frame(c, 'Comparison sources', comparisonSources, { x: 1450, y: 500, collapsed: true, text: 'Expand for full notes and the cited passages.' });
      note(c, 'independent Greek/Roman comparison containers');
      continue;
    }
    if (/^H\d+\.\d+ ·/.test(c.title) || /^(Iliad|Odyssey) · Books/.test(c.title)) {
      const readings = roots.filter(n => n.kind === 'text' && (/^Book \d+/.test(n.title) || /^H\d/.test(c.title) && n.y >= 260));
      const startY = /^H\d/.test(c.title) ? 280 : 330;
      readings.forEach((n, i) => {
        const width = Math.max(n.width + 56, 560), x = 25 + (i % 2) * (width + 40), y = startY + Math.floor(i / 2) * 555;
        frame(c, /^Book (\d+)/.test(n.title) ? `Book ${n.title.match(/^Book (\d+)/)[1]} · account` : `Reading ${i + 1} · passage and argument`, [n], { x, y, width, head: 105 });
      });
      const bottom = startY + Math.ceil(readings.length / 2) * 555;
      const notes = roots.filter(n => n.kind === 'text' && !readings.includes(n) && n.y >= 330);
      if (notes.length) frame(c, 'Reading questions & sources', notes, { x: 25, y: bottom + 30, collapsed: true, text: 'Expand for the interpretive questions and original reading links.' });
      roots.filter(n => n.kind === 'canvas-link' && n.y >= 330).forEach((n, i) => place(n, 550 + i * 490, bottom + 30));
      note(c, 'independently collapsible book or passage containers');
      continue;
    }
    // Hand-designed visual opening pages keep their typography, art and reading
    // paths. Source notes become real children of the relevant subject node.
    if (/^0[1-5] ·/.test(c.title) && roots.some(n => n.style?.variant === 'label') && roots.some(n => n.kind === 'image')) {
      if (c.id === draft.entryCanvasId) {
        const paths = roots.filter(n => n.kind === 'canvas-link' && n.y === 530);
        const previews = [[/^G10\.1/, /^D07/], [/^O09/, /^40 ·/], [/^R1 ·/, /^50 ·/], [/^D18/, /^D19/]];
        paths.forEach((n, i) => addPreviewChildren(c, n, previews[i] ?? []));
        if (paths.length) {
          // Dedicated columns have room to expand without covering a neighboring path.
          paths.forEach((n, i) => place(n, 50 + i * 450, 540));
          const lower = roots.filter(n => !paths.includes(n) && n.y >= 750);
          lower.forEach(n => place(n, n.x, n.y + 450));
          frame(c, 'Four ways into the collection', paths, { x: 25, y: 480, text: '' });
        }
        const notes = roots.filter(n => n.kind === 'document');
        const caution = roots.find(n => n.title === 'ONE TELLING IS NOT THE WHOLE TRADITION');
        if (notes.length && caution) { const branch = frame(c, 'Sources & reading principles', [caution, ...notes], { x: 25, y: 1290, collapsed: true, text: 'Keep alternative accounts and the original sources in view.' }); branch.width = Math.max(branch.width, 1000); }
        roots.filter(n => n.kind === 'canvas-link' && !paths.includes(n)).forEach(n => place(n, 450, 1290));
      } else {
        const docs = roots.filter(n => n.kind === 'document');
        if (docs.length) {
          const existing = roots.find(n => n.kind === 'container');
          if (existing) {
            const oldBottom = existing.y + existing.height;
            docs.forEach((n, i) => { n.parentId = existing.id; place(n, existing.x + 30 + i * 280, oldBottom + 32); });
            existing.height += 180; existing.collapsed = false;
            roots.filter(n => n.kind === 'canvas-link' && n.y >= oldBottom).forEach(n => place(n, n.x, Math.max(n.y, existing.y + existing.height + 35)));
          } else frame(c, 'Sources & close reading', docs, { x: docs[0].x, y: Math.max(...roots.map(n => n.y + n.height)) + 30, collapsed: true, text: 'Expand to inspect the full passages and object provenance.' });
        }
        const family = roots.filter(n => /^(Hestia|Demeter|Hera|Hades|Poseidon|Zeus)$/i.test(n.title));
        if (family.length === 6) {
          const graph = roots.filter(n => n.y >= 155 && n.kind !== 'canvas-link' && n.kind !== 'document');
          const group = frame(c, 'A selected family, with attributed relationships', graph, { x: 25, y: 190, head: 116 });
          roots.filter(n => n.kind === 'canvas-link').forEach((n, i) => place(n, 50 + (i % 2) * 490, group.y + group.height + 40 + Math.floor(i / 2) * 225));
          const shelf = draft.nodes.find(n => n.canvasId === c.id && n.title === 'Sources & close reading');
          if (shelf) place(shelf, 1050, group.y + group.height + 40);
        }
      }
      note(c, 'illustrated composition with real chapter/source containment');
      continue;
    }
    // Directories are chapters on the canvas, with genuine parent nodes. Three
    // thematic columns retain independent visibility and are not a sidebar list.
    const launches = roots.filter(n => n.kind === 'canvas-link' && !isBack(n));
    if ((c.title.includes('Complete atlas directory') || c.title.includes('Begin here')) && launches.length >= 8) {
      const categories = [
        { title: 'Families & divine stories', test: /famil|zeus|gods &|origins|olympian|individual|genealogy/i, width: 420 },
        { title: 'Poems & their worlds', test: /iliad|odyssey|hesiod|epics|theogony|works and days/i, width: 460 },
        { title: 'Objects, sources & experiments', test: /./, width: 420 },
      ];
      const remaining = new Set(launches);
      roots.filter(n => n.kind === 'image').forEach(n => place(n, 1560, 45));
      categories.forEach((category, index) => {
        const members = [...remaining].filter(n => category.test.test(n.title));
        members.forEach((n, j) => { remaining.delete(n); place(n, 78 + index * 500, 530 + j * 245); n.width = category.width - 56; n.height = 215; });
        frame(c, category.title, members, { x: 50 + index * 500, y: 410, width: category.width, head: 120, collapsed: index === 2, text: index === 2 ? 'Expand for objects, sources and reading tools.' : '' });
      });
      roots.filter(n => !launches.includes(n) && n.y >= 410).forEach((n, i) => place(n, 1560, 520 + i * 210));
      note(c, 'three independently expandable thematic chapters');
      continue;
    }
    // Other reading directories and movement overviews retain their designed
    // order inside one explicit chapter container, not an implicit adjacency group.
    if (/^45\.\d+ ·/.test(c.title)) {
      const members = roots.filter(n => n.y >= 260 && !isBack(n));
      const group = frame(c, 'Read the evidence together', members, { x: 25, y: 265 });
      roots.filter(n => n.kind === 'canvas-link' && isBack(n)).forEach(n => place(n, n.x, group.y + group.height + 35));
      note(c, 'a collapsible thematic constellation retaining its central question');
      continue;
    }
    if (launches.length >= 3 && roots.filter(n => n.kind === 'text' && n.y > 250).length <= 2 && !roots.some(n => n.kind === 'input' || n.kind === 'joint')) {
      const b = bbox(allBranch(launches));
      const group = frame(c, /family/i.test(c.title) ? 'Explore the family branches' : /Iliad|Odyssey|sequence/i.test(c.title) ? 'Follow this reading sequence' : 'Explore this chapter', launches, { x: b.x - 25, y: b.y, text: '' });
      roots.filter(n => !launches.includes(n) && n.y >= b.y && (isBack(n) || n.kind === 'document' || n.y >= b.bottom - 5)).forEach(n => place(n, n.x, Math.max(n.y + 140, group.y + group.height + 35)));
      note(c, 'formal chapter with preserved authored reading order');
      continue;
    }
    // Detailed prose is attached to its subject as a child node, leaving the
    // subject's title and summary visible. Existing child containers stay intact.
    const storyGroup = roots.find(n => n.kind === 'container' && /Stories and reading questions/.test(n.title));
    if (storyGroup) {
      const prose = draft.nodes.filter(n => n.parentId === storyGroup.id && n.kind === 'text' && n.title !== 'Read across versions');
      prose.forEach((n, i) => {
        const wrapper = frame(c, n.title, [n], { x: n.x - 16, y: n.y, padding: 16, head: 116, collapsed: i > 0, text: 'Expand to read this telling.' });
        wrapper.parentId = storyGroup.id;
      });
      const across = draft.nodes.find(n => n.parentId === storyGroup.id && n.title === 'Read across versions');
      if (across) place(across, across.x, Math.max(...draft.nodes.filter(n => n.parentId === storyGroup.id && n.id !== across.id).map(n => n.y + n.height)) + 28);
      const members = draft.nodes.filter(n => n.parentId === storyGroup.id);
      const bottom = Math.max(...allBranch(members).map(n => n.y + n.height));
      const previousRight = storyGroup.x + storyGroup.width;
      storyGroup.height = Math.max(storyGroup.height, bottom - storyGroup.y + 32);
      storyGroup.width = Math.max(storyGroup.width, Math.max(...allBranch(members).map(n => n.x + n.width)) - storyGroup.x + 32);
      const additionalWidth = storyGroup.x + storyGroup.width - previousRight;
      if (additionalWidth > 0) move(roots.filter(n => n.id !== storyGroup.id && n.x >= previousRight), additionalWidth + 20, 0);
      note(c, 'story nodes serve as independently expandable containers');
      continue;
    }
    if (c.title === 'Start here') {
      const research = roots.find(n => n.kind === 'container');
      const docs = draft.nodes.filter(n => n.parentId === research?.id && n.kind === 'document');
      const text = draft.nodes.find(n => n.parentId === research?.id && n.kind === 'text');
      if (research && docs.length && text) {
        docs.forEach(n => { n.parentId = text.id; place(n, text.x + 20, text.y + 215); n.width = 260; });
        text.width = 295; text.height = 390; text.collapsed = true;
        research.height = Math.max(research.height, text.y + text.height - research.y + 30);
      }
      note(c, 'research note owns its original source attachment');
      continue;
    }
    if (c.title === 'Logic lab') {
      const packing = roots.find(n => n.kind === 'container');
      if (packing) {
        const outputs = draft.nodes.filter(n => n.parentId === packing.id && n.kind === 'output');
        // An output is an ordinary sibling of the calculation packing container,
        // so collapsing that calculation keeps the result inspectable.
        outputs.forEach(n => { n.parentId = c.rootId; });
        packing.collapsed = false;
        const children = draft.nodes.filter(n => n.parentId === packing.id);
        if (children.length) { const b = bbox(allBranch(children)); packing.width = b.right - packing.x + 32; packing.height = b.bottom - packing.y + 32; }
      }
      note(c, 'visible sibling result survives collapsing the calculation');
      continue;
    }
    // Smaller non-directory compositions keep their spatial argument. A source
    // shelf makes supporting documents independently collapsible where present.
    const docs = roots.filter(n => n.kind === 'document');
    if (docs.length && !roots.some(n => n.kind === 'input' || n.kind === 'joint')) {
      const bottom = Math.max(...roots.filter(n => !docs.includes(n)).map(n => n.y + n.height), 250);
      docs.forEach((n, i) => place(n, 78 + i * 380, bottom + 185));
      frame(c, 'Sources & further reading', docs, { x: 50, y: bottom + 45, head: 140, collapsed: true, text: 'Original attachments and full source notes stay with this canvas.' });
      note(c, 'independently collapsible source shelf');
    }
  }

  const recipe = { version: 1, nodes: [], links: [] };
  const originalNodes = new Map(base.nodes.map(n => [n.id, n]));
  for (const n of draft.nodes) {
    const old = originalNodes.get(n.id);
    if (!old) {
      const { id, canvasId, parentId, targetId, assetId, ...fields } = n;
      recipe.nodes.push({ key: id, canvas: canvasId, parent: parentId, ...fields, ...(targetId ? { target: targetId } : {}), ...(assetId ? { asset: assetId } : {}) });
    } else {
      const changed = Object.fromEntries(['x', 'y', 'width', 'height', 'collapsed', 'style'].filter(k => JSON.stringify(n[k]) !== JSON.stringify(old[k])).map(k => [k, n[k]]));
      if (n.parentId !== old.parentId) changed.parent = n.parentId;
      if (Object.keys(changed).length) recipe.nodes.push({ key: `existing-${recipe.nodes.length}`, existingId: n.id, ...changed });
    }
  }
  for (const l of draft.links) {
    const old = base.links.find(x => x.id === l.id);
    if (JSON.stringify(l.points) !== JSON.stringify(old.points)) recipe.links.push({ key: `route-${recipe.links.length}`, existingId: l.id, points: l.points });
  }
  if (entry.title === 'Meridian · First connections') {
    const demonstration = numbersDemo(base.entryCanvasId, base.canvases.find(c => c.id === base.entryCanvasId).rootId);
    recipe.canvases = demonstration.canvases;
    recipe.nodes.push(...demonstration.nodes);
    recipe.links.push(...demonstration.links);
    changes.push({ title: 'Numbers, decisions and junctions', strategy: 'new nested numeric and Boolean teaching canvas' });
  }
  const result = applyRecipe(base, recipe);
  for (const change of changes) if (!change.canvasId) change.canvasId = result.aliases.canvases['numbers-demo'];
  let candidate = result.project;
  const junctions = [];
  // The original six directed branches are retained, now sharing one explicitly
  // authored visual junction. No arithmetic is introduced into mythic kinship.
  const familyCanvas = candidate.canvases.find(c => c.title === '02 · A selected divine family');
  const six = familyCanvas && candidate.nodes.find(n => n.canvasId === familyCanvas.id && n.title === 'Six children in Hesiod');
  const branches = six ? candidate.links.filter(l => l.sourceId === six.id && l.kind === 'visual') : [];
  if (branches.length === 6) {
    const inserted = splitLinkWithJunction(candidate, branches[0].id, { x: six.x + six.width / 2, y: six.y + six.height + 32 }, { title: 'Hesiod’s six-child branch' });
    candidate = inserted.project;
    for (const old of branches) {
      const l = candidate.links.find(l => l.id === old.id), child = candidate.nodes.find(n => n.id === l.targetId);
      l.sourceId = inserted.nodeId;
      l.sourceAnchor = { x: .5, y: 1 };
      l.targetAnchor = { x: .5, y: 0 };
      l.points = [{ x: six.x + six.width / 2, y: child.y - 24 }, { x: child.x + child.width / 2, y: child.y - 24 }];
    }
    junctions.push({ canvasId: familyCanvas.id, nodeId: inserted.nodeId, originalSourceId: six.id, originalBranchIds: branches.map(l => l.id), addedTrunkId: inserted.sourceLinkId });
  }
  candidate = commitSnapshots(candidate);
  const issues = validateProject(candidate);
  if (issues.length) throw new Error(JSON.stringify(issues));
  // Existing content and identity invariants are stronger than schema validation.
  for (const old of base.nodes) {
    const n = candidate.nodes.find(n => n.id === old.id);
    for (const k of ['title', 'text', 'assetId', 'targetId', 'value', 'closed', 'operation']) if (JSON.stringify(n?.[k]) !== JSON.stringify(old[k])) throw new Error(`Content changed: ${old.id} ${k}`);
  }
  for (const l of base.links) { const now = candidate.links.find(n => n.id === l.id); for (const k of ['sourceId', 'targetId', 'label', 'relation', 'kind']) if (JSON.stringify(now?.[k]) !== JSON.stringify(l[k]) && !(k === 'sourceId' && junctions.some(j => j.originalBranchIds.includes(l.id)))) throw new Error(`Link semantics changed: ${l.id} ${k}`); }
  if (JSON.stringify(candidate.assets) !== JSON.stringify(base.assets)) throw new Error('Assets changed');
  const report = inspectProject(candidate);
  const expandedOriginal = navigationIndex(base), expandedCandidate = navigationIndex(candidate);
  const untouchedCanvases = candidate.canvases.filter(c => !changes.some(x => x.canvasId === c.id)).map(c => ({ canvasId: c.id, title: c.title, reason: candidate.nodes.some(n => n.canvasId === c.id && n.kind === 'joint') ? 'Existing contained Boolean examples retained; new typed numeric demonstration is in First connections.' : 'Existing comparison, source gallery or narrative composition retained; it remains valid within the reworked collection.' }));
  const summary = { id: entry.id, title: entry.title, expectedRevision: saved.revision, expectedRecoverySavedAt: recovery?.savedAt ?? null, candidateFile: `${folder}/candidate.json`, originalNodes: base.nodes.length, nodes: candidate.nodes.length, addedContainers: candidate.nodes.filter(n => !originalNodes.has(n.id) && n.kind === 'container').length, canvases: candidate.canvases.length, revisedCanvases: changes.length, changes, junctions, untouchedCanvases, sampleCanvasIds: changes.slice(0, 6).map(x => x.canvasId), visibleNodesBefore: base.nodes.filter(n => expandedOriginal.visibleNode(n.id)?.id === n.id).length, visibleNodesAfter: candidate.nodes.filter(n => expandedCandidate.visibleNode(n.id)?.id === n.id).length, invariantChecks: { existingNodeIds: true, existingContent: true, existingLinkIdsAndSemantics: true, allAssetsByteExact: true, recoveryBased: !!recovery } };
  await Promise.all([
    writeFile(`${folder}/candidate.json`, JSON.stringify(candidate, null, 2)),
    writeFile(`${folder}/recipe.json`, JSON.stringify(recipe, null, 2)),
    writeFile(`${folder}/aliases.json`, JSON.stringify(result.aliases, null, 2)),
    writeFile(`${folder}/inspection.json`, JSON.stringify(report, null, 2)),
    writeFile(`${folder}/changes.json`, JSON.stringify(summary, null, 2)),
  ]);
  summaries.push(summary);
  console.log(JSON.stringify({ title: entry.title, revised: changes.length, of: base.canvases.length, nodes: `${base.nodes.length} → ${candidate.nodes.length}`, validation: 'passed', recoveryBased: !!recovery }));
}
await writeFile(`${directory}/manifest.json`, JSON.stringify(summaries, null, 2));
