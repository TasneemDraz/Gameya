using Ardalis.Specification;
using Gameya.Domain.Users;

namespace Gameya.Application.Users.Specifications;

/// <summary>Another non-deleted account that already owns this (verified) email.</summary>
public sealed class OtherUserWithVerifiedEmailSpec : Specification<User>
{
    public OtherUserWithVerifiedEmailSpec(string email, Guid exceptUserId) =>
        Query.Where(u => u.DeletedAt == null && u.Id != exceptUserId && u.Email == email);
}
