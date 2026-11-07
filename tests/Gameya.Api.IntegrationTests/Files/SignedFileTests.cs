using System.Net;
using System.Web;
using Gameya.Api.IntegrationTests.Infrastructure;

namespace Gameya.Api.IntegrationTests.Files;

[Collection(DatabaseCollection.Name)]
public sealed class SignedFileTests : IDisposable
{
    private const string PhotoPrefix = "/api/files/profile-photos/";

    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _client;

    public SignedFileTests(DatabaseFixture db)
    {
        _factory = new GameyaApiFactory(db);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<string> UploadAsync()
    {
        await TestApi.LoginAsync(_factory, _client);
        var upload = await TestApi.UploadPhotoAsync(_client, TestApi.TestFile("photo.jpg"));
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await TestApi.JsonAsync(upload)).GetProperty("photoUrl").GetString()!;
    }

    private static async Task ShouldFailWith(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        (await TestApi.JsonAsync(response)).GetProperty("code").GetString().ShouldBe(code);
    }

    private static Uri Parse(string url) => new("http://localhost" + url);

    [Fact]
    public async Task Tampered_signature_returns_403_FORBIDDEN()
    {
        var url = await UploadAsync();
        var tampered = url[..^2] + (url.EndsWith("AA") ? "BB" : "AA");

        await ShouldFailWith(await _client.GetAsync(tampered), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Expired_url_returns_403_FORBIDDEN()
    {
        var url = await UploadAsync();
        (await _client.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.OK);

        _factory.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        await ShouldFailWith(await _client.GetAsync(url), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Theory]
    [InlineData("..%5c..%5cetc%5cpasswd")]
    [InlineData("..%2f..%2fetc%2fpasswd")]
    [InlineData("%2e%2e%2f%2e%2e%2fetc%2fpasswd")]
    public async Task Key_with_path_traversal_returns_403_FORBIDDEN(string key)
    {
        // Correctly signed, so the rejection can only come from key validation.
        var exp = _factory.Clock.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var url = TestApi.SignedUrl("profile-photos", key, exp);

        await ShouldFailWith(await _client.GetAsync(url), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Unknown_container_returns_403_FORBIDDEN()
    {
        var url = await UploadAsync();
        var uri = Parse(url);
        var key = uri.AbsolutePath[PhotoPrefix.Length..];
        var exp = long.Parse(HttpUtility.ParseQueryString(uri.Query)["exp"]!);

        await ShouldFailWith(
            await _client.GetAsync(TestApi.SignedUrl("secrets", key, exp)), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Signed_url_for_deleted_file_returns_404_NOT_FOUND()
    {
        var url = await UploadAsync();
        (await _client.DeleteAsync("/api/me/photo")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldFailWith(await _client.GetAsync(url), HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
