using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

public sealed record GameyaDetailRow(
    Guid Id,
    Guid OrganizerId,
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
    string? InviteCode,
    int AssignedShares,
    int ReservedShares,
    int PendingJoinRequestCount,
    int MyShareCount,
    int? MyAcceptedTermsVersion);

/// <summary>Settings, share totals and the viewer's own membership; the handler hides organizer-only fields.</summary>
public sealed class GameyaDetailRowSpec : Specification<GameyaCircle, GameyaDetailRow>
{
    public GameyaDetailRowSpec(Guid gameyaId, Guid viewerId) =>
        Query
            .Where(g => g.Id == gameyaId)
            .Select(g => new GameyaDetailRow(
                g.Id,
                g.OrganizerId,
                g.Name,
                g.Description,
                g.ContributionAmount,
                g.ShareCount,
                g.Frequency,
                g.StartDate,
                g.GraceDays,
                g.PayoutTiming,
                g.PayoutOffsetDays,
                g.OrderMethod,
                g.MaxSharesPerMember,
                g.OrganizerParticipates,
                g.RulesText,
                g.Status,
                g.TermsVersion,
                g.Currency,
                g.InviteCode,
                g.Memberships.Where(m => m.Status == MembershipStatus.Active).SelectMany(m => m.Shares).Count(),
                g.PendingInvites.Where(i => i.Status == InviteStatus.Pending).Sum(i => i.ShareCount),
                g.JoinRequests.Count(r => r.Status == JoinRequestStatus.Pending),
                g.Memberships
                    .Where(m => m.UserId == viewerId && m.Status == MembershipStatus.Active)
                    .SelectMany(m => m.Shares)
                    .Count(),
                g.Memberships
                    .Where(m => m.UserId == viewerId && m.Status == MembershipStatus.Active)
                    .Select(m => (int?)m.AcceptedTermsVersion)
                    .FirstOrDefault()));
}
