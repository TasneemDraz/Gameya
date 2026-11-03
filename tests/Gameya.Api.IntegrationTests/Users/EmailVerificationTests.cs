using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Application.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gameya.Api.IntegrationTests.Users;

[Collection(PostgresCollection.Name)]
public sealed class EmailVerificationTests : IDisposable
{
    private readonly PostgresFixture _db;
    private readonly MailpitFixture _mail;
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _client;

    public EmailVerificationTests(PostgresFixture db, MailpitFixture mail)
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

    private static string NewEmail() => $"u{Guid.NewGuid():N}@example.com";

    private async Task<string?> ResolveAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<VerifiedEmailResolver>().ResolveAsync(userId, default);
    }

    private static async Task ShouldFailWith(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        (await TestApi.JsonAsync(response)).GetProperty("code").GetString().ShouldBe(code);
    }

    private Task<HttpResponseMessage> VerifyAsync(string token) =>
        _client.PostAsJsonAsync("/api/auth/email/verify", new { token });

    private async Task<List<string>> TokensToAsync(string email) =>
        (await _mail.MessagesToAsync(email))
            .Select(m => Regex.Match(m.Text, @"token=([A-Za-z0-9_-]+)").Groups[1].Value)
            .ToList();

    [Fact]
    public async Task Change_email_sends_link_and_resolver_returns_null_until_verified()
    {
        var userId = await TestApi.LoginAsync(_factory, _client);
        var email = NewEmail();

        (await _client.PutAsJsonAsync("/api/me/email", new { email = "  " + email.ToUpperInvariant() + " " }))
            .StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await ResolveAsync(userId)).ShouldBeNull();
        var token = await _mail.LatestTokenAsync(email);

        (await VerifyAsync(token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ResolveAsync(userId)).ShouldBe(email);
        await using var ctx = _db.CreateAppContext();
        (await ctx.ActivityLog.CountAsync(a => a.Action == "EMAIL_VERIFIED" && a.TargetId == userId.ToString()))
            .ShouldBe(1);

        // The token is single use.
        await ShouldFailWith(await VerifyAsync(token), HttpStatusCode.BadRequest, "EMAIL_TOKEN_INVALID");
    }

    [Fact]
    public async Task Changing_verified_email_keeps_old_one_until_new_verified()
    {
        var userId = await TestApi.LoginAsync(_factory, _client);
        var first = NewEmail();
        var second = NewEmail();
        await _client.PutAsJsonAsync("/api/me/email", new { email = first });
        (await VerifyAsync(await _mail.LatestTokenAsync(first))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await _client.PutAsJsonAsync("/api/me/email", new { email = second });

        (await ResolveAsync(userId)).ShouldBe(first);
        var me = await TestApi.JsonAsync(await _client.GetAsync("/api/me"));
        me.GetProperty("email").GetString().ShouldBe(first);
        me.GetProperty("emailVerified").GetBoolean().ShouldBeTrue();
        me.GetProperty("pendingEmail").GetString().ShouldBe(second);

        (await VerifyAsync(await _mail.LatestTokenAsync(second))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ResolveAsync(userId)).ShouldBe(second);
    }

    [Fact]
    public async Task Expired_token_returns_EMAIL_TOKEN_EXPIRED()
    {
        await TestApi.LoginAsync(_factory, _client);
        var email = NewEmail();
        await _client.PutAsJsonAsync("/api/me/email", new { email });
        var token = await _mail.LatestTokenAsync(email);

        _factory.Clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        await ShouldFailWith(await VerifyAsync(token), HttpStatusCode.BadRequest, "EMAIL_TOKEN_EXPIRED");
    }

    [Fact]
    public async Task Fourth_send_in_hour_returns_429_EMAIL_RESEND_LIMIT()
    {
        await TestApi.LoginAsync(_factory, _client);
        var email = NewEmail();
        (await _client.PutAsJsonAsync("/api/me/email", new { email })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await _client.PostAsync("/api/me/email/resend", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await _client.PostAsync("/api/me/email/resend", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await ShouldFailWith(await _client.PostAsync("/api/me/email/resend", null),
            HttpStatusCode.TooManyRequests, "EMAIL_RESEND_LIMIT");
        (await _mail.MessagesToAsync(email)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Resend_replaces_the_token_and_without_pending_email_is_rejected()
    {
        await TestApi.LoginAsync(_factory, _client);
        await ShouldFailWith(await _client.PostAsync("/api/me/email/resend", null),
            HttpStatusCode.BadRequest, "EMAIL_TOKEN_INVALID");

        var email = NewEmail();
        await _client.PutAsJsonAsync("/api/me/email", new { email });
        var old = await _mail.LatestTokenAsync(email);
        (await _client.PostAsync("/api/me/email/resend", null)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var latest = await _mail.LatestTokenAsync(email);

        latest.ShouldNotBe(old);
        await ShouldFailWith(await VerifyAsync(old), HttpStatusCode.BadRequest, "EMAIL_TOKEN_INVALID");
        (await VerifyAsync(latest)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Verifying_email_owned_by_other_account_returns_409_EMAIL_IN_USE()
    {
        var email = NewEmail();
        using var other = _factory.CreateClient();
        await TestApi.LoginAsync(_factory, other);
        await other.PutAsJsonAsync("/api/me/email", new { email });
        await TestApi.LoginAsync(_factory, _client);
        await _client.PutAsJsonAsync("/api/me/email", new { email });

        // Newest first: [this account's token, the other account's token].
        var tokens = await TokensToAsync(email);
        tokens.Count.ShouldBe(2);
        (await VerifyAsync(tokens[1])).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldFailWith(await VerifyAsync(tokens[0]), HttpStatusCode.Conflict, "EMAIL_IN_USE");
    }

    [Fact]
    public async Task Concurrent_verification_of_one_email_by_two_accounts_yields_one_success_and_one_409()
    {
        var email = NewEmail();
        using var other = _factory.CreateClient();
        await TestApi.LoginAsync(_factory, other);
        await other.PutAsJsonAsync("/api/me/email", new { email });
        await TestApi.LoginAsync(_factory, _client);
        await _client.PutAsJsonAsync("/api/me/email", new { email });
        var tokens = await TokensToAsync(email);

        var results = await Task.WhenAll(VerifyAsync(tokens[0]), VerifyAsync(tokens[1]));

        results.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
    }

    [Fact]
    public async Task Email_body_is_in_users_language()
    {
        await TestApi.LoginAsync(_factory, _client);
        (await _client.PutAsJsonAsync("/api/me/profile", new { fullName = "Test User", language = "ar" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var email = NewEmail();

        await _client.PutAsJsonAsync("/api/me/email", new { email });

        var message = (await _mail.MessagesToAsync(email)).Single();
        message.Subject.ShouldContain("تأكيد");
        message.Html.ShouldContain("/verify-email?token=");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task Invalid_email_is_rejected(string email)
    {
        await TestApi.LoginAsync(_factory, _client);
        await ShouldFailWith(await _client.PutAsJsonAsync("/api/me/email", new { email }),
            HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Send_failure_returns_503_and_rolls_back_the_pending_email()
    {
        using var offline = new GameyaApiFactory(_db);
        using var client = offline.CreateClient();
        await TestApi.LoginAsync(offline, client);

        await ShouldFailWith(await client.PutAsJsonAsync("/api/me/email", new { email = NewEmail() }),
            HttpStatusCode.ServiceUnavailable, "SMS_SEND_FAILED");

        var me = await TestApi.JsonAsync(await client.GetAsync("/api/me"));
        me.GetProperty("pendingEmail").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Garbage_token_is_rejected()
    {
        await ShouldFailWith(await VerifyAsync("nope"), HttpStatusCode.BadRequest, "EMAIL_TOKEN_INVALID");
        await ShouldFailWith(await VerifyAsync(""), HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Email_change_and_resend_require_authentication()
    {
        (await _client.PutAsJsonAsync("/api/me/email", new { email = NewEmail() }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _client.PostAsync("/api/me/email/resend", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
