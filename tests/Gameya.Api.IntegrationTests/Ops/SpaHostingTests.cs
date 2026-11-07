using System.Net;
using Gameya.Api.Infrastructure;
using Gameya.Api.IntegrationTests.Infrastructure;

namespace Gameya.Api.IntegrationTests.Ops;

[Collection(DatabaseCollection.Name)]
public sealed class SpaHostingTests : IDisposable
{
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "gameya-web-" + Guid.NewGuid().ToString("N"));
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _client;

    public SpaHostingTests(DatabaseFixture db)
    {
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<html><body><app-root></app-root></body></html>");
        File.WriteAllText(Path.Combine(_webRoot, "main-ABCD1234.js"), "console.log(1);");
        File.WriteAllText(Path.Combine(_webRoot, "favicon.ico"), "ico");
        _factory = new GameyaApiFactory(db, webRoot: _webRoot);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        TempDirectory.DeleteWithRetry(_webRoot);
    }

    [Fact]
    public async Task Root_serves_index_with_no_cache()
    {
        var response = await _client.GetAsync("/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        response.Headers.CacheControl!.ToString().ShouldBe("no-cache");
    }

    [Fact]
    public async Task Deep_link_serves_index()
    {
        var response = await _client.GetAsync("/profile/edit");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("<app-root>");
        response.Headers.CacheControl!.ToString().ShouldBe("no-cache");
    }

    [Fact]
    public async Task Missing_file_with_extension_is_404_not_index()
    {
        var response = await _client.GetAsync("/missing-file.js");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Hashed_asset_is_immutable()
    {
        var response = await _client.GetAsync("/main-ABCD1234.js");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("console.log(1);");
        response.Headers.CacheControl!.ToString().ShouldBe("public, max-age=31536000, immutable");
    }

    [Fact]
    public async Task Unhashed_asset_is_no_cache()
    {
        var response = await _client.GetAsync("/favicon.ico");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.ToString().ShouldBe("no-cache");
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api")]
    [InlineData("/hubs/x")]
    public async Task Unknown_api_route_is_404_not_index(string path)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("<app-root>");
    }

    [Fact]
    public async Task Health_is_not_swallowed()
    {
        var response = await _client.GetAsync("/health");

        var body = await TestApi.JsonAsync(response);
        body.GetProperty("status").GetString().ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("/", HttpStatusCode.OK)]
    [InlineData("/api/me", HttpStatusCode.Unauthorized)]
    public async Task Security_headers_on_every_response(string path, HttpStatusCode expected)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.ShouldBe(expected);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldBe(SecurityHeaders.ContentSecurityPolicy);
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("strict-origin-when-cross-origin");
        response.Headers.GetValues("Strict-Transport-Security").Single().ShouldBe("max-age=31536000; includeSubDomains");
    }
}
