using System.Threading.RateLimiting;
using Gameya.Domain.Common;

namespace Gameya.Api.Infrastructure;

public static class RateLimiting
{
    public const string OtpPolicy = "otp";

    /// <summary>Policy "otp": fixed window of 10 requests per 10 minutes per client IP.</summary>
    public static IServiceCollection AddOtpRateLimiting(this IServiceCollection services) =>
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
            options.AddPolicy(OtpPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0
                    }));
        });
}
