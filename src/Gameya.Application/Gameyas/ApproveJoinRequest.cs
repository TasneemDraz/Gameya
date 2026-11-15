using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

[RequiresGameyaOrganizer]
public sealed record ApproveJoinRequestCommand(Guid GameyaId, Guid RequestId) : ICommand<Unit>, IGameyaRequest;

/// <summary>
/// Passes the requester's phone so the domain revokes an invite sent to it before signup, and refuses a request whose
/// requester has since deleted their account.
/// </summary>
public sealed class ApproveJoinRequestHandler(IAppDbContext db, IClock clock)
    : IRequestHandler<ApproveJoinRequestCommand, Unit>
{
    public async Task<Unit> Handle(ApproveJoinRequestCommand request, CancellationToken cancellationToken)
    {
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        // An unknown request id is left to the domain, which answers GAMEYA_NOT_FOUND.
        var requesterId = gameya.JoinRequests.FirstOrDefault(r => r.Id == request.RequestId)?.UserId;
        var requester = requesterId is { } id
            ? await db.Users.FirstOrDefaultAsync(new UserByIdSpec(id), cancellationToken)
            : null;

        gameya.ApproveJoinRequest(
            request.RequestId, clock.UtcNow, requester?.Phone, requesterDeleted: requesterId is not null && requester is null);
        return Unit.Value;
    }
}
