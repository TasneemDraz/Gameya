using Gameya.Domain.Auth;

namespace Gameya.Domain.Tests;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Expires_after_30_days()
    {
        var t = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h", T0);
        t.ExpiresAt.ShouldBe(T0.AddDays(30));
        t.IsActive(T0.AddDays(30).AddSeconds(-1)).ShouldBeTrue();
        t.IsActive(T0.AddDays(30)).ShouldBeFalse();
    }

    [Fact]
    public void Revoked_token_is_not_active()
    {
        var t = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h", T0);
        t.Revoke(T0.AddMinutes(1));
        t.IsActive(T0.AddMinutes(2)).ShouldBeFalse();
        t.RevokedAt.ShouldBe(T0.AddMinutes(1));
    }

    [Fact]
    public void WasRotatedWithin_true_only_inside_window_and_only_when_replaced()
    {
        var window = TimeSpan.FromSeconds(10);
        var plain = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h", T0);
        plain.Revoke(T0);
        plain.WasRotatedWithin(window, T0.AddSeconds(1)).ShouldBeFalse();

        var rotated = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h", T0);
        var next = Guid.NewGuid();
        rotated.Revoke(T0, next);
        rotated.ReplacedByTokenId.ShouldBe(next);
        rotated.WasRotatedWithin(window, T0.AddSeconds(10)).ShouldBeTrue();
        rotated.WasRotatedWithin(window, T0.AddSeconds(11)).ShouldBeFalse();

        var fresh = RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "h", T0);
        fresh.WasRotatedWithin(window, T0).ShouldBeFalse();
    }
}
