using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

[RequiresGameyaOrganizer]
public sealed record OpenForRecruitingCommand(Guid GameyaId) : ICommand<Unit>, IGameyaRequest;

public sealed class OpenForRecruitingHandler(IAppDbContext db, IInviteCodeGenerator codes, IClock clock)
    : IRequestHandler<OpenForRecruitingCommand, Unit>
{
    public async Task<Unit> Handle(OpenForRecruitingCommand request, CancellationToken cancellationToken)
    {
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);
        var code = await InviteCodes.NextFreeAsync(db, codes, cancellationToken);

        gameya.OpenForRecruiting(code, CairoCalendar.ToCairoDate(clock.UtcNow));
        return Unit.Value;
    }
}
