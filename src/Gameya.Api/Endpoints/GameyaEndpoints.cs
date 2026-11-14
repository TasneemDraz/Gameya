using Gameya.Application.Gameyas;
using Gameya.Domain.Gameyas;
using MediatR;

namespace Gameya.Api.Endpoints;

public static class GameyaEndpoints
{
    /// <summary>The settings fields plus the organizer's own share count, as the create and edit forms send them.</summary>
    public sealed record GameyaSettingsBody(
        string? Name,
        string? Description,
        decimal ContributionAmount,
        int ShareCount,
        GameyaFrequency? Frequency,
        DateOnly StartDate,
        int GraceDays,
        PayoutTiming? PayoutTiming,
        int? PayoutOffsetDays,
        OrderMethod OrderMethod,
        int MaxSharesPerMember,
        bool OrganizerParticipates,
        string? RulesText,
        int OrganizerShares)
    {
        public GameyaSettings ToSettings() => new(
            Name ?? string.Empty,
            Description,
            ContributionAmount,
            ShareCount,
            Frequency ?? GameyaFrequency.Monthly,
            StartDate,
            GraceDays,
            PayoutTiming ?? Domain.Gameyas.PayoutTiming.AfterFullCollection,
            PayoutOffsetDays,
            OrderMethod,
            MaxSharesPerMember,
            OrganizerParticipates,
            RulesText);
    }

    public sealed record CancelBody(string? Reason);

    public static IEndpointRouteBuilder MapGameyaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/gameyas");

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

        return app;
    }
}
