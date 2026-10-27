/** Offline review of the native C++ catalogue Work. No HTTP requests or live writes.
 * Run only after the composition is frozen. Browser interaction changes its private
 * runtime clone; source files and the inert publication payload must remain intact.
 */
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { chromium } from 'playwright';
import { revealChildren } from '../../src/core/reveal.ts';
import { navigationIndex } from '../../src/core/navigation.ts';

const args = new Map();
for (let at = 2; at < process.argv.length; at += 2) {
  assert(['--input', '--out'].includes(process.argv[at]) && process.argv[at + 1], 'Use --input DIR [--out DIR].');
  args.set(process.argv[at], process.argv[at + 1]);
}
const directory = path.resolve(args.get('--input') ?? 'output/programming-trees-20261021/cpp');
const out = path.resolve(args.get('--out') ?? path.join(directory, 'browser-review'));
await mkdir(out, { recursive: true });
const json = async file => JSON.parse((await readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const digest = value => createHash('sha256').update(value).digest('hex');
const luminance = color => {
  const values=(color.match(/[\d.]+/g)??[]).slice(0,3).map(Number); assert.equal(values.length,3,`Cannot inspect color ${color}`);
  const linear=values.map(value=>{const v=value/255;return v<=.04045?v/12.92:((v+.055)/1.055)**2.4;});
  return linear[0]*.2126+linear[1]*.7152+linear[2]*.0722;
};
const contrast = (a,b) => {const one=luminance(a),two=luminance(b);return(Math.max(one,two)+.05)/(Math.min(one,two)+.05);};
const files = ['draft.json', 'aliases.json', 'manifest.json', 'publication.html'];
const [project, aliases, manifest, catalogue] = await Promise.all([
  ...files.slice(0, 3).map(file => json(path.join(directory, file))),
  json('content/programming-trees-20261021/cpp-types.json'),
]);
const sourceHashes = Object.fromEntries(await Promise.all(files.map(async file => [file, digest(await readFile(path.join(directory, file)))])));
sourceHashes['cpp-types.json'] = digest(await readFile('content/programming-trees-20261021/cpp-types.json'));
const workId = aliases.canvases.tree;
assert(workId, 'Missing aliases.canvases.tree.');
const work = project.canvases.find(item => item.id === workId); assert(work, 'Tree Work is missing.');
const nodeId = key => { const id = aliases.nodes[key]; assert(id, `Missing alias ${key}`); return id; };
const node = key => { const result = project.nodes.find(item => item.id === nodeId(key)); assert(result, `Missing node ${key}`); return result; };
const typeKey = qn => `type/${qn}`;
const categoryKey = id => `category/${id}`;
const groupKey = id => `group/${id}`;
const category = id => { const result = catalogue.categories.find(item => item.id === id); assert(result, `Missing catalogue category ${id}`); return result; };
const group = (cat, id) => { const result = category(cat).children.find(item => item.id === `${cat}/${id}`); assert(result, `Missing group ${cat}/${id}`); return result; };
const checks = [], browserErrors = [], rejectedRequests = [];
let screenshotCount = 0, initialPayload;
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ viewport: { width: 1840, height: 1180 }, deviceScaleFactor: 1, acceptDownloads: true, reducedMotion: 'reduce' });
const page = await context.newPage();
page.setDefaultTimeout(15000);
page.on('pageerror', error => browserErrors.push(error.message));
await context.route(/^https?:/i, route => { rejectedRequests.push({ method: route.request().method(), url: route.request().url() }); return route.abort(); });
await context.routeWebSocket(/.*/, socket => socket.close());
const card = key => page.locator(`.ms-node[data-node-id="${nodeId(key)}"]`);
async function settle() {
  await page.evaluate(async () => {
    await document.fonts.ready;
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  });
}
async function capture(label) {
  await settle();
  const name = `${String(++screenshotCount).padStart(2, '0')}-${label}.png`;
  await page.screenshot({ path: path.join(out, name) }); return name;
}
async function check(name, run) {
  try { checks.push({ name, passed: true, ...await run() }); }
  catch (error) {
    let screenshot;
    try { screenshot = await capture(`FAILED-${name.replace(/[^a-z0-9]/gi, '-').slice(0, 70)}`); } catch {}
    checks.push({ name, passed: false, error: error.stack ?? String(error), screenshot });
  }
}
async function closePanel() {
  const button = page.getByRole('button', { name: 'Close project navigation', exact: true });
  if (await button.isVisible()) await button.click();
}
async function focus(key, { keepPanel = false } = {}) {
  if (await page.getByRole('button', { name: 'Explore project', exact: true }).getAttribute('aria-expanded') !== 'true')
    await page.getByRole('button', { name: 'Explore project', exact: true }).click();
  await page.getByRole('searchbox', { name: 'Search project', exact: true }).fill(nodeId(key));
  await page.locator(`[data-navigation-node="${nodeId(key)}"]`).click();
  if (!keepPanel) await closePanel();
  await settle();
}
async function setExpanded(key, expanded) {
  await focus(key, { keepPanel: true });
  const toggle = page.getByRole('button', { name: 'Toggle contents from properties', exact: true });
  assert.equal(await toggle.count(), 1, `${key} must expose native disclosure.`);
  const currentlyExpanded = /Hide contents/.test(await toggle.innerText());
  if (currentlyExpanded !== expanded) await toggle.click();
  await closePanel(); await settle();
}
async function fitBranch(key) {
  await focus(key);
  await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click(); await settle();
}
async function frame(key) {
  return card(key).evaluate(element => ({ left: element.style.left, top: element.style.top, width: element.style.width, height: element.style.height }));
}
async function leafEvidence(qualifiedName) {
  const key = typeKey(qualifiedName), record = catalogue.types.find(item => item.qualifiedName === qualifiedName);
  await focus(key);
  const text = await card(key).innerText();
  assert(text.includes(record.primaryHeader), `${qualifiedName} header is absent from the visible tile.`);
  assert(text.includes(record.purpose), `${qualifiedName} purpose is absent from the visible tile.`);
  const title = card(key).locator('.ms-node-title').first();
  // Target uses the available viewport; step back to a comfortable reading card
  // instead of presenting a single type at billboard scale.
  for (let tries=0;tries<12;tries++) {
    const current=await card(key).boundingBox(); if(!current || current.width<=1050)break;
    await page.getByRole('button',{name:'Zoom out',exact:true}).click(); await settle();
  }
  let titleBox = await title.boundingBox(), lastZoom = '';
  for (let tries = 0; tries < 24 && titleBox && titleBox.height < 16; tries++) {
    const zoom = await page.locator('.ms-zoom').innerText(); if (zoom === lastZoom) break;
    lastZoom = zoom; await page.getByRole('button', { name: 'Zoom in', exact: true }).click(); await settle();
    titleBox = await title.boundingBox();
  }
  assert(titleBox && titleBox.height >= 14, `${qualifiedName} title cannot reach reading size using native zoom.`);
  const links = await card(key).locator('a[href]').evaluateAll(elements => elements.map(element => {
    const box=element.getBoundingClientRect(),card=element.closest('.ms-node').getBoundingClientRect(),style=getComputedStyle(element);
    return {href:element.href,rel:element.rel,target:element.target,text:element.textContent,
      box:{x:box.x,y:box.y,width:box.width,height:box.height},card:{x:card.x,y:card.y,width:card.width,height:card.height},
      color:style.color,background:getComputedStyle(element.closest('.ms-node'),'::after').backgroundColor,
      overflow:{left:Math.max(0,card.left-box.left),right:Math.max(0,box.right-card.right),top:Math.max(0,card.top-box.top),bottom:Math.max(0,box.bottom-card.bottom)}};
  }));
  assert(links.some(link => [record.sourceUrl, record.referenceUrl].includes(link.href)), `${qualifiedName} has no native clickable standard-source link.`);
  assert(links.every(link => link.target !== '_blank' || link.rel.includes('noopener')));
  const screenshot=await capture(qualifiedName.replace(/[^a-z0-9]/gi,'-'));
  assert(links.every(link=>Math.max(...Object.values(link.overflow))<2),`${qualifiedName} source label exceeds its visible card bounds: ${JSON.stringify(links.map(link=>link.overflow))}`);
  for(const link of links){link.contrastRatio=contrast(link.color,link.background);assert(link.contrastRatio>=4.5,`${qualifiedName} source text contrast ${link.contrastRatio.toFixed(2)}:1 is too low.`);}
  return { qualifiedName, titleBox, zoom: await page.locator('.ms-zoom').innerText(), sourceLinks: links,
    screenshot };
}

function jointPath(sourceId, targetId) {
  const byId = new Map(project.nodes.map(item => [item.id,item]));
  const queue = [{ id:sourceId, links:[] }], seen = new Set([sourceId]);
  while (queue.length) {
    const current = queue.shift();
    for (const link of project.links.filter(item => item.canvasId === workId)) {
      let next;
      if (link.sourceId === current.id) next = link.targetId;
      else if (link.direction === 'none' && link.targetId === current.id) next = link.sourceId;
      if (!next || seen.has(next)) continue;
      const route = [...current.links, link.id];
      if (next === targetId) return route;
      if (byId.get(next)?.kind === 'junction') { seen.add(next); queue.push({ id:next, links:route }); }
    }
  }
  return null;
}

try {
  await check('965 types have a unique native task hierarchy and source-backed detail', async () => {
    assert.equal(catalogue.types.length, 965); assert.equal(catalogue.categories.length, 13);
    assert.equal(catalogue.categories.reduce((sum, item) => sum + item.children.length, 0), 62);
    assert.equal(work.title, 'C++ classes and types');
    const root = node('topic/std'), ids = new Set();
    assert.equal(root.canvasId, workId); assert.equal(root.kind, 'media');
    for (const cat of catalogue.categories) {
      const catNode = node(categoryKey(cat.id)); assert.equal(catNode.parentId, root.id);
      for (const sub of cat.children) {
        const groupNode = node(groupKey(sub.id)); assert.equal(groupNode.parentId, catNode.id);
        for (const id of sub.typeIds) {
          assert(!ids.has(id)); ids.add(id);
          const original = catalogue.types.find(item => item.id === id), actual = node(typeKey(id));
          assert.equal(actual.canvasId, workId); assert.equal(actual.parentId, groupNode.id);
          assert.equal(actual.kind, 'media'); assert(actual.tag.includes(original.qualifiedName));
          assert(actual.text.includes(original.primaryHeader)); assert(actual.text.includes(original.purpose));
          assert(original.sourceUrl.startsWith('https://github.com/cplusplus/draft/blob/n4950/source/'));
          assert(original.referenceUrl.startsWith('https://timsong-cpp.github.io/cppwp/n4950/'));
          assert([actual.url, actual.hyperlink?.url, ...(actual.hyperlinks??[]).map(link=>link.url), actual.content?.url, actual.content?.hyperlink?.url].some(value => [original.sourceUrl, original.referenceUrl].includes(value)), `${id}: source hyperlink not retained.`);
        }
      }
    }
    assert.equal(ids.size, 965);
    const treeAliases = ['topic/std', ...catalogue.categories.flatMap(cat => [categoryKey(cat.id), ...cat.children.flatMap(sub => [groupKey(sub.id), ...sub.typeIds.map(typeKey)])])];
    for (const key of treeAliases) {
      const actual = node(key); assert(!actual.expansionTray, `${key} unexpectedly uses an Expansion Tray.`);
      assert.equal(actual.childLayout, 'spatial'); assert.equal(actual.childArrangement, 'free');
    }
    return { types: ids.size, categories: 13, groups: 62, actualNativeNodes: treeAliases.length, workId };
  });
  await check('Every ownership branch has a real native relationship through its shared Joints', async () => {
    const edges = catalogue.categories.flatMap(cat => [
      ['topic/std',categoryKey(cat.id)],
      ...cat.children.flatMap(sub => [[categoryKey(cat.id),groupKey(sub.id)],...sub.typeIds.map(id => [groupKey(sub.id),typeKey(id)])]),
    ]);
    const paths=[];
    for (const [parent,child] of edges) {
      assert(node(child).x >= node(parent).x + node(parent).width, `${child} must occupy a later rightward generation.`);
      const links=jointPath(nodeId(parent),nodeId(child)); assert(links, `${parent} → ${child} is missing a real connection.`);
      paths.push({parent,child,links});
    }
    assert.equal(paths.length,1040);
    await writeFile(path.join(out,'relationship-paths.json'),JSON.stringify(paths,null,2)+'\n');
    return {relationships:paths.length,sharedJointRoutes:paths.filter(item => item.links.length>1).length};
  });
  await page.goto(pathToFileURL(path.join(directory, 'publication.html')).href);
  initialPayload = JSON.parse(await page.locator('#meridian-data').textContent());
  await page.getByLabel('Choose Work', { exact: true }).selectOption(workId);
  await check('Actual viewer opens the authored branching overview', async () => {
    assert.equal(initialPayload.project.id, project.id);
    await setExpanded('topic/std', true); await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
    assert.equal(await card(categoryKey('containers')).count(), 1);
    assert.equal(await card(categoryKey('memory')).count(), 1);
    return { screenshot: await capture('overview'), authoredRevision: project.revision };
  });
  await check('Containers branch discloses sequence types toward the right', async () => {
    await setExpanded(categoryKey('containers'), true);
    const sub = group('containers', 'sequence'); await setExpanded(groupKey(sub.id), true);
    assert.equal(await card(typeKey('std::vector')).count(), 1);
    for (const id of sub.typeIds) {
      assert(node(typeKey(id)).x > node(groupKey(sub.id)).x);
      assert.equal(await card(typeKey(id)).count(), 1);
    }
    await fitBranch(categoryKey('containers'));
    return { types: sub.typeIds.length, screenshot: await capture('containers-branch') };
  });
  await check('Vector displays readable native text and a clickable official source', async () => leafEvidence('std::vector'));
  await check('Memory branch discloses smart owners toward the right', async () => {
    await setExpanded(categoryKey('memory'), true); const sub = group('memory', 'owners');
    await setExpanded(groupKey(sub.id), true); assert.equal(await card(typeKey('std::shared_ptr')).count(), 1);
    await fitBranch(categoryKey('memory'));
    return { types: sub.typeIds.length, screenshot: await capture('memory-ownership-branch') };
  });
  await check('Shared ownership displays readable native detail and a clickable official source', async () => leafEvidence('std::shared_ptr'));
  await check('Closing one sibling branch preserves the other and exact descendant coordinates', async () => {
    const samples = [typeKey('std::vector'), typeKey('std::shared_ptr'), groupKey('memory/owners'), groupKey('containers/sequence')];
    const before = Object.fromEntries(await Promise.all(samples.map(async key => [key, await frame(key)])));
    await setExpanded(categoryKey('containers'), false);
    assert.equal(await card(typeKey('std::vector')).count(), 0); assert.equal(await card(typeKey('std::shared_ptr')).count(), 1);
    await setExpanded(categoryKey('containers'), true);
    assert.equal(await card(typeKey('std::vector')).count(), 1);
    await setExpanded(categoryKey('memory'), false);
    assert.equal(await card(typeKey('std::shared_ptr')).count(), 0); assert.equal(await card(typeKey('std::vector')).count(), 1);
    await setExpanded(categoryKey('memory'), true);
    const after = Object.fromEntries(await Promise.all(samples.map(async key => [key, await frame(key)])));
    assert.deepEqual(after, before); assert.equal(await page.locator('.ms-expansion-tray').count(), 0);
    return { exactRetainedFrames: after, noExpansionTrays: true };
  });
  await check('Closing and reopening the root remembers the expanded branches', async () => {
    const before = await frame(typeKey('std::shared_ptr'));
    await setExpanded('topic/std', false); assert.equal(await card(typeKey('std::shared_ptr')).count(), 0);
    await setExpanded('topic/std', true); assert.equal(await card(typeKey('std::shared_ptr')).count(), 1);
    assert.equal(await card(typeKey('std::vector')).count(), 1); assert.deepEqual(await frame(typeKey('std::shared_ptr')), before);
    return { branchesRetained: ['memory/owners','containers/sequence'], rememberedSharedPointerFrame: before };
  });
  await check('JSON reopen preserves original branch coordinates, identities and nested disclosure', async () => {
    let runtime=structuredClone(project);
    for(const key of ['topic/std',categoryKey('memory'),groupKey('memory/owners'),categoryKey('containers'),groupKey('containers/sequence')])
      runtime=revealChildren(runtime,nodeId(key),true);
    const frames = value => value.nodes.filter(item => item.canvasId===workId).map(item => ({id:item.id,x:item.x,y:item.y,width:item.width,height:item.height})).sort((a,b)=>a.id.localeCompare(b.id));
    const before=frames(runtime), ids=runtime.nodes.map(item => item.id), links=runtime.links.map(item => item.id);
    runtime=revealChildren(runtime,nodeId('topic/std'),false);
    runtime=JSON.parse(JSON.stringify(runtime));
    runtime=revealChildren(runtime,nodeId('topic/std'),true);
    assert.deepEqual(frames(runtime),before); assert.deepEqual(runtime.nodes.map(item => item.id),ids); assert.deepEqual(runtime.links.map(item => item.id),links);
    assert(navigationIndex(runtime).isNodeVisible(nodeId(typeKey('std::vector'))));
    assert(navigationIndex(runtime).isNodeVisible(nodeId(typeKey('std::shared_ptr'))));
    assert(runtime.nodes.filter(item => item.canvasId===workId).every(item => !item.expansionTray));
    return {frames:before.length,identities:ids.length,links:links.length,rememberedGroups:['containers/sequence','memory/owners']};
  });
  await check('Complete CSV attachment downloads byte for byte', async () => {
    const asset = project.assets.find(item => item.name === 'Cpp-standard-library-types.csv'); assert(asset, 'Complete type CSV is absent.');
    const actual = project.nodes.find(item => item.assetId === asset.id && item.canvasId === workId); assert(actual, 'CSV must be reachable from this Work.');
    const key = Object.keys(aliases.nodes).find(key => aliases.nodes[key] === actual.id); assert(key);
    await focus(key, { keepPanel: true }); await page.getByRole('button', { name: 'Open selected attachment', exact: true }).click();
    const dialog = page.locator('.ms-asset-dialog').last(); const downloading = page.waitForEvent('download');
    await dialog.getByRole('button', { name: `Download ${asset.name}`, exact: true }).click();
    const downloaded = await downloading, bytes = await readFile(await downloaded.path());
    assert.deepEqual(bytes, Buffer.from(asset.base64, 'base64'));
    assert.deepEqual(bytes, await readFile('content/programming-trees-20261021/Cpp-standard-library-types.csv'));
    await dialog.getByRole('button', { name: 'Close attachment', exact: true }).click(); await closePanel();
    return { filename: downloaded.suggestedFilename(), bytes: bytes.length, sha256: digest(bytes) };
  });
  await check('Temporary viewer exploration leaves the inert authored payload unchanged', async () => {
    assert.deepEqual(JSON.parse(await page.locator('#meridian-data').textContent()), initialPayload);
    return { projectId: project.id, workId, payloadSHA256: digest(JSON.stringify(initialPayload)) };
  });
} finally {
  await context.close(); await browser.close();
}
const unchanged = Object.fromEntries(await Promise.all(files.map(async file => [file, sourceHashes[file] === digest(await readFile(path.join(directory, file)))])));
const report = { generatedAt: new Date().toISOString(), directory, workId, sourceHashes, unchanged, manifest,
  summary: { checks: checks.length, passed: checks.filter(item => item.passed).length, failed: checks.filter(item => !item.passed).length, screenshots: screenshotCount },
  checks, browserErrors, rejectedRequests };
await writeFile(path.join(out, 'review.json'), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify({ ...report.summary, browserErrors, rejectedRequests, unchanged,
  failures: checks.filter(item => !item.passed).map(item => ({ name: item.name, error: item.error.split('\n').slice(0,3).join('\n') })), report: path.join(out, 'review.json') }, null, 2));
if (report.summary.failed || browserErrors.length || Object.values(unchanged).some(value => !value)) process.exitCode = 1;
