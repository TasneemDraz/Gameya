import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const script = join(dirname(fileURLToPath(import.meta.url)), "write-secrets.mjs");

const REQUIRED = {
  ConnectionStrings__App: "Server=x;Database=y",
  ConnectionStrings__Migrator: "Server=x;Database=y",
  Jwt__SigningKey: "jwt-key",
  Security__HashKey: "hash-key",
  Storage__UrlSigningKey: "url-key",
};

function run(env) {
  const dir = mkdtempSync(join(tmpdir(), "write-secrets-"));
  const out = join(dir, "secrets.json");
  // Start from a clean environment so nothing leaks in from the host.
  const result = spawnSync(process.execPath, [script, out], {
    env: { PATH: process.env.PATH, SystemRoot: process.env.SystemRoot, ...env },
    encoding: "utf8",
  });
  return { result, out, json: () => JSON.parse(readFileSync(out, "utf8")) };
}

test("nests on double underscore", () => {
  const { result, json } = run({ ...REQUIRED, Email__Smtp__Host: "smtp.example.com" });
  assert.equal(result.status, 0, result.stderr);
  const o = json();
  assert.equal(o.Jwt.SigningKey, "jwt-key");
  assert.equal(o.ConnectionStrings.App, "Server=x;Database=y");
  assert.equal(o.Email.Smtp.Host, "smtp.example.com");
});

test("a value with quotes and a newline round-trips exactly", () => {
  const nasty = `a'b"c\nd\e $x`;
  const { result, json } = run({ ...REQUIRED, Jwt__SigningKey: nasty });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(json().Jwt.SigningKey, nasty);
});

test("a missing required value exits 1 and names the variable", () => {
  const env = { ...REQUIRED, Security__HashKey: "" };
  const { result, out } = run(env);
  assert.equal(result.status, 1);
  assert.match(result.stderr, /Security__HashKey/);
  assert.equal(existsSync(out), false);
});

test("an absent required variable also fails", () => {
  const { Jwt__SigningKey, ...rest } = REQUIRED;
  const { result } = run(rest);
  assert.equal(result.status, 1);
  assert.match(result.stderr, /Jwt__SigningKey/);
});

test("empty optional values are left out", () => {
  const { result, json } = run({ ...REQUIRED, Sms__SmsGate__Username: "", Email__From: "" });
  assert.equal(result.status, 0, result.stderr);
  const o = json();
  assert.equal(o.Sms?.SmsGate?.Username, undefined);
  assert.equal(o.Email?.From, undefined);
});

test("never prints values", () => {
  const { result } = run({ ...REQUIRED, Jwt__SigningKey: "super-secret-value" });
  assert.doesNotMatch(result.stdout + result.stderr, /super-secret-value/);
});
