using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

[RequiresGameyaOrganizer]
public sealed record RejectJoinRequestCommand(Guid GameyaId, Guid RequestId, string? Reason)
    : ICommand<Unit>, IGameyaRequest;

public sealed class RejectJoinRequestValidator : AbstractValidator<RejectJoinRequestCommand>
{
    public RejectJoinRequestValidator() =>
        // Measured as stored: the domain trims the reason and keeps a blank one as none.
        RuleFor(c => c.Reason).Must(r => (r?.Trim().Length ?? 0) <= 500)
            .WithErrorCode(ErrorCodes.VALIDATION_FAILED).OverridePropertyName("reason");
}

public sealed class RejectJoinRequestHandler(IAppDbContext db) : IRequestHandler<RejectJoinRequestCommand, Unit>
{
    public async Task<Unit> Handle(RejectJoinRequestCommand request, CancellationToken cancellationToken)
    {
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        gameya.RejectJoinRequest(request.RequestId, request.Reason);
        return Unit.Value;
    }
}
