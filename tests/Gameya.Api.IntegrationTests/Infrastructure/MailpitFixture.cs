using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Gameya.Api.IntegrationTests.Infrastructure;

/// <summary>One Mailpit container per test collection; tests read delivered mail through its HTTP API.</summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    private const int SmtpContainerPort = 1025;
    private const int HttpContainerPort = 8025;

    private readonly IContainer _container = new ContainerBuilder("axllent/mailpit:v1.21")
        .WithPortBinding(SmtpContainerPort, true)
        .WithPortBinding(HttpContainerPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(HttpContainerPort).ForPath("/livez")))
        .Build();

    private HttpClient _http = null!;

    public string Host => _container.Hostname;
    public int SmtpPort => _container.GetMappedPublicPort(SmtpContainerPort);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpContainerPort)}") };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>Messages sent to the address, newest first. Each test uses its own addresses, so none interfere.</summary>
    public async Task<IReadOnlyList<MailpitMessage>> MessagesToAsync(string address)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("/api/v1/messages?limit=200"));
        var result = new List<MailpitMessage>();
        foreach (var m in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            if (!m.GetProperty("To").EnumerateArray().Any(t => t.GetProperty("Address").GetString() == address))
                continue;
            var id = m.GetProperty("ID").GetString()!;
            using var full = JsonDocument.Parse(await _http.GetStringAsync($"/api/v1/message/{id}"));
            result.Add(new MailpitMessage(
                m.GetProperty("Subject").GetString()!,
                full.RootElement.GetProperty("Text").GetString()!,
                full.RootElement.GetProperty("HTML").GetString()!));
        }

        return result;
    }

    /// <summary>The token of the newest verification link sent to the address.</summary>
    public async Task<string> LatestTokenAsync(string address)
    {
        var message = (await MessagesToAsync(address)).First();
        var match = System.Text.RegularExpressions.Regex.Match(message.Text, @"/verify-email\?token=([A-Za-z0-9_-]+)");
        match.Success.ShouldBeTrue("verification link not found in: " + message.Text);
        return match.Groups[1].Value;
    }
}

public sealed record MailpitMessage(string Subject, string Text, string Html);
