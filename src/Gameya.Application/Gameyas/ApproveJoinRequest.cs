using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

[RequiresGameyaOrganizer]
public sealed record ApproveJoinRequestCommand(Guid GameyaId, Guid RequestId) : ICommand<Unit>, IGameyaRequest;

public sealed class ApproveJoinRequestHandler(IAppDbContext db, IClock clock)
    : IRequestHandler<ApproveJoinRequestCommand, Unit>
{
    public async Task<Unit> Handle(ApproveJoinRequestCommand request, CancellationToken cancellationToken)
    {
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        gameya.ApproveJoinRequest(request.RequestId, clock.UtcNow);
        return Unit.Value;
    }
}
