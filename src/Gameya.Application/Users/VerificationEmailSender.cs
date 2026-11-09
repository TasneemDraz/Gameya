using Gameya.Application.Abstractions;
using Gameya.Domain.Users;
using Microsoft.Extensions.Configuration;

namespace Gameya.Application.Users;

/// <summary>Builds the verification link from <c>App:WebBaseUrl</c> and sends it in the user's language.</summary>
public sealed class VerificationEmailSender(IEmailSender sender, IConfiguration configuration)
{
    public Task SendAsync(Language language, string to, string token, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["App:WebBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("App:WebBaseUrl is not configured.");

        var link = $"{baseUrl.TrimEnd('/')}/verify-email?token={token}";
        return sender.SendAsync(EmailTemplates.VerifyEmail(language, to, link), cancellationToken);
    }
}
