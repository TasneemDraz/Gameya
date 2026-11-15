using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>The gam'eya's activity log, newest first, 20 entries per page (pages start at 1).</summary>
[RequiresGameyaMember]
public sealed record GetGameyaActivityQuery(Guid GameyaId, int Page) : IQuery<ActivityPageDto>, IGameyaRequest;

public sealed class GetGameyaActivityValidator : AbstractValidator<GetGameyaActivityQuery>
{
    public GetGameyaActivityValidator() =>
        RuleFor(q => q.Page).InclusiveBetween(1, GetGameyaActivityHandler.MaxPage)
            .WithErrorCode(ErrorCodes.VALIDATION_FAILED).OverridePropertyName("page");
}

/// <summary>
/// The organizer sees every entry, but never who declined an invite: an invitee stays anonymous until they accept.
/// A member who is not the organizer sees the gam'eya's own history only. Entries about people who never joined
/// (JOIN_REQUESTED, JOIN_REQUEST_WITHDRAWN, JOIN_REQUEST_REJECTED, INVITE_DECLINED and a requester's
/// TERMS_REACCEPTED) are filtered out entirely, and notes are stripped except the cancellation reason, which the
/// organizer writes for the members. So the reason a member was removed, or a request declined, stays with the
/// organizer.
/// </summary>
public sealed class GetGameyaActivityHandler(IAppDbContext db, ICurrentUser currentUser, IGameyaAccess access)
    : IRequestHandler<GetGameyaActivityQuery, ActivityPageDto>
{
    public const int PageSize = 20;

    /// <summary>Keeps the row offset well inside an int.</summary>
    public const int MaxPage = 1_000_000;

    private const string DeclinedAction = "INVITE_DECLINED";
    private const string CancelledAction = "GAMEYA_CANCELLED";

    public async Task<ActivityPageDto> Handle(GetGameyaActivityQuery request, CancellationToken cancellationToken)
    {
        var viewerId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var role = await access.GetRoleAsync(request.GameyaId, viewerId, cancellationToken);
        var isOrganizer = role == GameyaRole.Organizer;
        var rows = await db.ActivityLog.ListAsync(
            new GameyaActivityPageSpec(request.GameyaId, request.Page, PageSize, isOrganizer), cancellationToken);
        var page = rows.Take(PageSize).ToList();

        // Names only: entries never carry phones, and a deleted actor's name is already gone (null).
        var actors = await UserCards.LoadAsync(
            db,
            page.Where(r => r.ActorUserId is not null && r.Action != DeclinedAction).Select(r => r.ActorUserId!.Value),
            cancellationToken);

        return new ActivityPageDto(
            [
                .. page.Select(r => new ActivityEntryDto(
                    r.OccurredAt,
                    r.Action == DeclinedAction ? null : actors.NameOf(r.ActorUserId),
                    r.Action,
                    ParseDetails(r.Action, r.DetailsJson),
                    isOrganizer || r.Action == CancelledAction ? r.Note : null)),
            ],
            request.Page,
            rows.Count > PageSize);
    }

    /// <summary>
    /// Entries written before invitees were kept anonymous still name the account in their details; those keys are
    /// dropped on the way out.
    /// </summary>
    private static JsonElement ParseDetails(string action, string json)
    {
        var hidden = action switch
        {
            "MEMBER_INVITED" => "registeredUserId",
            DeclinedAction => "userId",
            _ => null,
        };
        if (hidden is not null && JsonNode.Parse(json) is JsonObject node && node.Remove(hidden))
        {
            json = node.ToJsonString();
        }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
