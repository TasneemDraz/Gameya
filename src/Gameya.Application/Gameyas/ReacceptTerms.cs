using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>
/// Re-accepts changed terms. A Pending requester is not a member yet, so this is <c>[RequiresAuth]</c> and the
/// domain checks that the caller holds an Active membership or a Pending request (GAMEYA_NOT_FOUND otherwise).
/// </summary>
[RequiresAuth]
public sealed record ReacceptTermsCommand(Guid GameyaId) : ICommand<Unit>;

public sealed class ReacceptTermsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ReacceptTermsCommand, Unit>
{
    public async Task<Unit> Handle(ReacceptTermsCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        gameya.ReacceptTerms(userId);
        return Unit.Value;
    }
}
