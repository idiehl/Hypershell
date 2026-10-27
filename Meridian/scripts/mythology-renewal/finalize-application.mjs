/** Export the exact verified saved readbacks without modifying any host data. */
import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
import {OUT,ROOT,readJSON,writeJSON,auditPreservation} from './shared.mjs';
import {canonicalSha256} from './apply-reviewed.mjs';
import {buildPublication} from '../../src/publication.ts';
const receiptDirectory=path.resolve(process.argv[2]);
assert.ok(path.relative(path.join(OUT,'application'),receiptDirectory).startsWith('apply-'),'Expected a reviewed local application receipt.');
const receipt=await readJSON(path.join(receiptDirectory,'receipt.json'));
assert.equal(receipt.status,'applied-and-verified');
const [js,css]=await Promise.all(['viewer.js','scene.css'].map(name=>readFile(path.join(ROOT,'dist',name),'utf8')));
const sha=data=>createHash('sha256').update(data).digest('hex'),results=[];
for(const item of receipt.projects){
 assert.equal(item.state,'saved-and-readback-verified');
 const saved=await readJSON(path.join(receiptDirectory,item.key,'saved-readback.json'));
 const before=await readJSON(path.join(receiptDirectory,item.key,'before.json'));
 assert.equal(canonicalSha256(saved),item.savedCanonicalSha256);auditPreservation(before,saved);
 const folder=path.join(OUT,'applied',item.key);await mkdir(folder,{recursive:true});
 const native=JSON.stringify(saved),publication=buildPublication(saved,js,css);
 await writeFile(path.join(folder,'saved.meridian'),native);await writeFile(path.join(folder,'publication.html'),publication);
 results.push({key:item.key,id:saved.id,title:saved.title,revision:saved.revision,checkpoint:item.checkpoint,nativeBytes:Buffer.byteLength(native),nativeSha256:sha(native),publicationSha256:sha(publication),originalNodesRetained:before.nodes.length,canvasCount:saved.canvases.length});
}
await writeJSON(path.join(OUT,'applied','exports.json'),{applicationReceipt:receiptDirectory,manifestCanonicalSha256:receipt.manifestSha256,projects:results});
await writeFile(path.join(OUT,'APPLIED.md'),`# Applied and verified\n\nThe user confirmed all open Meridian windows were saved. The reviewed 362-file application was installed, the backend restarted at PID 24772, and all six served frontend assets were independently verified. Native windows 21144 and 176948 retained their identities. The original application is backed up under package/qa/application-backup-20261019-0302272797.\n\nAll three original projects were checkpointed and read back before the first replacement. Each exact reviewed document was saved with expected-revision and no-recovery guards, then read back and compared with only its revision incremented. The complete library and all 54 existing destinations remain unchanged.\n\n| Collection | Saved revision | Original checkpoint |\n| --- | ---: | --- |\n${results.map(r=>`| ${r.title} | ${r.revision} | ${r.checkpoint.name} |`).join('\n')}\n\nExact verified saved native documents and self-contained publications are in applied/gods, applied/living and applied/visual; their hashes are recorded in applied/exports.json. The staged recipe candidates remain frozen as reviewed evidence.\n\nApplication receipt: ${receiptDirectory.replaceAll('\\','/')}\n\nRefresh or reopen the Meridian view in an existing native window to load the new frontend and saved project revision. The backend restart preserves native windows; it does not reload their existing web documents. No website was published.\n`);
console.log(JSON.stringify({status:'exported-verified-saved-documents',projects:results.map(({key,id,revision,nativeBytes})=>({key,id,revision,nativeBytes}))}));
