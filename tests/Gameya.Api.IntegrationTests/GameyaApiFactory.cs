using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gameya.Api.IntegrationTests;

/// <summary>Hosts the API in the E2E environment against the Testcontainers database, as the gameya_app role.</summary>
public sealed class GameyaApiFactory(PostgresFixture database) : WebApplicationFactory<Program>
{
    public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("E2E");
        builder.UseSetting("ConnectionStrings:App", database.AppConnectionString);
        builder.UseSetting("ConnectionStrings:Migrator", database.MigratorConnectionString);
        builder.UseSetting("Sms:Provider", "Logging");
        builder.UseSetting("Security:HashKey", "test-hash-key-0123456789abcdef0123456789abcdef");
        builder.UseSetting("Jwt:Issuer", "gameya-tests");
        builder.UseSetting("Jwt:Audience", "gameya-tests");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789abcdef0123456789abcdef");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }
}
