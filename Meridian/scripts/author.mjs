#!/usr/bin/env node
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { newProject, forkProject, commitSnapshots } from '../src/core/model.ts';
import { applyRecipe, inspectProject, inventoryProject } from '../src/core/authoring.ts';
import { nodeTag } from '../src/core/node-tag.ts';
import { MIN_CAMERA_SCALE, MAX_CAMERA_SCALE } from '../src/core/camera.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { buildPublication } from '../src/publication.ts';

const root = fileURLToPath(new URL('../', import.meta.url));
const [command = 'help', ...argv] = process.argv.slice(2);
const args = new Map();
for (let at = 0; at < argv.length; at += 2) {
  if (!argv[at]?.startsWith('--') || !argv[at + 1] || argv[at + 1].startsWith('--')) throw new Error('Options use --name value. Run author.mjs help.');
  if (args.has(argv[at].slice(2))) throw new Error(`Repeated option ${argv[at]}`);
  args.set(argv[at].slice(2), argv[at + 1]);
}
const requireArg = key => { const value = args.get(key); if (!value) throw new Error(`Missing --${key}`); return value; };
const jsonFile = async file => JSON.parse(await readFile(path.resolve(file), 'utf8'));
const writeJson = async (file, value) => { await mkdir(path.dirname(path.resolve(file)), { recursive: true }); await writeFile(path.resolve(file), JSON.stringify(value, null, 2)); };
const host = new URL(args.get('host') ?? 'http://127.0.0.1:5240');
if (!['localhost', '127.0.0.1', '[::1]'].includes(host.hostname) || host.protocol !== 'http:' || host.username || host.password || host.pathname !== '/' || host.search || host.hash) throw new Error('--host must be a local Meridian HTTP origin.');
async function request(endpoint, method = 'GET', body) {
  const response = await fetch(new URL(endpoint, host), { method, ...(body === undefined ? {} : { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }) });
  if (!response.ok) { const error = await response.json().catch(() => ({})); throw new Error(`${response.status}: ${error.error ?? response.statusText}`); }
  return response.status === 204 ? undefined : response.json();
}
function valid(project) {
  if (!isProjectShape(project)) throw new Error('Invalid project data shape.');
  const issues = validateProject(project); if (issues.length) throw new Error(issues.map(i => i.message).join('\n'));
  return project;
}
async function inputProject() {
  if (args.has('input') && args.has('project')) throw new Error('Choose --input or --project, not both.');
  if (args.has('input')) return valid(await jsonFile(args.get('input')));
  if (args.has('project')) return valid(await request(`/api/projects/${encodeURIComponent(args.get('project'))}`));
  throw new Error('Supply --input project.json or --project project-id.');
}
async function inputRecipe() {
  const recipe = await jsonFile(requireArg('recipe'));
  if (!args.has('aliases')) return recipe;
  if (recipe && Object.hasOwn(recipe, 'bindings')) throw new Error('Choose recipe.bindings or --aliases, not both.');
  return { ...recipe, bindings: await jsonFile(args.get('aliases')) };
}
async function publication(project) {
  const [js, css] = await Promise.all(['viewer.js', 'scene.css'].map(file => readFile(path.join(root, 'dist', file), 'utf8')));
  return buildPublication(project, js, css);
}
async function noRecovery(id) {
  const response = await fetch(new URL(`/api/projects/${encodeURIComponent(id)}/recovery`, host));
  if (response.status === 404) return;
  if (!response.ok) throw new Error(`Cannot check recovery (${response.status}); no save attempted.`);
  throw new Error('This project has a recovery draft. Resolve it in the editor, or compose an independent copy. No save attempted.');
}
const help = `Meridian authoring tools (Node.js 24)

inspect --input project.json [--out report.json]
inspect --project ID [--host http://127.0.0.1:5240] [--out report.json]
inventory --input project.json [--out inventory.json]
inventory --project ID [--host ORIGIN] [--out inventory.json]
compose --recipe recipe.json --out draft.json [--input base.json] [--title Title] [--aliases aliases.json]
publish --input project.json --out publication.html
capture --input project.json --out review-directory [--work ID-or-title] [--node ID-or-Tag] [--scope node|branch] [--scale N]
install --input project.json [--host http://127.0.0.1:5240] [--out installed.json]
apply --project ID --expected-revision N --recipe recipe.json --out draft.json [--host ORIGIN] [--aliases aliases.json]

compose writes only a local draft; recipes never execute code.
capture renders the actual offline viewer, creates Work screenshots and reports
scrolling/clipping metrics. Review these images; a clean report is not a design review.
--node uses viewer navigation to reveal ancestors and the correct Gallery page.
--scope branch fits the selected subtree. --scale N sets a reading scale after centering.
inventory exports IDs, aliases, text, effective bounds, routes and media metadata;
when --out is supplied it also writes OUT.aliases.json for identity-preserving edits.
install always creates an independent project with fresh identities.
apply saves an existing project only at the stated revision, refuses recovery
drafts, writes the candidate to --out first, and creates a checkpoint before saving.
Use the editor's Composition tools to apply a recipe to an unsaved draft with undo.
See docs/AUTHORING.md for the recipe schema and visual authoring workflow.`;

try {
  const options = {
    help: [], inspect: ['input','project','host','out'], inventory: ['input','project','host','out'], compose: ['recipe','input','out','title','aliases'],
    publish: ['input','project','host','out'], capture: ['input','project','host','out','work','canvas','node','scope','scale'],
    install: ['input','project','host','out'], apply: ['project','expected-revision','recipe','out','host','aliases'],
  };
  if (Object.hasOwn(options, command)) for (const key of args.keys()) if (!options[command].includes(key)) throw new Error(`Unknown option --${key} for ${command}. No action taken.`);
  if (command === 'help') console.log(help);
  else if (command === 'inspect') {
    const report = inspectProject(await inputProject());
    if (args.has('out')) await writeJson(args.get('out'), report);
    console.log(JSON.stringify(report, null, 2));
  } else if (command === 'inventory') {
    const report = inventoryProject(await inputProject());
    if (args.has('out')) { await writeJson(args.get('out'), report); await writeJson(`${args.get('out')}.aliases.json`, report.aliases); }
    console.log(JSON.stringify(report, null, 2));
  } else if (command === 'compose') {
    const base = args.has('input') ? valid(await jsonFile(args.get('input'))) : newProject(args.get('title') ?? 'New composition');
    const result = applyRecipe(base, await inputRecipe());
    const committed = commitSnapshots(result.project), report = inspectProject(committed);
    await writeJson(requireArg('out'), committed);
    await writeJson(`${args.get('out')}.inspection.json`, report);
    await writeJson(`${args.get('out')}.aliases.json`, result.aliases);
    console.log(JSON.stringify({ file: path.resolve(args.get('out')), ...report.summary }));
  } else if (command === 'publish') {
    const project = await inputProject(); const file = path.resolve(requireArg('out'));
    await mkdir(path.dirname(file), { recursive: true }); await writeFile(file, await publication(project)); console.log(file);
  } else if (command === 'capture') {
    if (args.has('work') && args.has('canvas')) throw new Error('Supply only one Work filter.');
    const project = await inputProject();
    const nodeFilter = args.get('node'), scope = args.get('scope') ?? 'node';
    if (!['node', 'branch'].includes(scope) || args.has('scope') && !nodeFilter) throw new Error('--scope requires --node and must be node or branch.');
    const requestedScale = args.has('scale') ? Number(args.get('scale')) : undefined;
    if (requestedScale !== undefined && (!Number.isFinite(requestedScale) || requestedScale < MIN_CAMERA_SCALE || requestedScale > MAX_CAMERA_SCALE)) throw new Error(`--scale must be a finite number between ${MIN_CAMERA_SCALE} and ${MAX_CAMERA_SCALE}.`);
    const candidates = nodeFilter ? project.nodes.filter(node => node.id === nodeFilter || nodeTag(node) === nodeFilter) : [];
    if (nodeFilter && candidates.length !== 1) throw new Error(`--node must match exactly one ID or Tag; found ${candidates.length}. Use an exact ID from inventory.`);
    const focusNode = candidates[0];
    const directory = path.resolve(requireArg('out')); await mkdir(directory, { recursive: true });
    const html = path.join(directory, 'preview.html'); await writeFile(html, await publication(project));
    const filter = args.get('work') ?? args.get('canvas');
    const canvases = project.canvases.filter(c => (!filter || c.id === filter || c.title === filter) && (!focusNode || c.id === focusNode.canvasId));
    if (!canvases.length) throw new Error('No matching Work.');
    const { chromium } = await import('@playwright/test');
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage({ viewport: { width: 1600, height: 1100 }, deviceScaleFactor: 1 });
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    await page.route(/^https?:/, route => route.abort());
    const review = [];
    try {
      await page.goto(pathToFileURL(html).href);
      for (const [index, canvas] of canvases.entries()) {
        await page.getByLabel('Choose Work', { exact: true }).selectOption(canvas.id);
        if (focusNode) {
          await page.getByRole('button', { name: 'Explore project', exact: true }).click();
          await page.getByRole('searchbox', { name: 'Search project', exact: true }).fill(focusNode.id);
          // Navigate through the actual published outline: it reveals collapsed ancestors,
          // activates nested Gallery pages, and uses the renderer's own camera geometry.
          await page.locator('[data-navigation-node]').evaluateAll((items, id) => {
            const item = items.find(element => element.getAttribute('data-navigation-node') === id);
            if (!(item instanceof HTMLButtonElement)) throw new Error('Focused node is absent from the published outline.');
            item.click();
          }, focusNode.id);
          await page.getByRole('button', { name: 'Close project navigation', exact: true }).click();
          if (scope === 'branch') await page.getByRole('button', { name: 'Fit selected branch', exact: true }).click();
        } else await page.getByRole('button', { name: 'Fit Work', exact: true }).click();
        if (requestedScale !== undefined) {
          const current = await page.locator('.ms-world').evaluate(world => Number(getComputedStyle(world).getPropertyValue('--ms-camera-scale')));
          const viewport = page.locator('.ms-viewport'), box = await viewport.boundingBox();
          if (!box || !Number.isFinite(current) || current <= 0) throw new Error('Cannot measure the published camera.');
          await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
          // The shared renderer's native wheel input retains clipping/rebasing.
          await page.mouse.wheel(0, -Math.log(requestedScale / current) / 0.0013);
          await page.waitForFunction(({ expected }) => {
            const world = document.querySelector('.ms-world');
            const value = world && Number(getComputedStyle(world).getPropertyValue('--ms-camera-scale'));
            return value && Math.abs(value - expected) <= Math.max(0.00000001, expected * 0.0001);
          }, { expected: requestedScale });
        }
        await page.evaluate(async () => { await document.fonts.ready; await Promise.all(Array.from(document.images).map(image => image.decode().catch(() => {}))); });
        const file = `${String(index + 1).padStart(2, '0')}-${canvas.title.replace(/[^a-z0-9]+/gi, '-').slice(0, 70)}.png`;
        await page.screenshot({ path: path.join(directory, file) });
        const metrics = await page.locator('.ms-node,.ms-gallery-item').evaluateAll(cards => cards.map(card => {
          const gallery = card.classList.contains('ms-gallery-item');
          const body = gallery ? card : card.querySelector('.ms-node-body');
          const title = card.querySelector(gallery ? '.ms-gallery-item-title' : '.ms-node-title');
          const bounds = card.getBoundingClientRect();
          const style = getComputedStyle(card);
          const clippedContent = Array.from(card.querySelectorAll('.ms-text,.ms-caption,.ms-text-preview,.ms-node-body button')).flatMap(element => {
            const rect = element.getBoundingClientRect();
            let clipped = element.scrollHeight > element.clientHeight + 2 || element.scrollWidth > element.clientWidth + 2;
            let parent = element.parentElement;
            while (parent && card.contains(parent)) {
              const container = parent.getBoundingClientRect(), css = getComputedStyle(parent);
              if (/hidden|auto|scroll|clip/.test(css.overflowY) && (rect.top < container.top - 2 || rect.bottom > container.bottom + 2)) clipped = true;
              if (/hidden|auto|scroll|clip/.test(css.overflowX) && (rect.left < container.left - 2 || rect.right > container.right + 2)) clipped = true;
              parent = parent.parentElement;
            }
            return clipped ? [{ element: element.tagName.toLowerCase(), text: element.textContent?.slice(0, 100), scrollHeight: element.scrollHeight, clientHeight: element.clientHeight }] : [];
          });
          return { id: card.getAttribute(gallery ? 'data-gallery-item-id' : 'data-node-id'), ...(gallery ? { galleryPage: true } : {}), title: title?.textContent, screen: { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height }, font: style.fontFamily,
            titleClipped: !!title && (title.scrollWidth > title.clientWidth + 2 || title.scrollHeight > title.clientHeight + 2),
            bodyRequiresScrolling: !!body && (body.scrollHeight > body.clientHeight + 2 || body.scrollWidth > body.clientWidth + 2),
            clippedContent,
            images: Array.from(card.querySelectorAll('img')).map(image => ({ loaded: image.complete && image.naturalWidth > 0, naturalWidth: image.naturalWidth, naturalHeight: image.naturalHeight })) };
        }));
        review.push({ id: canvas.id, title: canvas.title, screenshot: file, zoom: await page.locator('.ms-zoom').textContent(), ...(focusNode ? { focusNodeId: focusNode.id, scope } : {}), ...(requestedScale !== undefined ? { requestedScale } : {}), nodes: metrics });
      }
    } finally { await browser.close(); }
    const report = { inspection: inspectProject(project), browserErrors: errors, canvases: review };
    await writeJson(path.join(directory, 'review.json'), report);
    console.log(JSON.stringify({ directory, canvases: review.length, browserErrors: errors, scrollingNodes: review.flatMap(c => c.nodes).filter(n => n.bodyRequiresScrolling).length, clippedNodes: review.flatMap(c => c.nodes).filter(n => n.titleClipped || n.clippedContent.length).length }));
    if (errors.length) process.exitCode = 1;
  } else if (command === 'install') {
    const copy = commitSnapshots(forkProject(await inputProject()));
    const installed = await request('/api/projects', 'POST', copy);
    if (args.has('out')) await writeJson(args.get('out'), installed);
    console.log(JSON.stringify({ id: installed.id, revision: installed.revision, title: installed.title, url: new URL(`/?project=${installed.id}`, host).href }));
  } else if (command === 'apply') {
    const id = requireArg('project'), expected = Number(requireArg('expected-revision'));
    const output = requireArg('out');
    if (!Number.isInteger(expected) || expected < 0) throw new Error('--expected-revision must be a nonnegative integer.');
    const status = await request('/__meridian_status');
    if (!status.capabilities?.includes('authoring-save-guard-v1')) throw new Error('This host does not support guarded authoring saves. Restart the updated Meridian host first.');
    const base = valid(await request(`/api/projects/${encodeURIComponent(id)}`));
    if (base.revision !== expected) throw new Error(`Stale revision: expected ${expected}, current ${base.revision}. No save attempted.`);
    const result = applyRecipe(base, await inputRecipe());
    const candidate = commitSnapshots(result.project); await writeJson(output, candidate);
    await writeJson(`${output}.aliases.json`, result.aliases);
    await writeJson(`${output}.inspection.json`, inspectProject(candidate));
    await noRecovery(id);
    const checkpoint = await request(`/api/projects/${encodeURIComponent(id)}/checkpoints`, 'POST', { project: base, name: `Before composition · r${expected}` });
    await noRecovery(id);
    const saved = await request(`/api/projects/${encodeURIComponent(id)}`, 'PUT', { project: candidate, expectedRevision: expected, requireNoRecovery: true });
    await writeJson(output, saved);
    console.log(JSON.stringify({ id, revision: saved.revision, checkpointId: checkpoint.id, draftFile: path.resolve(output), ...result.report.summary }));
  } else throw new Error(`Unknown command ${command}. Run author.mjs help.`);
} catch (error) { console.error(error instanceof Error ? error.message : String(error)); process.exitCode = 1; }
