using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Application.Abstractions;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Gameya.Api.IntegrationTests.Auth;

[Collection(PostgresCollection.Name)]
public sealed class OtpFlowTests : IDisposable
{
    private readonly PostgresFixture _db;
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _client;

    public OtpFlowTests(PostgresFixture db)
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

    private static string NewPhone() =>
        "01" + "0125"[Random.Shared.Next(4)] + Random.Shared.Next(0, 100_000_000).ToString("D8");

    private static Task<HttpResponseMessage> RequestOtp(HttpClient client, string phone) =>
        client.PostAsJsonAsync("/api/auth/otp/request", new { phone, language = "en" });

    private static Task<HttpResponseMessage> Verify(HttpClient client, string phone, string code) =>
        client.PostAsJsonAsync("/api/auth/otp/verify", new { phone, code });

    private static async Task<string> ErrorCode(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    private async Task<string> DevCode(string phone)
    {
        var response = await _client.GetAsync($"/api/dev/otp/{phone}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    private async Task<HttpResponseMessage> Login(string phone)
    {
        (await RequestOtp(_client, phone)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        return await Verify(_client, phone, await DevCode(phone));
    }

    private static async Task<string> SubjectOf(HttpResponseMessage verifyResponse)
    {
        verifyResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("accessToken").GetString()!;
        return new JsonWebTokenHandler().ReadJsonWebToken(token).Subject;
    }

    [Fact]
    public async Task Request_then_verify_creates_user_and_returns_session()
    {
        var phone = NewPhone();

        var request = await RequestOtp(_client, phone);
        request.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using (var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync()))
        {
            body.RootElement.TryGetProperty("expiresAt", out _).ShouldBeTrue();
            body.RootElement.TryGetProperty("resendAvailableAt", out _).ShouldBeTrue();
        }

        var verify = await Verify(_client, phone, await DevCode(phone));

        verify.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var session = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        session.RootElement.GetProperty("profileComplete").GetBoolean().ShouldBeFalse();
        session.RootElement.TryGetProperty("refreshToken", out _).ShouldBeFalse();

        var cookie = verify.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("gameya_rt=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/api/auth", Case.Insensitive);

        var userId = Guid.Parse(new JsonWebTokenHandler()
            .ReadJsonWebToken(session.RootElement.GetProperty("accessToken").GetString()!).Subject);
        await using var ctx = _db.CreateAppContext();
        (await ctx.ActivityLog.CountAsync(a => a.Action == "USER_REGISTERED" && a.ActorUserId == userId))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Verify_with_existing_phone_logs_in_same_user()
    {
        var phone = NewPhone();
        var first = await SubjectOf(await Login(phone));

        _factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var second = await SubjectOf(await Login(phone));

        second.ShouldBe(first);
        await using var ctx = _db.CreateAppContext();
        var parsed = PhoneNumber.Parse(phone);
        (await ctx.Users.CountAsync(u => u.Phone == parsed)).ShouldBe(1);
    }

    [Fact]
    public async Task Resend_within_60s_returns_429_OTP_RESEND_TOO_SOON()
    {
        var phone = NewPhone();
        (await RequestOtp(_client, phone)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var again = await RequestOtp(_client, phone);

        again.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await ErrorCode(again)).ShouldBe(ErrorCodes.OTP_RESEND_TOO_SOON);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_number()
    {
        var phone = NewPhone();
        (await RequestOtp(_client, phone)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var real = await DevCode(phone);
        var wrong = real == "000000" ? "111111" : "000000";

        for (var i = 1; i <= 4; i++)
        {
            var r = await Verify(_client, phone, wrong);
            r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ErrorCode(r)).ShouldBe(ErrorCodes.OTP_INVALID);
        }

        var fifth = await Verify(_client, phone, wrong);
        fifth.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await ErrorCode(fifth)).ShouldBe(ErrorCodes.OTP_LOCKED);

        _factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var resend = await RequestOtp(_client, phone);
        resend.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await ErrorCode(resend)).ShouldBe(ErrorCodes.OTP_LOCKED);

        // The correct code is also refused while locked.
        (await ErrorCode(await Verify(_client, phone, real))).ShouldBe(ErrorCodes.OTP_LOCKED);
    }

    [Fact]
    public async Task Invalid_phone_returns_400_INVALID_PHONE()
    {
        var response = await RequestOtp(_client, "12345");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).ShouldBe(ErrorCodes.INVALID_PHONE);
    }

    [Fact]
    public async Task Malformed_code_returns_400_VALIDATION_FAILED()
    {
        var response = await Verify(_client, NewPhone(), "12ab");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).ShouldBe(ErrorCodes.VALIDATION_FAILED);
    }

    [Fact]
    public async Task Parallel_requests_for_same_phone_never_500()
    {
        for (var round = 0; round < 3; round++)
        {
            var phone = NewPhone();

            var responses = await Task.WhenAll(RequestOtp(_client, phone), RequestOtp(_client, phone));

            foreach (var r in responses)
                new[] { HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests }.ShouldContain(r.StatusCode);
        }
    }

    [Fact]
    public async Task Blocked_user_cannot_log_in_USER_BLOCKED()
    {
        var phone = NewPhone();
        var userId = Guid.Parse(await SubjectOf(await Login(phone)));
        await using (var ctx = _db.CreateAppContext())
        {
            var user = await ctx.Users.SingleAsync(u => u.Id == userId);
            user.Block();
            await ctx.SaveChangesAsync();
        }

        _factory.Clock.Advance(TimeSpan.FromSeconds(61));
        var response = await Login(phone);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCode(response)).ShouldBe(ErrorCodes.USER_BLOCKED);
    }

    [Fact]
    public async Task Unknown_exception_returns_problem_with_code_INTERNAL_and_no_stack()
    {
        using var broken = _factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => Replace(s, new ThrowingSender(new InvalidOperationException("boom-secret")))));
        using var client = broken.CreateClient();

        var response = await RequestOtp(client, NewPhone());

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        (await ErrorCode(response)).ShouldBe("INTERNAL");
        body.ShouldNotContain("boom-secret");
        body.ShouldNotContain("InvalidOperationException");
        body.ShouldNotContain(" at ");
    }

    [Fact]
    public async Task Sms_failure_returns_503_SMS_SEND_FAILED_and_does_not_consume_a_resend()
    {
        var phone = NewPhone();
        using (var broken = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(
                   s => Replace(s, new ThrowingSender(new DomainException(ErrorCodes.SMS_SEND_FAILED))))))
        {
            using var brokenClient = broken.CreateClient();
            var failed = await RequestOtp(brokenClient, phone);
            failed.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await ErrorCode(failed)).ShouldBe(ErrorCodes.SMS_SEND_FAILED);
        }

        (await RequestOtp(_client, phone)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Otp_endpoints_are_rate_limited_per_client_with_429_RATE_LIMITED()
    {
        HttpResponseMessage? last = null;
        for (var i = 0; i < 11; i++) last = await Verify(_client, NewPhone(), "123456");

        last!.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await ErrorCode(last)).ShouldBe(ErrorCodes.RATE_LIMITED);
    }

    private static void Replace(IServiceCollection services, ISmsSender sender)
    {
        services.RemoveAll<ISmsSender>();
        services.AddSingleton(sender);
    }

    private sealed class ThrowingSender(Exception exception) : ISmsSender
    {
        public Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken) =>
            throw exception;
    }
}
