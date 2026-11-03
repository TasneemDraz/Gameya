namespace Gameya.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Sender mailbox, for example <c>Gam'eya &lt;noreply@example.com&gt;</c>.</summary>
    public string From { get; init; } = "";

    public SmtpOptions Smtp { get; init; } = new();
}

public sealed class SmtpOptions
{
    public const int TimeoutMilliseconds = 10_000;

    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool UseStartTls { get; init; }
}
