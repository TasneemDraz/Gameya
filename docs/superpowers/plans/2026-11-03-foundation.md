# Gam'eya Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A deployable, tested skeleton for Gam'eya covering phone OTP auth, profile with verified email, an append-only activity log, an Arabic/English RTL Angular shell, git hooks, CI, and Azure deployment.

**Architecture:** A modular monolith with Clean Architecture layers (Domain → Application → Infrastructure → Api). Each layer has feature folders. A MediatR pipeline (validation → authorization → transaction → activity log) wraps every request. An Angular SPA is served by Azure Static Web Apps, and its linked App Service backend serves `/api` on the same origin.

**Tech Stack:** .NET 10 (LTS), ASP.NET Core, MediatR, FluentValidation, Ardalis.Specification (+ `.EntityFrameworkCore`), EF Core + Npgsql (PostgreSQL 17), Hangfire.PostgreSql, Magick.NET, Azure Blob/ACS Email, Serilog, OpenTelemetry. Tests: xUnit, Shouldly, NSubstitute, Testcontainers, NetArchTest. Frontend: Angular (latest stable), Angular Material, Transloco, Playwright. Tooling: Husky, lint-staged, commitlint. Infra: Docker, GitHub Actions, Bicep.

**Spec:** `docs/superpowers/specs/2026-11-03-foundation-design.md` (requirements: `docs/requirements.md`)

## Global Constraints

- **Prerequisite:** install the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`). Only 9.0.320 is installed today. `global.json` pins `10.0.100` with `rollForward: latestFeature`.
- **Branch:** work on `feat/foundation`, created from `docs/foundation-spec`. Commits follow Conventional Commits and may end with `[FR-xxx-nn]`.
- **No commercial-license packages without a key:**
  - MediatR 13+ needs a license key. Use the free Community license and read it from config `MediatR:LicenseKey`. Its absence logs a warning only.
  - Do **not** use FluentAssertions 8+ (use Shouldly) or any mapping library (AutoMapper, Mapster).
- **Data access goes through specifications (Ardalis.Specification).**
  - Every read, both entity lookups in commands and DTO projections in queries, uses a named spec class in the feature's `Specifications/` folder.
  - Projections are `Specification<TEntity, TResult>` with `Query.Select(...)`, so EF translates them to SQL.
  - Handlers never write ad-hoc `.Where`/`.Select` on a `DbSet`. An architecture test enforces this.
  - Values that SQL can't compute (for example SAS photo URLs) are projected as raw fields into a `*Row` record and finished by the handler.
- **Central package management:** `Directory.Packages.props`. Nullable enabled, `TreatWarningsAsErrors=true`.
- **Time:**
  - Never call `DateTime.Now`, `DateTime.UtcNow`, or `DateTimeOffset.UtcNow` outside `SystemClock`. An architecture test enforces this.
  - Domain methods take `DateTimeOffset now`.
  - Store UTC `timestamptz`.
  - The Cairo zone id is `"Africa/Cairo"`. The runtime image must contain tzdata (use the Debian `aspnet` image, not chiseled).
- **Money (not used yet, convention fixed now):** `numeric(18,2)` / `decimal`.
- **Database:** snake_case names (EFCore.NamingConventions). Roles `gameya_migrator` (owns the schema, runs migrations) and `gameya_app` (runtime). `gameya_app` has only `SELECT, INSERT` on `activity_log`.
- **API errors:**
  - RFC 7807 ProblemDetails with an extension `code` (UPPER_SNAKE) and optional `params` object. The server never returns user-facing text.
  - Validation failures return 400 with code `VALIDATION_FAILED` and `errors` keyed by field.
- **Error code list (`ErrorCodes`):** `VALIDATION_FAILED, UNAUTHENTICATED, FORBIDDEN, NOT_FOUND, RATE_LIMITED, INVALID_PHONE, OTP_INVALID, OTP_EXPIRED, OTP_LOCKED, OTP_RESEND_TOO_SOON, OTP_RESEND_LIMIT, REFRESH_TOKEN_INVALID, REFRESH_TOKEN_REUSED, USER_BLOCKED, NAME_INVALID, EMAIL_TOKEN_INVALID, EMAIL_TOKEN_EXPIRED, EMAIL_RESEND_LIMIT, EMAIL_IN_USE, ACCOUNT_HAS_ACTIVE_GAMEYAS, FILE_TOO_LARGE, FILE_TYPE_NOT_ALLOWED, CONCURRENCY_CONFLICT`.
- **Auth values:**
  - OTP: 6 digits, valid 5 min. 5 wrong attempts → 15-min lock. 60 s between sends. Max 4 sends (1 + 3 resends) per 15-min window.
  - Access JWT: 10 min.
  - Refresh token: 32 random bytes (base64url), sliding 30 days. Cookie `gameya_rt`, `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`.
  - Email token: valid 24 h. Max 3 verification sends per rolling hour.
  - Photo: JPG/PNG/HEIC, ≤ 5 MB, resized to 512 px JPEG.
- **Secrets hashing:** OTP codes, refresh tokens, and email tokens are stored only as HMAC-SHA256 (key `Security:HashKey`), base64. Compare with `CryptographicOperations.FixedTimeEquals`.
- **Logging:** never log phone (unmasked), OTP code, tokens, or email. The dev `LoggingSmsSender` is the single exception and is only registered in Development/E2E.
- **Frontend:**
  - All text comes from `public/i18n/ar.json` / `en.json`. Default language `ar`.
  - CSS logical properties only.
  - Statuses are shown as icon + text.
  - Layout works at 360 px width.

## Review Focus

1. **Two browser tabs refreshing at once.** The second tab presents a just-rotated refresh token. Expected: the user is not logged out. Covered in Task 8 (10-second server grace without family revocation) and Task 15 (`navigator.locks` single-flight across tabs).
2. **Phone typed the way Egyptians type it:** spaces, dashes, `+20`, `0020`, Arabic-Indic or Persian digits. Expected: normalized to the same E.164. Covered by the theory cases in Task 2.
3. **Double-tapped "Send code" / concurrent OTP requests for one phone.** Expected: never a 500. One send succeeds and the other gets `OTP_RESEND_TOO_SOON`. Covered in Task 7 (unique phone index + `xmin` concurrency + parallel integration test).
4. **iPhone HEIC photos, 6 MB files, a PDF renamed to `.jpg`.** Expected: HEIC accepted and converted, the others rejected with `FILE_TOO_LARGE` / `FILE_TYPE_NOT_ALLOWED` by content sniffing, not extension. Covered in Task 9.
5. **Verifying an email already verified on another account.** Expected: `EMAIL_IN_USE`, no takeover. Covered in Task 10 (unique filtered index on verified email + handler test).

---

## File Structure

```
.gitattributes .gitignore .editorconfig global.json Directory.Build.props Directory.Packages.props Gameya.sln
package.json commitlint.config.mjs .lintstagedrc.mjs .husky/{pre-commit,commit-msg,pre-push}
tools/check-i18n.mjs
docker-compose.yml  docker/postgres/init.sql  src/Gameya.Api/Dockerfile
infra/main.bicep infra/modules/*.bicep infra/sql/roles.sql
.github/workflows/{ci.yml,deploy.yml}
src/Gameya.Domain/
  Common/{Entity.cs,IDomainEvent.cs,IActivityEvent.cs,DomainException.cs,ErrorCodes.cs,CairoCalendar.cs}
  Users/{PhoneNumber.cs,User.cs,Language.cs,UserEvents.cs}
  Auth/{OtpChallenge.cs,OtpRules.cs,RefreshToken.cs}
  Activity/ActivityLogEntry.cs
src/Gameya.Application/
  Abstractions/{IClock.cs,ICurrentUser.cs,IAppDbContext.cs,ISmsSender.cs,IEmailSender.cs,IFileStore.cs,ISecretHasher.cs,ITokenService.cs,IImageProcessor.cs,IActiveParticipationChecker.cs,ICommand.cs}
  Security/{AccessAttributes.cs,PhoneDisplay.cs,DisplayPhone.cs}
  Behaviors/{ValidationBehavior.cs,AuthorizationBehavior.cs,TransactionBehavior.cs,ActivityLogBehavior.cs}
  Common/Specifications/SpecificationQueryExtensions.cs
  Auth/{RequestOtp.cs,VerifyOtp.cs,RefreshSession.cs,Logout.cs,LogoutAll.cs,SessionResult.cs,SessionIssuer.cs}
  Auth/Specifications/{OtpChallengeByPhoneSpec.cs,RefreshTokenByHashSpec.cs,RefreshTokensByFamilySpec.cs,ActiveRefreshTokensByUserSpec.cs}
  Users/{GetMe.cs,UpdateProfile.cs,SetPhoto.cs,RemovePhoto.cs,ChangeEmail.cs,ResendEmailVerification.cs,VerifyEmail.cs,DeleteAccount.cs,MeDto.cs}
  Users/Specifications/{UserByIdSpec.cs,ActiveUserByPhoneSpec.cs,UserByPendingEmailTokenSpec.cs,VerifiedEmailOwnerSpec.cs,MeRowSpec.cs}
  DependencyInjection.cs
src/Gameya.Infrastructure/
  Persistence/{AppDbContext.cs,Configurations/*.cs,Migrations/*,ActivityLogImmutabilityInterceptor.cs,DomainEventCollector.cs}
  Security/{HmacSecretHasher.cs,JwtTokenService.cs}
  Sms/{LoggingSmsSender.cs,DevOtpStore.cs,ProviderSmsSender.cs}
  Email/{SmtpEmailSender.cs,AcsEmailSender.cs}
  Files/{BlobFileStore.cs,MagickImageProcessor.cs}
  Time/SystemClock.cs
  Participation/NoParticipationChecker.cs
  DependencyInjection.cs
src/Gameya.Api/
  Program.cs  Endpoints/{AuthEndpoints.cs,MeEndpoints.cs,DevEndpoints.cs}
  Infrastructure/{ProblemDetailsMapping.cs,HttpCurrentUser.cs,RefreshCookie.cs,RateLimiting.cs,HealthChecks.cs,Observability.cs}
src/gameya-web/  (Angular; files listed per task)
tests/Gameya.Domain.Tests/  tests/Gameya.Application.Tests/  tests/Gameya.Api.IntegrationTests/  tests/Gameya.ArchitectureTests/
```

The Api uses minimal-API endpoint groups. The spec says "controllers", but endpoint groups are the idiomatic .NET 10 equivalent and keep each feature in one file.

---

### Task 1: Repository hygiene and solution skeleton

**Files:**
- Create: `.gitattributes`, `.gitignore`, `.editorconfig`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `Gameya.sln`, the four `src/*` projects, four `tests/*` projects
- Test: `tests/Gameya.ArchitectureTests/LayerTests.cs`

**Interfaces:**
- Produces: project references Domain ← Application ← Infrastructure ← Api. All test projects reference what they test.

- [ ] **Step 1: Line endings first.** Write `.gitattributes`:
  ```
  * text=auto eol=lf
  *.{cmd,bat,ps1} text eol=crlf
  *.{png,jpg,jpeg,heic,ico,woff2,pdf} binary
  ```
  Then run `git add --renormalize . && git commit -m "chore: add .gitattributes and normalize line endings"`.
- [ ] **Step 2: Scaffold.** `dotnet new sln`, `classlib` for Domain/Application/Infrastructure, `web` for Api, `xunit` for the four test projects. Add references. Add `global.json`. In `Directory.Build.props` set `net10.0`, Nullable, ImplicitUsings, TreatWarningsAsErrors. Use .NET `gitignore` plus Node/Angular entries. `.editorconfig`: 4-space C#, 2-space TS/JSON/SCSS, LF.
- [ ] **Step 3: Write the failing architecture tests** in `LayerTests.cs` (NetArchTest):
  - `Domain_has_no_dependency_on_other_layers`: types in `Gameya.Domain` don't reference `Gameya.Application|Infrastructure|Api`, `Microsoft.EntityFrameworkCore`, or `MediatR`.
  - `Application_does_not_depend_on_Infrastructure_or_Api`.
  - `Only_SystemClock_reads_the_system_time`: no type except `Gameya.Infrastructure.Time.SystemClock` references `DateTime.get_Now`, `DateTime.get_UtcNow`, or `DateTimeOffset.get_UtcNow`. Implement with Mono.Cecil IL scan over the four assemblies, because NetArchTest can't see member calls.
- [ ] **Step 4:** Run `dotnet test tests/Gameya.ArchitectureTests`. Expected: FAIL to compile, because the clock test references `typeof(SystemClock)`. Add `src/Gameya.Infrastructure/Time/SystemClock.cs` as an empty `public sealed class` (Task 2 makes it implement `IClock`). Then prove the clock test can fail: temporarily add `_ = DateTime.UtcNow;` in an Api class, see it FAIL, and remove it.
- [ ] **Step 5:** Run `dotnet build && dotnet test`. Expected: all PASS, zero warnings.
- [ ] **Step 6:** Commit `chore: scaffold solution with layered projects and architecture tests`.

---

### Task 2: Domain primitives: errors, clock, Cairo calendar, phone number

**Files:**
- Create: `src/Gameya.Domain/Common/{DomainException.cs,ErrorCodes.cs,Entity.cs,IDomainEvent.cs,IActivityEvent.cs,CairoCalendar.cs}`, `src/Gameya.Domain/Users/PhoneNumber.cs`, `src/Gameya.Application/Abstractions/IClock.cs`; modify `SystemClock.cs`
- Test: `tests/Gameya.Domain.Tests/{CairoCalendarTests.cs,PhoneNumberTests.cs}`

**Interfaces:**
- Produces:
  - `DomainException(string code, IReadOnlyDictionary<string, object?>? @params = null)` with `Code` and `Params`.
  - `static class ErrorCodes`: `const string` per Global Constraints.
  - `abstract class Entity` with `IReadOnlyList<IDomainEvent> DomainEvents`, `protected void Raise(IDomainEvent e)`, `void ClearDomainEvents()`.
  - `interface IActivityEvent : IDomainEvent`: `string Action`, `Guid? GameyaId`, `string TargetType`, `string TargetId`, `object? Details`, `Guid? ActorUserIdOverride`, `string? Note`.
  - `static class CairoCalendar`: `TimeZoneInfo Zone`, `DateOnly ToCairoDate(DateTimeOffset utc)`, `DateTimeOffset StartOfDayUtc(DateOnly cairoDate)`, `DateOnly MonthlyDueDate(DateOnly start, int monthOffset)`.
  - `sealed record PhoneNumber`: `static PhoneNumber Parse(string input)` (throws `INVALID_PHONE`), `static bool TryParse(string?, out PhoneNumber?)`, `string E164`, `string Masked`.
  - `interface IClock { DateTimeOffset UtcNow { get; } }`.

- [ ] **Step 1: Write failing tests.**
  - **`CairoCalendarTests`:**
    - `MonthlyDueDate(new(2027,1,31), 1) == new(2027,2,28)`
    - `MonthlyDueDate(new(2027,1,31), 2) == new(2027,3,31)`, which shows no drift from clamping
    - `MonthlyDueDate(new(2028,1,31), 1) == new(2028,2,29)`
    - `ToCairoDate(2026-01-01T21:30Z) == 2026-01-01` (winter, +2)
    - `ToCairoDate(2026-07-01T21:30Z) == 2026-07-02` (summer DST, +3)
    - `StartOfDayUtc(2026-07-02) == 2026-07-01T21:00Z`
  - **`PhoneNumberTests`** (Theory):
    - Valid → `"+201012345678"`: `"01012345678"`, `"010 1234 5678"`, `"010-1234-5678"`, `"+201012345678"`, `"00201012345678"`, `"٠١٠١٢٣٤٥٦٧٨"`, `"۰۱۰۱۲۳۴۵۶۷۸"`.
    - Prefixes 011/012/015 are valid.
    - Invalid → `INVALID_PHONE`: `"01312345678"`, `"0101234567"`, `"010123456789"`, `""`, `"abc"`.
    - `Parse("01012345678").Masked == "010****5678"`.
- [ ] **Step 2:** Run `dotnet test tests/Gameya.Domain.Tests`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement.**
  - `PhoneNumber`: map U+0660–0669 and U+06F0–06F9 to ASCII, strip spaces and dashes, convert a leading `+20`/`0020` to `0`, then match `^01[0125]\d{8}$`.
  - `MonthlyDueDate`: clamp the day to `DaysInMonth` of the target month, always computed from `start`.
  - `SystemClock : IClock` returns `DateTimeOffset.UtcNow`.
- [ ] **Step 4:** Run domain and architecture tests. Expected: PASS.
- [ ] **Step 5:** Commit `feat(domain): phone number, Cairo calendar, domain errors [FR-AUTH-01] [FR-GAM-02]`.

---

### Task 3: OTP challenge rules

**Files:**
- Create: `src/Gameya.Domain/Auth/{OtpChallenge.cs,OtpRules.cs}`
- Test: `tests/Gameya.Domain.Tests/OtpChallengeTests.cs`

**Interfaces:**
- Consumes: `PhoneNumber`, `DomainException`, `ErrorCodes`.
- Produces:
  - `static class OtpRules`: `CodeLifetime = 5 min`, `MaxFailedAttempts = 5`, `LockDuration = 15 min`, `ResendCooldown = 60 s`, `SendWindow = 15 min`, `MaxSendsPerWindow = 4`.
  - `class OtpChallenge : Entity`:
    - `static OtpChallenge Create(PhoneNumber phone)`
    - `void RecordSend(string codeHash, DateTimeOffset now)`, which throws `OTP_LOCKED{lockedUntil}`, `OTP_RESEND_TOO_SOON{retryAt}`, or `OTP_RESEND_LIMIT{retryAt}`
    - `void Verify(string submittedHash, DateTimeOffset now)`, which throws `OTP_LOCKED{lockedUntil}`, `OTP_EXPIRED`, or `OTP_INVALID{attemptsLeft}`. On success it consumes the code.
    - Properties: `PhoneNumber Phone`, `string? CodeHash`, `DateTimeOffset? ExpiresAt`, `int FailedAttempts`, `int SendCount`, `DateTimeOffset? WindowStartedAt`, `DateTimeOffset? LastSentAt`, `DateTimeOffset? LockedUntil`.
    - `DateTimeOffset ResendAvailableAt` (= `LastSentAt + cooldown`).

- [ ] **Step 1: Write failing tests**, using `t0 = 2026-11-03T10:00Z`:
  - `First_send_sets_code_and_expiry`: `ExpiresAt == t0+5m`.
  - `Resend_before_60s_throws_RESEND_TOO_SOON` (at t0+59s), with `retryAt == t0+60s`.
  - `Fifth_send_in_window_throws_RESEND_LIMIT`: sends at t0, +1m, +2m, +3m succeed, then +4m throws.
  - `Window_resets_after_15_minutes`: after 4 sends, a send at t0+15m succeeds.
  - `Correct_code_verifies_and_consumes`: second verify with the same hash → `OTP_INVALID`.
  - `Expired_code_throws_EXPIRED_and_does_not_count_attempt`: at t0+5m+1s, `FailedAttempts == 0`.
  - `Wrong_code_reports_attempts_left`: first wrong → `attemptsLeft == 4`.
  - `Fifth_wrong_attempt_locks_for_15_minutes`: throws `OTP_LOCKED` with `lockedUntil == now+15m`, and `RecordSend` at now+14m also throws `OTP_LOCKED`.
  - `Resend_does_not_reset_failed_attempts`: 4 wrong, resend at +61s, 1 wrong → `OTP_LOCKED`.
  - `After_lock_expires_attempts_and_window_reset`: send at lockedUntil succeeds and `FailedAttempts == 0`.
  - `Successful_verify_resets_attempts_and_window`.
- [ ] **Step 2:** Run `dotnet test --filter OtpChallengeTests`. Expected: FAIL.
- [ ] **Step 3:** Implement `OtpChallenge` per the tests. All comparisons use `now`. Hash comparison uses `FixedTimeEquals` over UTF-8 bytes.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(domain): OTP challenge rules [FR-AUTH-01]`.

---

### Task 4: User aggregate, refresh token, activity log entry

**Files:**
- Create: `src/Gameya.Domain/Users/{User.cs,Language.cs,UserEvents.cs}`, `src/Gameya.Domain/Auth/RefreshToken.cs`, `src/Gameya.Domain/Activity/ActivityLogEntry.cs`
- Test: `tests/Gameya.Domain.Tests/{UserTests.cs,RefreshTokenTests.cs}`

**Interfaces:**
- Produces:
  - `enum Language { Ar, En }`
  - **`class User : Entity`.**
    - `static User Register(Guid id, PhoneNumber phone, Language language, DateTimeOffset now)` raises `UserRegistered`.
    - `void SetProfile(string fullName, Language language, DateTimeOffset now)` trims, enforces 2–60 chars else `NAME_INVALID`, sets `ProfileComplete = true`, and raises `ProfileUpdated(old, new)` only when something changed.
    - `void SetPhoto(string? blobKey, DateTimeOffset now)`.
    - `void RequestEmailChange(string email, string tokenHash, DateTimeOffset now)` throws `EMAIL_RESEND_LIMIT{retryAt}` if 3 sends happened in the last rolling hour. Tracks send times in `List<DateTimeOffset> EmailSendTimes` (json column) and prunes entries older than 1 h.
    - `void ResendEmailVerification(string tokenHash, DateTimeOffset now)` has the same limit and requires a `PendingEmail`.
    - `void VerifyEmail(string tokenHash, DateTimeOffset now)` throws `EMAIL_TOKEN_INVALID` / `EMAIL_TOKEN_EXPIRED`. Moves `PendingEmail → Email`, sets `EmailVerifiedAt`, clears the pending fields, and raises `EmailVerified`.
    - `void Delete(DateTimeOffset now)` sets `DeletedAt`, nulls Phone/FullName/Email/PendingEmail/PhotoKey, and raises `AccountDeleted`.
    - `void Block()` / `void Unblock()`.
    - `void MarkLoggedOutEverywhere(DateTimeOffset now)` raises `LoggedOutAll`.
    - Properties: `Guid Id`, `PhoneNumber? Phone`, `string? FullName`, `bool ProfileComplete`, `Language Language`, `string? Email`, `DateTimeOffset? EmailVerifiedAt`, `string? PendingEmail`, `string? PendingEmailTokenHash`, `DateTimeOffset? PendingEmailExpiresAt`, `string? PhotoKey`, `bool IsBlocked`, `DateTimeOffset CreatedAt`, `DateTimeOffset? DeletedAt`, `bool IsDeleted`, `IReadOnlyCollection<string> Roles` (`text[]`; `"PlatformAdmin"` is the only role used).
  - **`UserEvents.cs`:** records implementing `IActivityEvent` with actions `USER_REGISTERED`, `PROFILE_UPDATED`, `EMAIL_VERIFIED`, `LOGGED_OUT_ALL`, `ACCOUNT_DELETED`, `GameyaId = null`, `TargetType = "User"`. `UserRegistered.ActorUserIdOverride = userId`. The `ProfileUpdated.Details` example is `{ old: { fullName, language }, new: {...} }`. Details never contain the phone or email.
  - **`class RefreshToken`.**
    - `static RefreshToken Issue(Guid userId, Guid familyId, string tokenHash, DateTimeOffset now)` with `ExpiresAt = now+30d`.
    - `bool IsActive(DateTimeOffset now)`.
    - `void Revoke(DateTimeOffset now, Guid? replacedBy = null)`.
    - `bool WasRotatedWithin(TimeSpan window, DateTimeOffset now)`, which is true if `ReplacedByTokenId != null && now - RevokedAt <= window`.
    - Properties: `Guid Id`, `UserId`, `FamilyId`, `TokenHash`, `CreatedAt`, `ExpiresAt`, `RevokedAt`, `ReplacedByTokenId`.
  - **`sealed class ActivityLogEntry`:** `long Id`, `Guid? GameyaId`, `DateTimeOffset OccurredAt`, `Guid? ActorUserId`, `string ActorRole`, `string Action`, `string TargetType`, `string TargetId`, `string DetailsJson`, `string? Note`. Construct only via `static ActivityLogEntry From(IActivityEvent e, Guid? actorUserId, string actorRole, DateTimeOffset now)`. No setters.

- [ ] **Step 1: Write failing tests.**
  - **`UserTests`:**
    - `Name_must_be_2_to_60_chars_after_trim` (`" a "` → `NAME_INVALID`, 61 chars → `NAME_INVALID`).
    - `SetProfile_marks_profile_complete_and_raises_ProfileUpdated`.
    - `SetProfile_with_same_values_raises_nothing`.
    - `Fourth_email_send_within_an_hour_throws_RESEND_LIMIT`.
    - `Email_send_allowed_again_after_an_hour`.
    - `VerifyEmail_with_expired_token_throws` (24 h + 1 s).
    - `VerifyEmail_moves_pending_to_verified_and_keeps_old_until_then`. Before verifying, `Email` is still the old verified address.
    - `Delete_clears_personal_data_and_raises_AccountDeleted`.
  - **`RefreshTokenTests`:**
    - `Expires_after_30_days`.
    - `Revoked_token_is_not_active`.
    - `WasRotatedWithin_true_only_inside_window_and_only_when_replaced`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement per the Interfaces. `ActivityLogEntry.From` serializes `Details` with `System.Text.Json` camelCase.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(domain): user aggregate, refresh token, activity log entry [FR-AUTH-02] [FR-AUTH-04] [FR-LOG-02]`.

---

### Task 5: Application pipeline and access attributes

**Files:**
- Create: `src/Gameya.Application/Abstractions/{ICurrentUser.cs,IAppDbContext.cs,ICommand.cs}`, `Security/AccessAttributes.cs`, `Behaviors/*.cs`, `DependencyInjection.cs`
- Test: `tests/Gameya.Application.Tests/{PipelineTests.cs,AccessAttributeCoverageTests.cs}`

**Interfaces:**
- Consumes: `Entity`, `IActivityEvent`, `ActivityLogEntry`, `IClock`.
- Produces:
  - `interface ICurrentUser { Guid? UserId; bool IsAuthenticated; IReadOnlyCollection<string> Roles; }`
  - `interface ICommand<TResult> : IRequest<TResult>` (state-changing) and `interface IQuery<TResult> : IRequest<TResult>`.
  - Attributes `[PublicAccess]`, `[RequiresAuth]`, and `[RequiresRole(string role)]`. Exactly one per request type.
  - `interface IAppDbContext`: `DbSet<User> Users`, `DbSet<OtpChallenge> OtpChallenges`, `DbSet<RefreshToken> RefreshTokens`, `DbSet<ActivityLogEntry> ActivityLog`, `Task<int> SaveChangesAsync(CancellationToken)`, `Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken)`, `IEnumerable<Entity> TrackedEntitiesWithEvents()`.
  - **`SpecificationQueryExtensions`** (in `Common/Specifications/`) on `IQueryable<T>`/`DbSet<T>`, evaluated with `SpecificationEvaluator.Default` from `Ardalis.Specification.EntityFrameworkCore`:
    - `Task<T?> FirstOrDefaultAsync<T>(this DbSet<T>, ISpecification<T>, CancellationToken)`
    - `Task<TResult?> FirstOrDefaultAsync<T, TResult>(this DbSet<T>, ISpecification<T, TResult>, CancellationToken)`
    - `Task<List<T>> ListAsync<T>(this DbSet<T>, ISpecification<T>, CancellationToken)`
    - `Task<List<TResult>> ListAsync<T, TResult>(this DbSet<T>, ISpecification<T, TResult>, CancellationToken)`
    - `Task<bool> AnyAsync<T>(this DbSet<T>, ISpecification<T>, CancellationToken)`
    - Projection specs apply `AsNoTracking()`. Entity specs stay tracked so commands can modify the result.
  - **Spec classes**, built in the tasks that first use them:
    - `ActiveUserByPhoneSpec(PhoneNumber)`: `DeletedAt == null`.
    - `UserByIdSpec(Guid)`.
    - `UserByPendingEmailTokenSpec(string hash)`.
    - `VerifiedEmailOwnerSpec(string email, Guid excludeUserId)`.
    - `OtpChallengeByPhoneSpec(PhoneNumber)`.
    - `RefreshTokenByHashSpec(string hash)`.
    - `RefreshTokensByFamilySpec(Guid familyId)`: active only.
    - `ActiveRefreshTokensByUserSpec(Guid userId)`.
    - `MeRowSpec(Guid userId) : Specification<User, MeRow>`.
  - `static IServiceCollection AddApplication(this IServiceCollection, IConfiguration)` registers MediatR (license key from `MediatR:LicenseKey`), validators, and behaviors in the order Validation → Authorization → Transaction → ActivityLog.
- **Behavior semantics:**
  - **Validation:** collects FluentValidation failures and throws `ValidationFailedException(IDictionary<string,string[]>)`.
  - **Authorization:** `[RequiresAuth]` with an unauthenticated user throws `DomainException(UNAUTHENTICATED)`. A missing role throws `FORBIDDEN`. A blocked user is caught at token issue (Task 8), not here.
  - **Transaction:** only for `ICommand<>`. Begins a transaction, calls next, then `SaveChangesAsync`, then commits.
  - **ActivityLog:** only for `ICommand<>`. After next, drains `TrackedEntitiesWithEvents()`. Each `IActivityEvent` becomes `ActivityLogEntry.From(e, e.ActorUserIdOverride ?? currentUser.UserId, role, clock.UtcNow)`, added to the `ActivityLog` set. Events are then cleared. `role` is `"System"` when there is no actor, otherwise `"User"`. Gam'eya roles come in sub-project 2.

- [ ] **Step 1: Write failing tests** with NSubstitute fakes and an EF InMemory-free fake `IAppDbContext` (hand-written test double with lists):
  - `Validation_failure_throws_before_handler_runs`.
  - `RequiresAuth_rejects_anonymous_with_UNAUTHENTICATED`.
  - `RequiresRole_rejects_user_without_role_with_FORBIDDEN`.
  - `Command_activity_events_become_log_entries_with_actor`.
  - `Registration_event_uses_actor_override_when_anonymous`.
  - `Queries_do_not_open_transactions`.
  - `AccessAttributeCoverageTests.Every_request_has_exactly_one_access_attribute`: reflects over the Application assembly for `IRequest<>` implementations. This is the spec's "authorization declared on every request" check.
  - `SpecificationEvaluationTests` (EF Core SQLite in-memory, used only to evaluate expression translation):
    - `Projection_spec_returns_projected_type_untracked`: a sample `Specification<User, (Guid, string?)>` returns rows, and `ChangeTracker.Entries()` is empty.
    - `Entity_spec_returns_tracked_entity`.
  - `tests/Gameya.ArchitectureTests/DataAccessTests.Handlers_do_not_query_DbSets_directly`: a Mono.Cecil IL scan. No method in types implementing `IRequestHandler<,>` calls `System.Linq.Queryable::Where|Select|First*|Single*|Any|Count` or `EntityFrameworkQueryableExtensions::*Async`. The spec extensions are the only permitted read path. `DbSet.Add`/`Remove` are allowed.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement the behaviors and DI.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(app): MediatR pipeline with validation, authorization, transaction, activity log [FR-LOG-01]`.

---

### Task 6: Persistence, database roles, and log immutability

**Files:**
- Create: `src/Gameya.Infrastructure/Persistence/{AppDbContext.cs,Configurations/{UserConfiguration,OtpChallengeConfiguration,RefreshTokenConfiguration,ActivityLogEntryConfiguration}.cs,ActivityLogImmutabilityInterceptor.cs}`, `Migrations/*` (generated), `src/Gameya.Infrastructure/DependencyInjection.cs`, `docker/postgres/init.sql`, `infra/sql/roles.sql`
- Test: `tests/Gameya.Api.IntegrationTests/Infrastructure/{PostgresFixture.cs,ActivityLogImmutabilityTests.cs}`

**Interfaces:**
- Consumes: domain entities, `IAppDbContext`.
- Produces:
  - `AppDbContext : DbContext, IAppDbContext`.
  - `static IServiceCollection AddInfrastructure(this IServiceCollection, IConfiguration, IHostEnvironment)`.
  - Connection strings `ConnectionStrings:App` (`gameya_app`) and `ConnectionStrings:Migrator` (`gameya_migrator`).
  - `PostgresFixture` (xUnit collection fixture) exposes `string AppConnectionString` and `string MigratorConnectionString`, and migrates on start.
- **Mapping decisions:**
  - `PhoneNumber` maps to `text` via a value converter (E.164).
  - `users.phone` has a unique index filtered on `deleted_at IS NULL`.
  - `users.email` has a unique index filtered on `email IS NOT NULL` (verified only).
  - `users.pending_email_token_hash` is indexed.
  - `otp_challenges.phone` is unique, with an `xmin` concurrency token.
  - `refresh_tokens.token_hash` is unique; `family_id` and `user_id` are indexed.
  - `activity_log.details` is `jsonb`, indexed on `(gameya_id, occurred_at)`.
  - `email_send_times` is `jsonb`. `roles` is `text[]`.
- **SQL:**
  - `roles.sql` is idempotent: `DO $$ ... CREATE ROLE gameya_migrator LOGIN / gameya_app LOGIN IF NOT EXISTS $$`, then `CREATE SCHEMA IF NOT EXISTS hangfire AUTHORIZATION gameya_app`, then `ALTER DEFAULT PRIVILEGES FOR ROLE gameya_migrator IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO gameya_app` and `... GRANT USAGE, SELECT ON SEQUENCES TO gameya_app`. Passwords come from psql variables `:'migrator_password'` / `:'app_password'`.
  - `docker/postgres/init.sql` calls the same statements with dev passwords.
  - The initial migration ends with `migrationBuilder.Sql("REVOKE UPDATE, DELETE, TRUNCATE ON activity_log FROM gameya_app;")`.

- [ ] **Step 1: Write failing integration tests.**
  - The fixture uses Testcontainers `postgres:17`, runs `roles.sql` as superuser, then migrates as `gameya_migrator`.
  - `App_role_cannot_update_activity_log`: inserting a row as `gameya_app` succeeds. Raw `UPDATE activity_log SET action='X'` as `gameya_app` throws `PostgresException` with `SqlState == "42501"`. This is **AC-F5**.
  - `App_role_cannot_delete_activity_log` (same SqlState).
  - `Interceptor_throws_when_entry_modified_through_ef`: load an entry, modify `Note` via `Entry().Property`, `SaveChanges` → `InvalidOperationException`.
  - `Phone_unique_ignores_deleted_users`.
- [ ] **Step 2:** Run `dotnet test tests/Gameya.Api.IntegrationTests` (Docker running). Expected: FAIL.
- [ ] **Step 3:** Implement the context, configurations, and interceptor. Then run `dotnet ef migrations add Initial -p src/Gameya.Infrastructure -s src/Gameya.Api` and append the REVOKE SQL.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(infra): PostgreSQL persistence with append-only activity log [FR-LOG-03]`.

---

### Task 7: API host, error mapping, OTP request and verify

**Files:**
- Create: `src/Gameya.Api/Program.cs`, `Infrastructure/{ProblemDetailsMapping.cs,HttpCurrentUser.cs,RateLimiting.cs}`, `Endpoints/{AuthEndpoints.cs,DevEndpoints.cs}`, `src/Gameya.Application/Auth/{RequestOtp.cs,VerifyOtp.cs,SessionResult.cs,SessionIssuer.cs}`, `Abstractions/{ISmsSender.cs,ISecretHasher.cs,ITokenService.cs}`, `src/Gameya.Infrastructure/Security/{HmacSecretHasher.cs,JwtTokenService.cs}`, `Sms/{LoggingSmsSender.cs,DevOtpStore.cs}`
- Test: `tests/Gameya.Api.IntegrationTests/Auth/OtpFlowTests.cs`, `tests/Gameya.Api.IntegrationTests/GameyaApiFactory.cs`

**Interfaces:**
- Consumes: `OtpChallenge`, `User`, `RefreshToken`, the pipeline.
- Produces:
  - `ISmsSender.SendAsync(PhoneNumber to, string message, CancellationToken)`.
  - `ISecretHasher.Hash(string value) → string`.
  - `ITokenService.CreateAccessToken(User) → (string Token, DateTimeOffset ExpiresAt)` (claims `sub`, `role`*, `lang`; issuer and audience from `Jwt:*`; HS256 key `Jwt:SigningKey`) and `ITokenService.NewOpaqueToken() → string` (32 bytes, base64url).
  - `record SessionResult(string AccessToken, DateTimeOffset AccessTokenExpiresAt, bool ProfileComplete, string RefreshToken)`. `RefreshToken` never goes in a response body; the endpoint moves it to the cookie.
  - `SessionIssuer.IssueAsync(User user, Guid? familyId, CancellationToken) → SessionResult` throws `USER_BLOCKED` when `user.IsBlocked`, creates a `RefreshToken`, and is reused by Task 8.
  - **Commands:**
    - `RequestOtpCommand(string Phone, Language Language) : ICommand<RequestOtpResult(DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt)>` with `[PublicAccess]`. It creates or loads the challenge, generates a code with `RandomNumberGenerator.GetInt32(0, 1_000_000)` formatted `D6`, calls `RecordSend(hash)`, and sends the SMS text from `SmsTemplates.Otp(language, code)`:
      - ar: `رمز الدخول إلى جمعية: {code}. صالح لمدة 5 دقائق.`
      - en: `Your Gam'eya login code is {code}. Valid for 5 minutes.`
    - `VerifyOtpCommand(string Phone, string Code) : ICommand<SessionResult>` with `[PublicAccess]`. It verifies, finds a non-deleted user by phone or calls `User.Register(Guid.CreateVersion7(), phone, Language.Ar, now)`, then issues a session.
  - **Endpoints:**
    - `POST /api/auth/otp/request` → 202 `{expiresAt, resendAvailableAt}`.
    - `POST /api/auth/otp/verify` → 200 `{accessToken, accessTokenExpiresAt, profileComplete}` with `Set-Cookie: gameya_rt=...`.
    - Both are under rate-limit policy `"otp"`: fixed window of 10 requests per 10 minutes per client IP, 429 + `RATE_LIMITED`.
  - `GET /api/dev/otp/{phone}` → `{code}` from `DevOtpStore`. Mapped only when the environment is `Development` or `E2E`.
  - `ProblemDetailsMapping`:
    - `DomainException` → 400, except `UNAUTHENTICATED` (401), `FORBIDDEN` (403), `NOT_FOUND` (404), `OTP_LOCKED`/`OTP_RESEND_*`/`EMAIL_RESEND_LIMIT` (429), `ACCOUNT_HAS_ACTIVE_GAMEYAS`/`EMAIL_IN_USE` (409), `USER_BLOCKED` (403).
    - `DbUpdateConcurrencyException` → 409 `CONCURRENCY_CONFLICT`.
    - Unique violation on `otp_challenges.phone` → retry the command once, then 409.
    - Unhandled → 500 with code `INTERNAL`, no details.

- [ ] **Step 1: Write failing tests** with `GameyaApiFactory : WebApplicationFactory<Program>` on `PostgresFixture`, environment `E2E`, and a fake `IClock` (`MutableClock`):
  - `Request_then_verify_creates_user_and_returns_session`: 202, then verify with the code from `/api/dev/otp` → 200, `profileComplete == false`, `gameya_rt` cookie is HttpOnly+Secure+SameSite=Strict, and the activity log has `USER_REGISTERED`.
  - `Verify_with_existing_phone_logs_in_same_user`.
  - `Resend_within_60s_returns_429_OTP_RESEND_TOO_SOON` (**AC-F2**, part 1).
  - `Five_wrong_codes_lock_the_number` (**AC-F2**, part 2): the 5th returns 429 `OTP_LOCKED`, and requesting a new code also returns `OTP_LOCKED`.
  - `Invalid_phone_returns_400_INVALID_PHONE`.
  - `Parallel_requests_for_same_phone_never_500` (Review Focus 3): 2 concurrent requests return statuses ⊆ {202, 429}.
  - `Blocked_user_cannot_log_in_USER_BLOCKED`.
  - `Unknown_exception_returns_problem_with_code_INTERNAL_and_no_stack`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement `Program.cs` (AddApplication, AddInfrastructure, JWT bearer auth, ProblemDetails, rate limiter, endpoint groups), the handlers, and the adapters. `HttpCurrentUser` reads `sub` and `role` claims.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(auth): OTP request and verify with lockout and rate limiting [FR-AUTH-01]`.

---

### Task 8: Sessions: refresh rotation, reuse detection, logout

**Files:**
- Create: `src/Gameya.Application/Auth/{RefreshSession.cs,Logout.cs,LogoutAll.cs}`, `src/Gameya.Api/Infrastructure/RefreshCookie.cs`; modify `AuthEndpoints.cs`
- Test: `tests/Gameya.Api.IntegrationTests/Auth/SessionTests.cs`

**Interfaces:**
- Consumes: `SessionIssuer`, `RefreshToken`.
- Produces:
  - `RefreshSessionCommand(string RefreshToken) : ICommand<SessionResult>` with `[PublicAccess]`. Rules:
    - Unknown or expired → `REFRESH_TOKEN_INVALID`.
    - Revoked and `WasRotatedWithin(10 s)` → `REFRESH_TOKEN_INVALID` **without** revoking the family (grace for concurrent tabs).
    - Revoked otherwise → revoke every token in the family, then `REFRESH_TOKEN_REUSED`.
    - Active → revoke with `replacedBy` and issue a new token in the same family.
  - `LogoutCommand(string? RefreshToken)` with `[PublicAccess]` revokes the family and is idempotent.
  - `LogoutAllCommand` with `[RequiresAuth]` revokes all of the user's tokens and calls `MarkLoggedOutEverywhere`.
  - `RefreshCookie.Write(HttpResponse, string token, DateTimeOffset expires)` / `Clear(HttpResponse)` / `Read(HttpRequest) → string?`.
  - **Endpoints:**
    - `POST /api/auth/refresh` → 200 session + new cookie. On failure → 401 with the code, and the cookie is cleared.
    - `POST /api/auth/logout` → 204 and clears the cookie.
    - `POST /api/auth/logout-all` → 204.

- [ ] **Step 1: Write failing tests:**
  - `Refresh_rotates_cookie_and_returns_new_access_token`.
  - `Reusing_rotated_token_after_grace_revokes_family` (**AC-F3**): rotate A→B, advance the clock 11 s, present A → 401 `REFRESH_TOKEN_REUSED`, then presenting B → 401 `REFRESH_TOKEN_INVALID`.
  - `Reusing_rotated_token_within_grace_does_not_revoke_family` (Review Focus 1): present A at +5 s → 401 `REFRESH_TOKEN_INVALID`, and B still refreshes.
  - `Refresh_extends_expiry_sliding_30_days`: after 29 days of refreshing, the token is still valid. Without refresh, it's invalid at 30 days + 1 s.
  - `Logout_all_revokes_every_device_and_logs_LOGGED_OUT_ALL`: two sessions, logout-all from one, both refreshes → 401.
  - `Blocked_user_refresh_fails_USER_BLOCKED`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(auth): refresh rotation with reuse detection and logout everywhere [FR-AUTH-03]`.

---

### Task 9: Profile and photo

**Files:**
- Create: `src/Gameya.Application/Users/{GetMe.cs,UpdateProfile.cs,SetPhoto.cs,RemovePhoto.cs,MeDto.cs}`, `Abstractions/{IFileStore.cs,IImageProcessor.cs}`, `src/Gameya.Infrastructure/Files/{BlobFileStore.cs,MagickImageProcessor.cs}`, `src/Gameya.Api/Endpoints/MeEndpoints.cs`
- Test: `tests/Gameya.Api.IntegrationTests/Users/ProfileTests.cs`, `tests/Gameya.Api.IntegrationTests/TestFiles/{photo.jpg,photo.heic,doc.pdf}`

**Interfaces:**
- Produces:
  - `IFileStore.UploadAsync(string container, string key, Stream content, string contentType, CancellationToken)`, `DeleteAsync(container, key, ct)`, and `Uri GetReadUrl(string container, string key, TimeSpan lifetime)` (SAS, 5 min). Containers: `"profile-photos"`, later `"proofs"`.
  - `IImageProcessor.ToJpeg(Stream input, int maxSize) → Stream`. Throws `FILE_TYPE_NOT_ALLOWED` unless Magick identifies the format as JPEG, PNG, or HEIC (content sniffing, not extension).
  - `record MeRow(Guid Id, string? FullName, string PhoneE164, string? Email, DateTimeOffset? EmailVerifiedAt, string? PendingEmail, string? PhotoKey, Language Language, bool ProfileComplete)` is produced by `MeRowSpec` (SQL projection). `GetMeHandler` turns it into `MeDto`, adding `PhotoUrl = fileStore.GetReadUrl(...)` because SAS URLs can't be computed in SQL.
  - `record MeDto(Guid Id, string? FullName, string Phone, string? Email, bool EmailVerified, string? PendingEmail, Uri? PhotoUrl, string Language, bool ProfileComplete)`. `Phone` here is the caller's own E.164. This is the only place a raw phone may appear, and it's annotated `[OwnPhone]` for the Task 11 architecture test.
  - **Requests:**
    - `GetMeQuery` and `UpdateProfileCommand(string FullName, string Language)` (validator: Language ∈ {ar, en}).
    - `SetPhotoCommand(Stream Content, long Length)`: `Length > 5*1024*1024` → `FILE_TOO_LARGE`. Key is `{userId}/{Guid.CreateVersion7()}.jpg`. Deletes the old blob.
    - `RemovePhotoCommand`.
    - All `[RequiresAuth]`.
  - **Endpoints:** `GET /api/me`, `PUT /api/me/profile`, `PUT /api/me/photo` (multipart field `file`, with `RequestSizeLimit` 5 MB + 64 KB), `DELETE /api/me/photo`.
  - **Config:** `Storage:ConnectionString` (Azurite in dev and tests via Testcontainers `mcr.microsoft.com/azure-storage/azurite`).

- [ ] **Step 1: Write failing tests:**
  - `UpdateProfile_sets_name_and_completes_profile_and_logs_PROFILE_UPDATED`.
  - `UpdateProfile_rejects_one_char_name_with_NAME_INVALID`.
  - `Upload_jpg_returns_photo_url_resized_to_512` (download the SAS URL and check max dimension ≤ 512).
  - `Upload_heic_is_accepted_and_converted_to_jpeg` (Review Focus 4).
  - `Upload_pdf_renamed_jpg_returns_FILE_TYPE_NOT_ALLOWED`.
  - `Upload_6MB_returns_FILE_TOO_LARGE`.
  - `Me_requires_auth_401_UNAUTHENTICATED`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement (Magick.NET-Q8-AnyCPU, Azure.Storage.Blobs).
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(profile): profile and photo upload [FR-AUTH-02]`.

---

### Task 10: Email verification

**Files:**
- Create: `src/Gameya.Application/Users/{ChangeEmail.cs,ResendEmailVerification.cs,VerifyEmail.cs,VerifiedEmailResolver.cs}`, `Abstractions/IEmailSender.cs`, `src/Gameya.Infrastructure/Email/{SmtpEmailSender.cs,AcsEmailSender.cs}`, `EmailTemplates.cs`
- Test: `tests/Gameya.Api.IntegrationTests/Users/EmailVerificationTests.cs`, `tests/Gameya.Application.Tests/VerifiedEmailResolverTests.cs`

**Interfaces:**
- Produces:
  - `record EmailMessage(string To, string Subject, string HtmlBody, string TextBody)`.
  - `IEmailSender.SendAsync(EmailMessage, CancellationToken)`.
  - `VerifiedEmailResolver.ResolveAsync(Guid userId, CancellationToken) → string?` returns `user.Email` only when `EmailVerifiedAt != null && !IsDeleted`. Sub-project 4 must send through this.
  - **Commands:**
    - `ChangeEmailCommand(string Email)` with `[RequiresAuth]`. The validator checks email format and max 254 characters, and lowercases/trims. Creates a token, calls `RequestEmailChange`, and sends a link `{App:WebBaseUrl}/verify-email?token={token}` in the user's language.
    - `ResendEmailVerificationCommand` with `[RequiresAuth]`.
    - `VerifyEmailCommand(string Token)` with `[PublicAccess]`. Finds the user by `PendingEmailTokenHash`, else `EMAIL_TOKEN_INVALID`. If another non-deleted user already has `Email == pending`, throws `EMAIL_IN_USE`.
  - **Endpoints:** `PUT /api/me/email` → 202, `POST /api/me/email/resend` → 202, `POST /api/auth/email/verify {token}` → 204.
  - **Senders:** `SmtpEmailSender` (MailKit, dev → Mailpit `localhost:1025`, tests → Testcontainers `axllent/mailpit`); `AcsEmailSender` (prod, `Email:Acs:ConnectionString`, `Email:From`). Selected by `Email:Provider` = `Smtp|Acs`.

- [ ] **Step 1: Write failing tests:**
  - `Change_email_sends_link_and_resolver_returns_null_until_verified` (**AC-F4**): read the Mailpit API `/api/v1/messages`, extract the token, verify → 204, then the resolver returns the email and the log has `EMAIL_VERIFIED`.
  - `Changing_verified_email_keeps_old_one_until_new_verified`.
  - `Expired_token_returns_EMAIL_TOKEN_EXPIRED` (clock +24 h +1 s).
  - `Fourth_send_in_hour_returns_429_EMAIL_RESEND_LIMIT`.
  - `Verifying_email_owned_by_other_account_returns_409_EMAIL_IN_USE` (Review Focus 5).
  - `Email_body_is_in_users_language` (ar subject contains `تأكيد`).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(profile): email verification before any notification email [FR-AUTH-02]`.

---

### Task 11: Account deletion and phone privacy

**Files:**
- Create: `src/Gameya.Application/Users/DeleteAccount.cs`, `Abstractions/IActiveParticipationChecker.cs`, `Security/{PhoneDisplay.cs,DisplayPhone.cs,OwnPhoneAttribute.cs}`, `src/Gameya.Infrastructure/Participation/NoParticipationChecker.cs`
- Test: `tests/Gameya.Api.IntegrationTests/Users/DeleteAccountTests.cs`, `tests/Gameya.Application.Tests/PhoneDisplayTests.cs`, `tests/Gameya.ArchitectureTests/DtoPhoneTests.cs`

**Interfaces:**
- Produces:
  - `IActiveParticipationChecker.HasRecruitingOrActiveGameyasAsync(Guid userId, CancellationToken) → Task<bool>`. `NoParticipationChecker` returns `false`. **Sub-project 2 replaces it.**
  - `DeleteAccountCommand` with `[RequiresAuth]`. If the checker returns true → `ACCOUNT_HAS_ACTIVE_GAMEYAS`. Otherwise revokes all tokens, deletes the photo blob, and calls `user.Delete(now)`. The endpoint `DELETE /api/me` → 204 and clears the cookie.
  - `readonly record struct DisplayPhone` with an internal constructor, serialized as a string by a JSON converter. `static class PhoneDisplay { DisplayPhone For(Guid viewerId, Guid subjectId, PhoneNumber phone, bool viewerOrganizesSubjectsGameya) }` returns `E164` when the viewer is the subject or `viewerOrganizesSubjectsGameya`, else `Masked`.
  - **Client display rule:** deleted users are returned with `fullName = null, isDeleted = true`, and the client shows the translation `common.deletedUser`.

- [ ] **Step 1: Write failing tests:**
  - **`PhoneDisplayTests`:** self → full; organizer → full; other member → `010****5678`.
  - **`DtoPhoneTests.No_dto_exposes_raw_phone`:** among public types in `Gameya.Application` whose names end in `Dto`/`Result`, no property is of type `PhoneNumber`, and any `string` property whose name contains `Phone` must carry `[OwnPhone]`. Other DTOs must use `DisplayPhone`.
  - **`DeleteAccountTests`:**
    - `Delete_anonymizes_and_revokes_sessions_and_logs_ACCOUNT_DELETED`.
    - `Delete_blocked_when_checker_reports_active_gameya_409` (substitute the checker in the factory).
    - `Deleted_users_phone_can_sign_up_as_new_user` (new id ≠ old id, and the old row still exists).
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit `feat(profile): account deletion and phone masking [FR-AUTH-04] [FR-AUTH-05]`.

---

### Task 12: Operations, Hangfire, Docker

**Files:**
- Create: `src/Gameya.Api/Infrastructure/{HealthChecks.cs,Observability.cs}`, `src/Gameya.Api/Dockerfile`, `docker-compose.yml`, `.dockerignore`, `src/Gameya.Api/appsettings.Development.json`
- Test: `tests/Gameya.Api.IntegrationTests/Ops/{HealthTests.cs,LogRedactionTests.cs}`

**Interfaces:**
- Produces:
  - `GET /health` → 200 JSON with entries `postgres`, `blob`, `hangfire`.
  - The Hangfire server runs in-process on schema `hangfire`. The dashboard `/hangfire` is mapped **only in Development** in Foundation. The `PlatformAdmin`-guarded production dashboard ships with the admin UI in sub-project 5, because it needs a cookie-based admin session that doesn't exist yet.
  - Serilog with request logging, a correlation id (`X-Correlation-Id`, generated when absent, echoed in the response and in ProblemDetails `traceId`), and an enricher that masks any `+20\d{10}` in messages to the `Masked` form and drops properties named `code|token|refreshToken|email`.
  - OpenTelemetry → Azure Monitor when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
  - `docker-compose.yml` services:
    - `postgres` (17, mounts `docker/postgres/init.sql`)
    - `azurite`
    - `mailpit` (1025/8025)
    - `api` (build `src/Gameya.Api/Dockerfile`, env `ASPNETCORE_ENVIRONMENT=Development`, port 8080, runs migrations via `dotnet Gameya.Api.dll --migrate`; the flag exists only for local compose and is refused outside Development)
  - Dockerfile: multi-stage, `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian, includes tzdata), non-root user.

- [ ] **Step 1: Write failing tests:**
  - `Health_reports_healthy_with_three_checks`.
  - `Correlation_id_is_echoed`.
  - `Logs_never_contain_unmasked_phone_or_otp_code`: run the OTP flow with a test Serilog sink and assert no event text contains `01012345678`, `+201012345678`, or the code. The `LoggingSmsSender` line is excluded by its source context `Gameya.Infrastructure.Sms.LoggingSmsSender`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. Then run `docker compose up --build -d` and `curl -s localhost:8080/health`. Expected: `"status":"Healthy"`.
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Commit `feat(ops): health checks, redacted logging, Hangfire, docker compose`.

---

### Task 13: Angular shell, i18n, RTL

**Files:**
- Create: `src/gameya-web/` via `npx @angular/cli@latest new gameya-web --directory src/gameya-web --style=scss --routing --ssr=false --skip-git`, then:
  - `src/app/core/i18n/{transloco.config.ts,language.service.ts}`
  - `src/app/core/format/{money.pipe.ts,cairo-date.pipe.ts,digits.ts}`
  - `src/app/layout/{public-layout.ts,app-layout.ts}`
  - `src/app/shared/{status-chip.ts,confirm-dialog.ts}`
  - `public/i18n/{ar.json,en.json}`
  - `src/styles/{_theme.scss,_fonts.scss}`
  - `.stylelintrc.json`, `proxy.conf.json`
  - root `tools/check-i18n.mjs`
- Test: co-located `*.spec.ts`. `tools/check-i18n.test.mjs` (node:test).

**Interfaces:**
- Produces:
  - **`LanguageService`:** `current: Signal<'ar'|'en'>`, `use(lang)`. Sets `document.documentElement.dir` (`rtl`/`ltr`) and `lang`, switches Transloco, and stores the choice in `localStorage['gameya.lang']` inside try/catch. Initial value: stored, else `'ar'`.
  - **`MoneyPipe`:** `amount | money` → `Intl.NumberFormat(ar ? 'ar-EG' : 'en-EG', {style:'currency', currency:'EGP'})`. Arabic output uses Arabic-Indic digits and `ج.م.`.
  - **`CairoDatePipe`:** `Intl.DateTimeFormat` with `timeZone: 'Africa/Cairo'`.
  - **`normalizeDigits(s: string): string`:** maps Arabic-Indic and Persian digits to ASCII. Used by phone and OTP inputs.
  - **`<gy-status-chip [icon] [label] [tone]>`:** always renders `mat-icon` + text. `tone` ∈ `neutral|success|warning|danger|info`.
  - **Layouts:** `AppLayout` uses bottom `nav` below 960 px and a `mat-sidenav` at ≥ 960 px (CDK `BreakpointObserver`). `PublicLayout` has no nav.
  - **`tools/check-i18n.mjs`:** exits 1 and prints missing keys per file when the flattened key sets of `ar.json` and `en.json` differ.
  - **Stylelint:** `stylelint-config-standard-scss` + `stylelint-use-logical` with rule `"csstools/use-logical": ["always", {"except": []}]`.
  - **Fonts:** IBM Plex Sans Arabic + IBM Plex Sans via Google Fonts link in `index.html`. Angular Material custom theme with tokens in `_theme.scss`.
  - `proxy.conf.json`: `/api` → `http://localhost:8080`.

- [ ] **Step 1: Write failing tests:**
  - `language.service.spec.ts`: `use('en')` sets `dir="ltr"`, `lang="en"`. Default is `rtl`/`ar`. Works when `localStorage` throws.
  - `money.pipe.spec.ts`: `2000` in ar → contains `٢٬٠٠٠` and `ج.م`. In en → contains `2,000` and `EGP`.
  - `cairo-date.pipe.spec.ts`: `'2026-07-01T21:30:00Z'` in en → `Jul 2, 2026`.
  - `digits.spec.ts`: `normalizeDigits('٠١٠')==='010'`.
  - `status-chip.spec.ts`: renders both icon and label text.
  - `check-i18n.test.mjs`: fixtures with a missing key → exit 1 and the missing path printed. Equal keys → exit 0.
- [ ] **Step 2:** Run `npm test` in `src/gameya-web` and `node --test tools/`. Expected: FAIL.
- [ ] **Step 3:** Implement. Translation files start with `app.*`, `common.*` (including `deletedUser`: "مستخدم محذوف" / "Deleted user"), `errors.<CODE>` for **every** code in `ErrorCodes` plus `INTERNAL`, and `status.*`.
- [ ] **Step 4:** Run the tests, `npx stylelint "src/**/*.scss"`, and `ng build`. Expected: all PASS. Toggling language with `ng serve` flips the layout without reload (**AC-F6**, checked manually here and by e2e in Task 15).
- [ ] **Step 5:** Commit `feat(web): Angular shell with Arabic/English, RTL and Cairo formatting`.

---

### Task 14: Git hooks (Husky, lint-staged, commitlint)

**Files:**
- Create: root `package.json` (private, `devDependencies`: husky, lint-staged, @commitlint/cli, @commitlint/config-conventional, prettier; `scripts`: `prepare: "husky"`, `check:i18n: "node tools/check-i18n.mjs"`), `.lintstagedrc.mjs`, `commitlint.config.mjs`, `.husky/{pre-commit,commit-msg,pre-push}`, `.prettierrc`
- Test: manual verification steps below (hooks are shell glue; CI re-runs the same commands in Task 16)

**Interfaces:**
- **`.lintstagedrc.mjs`:**
  - `src/gameya-web/**/*.{ts,html}` → `eslint --fix` (cwd `src/gameya-web`), then `prettier --write`.
  - `**/*.scss` → `stylelint --fix`.
  - `**/*.{json,md}` → `prettier --write`.
  - `**/*.cs` → `(files) => 'dotnet format Gameya.sln --include ' + files.join(' ')`.
  - `src/gameya-web/public/i18n/*.json` → `node tools/check-i18n.mjs`.
- **`commitlint.config.mjs`:** extends `config-conventional`, allowed scopes are open, `subject-case` is off (so Arabic or mixed case is OK), `header-max-length` 100. Optional trailing requirement ID matching `\[(FR|NFR|AC)-[A-Z]+-\d{2}\]`.
- **pre-push:** `dotnet build Gameya.sln -warnaserror && dotnet test tests/Gameya.Domain.Tests --no-build && npm --prefix src/gameya-web exec tsc -- --noEmit -p tsconfig.app.json`.

- [ ] **Step 1:** Run `npm install` at the root. Expected: `.husky/_` is created.
- [ ] **Step 2: Verify commit-msg.** `git commit --allow-empty -m "bad message"` → rejected by commitlint. `git commit --allow-empty -m "chore: hook check"` → accepted. Then `git reset --soft HEAD~1`.
- [ ] **Step 3: Verify pre-commit.** Stage a `.scss` file containing `margin-left: 4px` → commit rejected by `csstools/use-logical`. Stage `ar.json` with an extra key → rejected by check-i18n. Revert both.
- [ ] **Step 4: Verify pre-push.** `git push --dry-run` runs the build, domain tests, and tsc. Expected: exit 0.
- [ ] **Step 5:** Commit `chore: husky, lint-staged and commitlint hooks`.

---

### Task 15: Angular auth and profile screens

**Files:**
- Create in `src/gameya-web/src/app/`:
  - `core/auth/{auth.store.ts,auth.api.ts,auth.interceptor.ts,error.interceptor.ts,auth.guards.ts,session-bootstrap.ts}`
  - `features/landing/landing.page.ts`
  - `features/auth/{phone.page.ts,otp.page.ts,name-setup.page.ts}`
  - `features/profile/{profile.page.ts,email-section.ts,photo-section.ts,danger-zone.ts}`
  - `features/verify-email/verify-email.page.ts`
  - `shared/{phone-input.ts,otp-input.ts}`
  - `app.routes.ts`
- Test: co-located `*.spec.ts`. `e2e/smoke.spec.ts` (Playwright, root `playwright.config.ts` at `src/gameya-web`).

**Interfaces:**
- Consumes: the API endpoints from Tasks 7–11 and `LanguageService`.
- Produces:
  - **`AuthStore` (signals):** `accessToken`, `profileComplete`, `isAuthenticated` (computed), `setSession(s)`, `clear()`.
  - **`AuthApi`:** typed methods for every endpoint, all with `withCredentials: true`.
  - **`authInterceptor`:**
    - Attaches `Authorization: Bearer`.
    - On a 401 from a non-`/api/auth/` URL, runs a single refresh shared by concurrent requests in this tab (one shared promise) **and across tabs** (`navigator.locks.request('gameya-refresh', ...)` when available). It then retries once. If the refresh fails, it clears the store and navigates to `/login`.
  - **`errorInterceptor`:** reads ProblemDetails `code` and shows a `MatSnackBar` with `translate('errors.' + code, params)`. Skipped for codes the page handles inline (`OTP_*`, `NAME_INVALID`, `VALIDATION_FAILED`).
  - **`session-bootstrap`:** an `provideAppInitializer` that tries `POST /api/auth/refresh` once at startup. On success it loads `/api/me` and applies `me.language` to `LanguageService`.
  - **Guards:** `authGuard` (not authenticated → `/login`) and `profileCompleteGuard` (incomplete → `/welcome/name`).
  - **Routes (public layout):** `/` landing, `/login`, `/login/code`, `/verify-email`. **Routes (app layout, guarded):** `/welcome/name`, `/profile`. Home is `/profile` for now; sub-project 2 adds `/gameyas`.
  - **`<gy-phone-input>`:** a `ControlValueAccessor`. Normalizes digits, `inputmode="tel"`, validates `^01[0125]\d{8}$` client-side with error `errors.INVALID_PHONE`.
  - **`<gy-otp-input>`:** 6 boxes, auto-advance, backspace back, paste of 6 digits (any digit set) fills all, `autocomplete="one-time-code"`, and emits `completed` when full.
  - **Pages:**
    - **Landing:** product pitch plus the mandatory notice (key `landing.noMoneyNotice`, ar: "التطبيق لا يحتفظ بأي أموال ولا ينقلها. كل الدفع بيتم بينكم مباشرة، والتطبيق بيسجّله ويأكّده بس.", en: "The app never holds or moves money. All payments happen directly between you; the app only records and confirms them."). This notice is also shown on name setup.
    - **OTP page:** resend countdown from `resendAvailableAt`, inline `OTP_INVALID` (attempts left), and `OTP_LOCKED` (until time via `CairoDatePipe`).
    - **Profile:**
      - Name, language toggle, photo (upload or remove, client pre-check ≤ 5 MB).
      - Email section with states none, unverified (with a "resend" button), and verified (`gy-status-chip`).
      - Danger zone: log out, log out everywhere (confirm dialog), delete account (confirm dialog that requires typing the localized word "حذف"/"DELETE").
- **Playwright:** runs against `docker compose up` + `ng serve` with `E2E` env. The OTP code comes from `GET /api/dev/otp/{phone}`.

- [ ] **Step 1: Write failing tests:**
  - `auth.interceptor.spec.ts`: three parallel 401s trigger exactly one refresh call, and all three retry with the new token. A failed refresh navigates to `/login` and clears the store.
  - `otp-input.spec.ts`: paste `'٤٥٦١٢٣'` fills the boxes and emits `'456123'`.
  - `phone-input.spec.ts`: `'010 1234 5678'` is valid and the value is `'01012345678'`.
  - `auth.guards.spec.ts`: incomplete profile redirects to `/welcome/name`.
  - `e2e/smoke.spec.ts`, `signup_in_arabic_reaches_profile` (**AC-F1**): `/` shows the no-money notice, then phone, code, name. The profile shows the name, and `html[dir=rtl]`.
  - `e2e/smoke.spec.ts`, `language_switch_flips_direction_without_reload` (**AC-F6**): click English → `html[dir=ltr]`, a currency sample shows `EGP`, and `performance.navigation` count is unchanged.
- [ ] **Step 2:** Run `npm test` and `npx playwright test`. Expected: FAIL.
- [ ] **Step 3:** Implement. Every string goes in both i18n files.
- [ ] **Step 4:** Run the tests plus `node tools/check-i18n.mjs` and `npx stylelint`. Expected: PASS. Then manually check at a 360 px viewport (Playwright project `mobile` with `viewport: {width: 360, height: 740}` runs the smoke suite).
- [ ] **Step 5:** Commit `feat(web): OTP login, name setup and profile screens [FR-AUTH-01] [FR-AUTH-02]`.

---

### Task 16: CI workflow

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- **Triggers:** `pull_request` and `push` to `main`. Jobs run in parallel.
  - **`backend`:** setup-dotnet from `global.json`, then `dotnet restore`, `dotnet build -warnaserror`, `dotnet format --verify-no-changes`, `dotnet test` for all four test projects (ubuntu has Docker for Testcontainers). Uploads TRX results.
  - **`frontend`:** Node 22, then `npm ci` (root and web), `npm --prefix src/gameya-web run lint`, `npx stylelint`, `node tools/check-i18n.mjs`, `npm --prefix src/gameya-web test -- --watch=false`, `ng build --configuration production`.
  - **`e2e`** (needs backend and frontend): `docker compose up -d --wait`, `ng serve` in the background, `npx playwright install --with-deps chromium`, `npx playwright test`. Uploads the report on failure.
  - **`commits`** (PR only): `npx commitlint --from ${{ github.event.pull_request.base.sha }} --to ${{ github.event.pull_request.head.sha }}`.
  - **`image`:** `docker build`, then a Trivy scan with `severity: CRITICAL,HIGH`, `exit-code: 1`, `ignore-unfixed: true`.
- **Branch protection** (document in the PR description, owner applies): all five jobs required on `main`.

- [ ] **Step 1:** Write the workflow.
- [ ] **Step 2:** Validate with `npx --yes action-validator .github/workflows/ci.yml` (or `actionlint` if installed). Expected: no errors.
- [ ] **Step 3: Prove the gate (AC-F7).** Push branch `ci/gate-check` with four separate commits, each breaking one thing: a failing domain test, a missing i18n key, `margin-left` in SCSS, and a non-conventional message pushed with `--no-verify`. Expected: each corresponding job fails on GitHub. Then delete the branch.
- [ ] **Step 4:** Commit `ci: build, test, lint, e2e, commitlint and image scan`.

---

### Task 17: Azure infrastructure and deploy pipeline

**Files:**
- Create:
  - `infra/main.bicep`, `infra/main.staging.bicepparam`, `infra/main.prod.bicepparam`
  - `infra/modules/{postgres.bicep,appservice.bicep,staticweb.bicep,storage.bicep,keyvault.bicep,acr.bicep,email.bicep,monitoring.bicep}`
  - `.github/workflows/deploy.yml`
  - `src/gameya-web/staticwebapp.config.json`
  - `docs/deployment.md`

**Interfaces:**
- **Bicep parameters:** `environmentName` (`staging|prod`), `location` (default `uaenorth`), `postgresSku` (staging `Standard_B1ms`, prod `Standard_B2s`).
- **Bicep outputs:** `apiAppName`, `swaName`, `acrLoginServer`, `postgresFqdn`, `keyVaultName`.
- **Resources:**
  - **PostgreSQL** Flexible Server 17: `backup.backupRetentionDays: 35`, public access with a firewall rule allowing Azure services, `geoRedundantBackup` Enabled in prod.
  - **App Service:** Linux plan (staging B1, prod P0v3), container from ACR via system-assigned managed identity (`AcrPull`), slot `staging`, health check path `/health`.
  - **Key Vault:** RBAC, app identity has `Key Vault Secrets User`. Secrets `ConnectionStrings--App`, `Jwt--SigningKey`, `Security--HashKey`, `Email--Acs--ConnectionString`, `Sms--ApiKey`, `MediatR--LicenseKey`, and the DB admin and role passwords. App settings use `@Microsoft.KeyVault(...)` references.
  - **Storage** with a private `profile-photos` container.
  - **ACS Email** with an Azure-managed domain until the custom domain exists.
  - **Application Insights** (workspace-based).
  - **Static Web Apps:** Standard tier with a **linked backend** to the App Service, so `/api/*` is same-origin and the `SameSite=Strict` cookie works without a custom domain. This replaces the spec's "shared parent domain" requirement, which needs a domain that is still an open item.
- **`deploy.yml`:**
  - **Triggers:** `push` to `main` → environment `staging`. `push` of tag `v*` → environment `production` (GitHub Environment with required reviewer).
  - `permissions: id-token: write`. `azure/login` with OIDC (secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`).
  - **Steps:**
    1. `az deployment group create` with the env's bicepparam.
    2. Build and push the image `gameya-api:${{ github.sha }}` to ACR.
    3. Add a temporary firewall rule for the runner IP (`curl -s ifconfig.me`).
    4. Run `psql` with `infra/sql/roles.sql` as admin (passwords from Key Vault via `az keyvault secret show`).
    5. `dotnet ef migrations bundle -p src/Gameya.Infrastructure -s src/Gameya.Api -r linux-x64 --self-contained -o efbundle`, then `./efbundle --connection "<migrator conn>"`.
    6. Remove the firewall rule in an `if: always()` step.
    7. Set the slot container image.
    8. Poll `https://<app>-staging.azurewebsites.net/health` up to 5 minutes for `Healthy`.
    9. `az webapp deployment slot swap`.
    10. `ng build --configuration production`, then `Azure/static-web-apps-deploy` with `skip_app_build: true`.
- **`staticwebapp.config.json`:** `navigationFallback` → `/index.html` excluding `/api/*`, plus security headers (CSP `default-src 'self'; font-src https://fonts.gstatic.com; style-src 'self' https://fonts.googleapis.com 'unsafe-inline'; img-src 'self' blob: https://*.blob.core.windows.net`).
- **`docs/deployment.md`:** one-time owner steps: create the Entra app registration with a federated credential per environment, set the three GitHub secrets, create GitHub Environments `staging`/`production` with a reviewer on production, seed Key Vault secrets, and choose the SMS provider (Cequens or SMS Misr) and set `Sms:Provider`. The production SMS adapter `ProviderSmsSender` is implemented against the chosen provider's HTTP API in a follow-up task once the contract exists. Until then, staging uses `LoggingSmsSender` behind `Sms:Provider=Logging`, and production deploy refuses to start with `Logging` (startup check).

- [ ] **Step 1:** Write the Bicep. Validate with `az bicep build --file infra/main.bicep`. Expected: no errors, no warnings.
- [ ] **Step 2:** Write the startup check test in `tests/Gameya.Api.IntegrationTests/Ops/StartupGuardTests.cs`: `Production_with_Logging_sms_provider_fails_to_start` (factory with environment `Production`, `Sms:Provider=Logging` → host build throws `InvalidOperationException` whose message contains `Sms:Provider`). Run it and expect FAIL, implement it, run again and expect PASS.
- [ ] **Step 3:** Write `deploy.yml` and validate it with actionlint.
- [ ] **Step 4: AC-F8 (requires the owner's Azure setup from `docs/deployment.md`).** Merge to `main`. Expected: the workflow is green, `/health` on staging returns Healthy, and the `__EFMigrationsHistory` table lists `Initial`. If Azure access isn't set up yet, stop here and report that AC-F8 is pending the owner's one-time setup. Do not claim it passes.
- [ ] **Step 5:** Commit `ci(deploy): Azure infrastructure as Bicep and gated deploy pipeline`.

---

## Spec coverage check

| Spec section | Task(s) |
|---|---|
| §4.1 structure, pipeline, errors, clock | 1, 2, 5, 7 |
| §4.2 OTP, tokens, blocking | 3, 4, 7, 8 |
| §4.3 profile, email verification, deletion, masking | 4, 9, 10, 11 |
| §4.4 activity log, immutability, rendering codes | 4, 5, 6 (rendering of log sentences starts in sub-project 2, the first log UI) |
| §4.5 Angular shell and screens | 13, 15 |
| §4.6 Husky | 14 |
| §4.7 environments, CI/CD, ops | 12, 16, 17 |
| §5 testing strategy | every task, plus 15 (e2e) |
| §6 AC-F1 … AC-F8 | 15, 7, 8, 10, 6, 13/15, 16, 17 |

**Deviations from the spec**, all listed for owner review:
1. Minimal-API endpoint groups instead of controllers.
2. A Static Web Apps linked backend instead of a shared parent domain.
3. The Hangfire dashboard is Development-only until sub-project 5.
4. The production SMS adapter is deferred until a provider contract exists, and production refuses to start without one.
