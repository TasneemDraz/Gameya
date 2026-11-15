using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>Invites a phone number, registered or not; the invite reserves its shares until answered.</summary>
[RequiresGameyaOrganizer]
public sealed record InviteByPhoneCommand(Guid GameyaId, string Phone, int ShareCount) : ICommand<Guid>, IGameyaRequest;

public sealed class InviteByPhoneValidator : AbstractValidator<InviteByPhoneCommand>
{
    public InviteByPhoneValidator()
    {
        RuleFor(c => c.Phone).Must(p => PhoneNumber.TryParse(p, out _)).WithErrorCode(ErrorCodes.INVALID_PHONE)
            .OverridePropertyName("phone");
        RuleFor(c => c.ShareCount).GreaterThanOrEqualTo(1).WithErrorCode(ErrorCodes.VALIDATION_FAILED)
            .OverridePropertyName("shareCount");
    }
}

public sealed class InviteByPhoneHandler(IAppDbContext db) : IRequestHandler<InviteByPhoneCommand, Guid>
{
    public async Task<Guid> Handle(InviteByPhoneCommand request, CancellationToken cancellationToken)
    {
        var phone = PhoneNumber.Parse(request.Phone);
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);
        var registered = await db.Users.FirstOrDefaultAsync(new ActiveUserByPhoneSpec(phone), cancellationToken);

        return gameya.InviteByPhone(phone, registered?.Id, request.ShareCount).Id;
    }
}
