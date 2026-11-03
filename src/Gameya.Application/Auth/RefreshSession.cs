using Gameya.Application.Abstractions;
using Gameya.Application.Auth.Specifications;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Auth;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Auth;

[PublicAccess]
public sealed record RefreshSessionCommand(string RefreshToken) : ICommand<SessionResult>;

/// <summary>
/// REFRESH_TOKEN_INVALID for a rotated token presented inside the grace window. On the wire it is the same
/// 401 as any invalid token; the type only tells the endpoint not to clear a cookie that is already the successor.
/// </summary>
public sealed class RotatedWithinGraceException() : DomainException(ErrorCodes.REFRESH_TOKEN_INVALID);

public sealed class RefreshSessionHandler(
    IAppDbContext db, ISecretHasher hasher, SessionIssuer sessions, IClock clock)
    : IRequestHandler<RefreshSessionCommand, SessionResult>
{
    /// <summary>A just-rotated token presented again in this window is a concurrent tab, not theft.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

    public async Task<SessionResult> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var token = await db.RefreshTokens.FirstOrDefaultAsync(
                new RefreshTokenByHashSpec(hasher.Hash(request.RefreshToken)), cancellationToken)
            ?? throw new DomainException(ErrorCodes.REFRESH_TOKEN_INVALID);

        if (token.RevokedAt is not null)
        {
            // Revoked without a successor (logout or a revoked family): nothing new to learn.
            if (token.ReplacedByTokenId is null)
            {
                throw new DomainException(ErrorCodes.REFRESH_TOKEN_INVALID);
            }

            // A rotated token inside the grace window is a concurrent tab, not theft.
            if (token.WasRotatedWithin(Grace, now))
            {
                throw new RotatedWithinGraceException();
            }

            // Rotated token replayed after the window is a theft signal: kill the whole family. The flag makes the transaction behavior commit
            // the revocations despite the exception.
            var family = await db.RefreshTokens.ListAsync(new RefreshTokensByFamilySpec(token.FamilyId), cancellationToken);
            foreach (var t in family.Where(t => t.RevokedAt is null))
            {
                t.Revoke(now);
            }

            throw new DomainException(ErrorCodes.REFRESH_TOKEN_REUSED, commitStateChanges: true);
        }

        if (!token.IsActive(now))
        {
            throw new DomainException(ErrorCodes.REFRESH_TOKEN_INVALID);
        }

        var user = await db.Users.FirstOrDefaultAsync(new UserByIdSpec(token.UserId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.REFRESH_TOKEN_INVALID);

        var session = await sessions.IssueAsync(user, token.FamilyId, cancellationToken);
        var successor = db.RefreshTokens.Local.Single(t => t.TokenHash == hasher.Hash(session.RefreshToken));
        token.Revoke(now, successor.Id);
        return session;
    }
}
