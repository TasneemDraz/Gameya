using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using MediatR;

namespace Gameya.Application.Gameyas;

[RequiresGameyaOrganizer]
public sealed record UpdateGameyaSettingsCommand(Guid GameyaId, GameyaSettings Settings, int OrganizerShares)
    : ICommand<Unit>, IGameyaRequest;

/// <summary>The start date is not checked here: only a changed start date must be today or later (domain rule).</summary>
public sealed class UpdateGameyaSettingsValidator : AbstractValidator<UpdateGameyaSettingsCommand>
{
    public UpdateGameyaSettingsValidator()
    {
        RuleFor(c => c.Settings).Custom(GameyaSettingsValidator.Into);
        GameyaSettingsValidator.OrganizerShares(this, c => c.Settings, c => c.OrganizerShares);
    }
}

public sealed class UpdateGameyaSettingsHandler(IAppDbContext db, IClock clock)
    : IRequestHandler<UpdateGameyaSettingsCommand, Unit>
{
    public async Task<Unit> Handle(UpdateGameyaSettingsCommand request, CancellationToken cancellationToken)
    {
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);
        var now = clock.UtcNow;

        gameya.UpdateSettings(request.Settings, request.OrganizerShares, CairoCalendar.ToCairoDate(now), now);
        return Unit.Value;
    }
}
