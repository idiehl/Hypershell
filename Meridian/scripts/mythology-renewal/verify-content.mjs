#!/usr/bin/env node
/** Offline preservation/evaluation/publication proof. Writes only the requested report. */
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { isDeepStrictEqual } from 'node:util';
import { auditPreservation, OUT, ROOT } from './shared.mjs';
import { evaluate } from '../../src/core/engine.ts';
import { isProjectShape, validateProject } from '../../src/core/rules.ts';
import { nodeAssetIds } from '../../src/core/appearance.ts';
import { createPublicationPayload } from '../../src/publication.ts';

const candidates = []; let reportPath, baselineRoot = path.join(OUT, 'baseline'), mode = 'diagnostic';
const argv = process.argv.slice(2);
for (let index = 0; index < argv.length; index += 2) {
  const option = argv[index], value = argv[index + 1];
  if (!value || value.startsWith('--')) throw new Error('Use --candidate draft.json (repeatable), --out report.json, and optional --baseline-root directory.');
  if (option === '--candidate') candidates.push(path.resolve(value));
  else if (option === '--out' && !reportPath) reportPath = path.resolve(value);
  else if (option === '--baseline-root') baselineRoot = path.resolve(value);
  else if (option === '--mode' && ['diagnostic', 'final'].includes(value)) mode = value;
  else throw new Error(`Unknown or repeated option ${option}.`);
}
if (!candidates.length || !reportPath) throw new Error('Supply at least one --candidate path and one --out report.json.');
if (new Set(candidates).size !== candidates.length) throw new Error('Candidate paths must be distinct.');
if (path.extname(reportPath).toLowerCase() !== '.json') throw new Error('--out must name a JSON report, not a project or publication.');
if (!reportPath.startsWith(path.resolve(OUT) + path.sep)) throw new Error('--out must remain within this renewal cycle output directory.');
const previousReport = await readFile(reportPath).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
if (previousReport) {
  let old; try { old = JSON.parse(previousReport.toString('utf8').replace(/^\uFEFF/, '')); } catch { /* Refuse unrelated files, including malformed project inputs. */ }
  if (old?.format !== 'meridian-content-preservation-proof') throw new Error('Refusing to overwrite a file that is not an earlier content verification report.');
}
const hash = value => createHash('sha256').update(value).digest('hex');
const parse = text => JSON.parse(text.replace(/^\uFEFF/, ''));
const proof = new Map(); let outputConflictsInput = false;
async function readInput(file) {
  const absolute = path.resolve(file);
  if (absolute.toLowerCase() === reportPath.toLowerCase()) { outputConflictsInput = true; throw new Error('The report must not overwrite an input.'); }
  const bytes = await readFile(absolute), sha256 = hash(bytes);
  const prior = proof.get(absolute);
  if (prior && prior.sha256 !== sha256) throw new Error(`Input changed between reads: ${absolute}`);
  proof.set(absolute, { path: absolute, bytes: bytes.length, sha256 }); return bytes;
}
const readJSON = async file => parse((await readInput(file)).toString('utf8'));
const errorText = error => (error instanceof Error ? error.message : String(error)).slice(0, 2000);
function checkedProject(project) {
  if (!isProjectShape(project)) throw new Error('Unsupported or malformed project shape.');
  const issues = validateProject(project);
  if (issues.length) throw new Error(`Invalid graph: ${issues.slice(0, 8).map(issue => issue.message).join(' ')}`);
  return project;
}
const state = (record, id) => Object.hasOwn(record, id) ? { present: true, value: record[id] } : { present: false };
function compareEvaluation(base, project, baselineEvaluation) {
  const actual = evaluate(project), differences = [];
  for (const original of base.nodes) {
    if (!isDeepStrictEqual(state(baselineEvaluation.values, original.id), state(actual.values, original.id)) ||
      !isDeepStrictEqual(state(baselineEvaluation.errors, original.id), state(actual.errors, original.id))) differences.push({
      nodeId: original.id, title: original.title, kind: original.kind,
      baselineValue: state(baselineEvaluation.values, original.id), actualValue: state(actual.values, original.id),
      baselineError: state(baselineEvaluation.errors, original.id), actualError: state(actual.errors, original.id),
    });
  }
  if (!isDeepStrictEqual(state(baselineEvaluation.errors, 'project'), state(actual.errors, 'project'))) differences.push({ scope: 'project', baselineError: state(baselineEvaluation.errors, 'project'), actualError: state(actual.errors, 'project') });
  return { passed: differences.length === 0, originalNodesCompared: base.nodes.length, differenceCount: differences.length, differences: differences.slice(0, 100),
    originalNonNullValues: base.nodes.filter(node => actual.values[node.id] !== null && Object.hasOwn(actual.values, node.id)).length,
    originalNullWithoutError: base.nodes.filter(node => actual.values[node.id] === null && !Object.hasOwn(actual.errors, node.id)).length,
    originalErrors: base.nodes.filter(node => Object.hasOwn(actual.errors, node.id)).map(node => ({ nodeId: node.id, title: node.title, message: actual.errors[node.id] })),
    totalEvaluationErrors: Object.keys(actual.errors).length,
  };
}
function resourceWiring(project) {
  const assets = new Set(project.assets.map(asset => asset.id)), missing = [];
  for (const node of project.nodes) for (const id of nodeAssetIds(node)) if (!assets.has(id)) missing.push({ nodeId: node.id, assetId: id });
  for (const canvas of project.canvases) if (canvas.style?.backgroundAssetId && !assets.has(canvas.style.backgroundAssetId)) missing.push({ canvasId: canvas.id, assetId: canvas.style.backgroundAssetId });
  for (const [originalId, snapshot] of Object.entries(project.snapshots)) for (const id of nodeAssetIds(snapshot.node))
    if (!assets.has(id) && snapshot.asset?.id !== id && snapshot.backgroundAsset?.id !== id) missing.push({ snapshotId: originalId, assetId: id });
  return { passed: missing.length === 0, missingCount: missing.length, missing: missing.slice(0, 100),
    primaryImageConsumers: project.nodes.filter(node => node.assetId && project.assets.some(asset => asset.id === node.assetId && asset.mime.startsWith('image/'))).length,
    decorativeBackgrounds: project.nodes.filter(node => node.style?.backgroundAssetId).length + project.canvases.filter(canvas => canvas.style?.backgroundAssetId).length,
    unframedNodes: project.nodes.filter(node => node.style?.shape === 'none' || node.style?.variant === 'label').length,
    hoverBackgrounds: project.nodes.filter(node => node.style?.backgroundDisplay === 'hover').length,
    displayPatterns: Object.fromEntries([...new Set(project.nodes.map(node => node.containerVariant).filter(Boolean))].map(pattern => [pattern, project.nodes.filter(node => node.containerVariant === pattern).length])),
  };
}
function readPayload(html) {
  const found = [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script\s*>/gi)].filter(match => /\bid\s*=\s*(["'])meridian-data\1/i.test(match[1]) && /\btype\s*=\s*(["'])application\/json\1/i.test(match[1]));
  if (found.length !== 1) throw new Error(`Expected exactly one inert publication payload; found ${found.length}.`);
  const payload = parse(found[0][2]);
  if (payload.format !== 'atlas-meridian-publication' || payload.version !== 1) throw new Error('Unsupported publication payload.');
  return payload;
}
let manifest; const verifiedMuseum = new Map();
async function museumSource(item) {
  if (verifiedMuseum.has(item.objectId)) return verifiedMuseum.get(item.objectId);
  const file = path.resolve(OUT, 'media', item.file), metadataFile = path.resolve(OUT, 'media', item.metadataFile);
  for (const selected of [file, metadataFile]) if (!selected.startsWith(path.join(OUT, 'media') + path.sep)) throw new Error('Museum manifest path escaped the media directory.');
  const image = await readInput(file), metadata = await readJSON(metadataFile);
  const result = { passed: hash(image) === item.sha256 && image.length === item.bytes && item.isPublicDomain === true && metadata.isPublicDomain === true && metadata.objectID === item.objectId && metadata.accessionNumber === item.accession && [metadata.primaryImage, metadata.primaryImageSmall].includes(item.imageUrl),
    objectId: item.objectId, accession: item.accession, sha256: hash(image), bytes: image.length,
    recordedPublicDomain: item.isPublicDomain === true && metadata.isPublicDomain === true, recordedImageMatchesMetadata: [metadata.primaryImage, metadata.primaryImageSmall].includes(item.imageUrl), sourceUrl: item.sourceUrl };
  verifiedMuseum.set(item.objectId, result); return result;
}
async function provenance(base, project) {
  manifest ??= await readJSON(path.join(OUT, 'media', 'manifest.json'));
  const old = new Set(base.assets.map(asset => asset.id)), textLocations = [];
  for (const node of project.nodes) textLocations.push({ nodeId: node.id, canvasId: node.canvasId, text: `${node.title}\n${node.text ?? ''}\n${(node.hyperlinks ?? []).map(link => `${link.label ?? ''} ${link.url}`).join('\n')}` });
  for (const asset of project.assets) if (/^text\/(plain|markdown|csv|tab-separated-values)(;|$)/i.test(asset.mime)) textLocations.push({ assetId: asset.id, text: Buffer.from(asset.base64, 'base64').toString('utf8') });
  const images = [];
  for (const asset of project.assets.filter(asset => !old.has(asset.id) && asset.mime.startsWith('image/'))) {
    const bytes = Buffer.from(asset.base64, 'base64'), sha256 = hash(bytes), item = manifest.items.find(item => item.sha256 === sha256 && item.mime === asset.mime);
    if (!item) { images.push({ passed: false, assetId: asset.id, name: asset.name, issue: 'Added image has no matching retained museum provenance record.', sha256 }); continue; }
    const source = await museumSource(item);
    const checks = Object.fromEntries(['caption', 'title', 'accession', 'credit', 'rights', 'sourceUrl', 'culture', 'date', 'artist', 'artistPrefix'].filter(field => item[field]).map(field => [field, textLocations.some(location => location.text.includes(item[field]))]));
    const consumers = project.nodes.filter(node => nodeAssetIds(node).includes(asset.id)).map(node => ({ nodeId: node.id, canvasId: node.canvasId, primary: node.assetId === asset.id, background: node.style?.backgroundAssetId === asset.id }));
    const canvasBackgrounds = project.canvases.filter(canvas => canvas.style?.backgroundAssetId === asset.id).map(canvas => canvas.id);
    const fullCreditLocations = textLocations.filter(location => location.text.includes(item.sourceUrl) && location.text.includes(item.accession) && location.text.includes(item.credit)).map(({ text, ...location }) => location);
    images.push({ passed: source.passed && Object.values(checks).every(Boolean) && fullCreditLocations.length > 0 && consumers.length + canvasBackgrounds.length > 0, assetId: asset.id, name: asset.name,
      objectId: item.objectId, accession: item.accession, source, preservedProvenanceFields: checks, fullCreditLocations, consumers, canvasBackgrounds });
  }
  return { passed: images.every(image => image.passed), addedImageAssets: images.length, recognizedMuseumObjects: [...new Set(images.map(image => image.objectId).filter(Boolean))], images };
}
const report = { format: 'meridian-content-preservation-proof', version: 1, startedAt: new Date().toISOString(), mode: `offline-${mode}`, readOnlyInputs: true, projects: [], inputs: [] };
const builtJs = (await readInput(path.join(ROOT, 'dist/viewer.js'))).toString('utf8'), builtCss = (await readInput(path.join(ROOT, 'dist/scene.css'))).toString('utf8');
for (const candidatePath of candidates) {
  const entry = { candidatePath, passed: false, checks: {} }; report.projects.push(entry);
  try {
    const candidate = checkedProject(await readJSON(candidatePath)); entry.projectId = candidate.id; entry.title = candidate.title;
    const base = checkedProject(await readJSON(path.join(baselineRoot, `${candidate.id}.json`))), baselineEvaluation = evaluate(base);
    entry.checks.preservation = auditPreservation(base, candidate);
    entry.checks.evaluation = compareEvaluation(base, candidate, baselineEvaluation);
    entry.checks.resources = resourceWiring(candidate);
    entry.checks.provenance = await provenance(base, candidate);
    const nativePath = path.join(path.dirname(candidatePath), 'renewed.meridian'), native = checkedProject(await readJSON(nativePath));
    entry.checks.native = { passed: isDeepStrictEqual(native, candidate), path: nativePath, sameProjectAndAllFields: isDeepStrictEqual(native, candidate), resources: resourceWiring(native), evaluation: compareEvaluation(base, native, baselineEvaluation) };
    const publicationPath = path.join(path.dirname(candidatePath), 'publication.html'), html = (await readInput(publicationPath)).toString('utf8'), payload = readPayload(html), portable = checkedProject(payload.project), expected = createPublicationPayload(candidate);
    // JSON intentionally drops in-memory null prototypes used by snapshot maps.
    // Compare the persisted data model, not runtime object prototypes.
    const expectedProject = parse(JSON.stringify(expected.project));
    const runtime = [...html.matchAll(/<script>([\s\S]*?)<\/script\s*>/gi)], styles = [...html.matchAll(/<style>([\s\S]*?)<\/style\s*>/gi)];
    const runtimeMatchesCurrentBuild = runtime.length === 1 && runtime[0][1] === builtJs.replace(/<\/script/gi, '<\\/script');
    const stylesMatchCurrentBuild = styles.length === 1 && styles[0][1] === builtCss.replace(/<\/style/gi, '<\\/style');
    const expectedMissing = expected.missing, oldMissing = new Set(createPublicationPayload(base).missing), newMissing = payload.missing.filter(item => !oldMissing.has(item));
    entry.checks.publication = { passed: isDeepStrictEqual(portable, expectedProject) && isDeepStrictEqual(payload.missing, expectedMissing) && runtimeMatchesCurrentBuild && stylesMatchCurrentBuild && newMissing.length === 0,
      path: publicationPath, sameAllowlistedProjectAndStyles: isDeepStrictEqual(portable, expectedProject), runtimeMatchesCurrentBuild, stylesMatchCurrentBuild,
      missing: payload.missing, newMissingVersusBaseline: newMissing, resources: resourceWiring(portable), evaluation: compareEvaluation(base, portable, baselineEvaluation) };
    entry.passed = Object.values(entry.checks).every(check => check.passed) && entry.checks.native.resources.passed && entry.checks.native.evaluation.passed && entry.checks.publication.resources.passed && entry.checks.publication.evaluation.passed;
  } catch (error) { entry.error = errorText(error); }
}
for (const item of proof.values()) {
  const after = await readFile(item.path).catch(() => null);
  report.inputs.push({ ...item, unchangedDuringVerification: after !== null && hash(after) === item.sha256 });
}
report.finishedAt = new Date().toISOString(); report.inputsStable = report.inputs.every(input => input.unchangedDuringVerification); report.passed = report.inputsStable && report.projects.every(project => project.passed);
if (outputConflictsInput) throw new Error('Report creation refused because its destination is an input.');
const reportNow = await readFile(reportPath).catch(error => { if (error.code === 'ENOENT') return null; throw error; });
if (previousReport ? !reportNow || hash(reportNow) !== hash(previousReport) : reportNow !== null) throw new Error('The report destination changed during verification; no file was replaced.');
await mkdir(path.dirname(reportPath), { recursive: true }); await writeFile(reportPath, JSON.stringify(report, null, 2) + '\n', { flag: previousReport ? 'w' : 'wx' });
console.log(JSON.stringify({ report: reportPath, passed: report.passed, inputsStable: report.inputsStable, projects: report.projects.map(project => ({ id: project.projectId, title: project.title, passed: project.passed, error: project.error, checks: Object.fromEntries(Object.entries(project.checks).map(([name, check]) => [name, check.passed])) })) }, null, 2));
if (!report.passed) process.exitCode = 1;
