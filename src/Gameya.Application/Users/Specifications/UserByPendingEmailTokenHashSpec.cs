using Ardalis.Specification;
using Gameya.Domain.Users;

namespace Gameya.Application.Users.Specifications;

public sealed class UserByPendingEmailTokenHashSpec : Specification<User>
{
    public UserByPendingEmailTokenHashSpec(string tokenHash) =>
        Query.Where(u => u.DeletedAt == null && u.PendingEmailTokenHash == tokenHash);
}
