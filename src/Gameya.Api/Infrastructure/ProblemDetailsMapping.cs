using Gameya.Application.Common;
using Gameya.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gameya.Api.Infrastructure;

/// <summary>
/// Maps exceptions to RFC 7807 responses with an extension <c>code</c>. The server never returns
/// user-facing text, stack traces or exception messages.
/// </summary>
public sealed class ProblemDetailsMapping(ILogger<ProblemDetailsMapping> logger) : IExceptionHandler
{
    private const string UniqueViolation = "23505";

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        switch (exception)
        {
            case ValidationFailedException v:
                await WriteAsync(context, StatusCodes.Status400BadRequest, ErrorCodes.VALIDATION_FAILED,
                    errors: v.Errors, ct: ct);
                break;
            case DomainException d:
                await WriteAsync(context, StatusFor(d.Code), d.Code, d.Params, ct: ct);
                break;
            case BadHttpRequestException:
                await WriteAsync(context, StatusCodes.Status400BadRequest, ErrorCodes.VALIDATION_FAILED, ct: ct);
                break;
            case DbUpdateConcurrencyException:
            case DbUpdateException when IsOtpChallengePhoneConflict(exception):
                await WriteAsync(context, StatusCodes.Status409Conflict, ErrorCodes.CONCURRENCY_CONFLICT, ct: ct);
                break;
            default:
                logger.LogError(exception, "Unhandled exception");
                await WriteAsync(context, StatusCodes.Status500InternalServerError, "INTERNAL", ct: ct);
                break;
        }

        return true;
    }

    /// <summary>True for the unique-index violation raised when two requests create the same phone's challenge.</summary>
    public static bool IsOtpChallengePhoneConflict(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException { SqlState: UniqueViolation, TableName: "otp_challenges" } };

    public static int StatusFor(string code) => code switch
    {
        ErrorCodes.UNAUTHENTICATED or ErrorCodes.REFRESH_TOKEN_INVALID or ErrorCodes.REFRESH_TOKEN_REUSED =>
            StatusCodes.Status401Unauthorized,
        ErrorCodes.FORBIDDEN or ErrorCodes.USER_BLOCKED => StatusCodes.Status403Forbidden,
        ErrorCodes.NOT_FOUND => StatusCodes.Status404NotFound,
        ErrorCodes.OTP_LOCKED or ErrorCodes.OTP_RESEND_TOO_SOON or ErrorCodes.OTP_RESEND_LIMIT
            or ErrorCodes.EMAIL_RESEND_LIMIT => StatusCodes.Status429TooManyRequests,
        ErrorCodes.ACCOUNT_HAS_ACTIVE_GAMEYAS or ErrorCodes.EMAIL_IN_USE => StatusCodes.Status409Conflict,
        ErrorCodes.SMS_SEND_FAILED => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest
    };

    public static async Task WriteAsync(
        HttpContext context, int status, string code,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IDictionary<string, string[]>? errors = null,
        CancellationToken ct = default)
    {
        var problem = new ProblemDetails { Status = status };
        problem.Extensions["code"] = code;
        if (parameters is { Count: > 0 }) problem.Extensions["params"] = parameters;
        if (errors is { Count: > 0 }) problem.Extensions["errors"] = errors;

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", ct);
    }
}
