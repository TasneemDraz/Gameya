using Ardalis.Specification;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;

namespace Gameya.Application.Gameyas.Specifications;

/// <summary>
/// The tracked aggregates (live children only) holding a Pending join request from the user, or a Pending invite
/// meant for them: by user id, or by phone for one sent before they signed up.
/// </summary>
public sealed class GameyaAggregatesWaitingOnUserSpec : Specification<GameyaCircle>
{
    public GameyaAggregatesWaitingOnUserSpec(Guid userId, PhoneNumber? phone)
    {
        if (phone is null)
        {
            Query.Where(g =>
                g.JoinRequests.Any(r => r.UserId == userId && r.Status == JoinRequestStatus.Pending)
                || g.PendingInvites.Any(i => i.Status == InviteStatus.Pending && i.UserId == userId));
        }
        else
        {
            Query.Where(g =>
                g.JoinRequests.Any(r => r.UserId == userId && r.Status == JoinRequestStatus.Pending)
                || g.PendingInvites.Any(i => i.Status == InviteStatus.Pending
                    && (i.UserId == userId || (i.UserId == null && i.Phone == phone))));
        }

        Query.IncludeLiveChildren();
    }
}
