using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

/// <summary>
/// The tracked aggregate (live children only) that owns the given invite, whatever the invite's status;
/// the domain then refuses an invite that is no longer Pending.
/// </summary>
public sealed class GameyaAggregateByInviteIdSpec : Specification<GameyaCircle>
{
    public GameyaAggregateByInviteIdSpec(Guid inviteId)
    {
        Query.Where(g => g.PendingInvites.Any(i => i.Id == inviteId));
        Query.IncludeLiveChildren();
    }
}
