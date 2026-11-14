using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>Declines a direct invite sent to the caller's own phone, releasing its reserved shares.</summary>
[RequiresAuth]
public sealed record DeclineInviteCommand(Guid InviteId) : ICommand<Unit>;

public sealed class DeclineInviteHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DeclineInviteCommand, Unit>
{
    public async Task<Unit> Handle(DeclineInviteCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var phone = (await db.Users.FirstOrDefaultAsync(new UserByIdSpec(userId), cancellationToken))?.Phone
            ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var gameya = await db.Gameyas.FirstOrDefaultAsync(
                new GameyaAggregateByInviteIdSpec(request.InviteId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.INVITE_NOT_FOUND);

        gameya.DeclineInvite(request.InviteId, userId, phone);
        return Unit.Value;
    }
}
