# Gam'eya App: Business Requirements (v1)

## 0. How to use this document

This file describes the **business requirements** for the Gam'eya app. It is the source of truth for scope and behavior. When a requirement is ambiguous, follow the Business Rules (section 6) first, then ask before inventing behavior.

Every requirement has an ID (e.g. `FR-ROUND-03`) so it can be referenced in stories, tasks, commits and tests.

---

## 1. Product overview

### 1.1 The problem
A gam'eya (جمعية) is a rotating savings circle, very common in Egypt. A group of people each pay a fixed amount every period (usually monthly), and each period one member receives the whole pot. After everyone has received the pot once, the gam'eya ends.

Today gam'eyas are managed in notebooks and WhatsApp groups. This causes:
- Disputes about who paid and who didn't.
- Forgotten payments and no reminders.
- Arguments about the payout order and its fairness.
- No history once the WhatsApp chat is lost.

### 1.2 The product
A web app (mobile-first, Arabic and English) that lets an **organizer** create a gam'eya, invite **members**, fix the payout order fairly, track every payment and payout, send reminders, and keep a permanent, transparent history.

### 1.3 Critical principle: the app does NOT hold money
The app is a **tracking and coordination tool only**. Money moves between people outside the app (cash, InstaPay, mobile wallets, bank transfer). The app records and confirms these movements. It never receives, stores, or transfers money. This must be stated clearly in the UI (onboarding and the gam'eya rules page).

---

## 2. Glossary

| Term | Meaning |
|---|---|
| **Gam'eya** | One savings circle with fixed members, amount, and schedule. |
| **Organizer** | The user who created the gam'eya and manages it. Equivalent to "صاحب/صاحبة الجمعية". |
| **Member** | A user who joined the gam'eya and holds one or more shares. The organizer may also be a member. |
| **Share (سهم)** | One slot in the gam'eya. Each share pays one contribution per round and receives the pot exactly once. A member may hold more than one share. |
| **Contribution amount** | The fixed amount each share pays every round (e.g. 2,000 EGP). |
| **Pot** | Total collected in one round = contribution amount × number of shares. |
| **Round (دور / شهر)** | One period of the gam'eya. Number of rounds = number of shares. |
| **Due date** | The date a round's contributions must be paid by. |
| **Grace period** | Number of days after the due date before a contribution becomes Overdue. |
| **Payout** | Handing the pot to the share whose turn it is in that round. |
| **Payout order** | The ordered list of shares saying which share receives the pot in which round. |
| **Proof** | An image (screenshot or photo of a receipt) uploaded by a member as evidence of payment. |

---

## 3. Roles and permissions

### 3.1 Roles
- **Guest**: not logged in. Can only view the landing page and an invitation preview.
- **User**: logged in, can create or join gam'eyas.
- **Organizer**: per gam'eya; the creator. Exactly one organizer per gam'eya in v1.
- **Member**: per gam'eya; holds at least one share.
- **Platform Admin**: internal role for support (see FR-ADMIN).

A user can be organizer of some gam'eyas and member of others at the same time.

### 3.2 Permission matrix

| Action | Organizer | Member | Non-member user |
|---|---|---|---|
| View gam'eya details, rounds, payout order | ✅ | ✅ | ❌ (only invite preview) |
| Edit gam'eya settings (before Active) | ✅ | ❌ | ❌ |
| Invite / approve / remove members (before Active) | ✅ | ❌ | ❌ |
| Set payout order / run the draw | ✅ | ❌ | ❌ |
| Start the gam'eya | ✅ | ❌ | ❌ |
| Submit own payment with proof | ✅ (for own shares) | ✅ (own shares only) | ❌ |
| Record payment on behalf of another member | ✅ | ❌ | ❌ |
| Confirm / reject a submitted payment | ✅ | ❌ | ❌ |
| View other members' payment **status** | ✅ | ✅ | ❌ |
| View other members' **proof images** | ✅ | ❌ (own only) | ❌ |
| Record a payout | ✅ | ❌ | ❌ |
| Confirm receipt of own payout | ✅ (if recipient) | ✅ (if recipient) | ❌ |
| Request a turn swap | ✅ (own shares) | ✅ (own shares) | ❌ |
| Approve a turn swap | ✅ | ❌ | ❌ |
| Post announcements | ✅ | ❌ | ❌ |
| View activity log | ✅ | ✅ | ❌ |
| Cancel the gam'eya | ✅ (rules in FR-GAM-08) | ❌ | ❌ |

---

## 4. Scope

### 4.1 In scope for v1
Accounts with phone login, creating and configuring a gam'eya, invitations and joining, multiple shares per member, payout order (manual, random draw, by request), automatic round generation, payment submission with proof and organizer confirmation, payouts with recipient confirmation, turn swaps, overdue handling, reminders and notifications (in-app and email), member and organizer dashboards, announcements, immutable activity log, Arabic/English with RTL, basic platform admin.

### 4.2 Out of scope for v1 (planned for later)
- WhatsApp and SMS notifications (design for it, implement later). Exception: OTP SMS for login is in scope.
- Native mobile apps (v1 is a responsive web app; PWA install is a nice-to-have).
- Member reliability/trust score across gam'eyas (v1.1).
- Partial payments.
- Multiple organizers / co-organizers.
- Weekly or custom frequencies other than those in FR-GAM-02.
- Any in-app money movement or payment gateway.
- Interest, fees, or "discount for early payout" calculations.
- Chat between members (announcements only).

---

## 5. Functional requirements

### 5.1 Accounts and authentication (FR-AUTH)

**FR-AUTH-01: Sign up and login with phone number.**
- Primary identity is an Egyptian mobile number (format `01XXXXXXXXX`, 11 digits, prefixes 010/011/012/015). Store in E.164 (`+201XXXXXXXXX`).
- Login uses a one-time password (OTP) sent by SMS.
- OTP is 6 digits, valid for 5 minutes, max 5 wrong attempts, then the number is locked for 15 minutes.
- Resend OTP allowed after 60 seconds, max 3 resends per 15 minutes.
- In development environment, OTP sending can be mocked (log the code).

**FR-AUTH-02: Profile.**
- Required: full name (2–60 chars), phone (verified).
- Optional: email (used for email notifications), profile photo.
- Preferred language: Arabic (default) or English.

**FR-AUTH-03: Sessions.**
- Access token plus refresh token. User stays logged in on the device until they log out or the refresh token expires (30 days of inactivity).
- User can log out from all devices.

**FR-AUTH-04: Account deletion.**
- A user can request deletion only if they are not in any gam'eya with status Recruiting or Active.
- After deletion, their name in historical records of completed gam'eyas is replaced with "Deleted user", but the records themselves stay (other members need the history).

**FR-AUTH-05: Phone number privacy.**
- Other members see a masked phone number (e.g. `010****4567`), except the organizer, who sees full numbers of their gam'eya's members.

---

### 5.2 Gam'eya creation and lifecycle (FR-GAM)

**FR-GAM-01: Create a gam'eya.** Any user can create one. Fields:

| Field | Rules |
|---|---|
| Name | Required, 3–60 characters. |
| Description | Optional, max 500 characters. |
| Contribution amount per share | Required, whole EGP, min 100, max 1,000,000. |
| Currency | EGP only in v1 (store currency code anyway). |
| Number of shares | Required, integer 2–50. |
| Frequency | Monthly (default) or Weekly. |
| Start date (first due date) | Required, must be today or in the future at creation and at start. |
| Due day | Derived from start date (e.g. monthly on the 5th; weekly on the same weekday). |
| Grace period | 0–10 days, default 3. |
| Payout timing | "After full collection" (default) or "Fixed date": N days after the due date (0–10). |
| Payout order method | Manual, Random draw, or By request (see FR-ORDER). |
| Max shares per member | 1–5, default 2. |
| Organizer participates | Yes/No. If yes, the organizer holds at least one share. |
| Rules text | Optional free text, max 2,000 characters (e.g. "late payment = 50 EGP penalty, paid outside the app"). Shown to every member before joining. |

The system displays calculated values live while creating: **pot per round** (amount × shares), **total duration** (number of rounds and expected end date), and **total each share pays** (amount × rounds), which always equals the pot.

**FR-GAM-02: Frequency and dates.**
- Monthly: each round's due date is the same day of the month as the start date. If the day doesn't exist in a month (e.g. 31st), use the last day of that month.
- Weekly: every 7 days from the start date.
- All dates are in the `Africa/Cairo` time zone.

**FR-GAM-03: Gam'eya statuses.**

```
Draft ──► Recruiting ──► Active ──► Completed
  │            │            │
  └────────────┴──► Cancelled ◄── (Active, only under FR-GAM-08)
```

- **Draft**: created, not yet open for joining. Organizer can edit everything.
- **Recruiting**: invite link is live; members can join. Organizer can still edit settings, but changing amount, number of shares, or frequency notifies all current members, and each member must re-accept the new terms or they are removed from the gam'eya.
- **Active**: started. Settings are locked (see FR-GAM-06).
- **Completed**: all rounds paid out and confirmed (or closed by the organizer per FR-PAY-07).
- **Cancelled**: stopped. Read-only.

**FR-GAM-04: Open for recruiting.** Organizer moves Draft → Recruiting. This generates an invite link and code (FR-INV).

**FR-GAM-05: Start the gam'eya (Recruiting → Active).** Allowed only if all of these are true:
1. All shares are assigned to members (no empty shares).
2. Every member has accepted the current terms.
3. Payout order is finalized (FR-ORDER).
4. Start date is today or in the future. If it has passed, the organizer must pick a new start date first.

On start, the system:
- Generates all rounds (FR-ROUND-01).
- Generates all contribution records for every share in every round (FR-PAY-01).
- Locks settings.
- Notifies all members with the schedule and their payout round(s).

**FR-GAM-06: Locked settings when Active.** The following can no longer change: amount, number of shares, frequency, dates, payout order (except approved swaps, FR-SWAP), member list (except replacement, FR-MEM-05). Editable while Active: name, description, rules text (rules changes are logged and notified), grace period can be increased but not decreased.

**FR-GAM-07: Completion.** When the last round's payout is confirmed by the recipient (or closed by the organizer), status becomes Completed automatically. All members receive a summary: total contributed per member, round each member received the pot, and any rounds closed with notes.

**FR-GAM-08: Cancellation.**
- Draft / Recruiting: organizer can cancel anytime, with an optional reason. All members are notified.
- Active: organizer can cancel only with a mandatory reason (min 20 chars). The system shows a **settlement summary** before confirming: for each member, total paid in vs total received. Members are notified with this summary. Settlement itself happens outside the app.
- Cancelled gam'eyas stay visible (read-only) in history.

**FR-GAM-09: Duplicate a gam'eya.** From a Completed or Cancelled gam'eya, the organizer can create a new Draft copying settings and offering to re-invite the same members.

---

### 5.3 Invitations and joining (FR-INV)

**FR-INV-01: Invite link and code.** Each gam'eya in Recruiting has a shareable link and a 6-character code (letters and digits, no ambiguous characters like 0/O, 1/I). The organizer can regenerate them, which invalidates the old ones.

**FR-INV-02: Share via WhatsApp.** A "Share on WhatsApp" button opens WhatsApp with a pre-filled Arabic/English message containing the gam'eya name, amount, number of shares, start date, and the link.

**FR-INV-03: Invite preview.** Opening the link (even when logged out) shows: name, organizer name, amount, shares, pot, frequency, start date, available shares count, and rules text. It does not show other members' names until joined.

**FR-INV-04: Join request.**
- The user chooses how many shares they want (1 up to max shares per member and up to available shares).
- The user must tick "I have read and accept the rules" and "I understand the app does not hold money".
- If the payout method is "By request", the user can choose preferred rounds (FR-ORDER-04).
- The request goes to the organizer as **Pending**.

**FR-INV-05: Organizer approval.** The organizer approves or rejects each request (optional rejection reason). Approval assigns the shares. If approving would exceed available shares, approval is blocked with a clear message.

**FR-INV-06: Direct add.** The organizer can add an existing user by phone number. If the number isn't registered, the system stores a pending invite and the person joins automatically after signing up with that number (they still must accept the terms).

**FR-INV-07: Join closes automatically** when all shares are assigned. Further visitors to the link see "This gam'eya is full".

---

### 5.4 Members (FR-MEM)

**FR-MEM-01: Member list.** Shows each member's name, photo, number of shares, payout round(s), and (when Active) their payment status for the current round.

**FR-MEM-02: Leave before start.** A member can leave anytime while Recruiting. Their shares become available again and any payout order entries for them are cleared.

**FR-MEM-03: Remove before start.** The organizer can remove a member while Recruiting, with a reason. The member is notified.

**FR-MEM-04: No leaving after start.** When Active, a member cannot leave by themselves. The UI explains they must contact the organizer.

**FR-MEM-05: Replacement after start.** While Active, the organizer can replace a member's share with a new person **only if that share has not yet received its payout**:
- The new person must accept the terms.
- The replacement takes effect from the next round that has not started collecting. Past contributions stay recorded under the original member.
- How money is settled between the old and new member happens outside the app; the organizer must enter a note.
- If the share has already received its payout, replacement is blocked; the member is still obliged to pay remaining rounds. The organizer can only add notes and mark contributions as Defaulted (FR-PAY-06).

---

### 5.5 Payout order (FR-ORDER)

**FR-ORDER-01: Each share gets exactly one round.** The payout order is a one-to-one mapping between shares and rounds. A member with 2 shares gets 2 rounds.

**FR-ORDER-02: Manual.** The organizer drags and drops shares into rounds. Can be changed freely until start.

**FR-ORDER-03: Random draw.**
- Runs only when all shares are assigned.
- The organizer presses "Run draw"; members currently viewing the gam'eya see the result appear live.
- The system records the draw time, who ran it, and the random seed, all visible in the activity log, so the draw is auditable.
- The organizer may lock some rounds before the draw (e.g. "round 1 goes to me as organizer") only if this is written in the rules text; locked rounds are shown as such to everyone.
- A draw can be re-run only before start, and every re-run is logged and visible to all members (to discourage re-rolling).

**FR-ORDER-04: By request.**
- When joining, members pick preferred rounds (up to 3 preferences per share), or "no preference".
- The organizer sees a grid of requests and conflicts, and resolves them manually, or presses "Auto-assign": the system satisfies first preferences first, resolves conflicts by join order (earlier join wins), then fills the rest randomly. The organizer can adjust the result.

**FR-ORDER-05: Finalize.** The organizer presses "Finalize order". All members are notified of their round(s). Finalized order can be reopened only before start.

---

### 5.6 Rounds (FR-ROUND)

**FR-ROUND-01: Generation.** On start, the system creates N rounds (N = number of shares), each with: round number, due date, overdue date (due date + grace), expected payout date (per payout timing), recipient share, and pot amount.

**FR-ROUND-02: Round statuses.**
- **Upcoming**: due date is more than 7 days away (monthly) or 3 days away (weekly).
- **Collecting**: within the collection window until all contributions are confirmed.
- **Fully collected**: all contributions for the round are Confirmed (or Waived/Defaulted closed by the organizer, FR-PAY-06).
- **Paid out**: organizer has recorded the payout.
- **Closed**: recipient confirmed receipt (or auto-closed per FR-PAY-09).

Members can pay a round early, even while it is Upcoming.

**FR-ROUND-03: Round view.** Shows: round number, dates, recipient, pot, progress bar (confirmed amount / pot), and the list of all shares with their contribution status.

**FR-ROUND-04: Current round.** The "current round" is the earliest round that is not Closed. Dashboards highlight it.

---

### 5.7 Contributions and payments (FR-PAY)

**FR-PAY-01: Contribution records.** On start, the system creates one contribution per share per round, with amount = contribution amount. The recipient share **also** has a contribution in its own payout round (it pays like everyone; the pot includes it). Organizer can mark it as "Deducted from payout" in one tap, which confirms it with method "Deducted".

**FR-PAY-02: Contribution statuses.**

| Status | Meaning |
|---|---|
| Pending | Not paid yet, not past overdue date. |
| Submitted | Member reported a payment; waiting for organizer confirmation. |
| Confirmed | Organizer confirmed the money was received. |
| Rejected | Organizer rejected the submission (reason required). Goes back to effectively unpaid; member can resubmit. |
| Overdue | Not confirmed and today is after the overdue date. Can still be submitted and confirmed (it then becomes Confirmed with a "late" flag). |
| Defaulted | Organizer marked it as not going to be paid (reason required). |
| Waived | Organizer covered or forgave it (reason required); counts as collected. |

**FR-PAY-03: Member submits a payment.**
- Choose method: Cash, InstaPay, Mobile wallet (Vodafone Cash, Orange Cash, Etisalat Cash, WE Pay, other), Bank transfer, Other.
- Proof image: required for InstaPay, wallet, and bank transfer; optional for cash. Formats JPG/PNG/HEIC/PDF, max 5 MB, max 3 files.
- Optional reference number and note (max 200 chars).
- Payment date (defaults to today, cannot be in the future).
- Amount is fixed to the contribution amount (no partial payments in v1).
- A member can submit for several shares and rounds in one action (e.g. pay 2 shares, or pay 2 months in advance); the system creates one submission per contribution, linked by a shared payment group ID.

**FR-PAY-04: Organizer confirms or rejects.**
- Organizer sees a queue of Submitted payments with proofs.
- Confirm: one tap, or bulk confirm.
- Reject: reason required (e.g. "transfer not received"). Member is notified.
- Confirming notifies the member ("Your payment for round 3 was confirmed").

**FR-PAY-05: Organizer records directly.** The organizer can mark any contribution as Confirmed directly (e.g. received cash in person), choosing the method, with optional proof and note. The activity log records that the organizer recorded it, not the member.

**FR-PAY-06: Defaulted and Waived.** For an Overdue contribution the organizer can mark it Defaulted or Waived with a reason. Both close the contribution for round-completion purposes. Defaulted contributions stay visible on the member's record and in the final summary.

**FR-PAY-07: Undo.** The organizer can revert a Confirmed, Waived or Defaulted contribution back to Pending/Overdue within 48 hours, with a mandatory reason, only if that round's payout hasn't been recorded. Everything is logged.

**FR-PAY-08: Overdue processing.** A daily job (runs at 00:30 Cairo time) moves contributions past their overdue date to Overdue and triggers notifications (FR-NOTIF).

**FR-PAY-09: Round payout.**
- When a round is Fully collected (or the organizer chooses "Pay out anyway" with a mandatory note explaining missing contributions), the organizer records the payout: date, method, optional proof, optional note.
- Round becomes Paid out; the recipient is notified and asked to confirm receipt.
- The recipient presses "I received it" → round becomes Closed.
- The recipient can press "I did not receive it" with a note → round is flagged **Disputed**, organizer is notified, and the flag shows to all members until the organizer and recipient resolve it (recipient confirms later, or organizer adds a resolution note).
- If the recipient takes no action for 7 days, the round auto-closes with the note "Auto-closed: no response from recipient".

**FR-PAY-10: Payment history.** Each member can see all their contributions across all gam'eyas, filterable by gam'eya and status, with totals: paid, pending, overdue, and payouts received.

---

### 5.8 Turn swaps (FR-SWAP)

**FR-SWAP-01: Request.** While Active, a member can request to swap the payout round of one of their shares with another member's share.
- Both rounds must be in status Upcoming or Collecting with no payout recorded.
- A share that already received its payout cannot be swapped.

**FR-SWAP-02: Approval flow.** Requester → target member accepts or declines → organizer approves or rejects. A request expires after 3 days without action.

**FR-SWAP-03: Effect.** On approval, the recipients of the two rounds are exchanged. All members are notified ("Round 4 and round 7 recipients swapped"). Logged.

**FR-SWAP-04: Organizer swap.** The organizer can propose a swap between two members; both members must accept.

---

### 5.9 Notifications (FR-NOTIF)

**FR-NOTIF-01: Channels.** v1: in-app notifications (bell with unread count) and email (if the user has an email). Design the notification system so WhatsApp/SMS/push can be added later as new channels without changing business logic.

**FR-NOTIF-02: User settings.** Each user can turn email on/off per category (reminders, payment updates, gam'eya updates). In-app notifications are always on.

**FR-NOTIF-03: Events and recipients.**

| Event | Recipient(s) |
|---|---|
| Join request received | Organizer |
| Join request approved / rejected | Requester |
| Terms changed (Recruiting) – re-accept needed | All members |
| Payout order finalized / draw run | All members |
| Gam'eya started (with schedule) | All members |
| Payment reminder: 3 days before due date | Members with unpaid contributions in that round |
| Payment reminder: on due date | Members with unpaid contributions |
| Payment overdue: day after overdue date, then every 3 days (max 4 times) | The overdue member; daily digest of overdue members to organizer |
| Payment submitted | Organizer |
| Payment confirmed / rejected | The member |
| Round fully collected | Organizer, round recipient |
| Payout recorded | Recipient |
| Payout disputed | Organizer |
| "Your payout round is next" (7 days before due date of that round) | Recipient |
| Swap requested / accepted / approved / expired | Involved members, organizer |
| Announcement posted | All members |
| Member replaced | All members |
| Gam'eya completed / cancelled (with summary) | All members |

**FR-NOTIF-04: Manual reminder.** Organizer can press "Remind" on an unpaid member (max once per 24 hours per member per round) or "Remind all unpaid". Also a "Copy WhatsApp reminder" button generating a polite pre-filled message to send manually.

**FR-NOTIF-05: Language.** Notifications are sent in each recipient's preferred language.

---

### 5.10 Dashboards (FR-DASH)

**FR-DASH-01: Home (member view).** For the logged-in user:
- List of gam'eyas grouped by status (Active first), each card showing: name, my shares, current round number / total, my next due amount and date, my status for the current round, my payout round(s) and date(s).
- "Due soon" section: all my unpaid contributions in the next 14 days across all gam'eyas, with a pay button.
- Totals: total I paid this month, my upcoming payouts.

**FR-DASH-02: Gam'eya page (all members).** Tabs: Overview (key numbers, current round progress, next recipient), Rounds, Members, Activity log, Announcements, Rules.

**FR-DASH-03: Organizer panel.** Extra for organizers: pending join requests, pending payment confirmations (with count badge), overdue list with remind buttons, swap requests, quick actions (record cash payment, record payout).

**FR-DASH-04: Round grid.** A table: rows = members/shares, columns = rounds, each cell colored by contribution status. Visible to all members (status only, no proofs).

---

### 5.11 Announcements (FR-ANN)

- Organizer can post announcements (max 1,000 characters) to a gam'eya. Members are notified.
- Organizer can pin one announcement.
- Members can react with a "Seen" acknowledgment; organizer sees who has seen it.
- No comments/chat in v1.

---

### 5.12 Activity log (FR-LOG)

**FR-LOG-01: What is logged.** Every state change: creation, settings changes (old → new values), members joining/leaving/removed/replaced, draws (with seed), order changes, start, each contribution status change, payout records, confirmations, disputes, swaps, announcements, cancellation.

**FR-LOG-02: Format.** Each entry: timestamp (Cairo time), actor, action, target, details, optional note.

**FR-LOG-03: Immutable.** Log entries cannot be edited or deleted by anyone, including the organizer and platform admin.

**FR-LOG-04: Visibility.** All members see the full log of their gam'eya. Proof images are not shown in the log to non-organizers.

---

### 5.13 Reports and export (FR-REP)

- **FR-REP-01:** Gam'eya summary (any time): per member total due so far, total paid, overdue, payouts received.
- **FR-REP-02:** Organizer can export the round grid and payment list to PDF (Arabic and English, RTL correct) and Excel/CSV.
- **FR-REP-03:** Final summary is generated automatically on Completed/Cancelled (FR-GAM-07, FR-GAM-08) and stays downloadable.

---

### 5.14 Platform admin (FR-ADMIN)

- View users and gam'eyas (read-only by default).
- Block/unblock a user (blocked users cannot log in; their gam'eyas stay visible to other members).
- View reported issues (a "Report a problem" form on every gam'eya page).
- Basic stats: users, active gam'eyas, rounds closed per month.
- Admin cannot edit contributions, payouts, or the activity log.

---

## 6. Business rules (summary)

1. The app never holds or moves money.
2. Number of rounds always equals number of shares.
3. Each share receives the pot exactly once.
4. Pot = contribution amount × number of shares; every share, including the recipient, contributes every round.
5. No partial payments in v1; one contribution = full contribution amount.
6. Only the organizer can mark a contribution as Confirmed, Waived, or Defaulted.
7. Settings that affect money (amount, shares, frequency, dates, order) are locked once Active, except via approved swaps and replacements.
8. A share that already received its payout cannot be swapped or replaced.
9. A member cannot leave an Active gam'eya by themselves.
10. All dates and scheduled jobs use Africa/Cairo time.
11. Money is stored as decimal with 2 places (even though v1 input is whole EGP).
12. The activity log is append-only.
13. Every destructive or exceptional action (reject, default, waive, undo, cancel, pay out despite missing contributions, replacement) requires a written reason.

---

## 7. Key edge cases

- **Start date passed while Recruiting** → cannot start until a new date is set (FR-GAM-05).
- **Member pays 2 rounds in advance** → both contributions become Submitted; organizer confirms both.
- **Organizer is also the round recipient** → organizer records the payout to themselves and confirms receipt; log shows both actions.
- **Recipient has overdue contributions from earlier rounds** → payout still allowed, but the organizer sees a warning listing the recipient's unpaid contributions before recording the payout.
- **Member with 2 shares receives 2 payouts** → each in its own round; the member's dashboard shows both.
- **31st of month** → last day of shorter months (FR-GAM-02).
- **Organizer wants to quit** → not supported in v1; organizer must cancel per FR-GAM-08. (Transfer of organizer role is a later feature.)
- **User deletes their account** → blocked while in Recruiting/Active gam'eyas (FR-AUTH-04).
- **Proof uploaded is wrong** → organizer rejects with reason; member resubmits.
- **Two people submit for the same contribution** → impossible; only the share owner (or organizer) can submit, and a contribution in Submitted/Confirmed cannot be submitted again.

---

## 8. Acceptance criteria for core flows (examples)

**AC-01: Start gam'eya**
- Given a gam'eya in Recruiting with 10 shares, all assigned, all members accepted terms, and a finalized order,
- When the organizer presses Start,
- Then status becomes Active, 10 rounds and 100 contributions are created, settings are locked, and all members receive a "Gam'eya started" notification with their payout round.

**AC-02: Start blocked**
- Given 9 of 10 shares are assigned,
- When the organizer presses Start,
- Then the system refuses and shows "1 share is still empty".

**AC-03: Submit and confirm payment**
- Given round 2 is Collecting and Ahmed's contribution is Pending,
- When Ahmed submits InstaPay with a screenshot,
- Then the contribution becomes Submitted and the organizer is notified;
- When the organizer confirms it,
- Then it becomes Confirmed, Ahmed is notified, and the round progress bar increases by one contribution amount.

**AC-04: Overdue**
- Given due date is the 5th and grace is 3 days,
- When the daily job runs on the 9th and Mona's contribution is still Pending,
- Then it becomes Overdue, Mona gets an overdue notification, and the organizer's digest lists Mona.

**AC-05: Payout and dispute**
- Given round 3 is Fully collected,
- When the organizer records the payout,
- Then the round becomes Paid out and the recipient is asked to confirm;
- When the recipient presses "I did not receive it" with a note,
- Then the round shows Disputed to all members and the organizer is notified.

**AC-06: Swap blocked**
- Given Sara already received her payout in round 1,
- When Sara tries to swap round 1 with round 6,
- Then the system refuses with "This share has already received its payout".

**AC-07: Random draw is auditable**
- Given all shares are assigned and the method is Random draw,
- When the organizer runs the draw twice,
- Then both draws, their seeds, and their results appear in the activity log visible to all members.

---

## 9. Non-functional requirements

- **Localization:** Arabic (default, full RTL) and English. All UI text, notifications, emails, PDFs. Numbers formatted per language; currency shown as "EGP" / "ج.م".
- **Mobile-first:** Fully usable on a 360px-wide phone screen. Most users will use phones.
- **Performance:** Main pages load in under 2 seconds on 4G for gam'eyas up to 50 shares.
- **Security:** Authorization checked server-side on every request (a member must never access another gam'eya's data by changing an ID). Proof images stored privately, served only through authorized, short-lived URLs. Rate limiting on OTP and login.
- **Privacy:** Masked phone numbers (FR-AUTH-05); proofs visible only to owner and organizer.
- **Reliability:** Scheduled jobs (overdue, reminders, auto-close) are idempotent: running twice must not double-send notifications or double-change statuses.
- **Auditability:** Activity log append-only (FR-LOG-03); soft-delete only, never hard-delete financial records.
- **Accessibility:** Sufficient color contrast; statuses shown with text/icons, not color alone.

---

## 10. Technical preferences (constraints for implementation)

- Backend: ASP.NET Core, Clean Architecture, CQRS with MediatR, EF Core.
- Frontend: Angular, responsive, RTL support.
- Background jobs for scheduled work (overdue processing, reminders, auto-close).
- Unit tests with xUnit for domain rules (especially sections 6 and 7).
- Docker, CI with GitHub Actions, deployment to Azure.

Details beyond these (database choice, job library, file storage, SMS provider) are open for the implementation plan to propose.
