using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Gameyas;

[RequiresGameyaOrganizer]
public sealed record RegenerateInviteCodeCommand(Guid GameyaId) : ICommand<Unit>, IGameyaRequest;

public sealed class RegenerateInviteCodeHandler(IAppDbContext db, IInviteCodeGenerator codes)
    : IRequestHandler<RegenerateInviteCodeCommand, Unit>
{
    public async Task<Unit> Handle(RegenerateInviteCodeCommand request, CancellationToken cancellationToken)
    {
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByIdSpec(request.GameyaId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);
        var code = await InviteCodes.NextFreeAsync(db, codes, cancellationToken);

        gameya.RegenerateInviteCode(code);
        return Unit.Value;
    }
}
