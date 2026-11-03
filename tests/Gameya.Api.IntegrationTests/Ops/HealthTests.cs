using System.Net;
using Gameya.Api.IntegrationTests.Infrastructure;

namespace Gameya.Api.IntegrationTests.Ops;

[Collection(PostgresCollection.Name)]
public sealed class HealthTests : IDisposable
{
    private readonly PostgresFixture _db;
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _client;

    public HealthTests(PostgresFixture db)
    {
        _db = db;
        _factory = new GameyaApiFactory(db);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Health_reports_healthy_with_three_checks()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await TestApi.JsonAsync(response);
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        var entries = body.GetProperty("entries");
        entries.EnumerateObject().Select(e => e.Name).Order().ToArray().ShouldBe(["files", "hangfire", "postgres"]);
        foreach (var entry in entries.EnumerateObject())
        {
            entry.Value.GetProperty("status").GetString().ShouldBe("Healthy", entry.Name);
        }
    }

    [Fact]
    public async Task Health_is_503_when_the_files_root_is_not_writable()
    {
        // A regular file where the storage root should be: it can be neither created nor written into.
        var blocked = Path.Combine(Path.GetTempPath(), "gameya-health-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(blocked, "x");
        try
        {
            using var factory = new GameyaApiFactory(_db, storageRoot: blocked);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/health");

            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            var entries = (await TestApi.JsonAsync(response)).GetProperty("entries");
            entries.GetProperty("files").GetProperty("status").GetString().ShouldBe("Unhealthy");
            entries.GetProperty("postgres").GetProperty("status").GetString().ShouldBe("Healthy");
        }
        finally
        {
            File.Delete(blocked);
        }
    }

    [Fact]
    public async Task Correlation_id_is_echoed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dev/otp/01012345678");
        request.Headers.Add("X-Correlation-Id", "test-corr-123");

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").ToArray().ShouldBe(["test-corr-123"]);
        // The ProblemDetails traceId is the correlation id, not the Activity trace id.
        (await TestApi.JsonAsync(response)).GetProperty("traceId").GetString().ShouldBe("test-corr-123");
    }

    [Fact]
    public async Task Correlation_id_is_generated_when_absent_and_replaced_when_unsafe()
    {
        var generated = await _client.GetAsync("/health");
        generated.Headers.GetValues("X-Correlation-Id").Single().ShouldNotBeNullOrWhiteSpace();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "bad id with spaces <script>");
        var replaced = await _client.SendAsync(request);
        replaced.Headers.GetValues("X-Correlation-Id").Single().ShouldNotContain(" ");
    }
}
