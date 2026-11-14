using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>Pending direct invites for the organizer, who typed the phones and so sees them in full.</summary>
[RequiresGameyaOrganizer]
public sealed record GetPendingInvitesQuery(Guid GameyaId) : IQuery<IReadOnlyList<PendingInviteDto>>, IGameyaRequest;

public sealed class GetPendingInvitesHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetPendingInvitesQuery, IReadOnlyList<PendingInviteDto>>
{
    public async Task<IReadOnlyList<PendingInviteDto>> Handle(
        GetPendingInvitesQuery request, CancellationToken cancellationToken)
    {
        var viewerId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var rows = await db.PendingInvites.ListAsync(new PendingInviteRowsSpec(request.GameyaId), cancellationToken);
        var cards = await UserCards.LoadAsync(
            db, rows.Where(r => r.UserId is not null).Select(r => r.UserId!.Value), cancellationToken);

        return
        [
            .. rows.Select(r => new PendingInviteDto(
                r.Id,
                PhoneDisplay.For(viewerId, r.UserId ?? Guid.Empty, r.Phone, true),
                r.ShareCount,
                cards.NameOf(r.UserId))),
        ];
    }
}
