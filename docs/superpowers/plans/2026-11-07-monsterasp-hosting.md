# MonsterASP.NET Hosting Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Gam'eya from one Oracle Cloud VM per environment (Docker Compose, PostgreSQL, Caddy) to one MonsterASP.NET site per environment (IIS, SQL Server). The API, the Angular build and Hangfire run in one process. Photos and backups go to Backblaze B2.

**Architecture:** The API is published self-contained for `win-x64` and runs in-process under IIS. It serves the Angular build from `wwwroot` on the same origin, so the relative `/api` calls and the `SameSite=Strict` refresh cookie keep working unchanged. EF Core and Hangfire move from Npgsql to SQL Server. Files move behind the existing `IFileStore` to an S3-compatible store (B2), still read through the API's signed `/api/files/...` URLs. GitHub Actions runs migrations from the runner, deploys with WebDeploy, and does a nightly `sqlpackage` export to B2.

**Tech Stack:** .NET 10, EF Core 10 + `Microsoft.EntityFrameworkCore.SqlServer`, `Hangfire.SqlServer`, `AWSSDK.S3`, `Serilog.Sinks.File`, Testcontainers (`Testcontainers.MsSql`, `Testcontainers.Minio`), IIS AspNetCoreModuleV2, Web Deploy (`msdeploy`), `sqlpackage`, rclone, Backblaze B2.

**Spec:** No separate design doc. The decisions were made in conversation on 2026-11-07 and are recorded in "Decisions" below. The current setup is described in `docs/deployment.md` and `docs/superpowers/specs/2026-11-03-foundation-design.md`. Executors read both.

## Decisions

1. **One MonsterASP site per environment** (`staging`, `production`). `main` deploys to staging. A `v*` tag deploys to production after the GitHub Environment reviewer approves, as today.
2. **Frontend:** same site, from `wwwroot`. Not Cloudflare Pages or Netlify: a second origin would turn the refresh cookie into a third-party cookie.
3. **Database:** SQL Server (MonsterASP's). The three existing migrations are deleted and regenerated as one SQL Server `Initial`, because no environment has been deployed yet (AC-F8 is still pending). If any environment holds real data when this starts, stop and ask.
4. **One database login.** Shared hosting gives one login, so `ConnectionStrings:Migrator` and `ConnectionStrings:App` may hold the same value. Both keys stay, so a host with two logins can still separate them. The `REVOKE` on `activity_log` is replaced by a trigger, which holds whatever login connects.
5. **Hangfire stays in-process** (`Hangfire.SqlServer`, schema `hangfire`). No recurring jobs exist yet, so there is no trigger endpoint (YAGNI). Keep-alive is an UptimeRobot monitor on `/health` every 5 minutes (owner setup, no code). It is not a GitHub cron, because per-run minimum billing would use up a private repo's free Actions minutes.
6. **Files:** `Storage:Provider` = `Local` (default; Development, E2E and tests) or `S3` (staging and production, pointed at B2). Reads still stream through `GET /api/files/{container}/{**key}`, so the CSP `img-src 'self'` does not change.
7. **Secrets** live in `secrets.json` in the content root (not `wwwroot`). The deploy job writes it from GitHub Environment secrets with real JSON encoding, which removes today's "no single quote or newline" rule.
8. **Logs:** Serilog compact JSON to a rolling file under `logs/` in the content root, because IIS discards stdout. The console sink stays.
9. **Rollback:** no automatic rollback. A failed health poll fails the job. The owner re-runs the Deploy workflow on the previous tag (`workflow_dispatch`). Migrations stay expand/contract.
10. **Removed:** `infra/` (Caddy, compose.prod, deploy/backup/restore scripts, systemd units, `roles.sql`), `docker/postgres/`, and the arm64 image build. The `Dockerfile` and `docker-compose.yml` stay for local development and CI e2e.

## Global Constraints

- All constraints in `docs/superpowers/plans/2026-11-03-foundation.md` "Global Constraints" still apply, except the PostgreSQL-specific ones replaced here.
- Branch: `feat/monsterasp-hosting` from `feat/foundation`. Conventional Commits.
- SQL Server column types: UTC `datetimeoffset`; money stays `decimal(18,2)`; JSON columns are `nvarchar(max)`. Do not use the SQL Server 2025 `json` type: the host's version is unknown until Task 0 confirms it.
- snake_case names stay (`UseSnakeCaseNamingConvention`). Index names stay as EF generates them (`ix_users_email`, `ix_otp_challenges_phone`).
- SQL Server images: `mcr.microsoft.com/mssql/server:2022-latest` for both Testcontainers and compose.
- `AWSSDK.S3` client: `ForcePathStyle = true`, `RequestChecksumCalculation = WHEN_REQUIRED`, `ResponseChecksumValidation = WHEN_REQUIRED` (B2 rejects the SDK's default checksum headers).
- The CSP string is copied verbatim from `infra/Caddyfile` into `SecurityHeaders.ContentSecurityPolicy` before that file is deleted.
- New package versions go in `Directory.Packages.props`. Use the latest stable version that is at least two weeks old. Remove `Npgsql.EntityFrameworkCore.PostgreSQL`, `Hangfire.PostgreSql` and `Testcontainers.PostgreSql`.

## Review Focus

1. **Shared-proxy client IP:** if MonsterASP puts a reverse proxy in front of IIS, every request could share one `RemoteIpAddress`, and one user's OTP attempts would rate-limit everyone. Expected: each client gets its own partition. Owned by Task 0 (find out) and Task 9 (post-deploy check).
2. **`Africa/Cairo` on Windows:** `CairoCalendar.Zone` calls `FindSystemTimeZoneById("Africa/Cairo")`, which only works on Windows through ICU. Expected: it resolves on the host. Owned by Task 7 (test run on the Windows runner).
3. **Unknown API route under the SPA fallback:** `GET /api/does-not-exist` must stay a 404, not return `index.html` with 200. Owned by Task 5.
4. **Secrets file exposure:** `GET /secrets.json` and `GET /appsettings.json` must be 404. Owned by Task 6.
5. **B2 upload rejected:** the S3 client must be built with the checksum settings above. MinIO accepts the defaults, so the MinIO tests alone would not catch this. Owned by Task 4 (factory test) and Task 9 (staging photo upload).

---

### Task 0: Confirm the host's limits (owner, no code)

This task decides whether the plan holds as written. Record the answers in the PR description.

- [ ] **Step 1:** Create the MonsterASP account and check whether the free plan allows **two sites** (staging and production). If not, use two accounts, or run staging only until a paid plan is bought.
- [ ] **Step 2:** In the panel, record: the SQL Server **version**; whether the database is reachable **remotely** (host name and port); the WebDeploy **server, site name, user and password**; whether **WebSockets** can be turned on; the **disk quota**; the **idle timeout** and whether "Always On" exists; and whether a **self-contained** `win-x64` app is allowed.
- [ ] **Step 3:** Ask MonsterASP support whether a proxy sits in front of IIS, and if so, its IP range and which forwarding header it sets (Review Focus 1).
- [ ] **Step 4:** Gate. If the database is **not** reachable remotely, Task 7's migration step changes: the API applies migrations at startup behind `Database:MigrateOnStartup=true`, using the Migrator connection string. Stop and update Task 7 before starting it.

---

## Phase A: SQL Server

### Task 1: Switch EF Core, Hangfire and the test fixture to SQL Server

This has to be one task: the integration tests boot the whole host, so the provider, Hangfire and the fixture must switch together.

**Files:**

- Modify: `Directory.Packages.props`, `src/Gameya.Infrastructure/Gameya.Infrastructure.csproj`, `src/Gameya.Api/Gameya.Api.csproj`, `tests/Gameya.Api.IntegrationTests/Gameya.Api.IntegrationTests.csproj`
- Modify: `src/Gameya.Infrastructure/Persistence/AppDbContext.cs:18-22` (`UseNpgsql` → `UseSqlServer`)
- Modify: `src/Gameya.Infrastructure/Persistence/Configurations/{User,RefreshToken,OtpChallenge,ActivityLogEntry}Configuration.cs`
- Delete and regenerate: `src/Gameya.Infrastructure/Persistence/Migrations/*`
- Modify: `src/Gameya.Api/Infrastructure/Observability.cs:74-88` (`AddBackgroundJobs`)
- Modify: `src/Gameya.Api/Infrastructure/ProblemDetailsMapping.cs:6,51-57`
- Modify: `src/Gameya.Api/Infrastructure/HealthChecks.cs` (`PostgresHealthCheck` → `DatabaseHealthCheck`, entry name `"database"`)
- Rename: `tests/Gameya.Api.IntegrationTests/Infrastructure/PostgresFixture.cs` → `DatabaseFixture.cs`, and update every `[Collection(PostgresCollection.Name)]` user
- Modify: `tests/Gameya.Api.IntegrationTests/Ops/HealthTests.cs` (expected entry names)

**Interfaces:**

- Produces: `AppDbContext.Configure(DbContextOptionsBuilder builder, string connectionString)` (same signature, now SQL Server).
- Produces: `DatabaseFixture` with `AppConnectionString`, `MigratorConnectionString`, `CreateAppContext()`; `DatabaseCollection.Name = "database"`.
- Produces: `ProblemDetailsMapping.IsOtpChallengePhoneConflict(Exception)` and `IsUserEmailConflict(Exception)` (same signatures).
- Produces: health entries `database`, `files`, `hangfire`.

- [ ] **Step 1: Update the health test first.** In `HealthTests.Health_reports_healthy_with_three_checks`, the expected entry keys become `["database", "files", "hangfire"]`.
- [ ] **Step 2: Swap the packages.** Remove the three Postgres packages. Add `Microsoft.EntityFrameworkCore.SqlServer` (10.0.x, matching EF), `Hangfire.SqlServer` (1.8.x, matching Hangfire.Core) and `Testcontainers.MsSql` (4.16.0).
- [ ] **Step 3: Rewrite the fixture as `DatabaseFixture`.** Use `MsSqlBuilder` with image `mcr.microsoft.com/mssql/server:2022-latest`. No `roles.sql` step. Both connection strings are the container's `sa` connection string with `Database=gameya;TrustServerCertificate=True`. `InitializeAsync` still runs `MigrateAsync()`.
- [ ] **Step 4: Change `AppDbContext.Configure`** to `UseSqlServer(connectionString)`. Keep snake_case and the interceptor.
- [ ] **Step 5: Update the entity configurations:**
  - `xmin` → `builder.Property<byte[]>("row_version").IsRowVersion()` on User, RefreshToken and OtpChallenge. Keep the comments' reasons, reworded for rowversion.
  - `_roles text[]` → `builder.PrimitiveCollection<List<string>>("_roles").HasColumnName("roles").HasColumnType("nvarchar(max)")`.
  - `jsonb` → `nvarchar(max)` for `EmailSendTimes` (also a `PrimitiveCollection`) and for `activity_log.details`.
  - The filtered indexes (`deleted_at IS NULL`, `email IS NOT NULL`) stay as written; SQL Server accepts the same filter text.
- [ ] **Step 6: Switch `AddBackgroundJobs`** to `UseSqlServerStorage(connectionString, new SqlServerStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true })`. Update the XML doc comment.
- [ ] **Step 7: Rewrite the two conflict predicates** to match `DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } }` whose `Message` contains the index name (`ix_otp_challenges_phone` / `ix_users_email`), compared ordinally. Drop the `Npgsql` using.
- [ ] **Step 8: Regenerate the migrations.** Delete `Migrations/*`, then run with any value in the variable:
      `ConnectionStrings__Migrator="Server=x;Database=gameya" dotnet ef migrations add Initial -p src/Gameya.Infrastructure -s src/Gameya.Infrastructure`.
      Remove the `REVOKE` line (Task 2 replaces it). Save the files without a BOM (see commit `0892ae3`).
- [ ] **Step 9: Fix the raw-SQL tests.** `SessionTests.BumpTokenRowAfterHandler` keeps its `UPDATE refresh_tokens SET revoked_at = revoked_at ...`, since any update bumps rowversion. Mark the two `ActivityLogImmutabilityTests` that expect `PostgresException` 42501 with `Skip = "Task 2"`.
- [ ] **Step 10: Run everything.**
      Run: `dotnet test Gameya.sln`
      Expected: PASS, except the two skipped tests. `UserConcurrencyTests`, `RefreshTokenConcurrencyTests` and the email-conflict tests must pass; they prove rowversion and the unique-violation mapping.
- [ ] **Step 11: Commit.** `refactor(db)!: move ef core and hangfire from postgresql to sql server`

### Task 2: Enforce the append-only activity log with a trigger

**Files:**

- Create: `src/Gameya.Infrastructure/Persistence/Migrations/<ts>_ActivityLogAppendOnlyTrigger.cs`
- Modify: `tests/Gameya.Api.IntegrationTests/Infrastructure/ActivityLogImmutabilityTests.cs`

**Interfaces:**

- Consumes: `DatabaseFixture.AppConnectionString` (Task 1).
- Produces: SQL Server error **51001**, message `activity_log is append-only`, raised on any UPDATE or DELETE of `activity_log`.

- [ ] **Step 1: Write the failing tests.** Replace the Npgsql helper with `ExecuteAsAppAsync(string sql)`, which uses `SqlConnection`/`SqlCommand` and returns the thrown `SqlException`. Tests:
  - `App_login_cannot_update_activity_log` → `ex.Number.ShouldBe(51001)`
  - `App_login_cannot_delete_activity_log` → `ex.Number.ShouldBe(51001)`
  - `Insert_still_works` → inserting through `CreateAppContext()` succeeds and the row count goes up by 1.

  Remove the `Skip`s. Keep the existing EF-interceptor tests unchanged.

- [ ] **Step 2: Run them and confirm they fail.**
      Run: `dotnet test tests/Gameya.Api.IntegrationTests --filter FullyQualifiedName~ActivityLogImmutability`
      Expected: the two new tests FAIL (no exception thrown).
- [ ] **Step 3: Add the migration.** It creates `trg_activity_log_append_only` as `INSTEAD OF UPDATE, DELETE` with body `THROW 51001, 'activity_log is append-only', 1;`. `Down` drops the trigger. (TRUNCATE can't be stopped by a trigger. The app never issues it, and the interceptor stays as the first line of defence.)
- [ ] **Step 4: Run them and confirm they pass.** Same command. Expected: PASS.
- [ ] **Step 5: Commit.** `feat(db): enforce append-only activity_log with a sql server trigger`

### Task 3: Local stack and CI e2e on SQL Server

**Files:**

- Modify: `docker-compose.yml` (service `postgres` → `mssql`)
- Delete: `docker/postgres/init.sql`
- Modify: `.github/workflows/ci.yml` (`image` job: delete the "Build arm64 image (no push)" step)
- Modify: `src/Gameya.Api/Dockerfile` (comments only: drop the arm64/Oracle wording; the cross-publish line stays)

**Interfaces:**

- Consumes: the `--migrate` flag in `Program.cs` (unchanged).

- [ ] **Step 1: Change the compose service** to image `mcr.microsoft.com/mssql/server:2022-latest` with env `ACCEPT_EULA=Y` and `MSSQL_SA_PASSWORD=Gameya-dev-Passw0rd`. Expose port 1433 and use volume `mssqldata`. Healthcheck: `/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$$MSSQL_SA_PASSWORD" -Q "SELECT 1"`. The API's `App` and `Migrator` strings are both `Server=mssql;Database=gameya;User Id=sa;Password=Gameya-dev-Passw0rd;TrustServerCertificate=True`. EF `MigrateAsync` creates the database.
- [ ] **Step 2: Verify locally.**
      Run: `docker compose up -d --build --wait`, then `curl -fsS http://localhost:8080/health`
      Expected: `"status":"Healthy"` with a `database` entry.
- [ ] **Step 3: Verify e2e.**
      Run: `npx playwright test` in `src/gameya-web` with `ng serve` running (same as the CI e2e job)
      Expected: PASS.
- [ ] **Step 4: Commit.** `build(compose): run the local stack on sql server`

---

## Phase B: Files on B2

### Task 4: S3-compatible file store

**Files:**

- Create: `src/Gameya.Infrastructure/Files/FileLocation.cs` (the allowlist and key regex moved out of `LocalFileStore`)
- Create: `src/Gameya.Infrastructure/Files/S3FileStore.cs`, `S3StorageOptions.cs`, `S3ClientFactory.cs`
- Create: `src/Gameya.Application/Abstractions/IStorageHealthProbe.cs`
- Modify: `src/Gameya.Infrastructure/Files/LocalFileStore.cs` (use `FileLocation`, implement the probe)
- Modify: `src/Gameya.Infrastructure/DependencyInjection.cs:58-83` (`AddFiles`)
- Modify: `src/Gameya.Api/Infrastructure/HealthChecks.cs` (`FilesHealthCheck` takes `IStorageHealthProbe`)
- Test: `tests/Gameya.Api.IntegrationTests/Infrastructure/MinioFixture.cs`, `tests/Gameya.Api.IntegrationTests/Files/S3FileStoreTests.cs`, `tests/Gameya.Api.IntegrationTests/Files/S3ClientFactoryTests.cs` (this project already references Infrastructure)

**Interfaces:**

- Produces: `public static class FileLocation { public static bool IsAllowed(string container, string key); }`
- Produces: `public interface IStorageHealthProbe { Task<bool> IsWritableAsync(CancellationToken ct); }`
- Produces: `public sealed class S3StorageOptions { public const string Section = "Storage:S3"; string ServiceUrl; string Bucket; string AccessKeyId; string SecretAccessKey; string Region = "us-east-1"; }`
- Produces: `public static class S3ClientFactory { public static AmazonS3Client Create(S3StorageOptions o); }`
- Produces: `public sealed class S3FileStore(IAmazonS3 s3, string bucket, SignedFileUrls urls) : IFileStore, IStorageHealthProbe`. The object key is `"{container}/{key}"`.
- Config: `Storage:Provider` is `Local` (default when empty) or `S3`. `S3` requires all `Storage:S3:*` values; a missing one throws `InvalidOperationException("Storage:S3:<Name> is required when Storage:Provider is 'S3'.")`. `Storage:RootPath` is required only for `Local`.

- [ ] **Step 1: Write the failing tests.**
  - `S3ClientFactoryTests.Create_uses_path_style_and_when_required_checksums`: asserts `ForcePathStyle == true`, `RequestChecksumCalculation == RequestChecksumCalculation.WHEN_REQUIRED`, `ResponseChecksumValidation == ResponseChecksumValidation.WHEN_REQUIRED`, and `ServiceURL` equals the option value.
  - `MinioFixture` uses `Testcontainers.Minio` and creates bucket `gameya-test`.
  - `S3FileStoreTests` (MinIO):
    - `Upload_then_open_read_returns_same_bytes`
    - `Open_read_missing_returns_null`
    - `Delete_removes_object`
    - `Disallowed_container_throws_ArgumentException` (container `"../x"`)
    - `Probe_is_writable_against_bucket` → `true`
    - `Probe_is_false_for_missing_bucket` → `false`, and it doesn't throw
  - `SignedFileTests`: add one case that runs the existing upload-then-signed-read flow with `Storage:Provider=S3` against MinIO and expects 200 with `image/jpeg`.
- [ ] **Step 2: Run them and confirm they fail.**
      Run: `dotnet test --filter "FullyQualifiedName~S3"`
      Expected: FAIL (types not defined).
- [ ] **Step 3: Implement.** Extract `FileLocation`. Write `S3FileStore`: `PutObjectAsync`, `GetObjectAsync` (return null on `AmazonS3Exception` with `NotFound`), `DeleteObjectAsync`. `GetReadUrl` and `IsReadUrlValid` delegate to `SignedFileUrls` after `FileLocation.IsAllowed`. The probe puts and deletes `.health/{guid}`. Switch `AddFiles` on the provider. Register the store instance as both `IFileStore` and `IStorageHealthProbe`. `StorageRoot` is registered only for `Local`.
- [ ] **Step 4: Run the tests and confirm they pass.**
      Run: `dotnet test Gameya.sln`
      Expected: PASS, including the existing `SignedFileTests` on `Local`.
- [ ] **Step 5: Commit.** `feat(files): add an s3-compatible file store for backblaze b2`

---

## Phase C: Hosting on IIS

### Task 5: Serve the Angular build from the API with the Caddy headers

**Files:**

- Create: `src/Gameya.Api/Infrastructure/SecurityHeaders.cs`
- Create: `src/Gameya.Api/Infrastructure/SpaHosting.cs`
- Modify: `src/Gameya.Api/Program.cs` (middleware order below)
- Modify: `tests/Gameya.Api.IntegrationTests/GameyaApiFactory.cs` (optional `webRoot` constructor parameter → `builder.UseWebRoot(webRoot)`)
- Test: `tests/Gameya.Api.IntegrationTests/Ops/SpaHostingTests.cs`

**Interfaces:**

- Produces: `SecurityHeaders.ContentSecurityPolicy` (const string, verbatim from `infra/Caddyfile`), `app.UseGameyaSecurityHeaders()`.
- Produces: `app.UseGameyaSpaStaticFiles()` and `app.MapGameyaSpaFallback()`.
- Order in `Program.cs`: `UseForwardedHeaders` → `UseGameyaSecurityHeaders` → `UseObservability` → `UseExceptionHandler` → `UseGameyaSpaStaticFiles` → `UseRateLimiter` → auth → endpoints → `MapGameyaSpaFallback` (last).

- [ ] **Step 1: Write the failing tests.** The fixture writes a temp web root with `index.html` (`<app-root>`), `main-ABCD1234.js` and `favicon.ico`.
  - `Root_serves_index_with_no_cache`: `GET /` → 200, `text/html`, `Cache-Control: no-cache`.
  - `Deep_link_serves_index`: `GET /profile/edit` → 200, body contains `<app-root>`.
  - `Hashed_asset_is_immutable`: `GET /main-ABCD1234.js` → `Cache-Control: public, max-age=31536000, immutable`.
  - `Unknown_api_route_is_404_not_index`: `GET /api/does-not-exist` → 404, body doesn't contain `<app-root>` (Review Focus 3).
  - `Health_is_not_swallowed`: `GET /health` → JSON with `status`.
  - `Security_headers_on_every_response`: the CSP equals the const; `X-Content-Type-Options: nosniff`; `Referrer-Policy: strict-origin-when-cross-origin`; `Strict-Transport-Security: max-age=31536000; includeSubDomains` outside Development. Check this on `/` and on `/api/me` (401).
- [ ] **Step 2: Run them and confirm they fail.**
      Run: `dotnet test tests/Gameya.Api.IntegrationTests --filter FullyQualifiedName~SpaHosting`
      Expected: FAIL.
- [ ] **Step 3: Implement.** Set the security headers in `OnStarting`, so error responses get them too. Static files: `UseDefaultFiles` + `UseStaticFiles`, with `OnPrepareResponse` setting immutable for paths matching `^/[^/]+-[A-Za-z0-9]{8}\.(js|css)$` and `no-cache` for everything else. Fallback: `MapFallbackToFile("{*path:regex(^(?!api/|api$|hubs/|health$).*$)}", "index.html")`, with `no-cache`.
- [ ] **Step 4: Run the tests and confirm they pass.**
      Run: `dotnet test Gameya.sln`
      Expected: PASS.
- [ ] **Step 5: Commit.** `feat(api): serve the angular build and security headers from the api`

### Task 6: IIS publish, the secrets file and file logging

**Files:**

- Create: `src/Gameya.Api/web.config` (AspNetCoreModuleV2, `hostingModel="inprocess"`, `<requestFiltering removeServerHeader="true" />`, `<webSocket enabled="true" />`, `stdoutLogEnabled="false"`)
- Modify: `src/Gameya.Api/Gameya.Api.csproj` (`<AspNetCoreHostingModel>InProcess</AspNetCoreHostingModel>`; `secrets.json` excluded from publish)
- Modify: `src/Gameya.Api/Program.cs` (`builder.Configuration.AddJsonFile("secrets.json", optional: true, reloadOnChange: false)` right after `CreateBuilder`)
- Modify: `src/Gameya.Api/Infrastructure/Observability.cs` (file sink inside `outputs`, when `Observability:LogFilePath` is set)
- Modify: `.gitignore` (`secrets.json`, `src/Gameya.Api/logs/`)
- Create: `tools/publish-site.ps1`
- Test: `tests/Gameya.Api.IntegrationTests/Ops/HostingConfigTests.cs`

**Interfaces:**

- Produces: `tools/publish-site.ps1 -OutDir <path>`. It runs `dotnet publish src/Gameya.Api -c Release -r win-x64 --self-contained -o <OutDir>`, then `npx ng build --configuration production` in `src/gameya-web`, then copies `dist/gameya-web/browser/*` into `<OutDir>/wwwroot/`. It exits non-zero if `<OutDir>/wwwroot/index.html` is missing.
- Config: `Observability:LogFilePath` (e.g. `logs/gameya-.json`): daily rolling, `retainedFileCountLimit: 7`, `fileSizeLimitBytes: 10_000_000`, `rollOnFileSizeLimit: true`, `CompactJsonFormatter`. Add `Serilog.Sinks.File`.

- [ ] **Step 1: Write the failing tests.**
  - `Secrets_and_appsettings_are_not_served`: put a `secrets.json` in the content root of a factory with a temp web root; `GET /secrets.json` and `GET /appsettings.json` → 404 (Review Focus 4).
  - `Secrets_json_values_are_read`: a `secrets.json` with `{"App":{"WebBaseUrl":"https://from-secrets.test"}}` makes `IConfiguration["App:WebBaseUrl"]` equal that value (factory without the `UseSetting` override for that key).
  - `File_sink_writes_redacted_events`: with `Observability:LogFilePath` set to a temp path, a logged message containing `01012345678` appears in the file as `010****5678`.
- [ ] **Step 2: Run them and confirm they fail.**
      Run: `dotnet test tests/Gameya.Api.IntegrationTests --filter FullyQualifiedName~HostingConfig`
      Expected: FAIL.
- [ ] **Step 3: Implement** the config source, the file sink, `web.config`, the csproj settings and the publish script.
- [ ] **Step 4: Run the tests and the script.**
      Run: `dotnet test Gameya.sln`, then `pwsh tools/publish-site.ps1 -OutDir $env:TEMP/gameya-site`
      Expected: tests PASS. The output contains `Gameya.Api.exe`, `web.config` and `wwwroot/index.html`, and no `secrets.json`.
- [ ] **Step 5: Commit.** `feat(hosting): add iis publish, secrets file and rolling file logs`

---

## Phase D: Deploy, backups, docs

### Task 7: Rewrite the deploy workflow for WebDeploy

**Files:**

- Modify: `.github/workflows/deploy.yml` (full rewrite; triggers `push main`, `push v*` tag, plus `workflow_dispatch` for rollback)
- Create: `tools/write-secrets.mjs`
- Test: `tools/write-secrets.test.mjs` (already picked up by CI's `node --test "tools/*.test.mjs"`)
- Test: `tests/Gameya.Domain.Tests/CairoCalendarTests.cs` (exists; it touches `CairoCalendar.Zone`, so running it on Windows is the check)

**Interfaces:**

- Produces: `node tools/write-secrets.mjs <out-path>`. It reads the env var names in `SECRET_NAMES` and writes nested JSON: `__` in a name becomes nesting, so `Jwt__SigningKey` becomes `{"Jwt":{"SigningKey":…}}`. Empty values are left out. It exits 1 and names the variable if a value in `REQUIRED_NAMES` is empty.
- GitHub Environment secrets (per environment), mapped to config keys:
  - `WEBDEPLOY_SERVER`, `WEBDEPLOY_SITE`, `WEBDEPLOY_USERNAME`, `WEBDEPLOY_PASSWORD`, `SITE_URL` (with scheme)
  - `DB_CONNECTION_STRING` → `ConnectionStrings__App` and `ConnectionStrings__Migrator`
  - `JWT_SIGNING_KEY`, `SECURITY_HASH_KEY`, `STORAGE_URL_SIGNING_KEY`
  - `SMTP_USERNAME`, `SMTP_PASSWORD`, `EMAIL_FROM`
  - `SMS_PROVIDER`, `SMSGATE_*`
  - `MEDIATR_LICENSE_KEY`
  - `B2_S3_ENDPOINT`, `B2_KEY_ID`, `B2_APP_KEY`, `B2_FILES_BUCKET` → `Storage__S3__*` with `Storage__Provider=S3`
  - optional `OTEL_*`
  - optional `FORWARDED_KNOWN_NETWORK` (from Task 0 Step 3)

- [ ] **Step 1: Write the failing tests** for `write-secrets.mjs`:
  - nesting on `__`
  - a value with `'`, `"` and a newline round-trips exactly
  - a missing required value → exit code 1 and stderr names the variable
  - empty optional values are left out
- [ ] **Step 2: Run them and confirm they fail.**
      Run: `node --test tools/write-secrets.test.mjs`
      Expected: FAIL.
- [ ] **Step 3: Implement `write-secrets.mjs`.**
- [ ] **Step 4: Rewrite `deploy.yml`.** One `deploy` job on `windows-latest`, environment chosen as today, same `concurrency`. Steps:
  1. checkout; setup-dotnet; setup-node 22
  2. `dotnet test tests/Gameya.Domain.Tests` (proves `Africa/Cairo` resolves on Windows; Review Focus 2)
  3. build `efbundle` with `-r win-x64 --self-contained`, then run it with `ConnectionStrings__Migrator` = `DB_CONNECTION_STRING`
  4. `pwsh tools/publish-site.ps1 -OutDir site`
  5. `node tools/write-secrets.mjs site/secrets.json`. Then set `ASPNETCORE_ENVIRONMENT` (`Production` for a tag, `Staging` otherwise) as an `<environmentVariable>` under `<aspNetCore>` in `site/web.config`. It goes there, not in `secrets.json`, because the environment must be known before configuration loads.
  6. `choco install webdeploy -y`
  7. `msdeploy -verb:sync -source:contentPath=site -dest:contentPath=$SITE,computerName=https://$SERVER:8172/msdeploy.axd?site=$SITE,userName=…,password=…,authType=Basic -enableRule:AppOffline -enableRule:DoNotDeleteRule -skip:Directory=\\logs$ -allowUntrusted`. The password is passed through an env var, never echoed.
  8. Poll `$SITE_URL/health` every 10 s for up to 5 min until the top-level `status` is exactly `Healthy`. Otherwise fail.
  9. `actions/upload-artifact` of `site/` **without** `secrets.json` (retention 30 days)
- [ ] **Step 5: Verify on staging.** Merge to `main`. Expected: Deploy is green, and `curl $SITE_URL/health` → `Healthy` with `database`, `files`, `hangfire`.
- [ ] **Step 6: Commit.** `ci(deploy): deploy to monsterasp with webdeploy and run migrations from the runner`

### Task 8: Nightly database export to B2

**Files:**

- Create: `.github/workflows/backup.yml`

**Interfaces:**

- Secrets (per environment): `DB_CONNECTION_STRING`, `B2_KEY_ID`, `B2_APP_KEY`, `B2_BACKUP_BUCKET`, optional `BACKUP_PING_URL`.

- [ ] **Step 1: Write the workflow.** `schedule: cron "30 1 * * *"` plus `workflow_dispatch`. A matrix over `[staging, production]` sets `environment: ${{ matrix.env }}`. On `ubuntu-latest`:
  1. `dotnet tool install -g microsoft.sqlpackage`
  2. `sqlpackage /a:Export /scs:"$DB_CONNECTION_STRING" /tf:gameya-<UTC ts>.bacpac`
  3. install rclone; upload with `RCLONE_CONFIG_B2_*` env vars to `b2:$B2_BACKUP_BUCKET/db/`
  4. `curl -fsS --retry 3 $BACKUP_PING_URL` if set

  Files need no copy: the files bucket has B2 "keep all versions" plus a 35-day lifecycle rule (Task 9 docs).

- [ ] **Step 2: Verify.** Run it with `workflow_dispatch` for staging. Expected: green, and a `.bacpac` listed under `db/` in the bucket.
- [ ] **Step 3: Verify a restore.** On a scratch database run `sqlpackage /a:Import /tcs:"<scratch conn>" /sf:<downloaded>.bacpac`. Expected: `__EFMigrationsHistory` lists `Initial` and `ActivityLogAppendOnlyTrigger`.
- [ ] **Step 4: Commit.** `ci(backup): export the database nightly to backblaze b2`

### Task 9: Remove the Oracle setup and rewrite the deployment docs

**Files:**

- Delete: `infra/` (whole directory)
- Modify: `docs/deployment.md` (full rewrite)
- Modify: `README` or any other doc that mentions Oracle, Caddy, DuckDNS or `infra/` (`grep -ri "oracle\|caddy\|duckdns\|infra/" docs src/gameya-web/README.md`)

- [ ] **Step 1: Delete `infra/`.** Check that nothing still references it:
      Run: `git grep -n "infra/"`
      Expected: only historical plans and specs under `docs/superpowers/`.
- [ ] **Step 2: Rewrite `docs/deployment.md`** with these sections:
  - **Hosting map:** MonsterASP site; SQL Server; B2 files and backup buckets; Brevo; SMS Gate; UptimeRobot.
  - **One-time owner setup:** the Task 0 answers; B2 buckets (files with versioning and a 35-day lifecycle, backups with a 35-day lifecycle) and a key per environment; turn on WebSockets in the panel; UptimeRobot every 5 min on `/health` (keep-alive); healthchecks.io for backups.
  - **GitHub Environment secrets** table (Task 7 and Task 8 names).
  - **What a deploy does** (Task 7 steps).
  - **Rollback:** re-run Deploy with `workflow_dispatch` on the previous tag; migrations are not rolled back (expand/contract).
  - **Backups and restore** (Task 8).
  - **Known limits:** idle shutdown pauses Hangfire until the next request; disk quota; shared CPU.
  - **Later:** Cloudflare in front, once a domain is bought.
  - **First-deploy checklist (AC-F8):**
    1. staging `/health` is `Healthy`
    2. a HEIC photo upload works on staging and lands in the B2 files bucket (Review Focus 5)
    3. a manual backup run plus a scratch restore pass
    4. OTP requests from two different networks are rate-limited separately (Review Focus 1)
- [ ] **Step 3: Verify.**
      Run: `npx prettier --check docs/deployment.md` and `dotnet build Gameya.sln`
      Expected: both pass.
- [ ] **Step 4: Commit.** `docs(deploy)!: document monsterasp hosting and remove the oracle vm setup`
