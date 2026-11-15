using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>The caller's own Pending requests to join, so they can withdraw or re-accept from home.</summary>
[RequiresAuth]
public sealed record ListMyJoinRequestsQuery : IQuery<IReadOnlyList<MyJoinRequestDto>>;

public sealed class ListMyJoinRequestsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListMyJoinRequestsQuery, IReadOnlyList<MyJoinRequestDto>>
{
    public async Task<IReadOnlyList<MyJoinRequestDto>> Handle(
        ListMyJoinRequestsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var rows = await db.Gameyas.ListAsync(new MyJoinRequestRowsSpec(userId), cancellationToken);

        return
        [
            .. rows.Select(r => new MyJoinRequestDto(
                r.GameyaId,
                r.Name,
                JoinRequestStatus.Pending,
                r.ContributionAmount,
                r.GameyaShareCount,
                r.ShareCount,
                r.AcceptedTermsVersion < r.TermsVersion)),
        ];
    }
}
