using System.Text.Json;
using Gameya.Application.Security;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas;

/// <summary>
/// The public invite page. It never carries member names, phones or user ids; <see cref="Relationship"/> is null for
/// an anonymous visitor. <see cref="InviteId"/> is the viewer's own pending direct invite, set only when
/// <see cref="Relationship"/> is "Invited".
/// </summary>
public sealed record InvitePreviewDto(
    Guid GameyaId,
    string State,
    string Name,
    string? Description,
    decimal ContributionAmount,
    string Currency,
    int ShareCount,
    GameyaFrequency Frequency,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Pot,
    int GraceDays,
    PayoutTiming PayoutTiming,
    int? PayoutOffsetDays,
    OrderMethod OrderMethod,
    int MaxSharesPerMember,
    int AvailableShares,
    string? RulesText,
    string? OrganizerName,
    string? Relationship,
    bool RequestNeedsReaccept,
    Guid? InviteId);

public sealed record MyGameyaDto(
    Guid Id,
    string Name,
    GameyaStatus Status,
    GameyaRole Role,
    decimal ContributionAmount,
    int ShareCount,
    int MyShareCount,
    bool NeedsReaccept);

public sealed record MyInviteDto(
    Guid InviteId,
    Guid GameyaId,
    string Name,
    decimal ContributionAmount,
    int ShareCount,
    int RoundCount,
    int InvitedShares,
    OrderMethod OrderMethod,
    string? OrganizerName);

/// <summary>The terms a member accepted, for each money field that has changed since; null fields are unchanged.</summary>
public sealed record TermsDiffDto(decimal? OldContributionAmount, int? OldShareCount, GameyaFrequency? OldFrequency);

/// <summary>Details for a member or the organizer. <see cref="InviteCode"/> and the request count are organizer-only.</summary>
public sealed record GameyaDto(
    Guid Id,
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
    string? RulesText,
    GameyaStatus Status,
    int TermsVersion,
    string Currency,
    decimal Pot,
    int RoundCount,
    DateOnly EndDate,
    decimal TotalPerShare,
    int AssignedShares,
    int ReservedShares,
    int AvailableShares,
    GameyaRole MyRole,
    int MyShareCount,
    bool NeedsReaccept,
    string? InviteCode,
    int? PendingJoinRequestCount,
    TermsDiffDto? TermsDiff);

/// <summary>A member row. The phone is full for the organizer and for the member's own row, masked otherwise.</summary>
public sealed record MemberDto(
    Guid UserId,
    string? FullName,
    Uri? PhotoUrl,
    DisplayPhone? Phone,
    int ShareCount,
    bool IsOrganizer,
    bool NeedsReaccept);

/// <summary>One activity entry; <see cref="ActorName"/> is null for a deleted user (or the system).</summary>
public sealed record ActivityEntryDto(
    DateTimeOffset OccurredAt, string? ActorName, string Action, JsonElement Details, string? Note);

public sealed record ActivityPageDto(IReadOnlyList<ActivityEntryDto> Items, int Page, bool HasMore);

public sealed record JoinRequestDto(
    Guid Id,
    Guid UserId,
    string? FullName,
    DisplayPhone? Phone,
    IReadOnlyList<IReadOnlyList<int>> SharePreferences,
    bool TermsCurrent);

/// <summary>
/// A pending direct invite: only the phone the organizer typed. Whether it belongs to an account, and whose, is not
/// shown until the invite is accepted.
/// </summary>
public sealed record PendingInviteDto(Guid Id, DisplayPhone Phone, int ShareCount);

/// <summary>The caller's own Pending request to join, for the home page.</summary>
public sealed record MyJoinRequestDto(
    Guid GameyaId,
    string Name,
    JoinRequestStatus Status,
    decimal ContributionAmount,
    int GameyaShareCount,
    int ShareCount,
    bool NeedsReaccept);
