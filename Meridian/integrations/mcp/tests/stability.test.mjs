import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { mkdtemp, mkdir, readFile, writeFile, cp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, sep } from 'node:path';
import { createServer } from 'node:http';

const scripts = fileURLToPath(new URL('../scripts/', import.meta.url));
const quote = value => `'${value.replaceAll("'", "''")}'`;
function powershell(source) {
  return execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `$ErrorActionPreference='Stop'; ${source}`], { encoding: 'utf8', windowsHide: true }).trim();
}
function powershellScript(path) {
  return new Promise((resolveRun, reject) => {
    const child = spawn('pwsh', ['-NoProfile', '-NonInteractive', '-File', path], { stdio: 'ignore', windowsHide: true });
    child.once('error', reject);
    child.once('exit', code => code === 0 ? resolveRun() : reject(new Error(`Fixture launcher exited ${code}.`)));
  });
}

test('stable launchers parse without executing or creating runtime state', { skip: process.platform !== 'win32' }, () => {
  const result = powershell(`$failed=@(); Get-ChildItem -LiteralPath ${quote(scripts)} -Filter '*.ps1' | ForEach-Object { $tokens=$null; $errors=$null; [void][Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$tokens,[ref]$errors); $failed += $errors }; if ($failed.Count) { throw 'PowerShell parse failed' }; 'passed'`);
  assert.equal(result, 'passed');
});

test('stable origin helper rejects temporary, credentialed, insecure and path URLs', { skip: process.platform !== 'win32' }, () => {
  const result = powershell(`. (Join-Path ${quote(scripts)} 'stable-common.ps1'); foreach ($value in @('http://example.com','https://x.trycloudflare.com','https://u:p@example.com','https://example.com/path','https://example.com/?x=1')) { $rejected=$false; try { Assert-StableOrigin $value | Out-Null } catch { $rejected=$true }; if (-not $rejected) { throw 'Unsafe origin accepted' } }; Assert-StableOrigin 'https://example.com/'`);
  assert.equal(result, 'https://example.com');
});

test('stable process ownership verifies executable and exact start time without stopping processes', { skip: process.platform !== 'win32' }, () => {
  const result = powershell(`. (Join-Path ${quote(scripts)} 'stable-common.ps1'); $identity=Get-ProcessIdentity (Get-Process -Id $PID); if ((Get-OwnedProcess $identity).Id -ne $PID) { throw 'Owned process rejected' }; $identity.startedAt='2000-01-01T00:00:00Z'; $rejected=$false; try { Get-OwnedProcess $identity | Out-Null } catch { $rejected=$true }; if (-not $rejected) { throw 'Wrong process accepted' }; 'passed'`);
  assert.equal(result, 'passed');
});

test('stable private directory ACL can be initialized and verified repeatedly without audit privileges', { skip: process.platform !== 'win32' }, async () => {
  const temporary = await mkdtemp(join(tmpdir(), 'meridian-mcp-acl-'));
  try {
    const privateRoot = join(temporary, 'state');
    const copied = join(temporary, 'common.ps1');
    const common = await readFile(join(scripts, 'stable-common.ps1'), 'utf8');
    const original = "[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion/Stable'))";
    assert(common.includes(original));
    await writeFile(copied, common.replace(original, `[IO.Path]::GetFullPath(${quote(privateRoot)})`));
    const result = powershell(`. ${quote(copied)}; $root=Initialize-StableState; $info=[IO.DirectoryInfo]::new($root); $sections=[Security.AccessControl.AccessControlSections]::Access -bor [Security.AccessControl.AccessControlSections]::Owner; $before=[IO.FileSystemAclExtensions]::GetAccessControl($info,$sections).GetSecurityDescriptorSddlForm($sections); Initialize-StableState | Out-Null; $after=[IO.FileSystemAclExtensions]::GetAccessControl($info,$sections).GetSecurityDescriptorSddlForm($sections); if ($before -ne $after) { throw 'Idempotent ACL changed' }; 'passed'`);
    assert.equal(result, 'passed');
  } finally {
    const resolved = resolve(temporary);
    assert(resolved.startsWith(`${resolve(tmpdir())}${sep}`) && resolved.includes('meridian-mcp-acl-'));
    await rm(resolved, { recursive: true, force: true });
  }
});

test('scheduled task ownership resolves normalized account names to an exact SID', { skip: process.platform !== 'win32' }, () => {
  const result = powershell(`. (Join-Path ${quote(scripts)} 'stable-common.ps1'); $identity=Get-StableTaskIdentity; $name=[Security.Principal.WindowsIdentity]::GetCurrent().Name.Split([char]92)[-1]; $task=[pscustomobject]@{Description=$identity.description;Actions=@([pscustomobject]@{Execute=(Get-Process -Id $PID).Path;Arguments=('-NoProfile -NonInteractive -WindowStyle Hidden -File "' + $identity.scriptPath + '"')});Principal=[pscustomobject]@{UserId=$name}}; Assert-OwnedStableTask $task | Out-Null; $task.Principal.UserId='S-1-5-18'; $denied=$false;try{Assert-OwnedStableTask $task | Out-Null}catch{$denied=$true};if(-not $denied){throw 'Wrong SID accepted'};'passed'`);
  assert.equal(result, 'passed');
});

test('stable supervisor restarts only its fixture companion and preserves configuration', { skip: process.platform !== 'win32', timeout: 45000 }, async () => {
  const temporary = await mkdtemp(join(tmpdir(), 'meridian-mcp-supervisor-'));
  const privateRoot = join(temporary, 'state');
  const copiedScripts = join(temporary, 'scripts');
  const backend = createServer((req, res) => { res.setHeader('content-type', 'application/json'); res.end(JSON.stringify({ application: 'atlas-meridian', status: 'ready' })); });
  const listen = server => new Promise(resolveListen => server.listen(0, '127.0.0.1', resolveListen));
  const close = server => new Promise(resolveClose => server.close(resolveClose));
  const reserve = createServer();
  const waitFor = async predicate => {
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
      try { const value = await predicate(); if (value) return value; } catch {}
      await new Promise(resolveWait => setTimeout(resolveWait, 200));
    }
    throw new Error('Fixture supervisor did not reach expected state.');
  };
  let launched = false;
  let fixtureSupervisor;
  try {
    await mkdir(privateRoot);
    await cp(scripts, copiedScripts, { recursive: true });
    const commonPath = join(copiedScripts, 'stable-common.ps1');
    const common = await readFile(commonPath, 'utf8');
    const original = "[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'AtlasMeridian/McpCompanion/Stable'))";
    assert(common.includes(original));
    await writeFile(commonPath, common.replace(original, `[IO.Path]::GetFullPath(${quote(privateRoot)})`));
    await listen(backend); await listen(reserve);
    const port = reserve.address().port;
    await close(reserve);
    const fixturePath = join(temporary, 'companion.mjs');
    await writeFile(fixturePath, `import {createServer} from 'node:http';import{readFileSync}from'node:fs';const c=JSON.parse(readFileSync(process.argv[3],'utf8'));createServer((q,s)=>{s.setHeader('content-type','application/json');s.end(JSON.stringify({issuer:c.publicOrigin}));}).listen(c.port,'127.0.0.1');`);
    const config = {
      publicOrigin: 'https://stable-fixture.example', port, meridianOrigin: `http://127.0.0.1:${backend.address().port}`,
      auth: { clientId: 'fixture-credential-retained', grantStorePath: join(privateRoot, 'grants.json') },
      launcher: { nodePath: process.execPath, serverPath: fixturePath, provider: 'external' },
    };
    const configPath = join(privateRoot, 'config.json');
    const expectedConfig = JSON.stringify(config);
    await writeFile(configPath, expectedConfig);
    // The isolated fixture intentionally runs its own supervisor directly;
    // production start-stable delegates to Task Scheduler and is never invoked here.
    fixtureSupervisor = spawn('pwsh', ['-NoProfile', '-NonInteractive', '-File', join(copiedScripts, 'supervise-stable.ps1')], { stdio: 'ignore', windowsHide: true });
    launched = true;
    const runPath = join(privateRoot, 'run.json');
    const first = await waitFor(async () => { const run = JSON.parse(await readFile(runPath)); return run.status === 'local-ready' && run.companion?.id ? run : null; });
    // Kill only the fixture companion recorded by the isolated supervisor.
    assert.equal(first.companion.path.toLowerCase(), process.execPath.toLowerCase());
    process.kill(first.companion.id);
    const restarted = await waitFor(async () => { const run = JSON.parse(await readFile(runPath)); return run.status === 'local-ready' && run.companion?.id && run.companion.id !== first.companion.id ? run : null; });
    assert(restarted.restartCount >= 1);
    assert.equal(await readFile(configPath, 'utf8'), expectedConfig);
    process.kill(fixtureSupervisor.pid);
    await new Promise(resolveWait => setTimeout(resolveWait, 300));
    fixtureSupervisor = spawn('pwsh', ['-NoProfile', '-NonInteractive', '-File', join(copiedScripts, 'run-stable-task.ps1')], { stdio: 'ignore', windowsHide: true });
    const recovered = await waitFor(async () => { const run = JSON.parse(await readFile(runPath)); return run.status === 'local-ready' && run.supervisor?.id === fixtureSupervisor.pid && run.companion?.id !== restarted.companion.id ? run : null; });
    assert.notEqual(recovered.companion.id, restarted.companion.id);
    assert.equal(await readFile(configPath, 'utf8'), expectedConfig);
    await powershellScript(join(copiedScripts, 'stop-stable.ps1'));
    launched = false;
    const stopped = JSON.parse(await readFile(runPath));
    assert.equal(stopped.status, 'stopped');
    assert.equal(stopped.companion, null);
    const pausedReceipt = await readFile(runPath, 'utf8');
    await powershellScript(join(copiedScripts, 'run-stable-task.ps1'));
    assert.equal(await readFile(runPath, 'utf8'), pausedReceipt, 'The watchdog must honor the explicit stop request without launching a helper.');
  } finally {
    if (launched) await powershellScript(join(copiedScripts, 'stop-stable.ps1'));
    await close(backend);
    const resolved = resolve(temporary);
    assert(resolved.startsWith(`${resolve(tmpdir())}${sep}`) && resolved.includes('meridian-mcp-supervisor-'));
    await rm(resolved, { recursive: true, force: true });
  }
});
