using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using Gameya.Application.Auth;
using MediatR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Gameya.Api.IntegrationTests.Auth;

[Collection(PostgresCollection.Name)]
public sealed class SessionTests : IDisposable
{
    private const string CookiePrefix = "gameya_rt=";

    private readonly PostgresFixture _db;
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _client;

    public SessionTests(PostgresFixture db)
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

    private sealed record Session(string Phone, string AccessToken, string RefreshToken);

    private static string NewPhone() =>
        "01" + "0125"[Random.Shared.Next(4)] + Random.Shared.Next(0, 100_000_000).ToString("D8");

    // The refresh cookie is Secure and the test server is http, so the cookie is carried by hand.
    private static string? CookieValue(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        var pair = values.Select(v => v.Split(';')[0])
            .FirstOrDefault(v => v.StartsWith(CookiePrefix, StringComparison.Ordinal));
        return pair?[CookiePrefix.Length..];
    }

    private async Task<Session> Login(string? phone = null)
    {
        phone ??= NewPhone();
        (await _client.PostAsJsonAsync("/api/auth/otp/request", new { phone, language = "en" }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var codeResponse = await _client.GetAsync($"/api/dev/otp/{phone}");
        using var codeDoc = JsonDocument.Parse(await codeResponse.Content.ReadAsStringAsync());
        var code = codeDoc.RootElement.GetProperty("code").GetString()!;

        var verify = await _client.PostAsJsonAsync("/api/auth/otp/verify", new { phone, code });
        verify.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        return new Session(phone, doc.RootElement.GetProperty("accessToken").GetString()!, CookieValue(verify)!);
    }

    private Task<HttpResponseMessage> Post(string url, string? refreshToken, string? accessToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (refreshToken is not null) request.Headers.Add("Cookie", CookiePrefix + refreshToken);
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> Refresh(string? token) => Post("/api/auth/refresh", token);

    private static async Task ShouldFailWith(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Refresh_rotates_cookie_and_returns_new_access_token()
    {
        var session = await Login();
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));

        var response = await Refresh(session.RefreshToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = CookieValue(response);
        rotated.ShouldNotBeNullOrEmpty();
        rotated.ShouldNotBe(session.RefreshToken);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(CookiePrefix, StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/api/auth", Case.Insensitive);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("accessToken").GetString().ShouldNotBeNullOrEmpty();
        body.RootElement.TryGetProperty("refreshToken", out _).ShouldBeFalse();

        (await Refresh(rotated)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reusing_rotated_token_after_grace_revokes_family()
    {
        var a = (await Login()).RefreshToken;
        var b = CookieValue(await Refresh(a))!;

        _factory.Clock.Advance(TimeSpan.FromSeconds(11));
        var reuse = await Refresh(a);
        await ShouldFailWith(reuse, HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_REUSED);
        CookieValue(reuse).ShouldBeEmpty();

        await ShouldFailWith(await Refresh(b), HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);
    }

    [Fact]
    public async Task Reusing_rotated_token_within_grace_does_not_revoke_family()
    {
        var a = (await Login()).RefreshToken;
        var b = CookieValue(await Refresh(a))!;

        _factory.Clock.Advance(TimeSpan.FromSeconds(5));
        await ShouldFailWith(await Refresh(a), HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);

        (await Refresh(b)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static bool DeletesCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
        && values.Any(v => v.StartsWith(CookiePrefix, StringComparison.Ordinal));

    [Fact]
    public async Task Grace_window_rejection_does_not_clear_the_cookie_but_other_failures_do()
    {
        var a = (await Login()).RefreshToken;
        (await Refresh(a)).StatusCode.ShouldBe(HttpStatusCode.OK);

        _factory.Clock.Advance(TimeSpan.FromSeconds(5));
        var grace = await Refresh(a);
        await ShouldFailWith(grace, HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);
        DeletesCookie(grace).ShouldBeFalse();

        var unknown = await Refresh("not-a-real-token");
        await ShouldFailWith(unknown, HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);
        DeletesCookie(unknown).ShouldBeTrue();
    }

    [Fact]
    public async Task Parallel_refreshes_of_one_token_yield_a_single_winner_and_do_not_fork_the_family()
    {
        var session = await Login();

        var responses = await Task.WhenAll(Refresh(session.RefreshToken), Refresh(session.RefreshToken));

        var winners = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var losers = responses.Where(r => r.StatusCode != HttpStatusCode.OK).ToList();
        winners.Count.ShouldBe(1);
        var loser = losers.Single();
        await ShouldFailWith(loser, HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);
        DeletesCookie(loser).ShouldBeFalse();

        var userId = Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(session.AccessToken).Subject);
        await using var ctx = _db.CreateAppContext();
        (await ctx.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null)).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrency_conflict_on_refresh_is_401_REFRESH_TOKEN_INVALID_and_keeps_the_cookie()
    {
        var session = await Login();
        using var racing = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<IPipelineBehavior<RefreshSessionCommand, SessionResult>>(
                new BumpTokenRowAfterHandler(_db))));
        using var client = racing.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", CookiePrefix + session.RefreshToken);

        var response = await client.SendAsync(request);

        await ShouldFailWith(response, HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);
        DeletesCookie(response).ShouldBeFalse();
    }

    /// <summary>
    /// Innermost behavior: after the handler has read and modified the token, a second writer touches the
    /// row (changing its xmin) before the transaction behavior saves, which forces the optimistic conflict.
    /// </summary>
    private sealed class BumpTokenRowAfterHandler(PostgresFixture db)
        : IPipelineBehavior<RefreshSessionCommand, SessionResult>
    {
        public async Task<SessionResult> Handle(
            RefreshSessionCommand request, RequestHandlerDelegate<SessionResult> next, CancellationToken ct)
        {
            var result = await next(ct);
            await using var other = db.CreateAppContext();
            await other.Database.ExecuteSqlRawAsync(
                "UPDATE refresh_tokens SET revoked_at = revoked_at WHERE revoked_at IS NULL", ct);
            return result;
        }
    }

    [Fact]
    public async Task Refresh_extends_expiry_sliding_30_days()
    {
        var token = (await Login()).RefreshToken;
        for (var day = 0; day < 29; day++)
        {
            _factory.Clock.Advance(TimeSpan.FromDays(1));
            var response = await Refresh(token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            token = CookieValue(response)!;
        }

        // 29 days after login the chain is alive, and the latest token carries a fresh 30 days.
        _factory.Clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1));
        (await Refresh(token)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var idle = (await Login()).RefreshToken;
        _factory.Clock.Advance(TimeSpan.FromDays(30) + TimeSpan.FromSeconds(1));
        await ShouldFailWith(await Refresh(idle), HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);
    }

    [Fact]
    public async Task Unknown_or_missing_token_is_REFRESH_TOKEN_INVALID_and_clears_cookie()
    {
        var unknown = await Refresh("not-a-real-token");
        await ShouldFailWith(unknown, HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);
        CookieValue(unknown).ShouldBeEmpty();

        await ShouldFailWith(await Refresh(null), HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);
    }

    [Fact]
    public async Task Logout_revokes_family_clears_cookie_and_is_idempotent()
    {
        var a = (await Login()).RefreshToken;
        var b = CookieValue(await Refresh(a))!;

        var logout = await Post("/api/auth/logout", b);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        CookieValue(logout).ShouldBeEmpty();

        await ShouldFailWith(await Refresh(b), HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);
        (await Post("/api/auth/logout", b)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Post("/api/auth/logout", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_all_revokes_every_device_and_logs_LOGGED_OUT_ALL()
    {
        // Access tokens are validated against the wall clock, so start the fake clock there.
        _factory.Clock.UtcNow = DateTimeOffset.UtcNow;
        var phone = NewPhone();
        var first = await Login(phone);
        _factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var second = await Login(phone);

        await ShouldFailWith(await Post("/api/auth/logout-all", null),
            HttpStatusCode.Unauthorized, ErrorCodes.UNAUTHENTICATED);

        var logoutAll = await Post("/api/auth/logout-all", first.RefreshToken, first.AccessToken);
        logoutAll.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldFailWith(await Refresh(first.RefreshToken), HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);
        await ShouldFailWith(await Refresh(second.RefreshToken), HttpStatusCode.Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID);

        var userId = Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(first.AccessToken).Subject);
        await using var ctx = _db.CreateAppContext();
        (await ctx.ActivityLog.CountAsync(a => a.Action == "LOGGED_OUT_ALL" && a.ActorUserId == userId)).ShouldBe(1);
    }

    [Fact]
    public async Task Blocked_user_refresh_fails_USER_BLOCKED()
    {
        var session = await Login();
        await using (var ctx = _db.CreateAppContext())
        {
            var phone = PhoneNumber.Parse(session.Phone);
            var user = await ctx.Users.SingleAsync(u => u.Phone == phone);
            user.Block();
            await ctx.SaveChangesAsync();
        }

        await ShouldFailWith(await Refresh(session.RefreshToken), HttpStatusCode.Forbidden, ErrorCodes.USER_BLOCKED);
    }
}
