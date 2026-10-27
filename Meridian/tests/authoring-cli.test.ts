import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, writeFile, rm, access } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { newProject } from '../src/core/model.ts';

function cli(args: string[]) {
  return new Promise<{ code: number | null; output: string }>(resolve => {
    const child = spawn(process.execPath, ['--experimental-strip-types', 'scripts/author.mjs', ...args], { cwd: process.cwd(), windowsHide: true });
    let output='';child.stdout.on('data',data=>output+=data);child.stderr.on('data',data=>output+=data);
    child.on('close',code=>resolve({code,output}));
  });
}
async function cleanup(dir:string){
  const resolved=path.resolve(dir),temporary=path.resolve(tmpdir());
  assert.ok(resolved.startsWith(temporary+path.sep)&&path.basename(resolved).startsWith('meridian-author-'));
  await rm(resolved,{recursive:true,force:true});
}

test('author CLI composes and inspects local drafts without touching the source', async()=>{
  const dir=await mkdtemp(path.join(tmpdir(),'meridian-author-'));
  try{
    const base=newProject('Original'),input=path.join(dir,'base.json'),recipe=path.join(dir,'recipe.json'),out=path.join(dir,'draft.json');
    await writeFile(input,JSON.stringify(base));
    await writeFile(recipe,JSON.stringify({version:1,canvases:[{key:'current',existingId:base.entryCanvasId}],nodes:[{key:'hero',canvas:'current',kind:'text',title:'Hero',width:360,height:180,style:{fontFamily:'serif',titleSize:40}}]}));
    const result=await cli(['compose','--input',input,'--recipe',recipe,'--out',out]);
    assert.equal(result.code,0,result.output);
    assert.deepEqual(JSON.parse(await readFile(input,'utf8')),base);
    const draft=JSON.parse(await readFile(out,'utf8'));assert.equal(draft.nodes.at(-1).style.titleSize,40);
    const inspected=await cli(['inspect','--input',out]);assert.equal(inspected.code,0,inspected.output);
    assert.equal(JSON.parse(inspected.output.slice(inspected.output.indexOf('{'))).summary.styledNodeCount,1);
  }finally{await cleanup(dir);}
});

test('author CLI guards revisions and recovery, then checkpoints before an atomic save', async()=>{
  const dir=await mkdtemp(path.join(tmpdir(),'meridian-author-host-'));
  const project=newProject('Host project');project.revision=4;
  let recovery=false;const mutations:string[]=[];
  const server=createServer(async(req,res)=>{
    let raw='';for await(const chunk of req)raw+=chunk;
    res.setHeader('Content-Type','application/json');
    const body=raw?JSON.parse(raw):undefined;
    if(req.url==='/__meridian_status'){res.end(JSON.stringify({capabilities:['authoring-save-guard-v1']}));return;}
    if(req.method==='GET'&&req.url?.endsWith('/recovery')){res.statusCode=recovery?200:404;res.end(JSON.stringify(recovery?{project,baseRevision:4,savedAt:'now'}:{error:'none'}));return;}
    if(req.method==='GET'){res.end(JSON.stringify(project));return;}
    mutations.push(`${req.method} ${req.url}`);
    if(req.method==='POST'&&req.url?.endsWith('/checkpoints')){assert.equal(body.project.revision,4);res.end(JSON.stringify({id:'checkpoint'}));return;}
    if(req.method==='PUT'){assert.equal(body.expectedRevision,4);assert.equal(body.requireNoRecovery,true);res.end(JSON.stringify({...body.project,revision:5}));return;}
    res.statusCode=500;res.end('{}');
  });
  await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
  const port=(server.address() as {port:number}).port;
  try{
    const recipe=path.join(dir,'recipe.json'),out=path.join(dir,'draft.json');
    await writeFile(recipe,JSON.stringify({version:1,title:'Composed title'}));
    const args=['apply','--host',`http://127.0.0.1:${port}`,'--project',project.id,'--recipe',recipe,'--out',out];
    const invalid=await cli([...args,'--expected-revision','4','--dry-run','true']);assert.equal(invalid.code,1);assert.match(invalid.output,/Unknown option/);assert.deepEqual(mutations,[]);
    let result=await cli([...args,'--expected-revision','3']);assert.equal(result.code,1);assert.match(result.output,/Stale revision/);assert.deepEqual(mutations,[]);
    recovery=true;result=await cli([...args,'--expected-revision','4']);assert.equal(result.code,1);assert.match(result.output,/recovery draft/);assert.deepEqual(mutations,[]);
    assert.equal(JSON.parse(await readFile(out,'utf8')).title,'Composed title');
    recovery=false;result=await cli([...args,'--expected-revision','4']);assert.equal(result.code,0,result.output);
    assert.deepEqual(mutations,[`POST /api/projects/${project.id}/checkpoints`,`PUT /api/projects/${project.id}`]);
    assert.equal(JSON.parse(await readFile(out,'utf8')).revision,5);
  }finally{await new Promise<void>(resolve=>server.close(()=>resolve()));await cleanup(dir);}
});

test('author CLI exports inventory bindings, reuses them without duplicating entities, and rejects ambiguous binding sources', async()=>{
  const dir=await mkdtemp(path.join(tmpdir(),'meridian-author-bindings-'));
  try {
    const base=newProject('Inventory test'),input=path.join(dir,'base.json'),recipe=path.join(dir,'recipe.json'),draft=path.join(dir,'draft.json'),inventory=path.join(dir,'inventory.json');
    await writeFile(input,JSON.stringify(base));
    await writeFile(recipe,JSON.stringify({version:1,nodes:[{key:'a',canvas:base.entryCanvasId,kind:'media',title:'A preserved title',text:'Preserved source text'}]}));
    let result=await cli(['compose','--input',input,'--recipe',recipe,'--out',draft]);assert.equal(result.code,0,result.output);
    result=await cli(['inventory','--input',draft,'--out',inventory]);assert.equal(result.code,0,result.output);
    const report=JSON.parse(await readFile(inventory,'utf8')),bindings=JSON.parse(await readFile(`${inventory}.aliases.json`,'utf8'));
    assert.equal(report.nodes[0].text,'Preserved source text');assert.equal(bindings.nodes['node-1'],report.nodes[0].id);
    await writeFile(recipe,JSON.stringify({version:1,nodes:[{key:'node-1',unset:['style'],style:{shape:'none',portalColor:'#4477AA'}}]}));
    const revised=path.join(dir,'revised.json');
    result=await cli(['compose','--input',draft,'--recipe',recipe,'--aliases',`${inventory}.aliases.json`,'--out',revised]);assert.equal(result.code,0,result.output);
    const updated=JSON.parse(await readFile(revised,'utf8'));assert.equal(updated.nodes.length,2);assert.equal(updated.nodes[1].id,report.nodes[0].id);assert.equal(updated.nodes[1].text,report.nodes[0].text);
    await writeFile(recipe,JSON.stringify({version:1,bindings,nodes:[]}));
    result=await cli(['compose','--input',draft,'--recipe',recipe,'--aliases',`${inventory}.aliases.json`,'--out',path.join(dir,'rejected.json')]);assert.equal(result.code,1);assert.match(result.output,/Choose recipe.bindings or --aliases/);
    assert.deepEqual(JSON.parse(await readFile(input,'utf8')),base);
  } finally {await cleanup(dir);}
});

test('author CLI documents Work capture filters and rejects combined legacy and current aliases before writing a preview', async()=>{
  const dir=await mkdtemp(path.join(tmpdir(),'meridian-author-work-filter-'));
  try {
    const project=newProject('Authored canvas title'),input=path.join(dir,'source.json'),output=path.join(dir,'rejected-review');
    await writeFile(input,JSON.stringify(project));
    const help=await cli(['help']);assert.equal(help.code,0,help.output);assert.match(help.output,/\[--work ID-or-title\]/);
    const result=await cli(['capture','--input',input,'--out',output,'--work',project.entryCanvasId,'--canvas',project.entryCanvasId]);
    assert.equal(result.code,1);assert.match(result.output,/Supply only one Work filter/);await assert.rejects(access(output));
    assert.deepEqual(JSON.parse(await readFile(input,'utf8')),project);
  } finally {await cleanup(dir);}
});
