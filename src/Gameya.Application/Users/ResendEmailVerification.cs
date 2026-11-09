using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Users;

[RequiresAuth]
public sealed record ResendEmailVerificationCommand : ICommand<Unit>;

public sealed class ResendEmailVerificationHandler(
    IAppDbContext db, ICurrentUser currentUser, ITokenService tokens, ISecretHasher hasher, IClock clock,
    VerificationEmailSender emails)
    : IRequestHandler<ResendEmailVerificationCommand, Unit>
{
    public async Task<Unit> Handle(ResendEmailVerificationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var user = await db.Users.FirstOrDefaultAsync(new UserByIdSpec(userId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);

        var token = tokens.NewOpaqueToken();
        user.ResendEmailVerification(hasher.Hash(token), clock.UtcNow);

        await emails.SendAsync(user.Language, user.PendingEmail!, token, cancellationToken);
        return Unit.Value;
    }
}
