using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;

namespace Gameya.Api.Infrastructure;

/// <summary>Serves the Angular production build from wwwroot on the same origin as the API, with Caddy's cache rules.</summary>
public static partial class SpaHosting
{
    private const string Immutable = "public, max-age=31536000, immutable";
    private const string NoCache = "no-cache";

    // Angular's production build hashes the file names of scripts and styles.
    [GeneratedRegex(@"^/[^/]+-[A-Za-z0-9]{8}\.(js|css)$")]
    private static partial Regex HashedAsset();

    private static StaticFileOptions Options() => new()
    {
        OnPrepareResponse = context =>
        {
            var path = context.Context.Request.Path.Value ?? string.Empty;
            context.Context.Response.Headers.CacheControl = HashedAsset().IsMatch(path) ? Immutable : NoCache;
        }
    };

    /// <summary>A missing wwwroot (Development without a build) is fine: nothing is found and the request moves on.</summary>
    public static IApplicationBuilder UseGameyaSpaStaticFiles(this IApplicationBuilder app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles(Options());
        // WebApplication adds UseRouting at the very start unless the app calls it. The catch-all fallback endpoint
        // would then be selected before this point and the static file middleware would skip every request.
        // Routing therefore starts here, after the static files and before the rate limiter and auth.
        return app.UseRouting();
    }

    /// <summary>Unmatched requests get index.html (when wwwroot has one), except under /api, /hubs and /health and paths whose last segment has a file extension, which stay real 404s.</summary>
    public static IEndpointRouteBuilder MapGameyaSpaFallback(this WebApplication app)
    {
        // Without a build there is no shell to serve; a catch-all endpoint would only answer 404 for everything.
        if (!app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists)
        {
            return app;
        }

        app.MapFallbackToFile("{*path:regex(^(?!api/|api$|hubs/|health$)(?!.*[.][^/]*$).*$)}", "index.html", Options());
        return app;
    }
}
