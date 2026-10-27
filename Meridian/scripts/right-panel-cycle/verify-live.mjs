import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const report = { verifiedAt: new Date().toISOString(), origin: 'http://127.0.0.1:5240', assets: {} };
assert.equal((await (await fetch(`${report.origin}/__meridian_status`)).json()).application, 'atlas-meridian');
for (const name of ['editor.js', 'viewer.js', 'app.css', 'scene.css', 'index.html']) {
  const response = await fetch(`${report.origin}/${name}`, { cache: 'no-store' }); assert.ok(response.ok);
  const served = digest(Buffer.from(await response.arrayBuffer()));
  const built = digest(await readFile(`dist/${name}`)), installed = digest(await readFile(`output/app/wwwroot/${name}`));
  assert.equal(served, built, `${name}: live assets differ from verified build`); assert.equal(installed, built);
  report.assets[name] = served;
}
const installation = JSON.parse(await readFile('output/right-panel-qa/static-install.json', 'utf8'));
assert.equal(installation.savedContentUnchanged, true); assert.equal(installation.hostRestarted, false);
await writeFile('output/right-panel-qa/live-static-verification.json', JSON.stringify(report, null, 2));
await writeFile('output/right-panel-qa/completion.json', JSON.stringify({ cycle: 'standard', backlogItem: null, staticOnly: true, typecheck: 'passed', build: 'passed', coreAndHostChecks: { passed: 277 }, chromium: { uniqueScenariosPassed: 99, regressionRun: { passed: 97, requiredTargetedReruns: 2 }, finalTargetedRuns: { containmentAndSnapping: { passed: 8, requiredFreshLocatorRerun: 1 }, snappingSettings: { passed: 1 } } }, firefox: 'Unavailable: executable spawn UNKNOWN before tests', visualReview: 'Actual Visual atlas image node and parent canvas launchers on isolated copies; desktop and compact captures reviewed', installation, liveAssets: report.assets }, null, 2));
console.log('Live editor, viewer, styles and index match the verified build; static-only installation and saved-content protection recorded.');
