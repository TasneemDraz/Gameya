using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>
/// Pending direct invites for the organizer, who typed the phones and so sees them in full. Nothing else: an
/// invitee's name, and whether the phone has an account at all, stay private until the invite is accepted.
/// </summary>
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

        // The invitee's user id is never used here, so the phone shows as the organizer typed it.
        return
        [
            .. rows.Select(r => new PendingInviteDto(
                r.Id, PhoneDisplay.For(viewerId, Guid.Empty, r.Phone, true), r.ShareCount)),
        ];
    }
}
