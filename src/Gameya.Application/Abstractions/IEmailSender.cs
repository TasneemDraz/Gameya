namespace Gameya.Application.Abstractions;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    /// <summary>Delivery failure throws <c>DomainException(SMS_SEND_FAILED)</c> (the shared "message could not be sent" code).</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
