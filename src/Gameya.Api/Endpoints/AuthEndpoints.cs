using Gameya.Api.Infrastructure;
using Gameya.Application.Abstractions;
using Gameya.Application.Auth;
using Gameya.Domain.Auth;
using Gameya.Domain.Users;
using MediatR;

namespace Gameya.Api.Endpoints;

public static class AuthEndpoints
{
    public const string RefreshCookie = "gameya_rt";

    public sealed record RequestOtpBody(string? Phone, Language Language = Language.Ar);

    public sealed record VerifyOtpBody(string? Phone, string? Code);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").RequireRateLimiting(RateLimiting.OtpPolicy);

        group.MapPost("/otp/request", async (RequestOtpBody body, HttpContext http, CancellationToken ct) =>
        {
            var result = await SendRetryingOnChallengeRace(
                http, new RequestOtpCommand(body.Phone ?? "", body.Language), ct);
            return Results.Json(new { result.ExpiresAt, result.ResendAvailableAt },
                statusCode: StatusCodes.Status202Accepted);
        });

        group.MapPost("/otp/verify", async (
            VerifyOtpBody body, ISender sender, IClock clock, HttpContext http, CancellationToken ct) =>
        {
            var session = await sender.Send(new VerifyOtpCommand(body.Phone ?? "", body.Code ?? ""), ct);
            http.Response.Cookies.Append(RefreshCookie, session.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/auth",
                Expires = clock.UtcNow + RefreshToken.Lifetime,
                IsEssential = true
            });
            return Results.Ok(new { session.AccessToken, session.AccessTokenExpiresAt, session.ProfileComplete });
        });

        return app;
    }

    /// <summary>
    /// Two first-time requests for one phone race on the unique phone index. The loser retries once in a
    /// fresh scope (the failed scope's tracked state is poisoned) and normally lands on OTP_RESEND_TOO_SOON.
    /// A second failure surfaces as 409 through the exception handler.
    /// </summary>
    private static async Task<T> SendRetryingOnChallengeRace<T>(HttpContext http, IRequest<T> command, CancellationToken ct)
    {
        try
        {
            return await http.RequestServices.GetRequiredService<ISender>().Send(command, ct);
        }
        catch (Exception ex) when (ProblemDetailsMapping.IsOtpChallengePhoneConflict(ex))
        {
            using var scope = http.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, ct);
        }
    }
}
