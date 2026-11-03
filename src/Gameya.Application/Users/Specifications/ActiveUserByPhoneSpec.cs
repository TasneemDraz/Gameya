using Ardalis.Specification;
using Gameya.Domain.Users;

namespace Gameya.Application.Users.Specifications;

public sealed class ActiveUserByPhoneSpec : Specification<User>
{
    public ActiveUserByPhoneSpec(PhoneNumber phone) =>
        Query.Where(u => u.DeletedAt == null && u.Phone == phone);
}
