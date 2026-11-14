using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

[RequiresGameyaOrganizer]
public sealed record RevokeInviteCommand(Guid GameyaId, Guid InviteId) : ICommand<Unit>, IGameyaRequest;

public sealed class RevokeInviteHandler(IAppDbContext db) : IRequestHandler<RevokeInviteCommand, Unit>
{
    public async Task<Unit> Handle(RevokeInviteCommand request, CancellationToken cancellationToken)
    {
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        gameya.RevokeInvite(request.InviteId);
        return Unit.Value;
    }
}
