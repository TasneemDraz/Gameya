#!/usr/bin/env node
// Fails (exit 1) when ar.json and en.json do not have the same flattened key set.
import { readFileSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const dir = resolve(process.argv[2] ?? join(here, '..', 'src', 'gameya-web', 'public', 'i18n'));

function flatten(obj, prefix = '') {
  return Object.entries(obj).flatMap(([k, v]) => {
    const path = prefix ? `${prefix}.${k}` : k;
    return v !== null && typeof v === 'object' && !Array.isArray(v) ? flatten(v, path) : [path];
  });
}

const load = (file) => new Set(flatten(JSON.parse(readFileSync(join(dir, file), 'utf8'))));
const files = { 'ar.json': load('ar.json'), 'en.json': load('en.json') };

let failed = false;
for (const [file, keys] of Object.entries(files)) {
  const other = file === 'ar.json' ? files['en.json'] : files['ar.json'];
  const missing = [...other].filter((k) => !keys.has(k)).sort();
  if (missing.length > 0) {
    failed = true;
    console.error(`${file} is missing ${missing.length} key(s):`);
    missing.forEach((k) => console.error(`  ${k}`));
  }
}
process.exit(failed ? 1 : 0);
