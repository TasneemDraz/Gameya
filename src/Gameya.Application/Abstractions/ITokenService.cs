using Gameya.Domain.Users;

namespace Gameya.Application.Abstractions;

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user);

    /// <summary>32 random bytes, base64url.</summary>
    string NewOpaqueToken();
}
