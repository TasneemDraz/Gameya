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
        Query.IncludeLiveChildren();
    }
}

internal static class GameyaAggregateIncludes
{
    /// <summary>The live children every aggregate load needs, so the domain sees capacity and duplicates.</summary>
    public static void IncludeLiveChildren(this ISpecificationBuilder<GameyaCircle> query)
    {
        query.Include(g => g.Memberships.Where(m => m.Status == MembershipStatus.Active))
            .ThenInclude(m => m.Shares);
        query.Include(g => g.JoinRequests.Where(r => r.Status == JoinRequestStatus.Pending))
            .ThenInclude(r => r.Shares);
        query.Include(g => g.PendingInvites.Where(i => i.Status == InviteStatus.Pending));
        query.AsSplitQuery();
    }
}
