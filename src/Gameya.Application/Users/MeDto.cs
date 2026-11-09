using Gameya.Application.Security;

namespace Gameya.Application.Users;

public sealed record MeDto(
    Guid Id,
    string? FullName,
    [property: OwnPhone] string Phone,
    string? Email,
    bool EmailVerified,
    string? PendingEmail,
    Uri? PhotoUrl,
    string Language,
    bool ProfileComplete);
