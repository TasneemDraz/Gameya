using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>Active members, organizer first. Phones are full for the organizer (and one's own row), masked otherwise.</summary>
[RequiresGameyaMember]
public sealed record GetMembersQuery(Guid GameyaId) : IQuery<IReadOnlyList<MemberDto>>, IGameyaRequest;

public sealed class GetMembersHandler(IAppDbContext db, ICurrentUser currentUser, IFileStore fileStore)
    : IRequestHandler<GetMembersQuery, IReadOnlyList<MemberDto>>
{
    public async Task<IReadOnlyList<MemberDto>> Handle(GetMembersQuery request, CancellationToken cancellationToken)
    {
        var viewerId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var row = await db.Gameyas.FirstOrDefaultAsync(new MemberRowsSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        var viewerIsOrganizer = row.OrganizerId == viewerId;
        var cards = await UserCards.LoadAsync(db, row.Members.Select(m => m.UserId), cancellationToken);

        return
        [
            .. row.Members
                .OrderByDescending(m => m.UserId == row.OrganizerId)
                .Select(m =>
                {
                    cards.TryGetValue(m.UserId, out var card);
                    return new MemberDto(
                        m.UserId,
                        card?.FullName,
                        UserCards.PhotoUrl(fileStore, card),
                        card?.Phone is { } phone
                            ? PhoneDisplay.For(viewerId, m.UserId, phone, viewerIsOrganizer)
                            : null,
                        m.ShareCount,
                        m.UserId == row.OrganizerId,
                        m.AcceptedTermsVersion < row.TermsVersion);
                }),
        ];
    }
}
