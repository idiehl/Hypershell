import test from 'node:test';
import assert from 'node:assert/strict';
import { createAutomaticSave } from '../src/automatic-save.ts';

function timers() {
  let now = 0, id = 0;
  const pending = new Map<number, { callback: () => void; at: number }>();
  return {
    clock: {
      setTimeout(callback: () => void, delay: number) { const key = ++id; pending.set(key, { callback, at: now + delay }); return key; },
      clearTimeout(key: unknown) { pending.delete(key as number); },
    },
    async advance(ms: number) {
      const end = now + ms;
      for (;;) {
        const next = [...pending].sort((a, b) => a[1].at - b[1].at)[0];
        if (!next || next[1].at > end) break;
        now = next[1].at; pending.delete(next[0]); next[1].callback();
        await Promise.resolve(); await Promise.resolve();
      }
      now = end; await Promise.resolve(); await Promise.resolve();
    },
  };
}

test('automatic save coalesces committed actions for 500 ms and flushes the fifth action immediately', async () => {
  const timer = timers(); let count = 0;
  const save = createAutomaticSave({ clock: timer.clock, commit: async () => { count++; return true; } });
  save.changed(); await timer.advance(300); save.changed(); await timer.advance(499); assert.equal(count, 0);
  await timer.advance(1); assert.equal(count, 1);
  for (let i = 0; i < 5; i++) save.changed();
  await save.flush(); assert.equal(count, 2); assert.equal(save.pending, false);
});

test('continuous editing still saves within the maximum two-second burst', async () => {
  const timer = timers(); let count = 0;
  const save = createAutomaticSave({ clock: timer.clock, maxActions: 100, commit: async () => { count++; return true; } });
  save.changed();
  for (let i = 0; i < 4; i++) { await timer.advance(400); save.changed(); }
  await timer.advance(399); assert.equal(count, 0);
  await timer.advance(1); assert.equal(count, 1);
});

test('edits during an in-flight request are coalesced and saved after its returned revision', async () => {
  let value = 'first', revision = 1, active = 0, maxActive = 0, release!: () => void;
  const held = new Promise<void>(resolve => { release = resolve; });
  const requests: { value: string; revision: number }[] = [];
  const save = createAutomaticSave({ commit: async () => {
    active++; maxActive = Math.max(maxActive, active); requests.push({ value, revision });
    if (requests.length === 1) await held;
    revision++; active--; return true;
  } });
  save.changed(); const flush = save.flush(); await Promise.resolve();
  value = 'second'; save.changed(); value = 'latest'; save.changed(); release();
  assert.equal(await flush, true);
  assert.deepEqual(requests, [{ value: 'first', revision: 1 }, { value: 'latest', revision: 2 }]);
  assert.equal(maxActive, 1);
});

test('a failed save pauses retries, preserves pending work, and retries only on an explicit flush', async () => {
  const timer = timers(); let succeed = false, count = 0;
  const save = createAutomaticSave({ clock: timer.clock, commit: async () => { count++; return succeed; } });
  save.changed(); await timer.advance(500); assert.equal(save.paused, true); assert.equal(save.pending, true);
  save.changed(); await timer.advance(5000); assert.equal(count, 1);
  assert.equal(await save.flush(), false); assert.equal(count, 1);
  succeed = true; assert.equal(await save.flush(true), true); assert.equal(count, 2); assert.equal(save.paused, false);
});

test('reset drops a previous canvas pending timer without carrying a pause into the next canvas', async () => {
  const timer = timers(); let count = 0;
  const save = createAutomaticSave({ clock: timer.clock, commit: async () => { count++; return true; } });
  save.changed(); save.pause(); save.reset(); await timer.advance(5000); assert.equal(count, 0);
  save.changed(); await save.flush(); assert.equal(count, 1);
});

test('switching canvas context while a request finishes drains the new canvas draft independently', async () => {
  let release!:()=>void,count=0;
  const held=new Promise<void>(resolve=>{release=resolve;});
  const save=createAutomaticSave({commit:async()=>{count++;if(count===1)await held;return true;}});
  save.changed();const old=save.flush();await Promise.resolve();
  save.reset();save.changed();release();await old;await save.flush();
  assert.equal(count,2);assert.equal(save.pending,false);
});
