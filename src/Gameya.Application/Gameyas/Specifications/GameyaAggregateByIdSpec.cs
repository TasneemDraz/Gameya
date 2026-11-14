using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

/// <summary>
/// The tracked aggregate with only its live children: Active memberships (with shares), Pending join requests
/// (with shares) and Pending invites. Commands load through this before calling a domain method.
/// </summary>
public sealed class GameyaAggregateByIdSpec : Specification<GameyaCircle>
{
    public GameyaAggregateByIdSpec(Guid id)
    {
        Query.Where(g => g.Id == id);
        Query.Include(g => g.Memberships.Where(m => m.Status == MembershipStatus.Active))
            .ThenInclude(m => m.Shares);
        Query.Include(g => g.JoinRequests.Where(r => r.Status == JoinRequestStatus.Pending))
            .ThenInclude(r => r.Shares);
        Query.Include(g => g.PendingInvites.Where(i => i.Status == InviteStatus.Pending));
        Query.AsSplitQuery();
    }
}
