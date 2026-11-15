using Gameya.Api.Infrastructure;
using Gameya.Application.Gameyas;
using Gameya.Domain.Gameyas;
using MediatR;

namespace Gameya.Api.Endpoints;

/// <summary>
/// The public invite preview, joining through the code, the caller's own join requests and the caller's own direct
/// invites.
/// </summary>
public static class InviteEndpoints
{
    public sealed record ShareBody(IReadOnlyList<int>? Preferences);

    /// <summary>The join and accept-invite form: one entry per share, plus the two required checkboxes.</summary>
    public sealed record JoinBody(IReadOnlyList<ShareBody?>? Shares, bool AcceptRules, bool AcceptNoMoney)
    {
        public IReadOnlyList<ShareRequest> ToShares() =>
            [.. (Shares ?? []).Select(s => new ShareRequest(s?.Preferences ?? []))];
    }

    public static IEndpointRouteBuilder MapInviteEndpoints(this IEndpointRouteBuilder app)
    {
        // Public, so it is rate-limited per IP to keep codes from being enumerated.
        app.MapGet("/api/invite/{code}", async (string code, ISender sender, CancellationToken ct) =>
                Results.Ok(await sender.Send(new GetInvitePreviewQuery(code), ct)))
            .RequireRateLimiting(RateLimiting.InvitePreviewPolicy);

        // Joining answers INVITE_CODE_INVALID for an unknown code too, so it shares the preview's per-IP limit.
        app.MapPost("/api/invite/{code}/join", async (string code, JoinBody body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new RequestToJoinCommand(code, body.ToShares(), body.AcceptRules, body.AcceptNoMoney), ct);
                return Results.NoContent();
            })
            .RequireRateLimiting(RateLimiting.InvitePreviewPolicy);

        app.MapGet("/api/me/join-requests", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListMyJoinRequestsQuery(), ct)));

        var mine = app.MapGroup("/api/me/invites");

        mine.MapGet("", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListMyInvitesQuery(), ct)));

        mine.MapPost("/{inviteId:guid}/accept",
            async (Guid inviteId, JoinBody body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(
                    new AcceptInviteCommand(inviteId, body.ToShares(), body.AcceptRules, body.AcceptNoMoney), ct);
                return Results.NoContent();
            });

        mine.MapPost("/{inviteId:guid}/decline", async (Guid inviteId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeclineInviteCommand(inviteId), ct);
            return Results.NoContent();
        });

        return app;
    }
}
