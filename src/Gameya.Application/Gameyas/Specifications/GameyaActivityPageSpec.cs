using Ardalis.Specification;
using Gameya.Domain.Activity;

namespace Gameya.Application.Gameyas.Specifications;

public sealed record ActivityRow(
    DateTimeOffset OccurredAt, Guid? ActorUserId, string Action, string DetailsJson, string? Note);

/// <summary>
/// One page of a gam'eya's activity, newest first (the identity breaks ties within one command). It reads one row
/// past the page so the handler knows whether another page follows. For a member who is not the organizer, entries
/// about people who never joined are left out here, in SQL, so every page is still full.
/// </summary>
public sealed class GameyaActivityPageSpec : Specification<ActivityLogEntry, ActivityRow>
{
    /// <summary>What a non-member did, or was told, before ever joining.</summary>
    public static readonly string[] OrganizerOnlyActions =
        ["JOIN_REQUESTED", "JOIN_REQUEST_WITHDRAWN", "JOIN_REQUEST_REJECTED", "INVITE_DECLINED"];

    public GameyaActivityPageSpec(Guid gameyaId, int page, int pageSize, bool viewerIsOrganizer)
    {
        Query.Where(e => e.GameyaId == gameyaId);
        if (!viewerIsOrganizer)
        {
            // A requester re-accepting changed terms is a join-request entry too (a member's re-accept is kept).
            Query.Where(e => !OrganizerOnlyActions.Contains(e.Action)
                && !(e.Action == "TERMS_REACCEPTED" && e.TargetType == "JoinRequest"));
        }

        Query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(e => new ActivityRow(e.OccurredAt, e.ActorUserId, e.Action, e.DetailsJson, e.Note));
    }
}
