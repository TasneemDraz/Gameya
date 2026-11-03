using System.Security.Cryptography;
using System.Text;
using Gameya.Application.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Gameya.Infrastructure.Files;

/// <summary>
/// Builds and verifies <c>/api/files/{container}/{key}?exp=…&amp;sig=…</c> URLs. The signature is
/// base64url(HMAC-SHA256(key, "{container}/{key}:{exp}")) and is compared in constant time.
/// </summary>
public sealed class SignedFileUrls(string signingKey, IClock clock)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(signingKey);

    public Uri GetReadUrl(string container, string key, TimeSpan lifetime)
    {
        var exp = (clock.UtcNow + lifetime).ToUnixTimeSeconds();
        return new Uri($"/api/files/{container}/{key}?exp={exp}&sig={Sign(container, key, exp)}", UriKind.Relative);
    }

    /// <summary>True when the link has not expired and the signature matches.</summary>
    public bool IsValid(string container, string key, long exp, string signature)
    {
        if (string.IsNullOrEmpty(signature) || clock.UtcNow.ToUnixTimeSeconds() > exp)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Sign(container, key, exp)), Encoding.UTF8.GetBytes(signature));
    }

    private string Sign(string container, string key, long exp) =>
        Base64UrlEncoder.Encode(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{container}/{key}:{exp}")));
}
