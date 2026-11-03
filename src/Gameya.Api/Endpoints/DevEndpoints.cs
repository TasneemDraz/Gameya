using Gameya.Domain.Common;
using Gameya.Domain.Users;
using Gameya.Infrastructure.Sms;

namespace Gameya.Api.Endpoints;

/// <summary>Mapped only in Development and E2E. Lets tests and local developers read the last OTP code.</summary>
public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dev/otp/{phone}", (string phone, DevOtpStore store) =>
        {
            var code = PhoneNumber.TryParse(phone, out var parsed) ? store.TryGetCode(parsed!) : null;
            return code is null ? throw new DomainException(ErrorCodes.NOT_FOUND) : Results.Ok(new { code });
        });

        return app;
    }
}
