using Gameya.Api.Infrastructure;
using Gameya.Application.Users;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Gameya.Api.Endpoints;

public static class MeEndpoints
{
    /// <summary>Photos are capped at 5 MB; the extra 64 KB covers multipart framing.</summary>
    private const long PhotoRequestLimit = SetPhotoHandler.MaxBytes + 64 * 1024;

    public sealed record UpdateProfileBody(string? FullName, string? Language);

    public sealed record ChangeEmailBody(string? Email);

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me");

        group.MapGet("", async (ISender sender, CancellationToken ct) => Results.Ok(await sender.Send(new GetMeQuery(), ct)));

        group.MapPut("/profile", async (UpdateProfileBody body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new UpdateProfileCommand(body.FullName ?? "", body.Language ?? ""), ct);
            return Results.NoContent();
        });

        group.MapPut("/email", async (ChangeEmailBody body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ChangeEmailCommand(body.Email ?? ""), ct);
            return Results.Accepted();
        });

        group.MapPost("/email/resend", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ResendEmailVerificationCommand(), ct);
            return Results.Accepted();
        });

        group.MapPut("/photo", async (IFormFile file, ISender sender, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                var result = await sender.Send(new SetPhotoCommand(stream, file.Length), ct);
                return Results.Ok(new { result.PhotoUrl });
            })
            .WithMetadata(new RequestSizeLimitAttribute(PhotoRequestLimit))
            .DisableAntiforgery();

        group.MapDelete("/photo", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RemovePhotoCommand(), ct);
            return Results.NoContent();
        });

        group.MapDelete("", async (ISender sender, HttpContext http, CancellationToken ct) =>
        {
            await sender.Send(new DeleteAccountCommand(), ct);
            RefreshCookie.Clear(http.Response);
            return Results.NoContent();
        });

        return app;
    }
}
