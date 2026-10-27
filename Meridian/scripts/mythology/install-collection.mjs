import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
const folder=new URL('../../examples/greek-mythology/',import.meta.url);
const p=JSON.parse(await readFile(new URL('greek-mythology.project.json',folder),'utf8'));
const host=process.env.MERIDIAN_URL??'http://127.0.0.1:5240';
const before=await (await fetch(host+'/api/projects')).json();
let saved;
if(before.some(x=>x.id===p.id)){
 saved=await (await fetch(`${host}/api/projects/${p.id}`)).json();
 console.log('Collection already installed. Preserving all current edits.');
}else{
 const response=await fetch(host+'/api/projects',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(p)});
 assert.equal(response.status,201,await response.clone().text());saved=await response.json();
 const checkpoint=await fetch(`${host}/api/projects/${p.id}/checkpoints`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:'Greek mythology · curated baseline',project:saved})});
 assert.equal(checkpoint.status,201,await checkpoint.clone().text());
}
const after=await (await fetch(host+'/api/projects')).json();
for(const existing of before)assert.deepEqual(after.find(p=>p.id===existing.id),existing);
const result={installedAt:new Date().toISOString(),projectId:p.id,title:saved.title,revision:saved.revision,url:`${host}/?project=${p.id}`,existingProjectsPreserved:before.length};
await writeFile(new URL('installation.json',folder),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
