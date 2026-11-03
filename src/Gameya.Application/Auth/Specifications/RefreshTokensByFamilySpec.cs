using Ardalis.Specification;
using Gameya.Domain.Auth;

namespace Gameya.Application.Auth.Specifications;

public sealed class RefreshTokensByFamilySpec : Specification<RefreshToken>
{
    public RefreshTokensByFamilySpec(Guid familyId) => Query.Where(t => t.FamilyId == familyId);
}
