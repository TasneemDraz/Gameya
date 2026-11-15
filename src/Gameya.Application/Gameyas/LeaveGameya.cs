using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

[RequiresGameyaMember]
public sealed record LeaveGameyaCommand(Guid GameyaId) : ICommand<Unit>, IGameyaRequest;

public sealed class LeaveGameyaHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<LeaveGameyaCommand, Unit>
{
    public async Task<Unit> Handle(LeaveGameyaCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        gameya.Leave(userId);
        return Unit.Value;
    }
}
