import { constants, closeSync, existsSync, fstatSync, fsyncSync, lstatSync, mkdirSync, openSync, readFileSync, renameSync, unlinkSync, writeFileSync } from 'node:fs';
import { createHash, randomBytes } from 'node:crypto';
import { dirname, isAbsolute, resolve } from 'node:path';

const LIMIT = 1024 * 1024;
const hash = value => createHash('sha256').update(value).digest('hex');
const error = message => new Error(`Private OAuth grant store ${message}`);

function checkDirectory(path) {
  mkdirSync(path, { recursive: true, mode: 0o700 });
  for (let current = path;; current = dirname(current)) {
    const stat = lstatSync(current);
    if (stat.isSymbolicLink() || !stat.isDirectory()) throw error('must not use linked directories.');
    if (dirname(current) === current) break;
  }
  // Windows setup must restrict this directory's ACL to the current user.
  if (process.platform !== 'win32' && (lstatSync(path).mode & 0o077)) throw error('directory permissions must be private.');
}

function readBounded(path, maximum) {
  let stat;
  try { stat = lstatSync(path); }
  catch (cause) { if (cause.code === 'ENOENT') return null; throw error('could not be inspected.'); }
  if (!stat.isFile() || stat.isSymbolicLink() || stat.size > maximum) throw error('is not a bounded regular file.');
  if (process.platform !== 'win32' && (stat.mode & 0o077)) throw error('file permissions must be private.');
  const fd = openSync(path, constants.O_RDONLY | (constants.O_NOFOLLOW ?? 0));
  try {
    if (fstatSync(fd).size > maximum) throw error('exceeds its size limit.');
    return readFileSync(fd, 'utf8');
  } finally { closeSync(fd); }
}

function durableNewFile(path, contents) {
  const fd = openSync(path, constants.O_WRONLY | constants.O_CREAT | constants.O_EXCL, 0o600);
  try { writeFileSync(fd, contents, 'utf8'); fsyncSync(fd); }
  finally { closeSync(fd); }
}
function flushDirectory(path) {
  // POSIX requires flushing directory entries as well as file contents. Node
  // cannot portably open/flush Windows directories; Windows relies on atomic
  // replacement and filesystem journaling, not a power-loss durability claim.
  if (process.platform === 'win32') return;
  const fd = openSync(path, constants.O_RDONLY | (constants.O_DIRECTORY ?? 0));
  try { fsyncSync(fd); } finally { closeSync(fd); }
}

/** Hash-only OAuth state. Atomic replacement plus a durable pending marker makes
 * interrupted writes fail closed rather than silently restoring a reusable token.
 * A local administrator must deliberately reset an incomplete store and relink.
 * File I/O is synchronous because OAuth grants are rare and commits are <=1 MiB;
 * no token is returned before its rotation/revocation reaches stable storage.
 */
export function createPrivateGrantStore({ path, binding }) {
  if (typeof path !== 'string' || !isAbsolute(path)) throw error('path must be absolute.');
  if (!/^[a-f0-9]{64}$/.test(binding)) throw error('binding must be a SHA-256 digest.');
  const target = resolve(path);
  const parent = dirname(target);
  checkDirectory(parent);
  const pendingPath = `${target}.pending`;
  if (existsSync(pendingPath)) throw error('contains an incomplete transaction; reset locally and relink.');
  const initial = readBounded(target, LIMIT);
  let revision = 0;
  let fingerprint = initial === null ? null : hash(initial);
  let payload;
  if (initial !== null) {
    let parsed;
    try { parsed = JSON.parse(initial); } catch { throw error('is malformed; reset locally and relink.'); }
    if (parsed?.schemaVersion !== 1 || parsed.binding !== binding || !Number.isSafeInteger(parsed.revision) || parsed.revision < 1
      || !parsed.payload || typeof parsed.payload !== 'object' || Array.isArray(parsed.payload)) {
      throw error('has an incompatible issuer/client binding or schema; reset locally and relink.');
    }
    revision = parsed.revision;
    payload = parsed.payload;
  }
  let unavailable = false;
  return {
    load() { if (unavailable) throw error('is unavailable.'); return payload === undefined ? undefined : structuredClone(payload); },
    save(value) {
      if (unavailable) throw error('is unavailable.');
      let next;
      try { next = JSON.stringify({ schemaVersion: 1, binding, revision: revision + 1, payload: value }); }
      catch { throw error('payload must be serializable.'); }
      if (!value || typeof value !== 'object' || Array.isArray(value) || Buffer.byteLength(next, 'utf8') > LIMIT) throw error('payload exceeds its bounded object format.');
      const temporaryPath = `${target}.${randomBytes(12).toString('hex')}.tmp`;
      let ownMarker = false;
      let mutationStarted = false;
      try {
        checkDirectory(parent);
        durableNewFile(pendingPath, JSON.stringify({ schemaVersion: 1, startedAt: Date.now() }));
        ownMarker = true;
        flushDirectory(parent);
        const current = readBounded(target, LIMIT);
        if ((current === null ? null : hash(current)) !== fingerprint) throw error('changed in another process; restart to reload it.');
        mutationStarted = true;
        durableNewFile(temporaryPath, next);
        renameSync(temporaryPath, target);
        flushDirectory(parent);
        unlinkSync(pendingPath);
        flushDirectory(parent);
        ownMarker = false;
        revision += 1;
        fingerprint = hash(next);
        payload = structuredClone(value);
      } catch {
        unavailable = true;
        // If no mutation started, remove only this process's own harmless marker.
        // Otherwise leave it in place so the next process cannot accept old grants.
        if (ownMarker && !mutationStarted) { try { unlinkSync(pendingPath); } catch {} }
        throw error('commit failed; access is disabled until local recovery.');
      }
    },
  };
}
