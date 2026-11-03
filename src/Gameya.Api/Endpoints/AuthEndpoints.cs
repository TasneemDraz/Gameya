using Gameya.Api.Infrastructure;
using Gameya.Application.Abstractions;
using Gameya.Application.Auth;
using Gameya.Domain.Auth;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Gameya.Api.Endpoints;

public static class AuthEndpoints
{
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
            RefreshCookie.Write(http.Response, session.RefreshToken, clock.UtcNow + RefreshToken.Lifetime);
            return Results.Ok(new { session.AccessToken, session.AccessTokenExpiresAt, session.ProfileComplete });
        });

        // Session endpoints are not OTP-SMS endpoints, so they sit outside the "otp" rate limit.
        var session = app.MapGroup("/api/auth");

        session.MapPost("/refresh", async (
            ISender sender, IClock clock, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var result = await sender.Send(new RefreshSessionCommand(RefreshCookie.Read(http.Request) ?? ""), ct);
                RefreshCookie.Write(http.Response, result.RefreshToken, clock.UtcNow + RefreshToken.Lifetime);
                return Results.Ok(new { result.AccessToken, result.AccessTokenExpiresAt, result.ProfileComplete });
            }
            catch (Exception ex) when (ex is RotatedWithinGraceException or DbUpdateConcurrencyException)
            {
                // A concurrent tab already rotated this token; the cookie it holds is the live successor,
                // so it must not be cleared.
                await ProblemDetailsMapping.WriteAsync(
                    http, StatusCodes.Status401Unauthorized, ErrorCodes.REFRESH_TOKEN_INVALID, ct: ct);
                return Results.Empty;
            }
            catch (DomainException ex) when (ex.Code is ErrorCodes.REFRESH_TOKEN_INVALID or ErrorCodes.REFRESH_TOKEN_REUSED
                                                 or ErrorCodes.USER_BLOCKED)
            {
                // Written here rather than thrown: the exception handler resets response headers,
                // which would drop the cookie deletion.
                RefreshCookie.Clear(http.Response);
                await ProblemDetailsMapping.WriteAsync(http, ProblemDetailsMapping.StatusFor(ex.Code), ex.Code, ct: ct);
                return Results.Empty;
            }
        });

        session.MapPost("/logout", async (ISender sender, HttpContext http, CancellationToken ct) =>
        {
            await sender.Send(new LogoutCommand(RefreshCookie.Read(http.Request)), ct);
            RefreshCookie.Clear(http.Response);
            return Results.NoContent();
        });

        session.MapPost("/logout-all", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new LogoutAllCommand(), ct);
            return Results.NoContent();
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
