using Gameya.Application.Files;
using MediatR;

namespace Gameya.Api.Endpoints;

public static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // The signed URL is the credential, so this endpoint takes no bearer token.
        app.MapGet("/api/files/{container}/{**key}", async (
            string container, string key, string? exp, string? sig,
            ISender sender, HttpContext http, CancellationToken ct) =>
        {
            // A missing or malformed exp is just another invalid link (FORBIDDEN), not a binding error.
            _ = long.TryParse(exp, out var expiresAt);
            var file = await sender.Send(new GetSignedFileQuery(container, key, expiresAt, sig ?? ""), ct);

            http.Response.Headers.CacheControl = "private, max-age=300";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.Stream(file.Content, file.ContentType);
        });

        return app;
    }
}
