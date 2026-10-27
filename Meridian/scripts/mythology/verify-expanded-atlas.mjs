/**
 * Read-only content/publication audit. No host requests, project writes, or saves.
 * Run: node --experimental-strip-types scripts/mythology/verify-expanded-atlas.mjs
 * Optional: --dir examples/visual-authoring --report .cache/expanded-atlas/verification.json
 */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { isDeepStrictEqual } from 'node:util';
import epics from './epics-expanded.mjs';
import * as genealogy from './genealogy-expanded.mjs';
import { validateProject } from '../../src/core/rules.ts';
import { evaluate } from '../../src/core/engine.ts';
import { navigationIndex } from '../../src/core/navigation.ts';
import { createPublicationPayload, readPublication } from '../../src/publication.ts';

const root = fileURLToPath(new URL('../../', import.meta.url));
const args = process.argv.slice(2);
const option = (flag, fallback) => {
  const index = args.indexOf(flag);
  if (index < 0) return fallback;
  if (!args[index + 1] || args[index + 1].startsWith('--')) throw new Error(`${flag} requires a path.`);
  return args[index + 1];
};
for (let index = 0; index < args.length; index += 2)
  if (!['--dir', '--report'].includes(args[index])) throw new Error(`Unknown argument ${args[index]}`);
const directory = path.resolve(root, option('--dir', 'examples/visual-authoring'));
const reportPath = path.resolve(root, option('--report', '.cache/expanded-atlas/verification.json'));
const corpus = path.join(root, 'content/greek-mythology');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const json = name => readFile(name, 'utf8').then(JSON.parse);
const report = {
  verifiedAt: new Date().toISOString(), projectPath: path.join(directory, 'visual-atlas.project.json'),
  readOnly: true, checks: [], counts: {}, assetHashes: [],
};
const protectedPaths = new Set([
  report.projectPath, path.join(directory, 'visual-atlas.aliases.json'),
  path.join(directory, 'visual-atlas.html'), path.join(directory, 'visual-atlas.manifest.json'),
  path.join(root, 'examples/greek-mythology/greek-mythology.project.json'),
  fileURLToPath(new URL('./epics-expanded.mjs', import.meta.url)),
  fileURLToPath(new URL('./genealogy-expanded.mjs', import.meta.url)),
  fileURLToPath(new URL('./roman-hesiod-expanded.mjs', import.meta.url)),
]);
if (protectedPaths.has(reportPath)) throw new Error('The report must not overwrite an input artifact.');
// Reports deliberately stay outside the generated collection and live storage.
const relativeReport = path.relative(path.join(root, '.cache'), reportPath);
const relativeOutput = path.relative(path.join(root, 'output'), reportPath);
if ([relativeReport, relativeOutput].every(value => value.startsWith('..') || path.isAbsolute(value)))
  throw new Error('Write the report beneath Meridian/.cache or Meridian/output.');
const beforeHashes = new Map();
for (const filename of protectedPaths) {
  try { beforeHashes.set(filename, hash(await readFile(filename))); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
}
async function check(name, action) {
  try { const details = await action(); report.checks.push({ name, status: 'passed', ...(details ? { details } : {}) }); }
  catch (error) { report.checks.push({ name, status: 'failed', error: String(error.message ?? error).slice(0, 2400) }); }
}
const present = (value, message) => { assert.ok(value, message); return value; };
const shortList = values => values.slice(0, 30).join(', ') + (values.length > 30 ? ` … (${values.length} total)` : '');
const wordCount = text => text.trim().split(/\s+/).length;
const stripFields = (object, fields) => Object.fromEntries(Object.entries(object).filter(([name]) => !fields.includes(name)));

let project, aliases, manifest, html;
await check('Generated project, aliases, manifest and publication are readable', async () => {
  [project, aliases, manifest, html] = await Promise.all([
    json(report.projectPath), json(path.join(directory, 'visual-atlas.aliases.json')),
    json(path.join(directory, 'visual-atlas.manifest.json')), readFile(path.join(directory, 'visual-atlas.html'), 'utf8'),
  ]);
  report.projectId = project.id; report.revision = project.revision;
  Object.assign(report.counts, { canvases: project.canvases.length, nodes: project.nodes.length, links: project.links.length, assets: project.assets.length });
});
await check('Research modules declare 48 books and complete genealogy exports', () => {
  for (const work of [epics.iliad, epics.odyssey]) {
    assert.equal(work.books.length, 24, `${work.key}: expected 24 books`);
    assert.deepEqual(work.books.map(book => book.number), Array.from({ length: 24 }, (_, i) => i + 1), `${work.key}: sequential books`);
    assert.equal(work.movements.length, 6, `${work.key}: six movements`);
    assert.deepEqual(work.movements.flatMap(movement => movement.books), work.books.map(book => book.key), `${work.key}: movement coverage`);
    for (const book of work.books) assert.ok(wordCount(book.summary) >= 150, `${book.key}: substantial synopsis`);
  }
  const bookKeys = new Set([...epics.iliad.books, ...epics.odyssey.books].map(book => book.key));
  for (const book of [...epics.iliad.books, ...epics.odyssey.books])
    for (const link of book.links) assert.ok(bookKeys.has(link.target), `${book.key}: missing linked book ${link.target}`);
  for (const item of [...epics.comparisons, ...epics.odyssey.chronology])
    for (const key of item.books) assert.ok(bookKeys.has(key), `${item.key}: missing book ${key}`);
  for (const field of ['persons', 'branches', 'romanComparisons', 'hesiodWorks', 'sources'])
    assert.ok(Array.isArray(genealogy[field]) && genealogy[field].length, `Genealogy export ${field} is not ready`);
});

if (project && aliases) {
  const nodes = new Map(project.nodes.map(node => [node.id, node]));
  const assets = new Map(project.assets.map(asset => [asset.id, asset]));
  const canvases = new Map(project.canvases.map(canvas => [canvas.id, canvas]));
  const links = new Map(project.links.map(link => [link.id, link]));
  const byAlias = key => present(nodes.get(aliases.nodes[key]), `Missing node alias: ${key}`);
  const assetAlias = key => present(assets.get(aliases.assets[key]), `Missing asset alias: ${key}`);
  const documentText = node => {
    const asset = present(assets.get(node.assetId), `Missing attachment: ${node.title}`);
    assert.match(asset.mime, /^text\/(plain|markdown)(;|$)/, `Not a text attachment: ${node.title}`);
    return Buffer.from(asset.base64, 'base64').toString('utf8');
  };
  const attachedAssets = new Set(project.nodes.map(node => node.assetId).filter(Boolean));
  const allText = project.nodes.map(node => `${node.title}\n${node.text ?? ''}`).concat(
    project.assets.filter(asset => attachedAssets.has(asset.id) && /^text\//.test(asset.mime))
      .map(asset => Buffer.from(asset.base64, 'base64').toString('utf8')),
  ).join('\n\n');
  const documentCorpus = project.nodes.filter(node => node.kind === 'document' && /^text\//.test(assets.get(node.assetId)?.mime ?? ''))
    .map(documentText).join('\n\n');
  const authoredBefore = JSON.stringify(project);

  await check('Project graph and every alias validate', () => {
    assert.deepEqual(validateProject(project), [], 'Invalid project graph');
    const maps = { nodes, canvases, roots: nodes, links, assets };
    for (const [group, mapping] of Object.entries(aliases)) {
      assert.ok(maps[group], `Unexpected alias group ${group}`);
      const missing = Object.entries(mapping).filter(([, id]) => !maps[group].has(id)).map(([key]) => `${group}.${key}`);
      assert.equal(missing.length, 0, `Stale aliases: ${shortList(missing)}`);
    }
    assert.equal(manifest.projectId, project.id, 'Manifest belongs to another project');
    for (const field of ['canvases','nodes','links','assets']) assert.equal(manifest[field], project[field].length, `Manifest ${field} count`);
  });

  for (const work of [epics.iliad, epics.odyssey]) {
    let originalText, bookSections;
    await check(`${work.title}: complete source translation matches local bytes`, async () => {
      const sourceBytes = await readFile(path.join(corpus, 'texts', `${work.key}-butler-full.txt`));
      originalText = sourceBytes.toString('utf8');
      const full = assetAlias(`full-${work.key}`);
      assert.equal(hash(Buffer.from(full.base64, 'base64')), hash(sourceBytes), `${work.title}: full translation was altered`);
      assert.equal(byAlias(`reading-${work.key}`).assetId, full.id, `${work.title}: full source is unattached`);
      const headings = [...originalText.matchAll(/^BOOK ([IVXLC]+)\.?\r?$/gm)];
      assert.equal(headings.length, 24, `${work.title}: source book boundaries`);
      bookSections = headings.map((heading, index) => {
        const end = headings[index + 1]?.index ?? originalText.indexOf('*** END OF THE PROJECT GUTENBERG', heading.index);
        return originalText.slice(heading.index, end > heading.index ? end : undefined);
      });
      report.assetHashes.push({ key: `full-${work.key}`, bytes: sourceBytes.length, sha256: hash(sourceBytes) });
    });
    for (const book of work.books) await check(`${book.key}: full account, events and complete book attachment`, () => {
      const canvasId = present(aliases.canvases[book.key], `Missing book canvas ${book.key}`);
      const account = byAlias(`${book.key}-account`);
      assert.equal(account.canvasId, canvasId, `${book.key}: account on wrong canvas`);
      assert.equal(account.text, book.summary, `${book.key}: synopsis truncated or changed`);
      assert.ok(account.title, `${book.key}: account missing a title`);
      book.events.forEach((event, index) => assert.equal(byAlias(`${book.key}-event-${index}`).text, typeof event === 'string' ? event : event.text ?? event.summary, `${book.key}: event ${index}`));
      const sourceNode = byAlias(`${book.key}-source`);
      assert.equal(sourceNode.kind, 'document', `${book.key}: source is not a document`);
      assert.equal(sourceNode.canvasId, canvasId, `${book.key}: source on wrong canvas`);
      assert.equal(sourceNode.assetId, assetAlias(`${book.key}-fulltext`).id, `${book.key}: wrong attachment`);
      const text = documentText(sourceNode);
      assert.ok(text.includes(book.sourceUrl), `${book.key}: primary source link missing`);
      const section = present(bookSections?.[book.number - 1], `${book.key}: local source section unavailable`);
      assert.ok(section.length > 8000, `${book.key}: suspiciously short primary source section`);
      assert.ok(text.endsWith(section), `${book.key}: full primary book bytes were changed or truncated`);
      assert.equal([...text.matchAll(/^BOOK [IVXLC]+\.?\r?$/gm)].length, 1, `${book.key}: attachment crosses book boundaries`);
      report.assetHashes.push({ key: `${book.key}-fulltext`, bytes: Buffer.byteLength(text), sha256: hash(Buffer.from(text)), primarySourceSha256: hash(Buffer.from(section)) });
      for (const field of ['characters','themes']) {
        const target = byAlias(`${book.key}-${field === 'characters' ? 'people' : 'themes'}`);
        for (const value of book[field]) assert.ok(target.text.includes(value), `${book.key}: missing ${field} ${value}`);
      }
      if (book.number > 1) assert.equal(byAlias(`${book.key}-previous`).targetId, project.canvases.find(canvas => canvas.id === aliases.canvases[work.books[book.number - 2].key])?.rootId, `${book.key}: previous-book target`);
      if (book.number < 24) assert.equal(byAlias(`${book.key}-next`).targetId, project.canvases.find(canvas => canvas.id === aliases.canvases[work.books[book.number].key])?.rootId, `${book.key}: next-book target`);
      book.links.forEach((related, index) => {
        const target = project.canvases.find(canvas => canvas.id === aliases.canvases[related.target]);
        const portal = byAlias(`${book.key}-related-${index}`);
        assert.equal(portal.kind, 'canvas-link', `${book.key}: related reading is not navigable`);
        assert.equal(portal.targetId, target?.rootId, `${book.key}: related-book target`);
        assert.equal(portal.title, related.label, `${book.key}: related-book label`);
      });
      report.counts.epicBooks = (report.counts.epicBooks ?? 0) + 1;
      return { words: wordCount(book.summary), primaryTextCharacters: section.length };
    });
  }

  await check('Odyssey narration frames and all journey stages are represented with working targets', () => {
    for (const [index, frame] of epics.odyssey.narrationMap.entries()) {
      const node = byAlias(`odyssey-frame-${index}`);
      assert.equal(node.title, frame.mode, `Narration frame ${index} title`);
      assert.equal(node.text, frame.note, `Narration frame ${index} explanation`);
    }
    for (const step of epics.odyssey.chronology) {
      const key = Object.keys(aliases.nodes).find(key => /^odyssey-journey-\d+-step-\d+$/.test(key) && key.endsWith(`-step-${step.order}`));
      const node = byAlias(present(key, `Missing journey stage ${step.order}`));
      assert.equal(node.kind, 'canvas-link', `Journey stage ${step.order}: not navigable`);
      assert.equal(node.targetId, canvases.get(aliases.canvases[step.books[0]])?.rootId, `Journey stage ${step.order}: wrong first evidence book`);
      assert.ok(node.title.includes(step.title) && node.text.includes(step.note), `Journey stage ${step.order}: explanatory content missing`);
      const citation = present(node.text.match(/Told in Books?\s+([0-9,\s–-]+)\./), `Journey stage ${step.order}: source books missing`);
      const citedBooks = citation[1].split(',').flatMap(part => {
        const range = part.trim().match(/^(\d+)(?:\s*[–-]\s*(\d+))?$/);
        assert.ok(range, `Journey stage ${step.order}: malformed book citation`);
        const first = Number(range[1]), last = Number(range[2] ?? range[1]);
        assert.ok(first >= 1 && last <= 24 && last >= first, `Journey stage ${step.order}: invalid book range`);
        return Array.from({ length: last - first + 1 }, (_, offset) => first + offset);
      });
      assert.deepEqual(citedBooks, step.books.map(key => Number(key.slice(-2))), `Journey stage ${step.order}: cited books differ from evidence`);
    }
    report.counts.chronologyStages = epics.odyssey.chronology.length;
  });

  await check('All cross-epic comparisons preserve questions and linked primary evidence', () => {
    for (const comparison of epics.comparisons) {
      const question = byAlias(`${comparison.key}-question`);
      assert.equal(question.text, `${comparison.text}\n\n${comparison.question}`, `${comparison.key}: question changed`);
      assert.equal(question.canvasId, aliases.canvases[comparison.key], `${comparison.key}: wrong canvas`);
      comparison.books.forEach((key, index) => {
        const portal = byAlias(`${comparison.key}-book-${index}`);
        assert.equal(portal.targetId, canvases.get(aliases.canvases[key])?.rootId, `${comparison.key}: incorrect evidence target ${key}`);
        const connection = links.get(aliases.links[`${comparison.key}-evidence-${index}`]);
        assert.ok(connection?.kind === 'visual' && connection.relation === 'mutual', `${comparison.key}: absent mutual comparison relationship`);
        assert.equal(connection.sourceId, portal.id, `${comparison.key}: comparison source`);
        assert.equal(connection.targetId, question.id, `${comparison.key}: comparison target`);
      });
    }
    report.counts.epicComparisons = epics.comparisons.length;
  });

  await check('Reading room keeps the Hesiod source and source provenance', async () => {
    const bytes = await readFile(path.join(corpus, 'texts', 'hesiod-hymns-evelyn-white-full.txt'));
    assert.equal(hash(Buffer.from(assetAlias('full-hesiod').base64, 'base64')), hash(bytes), 'Hesiod source bytes changed');
    assert.equal(byAlias('reading-hesiod').assetId, assetAlias('full-hesiod').id, 'Hesiod source unattached');
    for (const url of ['https://www.gutenberg.org/ebooks/2199','https://www.gutenberg.org/ebooks/1727','https://www.gutenberg.org/ebooks/348']) assert.ok(documentCorpus.includes(url), `Missing edition URL ${url}`);
    report.assetHashes.push({ key: 'full-hesiod', bytes: bytes.length, sha256: hash(bytes) });
  });

  if (Array.isArray(genealogy.persons)) {
    const persons = genealogy.persons, people = new Map(persons.map(person => [person.key, person]));
    const appearances = new Map(persons.map(person => [person.key, Object.entries(aliases.nodes)
      .filter(([key]) => key === `family-${person.key}` || key === `catalog-${person.key}` || key.endsWith(`-person-${person.key}`))
      .map(([, id]) => nodes.get(id)).filter(Boolean)]));
    const childCounts = {};
    for (const person of persons) childCounts[person.key] = {
      selected: persons.filter(child => child.parents.includes(person.key)).length,
      alternative: persons.filter(child => child.variants?.some(variant => variant.parents.includes(person.key))).length,
      distinct: persons.filter(child => child.parents.includes(person.key) || child.variants?.some(variant => variant.parents.includes(person.key))).length,
      collectiveRecords: persons.filter(child => child.category === 'collective' && (child.parents.includes(person.key) || child.variants?.some(variant => variant.parents.includes(person.key)))).length,
    };
    report.childCounts = childCounts;
    Object.assign(report.counts, { persons: persons.length, variants: persons.reduce((count, person) => count + (person.variants?.length ?? 0), 0), selectedParentEdges: persons.reduce((count, person) => count + person.parents.length, 0), zeusChildren: childCounts.zeus?.distinct });
    await check('Every declared genealogy person has a represented identity', () => {
      assert.equal(people.size, persons.length, 'Duplicate genealogy keys');
      const missing = persons.filter(person => !appearances.get(person.key).length).map(person => person.key);
      assert.equal(missing.length, 0, `Unrepresented people: ${shortList(missing)}`);
      for (const person of persons) {
        for (const parent of [...person.parents, ...(person.variants ?? []).flatMap(variant => variant.parents)]) assert.ok(people.has(parent), `${person.key}: unresolved parent ${parent}`);
        assert.ok(allText.includes(person.summary), `${person.key}: full sourced synopsis missing from readable content`);
      }
      assert.equal(manifest.persons, persons.length, 'Manifest person count');
    });
    await check('All twelve Titans are individually declared and represented', () => {
      const titans = ['oceanus','tethys','coeus','phoebe','crius','hyperion','theia','iapetus','themis','mnemosyne','cronus','rhea'];
      const declared = persons.filter(person => person.groups?.includes('titans-twelve')).map(person => person.key);
      assert.deepEqual(declared.sort(), [...titans].sort(), 'The twelve-Titan register is incomplete or duplicated');
      for (const key of titans) assert.ok(appearances.get(key)?.length, `Missing Titan ${key}`);
      report.counts.titans = titans.length;
    });
    await check('Variant narratives, sources and offspring counts remain explicit', () => {
      const sourceMap = new Map(genealogy.sources.map(source => [source.id, source]));
      const missing = [];
      for (const person of persons) {
        for (const variant of person.variants ?? []) if (!documentCorpus.includes(variant.note)) missing.push(`${person.key}: variant note`);
        for (const ref of [...person.sourceRefs, ...(person.variants ?? []).flatMap(variant => variant.sourceRefs)]) {
          const source = sourceMap.get(ref.sourceId);
          assert.ok(source, `${person.key}: unknown source ${ref.sourceId}`);
          if (!documentCorpus.includes(source.url)) missing.push(`${person.key}: source ${ref.sourceId}`);
          if (ref.locator && !documentCorpus.includes(ref.locator)) missing.push(`${person.key}: locator ${ref.locator}`);
        }
      }
      assert.equal(missing.length, 0, `Missing readable genealogy evidence: ${shortList(missing)}`);
      assert.ok(byAlias('zeus-register-count').title.includes(String(childCounts.zeus.distinct)), 'Displayed Zeus child count is stale');
      assert.equal(manifest.zeusChildren, childCounts.zeus.distinct, 'Manifest Zeus child count is stale');
      const register = documentText(byAlias('zeus-offspring-register'));
      for (const person of persons.filter(person => person.parents.includes('zeus') || person.variants?.some(variant => variant.parents.includes('zeus')))) assert.ok(register.includes(person.name), `Zeus register omits ${person.key}`);
      for (const person of persons) {
        const childNode = nodes.get(aliases.nodes[`profile-${person.key}-children`]);
        if (!childNode) continue;
        // Large families belong in readable attachments, not an overflowing card.
        const dossierText = [childNode.text ?? '', ...['sources','children-register'].map(suffix => {
          const attached = nodes.get(aliases.nodes[`profile-${person.key}-${suffix}`]);
          return attached?.kind === 'document' ? documentText(attached) : attached?.text ?? '';
        })].join('\n\n');
        for (const child of persons.filter(child => child.parents.includes(person.key) || child.variants?.some(variant => variant.parents.includes(person.key)))) assert.ok(dossierText.includes(child.name), `${person.key} dossier/attached register omits child ${child.key}`);
      }
    });
    await check('Every Roman comparison includes both sides and its cautions', () => {
      for (const comparison of genealogy.romanComparisons) {
        const prefix = Object.keys(aliases.nodes).find(key => key.endsWith(`-${comparison.key}-greek`));
        assert.ok(prefix, `Missing Roman comparison ${comparison.key}`);
        const greek = byAlias(prefix), roman = byAlias(prefix.replace(/-greek$/, '-roman'));
        assert.equal(greek.title, comparison.greek, `${comparison.key}: Greek label`);
        assert.equal(roman.title, comparison.roman, `${comparison.key}: Roman label`);
        assert.equal(greek.text, comparison.summary, `${comparison.key}: summary`);
        assert.equal(roman.text, comparison.differences.join('\n\n'), `${comparison.key}: differences`);
        assert.ok(project.links.some(link => link.sourceId === greek.id && link.targetId === roman.id && link.kind === 'visual' && link.relation === 'mutual'), `${comparison.key}: missing comparison link`);
      }
      report.counts.romanComparisons = genealogy.romanComparisons.length;
    });
    await check('Roman and Hesiodic readings retain their primary source locators', () => {
      const sourceMap = new Map(genealogy.sources.map(source => [source.id, source]));
      const inspect = value => {
        if (!value || typeof value !== 'object') return;
        if (Array.isArray(value.sourceRefs)) for (const ref of value.sourceRefs) {
          const source = present(sourceMap.get(ref.sourceId), `Unknown reading source ${ref.sourceId}`);
          assert.ok(documentCorpus.includes(source.url), `Missing source URL ${ref.sourceId}`);
          if (ref.locator) assert.ok(documentCorpus.includes(ref.locator), `Missing source locator ${ref.sourceId}: ${ref.locator}`);
        }
        for (const [key, nested] of Object.entries(value)) if (key !== 'sourceRefs' && typeof nested === 'object') inspect(nested);
      };
      inspect(genealogy.romanComparisons); inspect(genealogy.hesiodWorks);
      for (const work of genealogy.hesiodWorks) {
        assert.equal(byAlias(`hesiod-${work.key}-summary`).text, work.summary, `${work.key}: Hesiodic argument missing`);
        work.sections.forEach((section, index) => assert.ok(byAlias(`hesiod-${work.key}-section-${index}`).text.includes(section.summary), `${work.key}: section ${index} missing`));
      }
      report.counts.hesiodicWorks = genealogy.hesiodWorks.length;
    });
  }

  await check('Museum artwork and media fixtures retain original bytes and provenance', async () => {
    const media = await json(path.join(corpus, 'media.json'));
    const fixtures = await json(path.join(corpus, 'fixtures.json'));
    for (const record of media) {
      const sourceBytes = await readFile(path.join(corpus, 'media', record.file));
      const embedded = assetAlias(`art-${record.key}`);
      assert.equal(hash(sourceBytes), record.sha256, `Local museum source changed: ${record.file}`);
      assert.equal(hash(Buffer.from(embedded.base64, 'base64')), record.sha256, `Embedded museum source changed: ${record.file}`);
      assert.ok(attachedAssets.has(embedded.id), `Artwork is not attached: ${record.key}`);
      assert.ok(documentCorpus.includes(record.url) && documentCorpus.includes(record.sha256), `Missing museum provenance ${record.key}`);
      report.assetHashes.push({ key: `art-${record.key}`, bytes: sourceBytes.length, sha256: record.sha256 });
    }
    for (const [index, record] of fixtures.entries()) {
      const sourceBytes = await readFile(path.join(corpus, 'media', record.file));
      const embedded = assetAlias(`fixture-${index}`);
      assert.equal(hash(Buffer.from(embedded.base64, 'base64')), hash(sourceBytes), `Fixture changed: ${record.file}`);
      assert.equal(byAlias(`media-fixture-${index}`).assetId, embedded.id, `Fixture unattached: ${record.file}`);
      report.assetHashes.push({ key: `fixture-${index}`, bytes: sourceBytes.length, sha256: hash(sourceBytes) });
    }
    const pdf = await readFile(path.join(corpus, 'media', 'mythology-companion.pdf'));
    assert.equal(hash(Buffer.from(assetAlias('original-test-guide').base64, 'base64')), hash(pdf), 'PDF bytes changed');
    report.assetHashes.push({ key: 'original-test-guide', bytes: pdf.length, sha256: hash(pdf) });
  });

  await check('Every canvas is reachable through portals or reference context navigation', () => {
    const index = navigationIndex(project), edges = new Map(project.canvases.map(canvas => [canvas.id, new Set()]));
    for (const node of project.nodes) if (['canvas-link','reference'].includes(node.kind)) {
      const original = index.original(node.id);
      assert.ok(original, `Unresolved original context: ${node.title} (${node.id})`);
      edges.get(node.canvasId)?.add(original.canvasId);
      // Viewer provides a reciprocal "Referenced here" path for selected originals.
      if (node.kind === 'reference') edges.get(original.canvasId)?.add(node.canvasId);
    }
    const visited = new Set(), queue = [project.entryCanvasId];
    while (queue.length) { const id = queue.shift(); if (visited.has(id)) continue; visited.add(id); queue.push(...edges.get(id) ?? []); }
    const unreachable = project.canvases.filter(canvas => !visited.has(canvas.id));
    report.reachability = { reachable: visited.size, total: project.canvases.length, unreachable: unreachable.map(canvas => ({ id: canvas.id, title: canvas.title })) };
    assert.equal(unreachable.length, 0, `Unreachable canvases: ${shortList(unreachable.map(canvas => canvas.title))}`);
  });

  await check('Internal saved references show current authored originals', () => {
    for (const node of project.nodes.filter(node => node.kind === 'reference')) {
      const source = present(nodes.get(node.targetId), `Missing internal reference source ${node.title}`);
      if (source.canvasId === node.canvasId) continue;
      const saved = present(project.snapshots[node.targetId], `Missing pinned snapshot ${node.title}`);
      assert.deepEqual(saved.node, source, `Stale pinned original: ${node.title}`);
      if (source.assetId) assert.deepEqual(saved.asset, assets.get(source.assetId), `Stale pinned asset: ${node.title}`);
    }
  });

  await check('Publication matches the authored graph and imports with fresh identities', () => {
    const match = present(html.match(/<script\b[^>]*\bid=["']meridian-data["'][^>]*>([\s\S]*?)<\/script>/i), 'Missing publication payload');
    const payload = JSON.parse(match[1]);
    const expected = createPublicationPayload(project);
    assert.deepEqual(expected.missing, [], 'Publication has missing dependencies');
    // JSON publication has ordinary dictionaries; in-memory snapshots use a null prototype.
    // Compare serialized semantics without letting an assertion dump embedded asset bytes.
    assert.ok(isDeepStrictEqual(payload.project, JSON.parse(JSON.stringify(expected.project))), 'Publication is stale or omits authored content');
    assert.deepEqual(payload.missing, [], 'Generated publication reports missing dependencies');
    const imported = readPublication(html), original = payload.project;
    assert.notEqual(imported.id, original.id, 'Publication import reused the project ID');
    assert.equal(imported.revision, 0, 'Publication import is not a fresh draft');
    assert.deepEqual(validateProject(imported), [], 'Imported publication is invalid');
    const idMap = new Map([[original.id, imported.id]]);
    const originalIds = new Set([original.id, ...['canvases','nodes','links','assets'].flatMap(field => original[field].map(entity => entity.id))]);
    for (const field of ['canvases','nodes','links','assets']) {
      assert.equal(imported[field].length, original[field].length, `Imported ${field} count`);
      original[field].forEach((entity, index) => {
        const copy = imported[field][index];
        assert.ok(!originalIds.has(copy.id), `Reused ${field} identity ${entity.id}`);
        idMap.set(entity.id, copy.id);
      });
    }
    assert.equal(imported.entryCanvasId, idMap.get(original.entryCanvasId), 'Entry canvas not remapped');
    for (const [index, node] of original.nodes.entries()) {
      const copy = imported.nodes[index];
      assert.equal(copy.canvasId, idMap.get(node.canvasId), 'Node canvas not remapped');
      assert.equal(copy.parentId, node.parentId ? idMap.get(node.parentId) : null, 'Containment not remapped');
      if (node.targetId) assert.equal(copy.targetId, idMap.get(node.targetId), 'Internal reference/portal not remapped');
      if (node.assetId) assert.equal(copy.assetId, idMap.get(node.assetId), 'Asset reference not remapped');
      assert.deepEqual(stripFields(copy, ['id','canvasId','parentId','targetId','assetId']), stripFields(node, ['id','canvasId','parentId','targetId','assetId']), `Authored node defaults changed on import: ${node.title}`);
    }
    original.canvases.forEach((canvas, index) => {
      assert.equal(imported.canvases[index].rootId, idMap.get(canvas.rootId), 'Canvas root not remapped');
      assert.deepEqual(stripFields(imported.canvases[index], ['id','rootId']), stripFields(canvas, ['id','rootId']), 'Canvas appearance changed on import');
    });
    original.links.forEach((link, index) => {
      const copy = imported.links[index];
      for (const field of ['canvasId','sourceId','targetId']) assert.equal(copy[field], idMap.get(link[field]), `Link ${field} not remapped`);
      assert.deepEqual(stripFields(copy, ['id','canvasId','sourceId','targetId']), stripFields(link, ['id','canvasId','sourceId','targetId']), 'Link styling or routing changed');
    });
    original.assets.forEach((asset, index) => assert.ok(isDeepStrictEqual(stripFields(imported.assets[index], ['id']), stripFields(asset, ['id'])), `Asset changed on import: ${asset.name}`));
    for (const [id, saved] of Object.entries(original.snapshots)) {
      const copy = present(imported.snapshots[idMap.get(id)], `Snapshot key not remapped ${id}`);
      assert.equal(copy.node.id, idMap.get(saved.node.id), 'Snapshot node not remapped');
      assert.equal(copy.node.canvasId, idMap.get(saved.node.canvasId), 'Snapshot canvas not remapped');
      if (saved.node.targetId) assert.equal(copy.node.targetId, idMap.get(saved.node.targetId), 'Snapshot reference not remapped');
      assert.equal(copy.value, saved.value, 'Snapshot value changed');
      assert.equal(copy.savedAt, saved.savedAt, 'Snapshot timestamp changed');
      assert.deepEqual(stripFields(copy.node, ['id','canvasId','parentId','targetId','assetId']), stripFields(saved.node, ['id','canvasId','parentId','targetId','assetId']), 'Snapshot content changed');
      if (saved.asset) {
        assert.equal(copy.asset.id, idMap.get(saved.asset.id), 'Snapshot asset not remapped');
        assert.ok(isDeepStrictEqual(stripFields(copy.asset, ['id']), stripFields(saved.asset, ['id'])), 'Snapshot asset bytes changed');
      }
    }
    const importedIds = new Set([imported.id, ...['canvases','nodes','links','assets'].flatMap(field => imported[field].map(entity => entity.id))]);
    assert.equal(importedIds.size, 1 + imported.canvases.length + imported.nodes.length + imported.links.length + imported.assets.length, 'Fresh identities are not unique');
    report.importedProjectId = imported.id;
  });

  await check('Logic defaults and authored objects remain unchanged by all checks', () => {
    const evaluation = evaluate(project);
    assert.equal(byAlias('lab-input-one').value, true, 'First input default changed');
    assert.equal(byAlias('lab-input-two').value, true, 'Second input default changed');
    assert.equal(byAlias('lab-switch').closed, true, 'Switch default changed');
    assert.equal(evaluation.values[byAlias('lab-output').id], true, 'Authored output is not true');
    assert.notEqual(byAlias('lab-output').parentId, byAlias('lab-logic').id, 'Output must remain outside collapsing logic parent');
    assert.equal(JSON.stringify(project), authoredBefore, 'Audit mutated the authored project object');
  });
  await check('Original collection still matches the generator baseline hash', async () => {
    const bytes = await readFile(path.join(root, 'examples/greek-mythology/greek-mythology.project.json'));
    assert.equal(hash(bytes), manifest.originalCollectionHash, 'Original mythology collection changed since the build baseline');
  });
}

await check('All protected input files retain their byte hashes after validation', async () => {
  for (const [filename, expected] of beforeHashes) assert.equal(hash(await readFile(filename)), expected, `Input changed during audit: ${filename}`);
  return { files: beforeHashes.size };
});
report.status = report.checks.some(check => check.status === 'failed') ? 'failed' : 'passed';
report.passed = report.checks.filter(check => check.status === 'passed').length;
report.failed = report.checks.filter(check => check.status === 'failed').length;
await mkdir(path.dirname(reportPath), { recursive: true });
await writeFile(reportPath, JSON.stringify(report, null, 2));
console.log(JSON.stringify({ status: report.status, projectId: report.projectId, passed: report.passed, failed: report.failed, counts: report.counts, failures: report.checks.filter(check => check.status === 'failed'), report: reportPath }, null, 2));
if (report.failed) process.exitCode = 1;
