import assert from 'node:assert/strict';
import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { addNode, newProject } from '../src/core/model.ts';
import type { MeridianProject } from '../src/core/types.ts';

const root = fileURLToPath(new URL('../', import.meta.url));

test('project readers share the writer transaction and never block atomic replacement with Windows read handles', { timeout: 120_000 }, async t => {
  const temporary = await mkdtemp(path.join(tmpdir(), 'meridian-project-storage-tests-'));
  const buildRoot = path.join(temporary, 'build'), dataRoot = path.join(temporary, 'http-data');
  let host: ChildProcess | undefined, logs = '', base = '';
  const json = (method: string, route: string, body?: unknown) => fetch(base + route, { method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  try {
    const intermediate = path.join(temporary, 'obj') + path.sep;
    const built = spawnSync('dotnet', ['build', path.join(root, 'src/host/Meridian.Host.csproj'), '--nologo', '--output', buildRoot,
      `-p:MSBuildProjectExtensionsPath=${intermediate}`, `-p:IntermediateOutputPath=${intermediate}`], { cwd: root, encoding: 'utf8', timeout: 60_000, windowsHide: true });
    assert.equal(built.status, 0, `${built.stdout}\n${built.stderr}`);
    await t.test('all public project reads wait for an owned writer gate before opening storage', async () => {
      const probeRoot = path.join(temporary, 'probe'), fixturePath = path.join(temporary, 'fixture.json');
      await writeFile(fixturePath, JSON.stringify(fixture()));
      await mkdir(probeRoot);
      await writeFile(path.join(probeRoot, 'StorageProbe.csproj'), `<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><Reference Include="Meridian.Host"><HintPath>${path.join(buildRoot, 'Meridian.Host.dll')}</HintPath></Reference></ItemGroup></Project>`);
      await writeFile(path.join(probeRoot, 'Program.cs'), gateProbe);
      const probe = spawnSync('dotnet', ['run', '--project', path.join(probeRoot, 'StorageProbe.csproj'), '--', path.join(temporary, 'probe-data'), fixturePath],
        { cwd: root, encoding: 'utf8', timeout: 60_000, windowsHide: true });
      assert.equal(probe.status, 0, `${probe.stdout}\n${probe.stderr}`);
      assert.match(probe.stdout, /PROJECT_READ_GATE_OK:5/);
    });
    base = `http://127.0.0.1:${await freePort()}`;
    host = spawn('dotnet', [path.join(buildRoot, 'Meridian.Host.dll'), '--data-root', dataRoot, '--urls', base], { cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    host.stdout!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    host.stderr!.on('data', chunk => { logs = (logs + chunk).slice(-12_000); });
    let ready = false;
    for (let attempt = 0; attempt < 200; attempt++) {
      if (host.exitCode !== null) assert.fail(`Fixture host exited: ${logs}`);
      try { if ((await fetch(base + '/__meridian_status')).ok) { ready = true; break; } } catch { /* Isolated host is starting. */ }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(ready, `Fixture host did not start: ${logs}`);
    let project: MeridianProject;
    await t.test('overlapping GET and catalog requests observe one complete saved revision while autosave replaces its file', async () => {
      const created = await json('POST', '/api/projects', fixture());
      assert.equal(created.status, 201, await created.clone().text()); project = await created.json();
      for (let round = 0; round < 20; round++) {
        const previous = structuredClone(project), candidate = structuredClone(project); candidate.title = `Saved transaction ${round}`;
        candidate.nodes.find(node => node.kind === 'media')!.text = `${round}:` + 'Complete body; '.repeat(20_000);
        const [written, ...reads] = await Promise.all([
          json('PUT', `/api/projects/${project.id}`, { project: candidate, expectedRevision: project.revision }),
          ...Array.from({ length: 5 }, () => json('GET', `/api/projects/${project.id}`)), json('GET', '/api/projects'),
        ]);
        assert.equal(written.status, 200, `Save ${round}: ${await written.clone().text()}`); project = await written.json();
        for (const response of reads.slice(0, 5)) {
          assert.equal(response.status, 200, `Read during save ${round}: ${await response.clone().text()}`);
          const observed: MeridianProject = await response.json();
          assert.ok(observed.revision === previous.revision || observed.revision === project.revision);
          assert.deepEqual(observed, observed.revision === previous.revision ? previous : project);
        }
        const catalog = reads[5]; assert.equal(catalog.status, 200, await catalog.clone().text());
        const summary = (await catalog.json())[0];
        assert.ok(summary.revision === previous.revision || summary.revision === project.revision);
        assert.equal(summary.title, summary.revision === previous.revision ? previous.title : project.title);
      }
    });
    await t.test('recovery reads remain complete through replacement and return 404 rather than storage errors when a draft is consumed', async () => {
      const draft = structuredClone(project!); draft.title = 'Independent recovery';
      let response = await json('PUT', `/api/projects/${project!.id}/recovery`, { project: draft, baseRevision: project!.revision });
      assert.equal(response.status, 200, await response.clone().text());
      let recovery = await response.json();
      for (let round = 0; round < 20; round++) {
        const previous = structuredClone(recovery), candidate = structuredClone(draft); candidate.title = `Recovery transaction ${round}`;
        const [written, ...reads] = await Promise.all([
          json('PUT', `/api/projects/${project!.id}/recovery`, { project: candidate, baseRevision: project!.revision, expectedSavedAt: recovery.savedAt }),
          ...Array.from({ length: 5 }, () => json('GET', `/api/projects/${project!.id}/recovery`)),
        ]);
        assert.equal(written.status, 200, `Recovery ${round}: ${await written.clone().text()}`); recovery = await written.json();
        for (const read of reads) {
          assert.equal(read.status, 200, `Recovery read ${round}: ${await read.clone().text()}`);
          const observed = await read.json(); assert.ok(observed.savedAt === previous.savedAt || observed.savedAt === recovery.savedAt);
          assert.deepEqual(observed, observed.savedAt === previous.savedAt ? previous : recovery);
        }
      }
      const [removed, ...reads] = await Promise.all([
        json('DELETE', `/api/projects/${project!.id}/recovery`, { baseRevision: project!.revision, savedAt: recovery.savedAt }),
        ...Array.from({ length: 8 }, () => json('GET', `/api/projects/${project!.id}/recovery`)),
      ]);
      assert.equal(removed.status, 204, await removed.clone().text());
      for (const read of reads) {
        assert.ok(read.status === 200 || read.status === 404, await read.clone().text());
        if (read.status === 200) assert.deepEqual(await read.json(), recovery);
      }
      assert.equal((await json('GET', `/api/projects/${project!.id}/recovery`)).status, 404);
    });
  } finally {
    if (host && host.exitCode === null) { const stopped = once(host, 'exit'); host.kill(); await stopped; }
    await rm(temporary, { recursive: true, force: true });
  }
});

function fixture() {
  const project = newProject('Concurrent storage'), note = addNode(project, project.entryCanvasId, 'media', 'Preserved note');
  note.text = 'Complete body; '.repeat(20_000); return project;
}

const gateProbe = `using System.Reflection;
using System.Text.Json.Nodes;
using Meridian.Host;
using var store = new ProjectStore(args[0]);
var created = await store.Create((JsonObject)JsonNode.Parse(File.ReadAllText(args[1]))!);
var id = created["id"]!.GetValue<string>();
await store.SaveRecovery(id, created, 1, null);
var checkpoint = await store.SaveCheckpoint(id, "Stored frame", created);
var checkpointId = checkpoint["id"]!.GetValue<string>();
var gate = (SemaphoreSlim)typeof(ProjectStore).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(store)!;
var readers = new (string name, Func<object> read)[] {
    ("Read", () => store.Read(id)), ("List", () => store.List()),
    ("ReadRecovery", () => store.ReadRecovery(id)), ("ListCheckpoints", () => store.ListCheckpoints(id)),
    ("ReadCheckpoint", () => store.ReadCheckpoint(id, checkpointId))
};
foreach (var (name, read) in readers) {
    gate.Wait();
    using var started = new ManualResetEventSlim();
    using var finished = new ManualResetEventSlim();
    Exception? failure = null;
    var thread = new Thread(() => { started.Set(); try { _ = read(); } catch (Exception error) { failure = error; } finally { finished.Set(); } }) { IsBackground = true };
    thread.Start();
    if (!started.Wait(TimeSpan.FromSeconds(5))) { gate.Release(); throw new Exception(name + " did not start."); }
    var escapedWriter = finished.Wait(TimeSpan.FromMilliseconds(150));
    gate.Release();
    if (!thread.Join(TimeSpan.FromSeconds(5))) throw new Exception(name + " did not finish after the writer released its gate.");
    if (failure is not null) throw new Exception(name + " failed.", failure);
    if (escapedWriter) throw new Exception(name + " opened project storage while a writer owned the transaction.");
}
Console.WriteLine("PROJECT_READ_GATE_OK:" + readers.Length);
`;

async function freePort(): Promise<number> {
  const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const port = (server.address() as { port: number }).port;
  await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve())); return port;
}
