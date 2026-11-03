namespace Gameya.Infrastructure.Sms;

public sealed class SmsGateOptions
{
    public const string Section = "Sms:SmsGate";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public string BaseUrl { get; init; } = "https://api.sms-gate.app/3rdparty/v1";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
}
