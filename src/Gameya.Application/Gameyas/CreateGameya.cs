using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using MediatR;

namespace Gameya.Application.Gameyas;

[RequiresAuth]
public sealed record CreateGameyaCommand(GameyaSettings Settings, int OrganizerShares) : ICommand<Guid>;

public sealed class CreateGameyaValidator : AbstractValidator<CreateGameyaCommand>
{
    public CreateGameyaValidator(IClock clock)
    {
        RuleFor(c => c.Settings).Custom(GameyaSettingsValidator.Into);
        RuleFor(c => c.Settings.StartDate)
            .Must(d => d >= CairoCalendar.ToCairoDate(clock.UtcNow))
            .WithErrorCode(ErrorCodes.START_DATE_IN_PAST)
            .OverridePropertyName("startDate")
            .When(c => c.Settings is not null);
        GameyaSettingsValidator.OrganizerShares(this, c => c.Settings, c => c.OrganizerShares);
    }
}

public sealed class CreateGameyaHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<CreateGameyaCommand, Guid>
{
    public Task<Guid> Handle(CreateGameyaCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var now = clock.UtcNow;

        var gameya = GameyaCircle.Create(
            Guid.CreateVersion7(), userId, request.Settings, request.OrganizerShares, CairoCalendar.ToCairoDate(now), now);
        db.Gameyas.Add(gameya);
        return Task.FromResult(gameya.Id);
    }
}
