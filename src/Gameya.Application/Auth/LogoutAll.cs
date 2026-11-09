using Gameya.Application.Abstractions;
using Gameya.Application.Auth.Specifications;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Auth;

[RequiresAuth]
public sealed record LogoutAllCommand : ICommand<Unit>;

public sealed class LogoutAllHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<LogoutAllCommand, Unit>
{
    public async Task<Unit> Handle(LogoutAllCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var user = await db.Users.FirstOrDefaultAsync(new UserByIdSpec(userId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);

        var now = clock.UtcNow;
        foreach (var t in await db.RefreshTokens.ListAsync(new ActiveRefreshTokensByUserSpec(userId), cancellationToken))
        {
            t.Revoke(now);
        }

        user.MarkLoggedOutEverywhere(now);
        return Unit.Value;
    }
}
