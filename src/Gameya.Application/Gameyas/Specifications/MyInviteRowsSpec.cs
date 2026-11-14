using Ardalis.Specification;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;

namespace Gameya.Application.Gameyas.Specifications;

public sealed record MyInviteRow(
    Guid InviteId,
    Guid GameyaId,
    string Name,
    decimal ContributionAmount,
    int ShareCount,
    int InvitedShares,
    OrderMethod OrderMethod,
    Guid OrganizerId);

/// <summary>
/// Pending invites to the phone in Recruiting gam'eyas. A filtered unique index allows at most one pending invite per
/// phone and gam'eya, so each gam'eya yields one row.
/// </summary>
public sealed class MyInviteRowsSpec : Specification<GameyaCircle, MyInviteRow>
{
    public MyInviteRowsSpec(PhoneNumber phone) =>
        Query
            .Where(g => g.Status == GameyaStatus.Recruiting
                && g.PendingInvites.Any(i => i.Status == InviteStatus.Pending && i.Phone == phone))
            .OrderBy(g => g.Name)
            .Select(g => new MyInviteRow(
                g.PendingInvites
                    .Where(i => i.Status == InviteStatus.Pending && i.Phone == phone)
                    .Select(i => i.Id)
                    .First(),
                g.Id,
                g.Name,
                g.ContributionAmount,
                g.ShareCount,
                g.PendingInvites
                    .Where(i => i.Status == InviteStatus.Pending && i.Phone == phone)
                    .Select(i => i.ShareCount)
                    .First(),
                g.OrderMethod,
                g.OrganizerId));
}
