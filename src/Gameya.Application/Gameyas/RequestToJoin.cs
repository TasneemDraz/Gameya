using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>Asks to join through the invite code. The requester is not a member yet, so the code is the key.</summary>
[RequiresAuth]
public sealed record RequestToJoinCommand(
    string Code, IReadOnlyList<ShareRequest> Shares, bool AcceptRules, bool AcceptNoMoney) : ICommand<Unit>;

public sealed class RequestToJoinValidator : AbstractValidator<RequestToJoinCommand>
{
    public RequestToJoinValidator() =>
        JoinTermsValidation.Add(this, c => c.Shares, c => c.AcceptRules, c => c.AcceptNoMoney);
}

public sealed class RequestToJoinHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<RequestToJoinCommand, Unit>
{
    public async Task<Unit> Handle(RequestToJoinCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        var gameya = await db.Gameyas.FirstOrDefaultAsync(new GameyaAggregateByInviteCodeSpec(code), cancellationToken)
            ?? throw new DomainException(ErrorCodes.INVITE_CODE_INVALID);

        gameya.RequestToJoin(userId, request.Shares);
        return Unit.Value;
    }
}
