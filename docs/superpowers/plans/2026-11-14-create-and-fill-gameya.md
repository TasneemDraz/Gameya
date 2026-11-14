# Create and Fill a Gam'eya Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An organizer creates, configures and opens a gam'eya; people preview it by link, request shares or accept direct invites; the organizer approves, rejects, invites, removes and cancels; members leave and re-accept changed terms; every change lands in the activity log.

**Architecture:** One `GameyaCircle` aggregate (Domain) owns memberships, shares, join requests and pending invites, and enforces every rule. Application commands load the aggregate through a filtered-include spec, call one domain method, and let the existing pipeline (validation → authorization → transaction → activity log) do the rest. A new `AuditingInterceptor` stamps audit columns and touches the root row on any child change, so the root's `rowversion` guards the whole aggregate. Angular gets two lazy features: `gameyas` (authenticated) and `invite` (public).

**Tech Stack:** .NET 10, EF Core 10 + SQL Server, MediatR, FluentValidation, Ardalis.Specification; xUnit, Shouldly, NSubstitute, Testcontainers. Angular 21 (standalone, signals), Angular Material, Transloco, Vitest, Playwright.

**Spec:** `docs/superpowers/specs/2026-11-14-create-and-fill-gameya-design.md` (builds on `docs/superpowers/specs/2026-11-03-foundation-design.md`; requirements in `docs/requirements.md`)

## Global Constraints

- Branch `feat/create-and-fill-gameya`. Conventional Commits; end each commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **The aggregate class is `GameyaCircle`, not `Gameya`** (namespace `Gameya.Domain.Gameyas`). A type named `Gameya` cannot be referenced inside the `Gameya.*` namespaces because the root namespace wins name lookup. Table, routes, DbSet (`Gameyas`) and error codes keep the spec's names.
- Data access: handlers read only through named specs in `<Feature>/Specifications/` (`DataAccessTests` enforces it). Projections are `Specification<TEntity, TRow>`; handlers finish what SQL cannot (phone display, URLs).
- Time: never `DateTime(Offset).UtcNow` outside `SystemClock`. Handlers compute `today = CairoCalendar.ToCairoDate(clock.UtcNow)`.
- Money: `decimal`, column `decimal(18,2)`, currency `"EGP"` only.
- Field rules (FR-GAM-01), exact: name 3–60 chars trimmed; description ≤ 500; amount whole number 100–1,000,000; share count 2–50 and ≥ assigned + reserved; frequency `Monthly` (default) / `Weekly`; start date ≥ Cairo today when set and when opening; grace days 0–10 (default 3); payout timing `AfterFullCollection` (default) / `FixedDate`; payout offset 0–10, required only with `FixedDate` (must be null otherwise); order method `Manual` / `RandomDraw` / `ByRequest`; max shares per member 1–5 (default 2); organizer shares 1..max when participating, 0 otherwise; rules text ≤ 2,000; reasons ≤ 500; removal reason 5–500.
- Invite code: 6 chars from `ABCDEFGHJKMNPQRSTUVWXYZ23456789`, cryptographic RNG. Link `/j/{code}`.
- Non-members get 404 `GAMEYA_NOT_FOUND` on every gam'eya-scoped endpoint, never 403.
- Activity details never contain phone numbers or person names, and never the invite code. Free-text reasons go in the event's `Note`.
- Nothing is deleted; every removal is a status change (only `MemberShare` rows may be removed when the organizer lowers their own share count).
- Frontend: all text in `public/i18n/ar.json` and `en.json` (`npm run i18n:check` must pass); default `ar`; CSS logical properties only; statuses are icon + text (`gy-status-chip`); works at 360 px.
- Every new `ErrorCodes` value gets Arabic and English text under `errors.*`.

## Review Focus

1. **A pending join request goes stale when terms change.** The requester is not a member, so a `[RequiresGameyaMember]` re-accept would 404 them and their request could never be approved. Expected: they can re-accept. Resolution (spec deviation): `ReacceptTermsCommand` is `[RequiresAuth]` + domain check, and `ReacceptTerms` succeeds for an Active member _or_ a Pending requester. Tests in Tasks 3 and 7.
2. **A user with a pending join request accepts a direct invite, or is approved after joining another way.** Expected: accepting withdraws their pending request; approving a request from someone already Active fails `ALREADY_MEMBER`, never a 500 from the unique index. Tests in Task 2.
3. **Double-tapped buttons** (two invites to the same phone, two join requests, two approvals). Expected: one succeeds, the other gets 409 `CONCURRENCY_CONFLICT`, never a 500 from a filtered unique index. Tests in Tasks 4 and 9.
4. **Editing an unrelated field of a Recruiting gam'eya whose start date has since passed.** Expected: allowed; only a _changed_ start date must be ≥ today (opening still requires ≥ today). Test in Task 3.
5. **Logging in from an invite link, including a brand-new user who must set their name first.** Expected: they land back on `/j/{code}`; a `returnUrl` that is not a same-site path (`//evil.com`, `https://…`) is ignored. Tests in Task 15.

Other spec gaps resolved here (tell the reviewer): Cancel **keeps** `InviteCode` so the link shows "cancelled" (spec said it clears; a cleared code makes the cancelled page unreachable); domain methods take `now`/`today` parameters the spec omitted; the aggregate loads only live children (Active memberships, Pending requests and invites).

---

### Task 1: Gam'eya aggregate core (create, open, regenerate, derived values)

**Files:**

- Create: `src/Gameya.Domain/Common/AuditableEntity.cs`
- Create: `src/Gameya.Domain/Gameyas/GameyaEnums.cs` (`GameyaStatus`, `GameyaFrequency`, `PayoutTiming`, `OrderMethod`, `MembershipStatus`, `JoinRequestStatus`, `InviteStatus`)
- Create: `src/Gameya.Domain/Gameyas/GameyaSettings.cs`
- Create: `src/Gameya.Domain/Gameyas/GameyaCircle.cs`
- Create: `src/Gameya.Domain/Gameyas/Membership.cs`, `MemberShare.cs` (minimal now; Task 2 extends)
- Create: `src/Gameya.Domain/Gameyas/GameyaEvents.cs`
- Modify: `src/Gameya.Domain/Common/ErrorCodes.cs`
- Test: `tests/Gameya.Domain.Tests/Gameyas/GameyaCircleCreateTests.cs`

**Interfaces:**

- Produces:
  - `abstract class AuditableEntity : Entity` with `CreatedAt`, `CreatedByUserId`, `UpdatedAt`, `UpdatedByUserId` (all `private set`; the interceptor in Task 4 writes them through EF).
  - `sealed record GameyaSettings(string Name, string? Description, decimal ContributionAmount, int ShareCount, GameyaFrequency Frequency, DateOnly StartDate, int GraceDays, PayoutTiming PayoutTiming, int? PayoutOffsetDays, OrderMethod OrderMethod, int MaxSharesPerMember, bool OrganizerParticipates, string? RulesText)`.
  - `sealed record ShareRequest(IReadOnlyList<int> Preferences)`.
  - `GameyaCircle : AuditableEntity` with `Id, OrganizerId, Status, TermsVersion, InviteCode, CancelReason, CancelledAt, Currency ("EGP")`, every settings property, `IReadOnlyCollection<Membership> Memberships` (backing field `_memberships`), and computed `Pot, RoundCount, EndDate, TotalPerShare, AssignedShares, ReservedShares, AvailableShares, IsFull`.
  - `static GameyaCircle Create(Guid id, Guid organizerId, GameyaSettings settings, int organizerShares, DateOnly today, DateTimeOffset now)`
  - `void OpenForRecruiting(string code, DateOnly today)`, `void RegenerateInviteCode(string code)`
  - `Membership : AuditableEntity` with `Id, GameyaId, UserId, JoinedAt, AcceptedTermsVersion, Status, RemovalReason, IReadOnlyList<MemberShare> Shares`; `MemberShare : AuditableEntity` with `Id, IReadOnlyList<int> Preferences`.
  - `ErrorCodes`: `GAMEYA_NOT_FOUND, INVALID_STATUS_TRANSITION, NOT_ENOUGH_SHARES, MAX_SHARES_EXCEEDED, SHARES_BELOW_ASSIGNED, TERMS_NOT_ACCEPTED, INVITE_CODE_INVALID, GAMEYA_FULL, ALREADY_MEMBER, REQUEST_ALREADY_PENDING, INVITE_NOT_FOUND, ORGANIZER_CANNOT_LEAVE, START_DATE_IN_PAST, INVALID_PREFERENCES` (`CONCURRENCY_CONFLICT` exists).
  - Events (all `IActivityEvent`, `GameyaId` set, `TargetType "Gameya"`, `TargetId` = gam'eya id): `GameyaCreated` (details `{ shareCount, organizerShares }`), `GameyaOpened`, `InviteCodeRegenerated` (details `null`).

- [ ] **Step 1: Write the failing tests**

Helper `Settings(...)` builds valid settings (10 shares, 1,000 EGP, Monthly, start = `Today.AddDays(7)`, ByRequest, max 2, participating) with `with` overrides. `Today = new DateOnly(2026, 11, 14)`.

```csharp
[Fact] void Create_is_Draft_with_organizer_membership_at_terms_1()
{   var g = GameyaCircle.Create(Id, Org, Settings(), organizerShares: 1, Today, Now);
    g.Status.ShouldBe(GameyaStatus.Draft); g.TermsVersion.ShouldBe(1); g.InviteCode.ShouldBeNull();
    var m = g.Memberships.ShouldHaveSingleItem();
    m.UserId.ShouldBe(Org); m.Shares.Count.ShouldBe(1); m.AcceptedTermsVersion.ShouldBe(1);
    g.AvailableShares.ShouldBe(9);
    g.DomainEvents.OfType<GameyaCreated>().ShouldHaveSingleItem(); }

[Fact] void Non_participating_organizer_has_no_membership_and_must_pass_zero_shares()
// Create(..., Settings() with { OrganizerParticipates = false }, 0, ...) → no memberships, Available 10;
// organizerShares 1 → DomainException VALIDATION_FAILED

[Theory] // every edge of the field rules, each → VALIDATION_FAILED with params["field"] naming the property
[InlineData("Name", "ab")] [InlineData("Name", 61 chars)] [InlineData("ContributionAmount", 99)]
[InlineData("ContributionAmount", 100.5)] [InlineData("ContributionAmount", 1_000_001)]
[InlineData("ShareCount", 1)] [InlineData("ShareCount", 51)] [InlineData("GraceDays", 11)]
[InlineData("PayoutOffsetDays", null, FixedDate)] [InlineData("PayoutOffsetDays", 3, AfterFullCollection)]
[InlineData("MaxSharesPerMember", 0)] [InlineData("MaxSharesPerMember", 6)]
[InlineData("Description", 501 chars)] [InlineData("RulesText", 2001 chars)]
void Field_rules_are_enforced(...)
// plus the accepted boundaries: name "  abc  " trims to "abc"; amount 100 and 1,000,000; shares 2 and 50;
// organizer shares above MaxSharesPerMember → MAX_SHARES_EXCEEDED

[Fact] void Start_date_before_Cairo_today_fails_START_DATE_IN_PAST() // StartDate = Today.AddDays(-1); Today itself is fine

[Theory]
[InlineData("2026-01-31", GameyaFrequency.Monthly, 3, "2026-03-31")]  // 31st → end of month
[InlineData("2026-01-31", GameyaFrequency.Monthly, 2, "2026-02-28")]
[InlineData("2026-12-01", GameyaFrequency.Weekly, 4, "2026-12-22")]
void EndDate_is_due_date_of_last_round(string start, GameyaFrequency f, int shares, string end)

[Fact] void Pot_and_TotalPerShare_equal_amount_times_shares() // 1,000 × 10 → 10,000; RoundCount 10

[Fact] void Open_sets_code_and_Recruiting_only_from_Draft()
// Open("ABC234", Today) → Recruiting, InviteCode "ABC234", GameyaOpened raised;
// second Open → INVALID_STATUS_TRANSITION; Open when StartDate < today (pass today = StartDate+1) → START_DATE_IN_PAST

[Fact] void Regenerate_replaces_code_only_while_Recruiting() // Draft → INVALID_STATUS_TRANSITION; Recruiting → new code
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Gameya.Domain.Tests --filter FullyQualifiedName~GameyaCircleCreateTests`
Expected: build error, `GameyaCircle` not defined.

- [ ] **Step 3: Implement the types listed under Interfaces**

One private `Validate(GameyaSettings, int organizerShares, DateOnly today, bool checkStartDate)` used by `Create` (and `UpdateSettings` in Task 3). Field failures throw `new DomainException(ErrorCodes.VALIDATION_FAILED, new Dictionary<string, object?> { ["field"] = "<PropertyName>" })`. `EndDate`: Monthly → `CairoCalendar.MonthlyDueDate(StartDate, ShareCount - 1)`; Weekly → `StartDate.AddDays(7 * (ShareCount - 1))`. Child ids use `Guid.CreateVersion7()`. Any operation in a status it does not list throws `INVALID_STATUS_TRANSITION`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Gameya.Domain.Tests`
Expected: all pass.

- [ ] **Step 5: Commit** — `feat(domain): add the gam'eya aggregate with settings rules and derived values`

---

### Task 2: Joining: requests, approvals and direct invites

**Files:**

- Create: `src/Gameya.Domain/Gameyas/JoinRequest.cs`, `JoinRequestShare.cs`, `PendingInvite.cs`
- Modify: `src/Gameya.Domain/Gameyas/GameyaCircle.cs`, `Membership.cs`, `GameyaEvents.cs`
- Test: `tests/Gameya.Domain.Tests/Gameyas/GameyaCircleJoiningTests.cs`

**Interfaces:**

- Consumes: Task 1 types.
- Produces:
  - `JoinRequest : AuditableEntity` (`Id, GameyaId, UserId, IReadOnlyList<JoinRequestShare> Shares, AcceptedTermsVersion, Status, RejectReason`); `JoinRequestShare : AuditableEntity` (`Id, IReadOnlyList<int> Preferences`); `PendingInvite : AuditableEntity` (`Id, GameyaId, PhoneNumber Phone, int ShareCount, Guid? UserId, InviteStatus Status`).
  - `GameyaCircle.JoinRequests`, `GameyaCircle.PendingInvites` (read-only collections over backing fields).
  - `JoinRequest RequestToJoin(Guid userId, IReadOnlyList<ShareRequest> shares)`
  - `void WithdrawJoinRequest(Guid userId)`
  - `Membership ApproveJoinRequest(Guid requestId, DateTimeOffset now)`
  - `void RejectJoinRequest(Guid requestId, string? reason)`
  - `PendingInvite InviteByPhone(PhoneNumber phone, Guid? registeredUserId, int shareCount)`
  - `void RevokeInvite(Guid inviteId)`
  - `Membership AcceptInvite(Guid inviteId, Guid userId, PhoneNumber phone, IReadOnlyList<ShareRequest> shares, DateTimeOffset now)`
  - `void DeclineInvite(Guid inviteId, Guid userId, PhoneNumber phone)`
  - Events: `JoinRequested` (TargetType `JoinRequest`, details `{ userId, shareCount }`), `JoinRequestWithdrawn`, `JoinRequestApproved` (details `{ userId, shareCount }`), `JoinRequestRejected` (reason in `Note`), `MemberInvited` (TargetType `PendingInvite`, details `{ inviteId, shareCount, registeredUserId }`, no phone), `InviteRevoked`, `InviteAccepted` (details `{ inviteId, userId }`), `InviteDeclined`.

- [ ] **Step 1: Write the failing tests**

Helper `Recruiting()` = created (organizer 1 share, 10 shares, ByRequest, max 2) and opened.

```csharp
[Fact] void Request_then_approve_creates_membership_and_reduces_available()
// RequestToJoin(U, two shares [[2],[]]) → Pending at TermsVersion 1, Available still 9;
// Approve → Active membership with 2 shares, preferences [2] and [], Available 7 (AC-G2)

[Fact] void Approve_more_than_available_fails_NOT_ENOUGH_SHARES_with_available_count()
// fill to 1 available via invites, approve a 2-share request → Code NOT_ENOUGH_SHARES, Params["available"] == 1 (AC-G3)

[Fact] void Request_rules()
// organizer → ALREADY_MEMBER; active member → ALREADY_MEMBER; second pending → REQUEST_ALREADY_PENDING;
// 3 shares with max 2 → MAX_SHARES_EXCEEDED; 0 shares → VALIDATION_FAILED;
// when Available == 0 → GAMEYA_FULL; 2 shares when Available == 1 → NOT_ENOUGH_SHARES
// Draft gam'eya → INVALID_STATUS_TRANSITION

[Theory] // INVALID_PREFERENCES
[InlineData(OrderMethod.Manual, new[] { 1 })]              // preferences only under ByRequest
[InlineData(OrderMethod.ByRequest, new[] { 1, 2, 3, 4 })]  // max 3
[InlineData(OrderMethod.ByRequest, new[] { 2, 2 })]        // distinct
[InlineData(OrderMethod.ByRequest, new[] { 0 })]           // range 1..RoundCount
[InlineData(OrderMethod.ByRequest, new[] { 11 })]
void Preference_validation(OrderMethod method, int[] prefs)

[Fact] void Withdraw_only_own_pending_request() // other user → GAMEYA_NOT_FOUND; after withdraw, status Withdrawn

[Fact] void Reject_sets_reason_and_frees_nothing() // reason 501 chars → VALIDATION_FAILED; reason in event Note

[Fact] void Invite_reserves_shares_and_accept_takes_them()
// InviteByPhone(P, null, 2) → Reserved 2, Available 7; AcceptInvite(id, U, P, 2 shares) → Active membership,
// Reserved 0, Assigned 3, invite Accepted with UserId U, InviteAccepted raised (AC-G8)

[Fact] void Invite_rules()
// phone with Pending invite → REQUEST_ALREADY_PENDING; registeredUserId == organizer or Active member → ALREADY_MEMBER;
// shareCount > max → MAX_SHARES_EXCEEDED; shareCount > Available → NOT_ENOUGH_SHARES

[Fact] void Accept_or_decline_with_other_phone_fails_INVITE_NOT_FOUND() // also for revoked/unknown invite ids

[Fact] void Accept_requires_one_ShareRequest_per_invited_share() // 1 ShareRequest for a 2-share invite → INVALID_PREFERENCES

[Fact] void Accepting_an_invite_withdraws_the_users_pending_request()   // Review Focus 2
// U requests (request R), organizer invites U's phone, U accepts → R.Status == Withdrawn, one Active membership;
// ApproveJoinRequest(R) then fails GAMEYA_NOT_FOUND (not Pending), never a second membership

[Fact] void Decline_and_revoke_release_reserved_shares() // Available back to 9
```

The stale-terms approval test needs `UpdateSettings`, so it lives in Task 3.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Gameya.Domain.Tests --filter FullyQualifiedName~GameyaCircleJoiningTests`
Expected: build errors for the missing methods.

- [ ] **Step 3: Implement the methods listed under Interfaces**

`AssignedShares` = shares in Active memberships; `ReservedShares` = `ShareCount` of Pending invites. Share-count checks order: status → membership/pending → count 1..max (`MAX_SHARES_EXCEEDED`, 0 → `VALIDATION_FAILED`) → `Available == 0` → `GAMEYA_FULL` → count > Available → `NOT_ENOUGH_SHARES { available }` → preferences. `ApproveJoinRequest` checks, in order: request Pending (`GAMEYA_NOT_FOUND` otherwise), user not Active (`ALREADY_MEMBER`), terms (`TERMS_NOT_ACCEPTED`), capacity. Preferences are copied from request shares to member shares. Unknown or non-Pending request/invite ids → `GAMEYA_NOT_FOUND` for requests, `INVITE_NOT_FOUND` for invites.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Gameya.Domain.Tests`
Expected: all pass.

- [ ] **Step 5: Commit** — `feat(domain): join requests, approvals and direct invites`

---

### Task 3: Settings changes, re-acceptance, leave, remove, cancel

**Files:**

- Modify: `src/Gameya.Domain/Gameyas/GameyaCircle.cs`, `GameyaEvents.cs`
- Test: `tests/Gameya.Domain.Tests/Gameyas/GameyaCircleLifecycleTests.cs`

**Interfaces:**

- Consumes: Tasks 1–2.
- Produces:
  - `void UpdateSettings(GameyaSettings settings, int organizerShares, DateOnly today, DateTimeOffset now)` (Draft, Recruiting)
  - `void ReacceptTerms(Guid userId)` (Recruiting; Active member **or** Pending requester — Review Focus 1)
  - `void Leave(Guid userId)`, `void RemoveMember(Guid userId, string reason)` (Recruiting)
  - `void Cancel(string? reason, DateTimeOffset now)` (Draft, Recruiting)
  - `bool NeedsReaccept` on `Membership` is computed by the caller as `AcceptedTermsVersion < circle.TermsVersion`; add `bool MembershipNeedsReaccept(Guid userId)` on the root.
  - Events: `GameyaSettingsChanged` (details `{ changed: { <camelCaseField>: { old, new } }, termsVersion? }` — `termsVersion` only when bumped; field keys are the `GameyaSettings` property names in camelCase plus `organizerShares`), `TermsReaccepted` (TargetType `Membership` or `JoinRequest`), `MemberLeft`, `MemberRemoved` (reason in `Note`, details `{ userId }`), `GameyaCancelled` (reason in `Note`).

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory] [InlineData("ContributionAmount")] [InlineData("ShareCount")] [InlineData("Frequency")]
void Money_fields_bump_terms_while_Recruiting_and_organizer_auto_reaccepts(string field)
// TermsVersion 2; member U NeedsReaccept true; organizer false; event details contain termsVersion 2 and old/new

[Fact] void Other_fields_and_Draft_changes_never_bump_terms() // rename while Recruiting; amount change while Draft → TermsVersion 1

[Fact] void Changing_amount_logs_old_and_new_amount()  // AC-G4: details.changed.contributionAmount == { old: 1000, new: 1500 }

[Fact] void Share_count_cannot_go_below_assigned_plus_reserved() // assigned 3 + reserved 2, set 4 → SHARES_BELOW_ASSIGNED

[Fact] void Lowering_share_count_trims_out_of_range_preferences() // member pref [8,2], request pref [9], new count 7 → [2] and []

[Fact] void Unchanged_past_start_date_is_allowed_but_a_changed_one_must_be_today_or_later()  // Review Focus 4
// Recruiting with StartDate = Today; call UpdateSettings(rename, today: Today+3) → OK;
// UpdateSettings(StartDate = Today+1, today: Today+3) → START_DATE_IN_PAST

[Fact] void Toggling_participation_adds_or_ends_the_organizer_membership() // off → membership Left, shares free; on → new Active membership
[Fact] void Changing_organizer_shares_adds_or_removes_member_shares()     // 1 → 2 → 1; capacity checked (NOT_ENOUGH_SHARES)

[Fact] void Approve_on_stale_terms_fails_TERMS_NOT_ACCEPTED_until_requester_reaccepts() // Review Focus 1
// request at v1, bump to v2, Approve → TERMS_NOT_ACCEPTED; ReacceptTerms(requester) → Approve succeeds

[Fact] void Reaccept_clears_flag() // AC-G4; outsider (no membership, no pending request) → GAMEYA_NOT_FOUND

[Fact] void Organizer_cannot_leave_or_be_removed() // ORGANIZER_CANNOT_LEAVE for both
[Fact] void Leave_and_remove_free_shares_and_set_status() // Left / Removed with reason; reason "abcd" → VALIDATION_FAILED

[Fact] void Cancel_rejects_requests_revokes_invites_and_keeps_the_code()
// from Recruiting: status Cancelled, CancelledAt now, requests Rejected, invites Revoked, InviteCode unchanged;
// Cancel again → INVALID_STATUS_TRANSITION; reason 501 chars → VALIDATION_FAILED; Leave after cancel → INVALID_STATUS_TRANSITION
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Gameya.Domain.Tests --filter FullyQualifiedName~GameyaCircleLifecycleTests`
Expected: build errors for the missing methods.

- [ ] **Step 3: Implement the methods listed under Interfaces**

`UpdateSettings` validates with `checkStartDate: settings.StartDate != StartDate`. Terms bump only when `Status == Recruiting` and amount, share count or frequency changed. Lowering `MaxSharesPerMember` does not touch existing memberships; only the organizer's own shares must fit the new max. Raise `GameyaSettingsChanged` only if at least one field changed.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Gameya.Domain.Tests`
Expected: all pass.

- [ ] **Step 5: Commit** — `feat(domain): settings changes, terms re-acceptance, leave, remove and cancel`

---

### Task 4: Persistence, auditing interceptor and the `AddGameyas` migration

**Files:**

- Modify: `src/Gameya.Domain/Users/User.cs` (derive from `AuditableEntity`; drop its own `CreatedAt`; `Register` keeps its signature but no longer sets `CreatedAt`)
- Modify: `tests/Gameya.Domain.Tests/UserTests.cs` (remove the `u.CreatedAt.ShouldBe(T0)` assertion)
- Create: `src/Gameya.Infrastructure/Persistence/AuditingInterceptor.cs`
- Create: `src/Gameya.Infrastructure/Persistence/Configurations/GameyaCircleConfiguration.cs` (also configures `Membership`, `MemberShare`, `JoinRequest`, `JoinRequestShare`, `PendingInvite`), `PreferencesConversion.cs`
- Modify: `src/Gameya.Infrastructure/Persistence/AppDbContext.cs`, `src/Gameya.Application/Abstractions/IAppDbContext.cs`, `src/Gameya.Infrastructure/DependencyInjection.cs`, `src/Gameya.Api/Infrastructure/ProblemDetailsMapping.cs`
- Create: `src/Gameya.Infrastructure/Persistence/Migrations/<ts>_AddGameyas.cs` (generated)
- Test: `tests/Gameya.Api.IntegrationTests/Infrastructure/AuditingTests.cs`, `tests/Gameya.Api.IntegrationTests/Gameyas/GameyaPersistenceTests.cs`

**Interfaces:**

- Consumes: Tasks 1–3.
- Produces:
  - `IAppDbContext`: `DbSet<GameyaCircle> Gameyas`, `DbSet<Membership> Memberships`, `DbSet<JoinRequest> JoinRequests`, `DbSet<PendingInvite> PendingInvites` (children are read-only projection sources; writes go through the root).
  - `sealed class AuditingInterceptor(IClock clock, ICurrentUser? currentUser) : SaveChangesInterceptor`
  - `AppDbContext.Configure(DbContextOptionsBuilder builder, string connectionString, AuditingInterceptor? auditing = null)` — null falls back to `new AuditingInterceptor(new SystemClock(), null)` (design-time factory, `--migrate`, test fixture).
  - DI: `services.AddScoped<AuditingInterceptor>()`; `AddDbContext<AppDbContext>((sp, o) => AppDbContext.Configure(o, cs, sp.GetRequiredService<AuditingInterceptor>()))`.
  - `ProblemDetailsMapping`: status codes — 404: `GAMEYA_NOT_FOUND, INVITE_CODE_INVALID, INVITE_NOT_FOUND`; 409: `INVALID_STATUS_TRANSITION, NOT_ENOUGH_SHARES, SHARES_BELOW_ASSIGNED, TERMS_NOT_ACCEPTED, GAMEYA_FULL, ALREADY_MEMBER, REQUEST_ALREADY_PENDING, ORGANIZER_CANNOT_LEAVE, CONCURRENCY_CONFLICT`; others 400. Unique violations on `ix_gameyas_invite_code`, `ix_memberships_gameya_id_user_id`, `ix_join_requests_gameya_id_user_id`, `ix_pending_invites_gameya_id_phone` → 409 `CONCURRENCY_CONFLICT`.

- [ ] **Step 1: Write the failing tests**

`AuditingTests` (real SQL Server via `DatabaseFixture`; build contexts with an `AuditingInterceptor` over a fixed clock and a substitute `ICurrentUser`):

```csharp
[Fact] Insert_sets_Created_and_leaves_Updated_null()      // user + gam'eya: CreatedAt == clock, CreatedByUserId == actor, UpdatedAt null
[Fact] Update_sets_Updated_and_never_overwrites_Created() // advance clock, modify, save: CreatedAt unchanged, UpdatedAt == new clock
[Fact] Anonymous_actions_leave_ByUserId_null()
[Fact] Child_change_touches_the_root()                     // load aggregate, ApproveJoinRequest, save → gameyas.updated_at == clock
```

`GameyaPersistenceTests`:

```csharp
[Fact] Aggregate_round_trips_with_preferences_and_enums_as_strings()   // raw SQL: status = 'Recruiting', preferences = '[2,5]'
[Fact] Two_contexts_changing_children_of_one_gameya_conflict()          // approve two different requests from two contexts → second SaveChanges throws DbUpdateConcurrencyException
[Fact] Filtered_unique_indexes_hold()   // second Active membership, second Pending request, second Pending invite for a phone → DbUpdateException 2601; a Left + an Active row for the same user is fine
[Fact] Existing_users_keep_created_at() // a user inserted by the fixture keeps created_at; created_by_user_id null
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Gameya.Api.IntegrationTests --filter "FullyQualifiedName~AuditingTests|FullyQualifiedName~GameyaPersistenceTests"`
Expected: build errors (`AuditingInterceptor`, `Gameyas` not defined).

- [ ] **Step 3: Implement interceptor and configuration**

Interceptor: in `SavingChanges(Async)`, for `Added` `AuditableEntity` entries set `CreatedAt`/`CreatedByUserId`; for `Modified` set `UpdatedAt`/`UpdatedByUserId` and mark `Created*` not modified. Then root touch:

```text
for each Added/Modified/Deleted entry whose CLR type ∈ {Membership, MemberShare, JoinRequest, JoinRequestShare, PendingInvite}:
    follow its foreign keys whose principal type ∈ that set ∪ {GameyaCircle} to the tracked principal entry
    (match by key value), repeat until a GameyaCircle entry is reached;
    if that root entry is Unchanged or Modified: set UpdatedAt/UpdatedByUserId and mark those properties modified
```

Configuration: tables and columns as spec §6; enums `HasConversion<string>()`; `Phone` via `PhoneNumberConverter`; `Currency` `char(3)`; `ContributionAmount` `decimal(18,2)`; `StartDate` `date`; shadow `row_version` `IsRowVersion()` on `gameyas`; `preferences` `nvarchar(32)` through a `ValueConverter<IReadOnlyList<int>, string>` (System.Text.Json) with a `ValueComparer` comparing by sequence; FK `organizer_id` and `user_id` → `users` without navigations; children cascade on the root's FKs. Indexes exactly as spec §6 (filters `[status] = 'Active'` / `'Pending'`, `invite_code IS NOT NULL`). Ignore `DomainEvents` and every computed property.

- [ ] **Step 4: Generate the migration and check it**

Run: `ConnectionStrings__Migrator="Server=localhost;Database=x;TrustServerCertificate=True" dotnet ef migrations add AddGameyas --project src/Gameya.Infrastructure --output-dir Persistence/Migrations`
Expected: one migration that creates six tables and **adds** `created_by_user_id`, `updated_at`, `updated_by_user_id` to `users` without touching `users.created_at`. Index names match the four listed under Interfaces.

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test tests/Gameya.Domain.Tests; dotnet test tests/Gameya.Api.IntegrationTests`
Expected: all pass, including the existing suites.

- [ ] **Step 6: Commit** — `feat(persistence): store gam'eyas with audit columns and an aggregate row version`

---

### Task 5: Gam'eya-scoped authorization

**Files:**

- Modify: `src/Gameya.Application/Security/AccessAttributes.cs`, `src/Gameya.Application/Behaviors/AuthorizationBehavior.cs`, `src/Gameya.Application/DependencyInjection.cs`
- Create: `src/Gameya.Application/Gameyas/IGameyaRequest.cs`, `src/Gameya.Application/Gameyas/GameyaAccess.cs`, `src/Gameya.Application/Gameyas/Specifications/GameyaRoleSpec.cs`
- Modify: `tests/Gameya.Application.Tests/PipelineTests.cs`, `tests/Gameya.Application.Tests/AccessAttributeCoverageTests.cs`
- Test: `tests/Gameya.Api.IntegrationTests/Gameyas/GameyaAccessTests.cs`

**Interfaces:**

- Produces:
  - `public interface IGameyaRequest { Guid GameyaId { get; } }`
  - `RequiresGameyaOrganizerAttribute`, `RequiresGameyaMemberAttribute` (both `: AccessAttribute`, class-only, not inherited).
  - `public enum GameyaRole { None, Member, Organizer }`; `public interface IGameyaAccess { Task<GameyaRole> GetRoleAsync(Guid gameyaId, Guid userId, CancellationToken ct); }`; implementation `GameyaAccess(IAppDbContext db)` registered scoped, one projection query (`GameyaRoleSpec`: organizer id + whether an Active membership exists).
  - `AuthorizationBehavior(ICurrentUser currentUser, IGameyaAccess gameyaAccess)`: for the two new attributes require auth, then `None` → `GAMEYA_NOT_FOUND`; organizer-only with `Member` → `GAMEYA_NOT_FOUND`. A request with a gam'eya attribute that does not implement `IGameyaRequest` → `InvalidOperationException` (fail closed).

- [ ] **Step 1: Write the failing tests**

`PipelineTests` (substitute `IGameyaAccess`; existing constructions get a second argument):

```csharp
[Fact] Gameya_member_request_from_outsider_is_GAMEYA_NOT_FOUND()
[Fact] Gameya_organizer_request_from_member_is_GAMEYA_NOT_FOUND()
[Fact] Gameya_requests_pass_for_the_right_role()     // member → member request ok; organizer → both ok
[Fact] Gameya_request_from_anonymous_is_UNAUTHENTICATED()
[Fact] Gameya_attribute_without_IGameyaRequest_fails_closed()
```

`AccessAttributeCoverageTests`: `Every_gameya_scoped_request_implements_IGameyaRequest` over the Application assembly.

`GameyaAccessTests` (integration, seeded through `CreateAppContext`): organizer → `Organizer`, Active member → `Member`, Left member and outsider → `None`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Gameya.Application.Tests`
Expected: build errors for the new attributes.

- [ ] **Step 3: Implement the types listed under Interfaces**

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Gameya.Application.Tests; dotnet test tests/Gameya.Api.IntegrationTests --filter FullyQualifiedName~GameyaAccessTests`
Expected: all pass.

- [ ] **Step 5: Commit** — `feat(security): organizer and member access for gam'eya requests`

---

### Task 6: Organizer setup commands and endpoints

**Files:**

- Create: `src/Gameya.Application/Gameyas/CreateGameya.cs`, `UpdateGameyaSettings.cs`, `OpenForRecruiting.cs`, `RegenerateInviteCode.cs`, `CancelGameya.cs`, `GameyaSettingsValidator.cs`, `InviteCodeGenerator.cs`
- Create: `src/Gameya.Application/Gameyas/Specifications/GameyaAggregateByIdSpec.cs`, `GameyaByInviteCodeSpec.cs`
- Create: `src/Gameya.Api/Endpoints/GameyaEndpoints.cs`; modify `Program.cs` (`app.MapGameyaEndpoints()`)
- Test: `tests/Gameya.Application.Tests/InviteCodeGeneratorTests.cs`, `tests/Gameya.Api.IntegrationTests/Gameyas/GameyaSetupTests.cs`, `tests/Gameya.Api.IntegrationTests/Gameyas/GameyaTestApi.cs`

**Interfaces:**

- Consumes: Tasks 1–5.
- Produces:
  - `[RequiresAuth] CreateGameyaCommand(GameyaSettings Settings, int OrganizerShares) : ICommand<Guid>`
  - `[RequiresGameyaOrganizer] UpdateGameyaSettingsCommand(Guid GameyaId, GameyaSettings Settings, int OrganizerShares) : ICommand<Unit>, IGameyaRequest`
  - `[RequiresGameyaOrganizer] OpenForRecruitingCommand(Guid GameyaId)`, `RegenerateInviteCodeCommand(Guid GameyaId)`, `CancelGameyaCommand(Guid GameyaId, string? Reason)` — all `ICommand<Unit>, IGameyaRequest`.
  - `public interface IInviteCodeGenerator { string Next(); }` + `InviteCodeGenerator` (singleton; `RandomNumberGenerator.GetInt32` over the alphabet). Handlers retry up to 5 times while `GameyaByInviteCodeSpec` finds the code; the unique index is the backstop (409).
  - `GameyaSettingsValidator : AbstractValidator<GameyaSettings>` mirroring the field rules; used by create and update validators.
  - `GameyaAggregateByIdSpec(Guid id)`: tracked, split query, filtered includes of Active memberships (+shares), Pending requests (+shares), Pending invites. Every command handler loads through it and throws `GAMEYA_NOT_FOUND` on null.
  - HTTP: `POST /api/gameyas` → 201 `{ id }` with `Location`; `PUT /api/gameyas/{id}`; `POST /api/gameyas/{id}/open`; `POST /api/gameyas/{id}/invite-code`; `POST /api/gameyas/{id}/cancel` `{ reason? }` → 204. Body record `GameyaSettingsBody` = the settings fields + `organizerShares`, mapped field by field (missing enums default to `Monthly`/`AfterFullCollection`).
  - Test helper `GameyaTestApi`: `CreateAsync(HttpClient, object? overrides = null) → Guid`, `OpenAsync(HttpClient, Guid) → string code` (reads the code from the DB through `DatabaseFixture.CreateAppContext()`), `ValidBody(DateOnly today)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// InviteCodeGeneratorTests
[Fact] Codes_are_6_chars_from_the_alphabet() // 1,000 codes, all match ^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6}$, and > 990 distinct

// GameyaSetupTests (AC-G1 storage part)
[Fact] Create_returns_201_and_stores_a_Draft_with_organizer_share()    // 10 shares, organizer 1 share; activity GAMEYA_CREATED
[Fact] Create_with_invalid_fields_returns_400_with_field_errors()       // name "ab", shareCount 51 → errors keys present
[Fact] Open_generates_a_code_and_logs_without_the_code()                // GAMEYA_OPENED details do not contain the code
[Fact] Regenerate_replaces_the_code()
[Fact] Update_while_Recruiting_bumps_terms_and_logs_old_and_new_amount() // AC-G4 log part
[Fact] Cancel_returns_204_and_a_second_cancel_is_409_INVALID_STATUS_TRANSITION()
[Fact] Member_calling_organizer_endpoints_gets_404()                     // seeded Active member
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Gameya.Application.Tests --filter FullyQualifiedName~InviteCodeGeneratorTests; dotnet test tests/Gameya.Api.IntegrationTests --filter FullyQualifiedName~GameyaSetupTests`
Expected: build errors.

- [ ] **Step 3: Implement commands, validators, specs, generator and endpoints**

Register `IInviteCodeGenerator` as singleton in `AddApplication`. Handlers compute `today` from `IClock`.

- [ ] **Step 4: Run to verify they pass**

Run: same as Step 2, plus `dotnet test tests/Gameya.ArchitectureTests`
Expected: all pass.

- [ ] **Step 5: Commit** — `feat(gameyas): create, edit, open, regenerate code and cancel`

---

### Task 7: Joining commands, invite endpoints and the participation checker

**Files:**

- Create: `src/Gameya.Application/Gameyas/RequestToJoin.cs`, `WithdrawJoinRequest.cs`, `ApproveJoinRequest.cs`, `RejectJoinRequest.cs`, `InviteByPhone.cs`, `RevokeInvite.cs`, `AcceptInvite.cs`, `DeclineInvite.cs`, `ReacceptTerms.cs`, `LeaveGameya.cs`, `RemoveMember.cs`, `GameyaParticipationChecker.cs`
- Create: `src/Gameya.Application/Gameyas/Specifications/GameyaAggregateByInviteIdSpec.cs`, `GameyaAggregateByInviteCodeSpec.cs`, `ParticipationSpec.cs`
- Delete: `src/Gameya.Infrastructure/Participation/NoParticipationChecker.cs` (and its registration)
- Create: `src/Gameya.Api/Endpoints/InviteEndpoints.cs`; modify `GameyaEndpoints.cs`, `Program.cs`
- Test: `tests/Gameya.Api.IntegrationTests/Gameyas/JoiningTests.cs`

**Interfaces:**

- Consumes: Tasks 1–6 (`GameyaAggregateByIdSpec`, `UserByIdSpec`, `ActiveUserByPhoneSpec`).
- Produces:
  - `[RequiresAuth] RequestToJoinCommand(string Code, IReadOnlyList<ShareRequest> Shares, bool AcceptRules, bool AcceptNoMoney) : ICommand<Unit>` — unknown code → `INVITE_CODE_INVALID`.
  - `[RequiresAuth] AcceptInviteCommand(Guid InviteId, IReadOnlyList<ShareRequest> Shares, bool AcceptRules, bool AcceptNoMoney)`, `[RequiresAuth] DeclineInviteCommand(Guid InviteId)` — phone from the caller's user row.
  - `[RequiresAuth] WithdrawJoinRequestCommand(Guid GameyaId)`, `[RequiresAuth] ReacceptTermsCommand(Guid GameyaId)` — domain checks ownership (Review Focus 1). They do not implement `IGameyaRequest`.
  - `[RequiresGameyaOrganizer]` `ApproveJoinRequestCommand(Guid GameyaId, Guid RequestId)`, `RejectJoinRequestCommand(Guid GameyaId, Guid RequestId, string? Reason)`, `InviteByPhoneCommand(Guid GameyaId, string Phone, int ShareCount) : ICommand<Guid>`, `RevokeInviteCommand(Guid GameyaId, Guid InviteId)`, `RemoveMemberCommand(Guid GameyaId, Guid UserId, string Reason)`.
  - `[RequiresGameyaMember] LeaveGameyaCommand(Guid GameyaId)`.
  - Validators: both checkboxes must be true (`VALIDATION_FAILED`, fields `acceptRules`/`acceptNoMoney`); phone parsed with `PhoneNumber.TryParse` (`INVALID_PHONE`); reason lengths.
  - `GameyaParticipationChecker(IAppDbContext db) : IActiveParticipationChecker` (scoped, in `AddApplication`): true if the user organizes a Draft/Recruiting/Active gam'eya or has an Active membership in a Recruiting/Active one.
  - HTTP: routes from spec §7 for these commands; `POST /api/gameyas/{id}/invites` → 201 `{ id }`; `POST /api/invite/{code}/join` and `POST /api/me/invites/{inviteId}/accept` take `{ shares: [{ preferences: [] }], acceptRules, acceptNoMoney }`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] Join_then_approve_leaves_7_available()                    // AC-G2 (organizer 1 + member 2 of 10)
[Fact] Approve_beyond_available_is_409_with_available_param()    // AC-G3: params.available == 1
[Fact] Join_without_both_checkboxes_is_400()
[Fact] Join_with_unknown_code_is_404_INVITE_CODE_INVALID()
[Fact] Stale_requester_can_reaccept_then_be_approved()           // Review Focus 1, end to end
[Fact] Invite_unregistered_phone_then_signup_and_accept()        // AC-G8: invite 2 shares, sign up with that phone, accept → member with 2 shares
[Fact] Invite_phone_typed_with_Arabic_digits_matches_the_user()  // "٠١٠..." invite, user logs in with Latin digits, accept works
[Fact] Decline_releases_shares_and_revoke_hides_invite()
[Fact] Leave_and_remove()                                        // member leaves → 204; organizer leave → 409 ORGANIZER_CANNOT_LEAVE; remove with 3-char reason → 400
[Fact] Account_deletion_blocked_while_in_a_Recruiting_gameya()   // AC-G7: DELETE /api/me → 409 ACCOUNT_HAS_ACTIVE_GAMEYAS; after leaving → 204
[Fact] Every_command_writes_its_activity_entry()                 // runs the flow and checks each action code from spec §4.5 exists for the gam'eya
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Gameya.Api.IntegrationTests --filter FullyQualifiedName~JoiningTests`
Expected: build errors.

- [ ] **Step 3: Implement commands, checker and endpoints**

`InviteByPhone` resolves `registeredUserId` through `ActiveUserByPhoneSpec`. Accept/decline load the aggregate through `GameyaAggregateByInviteIdSpec`; a missing invite → `INVITE_NOT_FOUND`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Gameya.Api.IntegrationTests; dotnet test tests/Gameya.ArchitectureTests; dotnet test tests/Gameya.Application.Tests`
Expected: all pass (the existing `DeleteAccountTests` override still works because tests replace the checker).

- [ ] **Step 5: Commit** — `feat(gameyas): join requests, direct invites, leave, remove and re-accept`

---

### Task 8: Queries and the public preview

**Files:**

- Create: `src/Gameya.Application/Gameyas/GetInvitePreview.cs`, `ListMyGameyas.cs`, `ListMyInvites.cs`, `GetGameya.cs`, `GetMembers.cs`, `GetGameyaActivity.cs`, `GetJoinRequests.cs`, `GetPendingInvites.cs`, `GameyaDtos.cs`
- Create: `src/Gameya.Application/Gameyas/Specifications/` row specs: `InvitePreviewRowSpec`, `MyGameyaRowsSpec`, `MyInviteRowsSpec`, `GameyaDetailRowSpec`, `MemberRowsSpec`, `JoinRequestRowsSpec`, `PendingInviteRowsSpec`, `GameyaActivityPageSpec`, `TermsChangeEntriesSpec`; `src/Gameya.Application/Users/Specifications/UserCardsByIdsSpec.cs`
- Modify: `src/Gameya.Api/Infrastructure/RateLimiting.cs`, `src/Gameya.Api/Program.cs`, `GameyaEndpoints.cs`, `InviteEndpoints.cs`, `MeEndpoints.cs`
- Test: `tests/Gameya.Api.IntegrationTests/Gameyas/GameyaQueryTests.cs`

**Interfaces:**

- Consumes: Tasks 1–7; `PhoneDisplay.For`.
- Produces (DTO names end in `Dto` so `DtoPhoneTests` scans them; phones only as `DisplayPhone?`):
  - `[PublicAccess] GetInvitePreviewQuery(string Code) : IQuery<InvitePreviewDto>` — unknown code → `INVITE_CODE_INVALID` (404). `InvitePreviewDto(Guid GameyaId, string State /* Open|Full|Cancelled */, string Name, string? Description, decimal ContributionAmount, string Currency, int ShareCount, GameyaFrequency Frequency, DateOnly StartDate, DateOnly EndDate, decimal Pot, int GraceDays, PayoutTiming PayoutTiming, int? PayoutOffsetDays, OrderMethod OrderMethod, int MaxSharesPerMember, int AvailableShares, string? RulesText, string? OrganizerName, string? Relationship /* None|Member|RequestPending|Organizer; null when anonymous */, bool RequestNeedsReaccept)`.
  - `[RequiresAuth] ListMyGameyasQuery : IQuery<IReadOnlyList<MyGameyaDto>>` — `MyGameyaDto(Guid Id, string Name, GameyaStatus Status, GameyaRole Role, decimal ContributionAmount, int ShareCount, int MyShareCount, bool NeedsReaccept)`; organizer or Active member; sorted Recruiting, Draft, Cancelled then name.
  - `[RequiresAuth] ListMyInvitesQuery : IQuery<IReadOnlyList<MyInviteDto>>` — `MyInviteDto(Guid InviteId, Guid GameyaId, string Name, decimal ContributionAmount, int ShareCount, int RoundCount, int InvitedShares, OrderMethod OrderMethod, string? OrganizerName)`; Pending invites in Recruiting gam'eyas matching the caller's phone.
  - `[RequiresGameyaMember] GetGameyaQuery(Guid GameyaId) : IQuery<GameyaDto>` — all settings + `Status, TermsVersion, Currency, Pot, RoundCount, EndDate, TotalPerShare, AssignedShares, ReservedShares, AvailableShares, MyRole, MyShareCount, NeedsReaccept, InviteCode (organizer only), PendingJoinRequestCount (organizer only), TermsDiffDto? TermsDiff`. `TermsDiffDto(decimal? OldContributionAmount, int? OldShareCount, GameyaFrequency? OldFrequency)`: for each field, the `old` value from the earliest `GAMEYA_SETTINGS_CHANGED` entry with `termsVersion` greater than the caller's accepted version that contains that field; null when not needed.
  - `[RequiresGameyaMember] GetMembersQuery(Guid GameyaId) : IQuery<IReadOnlyList<MemberDto>>` — `MemberDto(Guid UserId, string? FullName, Uri? PhotoUrl, DisplayPhone? Phone, int ShareCount, bool IsOrganizer, bool NeedsReaccept)`; full phone when the viewer is the organizer.
  - `[RequiresGameyaMember] GetGameyaActivityQuery(Guid GameyaId, int Page) : IQuery<ActivityPageDto>` — page size 20, newest first; `ActivityEntryDto(DateTimeOffset OccurredAt, string? ActorName /* null = deleted user */, string Action, JsonElement Details, string? Note)`; `ActivityPageDto(IReadOnlyList<ActivityEntryDto> Items, int Page, bool HasMore)`.
  - `[RequiresGameyaOrganizer] GetJoinRequestsQuery(Guid GameyaId) : IQuery<IReadOnlyList<JoinRequestDto>>` — `JoinRequestDto(Guid Id, Guid UserId, string? FullName, DisplayPhone? Phone, IReadOnlyList<IReadOnlyList<int>> SharePreferences, bool TermsCurrent)`.
  - `[RequiresGameyaOrganizer] GetPendingInvitesQuery(Guid GameyaId) : IQuery<IReadOnlyList<PendingInviteDto>>` — `PendingInviteDto(Guid Id, DisplayPhone Phone, int ShareCount, string? RegisteredName)`.
  - Rate limiting: rename `AddOtpRateLimiting` to `AddGameyaRateLimiting`; add policy `RateLimiting.InvitePreviewPolicy = "invite-preview"`, fixed window 60 requests per minute per client IP, applied to `GET /api/invite/{code}`.
  - Routes: spec §7 `GET` endpoints, `GET /api/me/invites`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] Guest_preview_has_no_member_names_and_no_relationship()     // AC-G2: JSON has no "fullName" of members; relationship null
[Fact] Logged_in_preview_reports_relationship()                    // None → RequestPending after join; organizer → Organizer
[Fact] Full_and_cancelled_previews()                               // AC-G5: last share assigned → state "Full"; cancelled → "Cancelled"; regenerated old code → 404
[Fact] Preview_is_rate_limited()                                   // 61st request in a minute → 429 RATE_LIMITED
[Fact] My_gameyas_lists_roles_and_needs_reaccept()
[Fact] My_invites_lists_pending_invites_for_my_phone()             // AC-G8 listing
[Fact] Members_show_full_phone_to_organizer_and_masked_to_members()
[Fact] Get_gameya_hides_invite_code_from_members_and_returns_terms_diff() // AC-G4 banner data: OldContributionAmount 1000
[Fact] Activity_is_paged_newest_first_with_deleted_user_name_null()
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Gameya.Api.IntegrationTests --filter FullyQualifiedName~GameyaQueryTests`
Expected: build errors.

- [ ] **Step 3: Implement queries, specs, DTOs, endpoints and the policy**

Names, photos and phones come from a second query (`UserCardsByIdsSpec(IReadOnlyCollection<Guid>)` → `UserCardRow(Guid Id, string? FullName, PhoneNumber? Phone, string? PhotoKey)`) composed in the handler; photo URLs as in `GetMeHandler`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Gameya.Api.IntegrationTests; dotnet test tests/Gameya.ArchitectureTests`
Expected: all pass.

- [ ] **Step 5: Commit** — `feat(gameyas): details, members, activity, invites and public preview`

---

### Task 9: Cross-cutting acceptance tests

**Files:**

- Test: `tests/Gameya.Api.IntegrationTests/Gameyas/GameyaAcceptanceTests.cs`

**Interfaces:**

- Consumes: every endpoint from Tasks 6–8 and `GameyaTestApi`.

- [ ] **Step 1: Write the tests**

```csharp
[Fact] Full_flow_create_open_preview_join_approve_members()     // spec §9 flow; members list has organizer + member, available 7
[Fact] Outsider_gets_404_on_every_gameya_endpoint()             // AC-G6: iterate every /api/gameyas/{id}/* route (GET, PUT, POST, DELETE) with a real id
[Fact] Two_concurrent_approvals_for_the_last_share()            // Task.WhenAll on two 1-share requests when 1 is available → one 204, one 409 CONCURRENCY_CONFLICT; members total == ShareCount
[Fact] Double_tapped_invite_to_the_same_phone()                 // Review Focus 3: two parallel POST /invites → one 201, one 409; exactly one Pending invite row
[Fact] Double_tapped_join()                                     // two parallel POST /invite/{code}/join → one 204, one 409
```

- [ ] **Step 2: Run them**

Run: `dotnet test tests/Gameya.Api.IntegrationTests --filter FullyQualifiedName~GameyaAcceptanceTests`
Expected: all pass. A 500 in either concurrency test means a unique-index name is missing from `ProblemDetailsMapping` (Task 4) — fix there.

- [ ] **Step 3: Commit** — `test(gameyas): acceptance, outsider 404 and concurrency coverage`

---

### Task 10: Frontend foundation: API service, routes, i18n, return URL

**Files:**

- Create: `src/gameya-web/src/app/features/gameyas/gameya.api.ts`, `gameya.models.ts`, `gameya-status.ts`, `gameya-calc.ts`, `gameya-calc.spec.ts`
- Create: `src/gameya-web/src/app/shared/reason-dialog.ts`
- Create: `src/gameya-web/src/app/core/auth/return-url.ts`, `return-url.spec.ts`
- Modify: `src/gameya-web/src/app/app.routes.ts`, `layout/app-layout.ts`, `features/auth/phone.page.ts`, `otp.page.ts`, `name-setup.page.ts`
- Modify: `src/gameya-web/public/i18n/ar.json`, `en.json`

**Interfaces:**

- Produces:
  - `GameyaApi` (root-injectable) with one method per spec §7 route, typed by `gameya.models.ts` (TS mirrors of the Task 8 DTOs and `GameyaSettingsBody`).
  - `gameyaStatusChip(status: GameyaStatus): { icon: string; labelKey: string; tone: StatusTone }` — Draft `edit_note`/neutral, Recruiting `group_add`/info, Active `autorenew`/success, Completed `task_alt`/success, Cancelled `cancel`/danger.
  - `gameya-calc.ts`: `pot(amount, shares)`, `endDate(start: string, frequency, shares): string` (Monthly = same day clamped to month end, `shares − 1` months later; Weekly = `7 × (shares − 1)` days; mirrors `CairoCalendar.MonthlyDueDate`), `dueDayInWords` key + params.
  - `safeReturnUrl(raw: string | null): string | null` — accepts only values starting with `/` and not `//` or `/\`.
  - `ReasonDialog` (MatDialog): inputs `titleKey`, `required`, `minLength`, `maxLength`; returns the reason or `undefined`.
  - Routes: `/gameyas` (home, `profileCompleteGuard`), `/gameyas/new`, `/gameyas/:id`, `/gameyas/:id/edit` under `AppLayout`; `/j/:code` under `PublicLayout`. Login pages pass `returnUrl` through `/login` → `/login/code` → `/welcome/name`; after sign-in navigate to `safeReturnUrl(returnUrl) ?? '/gameyas'`. Nav "Home" points to `/gameyas`.
  - i18n: `errors.<every new code>` (`NOT_ENOUGH_SHARES` uses `{{available}}`), `gameyas.*`, `invite.*`, `activity.actions.<every action in spec §4.5>`, `gameyaStatus.*`.

- [ ] **Step 1: Write the failing tests**

```ts
// gameya-calc.spec.ts
it('end date clamps the 31st to month end', () =>
  expect(endDate('2026-01-31', 'Monthly', 3)).toBe('2026-03-31'));
it('end date for February', () => expect(endDate('2026-01-31', 'Monthly', 2)).toBe('2026-02-28'));
it('weekly end date', () => expect(endDate('2026-12-01', 'Weekly', 4)).toBe('2026-12-22'));
it('pot', () => expect(pot(1000, 10)).toBe(10000));
// return-url.spec.ts
it.each(['/j/ABC234', '/gameyas/1'])('accepts %s', (u) => expect(safeReturnUrl(u)).toBe(u));
it.each(['//evil.com', '/\\evil.com', 'https://evil.com', '', null])('rejects %s', (u) =>
  expect(safeReturnUrl(u)).toBeNull(),
);
```

- [ ] **Step 2: Run to verify they fail**

Run (in `src/gameya-web`): `npx ng test --watch=false`
Expected: FAIL, modules not found.

- [ ] **Step 3: Implement the pieces listed under Interfaces**

- [ ] **Step 4: Verify**

Run: `npx ng test --watch=false; npm run lint; npm run i18n:check`
Expected: all pass.

- [ ] **Step 5: Commit** — `feat(web): gam'eya API client, routes, return URL and translations`

---

### Task 11: My gam'eyas home

**Files:**

- Create: `src/gameya-web/src/app/features/gameyas/home/gameyas-home.page.ts`, `.scss`, `gameyas-home.page.spec.ts`

**Interfaces:**

- Consumes: `GameyaApi.listMine()`, `listMyInvites()`, `declineInvite(id)`; `gameyaStatusChip`; `MoneyPipe`.
- Produces: `GameyasHomePage` (route `/gameyas`). Accept on an invite card navigates to `/gameyas/invites/:inviteId/accept` (page added in Task 14).

- [ ] **Step 1: Write the failing tests** (`HttpTestingController` + `TranslocoTestingModule`, as in `danger-zone.spec.ts`)

```ts
it('shows pending invites above cards grouped by status'); // data-testid="invite-card", "gameya-card", group headers
it('shows the organizer badge and needs-reaccept marker');
it('decline removes the invite after the 204');
it('empty state explains a gameya and that the app never holds money'); // data-testid="empty-state" + "no-money-notice"
```

- [ ] **Step 2: Run to verify they fail** — `npx ng test --watch=false` → FAIL.
- [ ] **Step 3: Implement `GameyasHomePage`** (signals; card shows name, status chip, amount × shares, own share count; Create button `data-testid="create-gameya"`).
- [ ] **Step 4: Verify** — `npx ng test --watch=false; npm run lint; npm run stylelint` → pass.
- [ ] **Step 5: Commit** — `feat(web): my gam'eyas home with pending invites`

---

### Task 12: Create and edit form with live calculation

**Files:**

- Create: `src/gameya-web/src/app/features/gameyas/form/gameya-form.page.ts`, `.scss`, `gameya-form.page.spec.ts`

**Interfaces:**

- Consumes: `GameyaApi.create(body)`, `update(id, body)`, `get(id)`; `gameya-calc.ts`.
- Produces: `GameyaFormPage` (route `/gameyas/new` and `/gameyas/:id/edit`); after save navigates to `/gameyas/:id`.

- [ ] **Step 1: Write the failing tests**

```ts
it('calc card updates live: pot, rounds, end date, total per share'); // 1000 × 10 → pot 10,000; 10 rounds
it('payout offset is shown and required only for FixedDate');
it('my-shares stepper appears only when participating and is capped by max per member');
it('editing a money field while Recruiting asks to confirm N members re-accept'); // N = members − organizer; cancel keeps form unsent
it('server field errors are shown inline'); // 400 errors.contributionAmount → mat-error
```

- [ ] **Step 2: Run to verify they fail** — FAIL.
- [ ] **Step 3: Implement** — reactive form with the Global Constraints' field rules as validators; sections Basics, Money and schedule, Payout, Members, Rules; calc card `position: sticky; inset-block-end: 0` below 600 px; due day described with `gameyas.form.dueDay` (monthly: day of month, weekly: weekday). Confirmation uses `ConfirmDialog`.
- [ ] **Step 4: Verify** — `npx ng test --watch=false; npm run lint; npm run stylelint` → pass.
- [ ] **Step 5: Commit** — `feat(web): create and edit a gam'eya with live totals`

---

### Task 13: Gam'eya page and Manage tab

**Files:**

- Create: `src/gameya-web/src/app/features/gameyas/detail/gameya.page.ts`, `overview-tab.ts`, `members-tab.ts`, `rules-tab.ts`, `activity-tab.ts`, `manage-tab.ts`, `reaccept-banner.ts`, `.scss` files, `gameya.page.spec.ts`, `manage-tab.spec.ts`

**Interfaces:**

- Consumes: `GameyaApi` (`get`, `members`, `activity`, `joinRequests`, `invites`, `approve`, `reject`, `inviteByPhone`, `revokeInvite`, `removeMember`, `leave`, `reaccept`, `open`, `cancel`, `regenerateCode`); `ReasonDialog`; `PhoneInput`; `gameyaStatusChip`.
- Produces: `GameyaPage` (route `/gameyas/:id`). A 409 from any action reloads `get` + the active tab's data and shows the translated error toast.

- [ ] **Step 1: Write the failing tests**

```ts
// gameya.page.spec.ts
it('header shows name, status chip and key numbers');
it('Manage tab exists only for the organizer');
it('re-accept banner shows old and new amount and re-accept clears it'); // AC-G4 UI
it('Leave is shown to members only while Recruiting');
it('activity renders action codes through i18n and pages');
it('a 409 reloads data and shows a toast');
// manage-tab.spec.ts
it('WhatsApp link is wa.me with the pre-filled message containing the /j/ link');
it('join requests show a count badge; approve and reject (optional reason)');
it('invite by phone posts the phone and share count; revoke removes the row');
it('remove member requires a reason of at least 5 characters');
it('Open for recruiting shown only in Draft; Cancel in Draft and Recruiting');
```

- [ ] **Step 2: Run to verify they fail** — FAIL.
- [ ] **Step 3: Implement** — tabs with `mat-tab-group`; Rules tab includes the fixed `no-money-notice` text; invite link `${location.origin}/j/${code}`; WhatsApp `https://wa.me/?text=` + `encodeURIComponent(translate('gameyas.manage.whatsappMessage', { name, link }))`.
- [ ] **Step 4: Verify** — `npx ng test --watch=false; npm run lint; npm run stylelint` → pass.
- [ ] **Step 5: Commit** — `feat(web): gam'eya page with members, rules, activity and organizer tools`

---

### Task 14: Invite preview and join form

**Files:**

- Create: `src/gameya-web/src/app/features/invite/invite-preview.page.ts`, `.scss`, `invite-preview.page.spec.ts`
- Create: `src/gameya-web/src/app/features/gameyas/join/join-form.ts`, `join-form.spec.ts`, `accept-invite.page.ts`
- Modify: `src/gameya-web/src/app/app.routes.ts` (`/gameyas/invites/:inviteId/accept`)

**Interfaces:**

- Consumes: `GameyaApi.preview(code)` (silent errors), `join(code, body)`, `acceptInvite(inviteId, body)`, `listMyInvites()`; `safeReturnUrl`.
- Produces:
  - `JoinForm` component — inputs `maxShares: number` (min of max-per-member and available), `fixedShares: number | null` (invite mode), `roundCount`, `orderMethod`; output `submitted: { shares: { preferences: number[] }[]; acceptRules: true; acceptNoMoney: true }`. Preference pickers only under `ByRequest`, up to 3 distinct rounds per share or "no preference".
  - `InvitePreviewPage` (`/j/:code`): states `open` (fields + rules + join form or "Log in to join" → `/login?returnUrl=/j/:code`), `full`, `cancelled`, `invalid` (404), `member`/`organizer` (link to `/gameyas/:id`), `pending` (withdraw; re-accept when `requestNeedsReaccept`).
  - `AcceptInvitePage` renders `JoinForm` in invite mode from `listMyInvites()`.

- [ ] **Step 1: Write the failing tests**

```ts
it('guest sees preview without member names and a login link with returnUrl'); // href contains returnUrl=%2Fj%2FABC234
it('full, cancelled and invalid codes show distinct pages'); // AC-G5 UI
it('submit is disabled until both checkboxes are ticked');
it('share stepper is capped by min(max per member, available)');
it('preference pickers appear only under ByRequest and allow at most 3 distinct rounds');
it('invite mode fixes the share count');
```

- [ ] **Step 2: Run to verify they fail** — FAIL.
- [ ] **Step 3: Implement the components listed under Interfaces**
- [ ] **Step 4: Verify** — `npx ng test --watch=false; npm run lint; npm run stylelint; npm run i18n:check` → pass.
- [ ] **Step 5: Commit** — `feat(web): public invite preview and join form`

---

### Task 15: Return-to-invite login and the e2e smoke

**Files:**

- Modify: `src/gameya-web/e2e/smoke.spec.ts`
- Test: `src/gameya-web/src/app/features/auth/otp.page.spec.ts` (create), extend `src/app/core/auth/return-url.spec.ts` if needed

**Interfaces:**

- Consumes: Task 10's login `returnUrl` plumbing; Tasks 11–14 pages and their `data-testid`s.

- [ ] **Step 1: Write the failing tests**

```ts
// otp.page.spec.ts  (Review Focus 5)
it('a returning user lands on returnUrl after the code'); // navigateByUrl('/j/ABC234')
it('a new user goes to name setup carrying returnUrl, then to it'); // '/welcome/name?returnUrl=%2Fj%2FABC234'
it('an unsafe returnUrl falls back to /gameyas'); // '//evil.com'

// smoke.spec.ts
test('create_open_and_preview_in_arabic', async ({ page }) => {
  // sign up (reuse the existing helper flow), land on /gameyas, create 10 shares participating with 1,
  // check the calc card pot, save, open for recruiting, read the code from data-testid="invite-code",
  // open /j/<code> in a new context (logged out): preview renders, html dir="rtl", no member names, "log in to join" visible
});
```

- [ ] **Step 2: Run to verify they fail**

Run: `npx ng test --watch=false` → FAIL on the otp specs. E2E: `npm run e2e` (needs the API in E2E mode as in the foundation) → FAIL on the new test.

- [ ] **Step 3: Fix whatever the tests expose** (the plumbing came in Task 10; this step only closes gaps).

- [ ] **Step 4: Verify everything**

Run: `dotnet test` (whole solution), then in `src/gameya-web`: `npx ng test --watch=false; npm run lint; npm run stylelint; npm run i18n:check; npm run e2e`
Expected: all pass. Then deploy to staging and walk AC-G1…AC-G8 by hand in Arabic and English at 360 px.

- [ ] **Step 5: Commit** — `test(web): return to the invite after login and e2e create-open-preview`
