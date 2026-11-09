namespace Gameya.Domain.Auth;

public static class OtpRules
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SendWindow = TimeSpan.FromMinutes(15);
    public const int MaxSendsPerWindow = 4;
}
