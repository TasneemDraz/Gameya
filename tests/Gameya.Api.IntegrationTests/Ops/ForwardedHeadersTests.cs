using System.Net;
using Gameya.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Gameya.Api.IntegrationTests.Ops;

[Collection(DatabaseCollection.Name)]
public sealed class ForwardedHeadersTests(DatabaseFixture db)
{
    private const string TrustedPeer = "10.20.0.5";
    private const string UntrustedPeer = "203.0.113.9";

    /// <summary>Fakes the TCP peer (TestServer has none) and reports the client IP the pipeline ended up with.</summary>
    private sealed class PeerAndEchoFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nxt) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["X-Test-Peer"]!);
                return nxt();
            });
            next(app);
            app.Run(async context =>
                await context.Response.WriteAsync(context.Connection.RemoteIpAddress?.ToString() ?? "none"));
        };
    }

    private async Task<string> ClientIpSeenAsync(string peer, string forwardedFor)
    {
        using var root = new GameyaApiFactory(db);
        using var factory = root.WithWebHostBuilder(b =>
        {
            b.UseSetting("ForwardedHeaders:KnownNetworks:0", "10.20.0.0/16");
            b.ConfigureTestServices(s => s.AddTransient<IStartupFilter, PeerAndEchoFilter>());
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__client-ip");
        request.Headers.Add("X-Test-Peer", peer);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        var response = await client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Forwarded_for_from_the_trusted_network_sets_the_client_ip()
    {
        (await ClientIpSeenAsync(TrustedPeer, "198.51.100.7")).ShouldBe("198.51.100.7");
    }

    [Fact]
    public async Task Forwarded_for_from_an_untrusted_peer_is_ignored()
    {
        (await ClientIpSeenAsync(UntrustedPeer, "198.51.100.7")).ShouldBe(UntrustedPeer);
    }
}
