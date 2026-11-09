namespace Gameya.Api.Infrastructure;

/// <summary>The single definition of the refresh-token cookie: HttpOnly; Secure; SameSite=Strict; Path=/api/auth.</summary>
public static class RefreshCookie
{
    public const string Name = "gameya_rt";
    private const string CookiePath = "/api/auth";

    public static void Write(HttpResponse response, string token, DateTimeOffset expires) =>
        response.Cookies.Append(Name, token, Options(expires));

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(null));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        Expires = expires,
        IsEssential = true
    };
}
