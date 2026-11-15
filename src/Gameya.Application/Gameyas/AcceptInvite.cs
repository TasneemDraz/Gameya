using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>Accepts a direct invite sent to the caller's own phone (read from their user row).</summary>
[RequiresAuth]
public sealed record AcceptInviteCommand(
    Guid InviteId, IReadOnlyList<ShareRequest> Shares, bool AcceptRules, bool AcceptNoMoney) : ICommand<Unit>;

public sealed class AcceptInviteValidator : AbstractValidator<AcceptInviteCommand>
{
    public AcceptInviteValidator() =>
        JoinTermsValidation.Add(this, c => c.Shares, c => c.AcceptRules, c => c.AcceptNoMoney);
}

public sealed class AcceptInviteHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<AcceptInviteCommand, Unit>
{
    public async Task<Unit> Handle(AcceptInviteCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var phone = (await db.Users.FirstOrDefaultAsync(new UserByIdSpec(userId), cancellationToken))?.Phone
            ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var gameya = await db.Gameyas.FirstOrDefaultAsync(
                new GameyaAggregateByInviteIdSpec(request.InviteId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.INVITE_NOT_FOUND);

        gameya.AcceptInvite(request.InviteId, userId, phone, request.Shares, clock.UtcNow);
        return Unit.Value;
    }
}
