using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>Pending direct invites to the caller's own phone, including those sent before they signed up.</summary>
[RequiresAuth]
public sealed record ListMyInvitesQuery : IQuery<IReadOnlyList<MyInviteDto>>;

public sealed class ListMyInvitesHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListMyInvitesQuery, IReadOnlyList<MyInviteDto>>
{
    public async Task<IReadOnlyList<MyInviteDto>> Handle(ListMyInvitesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var me = await UserCards.LoadAsync(db, [userId], cancellationToken);
        if (!me.TryGetValue(userId, out var card) || card.Phone is null)
        {
            return [];
        }

        var rows = await db.Gameyas.ListAsync(new MyInviteRowsSpec(userId, card.Phone), cancellationToken);
        var organizers = await UserCards.LoadAsync(db, rows.Select(r => r.OrganizerId), cancellationToken);

        return
        [
            .. rows.Select(r => new MyInviteDto(
                r.InviteId,
                r.GameyaId,
                r.Name,
                r.ContributionAmount,
                r.ShareCount,
                r.ShareCount,
                r.InvitedShares,
                r.OrderMethod,
                organizers.NameOf(r.OrganizerId))),
        ];
    }
}
