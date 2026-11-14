using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

/// <summary>
/// Gam'eyas the user still takes part in: organizing one that is Draft, Recruiting or Active, or holding an
/// Active membership in one that is Recruiting or Active.
/// </summary>
public sealed class ParticipationSpec : Specification<GameyaCircle>
{
    public ParticipationSpec(Guid userId) =>
        Query.Where(g =>
            (g.OrganizerId == userId
                && (g.Status == GameyaStatus.Draft || g.Status == GameyaStatus.Recruiting || g.Status == GameyaStatus.Active))
            || ((g.Status == GameyaStatus.Recruiting || g.Status == GameyaStatus.Active)
                && g.Memberships.Any(m => m.UserId == userId && m.Status == MembershipStatus.Active)));
}
