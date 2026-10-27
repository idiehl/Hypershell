import assert from 'node:assert/strict';
import { readFile, writeFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { buildPublication, readPublication } from '../../src/publication.ts';
import { validateProject } from '../../src/core/rules.ts';
import { navigationIndex } from '../../src/core/navigation.ts';

const folder='output/branch-atlas', output='output/branch-qa', data=path.join(process.env.LOCALAPPDATA,'AtlasMeridian');
const json=async file=>JSON.parse(await readFile(file,'utf8'));
const candidate=await json(`${folder}/atlas.json`),baseline=await json(`${folder}/baseline.json`);
const project=(await json(path.join(data,'projects',candidate.id,'project.json'))).project;
assert.deepEqual(project,{...candidate,revision:4});assert.deepEqual(validateProject(project),[]);
const library=await json(path.join(data,'library.json')), expectedLibrary=await json(`${folder}/library.json`);
assert.deepEqual(library,{...expectedLibrary,revision:11});
assert.equal(library.entries.filter(e=>e.projectId===project.id).length,1);
const checkpoints=[];
for(const name of await readdir(path.join(data,'projects',project.id,'checkpoints'))){const checkpoint=await json(path.join(data,'projects',project.id,'checkpoints',name));if(checkpoint.metadata.name==='Before single-canvas atlas · saved')checkpoints.push(checkpoint);}
assert.equal(checkpoints.length,1);assert.deepEqual(checkpoints[0].project,baseline);
const sha=bytes=>createHash('sha256').update(bytes).digest('hex').toUpperCase();
let protectedCount=0;
for(const file of await json('.cache/branch-cycle/protected-files.json')){
  if(/\\library\.json(?:\.bak)?$/.test(file.Path)||file.Path.endsWith(`\\${project.id}\\project.json`))continue;
  assert.equal(sha(await readFile(file.Path)),file.Hash,`Unrelated saved content changed: ${file.Path}`);protectedCount++;
}
for(const name of ['Meridian.Host.exe','Meridian.Host.dll','wwwroot/editor.js','wwwroot/viewer.js','wwwroot/scene.css','wwwroot/app.css'])assert.equal(sha(await readFile(`output/app/${name}`)),sha(await readFile(`output/branch-20261005/app/${name}`)),`Installed package differs: ${name}`);
const html=buildPublication(project,await readFile('output/app/wwwroot/viewer.js','utf8'),await readFile('output/app/wwwroot/scene.css','utf8'));
await writeFile(`${output}/greek-mythology.html`,html);
const reopened=readPublication(html);assert.equal(reopened.nodes.length,project.nodes.length);assert.equal(reopened.canvases.length,1);
const nav=navigationIndex(project);assert.equal(project.nodes.filter(n=>n.kind!=='canvas'&&nav.isNodeVisible(n.id)).length,10);
const report={verifiedAt:new Date().toISOString(),projectId:project.id,title:project.title,revision:project.revision,canvases:project.canvases.length,nodes:project.nodes.length,connections:project.links.length,assets:project.assets.length,libraryRevision:library.revision,libraryEntries:library.entries.length,atlasLibraryEntries:1,checkpointId:checkpoints[0].metadata.id,checkpointMatchesOriginal:true,unchangedProtectedFiles:protectedCount,installedPackageMatches:true,portableViewerMatches:true};
await writeFile(`${output}/installed-verification.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report));
