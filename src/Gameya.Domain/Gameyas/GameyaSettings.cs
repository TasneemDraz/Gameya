namespace Gameya.Domain.Gameyas;

public sealed record GameyaSettings(
    string Name,
    string? Description,
    decimal ContributionAmount,
    int ShareCount,
    GameyaFrequency Frequency,
    DateOnly StartDate,
    int GraceDays,
    PayoutTiming PayoutTiming,
    int? PayoutOffsetDays,
    OrderMethod OrderMethod,
    int MaxSharesPerMember,
    bool OrganizerParticipates,
    string? RulesText);

public sealed record ShareRequest(IReadOnlyList<int> Preferences);
