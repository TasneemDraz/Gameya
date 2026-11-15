using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

[RequiresGameyaOrganizer]
public sealed record RemoveMemberCommand(Guid GameyaId, Guid UserId, string Reason) : ICommand<Unit>, IGameyaRequest;

public sealed class RemoveMemberValidator : AbstractValidator<RemoveMemberCommand>
{
    public RemoveMemberValidator() =>
        RuleFor(c => c.Reason).Must(r => r?.Trim().Length is >= 5 and <= 500)
            .WithErrorCode(ErrorCodes.VALIDATION_FAILED).OverridePropertyName("reason");
}

public sealed class RemoveMemberHandler(IAppDbContext db) : IRequestHandler<RemoveMemberCommand, Unit>
{
    public async Task<Unit> Handle(RemoveMemberCommand request, CancellationToken cancellationToken)
    {
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        gameya.RemoveMember(request.UserId, request.Reason);
        return Unit.Value;
    }
}
