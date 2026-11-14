using Gameya.Domain.Common;

namespace Gameya.Domain.Gameyas;

// Named GameyaCircle because a type called Gameya would clash with the root namespace.
public class GameyaCircle : AuditableEntity
{
    private readonly List<Membership> _memberships = [];

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

    public decimal Pot => ContributionAmount * ShareCount;
    public int RoundCount => ShareCount;
    public decimal TotalPerShare => ContributionAmount * ShareCount;

    public DateOnly EndDate => Frequency == GameyaFrequency.Monthly
        ? CairoCalendar.MonthlyDueDate(StartDate, ShareCount - 1)
        : StartDate.AddDays(7 * (ShareCount - 1));

    public int AssignedShares => _memberships
        .Where(m => m.Status == MembershipStatus.Active)
        .Sum(m => m.Shares.Count);

    // Task 2 adds invites and join requests, which hold shares without a membership yet.
    public int ReservedShares => 0;

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
