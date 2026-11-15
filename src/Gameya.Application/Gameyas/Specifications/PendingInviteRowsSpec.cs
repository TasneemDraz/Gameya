using Ardalis.Specification;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;

namespace Gameya.Application.Gameyas.Specifications;

/// <summary>No user id: the organizer's list must not reveal whether a phone has an account.</summary>
public sealed record PendingInviteRow(Guid Id, PhoneNumber Phone, int ShareCount);

/// <summary>Pending direct invites of a gam'eya, oldest first.</summary>
public sealed class PendingInviteRowsSpec : Specification<PendingInvite, PendingInviteRow>
{
    public PendingInviteRowsSpec(Guid gameyaId) =>
        Query
            .Where(i => i.GameyaId == gameyaId && i.Status == InviteStatus.Pending)
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .Select(i => new PendingInviteRow(i.Id, i.Phone, i.ShareCount));
}
