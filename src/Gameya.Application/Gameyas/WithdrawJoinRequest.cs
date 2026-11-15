using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>
/// The requester is not a member, so this is not a gam'eya-scoped request: the domain finds the caller's own
/// Pending request and answers GAMEYA_NOT_FOUND when there is none.
/// </summary>
[RequiresAuth]
public sealed record WithdrawJoinRequestCommand(Guid GameyaId) : ICommand<Unit>;

public sealed class WithdrawJoinRequestHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<WithdrawJoinRequestCommand, Unit>
{
    public async Task<Unit> Handle(WithdrawJoinRequestCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        gameya.WithdrawJoinRequest(userId);
        return Unit.Value;
    }
}
