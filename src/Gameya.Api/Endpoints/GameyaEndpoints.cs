using Gameya.Application.Common;
using Gameya.Application.Gameyas;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Gameya.Api.Endpoints;

public static class GameyaEndpoints
{
    /// <summary>
    /// The settings fields plus the organizer's own share count, as the create and edit forms send them. A missing
    /// frequency, payout timing, grace days or maximum shares takes the form's default; the order method has none.
    /// </summary>
    public sealed record GameyaSettingsBody(
        string? Name,
        string? Description,
        decimal ContributionAmount,
        int ShareCount,
        GameyaFrequency? Frequency,
        DateOnly StartDate,
        int? GraceDays,
        PayoutTiming? PayoutTiming,
        int? PayoutOffsetDays,
        OrderMethod? OrderMethod,
        int? MaxSharesPerMember,
        bool OrganizerParticipates,
        string? RulesText,
        int OrganizerShares)
    {
        public const int DefaultGraceDays = 3;
        public const int DefaultMaxSharesPerMember = 2;

        public GameyaSettings ToSettings() => new(
            Name ?? string.Empty,
            Description,
            ContributionAmount,
            ShareCount,
            Frequency ?? GameyaFrequency.Monthly,
            StartDate,
            GraceDays ?? DefaultGraceDays,
            PayoutTiming ?? Domain.Gameyas.PayoutTiming.AfterFullCollection,
            PayoutOffsetDays,
            OrderMethod ?? throw new ValidationFailedException(
                new Dictionary<string, string[]> { ["orderMethod"] = [ErrorCodes.VALIDATION_FAILED] }),
            MaxSharesPerMember ?? DefaultMaxSharesPerMember,
            OrganizerParticipates,
            RulesText);
    }

    public sealed record CancelBody(string? Reason);

    public static IEndpointRouteBuilder MapGameyaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/gameyas");

        group.MapGet("", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListMyGameyasQuery(), ct)));

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetGameyaQuery(id), ct)));

        group.MapGet("/{id:guid}/activity", async (Guid id, int? page, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetGameyaActivityQuery(id, page ?? 1), ct)));

        group.MapPost("", async (GameyaSettingsBody body, ISender sender, CancellationToken ct) =>
        {
            var id = await sender.Send(new CreateGameyaCommand(body.ToSettings(), body.OrganizerShares), ct);
            return Results.Created($"/api/gameyas/{id}", new { id });
        });

        group.MapPut("/{id:guid}", async (Guid id, GameyaSettingsBody body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new UpdateGameyaSettingsCommand(id, body.ToSettings(), body.OrganizerShares), ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/open", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new OpenForRecruitingCommand(id), ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/invite-code", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RegenerateInviteCodeCommand(id), ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/cancel", async (Guid id, CancelBody? body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new CancelGameyaCommand(id, body?.Reason), ct);
            return Results.NoContent();
        });

        MapMembership(group);
        MapJoinRequests(group);
        MapInvites(group);

        return app;
    }

    public sealed record ReasonBody(string? Reason);

    public sealed record InviteBody(string? Phone, int ShareCount);

    private static void MapMembership(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/members", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetMembersQuery(id), ct)));

        group.MapPost("/{id:guid}/leave", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new LeaveGameyaCommand(id), ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/reaccept", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ReacceptTermsCommand(id), ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}/members/{userId:guid}",
            async (Guid id, Guid userId, [FromBody] ReasonBody? body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new RemoveMemberCommand(id, userId, body?.Reason ?? string.Empty), ct);
                return Results.NoContent();
            });
    }

    private static void MapJoinRequests(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/join-requests", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetJoinRequestsQuery(id), ct)));

        group.MapPost("/{id:guid}/join-requests/{requestId:guid}/approve",
            async (Guid id, Guid requestId, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new ApproveJoinRequestCommand(id, requestId), ct);
                return Results.NoContent();
            });

        group.MapPost("/{id:guid}/join-requests/{requestId:guid}/reject",
            async (Guid id, Guid requestId, ReasonBody? body, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new RejectJoinRequestCommand(id, requestId, body?.Reason), ct);
                return Results.NoContent();
            });

        group.MapDelete("/{id:guid}/join-requests/mine", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new WithdrawJoinRequestCommand(id), ct);
            return Results.NoContent();
        });
    }

    private static void MapInvites(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/invites", async (Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPendingInvitesQuery(id), ct)));

        group.MapPost("/{id:guid}/invites", async (Guid id, InviteBody body, ISender sender, CancellationToken ct) =>
        {
            var inviteId = await sender.Send(new InviteByPhoneCommand(id, body.Phone ?? string.Empty, body.ShareCount), ct);
            return Results.Created($"/api/gameyas/{id}/invites/{inviteId}", new { id = inviteId });
        });

        group.MapDelete("/{id:guid}/invites/{inviteId:guid}",
            async (Guid id, Guid inviteId, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new RevokeInviteCommand(id, inviteId), ct);
                return Results.NoContent();
            });
    }
}
