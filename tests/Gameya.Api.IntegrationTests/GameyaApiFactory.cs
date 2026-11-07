using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Api.IntegrationTests.Ops;
using Gameya.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;

namespace Gameya.Api.IntegrationTests;

/// <summary>Hosts the API in the E2E environment against the Testcontainers SQL Server database, using a single login.</summary>
public sealed class GameyaApiFactory(DatabaseFixture database, MailpitFixture? mail = null, string? storageRoot = null, IReadOnlyDictionary<string, string?>? settings = null, string? webRoot = null, string? contentRoot = null) : WebApplicationFactory<Program>
{
    public DatabaseFixture Database => database;

    public const string UrlSigningKey = "test-url-signing-key-0123456789abcdef0123456789";

    /// <summary>A temp directory per factory, so tests never share stored files.</summary>
    public string StorageRoot { get; } = storageRoot ?? Path.Combine(Path.GetTempPath(), "gameya-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Every Serilog event the host writes; the host adds DI-registered sinks to its logger.</summary>
    public CapturingSink LogSink { get; } = new();

    public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("E2E");
        builder.UseSetting("ConnectionStrings:App", database.AppConnectionString);
        builder.UseSetting("ConnectionStrings:Migrator", database.MigratorConnectionString);
        builder.UseSetting("Sms:Provider", "Logging");
        builder.UseSetting("Security:HashKey", "test-hash-key-0123456789abcdef0123456789abcdef");
        builder.UseSetting("App:WebBaseUrl", "https://app.gameya.test");
        builder.UseSetting("Email:From", "Gam'eya <noreply@gameya.test>");
        // Without a Mailpit fixture, port 1 refuses connections, so a send fails fast instead of reaching a real server.
        builder.UseSetting("Email:Smtp:Host", mail?.Host ?? "127.0.0.1");
        builder.UseSetting("Email:Smtp:Port", (mail?.SmtpPort ?? 1).ToString());
        builder.UseSetting("Email:Smtp:UseStartTls", "false");
        builder.UseSetting("Storage:RootPath", StorageRoot);
        builder.UseSetting("Storage:UrlSigningKey", UrlSigningKey);
        builder.UseSetting("Jwt:Issuer", "gameya-tests");
        builder.UseSetting("Jwt:Audience", "gameya-tests");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789abcdef0123456789abcdef");

        if (contentRoot is not null)
        {
            // Under WebApplicationFactory the content root is src/Gameya.Api, which tests must never write into.
            // Tests that need their own content root get a copy of the appsettings files there.
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "appsettings*.json"))
            {
                File.Copy(file, Path.Combine(contentRoot, Path.GetFileName(file)), overwrite: true);
            }

            builder.UseContentRoot(contentRoot);
        }

        if (contentRoot is null)
        {
            // Defence in depth: a developer's own src/Gameya.Api/secrets.json must never feed the test suite.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                foreach (var source in config.Sources.OfType<JsonConfigurationSource>()
                             .Where(s => string.Equals(s.Path, "secrets.json", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    config.Sources.Remove(source);
                }
            });
        }

        if (webRoot is not null)
        {
            builder.UseWebRoot(webRoot);
        }

        foreach (var (name, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(name, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
            services.AddSingleton<ILogEventSink>(LogSink);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(StorageRoot))
        {
            TempDirectory.DeleteWithRetry(StorageRoot);
        }
    }
}
