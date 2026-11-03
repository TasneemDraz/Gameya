# Gam'eya: Sub-project 1, Foundation (Design)

- **Date:** 2026-11-03
- **Status:** Draft, awaiting review
- **Source requirements:** `docs/requirements.md` (v1)

---

## 1. Context

Gam'eya is a mobile-first, Arabic/English web app for managing Egyptian rotating savings circles. It tracks and coordinates money movements but never holds money. It is intended for a **real public launch in Egypt**. The full v1 scope in the requirements document ships before the public launch (no early pilot).

v1 is built as five sequential sub-projects, each with its own spec, plan, and implementation cycle:

1. **Foundation** (this document): solution skeleton, i18n/RTL shell, phone OTP auth and profile, activity log infrastructure, developer workflow, CI/CD and Azure deployment.
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
| Database | PostgreSQL: Azure Database for PostgreSQL Flexible Server, Npgsql provider |
| Money | `numeric(18,2)` in DB, `decimal` in code, currency code stored alongside |
| Time | All timestamps UTC `timestamptz`. Business dates computed in `Africa/Cairo` via `IClock` + `CairoCalendar` |
| Background jobs | Hangfire with `Hangfire.PostgreSql`, same database server, running in the API process |
| File storage | Azure Blob Storage, private containers, short-lived (~5 min) SAS URLs |
| Real-time | SignalR, running inside the API (needed from sub-project 2) |
| SMS (OTP) | `ISmsSender`. Dev: logging sender. Prod: Egyptian provider adapter (Cequens or SMS Misr, chosen at production setup), Twilio Verify as fallback |
| Email | Azure Communication Services Email behind `IEmailSender` |
| Frontend | Angular (latest stable at scaffold time), standalone components, signals, Angular Material + CDK, Transloco |
| Hosting | API: Azure App Service (Linux container). Web: Azure Static Web Apps. Region: UAE North |
| IaC / CI | Bicep, GitHub Actions, GitHub OIDC to Azure |
| Observability | Serilog + OpenTelemetry → Application Insights |

## 4. Design

### 4.1 Solution structure

```
Gameya/
├─ package.json                  repo tooling only (husky, lint-staged, commitlint)
├─ .husky/
├─ src/
│  ├─ Gameya.Domain/             entities, value objects, domain rules, domain events (no dependencies)
│  ├─ Gameya.Application/        commands/queries, validators, pipeline behaviors, ports (ISmsSender, IEmailSender, IClock, IFileStore…)
│  ├─ Gameya.Infrastructure/     EF Core/Npgsql, Hangfire, Blob, SMS/email adapters, JWT/token services
│  ├─ Gameya.Api/                controllers, auth setup, ProblemDetails mapping, SignalR hubs, health checks
│  └─ gameya-web/                Angular app
├─ tests/
│  ├─ Gameya.Domain.Tests/
│  ├─ Gameya.Application.Tests/
│  └─ Gameya.Api.IntegrationTests/   WebApplicationFactory + Testcontainers PostgreSQL
├─ infra/                        Bicep templates
├─ docker-compose.yml            api, postgres, azurite, mailpit
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

**Tokens (FR-AUTH-03):**
- **Access token:** JWT, 10-minute lifetime, kept in memory in the Angular app only. Claims: user id, roles, preferred language.
- **Refresh token:** 256-bit random value, stored hashed with a family id. Sent as an `HttpOnly; Secure; SameSite=Strict` cookie scoped to the API path. API and web share a parent domain (`api.<domain>`, `app.<domain>`).
- **Rotation:** every refresh issues a new refresh token and revokes the old one. Reuse of a revoked token revokes the whole family (theft detection).
- **Expiry:** sliding 30 days of inactivity. Each refresh extends it.
- **Log out:** revokes the current family. **Log out everywhere:** revokes all of the user's refresh tokens.
- **Blocked users:** a `IsBlocked` flag is checked on refresh and login, and blocking revokes all refresh tokens. Access is fully lost within ≤10 minutes. The admin action that sets the flag ships in sub-project 5.

### 4.3 Profile, email verification, deletion, privacy

**Profile (FR-AUTH-02):** full name (2–60 chars, required), verified phone, optional email, optional photo, preferred language (`ar` default, `en`). Photos: JPG/PNG/HEIC, max 5 MB, resized server-side to 512 px, stored in a private Blob container and served through short-lived SAS URLs.

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
- **Local:** `docker compose up` (API, Postgres, Azurite, Mailpit) plus `ng serve` with a proxy.
- **Staging:** auto-deploy on merge to `main`.
- **Production:** deploy from a release tag with manual approval (GitHub Environments).

**Azure (Bicep in `infra/`):** App Service (Linux, with a staging slot), PostgreSQL Flexible Server (point-in-time restore retention 35 days), Storage account (Blob), Static Web Apps, Communication Services Email, Key Vault, Container Registry, Application Insights. Region: UAE North. The app reads secrets from Key Vault via managed identity. GitHub authenticates to Azure via OIDC (no stored credentials).

**CI (every PR):**
1. Backend: restore, build, `dotnet format --verify-no-changes`, domain + application tests.
2. Integration tests on Testcontainers PostgreSQL.
3. Frontend: lint, Stylelint, translation-key parity, unit tests, production build.
4. Commitlint on PR commits.
5. Docker image build + vulnerability scan.

Any failure blocks the merge.

**Deploy:**
1. Push the image.
2. Run the EF Core migration bundle as its own step, under the migration role. The app never migrates on startup.
3. Deploy to the staging slot.
4. Check `/health`.
5. Swap the slot.
6. Deploy the Angular app.

**Operations:**
- Serilog structured logs and OpenTelemetry traces to Application Insights, with a correlation id per request.
- `/health` checks database, Blob, and Hangfire.
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

- **Legal:** check Egypt's Personal Data Protection Law (Law 151/2020) on cross-border storage (hosting in UAE North) before public launch. The region is a Bicep parameter and can change.
- **SMS provider contract:** choose between Cequens and SMS Misr, and register a sender ID, before production. Only the adapter depends on this.
- **Domain name:** needed for the shared-parent-domain cookie setup in staging and production.
