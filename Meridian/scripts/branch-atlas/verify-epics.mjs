import fs from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { build } from 'esbuild';
import { chromium, expect } from '@playwright/test';
import assert from 'node:assert/strict';
import { buildPublication } from '../../src/publication.ts';
import { navigationIndex } from '../../src/core/navigation.ts';
import { validateProject } from '../../src/core/rules.ts';
import { revealChildren } from '../../src/core/reveal.ts';
import { reflowPassage } from '../../scripts/branch-atlas/epics.mjs';

const inputText = await fs.readFile('output/branch-atlas/atlas.json', 'utf8');
const input = JSON.parse(inputText), candidateHash = createHash('sha256').update(inputText).digest('hex');
const report = JSON.parse(await fs.readFile('output/branch-atlas/manifest.json', 'utf8')).epics;
const bundle = await build({ entryPoints: ['src/viewer.ts'], bundle: true, write: false, format: 'iife', target: ['es2022'], minify: true });
const css = await fs.readFile('src/scene.css', 'utf8');
const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1680, height: 1120 }, deviceScaleFactor: 1 });
await page.route(/^https?:/, route => route.abort());
const errors = []; page.on('pageerror', error => errors.push(error.message));
const reviews = [];
const coverage = [];
function overlaps(nodes) {
  const result = [];
  for (const [index, a] of nodes.entries()) for (const b of nodes.slice(index + 1)) if (a.x + a.width > b.x + .01 && b.x + b.width > a.x + .01 && a.y + a.height > b.y + .01 && b.y + b.height > a.y + .01) result.push([a.id, b.id]);
  return result;
}
for (const epic of report.epics) {
  const opened = revealChildren(input, epic.id, true), oi = navigationIndex(opened);
  assert.deepEqual(overlaps(opened.nodes.filter(node => node.kind !== 'canvas' && oi.isNodeVisible(node.id))), []);
  for (const book of report.books.filter(book => book.epic === epic.key)) {
    const shown = revealChildren(opened, book.id, true), si = navigationIndex(shown);
    assert.deepEqual(overlaps(shown.nodes.filter(node => node.kind !== 'canvas' && si.isNodeVisible(node.id))), [], `${book.title}: reading block overlaps a visible node`);
    const raw = Buffer.from(input.assets.find(asset => asset.id === book.sourceAssetId).base64, 'base64').toString('utf8');
    assert.equal(createHash('sha256').update(raw).digest('hex'), book.sourceHash, 'Original source attachment must remain byte-identical');
    assert.equal(book.sections.map(part => raw.slice(part.start, part.end)).join(''), raw.slice(book.narrativeStart, book.narrativeEnd));
    for (const section of book.sections) assert.equal(si.nodes.get(section.id).text, reflowPassage(raw.slice(section.start, section.end)), 'Inline passage wording must match source');
    const hidden = revealChildren(shown, book.id, false), hi = navigationIndex(hidden);
    for (const section of book.sections) assert.equal(hi.isNodeVisible(section.id), false);
    for (const link of shown.links.filter(link => link.parentId === book.id)) assert.equal(hi.isLinkVisible(link), false);
    coverage.push({ epic: epic.key, book: book.number, passageCount: book.passageCount, sourceBytes: book.sourceBytes, overlaps: 0, collapseHidesPassagesAndLinks: true });
  }
}
let combined = input;
for (const epic of report.epics) combined = revealChildren(combined, epic.id, true);
for (const book of report.books.filter(book => [1, 2].includes(book.number))) combined = revealChildren(combined, book.id, true);
const combinedIndex = navigationIndex(combined);
assert.deepEqual(overlaps(combined.nodes.filter(node => node.kind !== 'canvas' && combinedIndex.isNodeVisible(node.id))), [], 'Two books open in each epic must remain clear of one another');
const card = id => page.locator(`.ms-node[data-node-id="${id}"]`);
const metrics = () => page.locator('.ms-node').evaluateAll(cards => cards.map(card => {
  const title = card.querySelector('.ms-node-title'), body = card.querySelector('.ms-node-body'), bounds = card.getBoundingClientRect();
  return { id: card.dataset.nodeId, title: title?.textContent, clippedTitle: !!title && (title.scrollHeight > title.clientHeight + 2 || title.scrollWidth > title.clientWidth + 2), scrollable: !!body && body.scrollHeight > body.clientHeight + 2, x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height };
}));
try {
  for (const epic of report.epics) {
    const project = structuredClone(input);
    assert.deepEqual(validateProject(project), []);
    const html = `output/branch-atlas/epics-${epic.key}-preview.html`;
    await fs.writeFile(html, buildPublication(project, bundle.outputFiles[0].text, css));
    await page.goto(pathToFileURL(process.cwd() + '/' + html).href);
    await card(epic.id).dblclick({ position: { x: 45, y: 45 } });
    await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click();
    await page.evaluate(() => document.fonts.ready);
    const expected = revealChildren(input, epic.id, true), expectedIndex = navigationIndex(expected);
    assert.equal(await page.locator('.ms-node').count(), expected.nodes.filter(node => node.kind !== 'canvas' && expectedIndex.isNodeVisible(node.id)).length);
    await page.screenshot({ path: `output/branch-atlas/epics-${epic.key}-overview.png` });
    const overview = await metrics();
    const book = report.books.find(book => book.epic === epic.key && book.number === 1);
    await card(book.id).dblclick({ position: { x: 45, y: 45 } });
    await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click();
    await page.screenshot({ path: `output/branch-atlas/epics-${epic.key}-book-01.png` });
    assert.ok(await card(book.sections[0].id).isVisible());
    const opened = await metrics();
    await card(book.sections[0].id).click({ position: { x: 40, y: 40 } });
    await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click();
    await page.screenshot({ path: `output/branch-atlas/epics-${epic.key}-passage-readable.png` });
    const paragraphNode = card(book.sections[0].id);
    assert.ok((await paragraphNode.boundingBox()).width > 650, 'Capture must show the passage at readable zoom');
    const textBody = paragraphNode.locator('.ms-text'), camera = await page.locator('.ms-world').getAttribute('style');
    await textBody.hover(); await page.mouse.wheel(0, 700);
    await expect.poll(() => textBody.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
    assert.equal(await page.locator('.ms-world').getAttribute('style'), camera, 'Reading a passage should scroll its text without changing camera zoom');
    // Return through the actual outline so the original chapter can be collapsed after reading.
    if (!await page.locator('.mv-navigation').isVisible()) await page.getByRole('button', { name: 'Explore project', exact: true }).click();
    await page.getByLabel('Search project', { exact: true }).fill(book.id);
    await page.locator('.mv-search-result').first().click();
    await card(book.id).dblclick({ position: { x: 45, y: 45 } });
    assert.equal(await card(book.sections[0].id).count(), 0);
    reviews.push({ epic: epic.key, overview, opened, disclosure: 'double-click reveals passage nodes; another double-click hides them' });
  }
  assert.deepEqual(errors, []);
  await fs.writeFile('output/branch-atlas/epics-browser-review.json', JSON.stringify({ candidateHash, errors, coverage, simultaneousFirstTwoBooksInBothEpics: { overlaps: 0 }, reviews }, null, 2));
  console.log(JSON.stringify({ errors, epics: reviews.map(review => ({ epic: review.epic, visibleOverviewNodes: review.overview.length, overviewClippedTitles: review.overview.filter(node => node.clippedTitle).map(node => node.title), expandedNodes: review.opened.length })) }));
} finally { await browser.close(); }
