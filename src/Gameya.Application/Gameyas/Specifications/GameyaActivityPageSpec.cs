using Ardalis.Specification;
using Gameya.Domain.Activity;

namespace Gameya.Application.Gameyas.Specifications;

public sealed record ActivityRow(
    DateTimeOffset OccurredAt, Guid? ActorUserId, string Action, string DetailsJson, string? Note);

/// <summary>
/// One page of a gam'eya's activity, newest first (the identity breaks ties within one command). It reads one row
/// past the page so the handler knows whether another page follows.
/// </summary>
public sealed class GameyaActivityPageSpec : Specification<ActivityLogEntry, ActivityRow>
{
    public GameyaActivityPageSpec(Guid gameyaId, int page, int pageSize) =>
        Query
            .Where(e => e.GameyaId == gameyaId)
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(e => new ActivityRow(e.OccurredAt, e.ActorUserId, e.Action, e.DetailsJson, e.Note));
}
