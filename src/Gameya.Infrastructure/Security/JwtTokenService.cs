using System.Security.Cryptography;
using System.Text;
using Gameya.Application.Abstractions;
using Gameya.Domain.Users;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gameya.Infrastructure.Security;

public sealed class JwtTokenService(JwtOptions options, IClock clock) : ITokenService
{
    private readonly SigningCredentials _credentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)), SecurityAlgorithms.HmacSha256);

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user)
    {
        var now = clock.UtcNow;
        var expires = now + JwtOptions.AccessTokenLifetime;

        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id.ToString(),
            ["lang"] = user.Language.ToString().ToLowerInvariant()
        };
        if (user.Roles.Count > 0) claims["role"] = user.Roles.ToArray();

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = _credentials
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public string NewOpaqueToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
}
