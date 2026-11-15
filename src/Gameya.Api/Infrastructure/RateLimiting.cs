using System.Threading.RateLimiting;
using Gameya.Domain.Common;

namespace Gameya.Api.Infrastructure;

public static class RateLimiting
{
    public const string OtpPolicy = "otp";
    public const string InvitePreviewPolicy = "invite-preview";

    /// <summary>
    /// Policy "otp": fixed window of 10 requests per 10 minutes per client IP. Policy "invite-preview": fixed window
    /// of 60 requests per minute per client IP, so invite codes cannot be enumerated quickly.
    /// </summary>
    public static IServiceCollection AddGameyaRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }

                await ProblemDetailsMapping.WriteAsync(
                    context.HttpContext, StatusCodes.Status429TooManyRequests, ErrorCodes.RATE_LIMITED, ct: ct);
            };
            options.AddPolicy(OtpPolicy, httpContext => PerClientIp(httpContext, 10, TimeSpan.FromMinutes(10)));
            options.AddPolicy(InvitePreviewPolicy, httpContext => PerClientIp(httpContext, 60, TimeSpan.FromMinutes(1)));
        });

    private static RateLimitPartition<string> PerClientIp(HttpContext httpContext, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0
            });
}
