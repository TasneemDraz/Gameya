using Ardalis.Specification;
using Gameya.Domain.Auth;

namespace Gameya.Application.Auth.Specifications;

public sealed class RefreshTokenByHashSpec : Specification<RefreshToken>
{
    public RefreshTokenByHashSpec(string tokenHash) => Query.Where(t => t.TokenHash == tokenHash);
}
