using Ardalis.Specification;
using Gameya.Domain.Users;

namespace Gameya.Application.Users.Specifications;

public sealed class UserByIdSpec : Specification<User>
{
    public UserByIdSpec(Guid id) => Query.Where(u => u.Id == id && u.DeletedAt == null);
}
