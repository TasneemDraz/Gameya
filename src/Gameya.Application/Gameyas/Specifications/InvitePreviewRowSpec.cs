using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

/// <summary>
/// What the public invite page may show, plus the viewer's own standing. It holds no member data: only share totals
/// and whether the viewer is a member or has a pending request.
/// </summary>
public sealed record InvitePreviewRow(
    Guid Id,
    Guid OrganizerId,
    GameyaStatus Status,
    int TermsVersion,
    string Name,
    string? Description,
    decimal ContributionAmount,
    string Currency,
    int ShareCount,
    GameyaFrequency Frequency,
    DateOnly StartDate,
    int GraceDays,
    PayoutTiming PayoutTiming,
    int? PayoutOffsetDays,
    OrderMethod OrderMethod,
    int MaxSharesPerMember,
    string? RulesText,
    int AssignedShares,
    int ReservedShares,
    bool ViewerIsMember,
    int? ViewerRequestTermsVersion);

public sealed class InvitePreviewRowSpec : Specification<GameyaCircle, InvitePreviewRow>
{
    /// <param name="code">The invite code, already normalized.</param>
    /// <param name="viewerId">The caller, or <see cref="Guid.Empty"/> for an anonymous visitor.</param>
    public InvitePreviewRowSpec(string code, Guid viewerId) =>
        Query
            .Where(g => g.InviteCode == code)
            .Select(g => new InvitePreviewRow(
                g.Id,
                g.OrganizerId,
                g.Status,
                g.TermsVersion,
                g.Name,
                g.Description,
                g.ContributionAmount,
                g.Currency,
                g.ShareCount,
                g.Frequency,
                g.StartDate,
                g.GraceDays,
                g.PayoutTiming,
                g.PayoutOffsetDays,
                g.OrderMethod,
                g.MaxSharesPerMember,
                g.RulesText,
                g.Memberships.Where(m => m.Status == MembershipStatus.Active).SelectMany(m => m.Shares).Count(),
                g.PendingInvites.Where(i => i.Status == InviteStatus.Pending).Sum(i => i.ShareCount),
                g.Memberships.Any(m => m.UserId == viewerId && m.Status == MembershipStatus.Active),
                g.JoinRequests
                    .Where(r => r.UserId == viewerId && r.Status == JoinRequestStatus.Pending)
                    .Select(r => (int?)r.AcceptedTermsVersion)
                    .FirstOrDefault()));
}
