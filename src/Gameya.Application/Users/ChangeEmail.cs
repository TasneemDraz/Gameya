using System.Net.Mail;
using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Users;

[RequiresAuth]
public sealed record ChangeEmailCommand(string Email) : ICommand<Unit>;

public sealed class ChangeEmailValidator : AbstractValidator<ChangeEmailCommand>
{
    public const int MaxLength = 254;

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var parsed) && parsed.Address == value && parsed.Host.Contains('.');

    public ChangeEmailValidator()
    {
        // Null and empty are rejected first: the domain does not tolerate them.
        RuleFor(c => c.Email).NotEmpty().WithErrorCode("EMAIL_REQUIRED").DependentRules(() =>
        {
            RuleFor(c => c.Email).Must(e => e.Trim().Length <= MaxLength).WithErrorCode("EMAIL_TOO_LONG")
                .Must(e => IsEmail(e.Trim())).WithErrorCode("EMAIL_INVALID");
        });
    }
}

public sealed class ChangeEmailHandler(
    IAppDbContext db, ICurrentUser currentUser, ITokenService tokens, ISecretHasher hasher, IClock clock,
    VerificationEmailSender emails)
    : IRequestHandler<ChangeEmailCommand, Unit>
{
    public async Task<Unit> Handle(ChangeEmailCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var user = await db.Users.FirstOrDefaultAsync(new UserByIdSpec(userId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);

        var email = request.Email.Trim().ToLowerInvariant();
        var token = tokens.NewOpaqueToken();
        user.RequestEmailChange(email, hasher.Hash(token), clock.UtcNow);

        // Sent before commit: a delivery failure rolls back the token and the counted send.
        await emails.SendAsync(user.Language, email, token, cancellationToken);
        return Unit.Value;
    }
}
