# Deployment

Gam'eya runs on one MonsterASP.NET site per environment (`staging` and `production`). The API runs in-process under IIS and serves the Angular build from `wwwroot` on the same origin, so there is no separate front-end host and no reverse proxy that we control.

| Branch or tag | Environment  | Trigger                                                           |
| ------------- | ------------ | ----------------------------------------------------------------- |
| `main`        | `staging`    | `.github/workflows/deploy.yml` on every push to `main`            |
| `v*` tag      | `production` | the same workflow, after the GitHub Environment reviewer approves |

## Hosting map

| Part                    | Where                                                                                                                    |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| API and Angular build   | one MonsterASP site per environment (IIS in-process, Angular served from `wwwroot`)                                      |
| Database                | the SQL Server that comes with the MonsterASP site. Hangfire uses the same database (schema `hangfire`)                  |
| Uploaded files (photos) | a Backblaze B2 bucket per environment, reached through the S3 API. The bucket has versioning and a 35-day lifecycle rule |
| Database backups        | a separate B2 backup bucket, written by `.github/workflows/backup.yml`                                                   |
| Email                   | Brevo SMTP                                                                                                               |
| SMS                     | SMS Gate (an Android phone) for staging and the pilot. A paid provider before public launch                              |
| Monitoring              | UptimeRobot on `/health` (also keeps the site awake), healthchecks.io for backups                                        |
| Deploys                 | `.github/workflows/deploy.yml`, WebDeploy from a Windows runner. Migrations run from the runner                          |

## One-time owner setup

Do this once per environment. Until it is done, the deploy workflow cannot succeed and acceptance criterion AC-F8 stays pending.

1. **MonsterASP account and host answers (Task 0).** Record the answers in the PR description.
   - Does the free plan allow two sites (staging and production)? If not, use two accounts, or run staging only until a paid plan is bought.
   - In the panel, note: the SQL Server version; whether the database accepts remote connections (host name and port), because the deploy runs migrations from the GitHub runner; the WebDeploy server, site name, user and password; whether WebSockets can be turned on; the disk quota; the idle timeout and whether "Always On" exists; and whether a self-contained `win-x64` app is allowed.
   - **Ask MonsterASP support whether a proxy sits in front of IIS.** If it does, ask for its IP range and which forwarding header it sets (see "Client IPs").
   - If the database is not reachable remotely, the migration step in the deploy has to change (the API would apply migrations at startup). Stop and ask before going on.
2. **Turn on WebSockets** in the site's panel settings (the app uses SignalR).
3. **Backblaze B2.**
   - Create a files bucket per environment with versioning on (keep all versions) and a lifecycle rule that deletes old versions after 35 days.
   - Create a backup bucket per environment with a lifecycle rule that keeps files for 35 days. The backup workflow does not set this rule.
   - Create an application key per environment. It needs read and write on both buckets of that environment. Note the S3 endpoint, for example `https://s3.us-west-004.backblazeb2.com`, and the bucket's region, for example `us-west-004`.
4. **Brevo.** Create an account and verify the sender address or domain. Use the SMTP credentials as `SMTP_USERNAME` / `SMTP_PASSWORD`. The API always uses STARTTLS. The host and port default to `smtp-relay.brevo.com` and 587. Use a plain display name in `EMAIL_FROM` (no apostrophe), for example `Gameya <noreply@example.com>`.
5. **SMS.**
   - **Staging and demo/pilot production** use `SmsGate`. Install SMS Gate on a spare Android phone with a SIM that has an SMS bundle, enable cloud mode (or point it at a self-hosted server), turn on end-to-end encryption if available, copy the username and password into `SMSGATE_USERNAME` / `SMSGATE_PASSWORD`, raise Android's SMS send limit with `adb shell settings put global sms_outgoing_check_max_count <n>`, and keep the phone charging on Wi-Fi.
   - The API refuses `Sms:Provider=Logging` outside Development and E2E (the codes would end up in logs), so staging must use `SmsGate` too. Production refuses to start with `Logging`.
   - **Before public launch**, switch to a paid provider (Cequens or SMS Misr). Its adapter `ProviderSmsSender` is implemented in a follow-up task once the contract exists.
6. **GitHub Environments.** Create `staging` and `production` (add a required reviewer on `production`) and set the secrets below on each.
7. **UptimeRobot (keep-alive).** Add an HTTP monitor on `https://<site>/health` with a 5-minute interval, for each environment. This stops MonsterASP's idle shutdown and keeps Hangfire running. Do not use a GitHub scheduled workflow for this: a run every few minutes would use up a private repo's free Actions minutes.
8. **healthchecks.io (optional).** Create a check per environment and put its ping URL in `BACKUP_PING_URL`.

### GitHub Environment secrets

Set these on both `staging` and `production`. Each environment has its own values.

| Secret                                                      | Required                                  | Purpose                                                                                                                |
| ----------------------------------------------------------- | ----------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `WEBDEPLOY_SERVER`                                          | yes                                       | WebDeploy host name from the panel (no scheme, no port)                                                                |
| `WEBDEPLOY_SITE`                                            | yes                                       | site name as WebDeploy knows it                                                                                        |
| `WEBDEPLOY_USERNAME`                                        | yes                                       | WebDeploy user                                                                                                         |
| `WEBDEPLOY_PASSWORD`                                        | yes                                       | WebDeploy password. It must not contain `,`, spaces or `"`: msdeploy argument parsing breaks on them                   |
| `SITE_URL`                                                  | yes                                       | public URL with scheme, for example `https://example.monsterasp.net`. Used for the health poll and as the web base URL |
| `DB_CONNECTION_STRING`                                      | yes                                       | SQL Server connection string. Used for migrations, the app, and the backup export                                      |
| `JWT_SIGNING_KEY`                                           | yes                                       | at least 32 characters                                                                                                 |
| `SECURITY_HASH_KEY`                                         | yes                                       | HMAC key for OTP and token hashes                                                                                      |
| `STORAGE_URL_SIGNING_KEY`                                   | yes                                       | at least 32 bytes                                                                                                      |
| `B2_S3_ENDPOINT`                                            | yes                                       | B2 S3 endpoint URL                                                                                                     |
| `B2_FILES_BUCKET`                                           | yes                                       | files bucket name                                                                                                      |
| `B2_KEY_ID`, `B2_APP_KEY`                                   | yes                                       | B2 application key (also used by the backup workflow)                                                                  |
| `B2_REGION`                                                 | yes                                       | the bucket's region, for example `us-west-004`. The default `us-east-1` breaks request signing on B2                   |
| `EMAIL_FROM`                                                | yes                                       | sender, for example `Gameya <noreply@example.com>`                                                                     |
| `SMTP_USERNAME`, `SMTP_PASSWORD`                            | yes                                       | Brevo SMTP credentials                                                                                                 |
| `SMTP_HOST`, `SMTP_PORT`                                    | no                                        | default to `smtp-relay.brevo.com` and `587`                                                                            |
| `SMS_PROVIDER`                                              | no                                        | `SmsGate` (the default when empty). It must be spelled exactly `SmsGate`: the app matches it case-sensitively          |
| `SMSGATE_USERNAME`, `SMSGATE_PASSWORD`                      | only if `SMS_PROVIDER` is `SmsGate`/empty | SMS Gate credentials                                                                                                   |
| `SMSGATE_BASE_URL`                                          | no                                        | for a self-hosted SMS Gate server. Defaults to `https://api.sms-gate.app/3rdparty/v1`                                  |
| `MEDIATR_LICENSE_KEY`                                       | no                                        | MediatR Community license key                                                                                          |
| `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS` | no                                        | OpenTelemetry export                                                                                                   |
| `FORWARDED_KNOWN_NETWORK`                                   | no                                        | CIDR of a MonsterASP proxy in front of IIS, if there is one (see "Client IPs")                                         |
| `B2_BACKUP_BUCKET`                                          | yes, for backups                          | backup bucket name (read by `backup.yml` only)                                                                         |
| `BACKUP_PING_URL`                                           | no                                        | healthchecks.io ping, called after a successful backup                                                                 |

The deploy has a preflight step that runs before anything touches the database. It fails with a message naming each required secret that is empty. The required secrets are the ones marked "yes" above, except `B2_BACKUP_BUCKET`, plus `SMSGATE_USERNAME` and `SMSGATE_PASSWORD` when `SMS_PROVIDER` is empty or `SmsGate`. The backup workflow has its own preflight for `DB_CONNECTION_STRING`, `B2_KEY_ID`, `B2_APP_KEY` and `B2_BACKUP_BUCKET`.

### Branch protection

Protect `main` so a merge needs these status checks from `ci.yml` (names as in the workflow): **backend**, **frontend**, **e2e**, **commits** and **image**.

## What a deploy does

`deploy.yml` runs on a Windows runner. It starts on a push to `main` (staging), a push of a `v*` tag (production, after the reviewer approves) or a manual run (see "Rollback"). Deploys to the same environment queue one after another and are never cancelled half way.

1. **Preflight.** Checks the required secrets (see above). Nothing has changed yet if it fails.
2. **Domain tests.** `dotnet test tests/Gameya.Domain.Tests` proves `Africa/Cairo` resolves on Windows.
3. **Build the migrations bundle.** A self-contained `win-x64` `efbundle.exe`. The design-time factory needs a connection string even to build, so a placeholder is set; it is never used to connect.
4. **Run the migrations** against `DB_CONNECTION_STRING` from the runner. The MonsterASP database must accept connections from GitHub's runners.
5. **Publish the site.** `tools/publish-site.ps1` builds the Angular app and the API into one folder, with the Angular build in `wwwroot`.
6. **Write `secrets.json`** into the site folder from the Environment secrets, and add `ASPNETCORE_ENVIRONMENT` (`Production` for a tag, `Staging` otherwise) to `web.config`. The environment name lives in `web.config` because it must be known before configuration loads.
7. **Sync with WebDeploy.** `msdeploy` pushes the folder with the `AppOffline` and `DoNotDeleteRule` rules and skips the `logs` folder.
8. **Wait for `/health`.** Polls `SITE_URL/health` every 10 seconds for up to 5 minutes until the top-level status is exactly `Healthy`. `Degraded` and `Unhealthy` keep waiting, and the job fails at the deadline.
9. **Upload the site** as a workflow artifact (30 days), without `secrets.json`.

Migrations run before the new files go live, and they are not rolled back. Every migration must therefore stay compatible with the previous release (expand/contract). The `AppOffline` rule means a short period of downtime while files are replaced.

### Configuration and logs

- `secrets.json` sits in the site's content root (not in `wwwroot`, and the app never serves it). The deploy writes it. It has lower precedence than environment variables and the command line, so either of those overrides a value in the file.
- Logs are written to `logs/` on the site as rolling JSON files, kept for 7 days. IIS discards console output, so read the files there. Deploys skip that folder, so logs survive a deploy.

### Stale files and the periodic clean deploy

`DoNotDeleteRule` stops WebDeploy from deleting files on the server that are not in the new build. That protects `logs/`, but it also means old hashed Angular bundles in `wwwroot` pile up with every deploy. Now and then, clean them out in one of two ways:

- Delete the old hashed bundles (`main-*.js`, `styles-*.css` and similar) from `wwwroot` by hand in the MonsterASP file manager, then run the Deploy workflow again; or
- Run one deploy without `DoNotDeleteRule` (remove it from the `msdeploy` line in `deploy.yml` for that run). Make sure `logs` is still skipped, or the logs are deleted too.

## Rollback

There is no automatic rollback. A failed health poll fails the job and leaves the new files in place.

To go back, open Actions, choose **Deploy**, click **Run workflow** and enter the previous tag in `ref` (for example `v1.2.0`). A `v*` ref deploys to production (with the reviewer approval); any other ref, such as a commit sha, deploys to staging.

Migrations are not rolled back, so the older release has to work with the newer schema. This is why every migration follows the expand/contract rule.

## Client IPs

Rate limiting keys on the client IP. The API enables `ForwardedHeaders` (`X-Forwarded-For`, `X-Forwarded-Proto`) and trusts only the networks in `ForwardedHeaders:KnownNetworks`. With nothing configured no sender is trusted, so a client cannot choose its own rate-limit bucket by spoofing a header.

If MonsterASP has a proxy in front of IIS, every request may look like it comes from the proxy, and one person's OTP attempts would then rate-limit everyone. In that case, set the `FORWARDED_KNOWN_NETWORK` secret to the proxy's CIDR range, which is what the `ForwardedHeaders__KnownNetworks__0` setting is filled from. Ask MonsterASP support (see setup step 1).

## Content Security Policy

The API sends `default-src 'self'; font-src https://fonts.gstatic.com; style-src 'self' https://fonts.googleapis.com 'unsafe-inline'; img-src 'self' blob:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`. It matches what the app loads: IBM Plex and Material Icons from Google Fonts, photos from `/api/files/...` (same origin), and the API on the same origin.

One build setting depends on it: Angular's critical-CSS inlining rewrites the stylesheet link to `media="print" onload="this.media='all'"`. An inline event handler is blocked by this CSP, so the page would render unstyled. `angular.json` therefore sets `optimization.styles.inlineCritical` to `false` for the production build. Keep that setting, or add a nonce-based `script-src`, if the CSP changes.

## Backups and restore

`.github/workflows/backup.yml` runs every night at 01:30 UTC for both environments (and on demand with **Run workflow**). For each environment it exports the SQL Server database as a `.bacpac` with `sqlpackage` and uploads it with rclone to `db/gameya-<timestamp>.bacpac` in that environment's backup bucket. Uploads use `copyto`, so nothing already in the bucket is deleted. Uploaded photos already live in the files bucket (versioned, 35-day lifecycle), so they need no separate copy. If `BACKUP_PING_URL` is set, a successful run pings it.

- **Scheduled runs only start from the default branch.** The nightly backup does nothing until this work is merged to `main`.
- **The production approval gate.** If the `production` Environment has a required reviewer, the nightly production backup waits for approval every night. Decide whether to approve each one, or to give backups a separate Environment without the reviewer (the secrets would have to be copied to it, and `backup.yml` pointed at it). Nothing is set up for this yet.
- **Requirements.** The MonsterASP SQL host must accept connections from GitHub runners, and the login in `DB_CONNECTION_STRING` needs permission to export. The export of a live database is not transactionally consistent, which is fine for a nightly backup.
- **Retention** is the bucket's 35-day lifecycle rule. Set it yourself (setup step 3).

To restore, download the `.bacpac` from the backup bucket and import it into an empty or scratch database:

```
sqlpackage /a:Import /tcs:"<target connection string>" /sf:gameya-<timestamp>.bacpac
```

Check the result: `__EFMigrationsHistory` should list `Initial` and `ActivityLogAppendOnlyTrigger`. To restore over the live database, stop the site first (take it offline in the panel) and import into an empty database, then switch `DB_CONNECTION_STRING` to it.

## Known limits

- **Idle shutdown.** The host stops an idle site, and Hangfire does not run until the next request wakes it. The UptimeRobot monitor reduces this but does not remove it.
- **Disk quota.** The site's disk is limited. Files go to B2, but logs (7 days) and stale bundles (see above) use the quota.
- **Shared CPU.** Shared hosting can be slow under load. Photo processing (HEIC conversion) is the heaviest work.
- **One database login.** `ConnectionStrings:App` and `:Migrator` hold the same value here.

## Later

When a domain is bought, put Cloudflare in front: move DNS to Cloudflare with the proxy on, then set `FORWARDED_KNOWN_NETWORK` to Cloudflare's ranges so the real client address is read (check which header the API honours first), and add SPF/DKIM records for the Brevo sender domain.

## First-deploy checklist (AC-F8)

AC-F8 needs the setup above and is verified by the owner, not by CI.

1. Merge to `main`. The Deploy workflow is green and `https://<staging-site>/health` returns `Healthy` with the `database`, `files` and `hangfire` entries.
2. Upload a HEIC photo on staging (the OTP is delivered by SMS Gate). It must work and the file must land in the B2 files bucket. This also proves B2 accepts the S3 client's settings.
3. Run the Backup workflow by hand. A `.bacpac` appears under `db/` in the backup bucket, and a restore into a scratch database with `sqlpackage /a:Import` passes.
4. Request OTPs from two different networks (for example Wi-Fi and mobile data). They must be rate-limited separately. If they share one limit, a proxy sits in front of IIS: set `FORWARDED_KNOWN_NETWORK`.

Also confirm once, on staging:

- msdeploy authenticates (Basic, port 8172), `logs/` survives a second deploy, and `secrets.json` is on the server but not in the workflow artifact.
- `GET /secrets.json` and `GET /appsettings.json` return 404.
- A rollback run (Run workflow with a previous tag or sha) works, and a `v*` tag push stops at the production approval gate.
- `ASPNETCORE_ENVIRONMENT` is picked up from `web.config` (the app behaves as Staging).
