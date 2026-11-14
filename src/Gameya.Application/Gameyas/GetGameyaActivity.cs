using System.Text.Json;
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

public sealed class GetGameyaActivityHandler(IAppDbContext db) : IRequestHandler<GetGameyaActivityQuery, ActivityPageDto>
{
    public const int PageSize = 20;

    /// <summary>Keeps the row offset well inside an int.</summary>
    public const int MaxPage = 1_000_000;

    public async Task<ActivityPageDto> Handle(GetGameyaActivityQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.ActivityLog.ListAsync(
            new GameyaActivityPageSpec(request.GameyaId, request.Page, PageSize), cancellationToken);
        var page = rows.Take(PageSize).ToList();

        // Names only: entries never carry phones, and a deleted actor's name is already gone (null).
        var actors = await UserCards.LoadAsync(
            db, page.Where(r => r.ActorUserId is not null).Select(r => r.ActorUserId!.Value), cancellationToken);

        return new ActivityPageDto(
            [
                .. page.Select(r => new ActivityEntryDto(
                    r.OccurredAt,
                    actors.NameOf(r.ActorUserId),
                    r.Action,
                    ParseDetails(r.DetailsJson),
                    r.Note)),
            ],
            request.Page,
            rows.Count > PageSize);
    }

    private static JsonElement ParseDetails(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
