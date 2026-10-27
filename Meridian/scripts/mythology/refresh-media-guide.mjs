// Correct the authored media guide in the existing collection, preserving every identity.
import assert from 'node:assert/strict';
import {readFile,writeFile,copyFile} from 'node:fs/promises';
import {commitSnapshots} from '../../src/core/model.ts';
import {buildPublication} from '../../src/publication.ts';
const root=new URL('../../',import.meta.url),folder=new URL('examples/greek-mythology/',root);
const projectPath=new URL('greek-mythology.project.json',folder);
let p=JSON.parse(await readFile(projectPath,'utf8'));
const original=structuredClone(p);
const manifest=JSON.parse(await readFile(new URL('collection-manifest.json',folder),'utf8'));
const oldText='TXT previews show only the first 3,000 characters. Use Download for the complete source register or collection reader.';
const newText='TXT cards and published viewer previews show the first 3,000 characters. Open TXT in the editor for full text, or Download the complete file.';
const n=p.nodes.find(n=>n.id===manifest.nodeKeys['source-method']);assert.ok(n.text.includes(oldText)||n.text.includes(newText));n.text=n.text.replace(oldText,newText);
const readerPath=new URL('content/greek-mythology/media/collection-reader.txt',root);
const reader=(await readFile(readerPath,'utf8')).replace(oldText,newText);await writeFile(readerPath,reader);
for(const filename of ['collection-reader.txt','mythology-companion.pdf']){
 const source=new URL(`content/greek-mythology/media/${filename}`,root);const bytes=await readFile(source);const asset=p.assets.find(a=>a.name===filename);asset.base64=bytes.toString('base64');manifest.assets.find(a=>a.id===asset.id).bytes=bytes.length;await copyFile(source,new URL(filename,folder));
}
p=commitSnapshots(p);manifest.wordCount=reader.split(/\s+/).length;
await writeFile(projectPath,JSON.stringify(p));
const js=await readFile(new URL('dist/viewer.js',root),'utf8'),css=await readFile(new URL('dist/scene.css',root),'utf8');
await writeFile(new URL('greek-mythology.html',folder),buildPublication(p,js,css,{includeExternalReferences:true}));
await writeFile(new URL('collection-manifest.json',folder),JSON.stringify(manifest,null,2));
if(process.argv.includes('--save')){
 const host=process.env.MERIDIAN_URL??'http://127.0.0.1:5240';
 const current=await (await fetch(`${host}/api/projects/${p.id}`)).json();
 assert.deepEqual({...current,revision:0},{...original,revision:0},'Preserve any independent editor changes');
 assert.equal((await fetch(`${host}/api/projects/${p.id}/recovery`)).status,404,'Preserve any active recovery draft');
 const response=await fetch(`${host}/api/projects/${p.id}`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({project:{...p,revision:current.revision},expectedRevision:current.revision})});
 assert.ok(response.ok,await response.clone().text());const saved=await response.json();
 const checkpoint=await fetch(`${host}/api/projects/${p.id}/checkpoints`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:'Greek mythology · verified media guide',project:saved})});assert.equal(checkpoint.status,201);
 const installationPath=new URL('installation.json',folder),installation=JSON.parse(await readFile(installationPath,'utf8'));installation.revision=saved.revision;installation.guideRefreshedAt=new Date().toISOString();await writeFile(installationPath,JSON.stringify(installation,null,2));
}
console.log(JSON.stringify({projectId:p.id,nodes:p.nodes.length,refreshed:['source-method','collection-reader.txt','mythology-companion.pdf']},null,2));
