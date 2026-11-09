using System.Net;
using Gameya.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Gameya.Api.IntegrationTests.Ops;

[Collection(DatabaseCollection.Name)]
public sealed class HostingConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gameya-hosting-" + Guid.NewGuid().ToString("N"));
    private readonly string _contentRoot;
    private readonly string _webRoot;
    private readonly DatabaseFixture _db;
    private GameyaApiFactory? _factory;

    public HostingConfigTests(DatabaseFixture db)
    {
        _db = db;
        _contentRoot = Path.Combine(_root, "content");
        _webRoot = Path.Combine(_root, "web");
        Directory.CreateDirectory(_contentRoot);
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<html><body><app-root></app-root></body></html>");
    }

    public void Dispose()
    {
        _factory?.Dispose();
        TempDirectory.DeleteWithRetry(_root);
    }

    private GameyaApiFactory Create(IReadOnlyDictionary<string, string?>? settings = null) =>
        _factory = new GameyaApiFactory(_db, settings: settings, webRoot: _webRoot, contentRoot: _contentRoot);

    [Theory]
    [InlineData("/secrets.json")]
    [InlineData("/appsettings.json")]
    public async Task Secrets_and_appsettings_are_not_served(string path)
    {
        File.WriteAllText(Path.Combine(_contentRoot, "secrets.json"), """{"Jwt":{"SigningKey":"secret-signing-key-0123456789abcdef0123456789"}}""");
        using var client = Create().CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Secrets_json_values_are_read()
    {
        File.WriteAllText(
            Path.Combine(_contentRoot, "secrets.json"),
            """{"Sample":{"FromSecrets":"https://from-secrets.test"}}""");
        var factory = Create();

        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        configuration["Sample:FromSecrets"].ShouldBe("https://from-secrets.test");
    }

    [Fact]
    public void Factory_settings_win_over_secrets_json()
    {
        File.WriteAllText(
            Path.Combine(_contentRoot, "secrets.json"),
            """{"App":{"WebBaseUrl":"https://from-secrets.test"}}""");
        var factory = Create(new Dictionary<string, string?> { ["App:WebBaseUrl"] = "https://from-settings.test" });

        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        configuration["App:WebBaseUrl"].ShouldBe("https://from-settings.test");
    }

    [Fact]
    public async Task File_sink_writes_redacted_events()
    {
        var factory = Create(new Dictionary<string, string?> { ["Observability:LogFilePath"] = "logs/test-.json" });
        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<ILogger>().Information("call from {Phone}", "01012345678");

        var logs = Directory.GetFiles(Path.Combine(_contentRoot, "logs"), "test-*.json");
        logs.Length.ShouldBe(1);
        using var stream = new FileStream(logs[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var text = await new StreamReader(stream).ReadToEndAsync();
        text.ShouldContain("010****5678");
        text.ShouldNotContain("01012345678");
    }
}
