using Gameya.Application.Gameyas;
using Gameya.Domain.Gameyas;
using MediatR;

namespace Gameya.Api.Endpoints;

/// <summary>Joining through the invite code, and answering direct invites sent to the caller's phone.</summary>
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
        app.MapPost("/api/invite/{code}/join", async (string code, JoinBody body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RequestToJoinCommand(code, body.ToShares(), body.AcceptRules, body.AcceptNoMoney), ct);
            return Results.NoContent();
        });

        var mine = app.MapGroup("/api/me/invites");

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
