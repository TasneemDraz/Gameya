using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gameya.Api.IntegrationTests.Ops;

/// <summary>
/// Misconfiguration must stop a Production host from starting. The guards fire while the service graph is built,
/// before anything connects to a database, so these tests need no containers.
/// </summary>
public sealed class StartupGuardTests
{
    private static Dictionary<string, string?> ValidProductionSettings() => new()
    {
        ["ConnectionStrings:App"] = "Host=127.0.0.1;Port=1;Database=gameya;Username=gameya_app;Password=x",
        ["Security:HashKey"] = "test-hash-key-0123456789abcdef0123456789abcdef",
        ["Jwt:Issuer"] = "gameya-tests",
        ["Jwt:Audience"] = "gameya-tests",
        ["Jwt:SigningKey"] = "test-signing-key-0123456789abcdef0123456789abcdef",
        ["App:WebBaseUrl"] = "https://app.gameya.test",
        ["Email:From"] = "Gam'eya <noreply@gameya.test>",
        ["Email:Smtp:Host"] = "smtp.gameya.test",
        ["Email:Smtp:Port"] = "587",
        ["Email:Smtp:UseStartTls"] = "true",
        ["Storage:RootPath"] = Path.Combine(Path.GetTempPath(), "gameya-guard-tests"),
        ["Storage:UrlSigningKey"] = GameyaApiFactory.UrlSigningKey,
        ["Sms:Provider"] = "SmsGate"
    };

    private static InvalidOperationException StartProduction(Action<Dictionary<string, string?>> change)
    {
        var settings = ValidProductionSettings();
        change(settings);
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            foreach (var (key, value) in settings)
            {
                if (value is null) continue;
                builder.UseSetting(key, value);
            }
        });
        return Should.Throw<InvalidOperationException>(() => factory.CreateClient());
    }

    [Fact]
    public void Production_with_Logging_sms_provider_fails_to_start()
    {
        var ex = StartProduction(s => s["Sms:Provider"] = "Logging");

        ex.Message.ShouldContain("Sms:Provider");
    }

    [Fact]
    public void Production_without_url_signing_key_fails_to_start()
    {
        var ex = StartProduction(s => s["Storage:UrlSigningKey"] = null);

        ex.Message.ShouldContain("Storage:UrlSigningKey");
    }

    [Fact]
    public void Production_smtp_without_starttls_fails_to_start()
    {
        var ex = StartProduction(s => s["Email:Smtp:UseStartTls"] = "false");

        ex.Message.ShouldContain("Email:Smtp:UseStartTls");
    }
}
