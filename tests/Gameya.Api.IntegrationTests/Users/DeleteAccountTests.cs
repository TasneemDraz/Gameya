using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Application.Abstractions;
using Gameya.Domain.Common;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Gameya.Api.IntegrationTests.Users;

[Collection(PostgresCollection.Name)]
public sealed class DeleteAccountTests : IDisposable
{
    private const string CookiePrefix = "gameya_rt=";

    private readonly PostgresFixture _db;
    private readonly MailpitFixture _mail;
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _client;

    public DeleteAccountTests(PostgresFixture db, MailpitFixture mail)
    {
        _db = db;
        _mail = mail;
        _factory = new GameyaApiFactory(db, mail);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed class ActiveGameyaChecker : IActiveParticipationChecker
    {
        public Task<bool> HasRecruitingOrActiveGameyasAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed record Login(Guid UserId, string Phone, string Refresh, string Access);

    private static string? CookieValue(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0]).FirstOrDefault(v => v.StartsWith(CookiePrefix, StringComparison.Ordinal))
            : null;

    // skew moves the fake clock ahead of wall time, to clear the 60 s OTP resend gate on a repeated phone.
    private async Task<Login> LoginAsync(string? phone = null, TimeSpan skew = default)
    {
        _factory.Clock.UtcNow = DateTimeOffset.UtcNow + skew;
        phone ??= TestApi.NewPhone();
        (await _client.PostAsJsonAsync("/api/auth/otp/request", new { phone, language = "en" })).EnsureSuccessStatusCode();
        var code = (await TestApi.JsonAsync(await _client.GetAsync($"/api/dev/otp/{phone}"))).GetProperty("code").GetString()!;
        var verify = await _client.PostAsJsonAsync("/api/auth/otp/verify", new { phone, code });
        verify.EnsureSuccessStatusCode();
        var access = (await TestApi.JsonAsync(verify)).GetProperty("accessToken").GetString()!;
        var id = Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(access).Subject);
        return new Login(id, phone, CookieValue(verify)![CookiePrefix.Length..], access);
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string access, string? refresh = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (refresh is not null) request.Headers.Add("Cookie", CookiePrefix + refresh);
        return request;
    }

    [Fact]
    public async Task Delete_anonymizes_and_revokes_sessions_and_logs_ACCOUNT_DELETED()
    {
        var s = await LoginAsync();
        (await _client.SendAsync(Request(HttpMethod.Get, "/api/me", s.Access))).EnsureSuccessStatusCode();
        var put = Request(HttpMethod.Put, "/api/me/profile", s.Access);
        put.Content = JsonContent.Create(new { fullName = "Test Person", language = "en" });
        (await _client.SendAsync(put)).EnsureSuccessStatusCode();

        using var photoClient = _factory.CreateClient();
        photoClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s.Access);
        (await TestApi.UploadPhotoAsync(photoClient, TestApi.TestFile("photo.jpg"))).EnsureSuccessStatusCode();

        string photoKey;
        await using (var before = _db.CreateAppContext())
        {
            photoKey = (await before.Users.AsNoTracking().SingleAsync(u => u.Id == s.UserId)).PhotoKey!;
        }

        var response = await _client.SendAsync(Request(HttpMethod.Delete, "/api/me", s.Access, s.Refresh));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Set-Cookie").ShouldContain(c =>
            c.StartsWith(CookiePrefix, StringComparison.Ordinal)
            && c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

        await using var ctx = _db.CreateAppContext();
        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == s.UserId);
        user.IsDeleted.ShouldBeTrue();
        user.Phone.ShouldBeNull();
        user.FullName.ShouldBeNull();
        user.Email.ShouldBeNull();
        user.PhotoKey.ShouldBeNull();
        (await ctx.RefreshTokens.AsNoTracking().Where(t => t.UserId == s.UserId).ToListAsync())
            .ShouldAllBe(t => t.RevokedAt != null);
        (await ctx.ActivityLog.CountAsync(a => a.Action == "ACCOUNT_DELETED" && a.ActorUserId == s.UserId)).ShouldBe(1);

        var store = _factory.Services.GetRequiredService<IFileStore>();
        (await store.OpenReadAsync(FileContainers.ProfilePhotos, photoKey, CancellationToken.None)).ShouldBeNull();

        var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refresh.Headers.Add("Cookie", CookiePrefix + s.Refresh);
        (await _client.SendAsync(refresh)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A repeated DELETE with the still-valid access token is rejected, not a 500.
        (await _client.SendAsync(Request(HttpMethod.Delete, "/api/me", s.Access))).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_blocked_when_checker_reports_active_gameya_409()
    {
        using var blocked = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IActiveParticipationChecker>();
            s.AddSingleton<IActiveParticipationChecker, ActiveGameyaChecker>();
        }));
        using var client = blocked.CreateClient();
        var userId = await TestApi.LoginAsync(_factory, client);

        var response = await client.DeleteAsync("/api/me");
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TestApi.JsonAsync(response)).GetProperty("code").GetString().ShouldBe(ErrorCodes.ACCOUNT_HAS_ACTIVE_GAMEYAS);

        await using var ctx = _db.CreateAppContext();
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).IsDeleted.ShouldBeFalse();
        (await ctx.RefreshTokens.AsNoTracking().Where(t => t.UserId == userId).ToListAsync())
            .ShouldAllBe(t => t.RevokedAt == null);
    }

    [Fact]
    public async Task Deleted_users_phone_can_sign_up_as_new_user()
    {
        var first = await LoginAsync();
        (await _client.SendAsync(Request(HttpMethod.Delete, "/api/me", first.Access, first.Refresh)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var second = await LoginAsync(first.Phone, TimeSpan.FromSeconds(90));
        second.UserId.ShouldNotBe(first.UserId);

        await using var ctx = _db.CreateAppContext();
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == first.UserId)).IsDeleted.ShouldBeTrue();
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == second.UserId)).IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Deleted_user_with_a_still_valid_token_cannot_repopulate_the_account()
    {
        var s = await LoginAsync();
        (await _client.SendAsync(Request(HttpMethod.Delete, "/api/me", s.Access, s.Refresh)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var email = $"u{Guid.NewGuid():N}@example.com";
        var profile = Request(HttpMethod.Put, "/api/me/profile", s.Access);
        profile.Content = JsonContent.Create(new { fullName = "Back Again", language = "en" });
        var changeEmail = Request(HttpMethod.Put, "/api/me/email", s.Access);
        changeEmail.Content = JsonContent.Create(new { email });

        foreach (var request in new[] { profile, changeEmail, Request(HttpMethod.Get, "/api/me", s.Access) })
        {
            var response = await _client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, request.RequestUri!.ToString());
            (await TestApi.JsonAsync(response)).GetProperty("code").GetString().ShouldBe(ErrorCodes.UNAUTHENTICATED);
        }

        await using var ctx = _db.CreateAppContext();
        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == s.UserId);
        user.FullName.ShouldBeNull();
        user.Email.ShouldBeNull();
        user.PendingEmail.ShouldBeNull();
        (await _mail.MessagesToAsync(email)).ShouldBeEmpty();
    }
}
