using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

[RequiresGameyaOrganizer]
public sealed record CancelGameyaCommand(Guid GameyaId, string? Reason) : ICommand<Unit>, IGameyaRequest;

public sealed class CancelGameyaValidator : AbstractValidator<CancelGameyaCommand>
{
    public CancelGameyaValidator() =>
        // Measured as stored: the domain trims the reason and keeps a blank one as none.
        RuleFor(c => c.Reason).Must(r => (r?.Trim().Length ?? 0) <= 500)
            .WithErrorCode(ErrorCodes.VALIDATION_FAILED).OverridePropertyName("reason");
}

public sealed class CancelGameyaHandler(IAppDbContext db, IClock clock) : IRequestHandler<CancelGameyaCommand, Unit>
{
    public async Task<Unit> Handle(CancelGameyaCommand request, CancellationToken cancellationToken)
    {
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        gameya.Cancel(request.Reason, clock.UtcNow);
        return Unit.Value;
    }
}
