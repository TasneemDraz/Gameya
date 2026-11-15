using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>Gam'eyas the caller organizes or is an Active member of: Recruiting, then Draft, then Cancelled, by name.</summary>
[RequiresAuth]
public sealed record ListMyGameyasQuery : IQuery<IReadOnlyList<MyGameyaDto>>;

public sealed class ListMyGameyasHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListMyGameyasQuery, IReadOnlyList<MyGameyaDto>>
{
    public async Task<IReadOnlyList<MyGameyaDto>> Handle(ListMyGameyasQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var rows = await db.Gameyas.ListAsync(new MyGameyaRowsSpec(userId), cancellationToken);

        // Statuses are stored as strings, so the business order is applied here rather than in SQL.
        return
        [
            .. rows
                .OrderBy(r => StatusRank(r.Status))
                .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => new MyGameyaDto(
                    r.Id,
                    r.Name,
                    r.Status,
                    r.OrganizerId == userId ? GameyaRole.Organizer : GameyaRole.Member,
                    r.ContributionAmount,
                    r.ShareCount,
                    r.MyShareCount,
                    r.MyAcceptedTermsVersion is { } accepted && accepted < r.TermsVersion)),
        ];
    }

    private static int StatusRank(GameyaStatus status) => status switch
    {
        GameyaStatus.Active => 0,
        GameyaStatus.Recruiting => 1,
        GameyaStatus.Draft => 2,
        GameyaStatus.Completed => 3,
        _ => 4,
    };
}
