using Ardalis.Specification;
using Gameya.Domain.Auth;

namespace Gameya.Application.Auth.Specifications;

public sealed class ActiveRefreshTokensByUserSpec : Specification<RefreshToken>
{
    public ActiveRefreshTokensByUserSpec(Guid userId) =>
        Query.Where(t => t.UserId == userId && t.RevokedAt == null);
}
