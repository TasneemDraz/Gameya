using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

public sealed record GameyaRoleRow(Guid OrganizerId, bool IsActiveMember);

/// <summary>The gam'eya's organizer and whether the user holds an Active membership in it.</summary>
public sealed class GameyaRoleSpec : Specification<GameyaCircle, GameyaRoleRow>
{
    public GameyaRoleSpec(Guid gameyaId, Guid userId) =>
        Query
            .Where(g => g.Id == gameyaId)
            .Select(g => new GameyaRoleRow(
                g.OrganizerId,
                g.Memberships.Any(m => m.UserId == userId && m.Status == MembershipStatus.Active)));
}
