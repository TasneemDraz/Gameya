using Gameya.Domain.Common;
using Gameya.Domain.Users;

namespace Gameya.Domain.Gameyas;

// Named GameyaCircle because a type called Gameya would clash with the root namespace.
public class GameyaCircle : AuditableEntity
{
    private const int MaxReasonLength = 500;
    private const int MaxPreferencesPerShare = 3;

    private readonly List<Membership> _memberships = [];
    private readonly List<JoinRequest> _joinRequests = [];
    private readonly List<PendingInvite> _pendingInvites = [];

    private GameyaCircle()
    {
    }

    public Guid Id { get; private set; }
    public Guid OrganizerId { get; private set; }
    public GameyaStatus Status { get; private set; }
    public int TermsVersion { get; private set; }
    public string? InviteCode { get; private set; }
    public string? CancelReason { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string Currency { get; private set; } = "EGP";

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal ContributionAmount { get; private set; }
    public int ShareCount { get; private set; }
    public GameyaFrequency Frequency { get; private set; }
    public DateOnly StartDate { get; private set; }
    public int GraceDays { get; private set; }
    public PayoutTiming PayoutTiming { get; private set; }
    public int? PayoutOffsetDays { get; private set; }
    public OrderMethod OrderMethod { get; private set; }
    public int MaxSharesPerMember { get; private set; }
    public bool OrganizerParticipates { get; private set; }
    public string? RulesText { get; private set; }

    public IReadOnlyCollection<Membership> Memberships => _memberships;
    public IReadOnlyCollection<JoinRequest> JoinRequests => _joinRequests;
    public IReadOnlyCollection<PendingInvite> PendingInvites => _pendingInvites;

    public decimal Pot => ContributionAmount * ShareCount;
    public int RoundCount => ShareCount;
    public decimal TotalPerShare => ContributionAmount * ShareCount;

    public DateOnly EndDate => Frequency == GameyaFrequency.Monthly
        ? CairoCalendar.MonthlyDueDate(StartDate, ShareCount - 1)
        : StartDate.AddDays(7 * (ShareCount - 1));

    public int AssignedShares => _memberships
        .Where(m => m.Status == MembershipStatus.Active)
        .Sum(m => m.Shares.Count);

    // Pending invites hold shares without a membership; pending join requests hold none until approved.
    public int ReservedShares => _pendingInvites
        .Where(i => i.Status == InviteStatus.Pending)
        .Sum(i => i.ShareCount);

    public int AvailableShares => ShareCount - AssignedShares - ReservedShares;
    public bool IsFull => AvailableShares == 0;

    public static GameyaCircle Create(
        Guid id, Guid organizerId, GameyaSettings settings, int organizerShares, DateOnly today, DateTimeOffset now)
    {
        var clean = Validate(settings, organizerShares, today, checkStartDate: true);

        var gameya = new GameyaCircle
        {
            Id = id,
            OrganizerId = organizerId,
            Status = GameyaStatus.Draft,
            TermsVersion = 1,
        };
        gameya.Apply(clean);

        if (clean.OrganizerParticipates)
            gameya._memberships.Add(Membership.Create(id, organizerId, organizerShares, gameya.TermsVersion, now));

        gameya.Raise(new GameyaCreated(id, clean.ShareCount, organizerShares));
        return gameya;
    }

    public void OpenForRecruiting(string code, DateOnly today)
    {
        if (Status != GameyaStatus.Draft)
            throw new DomainException(ErrorCodes.INVALID_STATUS_TRANSITION);
        if (StartDate < today)
            throw new DomainException(ErrorCodes.START_DATE_IN_PAST);

        InviteCode = code;
        Status = GameyaStatus.Recruiting;
        Raise(new GameyaOpened(Id));
    }

    public void RegenerateInviteCode(string code)
    {
        if (Status != GameyaStatus.Recruiting)
            throw new DomainException(ErrorCodes.INVALID_STATUS_TRANSITION);

        InviteCode = code;
        Raise(new InviteCodeRegenerated(Id));
    }

    public JoinRequest RequestToJoin(Guid userId, IReadOnlyList<ShareRequest> shares)
    {
        RequireRecruiting();
        if (IsActiveMember(userId))
            throw new DomainException(ErrorCodes.ALREADY_MEMBER);
        if (_joinRequests.Any(r => r.UserId == userId && r.Status == JoinRequestStatus.Pending))
            throw new DomainException(ErrorCodes.REQUEST_ALREADY_PENDING);
        RequireCapacity(shares.Count);
        ValidatePreferences(shares);

        var request = JoinRequest.Create(Id, userId, shares, TermsVersion);
        _joinRequests.Add(request);
        Raise(new JoinRequested(Id, request.Id, userId, shares.Count));
        return request;
    }

    public void WithdrawJoinRequest(Guid userId)
    {
        // Ownership before status: a stranger must not learn that the gam'eya exists or what state it is in.
        var request = _joinRequests.FirstOrDefault(r => r.UserId == userId && r.Status == JoinRequestStatus.Pending)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);
        RequireRecruiting();

        request.Withdraw();
        Raise(new JoinRequestWithdrawn(Id, request.Id));
    }

    public Membership ApproveJoinRequest(Guid requestId, DateTimeOffset now)
    {
        RequireRecruiting();
        var request = FindPendingRequest(requestId);
        if (IsActiveMember(request.UserId))
            throw new DomainException(ErrorCodes.ALREADY_MEMBER);
        if (request.AcceptedTermsVersion != TermsVersion)
            throw new DomainException(ErrorCodes.TERMS_NOT_ACCEPTED);
        RequireCapacity(request.Shares.Count);

        var membership = Membership.Create(
            Id, request.UserId, [.. request.Shares.Select(s => s.Preferences)], TermsVersion, now);
        _memberships.Add(membership);
        request.Approve();
        Raise(new JoinRequestApproved(Id, request.Id, request.UserId, request.Shares.Count));
        return membership;
    }

    public void RejectJoinRequest(Guid requestId, string? reason)
    {
        RequireRecruiting();
        if (reason is { Length: > MaxReasonLength })
            throw Invalid("reason");
        var request = FindPendingRequest(requestId);

        request.Reject(reason);
        Raise(new JoinRequestRejected(Id, request.Id, reason));
    }

    public PendingInvite InviteByPhone(PhoneNumber phone, Guid? registeredUserId, int shareCount)
    {
        RequireRecruiting();
        if (registeredUserId is { } userId && IsActiveMember(userId))
            throw new DomainException(ErrorCodes.ALREADY_MEMBER);
        if (_pendingInvites.Any(i => i.Status == InviteStatus.Pending && i.Phone == phone))
            throw new DomainException(ErrorCodes.REQUEST_ALREADY_PENDING);
        RequireCapacity(shareCount);

        var invite = PendingInvite.Create(Id, phone, registeredUserId, shareCount);
        _pendingInvites.Add(invite);
        Raise(new MemberInvited(Id, invite.Id, shareCount, registeredUserId));
        return invite;
    }

    public void RevokeInvite(Guid inviteId)
    {
        RequireRecruiting();
        var invite = FindPendingInvite(inviteId);

        invite.Revoke();
        Raise(new InviteRevoked(Id, invite.Id));
    }

    public Membership AcceptInvite(
        Guid inviteId, Guid userId, PhoneNumber phone, IReadOnlyList<ShareRequest> shares, DateTimeOffset now)
    {
        // The invite and phone match first, so an unrelated invite id reveals nothing about its gam'eya.
        var invite = FindPendingInvite(inviteId, phone);
        RequireRecruiting();
        if (IsActiveMember(userId))
            throw new DomainException(ErrorCodes.ALREADY_MEMBER);
        // The invite already reserved its shares, so there is no capacity check here.
        if (shares.Count != invite.ShareCount)
            throw new DomainException(ErrorCodes.INVALID_PREFERENCES);
        ValidatePreferences(shares);

        var membership = Membership.Create(
            Id, userId, [.. shares.Select(s => s.Preferences)], TermsVersion, now);
        _memberships.Add(membership);
        invite.Accept(userId);

        // A still-pending request from this user must not later be approvable into a second membership.
        foreach (var request in _joinRequests.Where(r => r.UserId == userId && r.Status == JoinRequestStatus.Pending))
        {
            request.Withdraw();
            Raise(new JoinRequestWithdrawn(Id, request.Id));
        }

        Raise(new InviteAccepted(Id, invite.Id, userId));
        return membership;
    }

    public void DeclineInvite(Guid inviteId, Guid userId, PhoneNumber phone)
    {
        var invite = FindPendingInvite(inviteId, phone);
        RequireRecruiting();

        invite.Decline(userId);
        Raise(new InviteDeclined(Id, invite.Id, userId));
    }

    public void UpdateSettings(GameyaSettings settings, int organizerShares, DateOnly today, DateTimeOffset now)
    {
        if (Status is not (GameyaStatus.Draft or GameyaStatus.Recruiting))
            throw new DomainException(ErrorCodes.INVALID_STATUS_TRANSITION);

        var clean = Validate(settings, organizerShares, today, checkStartDate: settings.StartDate != StartDate);

        var organizer = _memberships.FirstOrDefault(m => m.UserId == OrganizerId && m.Status == MembershipStatus.Active);
        var oldOrganizerShares = organizer?.Shares.Count ?? 0;
        var heldByOthers = AssignedShares - oldOrganizerShares + ReservedShares;
        if (clean.ShareCount < heldByOthers + Math.Min(oldOrganizerShares, organizerShares))
            throw new DomainException(ErrorCodes.SHARES_BELOW_ASSIGNED);
        if (heldByOthers + organizerShares > clean.ShareCount)
            throw new DomainException(ErrorCodes.NOT_ENOUGH_SHARES);

        var changed = SettingsDiff.Build(Snapshot(), oldOrganizerShares, clean, organizerShares);
        if (changed.Count == 0)
            return;

        var bump = Status == GameyaStatus.Recruiting
            && (changed.ContainsKey("contributionAmount") || changed.ContainsKey("shareCount") || changed.ContainsKey("frequency"));
        Apply(clean);
        if (bump)
            TermsVersion++;

        TrimPreferences();
        ReconcileOrganizer(organizer, organizerShares, now);

        Raise(new GameyaSettingsChanged(Id, changed, bump ? TermsVersion : null));
    }

    public void ReacceptTerms(Guid userId)
    {
        // Ownership before status, as in WithdrawJoinRequest.
        var membership = _memberships.FirstOrDefault(m => m.UserId == userId && m.Status == MembershipStatus.Active);
        var request = _joinRequests.FirstOrDefault(r => r.UserId == userId && r.Status == JoinRequestStatus.Pending);
        if (membership is null && request is null)
            throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);
        RequireRecruiting();

        if (membership is not null)
        {
            membership.AcceptTerms(TermsVersion);
            Raise(new TermsReaccepted(Id, "Membership", membership.Id, userId, TermsVersion));
        }

        if (request is not null)
        {
            request.AcceptTerms(TermsVersion);
            Raise(new TermsReaccepted(Id, "JoinRequest", request.Id, userId, TermsVersion));
        }
    }

    public bool MembershipNeedsReaccept(Guid userId) =>
        _memberships.Any(m => m.UserId == userId && m.Status == MembershipStatus.Active
            && m.AcceptedTermsVersion < TermsVersion);

    public void Leave(Guid userId)
    {
        RequireRecruiting();
        if (userId == OrganizerId)
            throw new DomainException(ErrorCodes.ORGANIZER_CANNOT_LEAVE);
        var membership = FindActiveMembership(userId);

        membership.Leave();
        Raise(new MemberLeft(Id, membership.Id, userId));
    }

    public void RemoveMember(Guid userId, string reason)
    {
        RequireRecruiting();
        if (userId == OrganizerId)
            throw new DomainException(ErrorCodes.ORGANIZER_CANNOT_LEAVE);
        var clean = (reason ?? string.Empty).Trim();
        if (clean.Length is < 5 or > MaxReasonLength)
            throw Invalid("reason");
        var membership = FindActiveMembership(userId);

        membership.Remove(clean);
        Raise(new MemberRemoved(Id, membership.Id, userId, clean));
    }

    public void Cancel(string? reason, DateTimeOffset now)
    {
        if (Status is not (GameyaStatus.Draft or GameyaStatus.Recruiting))
            throw new DomainException(ErrorCodes.INVALID_STATUS_TRANSITION);
        if (reason is { Length: > MaxReasonLength })
            throw Invalid("reason");

        foreach (var request in _joinRequests.Where(r => r.Status == JoinRequestStatus.Pending))
            request.Reject(null);
        foreach (var invite in _pendingInvites.Where(i => i.Status == InviteStatus.Pending))
            invite.Revoke();

        // InviteCode is kept on purpose so the cancelled preview stays reachable.
        Status = GameyaStatus.Cancelled;
        CancelReason = reason;
        CancelledAt = now;
        Raise(new GameyaCancelled(Id, reason));
    }

    private Membership FindActiveMembership(Guid userId) =>
        _memberships.FirstOrDefault(m => m.UserId == userId && m.Status == MembershipStatus.Active)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

    private GameyaSettings Snapshot() => new(
        Name, Description, ContributionAmount, ShareCount, Frequency, StartDate, GraceDays, PayoutTiming,
        PayoutOffsetDays, OrderMethod, MaxSharesPerMember, OrganizerParticipates, RulesText);

    // A shrunken round count or a non-ByRequest order method invalidates previously stored preferences.
    private void TrimPreferences()
    {
        // Returns null when nothing needs to change, so untouched shares are not marked modified.
        IReadOnlyList<int>? Trimmed(IReadOnlyList<int> prefs)
        {
            var kept = OrderMethod == OrderMethod.ByRequest ? prefs.Where(p => p <= RoundCount).ToList() : [];
            return kept.Count == prefs.Count ? null : kept;
        }

        foreach (var share in _memberships.Where(m => m.Status == MembershipStatus.Active).SelectMany(m => m.Shares))
            if (Trimmed(share.Preferences) is { } kept)
                share.SetPreferences(kept);
        foreach (var share in _joinRequests.Where(r => r.Status == JoinRequestStatus.Pending).SelectMany(r => r.Shares))
            if (Trimmed(share.Preferences) is { } kept)
                share.SetPreferences(kept);
    }

    private void ReconcileOrganizer(Membership? organizer, int organizerShares, DateTimeOffset now)
    {
        if (organizerShares == 0)
        {
            organizer?.Leave();
            return;
        }

        if (organizer is null)
        {
            _memberships.Add(Membership.Create(Id, OrganizerId, organizerShares, TermsVersion, now));
            return;
        }

        if (organizerShares > organizer.Shares.Count)
            organizer.AddShares(organizerShares - organizer.Shares.Count);
        else if (organizerShares < organizer.Shares.Count)
            organizer.RemoveLastShares(organizer.Shares.Count - organizerShares);
        organizer.AcceptTerms(TermsVersion);
    }

    private void RequireRecruiting()
    {
        if (Status != GameyaStatus.Recruiting)
            throw new DomainException(ErrorCodes.INVALID_STATUS_TRANSITION);
    }

    private bool IsActiveMember(Guid userId) =>
        userId == OrganizerId || _memberships.Any(m => m.UserId == userId && m.Status == MembershipStatus.Active);

    private JoinRequest FindPendingRequest(Guid requestId) =>
        _joinRequests.FirstOrDefault(r => r.Id == requestId && r.Status == JoinRequestStatus.Pending)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

    private PendingInvite FindPendingInvite(Guid inviteId, PhoneNumber? phone = null) =>
        _pendingInvites.FirstOrDefault(i =>
            i.Id == inviteId && i.Status == InviteStatus.Pending && (phone is null || i.Phone == phone))
            ?? throw new DomainException(ErrorCodes.INVITE_NOT_FOUND);

    private void RequireCapacity(int shareCount)
    {
        if (shareCount < 1)
            throw Invalid("shareCount");
        if (shareCount > MaxSharesPerMember)
            throw new DomainException(ErrorCodes.MAX_SHARES_EXCEEDED);
        if (AvailableShares == 0)
            throw new DomainException(ErrorCodes.GAMEYA_FULL);
        if (shareCount > AvailableShares)
            throw new DomainException(
                ErrorCodes.NOT_ENOUGH_SHARES, new Dictionary<string, object?> { ["available"] = AvailableShares });
    }

    private void ValidatePreferences(IReadOnlyList<ShareRequest> shares)
    {
        foreach (var prefs in shares.Select(s => s.Preferences).Where(p => p.Count > 0))
        {
            if (OrderMethod != OrderMethod.ByRequest
                || prefs.Count > MaxPreferencesPerShare
                || prefs.Distinct().Count() != prefs.Count
                || prefs.Any(p => p < 1 || p > RoundCount))
                throw new DomainException(ErrorCodes.INVALID_PREFERENCES);
        }
    }

    private void Apply(GameyaSettings s)
    {
        Name = s.Name;
        Description = s.Description;
        ContributionAmount = s.ContributionAmount;
        ShareCount = s.ShareCount;
        Frequency = s.Frequency;
        StartDate = s.StartDate;
        GraceDays = s.GraceDays;
        PayoutTiming = s.PayoutTiming;
        PayoutOffsetDays = s.PayoutOffsetDays;
        OrderMethod = s.OrderMethod;
        MaxSharesPerMember = s.MaxSharesPerMember;
        OrganizerParticipates = s.OrganizerParticipates;
        RulesText = s.RulesText;
    }

    /// <summary>Returns the settings with the name trimmed; throws on the first broken rule.</summary>
    private static GameyaSettings Validate(
        GameyaSettings s, int organizerShares, DateOnly today, bool checkStartDate)
    {
        var name = (s.Name ?? string.Empty).Trim();
        if (name.Length is < 3 or > 60)
            throw Invalid(nameof(GameyaSettings.Name));
        if (s.Description is { Length: > 500 })
            throw Invalid(nameof(GameyaSettings.Description));
        if (s.ContributionAmount is < 100m or > 1_000_000m || s.ContributionAmount != decimal.Truncate(s.ContributionAmount))
            throw Invalid(nameof(GameyaSettings.ContributionAmount));
        if (s.ShareCount is < 2 or > 50)
            throw Invalid(nameof(GameyaSettings.ShareCount));
        if (s.GraceDays is < 0 or > 10)
            throw Invalid(nameof(GameyaSettings.GraceDays));
        if (s.PayoutTiming == PayoutTiming.FixedDate
                ? s.PayoutOffsetDays is null or < 0 or > 10
                : s.PayoutOffsetDays is not null)
            throw Invalid(nameof(GameyaSettings.PayoutOffsetDays));
        if (s.MaxSharesPerMember is < 1 or > 5)
            throw Invalid(nameof(GameyaSettings.MaxSharesPerMember));
        if (s.RulesText is { Length: > 2000 })
            throw Invalid(nameof(GameyaSettings.RulesText));

        if (checkStartDate && s.StartDate < today)
            throw new DomainException(ErrorCodes.START_DATE_IN_PAST);

        if (s.OrganizerParticipates ? organizerShares < 1 : organizerShares != 0)
            throw Invalid("OrganizerShares");
        if (organizerShares > s.MaxSharesPerMember)
            throw new DomainException(ErrorCodes.MAX_SHARES_EXCEEDED);
        if (organizerShares > s.ShareCount)
            throw new DomainException(ErrorCodes.NOT_ENOUGH_SHARES);

        return s with { Name = name };
    }

    private static DomainException Invalid(string field) =>
        new(ErrorCodes.VALIDATION_FAILED, new Dictionary<string, object?> { ["field"] = field });
}
