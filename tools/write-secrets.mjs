// Writes the content-root secrets.json the API loads at startup (see tools/publish-site.ps1 and docs/deployment.md).
// Usage: node tools/write-secrets.mjs <out-path>
// Each name below is read from the environment; "__" in a name becomes nesting, so Jwt__SigningKey is
// {"Jwt":{"SigningKey":...}}. Empty values are omitted. Values are never printed.
import { writeFileSync } from "node:fs";

export const REQUIRED_NAMES = [
  "ConnectionStrings__App",
  "ConnectionStrings__Migrator",
  "Jwt__SigningKey",
  "Security__HashKey",
  "Storage__UrlSigningKey",
];

export const SECRET_NAMES = [
  ...REQUIRED_NAMES,
  "Jwt__Issuer",
  "Jwt__Audience",
  "Storage__Provider",
  "Storage__S3__ServiceUrl",
  "Storage__S3__Bucket",
  "Storage__S3__AccessKeyId",
  "Storage__S3__SecretAccessKey",
  "Storage__S3__Region",
  "Email__Smtp__Host",
  "Email__Smtp__Port",
  "Email__Smtp__Username",
  "Email__Smtp__Password",
  "Email__Smtp__UseStartTls",
  "Email__From",
  "Sms__Provider",
  "Sms__SmsGate__BaseUrl",
  "Sms__SmsGate__Username",
  "Sms__SmsGate__Password",
  "MediatR__LicenseKey",
  "App__WebBaseUrl",
  "OTEL_EXPORTER_OTLP_ENDPOINT",
  "OTEL_EXPORTER_OTLP_HEADERS",
  "ForwardedHeaders__KnownNetworks__0",
  "Observability__LogFilePath",
];

export function buildSecrets(env) {
  const missing = REQUIRED_NAMES.filter((n) => !env[n]);
  if (missing.length > 0) {
    return { missing };
  }
  const root = {};
  for (const name of SECRET_NAMES) {
    const value = env[name];
    if (value === undefined || value === "") continue;
    const parts = name.split("__");
    let node = root;
    for (const part of parts.slice(0, -1)) {
      node = node[part] ??= {};
    }
    node[parts[parts.length - 1]] = value;
  }
  return { root };
}

const outPath = process.argv[2];
if (!outPath) {
  console.error("usage: node tools/write-secrets.mjs <out-path>");
  process.exit(2);
}

const { missing, root } = buildSecrets(process.env);
if (missing) {
  for (const name of missing) console.error(`::error::required secret ${name} is empty or not set`);
  process.exit(1);
}
writeFileSync(outPath, JSON.stringify(root, null, 2), { mode: 0o600 });
console.log(`Wrote ${outPath}`);
