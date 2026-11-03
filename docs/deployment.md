# Deployment

Gam'eya runs on one Oracle Cloud Always Free Ampere A1 VM per environment (2 OCPU, 12 GB, Ubuntu 24.04 aarch64). Hosting cost is $0. Docker Compose runs three containers: `postgres`, `api` and `caddy`. Caddy terminates TLS (Let's Encrypt), serves the Angular build and proxies `/api/*`, `/health` and `/hubs/*` to the API.

| Branch or tag | Environment  | Trigger                                                           |
| ------------- | ------------ | ----------------------------------------------------------------- |
| `main`        | `staging`    | `.github/workflows/deploy.yml` on every push to `main`            |
| `v*` tag      | `production` | the same workflow, after the GitHub Environment reviewer approves |

## One-time owner setup

Do this once per environment. Until it is done, the deploy workflow cannot succeed and acceptance criterion AC-F8 stays pending.

1. **Oracle Cloud account.** Sign up for Oracle Cloud Free Tier. A card is needed for identity verification only. The home region cannot be changed later, so pick it with the legal open item in mind.
2. **Two A1 VMs (staging and production).** Shape `VM.Standard.A1.Flex`, 2 OCPU, 12 GB, Ubuntu 24.04. If you get "out of host capacity", retry later or try another availability domain. In the VCN security list, allow ingress TCP 80 and 443 from `0.0.0.0/0` (and 22 for SSH, ideally from your own IP).
3. **Bootstrap each VM.** Generate a deploy key pair (`ssh-keygen -t ed25519 -f gameya-deploy -C gameya-deploy`), copy the `infra/` directory to the VM, and run it once as root:
   ```bash
   scp -r infra ubuntu@<vm-ip>:~/infra
   ssh ubuntu@<vm-ip> 'sudo bash ~/infra/bootstrap-vm.sh "$(cat)"' < gameya-deploy.pub
   ```
   The script is idempotent. It installs Docker Engine and the compose plugin, rclone and unattended-upgrades, creates the `deploy` user (in the `docker` group) with your public key, sets `PasswordAuthentication no` and `PermitRootLogin no`, opens 80/443 in the image's iptables rules and persists them, creates `/opt/gameya`, and enables the `gameya-backup.timer`. Capture the VM's host key for the `DEPLOY_KNOWN_HOSTS` secret: `ssh-keyscan -t ed25519 <vm-ip>`.
4. **DuckDNS.** Create a free subdomain per environment (for example `gameya-staging.duckdns.org`) pointing at the VM's public IP. This is `SITE_ADDRESS`.
5. **Brevo.** Create an account and verify the sender address or domain. Use the SMTP credentials as `SMTP_USERNAME` / `SMTP_PASSWORD`. The API always uses STARTTLS on port 587 (`smtp-relay.brevo.com`). Use a plain display name in `EMAIL_FROM` (no apostrophe), for example `Gameya <noreply@example.com>`.
6. **Backblaze B2.** Create a bucket per environment with a lifecycle rule that keeps files for 35 days, and an application key limited to that bucket. These are `B2_KEY_ID`, `B2_APP_KEY` and `B2_BUCKET`.
7. **GitHub Environments.** Create `staging` and `production` (add a required reviewer on `production`) and set the secrets below on each.
8. **Optional monitoring.** An UptimeRobot monitor on `https://<SITE_ADDRESS>/health`, and a healthchecks.io check whose ping URL goes in `BACKUP_PING_URL`.
9. **SMS.**
   - **Staging and demo/pilot production** use `SmsGate`. Install SMS Gate on a spare Android phone with a SIM that has an SMS bundle, enable cloud mode (or point it at a self-hosted server), turn on end-to-end encryption if available, copy the username and password into `SMSGATE_USERNAME` / `SMSGATE_PASSWORD`, raise Android's SMS send limit with `adb shell settings put global sms_outgoing_check_max_count <n>`, and keep the phone charging on Wi-Fi.
   - The brief allowed `Logging` on staging, but the API refuses `Sms:Provider=Logging` outside Development and E2E (the codes would end up in logs), so staging must use `SmsGate` too.
   - **Before public launch**, switch to a paid provider (Cequens or SMS Misr). Its adapter `ProviderSmsSender` is implemented in a follow-up task once the contract exists; `SMS_API_KEY` is reserved for it.
   - Production refuses to start with `Logging`.
10. **Later, when a domain is bought.** Move DNS to Cloudflare with the proxy on and SSL "Full (strict)", install a Cloudflare Origin CA certificate in Caddy, limit 80/443 to Cloudflare's IP ranges, configure Caddy's `trusted_proxies` and `client_ip_headers` (see "Client IPs" below), and add SPF/DKIM records for the Brevo sender domain.

### GitHub Environment secrets

Values must not contain a single quote or a newline (the workflow rejects them). Database passwords are also quoted into psql, so keep them to letters, digits and `-_.~`.

| Secret                                                               | Purpose                                                                     |
| -------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| `DEPLOY_HOST`                                                        | VM public IP or DNS name                                                    |
| `DEPLOY_SSH_KEY`                                                     | private half of the deploy key                                              |
| `DEPLOY_KNOWN_HOSTS`                                                 | pinned host key line (`ssh-keyscan` output). Host key checking is never off |
| `SITE_ADDRESS`                                                       | DuckDNS host name, no scheme                                                |
| `POSTGRES_PASSWORD`                                                  | postgres superuser                                                          |
| `DB_MIGRATOR_PASSWORD`, `DB_APP_PASSWORD`                            | passwords for `gameya_migrator` and `gameya_app` (set by `roles.sql`)       |
| `JWT_SIGNING_KEY`                                                    | at least 32 characters                                                      |
| `SECURITY_HASH_KEY`                                                  | HMAC key for OTP and token hashes                                           |
| `STORAGE_URL_SIGNING_KEY`                                            | at least 32 bytes                                                           |
| `SMTP_USERNAME`, `SMTP_PASSWORD`                                     | Brevo SMTP credentials                                                      |
| `EMAIL_FROM`                                                         | sender, for example `Gameya <noreply@example.com>`                          |
| `SMS_PROVIDER`                                                       | `SmsGate` (default when empty)                                              |
| `SMSGATE_USERNAME`, `SMSGATE_PASSWORD`                               | SMS Gate credentials. Optional `SMSGATE_BASE_URL` for a self-hosted server  |
| `SMS_API_KEY`                                                        | reserved for the paid provider adapter (not read by the API yet)            |
| `MEDIATR_LICENSE_KEY`                                                | MediatR Community license key                                               |
| `B2_KEY_ID`, `B2_APP_KEY`, `B2_BUCKET`                               | backup upload                                                               |
| optional `BACKUP_PING_URL`                                           | healthchecks.io ping, called after a successful backup                      |
| optional `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS` | OpenTelemetry export                                                        |

### Branch protection

Protect `main` so a merge needs these status checks from `ci.yml` (names as in the workflow): **backend**, **frontend**, **e2e**, **commits** and **image**.

## What a deploy does

`deploy.yml` builds the linux/arm64 API image (`docker buildx`, no emulation: the Dockerfile cross-publishes), the self-contained linux-arm64 `efbundle`, and the Angular production build. It uploads the web build, `efbundle` and `roles.sql` to `/opt/gameya/releases/<sha>/` and the `infra/` runtime files to `/opt/gameya/` (the Caddyfile to `/opt/gameya/caddy/`) over SSH with the pinned host key, loads the image, writes `/opt/gameya/.env` (mode 600) through stdin, and runs `deploy-release.sh <sha>`. The job result is that script's exit code.

On the VM, `deploy-release.sh`:

1. starts `postgres` and `caddy` and reloads `/opt/gameya/caddy/Caddyfile` into the running Caddy (an invalid Caddyfile fails the deploy before anything changes);
2. applies `roles.sql` (idempotent roles and grants; passwords go over stdin);
3. runs the `efbundle` as `gameya_migrator` in a `runtime-deps` container on the compose network;
4. records the previous `API_TAG` and the `releases/current` target, writes the new `API_TAG` to `image.env`, restarts `api` and flips `releases/current` (a relative symlink) to the new release;
5. polls `https://<SITE_ADDRESS>/health` for up to 5 minutes until it contains `Healthy`;
6. on failure, restores the previous tag and release, restarts `api` and exits 1;
7. prunes releases and `gameya-api` images beyond the newest 3.

Migrations are not rolled back, so every migration must stay compatible with the previous release (expand/contract). The deploy restarts the API in place, which means a few seconds of downtime.

To roll back by hand, run `/opt/gameya/deploy-release.sh <older-sha>` (its release directory and image are kept for the last 3 deploys).

## Client IPs

Rate limiting keys on the client IP. The API enables `ForwardedHeaders` (`X-Forwarded-For`, `X-Forwarded-Proto`) and trusts only the networks in `ForwardedHeaders:KnownNetworks`. `compose.prod.yml` pins the compose network to `172.30.0.0/24` and passes that range to the API, so only Caddy can set the headers; a spoofed `X-Forwarded-For` from anywhere else is ignored. With nothing configured no sender is trusted. If you change the subnet in the compose file, change `ForwardedHeaders__KnownNetworks__0` with it.

When Cloudflare is turned on later, Caddy is no longer the first hop, so add this to the Caddyfile's global options so Caddy trusts Cloudflare and reads the real client address from `CF-Connecting-IP`:

```
{
	servers {
		trusted_proxies static <Cloudflare IPv4 and IPv6 ranges>
		client_ip_headers CF-Connecting-IP
	}
}
```

Caddy then sets `X-Forwarded-For` to that client IP for the API.

## Content Security Policy

Caddy sends `default-src 'self'; font-src https://fonts.gstatic.com; style-src 'self' https://fonts.googleapis.com 'unsafe-inline'; img-src 'self' blob:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`. It matches what the app loads: IBM Plex and Material Icons from Google Fonts, photos from `/api/files/...` (same origin), and the API on the same origin.

One build setting depends on it: Angular's critical-CSS inlining rewrites the stylesheet link to `media="print" onload="this.media='all'"`. An inline event handler is blocked by this CSP, so the page would render unstyled. `angular.json` therefore sets `optimization.styles.inlineCritical` to `false` for the production build. Keep that setting, or add a nonce-based `script-src`, if the CSP changes.

## Backups and restore

`gameya-backup.timer` runs `backup.sh` daily at 01:30 UTC (`Persistent=true`, so a missed run happens at the next boot). It dumps the database (`pg_dump -Fc`), archives the `files` volume, uploads both to `b2:<bucket>/{db,files}/` with rclone (configured by `RCLONE_CONFIG_B2_*` environment variables, no config file), keeps 3 days locally and pings `BACKUP_PING_URL`. The bucket's lifecycle rule enforces 35-day retention.

- Run a backup now: `sudo systemctl start gameya-backup` (follow it with `journalctl -u gameya-backup`).
- Check the latest backup without touching live data: `/opt/gameya/restore.sh latest --verify-only`. It restores into a throwaway `postgres:17` container and checks that `__EFMigrationsHistory` and `users` are readable.
- Restore for real: `/opt/gameya/restore.sh latest` (or a timestamp such as `20261103T013000Z`). It asks for confirmation, stops `api`, runs `pg_restore --clean --if-exists`, restores the files volume and starts `api`.

## First-deploy checklist (AC-F8)

AC-F8 needs the setup above and is verified by the owner, not by CI.

1. Merge to `main`: the Deploy workflow is green, `https://<staging-host>/health` returns `Healthy`, and `__EFMigrationsHistory` lists `Initial` (`docker compose -p gameya exec postgres psql -U postgres -d gameya -c 'select * from "__EFMigrationsHistory"'`).
2. Upload a HEIC photo on staging (the OTP is delivered by SMS Gate) to confirm Magick.NET works on ARM64.
3. `sudo systemctl start gameya-backup`, then `/opt/gameya/restore.sh latest --verify-only` passes.
