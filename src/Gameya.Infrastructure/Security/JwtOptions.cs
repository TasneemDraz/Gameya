namespace Gameya.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(10);

    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string SigningKey { get; init; } = "";
}
