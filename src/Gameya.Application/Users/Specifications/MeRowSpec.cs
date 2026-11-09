using Ardalis.Specification;
using Gameya.Domain.Users;

namespace Gameya.Application.Users.Specifications;

/// <summary>Raw projection of the caller's profile. The handler adds what SQL cannot compute (signed photo URL).</summary>
public sealed record MeRow(
    Guid Id,
    string? FullName,
    string PhoneE164,
    string? Email,
    DateTimeOffset? EmailVerifiedAt,
    string? PendingEmail,
    string? PhotoKey,
    Language Language,
    bool ProfileComplete);

public sealed class MeRowSpec : Specification<User, MeRow>
{
    public MeRowSpec(Guid userId) =>
        Query
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => new MeRow(
                u.Id, u.FullName, u.Phone!.E164, u.Email, u.EmailVerifiedAt, u.PendingEmail, u.PhotoKey,
                u.Language, u.ProfileComplete));
}
