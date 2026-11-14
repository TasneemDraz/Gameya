using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>Pending join requests for the organizer, with the requesters' full phones.</summary>
[RequiresGameyaOrganizer]
public sealed record GetJoinRequestsQuery(Guid GameyaId) : IQuery<IReadOnlyList<JoinRequestDto>>, IGameyaRequest;

public sealed class GetJoinRequestsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetJoinRequestsQuery, IReadOnlyList<JoinRequestDto>>
{
    public async Task<IReadOnlyList<JoinRequestDto>> Handle(GetJoinRequestsQuery request, CancellationToken cancellationToken)
    {
        var viewerId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var row = await db.Gameyas.FirstOrDefaultAsync(new JoinRequestRowsSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);
        var cards = await UserCards.LoadAsync(db, row.Requests.Select(r => r.UserId), cancellationToken);

        return
        [
            .. row.Requests.Select(r =>
            {
                cards.TryGetValue(r.UserId, out var card);
                return new JoinRequestDto(
                    r.Id,
                    r.UserId,
                    card?.FullName,
                    card?.Phone is { } phone ? PhoneDisplay.For(viewerId, r.UserId, phone, true) : null,
                    r.SharePreferences,
                    r.AcceptedTermsVersion == row.TermsVersion);
            }),
        ];
    }
}
