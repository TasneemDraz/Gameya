using Gameya.Application.Abstractions;
using Gameya.Domain.Auth;
using Gameya.Domain.Common;
using Gameya.Domain.Users;

namespace Gameya.Application.Auth;

/// <summary>Issues an access token and a new refresh token. Shared by OTP login and refresh rotation.</summary>
public sealed class SessionIssuer(IAppDbContext db, ITokenService tokens, ISecretHasher hasher, IClock clock)
{
    public Task<SessionResult> IssueAsync(User user, Guid? familyId, CancellationToken cancellationToken)
    {
        if (user.IsBlocked)
        {
            throw new DomainException(ErrorCodes.USER_BLOCKED);
        }

        var now = clock.UtcNow;
        var refresh = tokens.NewOpaqueToken();
        db.RefreshTokens.Add(RefreshToken.Issue(user.Id, familyId ?? Guid.NewGuid(), hasher.Hash(refresh), now));
        var (access, expiresAt) = tokens.CreateAccessToken(user);

        return Task.FromResult(new SessionResult(access, expiresAt, user.ProfileComplete, refresh));
    }
}
