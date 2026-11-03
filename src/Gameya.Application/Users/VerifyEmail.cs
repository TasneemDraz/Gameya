using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Users;

[PublicAccess]
public sealed record VerifyEmailCommand(string Token) : ICommand<Unit>;

public sealed class VerifyEmailValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailValidator() =>
        RuleFor(c => c.Token).NotEmpty().WithErrorCode("TOKEN_REQUIRED")
            .MaximumLength(200).WithErrorCode("TOKEN_TOO_LONG");
}

public sealed class VerifyEmailHandler(IAppDbContext db, ISecretHasher hasher, IClock clock)
    : IRequestHandler<VerifyEmailCommand, Unit>
{
    public async Task<Unit> Handle(VerifyEmailCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = hasher.Hash(request.Token);
        var user = await db.Users.FirstOrDefaultAsync(new UserByPendingEmailTokenHashSpec(tokenHash), cancellationToken)
            ?? throw new DomainException(ErrorCodes.EMAIL_TOKEN_INVALID);

        // Expiry is checked by the domain first; the ownership check follows. Either failure rolls back.
        user.VerifyEmail(tokenHash, clock.UtcNow);

        if (await db.Users.AnyAsync(new OtherUserWithVerifiedEmailSpec(user.Email!, user.Id), cancellationToken))
            throw new DomainException(ErrorCodes.EMAIL_IN_USE);

        return Unit.Value;
    }
}
