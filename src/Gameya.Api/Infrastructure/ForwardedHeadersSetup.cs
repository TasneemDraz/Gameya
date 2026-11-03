using Microsoft.AspNetCore.HttpOverrides;

namespace Gameya.Api.Infrastructure;

/// <summary>
/// Rate limiting keys on the client IP, so behind Caddy the API must read it from X-Forwarded-For. Only the
/// networks listed in <c>ForwardedHeaders:KnownNetworks</c> (CIDR, the compose network in production) may set
/// those headers; from anywhere else they are ignored, so a client cannot pick its own rate-limit bucket.
/// With nothing configured no sender is trusted, which is the safe default for Development and tests.
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string Section = "ForwardedHeaders:KnownNetworks";

    public static IServiceCollection AddGameyaForwardedHeaders(this IServiceCollection services, IConfiguration configuration) =>
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Replace the defaults, which trust loopback.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var cidr in configuration.GetSection(Section).Get<string[]>() ?? [])
            {
                if (!System.Net.IPNetwork.TryParse(cidr, out var network))
                {
                    throw new InvalidOperationException($"{Section} contains '{cidr}', which is not a CIDR network.");
                }

                options.KnownIPNetworks.Add(network);
            }
        });
}
