// Run: node AIInterviewSimulator.Tests/browser-timing.test.mjs
// Executes the actual browser timing module with a controlled monotonic clock.
import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
let now = 0;
globalThis.performance = { now: () => now };
globalThis.requestAnimationFrame = callback => callback();
const source = await readFile(new URL('../AIInterviewSimulator.Client/wwwroot/js/interview-chat.js', import.meta.url), 'utf8');
const { markPhase, captureTiming, storeSession, loadSession } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const key = 'session:1';
assert.equal(captureTiming('not-observed', 'submit'), null);
await markPhase(key, 'question');
now = 1000;
const submitted = captureTiming(key, 'submit');
assert.equal(submitted.elapsedMilliseconds, 1000);
now = 9000;
assert.deepEqual(captureTiming(key, 'submit'), submitted); // retries never restart writing time
const feedback = await markPhase(key, 'feedback1');
assert.equal(feedback.waitMilliseconds, 8000);
now = 12000;
assert.equal(captureTiming(key, 'decision').elapsedMilliseconds, 3000);
now = 13000;
await markPhase(key, 'revision');
now = 15000;
assert.equal(captureTiming(key, 'revision').elapsedMilliseconds, 2000);
now = 25000;
assert.equal((await markPhase(key, 'feedback2')).waitMilliseconds, 10000);
now = 27000;
const continued = captureTiming(key, 'continue');
assert.equal(continued.elapsedMilliseconds, 2000);
assert.equal(continued.stageElapsedMilliseconds, 27000);
now = 40000;
assert.deepEqual(captureTiming(key, 'continue'), continued);
assert.deepEqual(await markPhase(key, 'feedback1'), feedback); // redisplay acknowledgement is immutable
await markPhase('session:2', 'question');
now = 41000;
assert.equal(captureTiming('session:2', 'submit').elapsedMilliseconds, 1000);
console.log('PASS: browser timing separates writing, feedback wait, revision, decision and continuation; retries preserve observations.');

const storage = new Map();
globalThis.sessionStorage = { setItem: (key, value) => storage.set(key, value), getItem: key => storage.get(key) ?? null };
const savedSession = { id: 'isolated-session', flowVersion: 2, condition: 'A', nickname: 'Test' };
assert.equal(storeSession(savedSession), true);
assert.deepEqual(loadSession(), savedSession);
storage.set('interview-session-v2', '{');
assert.equal(loadSession(), null);
storeSession({ id: 'legacy', flowVersion: 1 });
assert.equal(loadSession(), null);
globalThis.sessionStorage = { setItem: () => { throw new Error('blocked'); }, getItem: () => { throw new Error('blocked'); } };
assert.equal(storeSession(savedSession), false);
assert.equal(loadSession(), null);
console.log('PASS: tab-scoped session recovery validates version and tolerates unavailable/corrupt storage.');
