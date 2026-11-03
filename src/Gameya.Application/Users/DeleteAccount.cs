using Gameya.Application.Abstractions;
using Gameya.Application.Auth.Specifications;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Users;

[RequiresAuth]
public sealed record DeleteAccountCommand : ICommand<Unit>;

public sealed class DeleteAccountHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock, IFileStore fileStore,
    ITransactionCallbacks callbacks, IActiveParticipationChecker participation)
    : IRequestHandler<DeleteAccountCommand, Unit>
{
    public async Task<Unit> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var user = await db.Users.FirstOrDefaultAsync(new UserByIdSpec(userId), cancellationToken);

        // An access token outlives the account by up to its lifetime; a deleted user is simply unauthenticated.
        if (user is null || user.IsDeleted)
            throw new DomainException(ErrorCodes.UNAUTHENTICATED);

        if (await participation.HasRecruitingOrActiveGameyasAsync(userId, cancellationToken))
            throw new DomainException(ErrorCodes.ACCOUNT_HAS_ACTIVE_GAMEYAS);

        var now = clock.UtcNow;
        foreach (var t in await db.RefreshTokens.ListAsync(new ActiveRefreshTokensByUserSpec(userId), cancellationToken))
        {
            t.Revoke(now);
        }

        var photoKey = user.PhotoKey;
        user.Delete(now);
        if (photoKey is not null)
        {
            callbacks.OnCommitted(ct => fileStore.DeleteAsync(FileContainers.ProfilePhotos, photoKey, ct));
        }

        return Unit.Value;
    }
}
