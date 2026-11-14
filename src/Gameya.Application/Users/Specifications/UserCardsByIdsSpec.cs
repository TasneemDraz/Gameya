using Ardalis.Specification;
using Gameya.Domain.Users;

namespace Gameya.Application.Users.Specifications;

/// <summary>
/// Name, phone and photo key for the given users, deleted ones included (their personal fields are already null).
/// Handlers turn the phone into a <c>DisplayPhone</c> and the key into a signed URL.
/// </summary>
public sealed record UserCardRow(Guid Id, string? FullName, PhoneNumber? Phone, string? PhotoKey);

public sealed class UserCardsByIdsSpec : Specification<User, UserCardRow>
{
    public UserCardsByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        var wanted = ids.Distinct().ToArray();
        Query
            .Where(u => wanted.Contains(u.Id))
            .Select(u => new UserCardRow(u.Id, u.FullName, u.Phone, u.PhotoKey));
    }
}
