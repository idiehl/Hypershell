import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { chmodSync, existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, statSync, symlinkSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { tmpdir } from 'node:os';
import { test } from 'node:test';
import { createPrivateGrantStore } from '../src/private-grant-store.mjs';

const binding = createHash('sha256').update('isolated-grant-store-binding').digest('hex');
function fixture(t) {
  const directory = mkdtempSync(join(tmpdir(), 'meridian-store-'));
  const path = join(directory, 'private', 'grants.json');
  t.after(() => {
    const absolute = resolve(directory);
    assert.ok(absolute.startsWith(`${resolve(tmpdir())}\\meridian-store-`) || absolute.startsWith(`${resolve(tmpdir())}/meridian-store-`));
    rmSync(absolute, { recursive: true, force: true });
  });
  return { path, directory, create: () => createPrivateGrantStore({ path, binding }) };
}

test('atomic grant store survives reload, increments revisions, leaves no marker or temporary file', t => {
  const f = fixture(t);
  const store = f.create();
  assert.equal(store.load(), undefined);
  const value = { schemaVersion: 1, tokens: [['hash-only-example', { expiresAt: 123 }]] };
  store.save(value);
  value.tokens[0][1].expiresAt = 999;
  assert.equal(store.load().tokens[0][1].expiresAt, 123);
  const returned = store.load();
  returned.tokens[0][1].expiresAt = 555;
  assert.equal(store.load().tokens[0][1].expiresAt, 123);
  assert.deepEqual(f.create().load(), store.load());
  store.save({ schemaVersion: 1, tokens: [] });
  const envelope = JSON.parse(readFileSync(f.path, 'utf8'));
  assert.equal(envelope.revision, 2);
  assert.equal(envelope.binding, binding);
  assert.deepEqual(readdirSync(join(f.directory, 'private')), ['grants.json']);
  if (process.platform !== 'win32') {
    assert.equal(statSync(f.path).mode & 0o077, 0);
    assert.equal(statSync(join(f.directory, 'private')).mode & 0o077, 0);
  }
});

test('different issuer/client binding, corrupted state and incomplete journal fail closed', t => {
  const f = fixture(t);
  const store = f.create();
  store.save({ records: [] });
  assert.throws(() => createPrivateGrantStore({ path: f.path, binding: 'a'.repeat(64) }), /binding/);
  writeFileSync(f.path, '{not-json', { mode: 0o600 });
  assert.throws(f.create, /malformed/);
  writeFileSync(`${f.path}.pending`, '{}', { mode: 0o600 });
  assert.throws(f.create, /incomplete transaction/);
});

test('stale writers cannot overwrite a successful competing revision', t => {
  const f = fixture(t);
  const one = f.create();
  const two = f.create();
  one.save({ accepted: 'first-commit' });
  assert.throws(() => two.save({ accepted: 'stale-overwrite' }), /commit failed/);
  assert.throws(two.load, /unavailable/);
  assert.deepEqual(f.create().load(), { accepted: 'first-commit' });
  assert.equal(existsSync(`${f.path}.pending`), false);
});

test('bounded payloads, malformed schema and relative paths are rejected', t => {
  const f = fixture(t);
  assert.throws(() => createPrivateGrantStore({ path: 'relative.json', binding }), /absolute/);
  assert.throws(() => createPrivateGrantStore({ path: f.path, binding: 'not-a-digest' }), /binding/);
  const store = f.create();
  assert.throws(() => store.save({ tooLarge: 'x'.repeat(1024 * 1024) }), /bounded/);
  assert.throws(() => store.save([]), /bounded/);
  assert.equal(existsSync(f.path), false);
  writeFileSync(f.path, JSON.stringify({ schemaVersion: 1, revision: 1, binding, payload: [] }), { mode: 0o600 });
  assert.throws(f.create, /schema/);
  writeFileSync(f.path, 'x'.repeat(1024 * 1024 + 1), { mode: 0o600 });
  assert.throws(f.create, /bounded/);
});

test('store never follows a linked grant file', t => {
  const f = fixture(t);
  f.create();
  const other = join(f.directory, 'other.json');
  writeFileSync(other, '{}', { mode: 0o600 });
  try { symlinkSync(other, f.path, 'file'); }
  catch (error) {
    if (['EPERM', 'EACCES', 'ENOTSUP'].includes(error.code)) { t.skip('Creating symlinks is unavailable in this isolated environment.'); return; }
    throw error;
  }
  assert.throws(f.create, /regular file/);
  assert.equal(readFileSync(other, 'utf8'), '{}');
});

test('non-Windows grant file and final directory permissions must remain private', { skip: process.platform === 'win32' }, t => {
  const f = fixture(t);
  const store = f.create();
  store.save({ records: [] });
  chmodSync(f.path, 0o644);
  assert.throws(f.create, /file permissions/);
  chmodSync(f.path, 0o600);
  chmodSync(join(f.directory, 'private'), 0o755);
  assert.throws(f.create, /directory permissions/);
});
