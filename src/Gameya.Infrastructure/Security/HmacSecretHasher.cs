using System.Security.Cryptography;
using System.Text;
using Gameya.Application.Abstractions;

namespace Gameya.Infrastructure.Security;

/// <summary>HMAC-SHA256 keyed by <c>Security:HashKey</c>, base64.</summary>
public sealed class HmacSecretHasher(string key) : ISecretHasher
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(key);

    public string Hash(string value) =>
        Convert.ToBase64String(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value)));
}
