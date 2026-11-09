using Gameya.Application.Abstractions;
using Gameya.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Gameya.Infrastructure.Sms;

/// <summary>
/// Dev/E2E adapter: logs the message and stores it for the dev OTP endpoint. The single sanctioned
/// exception to "never log the code"; it is only registered in Development and E2E.
/// </summary>
public sealed partial class LoggingSmsSender(DevOtpStore store, ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken)
    {
        store.Record(to, message);
        LogSms(logger, to.Masked, message);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "DEV SMS to {Phone}: {Message}")]
    private static partial void LogSms(ILogger logger, string phone, string message);
}
