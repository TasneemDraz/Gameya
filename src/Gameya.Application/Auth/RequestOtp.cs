using System.Security.Cryptography;
using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Auth.Specifications;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Auth;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using MediatR;

namespace Gameya.Application.Auth;

public sealed record RequestOtpResult(DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt);

[PublicAccess]
public sealed record RequestOtpCommand(string Phone, Language Language) : ICommand<RequestOtpResult>;

public sealed class RequestOtpValidator : AbstractValidator<RequestOtpCommand>
{
    public RequestOtpValidator() =>
        RuleFor(c => c.Language).IsInEnum().WithErrorCode("INVALID_LANGUAGE");
}

public sealed class RequestOtpHandler(IAppDbContext db, ISecretHasher hasher, ISmsSender sms, IClock clock)
    : IRequestHandler<RequestOtpCommand, RequestOtpResult>
{
    public async Task<RequestOtpResult> Handle(RequestOtpCommand request, CancellationToken cancellationToken)
    {
        if (!PhoneNumber.TryParse(request.Phone, out var phone))
        {
            throw new DomainException(ErrorCodes.INVALID_PHONE);
        }

        var challenge = await db.OtpChallenges.FirstOrDefaultAsync(new OtpChallengeByPhoneSpec(phone!), cancellationToken);
        if (challenge is null)
        {
            challenge = OtpChallenge.Create(phone!);
            db.OtpChallenges.Add(challenge);
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        challenge.RecordSend(hasher.Hash(code), clock.UtcNow);

        // Sent before commit: a delivery failure throws and rolls the send back, so no resend is consumed.
        await sms.SendAsync(phone!, SmsTemplates.Otp(request.Language, code), cancellationToken);

        return new RequestOtpResult(challenge.ExpiresAt!.Value, challenge.ResendAvailableAt);
    }
}
