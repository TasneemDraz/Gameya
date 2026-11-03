using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gameya.Application.Abstractions;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gameya.Infrastructure.Sms;

/// <summary>
/// SMS Gate for Android adapter. One attempt only (no retries) so a user never receives duplicate codes.
/// Logs the masked phone and the gateway message id, never the text.
/// </summary>
public sealed partial class SmsGateSmsSender(
    HttpClient http, IOptions<SmsGateOptions> options, ILogger<SmsGateSmsSender> logger) : ISmsSender
{
    public async Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken)
    {
        var config = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, config.BaseUrl.TrimEnd('/') + "/messages")
        {
            Content = JsonContent.Create(new
            {
                textMessage = new { text = message },
                phoneNumbers = new[] { to.E164 }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}")));

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogRejected(logger, to.Masked, (int)response.StatusCode);
                throw new DomainException(ErrorCodes.SMS_SEND_FAILED);
            }

            LogSent(logger, to.Masked, await ReadMessageId(response, cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogFailed(logger, to.Masked, ex.GetType().Name);
            throw new DomainException(ErrorCodes.SMS_SEND_FAILED);
        }
    }

    private static async Task<string?> ReadMessageId(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SMS handed to gateway for {Phone}, message id {MessageId}")]
    private static partial void LogSent(ILogger logger, string phone, string? messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMS gateway rejected message for {Phone} with status {Status}")]
    private static partial void LogRejected(ILogger logger, string phone, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMS gateway call failed for {Phone}: {Error}")]
    private static partial void LogFailed(ILogger logger, string phone, string error);
}
