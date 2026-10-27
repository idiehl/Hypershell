import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { cp, mkdir, mkdtemp, readdir, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { createSmartLink, reflowSmartLinks } from '../src/core/smart-links.ts';
import type { MeridianProject } from '../src/core/types.ts';

test('host preserves Smart Links through save, recovery, original native package and snapshots while rejecting malformed settings', { timeout:120000 },async()=>{
 const root=fileURLToPath(new URL('../',import.meta.url)),temporary=await mkdtemp(path.join(tmpdir(),'meridian-smartlink-host-'));
 const source=path.join(temporary,'src','host'),brand=path.join(temporary,'assets','brand');await mkdir(source,{recursive:true});await mkdir(brand,{recursive:true});
 for(const name of await readdir(path.join(root,'src','host')))if(/\.(?:cs|csproj)$/.test(name))await cp(path.join(root,'src','host',name),path.join(source,name));
 for(const name of ['meridian.ico','meridian.png'])await cp(path.join(root,'assets','brand',name),path.join(brand,name));
 let child:ChildProcess|undefined;
 try{
  const built=spawnSync('dotnet',['build',path.join(source,'Meridian.Host.csproj'),'--nologo'],{cwd:temporary,windowsHide:true,encoding:'utf8',timeout:60000});assert.equal(built.status,0,built.stdout+built.stderr);
  const port=await new Promise<number>((resolve,reject)=>{const s=createServer();s.on('error',reject);s.listen(0,'127.0.0.1',()=>{const a=s.address();s.close(()=>resolve(typeof a==='object'&&a?a.port:0));});});const origin=`http://127.0.0.1:${port}`;
  child=spawn('dotnet',[path.join(source,'bin','Debug','net10.0','Meridian.Host.dll'),'--data-root',path.join(temporary,'data'),'--urls',origin],{cwd:temporary,windowsHide:true,stdio:['ignore','pipe','pipe']});let logs='';child.stdout?.on('data',d=>logs=(logs+d).slice(-4000));child.stderr?.on('data',d=>logs=(logs+d).slice(-4000));
  let ready=false;for(let i=0;i<200;i++){try{if((await fetch(origin+'/__meridian_status')).ok){ready=true;break;}}catch{}await new Promise(r=>setTimeout(r,50));}assert(ready,logs);
  const json=(route:string,body:unknown,method='POST')=>fetch(origin+route,{method,headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
  const p=newProject('Smart Link host'),rail=createSmartLink(p,p.entryCanvasId,{x:100,y:100},{direction:'down',gap:32});const a=addNode(p,p.entryCanvasId,'media','A'),b=addNode(p,p.entryCanvasId,'media','B');a.parentId=rail.id;b.parentId=rail.id;reflowSmartLinks(p);const candidate=commitSnapshots(p);
  const created=await json('/api/projects',candidate);assert.equal(created.status,201,await created.clone().text());const saved=await created.json() as MeridianProject;assert.equal(saved.revision,1);assert.deepEqual(saved.nodes.find(n=>n.id===rail.id)?.smartLink,rail.smartLink);assert.deepEqual(saved.snapshots[rail.id].node.smartLink,rail.smartLink);
  const recovery=await json(`/api/projects/${p.id}/recovery`,{project:saved,baseRevision:1,expectedSavedAt:null},'PUT');assert.equal(recovery.status,200,await recovery.clone().text());assert.deepEqual((await recovery.json()).project.nodes.find((n:{id:string})=>n.id===rail.id).smartLink,rail.smartLink);
  const exported=await json('/api/export',saved);assert.equal(exported.status,200,await exported.clone().text());const data=new FormData();data.append('file',new Blob([await exported.arrayBuffer()],{type:'application/octet-stream'}),'smart.meridian');const imported=await fetch(origin+'/api/import',{method:'POST',body:data});assert.equal(imported.status,200,await imported.clone().text());assert.deepEqual(await imported.json(),saved);
  for(const setting of[{direction:'down',gap:0},{direction:'diagonal',gap:48},{direction:'auto',gap:32,script:'bad'},{direction:'auto',gap:48,resolvedDirection:'auto'}]){const invalid=structuredClone(p);invalid.nodes.find(n=>n.id===rail.id)!.smartLink=setting as never;const response=await json('/api/projects',invalid);assert.equal(response.status,400,await response.text());}
  const wrongRole=structuredClone(p);wrongRole.nodes.find(n=>n.id===rail.id)!.kind='media';assert.equal((await json('/api/projects',wrongRole)).status,400);
  const badSnapshot=structuredClone(candidate);badSnapshot.snapshots[rail.id].node.smartLink={direction:'down',gap:48,html:'bad'} as never;assert.equal((await json('/api/projects',badSnapshot)).status,400);
  const invalidSignal=structuredClone(p);invalidSignal.links.push({id:'invalid-wire',canvasId:p.entryCanvasId,sourceId:a.id,targetId:rail.id,kind:'numeric',relation:'causal'});assert.equal((await json('/api/projects',invalidSignal)).status,400);
  const invalidStyle=structuredClone(p);invalidStyle.nodes.find(n=>n.id===a.id)!.style={textLayout:'unknown'} as never;assert.equal((await json('/api/projects',invalidStyle)).status,400);
 }finally{
  if(child&&child.exitCode===null){const stopped=once(child,'exit');child.kill();await stopped;}
  assert.equal(path.dirname(path.resolve(temporary)).toLowerCase(),path.resolve(tmpdir()).toLowerCase());assert(path.basename(temporary).startsWith('meridian-smartlink-host-'));await rm(temporary,{recursive:true,force:true});
 }
});
