import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const script = join(dirname(fileURLToPath(import.meta.url)), 'check-i18n.mjs');

function run(ar, en) {
  const dir = mkdtempSync(join(tmpdir(), 'i18n-'));
  writeFileSync(join(dir, 'ar.json'), JSON.stringify(ar));
  writeFileSync(join(dir, 'en.json'), JSON.stringify(en));
  return spawnSync(process.execPath, [script, dir], { encoding: 'utf8' });
}

test('equal key sets exit 0', () => {
  const r = run({ a: { b: 'x' }, c: 'y' }, { a: { b: 'z' }, c: 'w' });
  assert.equal(r.status, 0);
});

test('missing key exits 1 and prints the path and the file', () => {
  const r = run({ a: { b: 'x' } }, { a: { b: 'x', c: 'y' }, d: 'z' });
  assert.equal(r.status, 1);
  const out = r.stdout + r.stderr;
  assert.match(out, /ar\.json/);
  assert.match(out, /a\.c/);
  assert.match(out, /\bd\b/);
});

test('key missing from en is reported against en.json', () => {
  const r = run({ only: 'x' }, {});
  assert.equal(r.status, 1);
  assert.match(r.stdout + r.stderr, /en\.json[\s\S]*only/);
});
