using Gameya.Application.Abstractions;
using Gameya.Domain.Common;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Gameya.Infrastructure.Email;

/// <summary>
/// MailKit SMTP adapter used in every environment (Mailpit in dev and tests, a relay in staging and production).
/// One attempt only. Logs the message id and recipient count, never an address or the body.
/// </summary>
public sealed partial class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var config = options.Value;
        var messageId = MimeKit.Utils.MimeUtils.GenerateMessageId();
        var mime = new MimeMessage { MessageId = messageId };
        mime.From.Add(MailboxAddress.Parse(config.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient { Timeout = SmtpOptions.TimeoutMilliseconds };
            await client.ConnectAsync(
                config.Smtp.Host, config.Smtp.Port,
                config.Smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
            if (!string.IsNullOrEmpty(config.Smtp.Username))
                await client.AuthenticateAsync(config.Smtp.Username, config.Smtp.Password ?? "", cancellationToken);
            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            LogSent(logger, messageId, mime.To.Count);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Exception messages can carry the recipient, so only the type is logged.
            LogFailed(logger, messageId, ex.GetType().Name);
            throw new DomainException(ErrorCodes.SMS_SEND_FAILED);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {MessageId} handed to SMTP server ({Recipients} recipient(s))")]
    private static partial void LogSent(ILogger logger, string messageId, int recipients);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {MessageId} could not be sent: {Error}")]
    private static partial void LogFailed(ILogger logger, string messageId, string error);
}
