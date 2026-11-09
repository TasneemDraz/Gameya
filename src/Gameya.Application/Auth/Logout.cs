using Gameya.Application.Abstractions;
using Gameya.Application.Auth.Specifications;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using MediatR;

namespace Gameya.Application.Auth;

[PublicAccess]
public sealed record LogoutCommand(string? RefreshToken) : ICommand<Unit>;

/// <summary>Revokes the presented token's family. Unknown, missing or already revoked tokens are a no-op.</summary>
public sealed class LogoutHandler(IAppDbContext db, ISecretHasher hasher, IClock clock)
    : IRequestHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return Unit.Value;
        }

        var token = await db.RefreshTokens.FirstOrDefaultAsync(
            new RefreshTokenByHashSpec(hasher.Hash(request.RefreshToken)), cancellationToken);
        if (token is null)
        {
            return Unit.Value;
        }

        var now = clock.UtcNow;
        var family = await db.RefreshTokens.ListAsync(new RefreshTokensByFamilySpec(token.FamilyId), cancellationToken);
        foreach (var t in family.Where(t => t.RevokedAt is null))
        {
            t.Revoke(now);
        }

        return Unit.Value;
    }
}
