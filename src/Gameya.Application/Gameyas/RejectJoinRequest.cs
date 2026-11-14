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
        RuleFor(c => c.Reason).MaximumLength(500).WithErrorCode(ErrorCodes.VALIDATION_FAILED).OverridePropertyName("reason");
}

public sealed class RejectJoinRequestHandler(IAppDbContext db) : IRequestHandler<RejectJoinRequestCommand, Unit>
{
    public async Task<Unit> Handle(RejectJoinRequestCommand request, CancellationToken cancellationToken)
    {
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        gameya.RejectJoinRequest(
            request.RequestId, string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim());
        return Unit.Value;
    }
}
