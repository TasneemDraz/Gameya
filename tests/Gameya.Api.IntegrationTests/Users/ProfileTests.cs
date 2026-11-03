using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Application.Abstractions;
using ImageMagick;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gameya.Api.IntegrationTests.Users;

[Collection(PostgresCollection.Name)]
public sealed class ProfileTests : IDisposable
{
    private readonly PostgresFixture _db;
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _client;

    public ProfileTests(PostgresFixture db)
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

    private static async Task ShouldFailWith(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        (await TestApi.JsonAsync(response)).GetProperty("code").GetString().ShouldBe(code);
    }

    private sealed class FailingUrlFileStore(IFileStore inner) : IFileStore
    {
        public static volatile bool Fail;

        public Task UploadAsync(string container, string key, Stream content, string contentType, CancellationToken ct) =>
            inner.UploadAsync(container, key, content, contentType, ct);

        public Task DeleteAsync(string container, string key, CancellationToken ct) => inner.DeleteAsync(container, key, ct);

        public Task<Stream?> OpenReadAsync(string container, string key, CancellationToken ct) =>
            inner.OpenReadAsync(container, key, ct);

        // Called by SetPhoto after the upload, so throwing here fails the request after the file is stored.
        public Uri GetReadUrl(string container, string key, TimeSpan lifetime) =>
            Fail ? throw new InvalidOperationException("forced failure after upload") : inner.GetReadUrl(container, key, lifetime);

        public bool IsReadUrlValid(string container, string key, long exp, string sig) =>
            inner.IsReadUrlValid(container, key, exp, sig);
    }

    [Fact]
    public async Task Failure_after_upload_removes_the_new_file_and_keeps_the_old_photo()
    {
        using var failing = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            var inner = s.Single(d => d.ServiceType == typeof(IFileStore));
            s.Remove(inner);
            s.AddSingleton<IFileStore>(sp => new FailingUrlFileStore((IFileStore)inner.ImplementationFactory!(sp)));
        }));
        using var client = failing.CreateClient();
        await TestApi.LoginAsync(_factory, client);
        var first = (await TestApi.JsonAsync(await TestApi.UploadPhotoAsync(client, TestApi.TestFile("photo.jpg"))))
            .GetProperty("photoUrl").GetString()!;

        FailingUrlFileStore.Fail = true;
        try
        {
            (await TestApi.UploadPhotoAsync(client, TestApi.TestFile("photo.jpg")))
                .StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }
        finally
        {
            FailingUrlFileStore.Fail = false;
        }

        Directory.GetFiles(_factory.StorageRoot, "*", SearchOption.AllDirectories).Length.ShouldBe(1);
        (await client.GetAsync(first)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Me_requires_auth_401_UNAUTHENTICATED()
    {
        await ShouldFailWith(await _client.GetAsync("/api/me"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task UpdateProfile_sets_name_and_completes_profile_and_logs_PROFILE_UPDATED()
    {
        var phone = TestApi.NewPhone();
        var userId = await TestApi.LoginAsync(_factory, _client, phone);

        var before = await TestApi.JsonAsync(await _client.GetAsync("/api/me"));
        before.GetProperty("profileComplete").GetBoolean().ShouldBeFalse();
        before.GetProperty("phone").GetString().ShouldBe("+2" + phone);
        before.GetProperty("photoUrl").ValueKind.ShouldBe(JsonValueKind.Null);

        var put = await _client.PutAsJsonAsync("/api/me/profile", new { fullName = "  Mona Ali ", language = "en" });
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await TestApi.JsonAsync(await _client.GetAsync("/api/me"));
        after.GetProperty("fullName").GetString().ShouldBe("Mona Ali");
        after.GetProperty("language").GetString().ShouldBe("en");
        after.GetProperty("profileComplete").GetBoolean().ShouldBeTrue();
        after.GetProperty("emailVerified").GetBoolean().ShouldBeFalse();

        await using var ctx = _db.CreateAppContext();
        (await ctx.ActivityLog.CountAsync(a => a.Action == "PROFILE_UPDATED" && a.ActorUserId == userId)).ShouldBe(1);
        var row = await ctx.ActivityLog.SingleAsync(a => a.Action == "PROFILE_UPDATED" && a.ActorUserId == userId);
        row.DetailsJson.ShouldNotContain("Mona");
        using var details = JsonDocument.Parse(row.DetailsJson);
        details.RootElement.GetProperty("changed").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(["fullName", "language"]);
    }

    [Fact]
    public async Task UpdateProfile_rejects_one_char_name_with_NAME_INVALID()
    {
        await TestApi.LoginAsync(_factory, _client);

        await ShouldFailWith(
            await _client.PutAsJsonAsync("/api/me/profile", new { fullName = "A", language = "ar" }),
            HttpStatusCode.BadRequest, "NAME_INVALID");
    }

    [Fact]
    public async Task UpdateProfile_rejects_unknown_language_with_VALIDATION_FAILED()
    {
        await TestApi.LoginAsync(_factory, _client);

        await ShouldFailWith(
            await _client.PutAsJsonAsync("/api/me/profile", new { fullName = "Mona Ali", language = "fr" }),
            HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Upload_jpg_returns_photo_url_resized_to_512()
    {
        await TestApi.LoginAsync(_factory, _client);

        var upload = await TestApi.UploadPhotoAsync(_client, TestApi.TestFile("photo.jpg"));

        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        var url = (await TestApi.JsonAsync(upload)).GetProperty("photoUrl").GetString()!;
        url.ShouldStartWith("/api/files/profile-photos/");

        var me = await TestApi.JsonAsync(await _client.GetAsync("/api/me"));
        me.GetProperty("photoUrl").GetString()!.ShouldStartWith("/api/files/profile-photos/");

        // The signed URL is the credential: no bearer token.
        using var anonymous = _factory.CreateClient();
        var download = await anonymous.GetAsync(url);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
        download.Headers.CacheControl!.Private.ShouldBeTrue();
        download.Headers.CacheControl.MaxAge.ShouldBe(TimeSpan.FromSeconds(300));
        download.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");

        using var image = new MagickImage(await download.Content.ReadAsByteArrayAsync());
        image.Format.ShouldBe(MagickFormat.Jpeg);
        Math.Max(image.Width, image.Height).ShouldBe(512u);
    }

    [Fact]
    public async Task Upload_heic_is_accepted_and_converted_to_jpeg()
    {
        await TestApi.LoginAsync(_factory, _client);

        var upload = await TestApi.UploadPhotoAsync(_client, TestApi.TestFile("photo.heic"), "photo.heic");

        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        var url = (await TestApi.JsonAsync(upload)).GetProperty("photoUrl").GetString()!;
        var bytes = await _factory.CreateClient().GetByteArrayAsync(url);
        bytes[..3].ShouldBe(new byte[] { 0xFF, 0xD8, 0xFF });
        using var image = new MagickImage(bytes);
        image.Format.ShouldBe(MagickFormat.Jpeg);
        Math.Max(image.Width, image.Height).ShouldBeLessThanOrEqualTo(512u);
    }

    [Fact]
    public async Task Upload_pdf_renamed_jpg_returns_FILE_TYPE_NOT_ALLOWED()
    {
        await TestApi.LoginAsync(_factory, _client);

        await ShouldFailWith(
            await TestApi.UploadPhotoAsync(_client, TestApi.TestFile("doc.pdf"), "photo.jpg"),
            HttpStatusCode.BadRequest, "FILE_TYPE_NOT_ALLOWED");
    }

    [Fact]
    public async Task Upload_6MB_returns_FILE_TOO_LARGE()
    {
        await TestApi.LoginAsync(_factory, _client);

        await ShouldFailWith(
            await TestApi.UploadPhotoAsync(_client, new byte[6 * 1024 * 1024]),
            HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE");
    }

    [Fact]
    public async Task Replacing_a_photo_deletes_the_old_file_and_removing_clears_it()
    {
        await TestApi.LoginAsync(_factory, _client);
        var first = (await TestApi.JsonAsync(await TestApi.UploadPhotoAsync(_client, TestApi.TestFile("photo.jpg"))))
            .GetProperty("photoUrl").GetString()!;
        var second = (await TestApi.JsonAsync(await TestApi.UploadPhotoAsync(_client, TestApi.TestFile("photo.jpg"))))
            .GetProperty("photoUrl").GetString()!;

        first.ShouldNotBe(second);
        (await _client.GetAsync(first)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.GetAsync(second)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _client.DeleteAsync("/api/me/photo")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _client.GetAsync(second)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TestApi.JsonAsync(await _client.GetAsync("/api/me")))
            .GetProperty("photoUrl").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
