using Gameya.Domain.Common;

namespace Gameya.Domain.Gameyas;

public class Membership : AuditableEntity
{
    private readonly List<MemberShare> _shares = [];

    private Membership()
    {
    }

    public Guid Id { get; private set; }
    public Guid GameyaId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public int AcceptedTermsVersion { get; private set; }
    public MembershipStatus Status { get; private set; }
    public string? RemovalReason { get; private set; }
    public IReadOnlyList<MemberShare> Shares => _shares;

    internal static Membership Create(
        Guid gameyaId, Guid userId, int shareCount, int termsVersion, DateTimeOffset now)
    {
        var membership = new Membership
        {
            Id = Guid.CreateVersion7(),
            GameyaId = gameyaId,
            UserId = userId,
            JoinedAt = now,
            AcceptedTermsVersion = termsVersion,
            Status = MembershipStatus.Active,
        };
        for (var i = 0; i < shareCount; i++)
            membership._shares.Add(MemberShare.Create([]));
        return membership;
    }

    internal static Membership Create(
        Guid gameyaId, Guid userId, IReadOnlyList<IReadOnlyList<int>> preferences, int termsVersion, DateTimeOffset now)
    {
        var membership = Create(gameyaId, userId, 0, termsVersion, now);
        membership._shares.AddRange(preferences.Select(MemberShare.Create));
        return membership;
    }

    internal void AcceptTerms(int termsVersion) => AcceptedTermsVersion = termsVersion;

    internal void Leave() => Status = MembershipStatus.Left;

    internal void Remove(string reason)
    {
        Status = MembershipStatus.Removed;
        RemovalReason = reason;
    }

    internal void AddShares(int count)
    {
        for (var i = 0; i < count; i++)
            _shares.Add(MemberShare.Create([]));
    }

    internal void RemoveLastShares(int count) => _shares.RemoveRange(_shares.Count - count, count);
}
