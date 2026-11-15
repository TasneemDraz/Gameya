using Ardalis.Specification;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;

namespace Gameya.Application.Gameyas.Specifications;

/// <summary>
/// The id of the viewer's own Pending invite to a gam'eya: by user id, or by phone for one sent before they signed
/// up. A filtered unique index allows at most one pending invite per phone and gam'eya.
/// </summary>
public sealed class ViewerPendingInviteSpec : Specification<PendingInvite, Guid>
{
    public ViewerPendingInviteSpec(Guid gameyaId, Guid viewerId, PhoneNumber phone) =>
        Query
            .Where(i => i.GameyaId == gameyaId && i.Status == InviteStatus.Pending
                && (i.UserId == viewerId || i.Phone == phone))
            .OrderBy(i => i.CreatedAt)
            .Select(i => i.Id);
}
