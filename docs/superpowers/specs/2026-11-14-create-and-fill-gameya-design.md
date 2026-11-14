# Gam'eya: Sub-project 2, Create and Fill a Gam'eya (Design)

- **Date:** 2026-11-14
- **Status:** Draft, awaiting review
- **Source requirements:** `docs/requirements.md` (v1)
- **Builds on:** `docs/superpowers/specs/2026-11-03-foundation-design.md`

---

## 1. Context

The foundation (sub-project 1) is merged and live on staging (`gameya.tryasp.net`, SQL Server on MonsterASP). It provides phone OTP auth, profile, the append-only activity log, the MediatR pipeline (validation, authorization, transaction, activity log), and the Angular shell with Arabic/English and RTL.

The foundation spec grouped "gam'eya setup" (including payout order) into sub-project 2. This design re-splits the remaining work so that each sub-project can be tried end to end on staging:

| #     | Sub-project                                                   | Covers                                                                                                                                                  |
| ----- | ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **2** | **Create and fill a gam'eya (this document)**                 | FR-GAM-01 to 04, FR-GAM-08 for Draft/Recruiting, FR-INV-01 to 07, FR-MEM-01 to 04, terms re-acceptance, the real "in a Recruiting/Active gam'eya" check |
| 3     | Payout order and start                                        | FR-ORDER, FR-GAM-05/06, FR-ROUND generation, contribution records                                                                                       |
| 4     | Payments and payouts                                          | FR-PAY, round statuses, overdue job, disputes, FR-GAM-07, FR-GAM-08 for Active, FR-GAM-09                                                               |
| 5     | Notifications                                                 | FR-NOTIF in-app and email, reminders                                                                                                                    |
| 6+    | Swaps, replacement, dashboards, announcements, reports, admin |                                                                                                                                                         |

## 2. Goals and non-goals

### Goals

- An organizer creates and configures a gam'eya, opens it for recruiting, and shares a link or code (including via WhatsApp).
- Anyone with the link sees a preview (even logged out), logs in, and requests shares. Under the "By request" method they also pick preferred rounds.
- The organizer approves or rejects requests, invites people directly by phone (registered or not), removes members, edits settings, and cancels.
- Members leave while Recruiting and re-accept changed terms.
- Every state change is written to the activity log, ready for the notification engine (#5) to consume.
- A reusable `AuditableEntity` base with automatic created/updated audit columns.
- Done means the full flow works on staging in Arabic and English on a 360px screen, with the acceptance criteria in section 10 passing.

### Non-goals (later sub-projects)

- Start, payout order and the draw, rounds, contributions (#3). There is no Start button yet.
- Cancelling an Active gam'eya and its settlement summary, payments, completion, duplicating a gam'eya (#4).
- Sending notifications of any kind (#5). Events are logged only.
- Real-time UI updates.
- Soft delete. Nothing in this design is deleted; every removal is a status change.

## 3. Decisions made during design

| Topic                                      | Decision                                                                                                                                                                                          |
| ------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Re-acceptance of changed terms (FR-GAM-03) | Members are flagged "needs to re-accept". They are never removed automatically. Start (#3) is blocked until each flagged member re-accepts or leaves. The organizer may remove holdouts manually. |
| "By request" preferences (FR-INV-04)       | Collected in the join form in this sub-project and stored per share. #3 only reads them.                                                                                                          |
| Aggregate design                           | One `Gameya` aggregate owns memberships, shares, join requests and pending invites. Invariants are enforced in one place. Concurrency is protected by a row version.                              |
| Audit columns                              | New `AuditableEntity` base, filled by an EF interceptor. Audit fields only, no soft delete. `User` moves onto it too.                                                                             |
| Direct add (FR-INV-06)                     | Always creates a pending invite, for registered and unregistered numbers alike, because the invitee must accept the terms. Pending invites reserve their shares.                                  |
| Invite link vs code (FR-INV-01)            | One 6-character code. The link is `/j/{code}`. Regenerating the code invalidates both.                                                                                                            |
| Non-member access                          | Every gam'eya-scoped endpoint returns 404 to non-members, never 403, so IDs reveal nothing.                                                                                                       |

## 4. Domain model (`Gameya.Domain`)

### 4.1 `AuditableEntity` (in `Common/`)

```csharp
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
}
```

- The fields are set only by `AuditingInterceptor` (Infrastructure), never by domain methods or handlers. The interceptor uses `IClock` and `ICurrentUser`.
- Added entities get Created* and Updated* is left null. Modified entities get Updated*. Created* is never overwritten.
- `*ByUserId` is null for anonymous or system actions.
- When a child of `Gameya` is added or modified, the interceptor also marks the owning `Gameya` row as updated, so the root's `UpdatedAt` means "last change to this gam'eya".
- `User` derives from `AuditableEntity`. Its own `CreatedAt` property moves to the base class and `User.Register` stops setting it; the interceptor sets it on insert. The existing `users.created_at` column keeps its values.

### 4.2 `Gameya` (aggregate root)

**Settings** (FR-GAM-01)

| Property                | Type     | Rule                                                                |
| ----------------------- | -------- | ------------------------------------------------------------------- |
| `Name`                  | string   | 3 to 60 characters, trimmed                                         |
| `Description`           | string?  | max 500                                                             |
| `ContributionAmount`    | decimal  | whole number, 100 to 1,000,000                                      |
| `Currency`              | string   | `"EGP"` only                                                        |
| `ShareCount`            | int      | 2 to 50, and never below assigned + reserved shares                 |
| `Frequency`             | enum     | `Monthly` (default), `Weekly`                                       |
| `StartDate`             | DateOnly | Cairo date, today or later when set and when opening for recruiting |
| `GraceDays`             | int      | 0 to 10, default 3                                                  |
| `PayoutTiming`          | enum     | `AfterFullCollection` (default), `FixedDate`                        |
| `PayoutOffsetDays`      | int?     | 0 to 10, required only with `FixedDate`                             |
| `OrderMethod`           | enum     | `Manual`, `RandomDraw`, `ByRequest`                                 |
| `MaxSharesPerMember`    | int      | 1 to 5, default 2                                                   |
| `OrganizerParticipates` | bool     | If true, the organizer holds 1 to `MaxSharesPerMember` shares       |
| `RulesText`             | string?  | max 2,000                                                           |

**State:** `Id`, `OrganizerId`, `Status` (`Draft`, `Recruiting`, `Active`, `Completed`, `Cancelled`; only Draft, Recruiting and Cancelled are reachable in this sub-project), `TermsVersion` (starts at 1), `InviteCode` (null outside Recruiting), `CancelReason`, `CancelledAt`, `RowVersion`.

**Derived (computed, not stored):**

- `Pot = ContributionAmount × ShareCount`
- `RoundCount = ShareCount`
- `EndDate`: the due date of the last round. Monthly uses `CairoCalendar.MonthlyDueDate(StartDate, ShareCount - 1)`. Weekly uses `StartDate + 7 × (ShareCount - 1)` days.
- `TotalPerShare = ContributionAmount × RoundCount` (always equals `Pot`)
- `AssignedShares`: shares held by Active memberships
- `ReservedShares`: shares held by Pending invites
- `AvailableShares = ShareCount − AssignedShares − ReservedShares`
- `IsFull = AvailableShares == 0`

### 4.3 Children

- **`Membership`**: `Id`, `UserId`, `JoinedAt`, `AcceptedTermsVersion`, `Status` (`Active`, `Left`, `Removed`), `RemovalReason`, `Shares` (list of `MemberShare`). `NeedsReaccept` is `AcceptedTermsVersion < Gameya.TermsVersion`. Rows are never deleted.
- **`MemberShare`**: `Id`, `Preferences` (0 to 3 distinct round numbers in `1..RoundCount`; empty means no preference). This is the unit #3 maps to a round.
- **`JoinRequest`**: `Id`, `UserId`, `Shares` (list of `JoinRequestShare` with `Preferences`), `AcceptedTermsVersion`, `Status` (`Pending`, `Approved`, `Rejected`, `Withdrawn`), `RejectReason`.
- **`PendingInvite`**: `Id`, `Phone` (E.164 `PhoneNumber`), `ShareCount`, `UserId` (set when accepted or declined), `Status` (`Pending`, `Accepted`, `Declined`, `Revoked`).

All five, plus `Gameya`, derive from `AuditableEntity`.

### 4.4 Operations and rules

| Operation                                               | Allowed in        | Rules                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| ------------------------------------------------------- | ----------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Create(organizerId, settings, organizerShares, today)` | n/a               | Validates all settings. If `OrganizerParticipates`, creates the organizer's Membership with `organizerShares` shares, accepted at version 1. Status Draft.                                                                                                                                                                                                                                                                                            |
| `UpdateSettings(settings, organizerShares, today)`      | Draft, Recruiting | Validates. `ShareCount` must be ≥ assigned + reserved. If Recruiting and amount, share count or frequency changed: `TermsVersion++` (the organizer's own membership re-accepts automatically). Lowering `ShareCount` removes any preference above the new last round, from member shares and pending join requests. Raises `GameyaSettingsChanged` with old and new values. Toggling `OrganizerParticipates` adds or ends the organizer's membership. |
| `OpenForRecruiting(code, today)`                        | Draft             | `StartDate ≥ today`. Sets `InviteCode`. Status Recruiting.                                                                                                                                                                                                                                                                                                                                                                                            |
| `RegenerateInviteCode(code)`                            | Recruiting        | Replaces the code.                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `Cancel(reason?)`                                       | Draft, Recruiting | Reason optional, max 500. Clears `InviteCode`. Pending requests and invites become Rejected and Revoked. Status Cancelled.                                                                                                                                                                                                                                                                                                                            |
| `RequestToJoin(userId, shares)`                         | Recruiting        | The user is not the organizer, has no Active membership and no Pending request. `1 ≤ count ≤ MaxSharesPerMember`. `count ≤ AvailableShares`. Preferences only when `OrderMethod == ByRequest`, max 3 per share, distinct, in range. Accepted at the current `TermsVersion`.                                                                                                                                                                           |
| `WithdrawJoinRequest(userId)`                           | Recruiting        | Own Pending request only.                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `ApproveJoinRequest(requestId)`                         | Recruiting        | Request Pending. `AcceptedTermsVersion == TermsVersion` (else `TERMS_NOT_ACCEPTED`). `count ≤ AvailableShares` (else `NOT_ENOUGH_SHARES`, with the available count). Creates the Membership.                                                                                                                                                                                                                                                          |
| `RejectJoinRequest(requestId, reason?)`                 | Recruiting        | Reason optional, max 500.                                                                                                                                                                                                                                                                                                                                                                                                                             |
| `InviteByPhone(phone, shareCount)`                      | Recruiting        | The phone does not belong to an Active member or have a Pending invite. `1 ≤ shareCount ≤ MaxSharesPerMember`. `shareCount ≤ AvailableShares`.                                                                                                                                                                                                                                                                                                        |
| `RevokeInvite(inviteId)`                                | Recruiting        | Pending invite only.                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `AcceptInvite(inviteId, userId, phone)`                 | Recruiting        | Pending invite and the phone matches. The user is not already a member. Creates the Membership at the current `TermsVersion`. Preferences can be supplied, as in a join request.                                                                                                                                                                                                                                                                      |
| `DeclineInvite(inviteId, userId, phone)`                | Recruiting        | Pending invite and the phone matches.                                                                                                                                                                                                                                                                                                                                                                                                                 |
| `ReacceptTerms(userId)`                                 | Recruiting        | Active membership. Sets `AcceptedTermsVersion = TermsVersion`. The same call also refreshes the user's Pending join request, if any.                                                                                                                                                                                                                                                                                                                  |
| `Leave(userId)`                                         | Recruiting        | Active membership, not the organizer (`ORGANIZER_CANNOT_LEAVE`). Status Left. Shares become available.                                                                                                                                                                                                                                                                                                                                                |
| `RemoveMember(userId, reason)`                          | Recruiting        | Reason required, 5 to 500 characters. Not the organizer. Status Removed.                                                                                                                                                                                                                                                                                                                                                                              |

Any operation in a status not listed throws `INVALID_STATUS_TRANSITION`.

**Invite code:** 6 characters from `ABCDEFGHJKMNPQRSTUVWXYZ23456789` (no 0/O, 1/I/L). It is generated in the Application layer with a cryptographic RNG and retried on a unique-index collision.

### 4.5 Activity events

Each operation raises an `IActivityEvent` with `GameyaId` set: `GAMEYA_CREATED`, `GAMEYA_SETTINGS_CHANGED` (changed fields old→new, plus `termsVersion` when bumped), `GAMEYA_OPENED`, `INVITE_CODE_REGENERATED`, `GAMEYA_CANCELLED`, `JOIN_REQUESTED`, `JOIN_REQUEST_WITHDRAWN`, `JOIN_REQUEST_APPROVED`, `JOIN_REQUEST_REJECTED`, `MEMBER_INVITED`, `INVITE_REVOKED`, `INVITE_ACCEPTED`, `INVITE_DECLINED`, `TERMS_REACCEPTED`, `MEMBER_LEFT`, `MEMBER_REMOVED`. As in the foundation, details never contain phone numbers or names: they reference user IDs and invite IDs. The invite code itself is never logged.

## 5. Application layer (`Gameya.Application/Gameyas`)

### 5.1 Authorization

- New attributes `[RequiresGameyaOrganizer]` and `[RequiresGameyaMember]` (organizer or Active member). They apply to requests implementing `IGameyaRequest { Guid GameyaId }`.
- `AuthorizationBehavior` requires authentication, then calls `IGameyaAccess.GetRoleAsync(gameyaId, userId)`, which returns `Organizer`, `Member` or `None` from one indexed query.
- `None` (or an organizer-only request from a member) throws `GAMEYA_NOT_FOUND` (404).
- The fail-closed rule (exactly one access attribute) stays.

### 5.2 Commands

| Access                          | Commands                                                                                                                                                                        |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `[RequiresAuth]`                | `CreateGameya`, `RequestToJoin(code, shares)`, `AcceptInvite`, `DeclineInvite`                                                                                                  |
| `[RequiresGameyaOrganizer]`     | `UpdateGameyaSettings`, `OpenForRecruiting`, `RegenerateInviteCode`, `CancelGameya`, `ApproveJoinRequest`, `RejectJoinRequest`, `InviteByPhone`, `RevokeInvite`, `RemoveMember` |
| `[RequiresGameyaMember]`        | `ReacceptTerms`, `LeaveGameya`                                                                                                                                                  |
| `[RequiresAuth]` + domain check | `WithdrawJoinRequest(gameyaId)` (the requester is not yet a member, so the domain checks ownership)                                                                             |

### 5.3 Queries

| Access                                 | Query                                          | Returns                                                                                                                                                                                                                                                            |
| -------------------------------------- | ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `[PublicAccess]` (rate-limited per IP) | `GetInvitePreview(code)`                       | FR-INV-03 fields, organizer name, available shares, rules text, order method, max shares per member. No member names. If logged in: `relationship` = `None`, `Member`, `RequestPending`, `Organizer`. Status `Full`, `Cancelled` or not found as distinct results. |
| `[RequiresAuth]`                       | `ListMyGameyas`                                | Gam'eyas where the user is the organizer or an Active member, with role, status, key numbers, own share count, needs-reaccept.                                                                                                                                     |
| `[RequiresAuth]`                       | `ListMyInvites`                                | Pending invites matched by the current user's phone, with gam'eya summary.                                                                                                                                                                                         |
| `[RequiresGameyaMember]`               | `GetGameya(id)`                                | Settings, derived values, status, own role and state. `InviteCode` only for the organizer.                                                                                                                                                                         |
| `[RequiresGameyaMember]`               | `GetMembers(id)`                               | Name, photo, share count, needs-reaccept. Phone through `DisplayPhone` (full for the organizer, masked for others, FR-AUTH-05).                                                                                                                                    |
| `[RequiresGameyaMember]`               | `GetGameyaActivity(id, page)`                  | Paged entries (Cairo time, actor name or "Deleted user", action, details).                                                                                                                                                                                         |
| `[RequiresGameyaOrganizer]`            | `GetJoinRequests(id)`, `GetPendingInvites(id)` | Pending items with requester name, masked/full phone, shares and preferences.                                                                                                                                                                                      |

All lookups and projections use named specifications, following the foundation's data-access rule.

### 5.4 Other services

- `GameyaParticipationChecker` replaces `NoParticipationChecker`: true if the user organizes a Draft, Recruiting or Active gam'eya, or has an Active membership in a Recruiting or Active gam'eya. This makes FR-AUTH-04 enforce the real rule.
- `IInviteCodeGenerator` (cryptographic RNG, the alphabet above).

### 5.5 Errors

New `ErrorCodes`: `GAMEYA_NOT_FOUND`, `INVALID_STATUS_TRANSITION`, `NOT_ENOUGH_SHARES`, `MAX_SHARES_EXCEEDED`, `SHARES_BELOW_ASSIGNED`, `TERMS_NOT_ACCEPTED`, `INVITE_CODE_INVALID`, `GAMEYA_FULL`, `ALREADY_MEMBER`, `REQUEST_ALREADY_PENDING`, `INVITE_NOT_FOUND`, `ORGANIZER_CANNOT_LEAVE`, `START_DATE_IN_PAST`, `INVALID_PREFERENCES`, `CONCURRENCY_CONFLICT`. FluentValidation mirrors the FR-GAM-01 field rules for clean 400 responses. The domain enforces them too and remains the source of truth. Each code gets Arabic and English text in the frontend i18n files.

### 5.6 Concurrency

`Gameya.RowVersion` is a SQL Server `rowversion` concurrency token. Any command changing the aggregate (including only its children) updates the root row through the auditing interceptor, so the token always guards the whole aggregate. `DbUpdateConcurrencyException` maps to 409 `CONCURRENCY_CONFLICT`. This guarantees that two approvals racing for the last share cannot overbook it.

## 6. Persistence (`Gameya.Infrastructure`)

| Table                 | Key columns                                                                                                                                                                                                          |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `gameyas`             | settings, `status`, `terms_version`, `invite_code`, `cancel_reason`, `cancelled_at`, `organizer_id` (FK users), `contribution_amount decimal(18,2)`, `currency char(3)`, `start_date date`, `row_version rowversion` |
| `memberships`         | `gameya_id`, `user_id`, `status`, `joined_at`, `accepted_terms_version`, `removal_reason`                                                                                                                            |
| `member_shares`       | `membership_id`, `preferences nvarchar(32)`                                                                                                                                                                          |
| `join_requests`       | `gameya_id`, `user_id`, `status`, `accepted_terms_version`, `reject_reason`                                                                                                                                          |
| `join_request_shares` | `join_request_id`, `preferences nvarchar(32)`                                                                                                                                                                        |
| `pending_invites`     | `gameya_id`, `phone`, `share_count`, `status`, `user_id`                                                                                                                                                             |

- Every table has `created_at`, `created_by_user_id`, `updated_at`, `updated_by_user_id`.
- Enums are stored as strings. `preferences` is a JSON array (`"[2,5]"`) with an EF value converter and value comparer.
- **Indexes:** unique `invite_code` filtered `IS NOT NULL`; unique `memberships (gameya_id, user_id)` filtered `status = 'Active'`; unique `join_requests (gameya_id, user_id)` filtered `status = 'Pending'`; unique `pending_invites (gameya_id, phone)` filtered `status = 'Pending'`; `gameyas (organizer_id, status)`; `memberships (user_id)`; `pending_invites (phone)`.
- `AuditingInterceptor` is registered next to `ActivityLogImmutabilityInterceptor`.
- **Migration `AddGameyas`:** creates the tables and adds the audit columns to `users` (`created_at` already exists; the three new columns start null).

## 7. API (`Gameya.Api/Endpoints`)

`GameyaEndpoints.cs` and `InviteEndpoints.cs`, minimal-API groups in the style of `MeEndpoints`.

```
POST   /api/gameyas                                   create (201 + id)
GET    /api/gameyas                                   my gam'eyas
GET    /api/gameyas/{id}                              details
PUT    /api/gameyas/{id}                              update settings
POST   /api/gameyas/{id}/open                         Draft → Recruiting
POST   /api/gameyas/{id}/cancel                       { reason? }
POST   /api/gameyas/{id}/invite-code                  regenerate
GET    /api/gameyas/{id}/members
DELETE /api/gameyas/{id}/members/{userId}             { reason }
POST   /api/gameyas/{id}/leave
POST   /api/gameyas/{id}/reaccept
GET    /api/gameyas/{id}/join-requests
POST   /api/gameyas/{id}/join-requests/{rid}/approve
POST   /api/gameyas/{id}/join-requests/{rid}/reject   { reason? }
DELETE /api/gameyas/{id}/join-requests/mine           withdraw
GET    /api/gameyas/{id}/invites
POST   /api/gameyas/{id}/invites                      { phone, shareCount }
DELETE /api/gameyas/{id}/invites/{inviteId}
GET    /api/gameyas/{id}/activity?page=
GET    /api/invite/{code}                             public preview (rate-limited)
POST   /api/invite/{code}/join                        { shares: [{ preferences: [] }] , acceptRules, acceptNoMoney }
GET    /api/me/invites
POST   /api/me/invites/{inviteId}/accept              { shares: [{ preferences: [] }], acceptRules, acceptNoMoney }
POST   /api/me/invites/{inviteId}/decline
```

Writes return 204 unless stated. Errors use the existing problem-details shape with the error code. The join and accept bodies require both checkboxes to be true (validation error otherwise).

## 8. Frontend (`src/gameya-web`)

New lazy features `features/gameyas` (authenticated) and `features/invite` (public), following the existing standalone-component, signals and i18n patterns. Usable at 360px, RTL first.

1. **Home / My gam'eyas** becomes the post-login landing page. Pending invites at the top (Accept opens the join form; Decline). Gam'eya cards grouped by status: name, status chip (text and icon), amount × shares, own share count, organizer badge, needs-reaccept marker. A Create button. The empty state explains what a gam'eya is and that the app never holds money.
2. **Create / edit form:** sections Basics, Money and schedule, Payout, Members, Rules. A live calculation card (pot per round, rounds and end date, total per share) is sticky at the bottom on phones. The due day is described in words. "My shares" stepper when the organizer participates. Editing a money field while Recruiting shows a confirmation that N members will need to re-accept.
3. **Gam'eya page:** header (name, status chip, key numbers) and tabs Overview, Members, Rules (including the fixed "app does not hold money" statement), Activity (paged, action codes rendered through i18n). Organizers get a **Manage** tab: invite card (code, copy link, Share on WhatsApp via a `wa.me` link with a pre-filled message in the organizer's language, regenerate), join requests with a count badge (approve, reject with optional reason), invite by phone and pending invites (revoke), edit settings, Open for recruiting, Cancel. Member rows get Remove (reason required). Members needing re-acceptance see a banner comparing old and new terms with Re-accept and Leave. Members get Leave while Recruiting.
4. **Invite preview `/j/:code`** (public): FR-INV-03 fields and rules, no member names. Join form: share stepper limited by max per member and available shares, preference pickers under By request (up to 3 per share or no preference), the two required checkboxes. Guests see "Log in to join", which goes through the OTP flow with `returnUrl` back to this page. Distinct pages for full, cancelled and invalid codes.

Shared pieces: `GameyaApi` service, status chip, reason dialog (remove, reject, cancel), money and date formatting via `core/format`. A 409 reloads the current data and shows a toast.

## 9. Testing

- **`Gameya.Domain.Tests`:** status transitions (allowed and forbidden); every field-rule edge; `TermsVersion` bumps only on amount, share count or frequency; organizer auto-reaccept; share capacity, max per member, invites reserving shares; share count not below assigned + reserved; preferences trimmed on reduction; preference validation; organizer cannot leave or be removed; approval blocked on stale terms; cancel side effects; derived values including the 31st→end-of-month end date and weekly end dates; invite code alphabet and length.
- **Auditing interceptor:** sets Created/Updated correctly, never overwrites Created, touches the root on child changes.
- **`Gameya.Application.Tests`:** access attribute coverage (existing test picks up new requests); `IGameyaAccess` behaviour for organizer, member and outsider; organizer-only request from a member returns `GAMEYA_NOT_FOUND`.
- **`Gameya.ArchitectureTests`:** existing layer and data-access rules; gam'eya DTOs expose phones only through `DisplayPhone`.
- **`Gameya.Api.IntegrationTests`** (real SQL Server): the full flow create → open → guest preview → join → approve → members; an outsider gets 404 on every `/api/gameyas/{id}/*` endpoint; two concurrent approvals for the last share (one 204, one 409); filtered unique indexes; account deletion blocked while in a Recruiting gam'eya; invite an unregistered phone → sign up → invite listed → accept; activity entries written for every command; preview rate limit.
- **e2e (`smoke.spec.ts`):** create → open → invite preview renders, in Arabic RTL.

## 10. Acceptance criteria

- **AC-G1:** An organizer creates a gam'eya with 10 shares, participating with 1 share, and sees the pot, duration and per-share totals update live. It is saved as Draft.
- **AC-G2:** Opening it produces a code and link. A logged-out visitor sees the preview without member names, logs in, requests 2 shares, and the organizer approves. 7 shares remain available.
- **AC-G3:** Approving a request for more shares than are available is refused with a message naming the available count.
- **AC-G4:** Changing the amount while Recruiting marks every member except the organizer as needing to re-accept, and the activity log shows the old and new amount. Re-accepting clears the flag.
- **AC-G5:** When the last share is assigned, the invite link shows "This gam'eya is full".
- **AC-G6:** A non-member requesting any gam'eya endpoint by ID receives 404.
- **AC-G7:** A user in a Recruiting gam'eya cannot delete their account.
- **AC-G8:** A phone number invited before registering sees the invite on first login and can accept it, taking the reserved shares.
