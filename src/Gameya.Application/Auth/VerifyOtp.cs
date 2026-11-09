using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Auth.Specifications;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using MediatR;

namespace Gameya.Application.Auth;

[PublicAccess]
public sealed record VerifyOtpCommand(string Phone, string Code) : ICommand<SessionResult>;

public sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpValidator() =>
        RuleFor(c => c.Code).Matches("^[0-9]{6}$").WithErrorCode(ErrorCodes.OTP_INVALID);
}

public sealed class VerifyOtpHandler(
    IAppDbContext db, ISecretHasher hasher, SessionIssuer sessions, IClock clock)
    : IRequestHandler<VerifyOtpCommand, SessionResult>
{
    public async Task<SessionResult> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
    {
        if (!PhoneNumber.TryParse(request.Phone, out var phone))
        {
            throw new DomainException(ErrorCodes.INVALID_PHONE);
        }

        var now = clock.UtcNow;
        var challenge = await db.OtpChallenges.FirstOrDefaultAsync(new OtpChallengeByPhoneSpec(phone!), cancellationToken)
            ?? throw new DomainException(ErrorCodes.OTP_INVALID);
        challenge.Verify(hasher.Hash(request.Code), now);

        var user = await db.Users.FirstOrDefaultAsync(new ActiveUserByPhoneSpec(phone!), cancellationToken);
        if (user is null)
        {
            user = User.Register(Guid.CreateVersion7(), phone!, Language.Ar, now);
            db.Users.Add(user);
        }

        return await sessions.IssueAsync(user, null, cancellationToken);
    }
}
