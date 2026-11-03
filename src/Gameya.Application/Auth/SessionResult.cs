namespace Gameya.Application.Auth;

/// <summary>RefreshToken never goes in a response body; the endpoint moves it to the cookie.</summary>
public sealed record SessionResult(
    string AccessToken, DateTimeOffset AccessTokenExpiresAt, bool ProfileComplete, string RefreshToken);
