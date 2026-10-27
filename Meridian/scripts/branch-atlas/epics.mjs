/** Compose the two epics as expandable branches of one canvas. No host or filesystem writes. */
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { applyRecipe } from '../../src/core/authoring.ts';

const definitions = [
  { key: 'iliad', prefix: 'I', overview: /^30 ·/, maps: /^31\./, title: 'The Iliad', color: '#A55135', fill: '#FFF7EC', ink: '#49352B' },
  { key: 'odyssey', prefix: 'O', overview: /^40 ·/, maps: /^41\./, title: 'The Odyssey', color: '#D5B977', fill: '#203D4D', ink: '#E9F0EE' },
];
const words = text => text.match(/\S+/g)?.length ?? 0;
const hash = text => createHash('sha256').update(text).digest('hex');
const bytes = text => Buffer.byteLength(text, 'utf8');
const pad = value => String(value).padStart(2, '0');

/** Consecutive whole-paragraph slices, retaining every source character and original line ending. */
export function passageSlices(source, start = 0, end = source.length, { minimum = 6, maximum = 10 } = {}) {
  const text = source.slice(start, end), boundaries = [0];
  for (const match of text.matchAll(/\r?\n[\t ]*\r?\n(?:[\t ]*\r?\n)*/g)) {
    const next = match.index + match[0].length;
    if (next < text.length && text.slice(boundaries.at(-1), next).trim()) boundaries.push(next);
  }
  boundaries.push(text.length);
  const paragraphs = boundaries.slice(0, -1).map((offset, index) => ({ start: offset, end: boundaries[index + 1], words: words(text.slice(offset, boundaries[index + 1])) }));
  const count = Math.min(paragraphs.length, Math.max(minimum, Math.min(maximum, Math.ceil(words(text) / 850))));
  const result = [];
  let cursor = 0, remainingWords = words(text);
  for (let group = 0; group < count; group++) {
    const first = cursor, groupsLeft = count - group, goal = remainingWords / groupsLeft;
    let accumulated = 0;
    while (cursor < paragraphs.length && (cursor === first || group === count - 1 || accumulated < goal)) {
      if (cursor > first && paragraphs.length - cursor <= groupsLeft - 1) break;
      accumulated += paragraphs[cursor++].words;
    }
    const from = paragraphs[first].start, to = paragraphs[cursor - 1].end;
    result.push({ start: start + from, end: start + to, paragraphStart: first + 1, paragraphEnd: cursor, words: accumulated, bytes: bytes(text.slice(from, to)), text: text.slice(from, to) });
    remainingWords -= accumulated;
  }
  assert.equal(result.map(part => part.text).join(''), text, 'Passage segmentation lost original text');
  return result;
}

function incipit(text) {
  const paragraphs = text.split(/\r?\n[\t ]*\r?\n/).map(part => part.replace(/\s+/g, ' ').trim()).filter(Boolean);
  const content = paragraphs.find(part => !/^BOOK\s+[IVXLCDM]+\.?$/i.test(part)) ?? paragraphs[0] ?? '';
  const excerpt = content.split(' ').slice(0, 9).join(' ');
  return excerpt.length < content.length ? excerpt.replace(/[—,;:]$/, '') + '…' : excerpt;
}

/** Keep wording/punctuation and paragraph boundaries; remove the edition's hard line wraps for tiles. */
export function reflowPassage(text) {
  return text.split(/\r?\n[\t ]*\r?\n(?:[\t ]*\r?\n)*/).map(paragraph => paragraph.replace(/\s+/g, ' ').trim()).filter(Boolean).join('\n\n');
}

function retainAliases(result, previous = {}) {
  const identities = new Map();
  for (const scope of ['canvases', 'roots', 'nodes', 'assets', 'links']) for (const [key, id] of Object.entries(result.aliases[scope])) {
    const stable = previous[scope]?.[key];
    if (stable && stable !== id) identities.set(id, stable);
  }
  const mapped = id => identities.get(id) ?? id;
  for (const node of result.project.nodes) {
    node.id = mapped(node.id); node.canvasId = mapped(node.canvasId);
    if (node.parentId) node.parentId = mapped(node.parentId);
    if (node.targetId) node.targetId = mapped(node.targetId);
    if (node.assetId) node.assetId = mapped(node.assetId);
    if (node.dependencyIds) node.dependencyIds = node.dependencyIds.map(mapped);
  }
  for (const link of result.project.links) {
    link.id = mapped(link.id); link.canvasId = mapped(link.canvasId); link.sourceId = mapped(link.sourceId); link.targetId = mapped(link.targetId);
    if (link.parentId) link.parentId = mapped(link.parentId);
  }
  for (const scope of ['canvases', 'roots', 'nodes', 'assets', 'links']) for (const key of Object.keys(result.aliases[scope])) result.aliases[scope][key] = mapped(result.aliases[scope][key]);
}

/**
 * Input must already be consolidated: old roots are ordinary nodes in canvasId.
 * epicPositions optionally maps iliad/odyssey to a branch heading's absolute x/y.
 * Existing object IDs, source text, attachments and links are retained.
 */
export function reworkEpics(project, baseline, { canvasId = project.entryCanvasId, epicPositions = {}, aliases: previousAliases = {} } = {}) {
  assert.equal(project.canvases.length, 1, 'Flatten the atlas into one canvas before reworking epics');
  const current = new Map(project.nodes.map(node => [node.id, node]));
  const old = new Map(baseline.nodes.map(node => [node.id, node]));
  const assets = new Map(baseline.assets.map(asset => [asset.id, asset]));
  const updates = new Map(), additions = [], linkUpdates = new Map(), newLinks = [];
  const report = { books: [], epics: [], sourceText: 'Samuel Butler translations; existing attachments retained byte for byte. Passage offsets are UTF-16 character offsets in their decoded source attachment. Tiles remove intra-paragraph line wraps and indentation; wording and paragraph boundaries remain unchanged. Raw source hashes and reflowed tile hashes are both recorded.', notes: [] };
  const alias = (scope, id) => `${scope}.${id}`;
  const update = (id, value, key = alias('existing', id)) => {
    assert.ok(current.has(id), `Missing preserved node ${id}`);
    assert.notEqual(current.get(id).kind, 'canvas', 'Former canvas roots must first become ordinary nodes');
    updates.set(id, { ...(updates.get(id) ?? { key, existingId: id }), ...value });
  };
  const updateLink = (id, value) => linkUpdates.set(id, { ...(linkUpdates.get(id) ?? { key: alias('existing-link', id), existingId: id }), ...value });
  const node = id => ({ ...current.get(id), ...updates.get(id) });

  for (const definition of definitions) {
    const epic = baseline.canvases.find(canvas => definition.overview.test(canvas.title));
    assert.ok(epic, `Missing ${definition.title} overview`);
    const overviewNodes = baseline.nodes.filter(node => node.canvasId === epic.id);
    const context = overviewNodes.find(node => node.title === 'Follow this reading sequence');
    assert.ok(context, `Missing ${definition.title} reading map`);
    const position = epicPositions[definition.key] ?? current.get(epic.rootId);
    const x = position.x, y = position.y;
    const tone = { fill: definition.fill, textColor: definition.ink, border: definition.color, accent: definition.color, fontFamily: 'serif', radius: 10 };
    const bookMapKey = `${definition.key}.book-map`;
    update(epic.rootId, { title: definition.title, ...(old.get(epic.rootId)?.text === undefined ? { text: '24 books · complete Samuel Butler translation\nExpand a book to reveal its text passages here.' } : {}), x, y, width: 460, height: 190, childLayout: 'spatial', placement: 'free', collapsed: true, style: { ...tone, titleSize: 34, fontSize: 18 } }, `${definition.key}.root`);
    update(context.id, { parent: epic.rootId, title: 'Context & reading aids', x: x + 530, y, width: 380, height: 170, childLayout: 'spatial', placement: 'near', collapsed: true, style: { ...tone, titleSize: 24, fontSize: 17 } }, `${definition.key}.context`);
    additions.push({ key: bookMapKey, canvas: canvasId, parent: epic.rootId, kind: 'container', title: 'Books 1–24 · reading order', text: 'Follow the arrows through the poem.', x, y: y + 260, width: 660, height: 95, childLayout: 'spatial', placement: 'near', collapsed: false, style: { ...tone, variant: 'label', titleSize: 26, fontSize: 18 } });
    let contextIndex = 0;
    for (const original of overviewNodes) {
      if (original.id === epic.rootId || original.id === context.id) continue;
      update(original.id, { parent: context.id, x: x + 520 + (contextIndex % 3) * 440, y: y - 240 - Math.floor(contextIndex / 3) * 280, width: 400, height: original.kind === 'image' ? 330 : 240, childLayout: 'spatial', placement: 'near', collapsed: true, style: { titleSize: original.title.length > 100 ? 19 : 24, fontSize: 17, variant: 'card' } });
      contextIndex++;
    }
    const maps = baseline.canvases.filter(canvas => definition.maps.test(canvas.title));
    for (const [index, map] of maps.entries()) {
      update(map.rootId, { parent: context.id, x: x + 540 + index * 420, y: y - 600, width: 380, height: 170, childLayout: 'spatial', placement: 'near', collapsed: true, style: { ...tone, titleSize: 22, fontSize: 17 } });
      const members = baseline.nodes.filter(node => node.canvasId === map.id && node.id !== map.rootId);
      for (const [memberIndex, original] of members.entries()) {
        update(original.id, { parent: map.rootId, x: x + 540 + (memberIndex % 3) * 400, y: y - 840 - Math.floor(memberIndex / 3) * 235, width: 360, height: 195, childLayout: 'spatial', placement: 'near', collapsed: true, style: { titleSize: original.title.length > 100 ? 18 : 22, fontSize: 16, variant: 'card' } });
      }
      for (const link of baseline.links.filter(link => link.canvasId === map.id)) updateLink(link.id, { parent: map.rootId, relation: 'chronological', points: [] });
    }
    for (const link of baseline.links.filter(link => link.canvasId === epic.id)) updateLink(link.id, { parent: context.id, relation: 'chronological', points: [] });

    const books = baseline.canvases.filter(canvas => new RegExp(`^${definition.prefix}\\d{2} ·`).test(canvas.title)).sort((left, right) => Number(left.title.slice(1, 3)) - Number(right.title.slice(1, 3)));
    assert.equal(books.length, 24, `${definition.title} must retain all 24 books`);
    for (const [bookIndex, book] of books.entries()) {
      const number = bookIndex + 1, bookKey = `${definition.key}.book-${pad(number)}`;
      const row = Math.floor(bookIndex / 6), column = row % 2 ? 5 - bookIndex % 6 : bookIndex % 6;
      const bx = x + column * 350, by = y + 390 + row * 235;
      const members = baseline.nodes.filter(member => member.canvasId === book.id && member.id !== book.rootId);
      const aids = members.find(member => member.title === 'Read the account');
      const document = members.find(member => member.kind === 'document' && member.title === 'Read the entire book');
      const events = members.find(member => member.title === 'Follow the events');
      const interpretation = members.find(member => member.title === 'People & interpretation');
      assert.ok(aids && document && events && interpretation, `Incomplete preserved reading structure in ${book.title}`);
      const asset = assets.get(document.assetId);
      assert.ok(asset?.mime === 'text/plain', `Missing complete source text for ${book.title}`);
      const source = Buffer.from(asset.base64, 'base64').toString('utf8');
      const heading = /^BOOK\s+[IVXLCDM]+\.?\s*$/im.exec(source);
      assert.ok(heading, `Missing original book heading in ${asset.name}`);
      const notesHeading = /^FOOTNOTES:\s*$/im.exec(source);
      const bodyEnd = notesHeading?.index ?? source.length;
      const passages = passageSlices(source, heading.index, bodyEnd);
      const readingKey = `${bookKey}.reading`;
      update(book.rootId, { parent: bookMapKey, title: `Book ${number} · ${book.title.split(' · ').slice(1).join(' · ')}`, ...(old.get(book.rootId)?.text === undefined ? { text: `${passages.length} text passages · Samuel Butler` } : {}), x: bx, y: by, width: 304, height: 180, childLayout: 'spatial', placement: 'free', collapsed: true, style: { ...tone, titleSize: 23, fontSize: 16 } }, bookKey);
      update(aids.id, { parent: book.rootId, title: 'Reading aids & original edition', x: bx - 450, y: by + 230, width: 380, height: 190, childLayout: 'spatial', placement: 'near', collapsed: true, style: { ...tone, titleSize: 23, fontSize: 17 } }, `${bookKey}.aids`);
      additions.push({ key: readingKey, canvas: canvasId, parent: book.rootId, kind: 'container', title: `${definition.title} · Book ${number} · text`, text: 'Samuel Butler translation · original paragraph order', x: bx + 380, y: by, width: 1080, height: 95, childLayout: 'spatial', placement: 'near', collapsed: false, style: { ...tone, variant: 'label', titleSize: 27, fontSize: 18 } });
      let order = 0;
      for (const original of members) {
        if (original.id === aids.id) continue;
        // Original internal event/theme relationships survive; top-level presentation is folded into aids.
        const parent = original.parentId === book.rootId || original.id === document.id ? aids.id : original.parentId;
        const isBranch = members.some(candidate => candidate.parentId === original.id);
        update(original.id, { parent, x: bx - 460 + (order % 3) * 440, y: by + 480 + Math.floor(order / 3) * 260, width: original.title === 'The account' ? 530 : 390, height: original.title === 'The account' ? 430 : 215, childLayout: 'spatial', placement: 'near', collapsed: isBranch, style: { variant: 'card', titleSize: original.title.length > 90 ? 18 : 22, fontSize: 17 } });
        order++;
      }
      const passageKeys = [];
      for (const [index, part] of passages.entries()) {
        const key = `${bookKey}.passage-${pad(index + 1)}`;
        passageKeys.push(key);
        const passageRow = Math.floor(index / 2), passageColumn = passageRow % 2 ? 1 - index % 2 : index % 2;
        additions.push({ key, canvas: canvasId, parent: readingKey, kind: 'text', title: `${pad(index + 1)} · ${incipit(part.text)}`, text: reflowPassage(part.text),
          x: bx + 380 + passageColumn * 570, y: by + 135 + passageRow * 490, width: 520, height: 440, childLayout: 'spatial', placement: 'free',
          style: { ...tone, titleSize: 22, fontSize: 19 } });
        if (index) {
          const sameRow = index % 2 === 1, right = passageRow % 2 === 0;
          newLinks.push({ key: `${bookKey}.passage-sequence-${pad(index)}`, canvas: canvasId, parent: book.rootId, source: passageKeys[index - 1], target: key, kind: 'visual', relation: 'chronological', direction: 'forward', sourceAnchor: sameRow ? { x: right ? 1 : 0, y: .5 } : { x: .5, y: 1 }, targetAnchor: sameRow ? { x: right ? 0 : 1, y: .5 } : { x: .5, y: 0 }, style: { color: definition.color, width: 1.5 } });
        }
      }
      if (notesHeading) {
        const notesKey = `${bookKey}.edition-notes`, notes = passageSlices(source, notesHeading.index, source.length, { minimum: 6, maximum: 10 });
        additions.push({ key: notesKey, canvas: canvasId, parent: aids.id, kind: 'container', title: 'Butler’s edition notes', text: 'Historical commentary from the included edition. These notes are separate from Homer’s narrative and reflect the translator’s interpretations.', x: bx - 450, y: by + 760, width: 390, height: 215, collapsed: true, childLayout: 'spatial', placement: 'near', style: { ...tone, titleSize: 24, fontSize: 17 } });
        for (const [index, part] of notes.entries()) additions.push({ key: `${notesKey}.${pad(index + 1)}`, canvas: canvasId, parent: notesKey, kind: 'text', title: `Edition notes · ${index + 1} of ${notes.length}`, text: reflowPassage(part.text), x: bx - 450 + index % 2 * 560, y: by + 1030 + Math.floor(index / 2) * 480, width: 520, height: 430, childLayout: 'spatial', placement: 'near', style: { ...tone, titleSize: 23, fontSize: 18 } });
        report.notes.push({ epic: definition.key, book: number, sourceAssetId: asset.id, parentAlias: notesKey, start: notesHeading.index, end: source.length, bytes: bytes(source.slice(notesHeading.index)), sourceHash: hash(source.slice(notesHeading.index)), sections: notes.map(({ text, ...part }, index) => ({ ...part, alias: `${notesKey}.${pad(index + 1)}`, hash: hash(text), renderedHash: hash(reflowPassage(text)) })) });
      }
      for (const link of baseline.links.filter(link => link.canvasId === book.id)) {
        const eventIds = new Set(members.filter(member => member.parentId === events.id).map(member => member.id));
        updateLink(link.id, { parent: eventIds.has(link.sourceId) && eventIds.has(link.targetId) ? events.id : aids.id, ...(eventIds.has(link.sourceId) && eventIds.has(link.targetId) ? { relation: 'chronological' } : {}), points: [] });
      }
      report.books.push({ epic: definition.key, number, id: book.rootId, alias: bookKey, readingAlias: readingKey, title: book.title, oldCanvasId: book.id, sourceDocumentId: document.id, sourceAssetId: asset.id, sourceName: asset.name, sourceHash: hash(source), sourceBytes: bytes(source), attributionStart: 0, attributionEnd: heading.index, attributionBytes: bytes(source.slice(0, heading.index)), narrativeStart: heading.index, narrativeEnd: bodyEnd, narrativeBytes: bytes(source.slice(heading.index, bodyEnd)), narrativeHash: hash(source.slice(heading.index, bodyEnd)), passageCount: passages.length, narrativeWords: words(source.slice(heading.index, bodyEnd)), sections: passages.map(({ text, ...part }, index) => ({ ...part, alias: passageKeys[index], hash: hash(text), renderedHash: hash(reflowPassage(text)) })) });
      if (bookIndex) {
        const previous = node(books[bookIndex - 1].rootId), self = node(book.rootId), sameRow = Math.floor((bookIndex - 1) / 6) === row;
        const right = sameRow && self.x > previous.x;
        newLinks.push({ key: `${definition.key}.book-sequence-${pad(bookIndex)}`, canvas: canvasId, parent: epic.rootId, source: books[bookIndex - 1].rootId, target: book.rootId, kind: 'visual', relation: 'chronological', direction: 'forward', sourceAnchor: sameRow ? { x: right ? 1 : 0, y: 0.5 } : { x: 0.5, y: 1 }, targetAnchor: sameRow ? { x: right ? 0 : 1, y: 0.5 } : { x: 0.5, y: 0 }, style: { color: definition.color, width: 2 } });
      }
    }
    report.epics.push({ key: definition.key, id: epic.rootId, contextId: context.id, bookMapAlias: bookMapKey, bookIds: books.map(book => book.rootId), oldCanvasIds: [epic.id, ...maps.map(map => map.id), ...books.map(book => book.id)], bounds: { x, y, width: 2054, height: 1275 } });
  }
  const recipe = { version: 1, nodes: [...updates.values(), ...additions], links: [...linkUpdates.values(), ...newLinks] };
  const result = applyRecipe(project, recipe);
  retainAliases(result, previousAliases);
  const finalNodes = new Map(result.project.nodes.map(item => [item.id, item]));
  for (const [id, original] of old) if (original.text !== undefined) assert.equal(finalNodes.get(id)?.text, original.text, `Source text changed in ${original.title}`);
  for (const original of baseline.assets) assert.deepEqual(result.project.assets.find(asset => asset.id === original.id), original, `Asset changed: ${original.name}`);
  for (const book of report.books) {
    book.readingId = result.aliases.nodes[book.readingAlias];
    for (const part of book.sections) part.id = result.aliases.nodes[part.alias];
    const source = Buffer.from(assets.get(book.sourceAssetId).base64, 'base64').toString('utf8');
    assert.equal(hash(book.sections.map(part => source.slice(part.start, part.end)).join('')), book.narrativeHash, `Incomplete source coverage in ${book.title}`);
    for (const part of book.sections) assert.equal(finalNodes.get(part.id).text, reflowPassage(source.slice(part.start, part.end)), `Changed wording in ${book.title}`);
  }
  for (const epic of report.epics) epic.bookMapId = result.aliases.nodes[epic.bookMapAlias];
  for (const note of report.notes) {
    for (const part of note.sections) part.id = result.aliases.nodes[part.alias];
    const source = Buffer.from(assets.get(note.sourceAssetId).base64, 'base64').toString('utf8');
    assert.equal(hash(note.sections.map(part => source.slice(part.start, part.end)).join('')), note.sourceHash, 'Incomplete preserved edition notes');
    for (const part of note.sections) assert.equal(finalNodes.get(part.id).text, reflowPassage(source.slice(part.start, part.end)), 'Changed wording in edition notes');
  }
  for (const book of report.books) assert.equal(book.attributionBytes + book.sections.reduce((sum, part) => sum + part.bytes, 0) + report.notes.filter(note => note.sourceAssetId === book.sourceAssetId).reduce((sum, note) => sum + note.bytes, 0), book.sourceBytes, `Uncovered source bytes in ${book.title}`);
  return { project: result.project, aliases: result.aliases, report, recipe };
}
