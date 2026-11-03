namespace Gameya.Domain.Auth;

public class RefreshToken
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private RefreshToken()
    {
        TokenHash = null!;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    public static RefreshToken Issue(Guid userId, Guid familyId, string tokenHash, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        FamilyId = familyId,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now + Lifetime
    };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now, Guid? replacedBy = null)
    {
        RevokedAt = now;
        ReplacedByTokenId = replacedBy;
    }

    public bool WasRotatedWithin(TimeSpan window, DateTimeOffset now) =>
        ReplacedByTokenId is not null && RevokedAt is { } revoked && now - revoked <= window;
}
