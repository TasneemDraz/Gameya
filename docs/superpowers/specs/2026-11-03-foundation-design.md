# Gam'eya: Sub-project 1, Foundation (Design)

- **Date:** 2026-11-03
- **Status:** Draft, awaiting review
- **Source requirements:** `docs/requirements.md` (v1)

---

## 1. Context

Gam'eya is a mobile-first, Arabic/English web app for managing Egyptian rotating savings circles. It tracks and coordinates money movements but never holds money. It is intended for a **real public launch in Egypt**. The full v1 scope in the requirements document ships before the public launch (no early pilot).

v1 is built as five sequential sub-projects, each with its own spec, plan, and implementation cycle:

1. **Foundation** (this document): solution skeleton, i18n/RTL shell, phone OTP auth and profile, activity log infrastructure, developer workflow, CI/CD and free-tier deployment.
2. **Gam'eya setup:** create and edit gam'eyas, Draft → Recruiting, invites and joining, members, payout order (manual, auditable random draw, by request).
3. **Running a gam'eya:** start, rounds and contributions, payment submission and confirmation with proofs, payouts and disputes, overdue and auto-close jobs, completion and cancellation.
4. **Coordination:** notification engine (in-app and email, extensible channels), reminders, swaps, announcements.
5. **Visibility:** dashboards, round grid, reports and PDF/Excel export, platform admin.

**Working model:** Claude implements most of the code. The owner (an experienced .NET/Angular developer) reviews the results at the end of each sub-project.

## 2. Goals and non-goals

### Goals
- A deployable, tested skeleton that all later sub-projects build on without restructuring.
- Complete, production-grade auth: FR-AUTH-01 through FR-AUTH-05, plus email verification.
- An append-only activity log that later sub-projects only need to emit events into.
- Arabic (default, RTL) and English working end to end from the first screen.
- Local dev in one command. CI on every PR. Staging deploy on merge. Gated production deploy.

### Non-goals (later sub-projects)
- Any gam'eya, membership, payment, or notification-engine features.
- The real implementation of "is this user in a Recruiting/Active gam'eya" (a stub is provided here, see §4.3).
- Platform admin UI. Foundation only defines the `PlatformAdmin` role and blocking hook.
- PWA install support.

## 3. Technical decisions

| Concern | Decision |
|---|---|
| Architecture | Modular monolith: Clean Architecture layers, feature folders per module inside each layer |
| Backend | ASP.NET Core (current LTS), MediatR (CQRS), FluentValidation, EF Core |
| Data access | Specification pattern (Ardalis.Specification). Named specs for every lookup and DTO projection (`Query.Select`, translated to SQL). No mapping libraries (no AutoMapper) |
| Database | PostgreSQL 17 in a Docker container on the app VM (volume-backed, not exposed to the internet), Npgsql provider |
| Money | `numeric(18,2)` in DB, `decimal` in code, currency code stored alongside |
| Time | All timestamps UTC `timestamptz`. Business dates computed in `Africa/Cairo` via `IClock` + `CairoCalendar` |
| Background jobs | Hangfire with `Hangfire.PostgreSql`, same database server, running in the API process |
| File storage | `IFileStore` on a local Docker volume. Files are served by the API through short-lived (~5 min) HMAC-signed URLs. An S3-compatible store can replace it later without touching handlers |
| Backups | Nightly `pg_dump` plus the file volume, pushed off-site to Backblaze B2 (free 10 GB) with 35-day retention. A restore script is tested |
| Real-time | SignalR, running inside the API (needed from sub-project 2) |
| SMS (OTP) | `ISmsSender`, selected by `Sms:Provider`, in three stages: **(1) Dev/test:** `Logging` writes the code to the log ($0). **(2) Demo/pilot:** `SmsGate`, an Android phone running the open-source SMS Gate app with the owner's SIM, called over HTTP (costs only the SIM's SMS bundle). **(3) Public launch:** a paid provider adapter (Cequens or SMS Misr, Twilio as fallback) or WhatsApp authentication messages to cut cost. Only the adapter changes between stages |
| Email | SMTP (MailKit) behind `IEmailSender`. Dev: Mailpit. Prod: Brevo free SMTP relay (~300/day), verified single sender until a domain exists |
| Frontend | Angular (latest stable at scaffold time), standalone components, signals, Angular Material + CDK, Transloco |
| Hosting | Oracle Cloud Always Free: one Ampere A1 (ARM64) VM per environment (staging, prod), 2 OCPU / 12 GB each, Ubuntu + Docker Compose. Caddy serves the Angular build and proxies `/api` on the same origin, with automatic Let's Encrypt TLS. Hostname: free DuckDNS subdomain until a domain is bought, then Cloudflare (free plan) in front |
| IaC / CI | Docker Compose + shell scripts in `infra/`, GitHub Actions, deploy over SSH. Secrets in GitHub Environments |
| Observability | Serilog JSON to stdout (Docker log rotation) + OpenTelemetry OTLP exporter, enabled when an endpoint is configured (for example Grafana Cloud free). Free external uptime check on `/health` |

## 4. Design

### 4.1 Solution structure

```
Gameya/
├─ package.json                  repo tooling only (husky, lint-staged, commitlint)
├─ .husky/
├─ src/
│  ├─ Gameya.Domain/             entities, value objects, domain rules, domain events (no dependencies)
│  ├─ Gameya.Application/        commands/queries, validators, pipeline behaviors, ports (ISmsSender, IEmailSender, IClock, IFileStore…)
│  ├─ Gameya.Infrastructure/     EF Core/Npgsql, Hangfire, file store, SMS/email adapters, JWT/token services
│  ├─ Gameya.Api/                controllers, auth setup, ProblemDetails mapping, SignalR hubs, health checks
│  └─ gameya-web/                Angular app
├─ tests/
│  ├─ Gameya.Domain.Tests/
│  ├─ Gameya.Application.Tests/
│  └─ Gameya.Api.IntegrationTests/   WebApplicationFactory + Testcontainers PostgreSQL
├─ infra/                        production compose, Caddyfile, VM bootstrap, backup/restore scripts, SQL roles
├─ docker-compose.yml            api, postgres, mailpit (local dev)
└─ .github/workflows/
```

**Module rule:** each layer has feature folders (`Auth/`, `Users/`, `Activity/`, and later `Gameyas/`, `Payments/`, `Notifications/`). Modules interact only through Application interfaces or domain events. Handlers never call each other.

**MediatR pipeline order:**
1. Validation (FluentValidation). Failures return 400 with field errors.
2. Authorization. Each request declares its requirement (authenticated, role, or a later gam'eya-membership check). It is evaluated server-side before the handler runs.
3. Unit of work / transaction.
4. Activity log writer (§4.4), which runs inside the same transaction.

**Errors:** domain rule violations throw typed `DomainException`s carrying a stable error code (for example `OTP_LOCKED`, `EMAIL_NOT_VERIFIED`). The API maps them to RFC 7807 ProblemDetails with `code` and optional `params`. The server never returns user-facing text. The client translates the code.

**Clock:** no direct `DateTime.Now`/`UtcNow` anywhere. `IClock` is injected. `CairoCalendar` converts between UTC and Cairo dates and implements the "last day of month when the day doesn't exist" rule (FR-GAM-02) for reuse in sub-project 3.

### 4.2 Authentication and sessions

**Phone numbers (FR-AUTH-01):** `PhoneNumber` value object. Accepts `01XXXXXXXXX` (11 digits, prefixes 010/011/012/015), with Western or Arabic-Indic digits. Normalizes to E.164 `+201XXXXXXXXX`. Unique per user.

**OTP flow:**
1. `POST /auth/otp/request {phone}` creates or reuses an `OtpChallenge` and sends a 6-digit code.
2. `POST /auth/otp/verify {phone, code}`. On success: if the user doesn't exist, it is created with `ProfileIncomplete = true`. Tokens are issued.
3. If the profile is incomplete, the client routes to name setup before anything else.

`OtpChallenge`: phone, code hash (never plaintext), expires at (+5 min), failed attempts, send count, window start, last sent at, locked until.

**Domain rules (unit tested):**
- Code valid for 5 minutes. A successful verification consumes it.
- 5 wrong attempts lock the number for 15 minutes, and requests and verifies are refused during the lock.
- Resend allowed only 60 s after the previous send. Max 3 resends per 15-minute window.
- ASP.NET rate limiter also applies per-IP limits on `/auth/otp/*`.
- Dev environment: `LoggingSmsSender` writes the code to the log.
- Demo/pilot: `SmsGateSmsSender` sends through the Android phone gateway.
- If the SMS can't be sent, the request fails with `SMS_SEND_FAILED` (503). The transaction rolls back, so the failed attempt doesn't count against the user's resend limit.

**Tokens (FR-AUTH-03):**
- **Access token:** JWT, 10-minute lifetime, kept in memory in the Angular app only. Claims: user id, roles, preferred language.
- **Refresh token:** 256-bit random value, stored hashed with a family id. Sent as an `HttpOnly; Secure; SameSite=Strict` cookie scoped to the API path. The web app and API are served from one origin (Caddy routes `/api` to the API), so no shared parent domain is needed.
- **Rotation:** every refresh issues a new refresh token and revokes the old one. Reuse of a revoked token revokes the whole family (theft detection).
- **Expiry:** sliding 30 days of inactivity. Each refresh extends it.
- **Log out:** revokes the current family. **Log out everywhere:** revokes all of the user's refresh tokens.
- **Blocked users:** a `IsBlocked` flag is checked on refresh and login, and blocking revokes all refresh tokens. Access is fully lost within ≤10 minutes. The admin action that sets the flag ships in sub-project 5.

### 4.3 Profile, email verification, deletion, privacy

**Profile (FR-AUTH-02):** full name (2–60 chars, required), verified phone, optional email, optional photo, preferred language (`ar` default, `en`). Photos: JPG/PNG/HEIC, max 5 MB, resized server-side to 512 px, stored in the private file store and served through short-lived signed URLs.

**Email verification (added requirement, approved):**
- Setting or changing an email stores it as **unverified** and sends a verification link (single-use token, stored hashed, valid 24 hours).
- No notification email is ever sent to an unverified address. `IEmailSender` callers must go through a `VerifiedEmailResolver`, which returns nothing for unverified emails.
- The profile shows "Unverified" with a "Resend link" action (rate-limited: max 3 per hour).
- Changing a verified email keeps the old one active until the new one is verified.

**Account deletion (FR-AUTH-04):**
- Guarded by `IActiveParticipationChecker.HasRecruitingOrActiveGameyas(userId)`. Foundation ships a stub returning `false`, and sub-project 2 replaces it.
- Deletion soft-deletes the user, revokes all tokens, clears phone/email/photo, and sets the display name to the localized "Deleted user". Historical records keep their foreign keys.
- A deleted user's phone number may sign up again as a brand-new user.

**Phone masking (FR-AUTH-05):** a single helper, `PhoneDisplay.For(viewer, subject, context)`, returns the masked form `010****4567` unless the viewer is the subject or the organizer of the relevant gam'eya. All query projections that expose phones must use it. An architecture test asserts that no DTO exposes raw `PhoneNumber` outside this helper.

### 4.4 Activity log infrastructure (FR-LOG)

**Table `activity_log`:** `id` (bigint identity), `gameya_id` (nullable, null for account-level events), `occurred_at` (UTC), `actor_user_id` (nullable for system jobs), `actor_role`, `action` (stable code, e.g. `PROFILE_UPDATED`), `target_type`, `target_id`, `details` (`jsonb`: old/new values, seeds, etc.), `note` (nullable).

**Write path:** handlers raise domain events. The pipeline's activity-log behavior maps events to rows and saves them **in the same transaction** as the state change.

**Immutability (FR-LOG-03), enforced in two layers:**
1. The database role used by the application has only `INSERT` and `SELECT` on `activity_log`. `UPDATE`, `DELETE`, and `TRUNCATE` are revoked. Migrations run under a separate migration role.
2. An EF Core `SaveChanges` interceptor throws if any `ActivityLogEntry` is in the Modified or Deleted state.

**Rendering:** the API returns codes and details. The Angular app renders the sentence from translation templates in the viewer's language.

**Foundation emits:** `USER_REGISTERED`, `PROFILE_UPDATED`, `EMAIL_VERIFIED`, `LOGGED_OUT_ALL`, `ACCOUNT_DELETED` (all with `gameya_id` null).

### 4.5 Angular app shell

- **Structure:** standalone components, signals, lazy-loaded feature routes (`auth`, `profile`, and later `gameyas`, …).
- **UI:** Angular Material + CDK with a custom theme. Arabic font IBM Plex Sans Arabic, plus a matching Latin font.
- **i18n:** Transloco with runtime language switching. `ar.json` and `en.json` are the only text sources. A CI script fails if the two files' key sets differ.
- **RTL:** `dir`/`lang` set on `<html>` from the active language. Styles use CSS logical properties only, enforced by a Stylelint rule that disallows physical `left`/`right` margin, padding, and positioning.
- **Formatting:** `Intl` with `ar-EG`/`en-EG`, time zone `Africa/Cairo`. Arabic shows Arabic-Indic digits and "ج.م". English shows Western digits and "EGP". Numeric inputs accept both digit sets.
- **Layout:** mobile-first at 360 px. Bottom navigation on phones, side navigation at ≥ 960 px. Authenticated layout plus a public layout.
- **HTTP:**
  - An interceptor attaches the access token.
  - On 401 it performs one silent refresh (single-flight) and retries. If that fails, it routes to login.
  - It maps ProblemDetails `code` to translated messages.
- **Shared components:** `<status-chip>` (icon + text, never color alone), phone input, OTP input (auto-advance, paste support, `autocomplete="one-time-code"`), confirm dialog.
- **Screens in this sub-project:** landing, phone entry, OTP entry (with resend countdown), name setup, profile (name, photo, language, email + verification state), log out / log out everywhere, delete account, email-verification landing page.
- **Mandatory UI notice:** onboarding includes the "This app does not hold or move money" statement (§1.3 of the requirements).

### 4.6 Developer workflow (Husky)

- **Root `package.json`:** contains only repo tooling. The Angular app keeps its own `package.json`.
- **pre-commit:** `lint-staged` on staged files only:
  - `*.ts`, `*.html` → ESLint `--fix`, Prettier
  - `*.scss` → Stylelint (including the logical-properties rule)
  - `*.json`, `*.md` → Prettier
  - `*.cs` → `dotnet format --include <staged files>`
  - `ar.json`/`en.json` staged → translation-key parity check
- **commit-msg:** commitlint with Conventional Commits. A requirement ID suffix like `[FR-AUTH-01]` is encouraged, not required.
- **pre-push:** `dotnet build`, domain unit tests, Angular type-check (`tsc --noEmit`).
- **CI re-runs all of these checks.** Hooks are a convenience, and CI is the gate.

### 4.7 Environments, CI/CD, deployment

**Environments:**
- **Local:** `docker compose up` (API, Postgres, Mailpit) plus `ng serve` with a proxy.
- **Staging:** auto-deploy on merge to `main`.
- **Production:** deploy from a release tag with manual approval (GitHub Environments).

**Hosting cost: $0.** Everything runs on free tiers. The only costs are SMS for OTP (the SIM's SMS bundle during the demo stage, a paid provider at public launch) and, optionally, a domain name (~$10/year).

**Each environment's VM (Oracle Cloud Always Free, `infra/`):**
- Docker Compose services: `caddy` (TLS, static Angular files, reverse proxy for `/api` and `/health`), `api` (with in-process Hangfire), `postgres` (port not published).
- Volumes: `pgdata`, `files`, `caddy_data`.
- A host systemd timer runs `infra/backup.sh` nightly. It pushes `pg_dump -Fc` and a `files` archive to a Backblaze B2 bucket per environment, and a B2 lifecycle rule keeps 35 days.
- Hardening: only ports 22/80/443 open, SSH key-only login with a non-root `deploy` user, unattended security upgrades.
- Region: the Oracle home region chosen at sign-up (for example Dubai or Jeddah). It can't be changed later for Always Free resources.
- Secrets live in GitHub Environment secrets. The deploy job writes them to `/opt/gameya/.env` (mode 600) on the VM.
- **When a domain exists:** move DNS to Cloudflare (free), turn on the proxy with SSL "Full (strict)", give Caddy a Cloudflare Origin CA certificate, and allow 80/443 only from Cloudflare IP ranges. Only the site address and certificate settings change.

**CI (every PR):**
1. Backend: restore, build, `dotnet format --verify-no-changes`, domain + application tests.
2. Integration tests on Testcontainers PostgreSQL.
3. Frontend: lint, Stylelint, translation-key parity, unit tests, production build.
4. Commitlint on PR commits.
5. Docker image build + vulnerability scan.

Any failure blocks the merge.

**Deploy (GitHub Actions over SSH):**
1. Build the ARM64 API image (cross-compiled, no emulation), the ARM64 EF Core migration bundle, and the Angular production build.
2. Copy them to a new release folder on the VM, then `docker load` the image. No container registry is needed.
3. Apply SQL roles, then run the migration bundle as its own step under the migration role. The app never migrates on startup. Migrations must stay compatible with the previous release so a rollback works.
4. Restart `api` on the new image and point the web root at the new release. This means a few seconds of downtime, with no slot swap.
5. Poll `/health`. If it isn't healthy within 5 minutes, roll back to the previous image and web release, then fail the job.

**Operations:**
- Serilog structured logs (stdout, rotated by Docker) with a correlation id per request. OpenTelemetry traces go to an OTLP endpoint when one is configured.
- `/health` checks the database, the file store, and Hangfire.
- The Hangfire dashboard is restricted to `PlatformAdmin`.
- Phone numbers, OTP codes, tokens, and emails are never logged in clear text. Phones are masked.

## 5. Testing strategy

- **Domain tests (xUnit):** `PhoneNumber` parsing/normalization, OTP rules (expiry, 5-attempt lock, 60 s resend, 3-per-15-min cap), refresh-token rotation and reuse detection, `CairoCalendar` (31st → last day of month, correct handling of Egypt's daylight-saving transitions, which were reinstated in 2023, via the IANA `Africa/Cairo` zone), email-verification token expiry and single use.
- **Application tests:** handlers with fake `IClock`, `ISmsSender`, `IEmailSender`. Verifies authorization requirements are declared on every request (reflection-based test).
- **Integration tests (Testcontainers PostgreSQL):** full OTP login → refresh → rotate → reuse-detected flow. Log out everywhere. Account deletion anonymization. Activity log `UPDATE`/`DELETE` refused by the database role. ProblemDetails codes.
- **Architecture tests:** layer dependency rules (Domain depends on nothing, etc.). No raw phone in DTOs.
- **Frontend:** unit tests for interceptor refresh logic, OTP input, translation parity script. One smoke e2e (phone → OTP via test hook → profile) against local compose.

## 6. Acceptance criteria

- **AC-F1:** `docker compose up` followed by `ng serve` lets a developer sign up with a phone number, read the OTP from the API log, set a name, and land on the profile page in Arabic (RTL).
- **AC-F2:** Five wrong OTPs lock the number for 15 minutes. A resend within 60 s is refused with code `OTP_RESEND_TOO_SOON`.
- **AC-F3:** Reusing a rotated refresh token revokes the token family, and the next refresh fails.
- **AC-F4:** Adding an email sends a verification link (visible in Mailpit). Before it is clicked, `VerifiedEmailResolver` returns no address.
- **AC-F5:** An `UPDATE` on `activity_log` executed with the application's DB role fails at the database.
- **AC-F6:** Switching language to English flips layout to LTR and reformats numbers/currency without a reload.
- **AC-F7:** A PR with a failing test, missing translation key, physical CSS property, or non-conventional commit message cannot be merged.
- **AC-F8:** A merge to `main` deploys to staging with migrations applied and `/health` green.

## 7. Open items (not blocking implementation)

- **Legal:** check Egypt's Personal Data Protection Law (Law 151/2020) on cross-border storage (data hosted in the Oracle home region, and backups in Backblaze B2's region) before public launch.
- **SMS for public launch:** before opening to the public, switch from the phone gateway to a paid provider (Cequens or SMS Misr, with a registered sender ID) or WhatsApp authentication messages. Only the adapter depends on this.
- **Phone gateway limits (demo stage):**
  - Texts come from the owner's personal number, with no sender name.
  - The phone must stay powered on and online.
  - Android limits how many SMS an app can send (by default around 30 per 30 minutes; it can be raised with `adb`).
  - The carrier may flag bulk sending.
  - With SMS Gate's cloud relay, phone numbers and codes pass through a third-party server. Turn on the app's end-to-end encryption if available, or self-host the SMS Gate server on the VM.
- **Domain name (optional, ~$10/year):** not required, since free DuckDNS hostnames work. A domain is needed for Cloudflare in front, a professional URL, and an email sender domain with SPF/DKIM.
- **Free-tier risks:**
  - Oracle may reclaim Always Free VMs it considers idle, and ARM capacity is sometimes unavailable at sign-up. Upgrading the account to Pay-As-You-Go keeps Always Free resources at $0 and removes idle reclamation, but it needs a card on file.
  - A single VM per environment has no high availability. If it's lost, restore from B2 backups onto a new VM using `infra/bootstrap-vm.sh` and `infra/restore.sh`.
  - Free-tier limits (Brevo daily sends, B2 storage) must be watched as usage grows.
