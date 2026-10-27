// Recompose the existing, attributed catalog as person-owned branches on ONE canvas.
// This is an offline transform. It never fetches sources or writes live Meridian data.
import assert from 'node:assert/strict';
import genealogy from '../mythology/genealogy-expanded.mjs';
import { applyRecipe } from '../../src/core/authoring.ts';

const people = new Map(genealogy.persons.map(person => [person.key, person]));
const sources = new Map(genealogy.sources.map(source => [source.id, source]));
const origins = new Set(['chaos', 'gaia', 'tartarus', 'eros']);
const major = new Set(['gaia', 'uranus', 'cronus', 'rhea', 'zeus', 'hera', 'poseidon', 'hades', 'demeter', 'apollo', 'artemis', 'athena', 'aphrodite', 'ares', 'hermes', 'hephaestus', 'dionysus']);
const color = person => ['primordial', 'titan'].includes(person.category) ? '#E9D4AF' : ['hero', 'mortal', 'historical'].includes(person.category) ? '#E8EEE2' : '#FFFCF4';
const evidence = refs => refs.map(ref => {
  const source = sources.get(ref.sourceId);
  return `${source?.title ?? ref.sourceId} — ${ref.locator}\n${source?.url ?? ''}`;
}).join('\n\n');
const account = person => [
  person.summary,
  `Selected parents: ${person.parents.map(key => people.get(key)?.name ?? key).join(' + ') || 'No ancestry selected or named in this catalog.'}`,
  person.birthNote || '', evidence(person.sourceRefs),
  ...person.variants.map(variant => `ALTERNATIVE ACCOUNT\n${variant.note}\nParents: ${variant.parents.map(key => people.get(key)?.name ?? key).join(' + ')}\n${evidence(variant.sourceRefs)}`),
].filter(Boolean).join('\n\n');

/** The old installer independently copied IDs; resolve known catalog records within their original family context. */
export function identifyGenealogy(baseline) {
  const canvases = new Map(baseline.canvases.map(canvas => [canvas.id, canvas]));
  const canonical = {};
  for (const person of genealogy.persons) {
    const matches = baseline.nodes.filter(node => node.kind === 'text' && node.title.toLocaleLowerCase() === person.name.toLocaleLowerCase() && /^(G\d|02 ·)/.test(canvases.get(node.canvasId)?.title ?? ''));
    assert.equal(matches.length, 1, `Expected one preserved family identity for ${person.name}; found ${matches.length}`);
    canonical[person.key] = matches[0].id;
  }
  return canonical;
}

/**
 * Apply after root consolidation has moved every node to canvasId and converted old
 * canvas roots to containers. Existing text, attachments, node IDs and link IDs survive.
 * options.aliases may be supplied when revising an already staged recipe.
 */
export function reworkGenealogy(project, baseline, options) {
  const { canvasId, x = 160, y = 420 } = options;
  const oldCanvas = new Map(baseline.canvases.map(canvas => [canvas.id, canvas]));
  const oldNodes = new Map(baseline.nodes.map(node => [node.id, node]));
  const current = new Map(project.nodes.map(node => [node.id, node]));
  const canonical = identifyGenealogy(baseline);
  const keyById = new Map(Object.entries(canonical).map(([key, id]) => [id, key]));
  const indexCanvas = baseline.canvases.find(canvas => canvas.title.startsWith('10 · Families'));
  assert.ok(indexCanvas, 'The existing families chapter is required');
  const parentId = options.parentId ?? indexCanvas.rootId;
  assert.ok(current.has(parentId));
  const familyCanvases = new Set(baseline.canvases.filter(canvas => /^(G\d|10(?:\.| ·)|11 ·|02 ·)/.test(canvas.title)).map(canvas => canvas.id));
  const familyNodeIds = new Set(baseline.nodes.filter(node => familyCanvases.has(node.canvasId)).map(node => node.id));
  const recipe = { version: 1, nodes: [], links: [] };
  const updates = new Map();
  const declaredByAlias = new Map();
  const reuse = key => options.aliases?.nodes?.[key];
  const add = (key, fields, existingId) => {
    const prior = existingId ?? reuse(key);
    const declaration = { key, ...(prior ? { existingId: prior } : {}), canvas: canvasId, ...fields };
    if (prior && updates.has(prior)) Object.assign(updates.get(prior), fields);
    else { recipe.nodes.push(declaration); if (prior) updates.set(prior, declaration); }
    declaredByAlias.set(key, prior && updates.has(prior) ? updates.get(prior) : declaration);
    return prior ?? key;
  };
  const spatial = (title, parent, tx = x, ty = y) => ({ kind: 'container', title, parent, x: tx, y: ty, width: 340, height: 180, childLayout: 'spatial', placement: 'near', collapsed: true, style: { fill: '#EEE6D5', border: '#B49870', textColor: '#243744', accent: '#9B673B', titleSize: 23, fontSize: 16, radius: 12 } });

  // The main branch is a beginning, not a directory of old pages.
  add('genealogy-root', { title: 'Genealogy · from first powers to heroes', x, y, width: 470, height: 250, childLayout: 'spatial', collapsed: true, placement: 'near', text: 'Expand a figure to reveal its children. Solid arrows show selected parentage. Purple dashed arrows mark separate alternative accounts. A reference is the same figure in another family. Every generation stays on this canvas.', style: { fill: '#D9C298', border: '#A17B46', textColor: '#243744', accent: '#8B5F30', titleSize: 29, fontSize: 18, radius: 14 } }, parentId);
  const provenance = add('genealogy-provenance', { ...spatial('Sources, earlier appearances & scope', parentId, x - 395, y + 265), height: 210, text: 'Complete source packets and earlier appearances. Explore the person tiles for the active genealogy.' });
  const otherOrigins = add('genealogy-other-origins', { ...spatial('Parents whose ancestry is not selected', parentId, x + 850, y + 265), height: 210, text: 'No ancestry is selected for these figures here. This is a limit of the catalog, not a claim that they had no parents.' });
  const legacyConnections = add('genealogy-earlier-connections', { ...spatial('Earlier appearance connections', provenance, x - 790, y + 530), text: 'Preserved duplicate connections from the previous page-based maps. Active person-owned branches above carry the selected and alternative parentage.' });
  add('genealogy-scope', { kind: 'text', title: 'What this genealogy claims', parent: provenance, x: x - 395, y: y + 530, width: 620, height: 400, childLayout: 'spatial', placement: 'near', text: genealogy.scopeNotes.join('\n\n'), style: { fill: '#FFF9ED', border: '#B49870', textColor: '#243744', titleSize: 25, fontSize: 17 } });

  // One containment path per identity. Earth generates Sky; the shared Earth/Sky
  // children continue through Sky so the actual generations can be opened in order.
  // Additional selected parentage is represented below, without inventing extra ancestors.
  const primary = new Map(genealogy.persons.map(person => [person.key, person.parents.includes('uranus') ? 'uranus' : person.parents[0]]));
  const depth = key => { let count = 0, cursor = key; const seen = new Set(); while (primary.get(cursor)) { assert.ok(!seen.has(cursor), `Containment cycle for ${key}`); seen.add(cursor); count++; cursor = primary.get(cursor); } return count; };
  const childKeys = new Map();
  for (const person of genealogy.persons) { const parent = primary.get(person.key) ?? (origins.has(person.key) ? '$origins' : '$unselected'); const keys = childKeys.get(parent) ?? []; keys.push(person.key); childKeys.set(parent, keys); }
  const position = new Map();
  const positions = (parentKey, px, py) => {
    const children = childKeys.get(parentKey) ?? [];
    children.sort((a, b) => Number(major.has(b)) - Number(major.has(a)) || genealogy.persons.findIndex(p => p.key === a) - genealogy.persons.findIndex(p => p.key === b));
    children.forEach((key, i) => {
      const columns = children.length <= 6 ? 3 : 5;
      const point = { x: px + (i % columns) * 390 - (Math.min(columns, children.length) - 1) * 195, y: py + Math.floor(i / columns) * 260 };
      position.set(key, point); positions(key, point.x + 70, point.y + 290);
    });
  };
  positions('$origins', x + 30, y + 320);
  positions('$unselected', x + 1100, y + 620);
  const details = new Map(), appearances = new Map();
  for (const person of genealogy.persons) {
    const id = canonical[person.key], pt = position.get(person.key), old = oldNodes.get(id);
    const parent = primary.has(person.key) && primary.get(person.key) ? canonical[primary.get(person.key)] : origins.has(person.key) ? parentId : otherOrigins;
    add(`person-${person.key}`, { parent, ...pt, width: major.has(person.key) ? 360 : 320, height: major.has(person.key) ? 215 : 190, childLayout: 'spatial', placement: 'near', collapsed: true, style: { fill: color(person), border: '#BBA887', textColor: '#243744', accent: '#A17B46', titleSize: major.has(person.key) ? 28 : 24, fontSize: 16, radius: 12, fontFamily: 'sans', variant: 'card' } }, id);
    // Keep the exact old tile text. Full untruncated research belongs to a child and inspector.
    assert.equal(updates.get(id).text, undefined);
    const detail = add(`person-${person.key}-account`, { kind: 'text', title: `${person.name} · account & sources`, parent: id, x: pt.x - 375, y: pt.y + 265, width: 420, height: 280, childLayout: 'spatial', placement: 'near', collapsed: true, text: account(person), style: { fill: '#F7F1E5', border: '#C8BDA6', textColor: '#243744', accent: '#9B673B', titleSize: 21, fontSize: 16, radius: 10 } });
    details.set(person.key, detail);
    assert.ok(old.text !== undefined, `Original tile text missing for ${person.name}`);
  }

  // Reuse previous saved reference appearances before allocating additional ones.
  const availableRefs = new Map();
  for (const node of baseline.nodes) if (node.kind === 'reference' && familyNodeIds.has(node.id) && keyById.has(node.targetId)) {
    const key = keyById.get(node.targetId), refs = availableRefs.get(key) ?? []; refs.push(node.id); availableRefs.set(key, refs);
  }
  const consumedRefs = new Set();
  const ref = (key, childKey, parent, fields = {}) => {
    const existing = (availableRefs.get(childKey) ?? []).find(id => !consumedRefs.has(id));
    if (existing) consumedRefs.add(existing);
    const pt = position.get(childKey), person = people.get(childKey);
    return add(key, { kind: 'reference', title: person.name, target: canonical[childKey], parent, x: pt.x, y: pt.y, width: 320, height: 190, childLayout: 'spatial', placement: 'near', collapsed: true, style: { fill: color(person), border: '#BBA887', textColor: '#243744', accent: '#A17B46', titleSize: 24, fontSize: 16, radius: 12 }, ...fields }, existing);
  };
  const originalIdentity = id => keyById.has(id) ? keyById.get(id) : keyById.get(oldNodes.get(id)?.targetId);
  const availableLinks = new Map();
  for (const link of baseline.links) if (familyCanvases.has(link.canvasId)) {
    const parent = originalIdentity(link.sourceId), child = originalIdentity(link.targetId);
    if (parent && child) { const key = `${parent}:${child}:${link.style?.dashed ? 'variant' : 'main'}`, links = availableLinks.get(key) ?? []; links.push(link.id); availableLinks.set(key, links); }
  }
  const consumedLinks = new Set(), relationshipAliases = {};
  const relationship = (key, parentKey, childKey, target, owner, variant = false, label = '') => {
    const existing = (availableLinks.get(`${parentKey}:${childKey}:${variant ? 'variant' : 'main'}`) ?? []).find(id => !consumedLinks.has(id));
    if (existing) consumedLinks.add(existing);
    recipe.links.push({ key, ...(existing ? { existingId: existing } : {}), canvas: canvasId, parent: owner, source: canonical[parentKey], target, kind: 'visual', relation: 'causal', direction: 'forward', points: [], label, style: { color: variant ? '#927398' : '#9B673B', width: 1.6, dashed: variant } });
    relationshipAliases[key] = { parentKey, childKey, owner, variant };
  };
  const alternateGroups = new Map();
  let selectedAppearances = 0, alternateAppearances = 0;
  for (const person of genealogy.persons) {
    for (const parentKey of person.parents) {
      const owner = canonical[parentKey];
      const target = primary.get(person.key) === parentKey ? canonical[person.key] : ref(`selected-${parentKey}-${person.key}`, person.key, owner);
      if (target !== canonical[person.key]) {
        selectedAppearances++;
        add(`selected-${parentKey}-${person.key}-note`, { kind: 'text', title: 'Selected account · same figure', parent: target, x: position.get(parentKey).x + 350, y: position.get(parentKey).y + 250, width: 390, height: 240, childLayout: 'spatial', placement: 'near', text: `Selected parentage: ${person.parents.map(key => people.get(key).name).join(' + ')}.\n\nThis is a reference to the same ${person.name} identity. Open the original context to explore that figure’s descendant branch.\n\n${evidence(person.sourceRefs)}`, style: { fill: '#F7F1E5', textColor: '#243744', titleSize: 19, fontSize: 16 } });
      }
      relationship(`selected-edge-${parentKey}-${person.key}`, parentKey, person.key, target, owner);
    }
    person.variants.forEach((variant, variantIndex) => {
      for (const parentKey of variant.parents) {
        if (!alternateGroups.has(parentKey)) { const pt = position.get(parentKey); alternateGroups.set(parentKey, add(`alternatives-${parentKey}`, { ...spatial(`${people.get(parentKey).name} · alternative offspring accounts`, canonical[parentKey], pt.x + 400, pt.y + 250), style: { fill: '#EBE1ED', border: '#927398', accent: '#927398', textColor: '#352B3A', titleSize: 22, fontSize: 16 }, text: 'Competing attributed parentages. These are not additional simultaneous parents. Expand an appearance for its witness and qualification.' })); }
        const owner = alternateGroups.get(parentKey);
        const key = `alternative-${parentKey}-${person.key}-${variantIndex}`;
        const target = ref(key, person.key, owner, { style: { fill: '#F1E8F2', border: '#927398', textColor: '#352B3A', accent: '#927398', titleSize: 23, fontSize: 16, radius: 12 } });
        add(`${key}-witness`, { kind: 'text', title: `${person.name} · alternative ${variantIndex + 1}`, parent: target, x: position.get(parentKey).x + 390, y: position.get(parentKey).y + 500, width: 440, height: 270, childLayout: 'spatial', placement: 'near', text: `${variant.note}\n\nAttributed parents: ${variant.parents.map(key => people.get(key)?.name ?? key).join(' + ')}\n\n${evidence(variant.sourceRefs)}\n\nThis appearance retains the original identity; its selected descent is explored from the original context.`, style: { fill: '#F1E8F2', border: '#927398', textColor: '#352B3A', titleSize: 21, fontSize: 16 } });
        relationship(`${key}-edge`, parentKey, person.key, target, owner, true, 'Alternative account');
        alternateAppearances++;
      }
    });
  }

  // Prior reference copies remain addressable without acting as page navigation.
  for (const [key, ids] of availableRefs) for (const id of ids) if (!consumedRefs.has(id)) {
    if (!appearances.has(key)) { const pt = position.get(key); appearances.set(key, add(`appearances-${key}`, { ...spatial(`${people.get(key).name} · earlier appearances`, details.get(key), pt.x - 760, pt.y + 520), text: 'Preserved reference appearances from the earlier maps. They refer to the same identity.' })); }
    add(`preserved-appearance-${id}`, { parent: appearances.get(key), canvas: canvasId, childLayout: 'spatial', placement: 'near', collapsed: true, width: 320, height: 190 }, id);
  }
  // Old G pages become closed source packets beneath provenance. Their texts,
  // media, old root identities and navigation references are preserved in place.
  for (const canvas of baseline.canvases) if (familyCanvases.has(canvas.id) && canvas.rootId !== parentId) {
    add(`source-packet-${canvas.rootId}`, { parent: provenance, x: x - 780, y: y + 820, width: 360, height: 185, childLayout: 'spatial', placement: 'near', collapsed: true }, canvas.rootId);
  }
  // The old family index already lives on the major branch, so its directory
  // cards and labels are also moved into provenance rather than crowding origins.
  for (const node of baseline.nodes) if (node.canvasId === indexCanvas.id && node.id !== parentId && !updates.has(node.id)) {
    add(`old-index-${node.id}`, { parent: provenance, childLayout: 'spatial', placement: 'near', collapsed: true }, node.id);
  }
  for (const link of baseline.links) if (familyCanvases.has(link.canvasId) && !consumedLinks.has(link.id)) recipe.links.push({ key: `preserved-edge-${link.id}`, existingId: link.id, canvas: canvasId, parent: legacyConnections });

  // Fixed page-row ports become misleading once children pack around the parent.
  // Clear those layout-only overrides so the shared renderer aims at the nearest
  // tile edge after each reveal; the original data remains in the baseline file.
  const layoutBase = structuredClone(project);
  for (const link of layoutBase.links) if (consumedLinks.has(link.id)) { delete link.sourceAnchor; delete link.targetAnchor; }
  const authored = applyRecipe(layoutBase, recipe);
  const result = authored.project;
  for (const old of baseline.nodes) if (keyById.has(old.id)) assert.equal(result.nodes.find(node => node.id === old.id).text, old.text, `Preserve exact tile text: ${old.title}`);
  const nodeIds = new Set(result.nodes.map(node => node.id)), linkIds = new Set(result.links.map(link => link.id));
  assert.ok(baseline.nodes.every(node => nodeIds.has(node.id)), 'No prior node identity may be lost');
  assert.ok(baseline.links.every(link => linkIds.has(link.id)), 'No prior link identity may be lost');
  const report = {
    persons: genealogy.persons.length, namedIndividuals: genealogy.stats.namedIndividuals,
    selectedParentageLinks: genealogy.parentageEdges.filter(edge => edge.tradition === 'main').length,
    alternativeParentageLinks: alternateAppearances, selectedReferenceAppearances: selectedAppearances,
    reusedReferenceIds: consumedRefs.size, reusedRelationshipIds: consumedLinks.size, clearedPageAnchorLinkIds: [...consumedLinks],
    originalReferenceAppearancesPreserved: [...availableRefs.values()].flat().length,
    maximumContainmentGeneration: Math.max(...genealogy.persons.map(person => depth(person.key))),
    preservedPersonIds: canonical, canonicalSourceCanvasCount: new Set(Object.values(canonical).map(id => oldNodes.get(id).canvasId)).size,
    sourcePackets: familyCanvases.size - 1,
    scope: 'All 529 existing attributed records share one canvas. The selected descent is a containment tree; other parents and variant accounts use reference appearances. An alternate appearance reveals its witness and can open the original context; it does not recursively clone the full descendant tree.',
  };
  return { project: result, aliases: { ...authored.aliases, people: canonical, relationships: relationshipAliases }, report, recipe };
}
