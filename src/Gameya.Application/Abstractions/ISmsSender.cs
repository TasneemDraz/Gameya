using Gameya.Domain.Users;

namespace Gameya.Application.Abstractions;

public interface ISmsSender
{
    /// <summary>Delivery failure throws <c>DomainException(SMS_SEND_FAILED)</c>.</summary>
    Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken);
}
