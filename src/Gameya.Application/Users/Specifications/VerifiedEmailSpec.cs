using Ardalis.Specification;
using Gameya.Domain.Users;

namespace Gameya.Application.Users.Specifications;

/// <summary>The user's email, only when it is verified and the account is not deleted.</summary>
public sealed class VerifiedEmailSpec : Specification<User, string?>
{
    public VerifiedEmailSpec(Guid userId) =>
        Query
            .Where(u => u.Id == userId && u.DeletedAt == null && u.EmailVerifiedAt != null)
            .Select(u => u.Email);
}
