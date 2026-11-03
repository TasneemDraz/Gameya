using Gameya.Domain.Auth;
using Gameya.Domain.Common;
using Gameya.Domain.Users;

namespace Gameya.Domain.Tests;

public class OtpChallengeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static OtpChallenge NewChallenge() => OtpChallenge.Create(PhoneNumber.Parse("01012345678"));

    private static DomainException Thrown(Action act, string code)
    {
        var ex = Should.Throw<DomainException>(act);
        ex.Code.ShouldBe(code);
        return ex;
    }

    [Fact]
    public void First_send_sets_code_and_expiry()
    {
        var c = NewChallenge();
        c.RecordSend("h1", T0);
        c.CodeHash.ShouldBe("h1");
        c.ExpiresAt.ShouldBe(T0.AddMinutes(5));
        c.SendCount.ShouldBe(1);
        c.WindowStartedAt.ShouldBe(T0);
        c.LastSentAt.ShouldBe(T0);
        c.ResendAvailableAt.ShouldBe(T0.AddSeconds(60));
    }

    [Fact]
    public void Resend_before_60s_throws_RESEND_TOO_SOON()
    {
        var c = NewChallenge();
        c.RecordSend("h1", T0);
        var ex = Thrown(() => c.RecordSend("h2", T0.AddSeconds(59)), ErrorCodes.OTP_RESEND_TOO_SOON);
        ex.Params!["retryAt"].ShouldBe(T0.AddSeconds(60));
    }

    [Fact]
    public void Fifth_send_in_window_throws_RESEND_LIMIT()
    {
        var c = NewChallenge();
        for (var i = 0; i < 4; i++) c.RecordSend("h" + i, T0.AddMinutes(i));
        var ex = Thrown(() => c.RecordSend("h5", T0.AddMinutes(4)), ErrorCodes.OTP_RESEND_LIMIT);
        ex.Params!["retryAt"].ShouldBe(T0.AddMinutes(15));
    }

    [Fact]
    public void Window_resets_after_15_minutes()
    {
        var c = NewChallenge();
        for (var i = 0; i < 4; i++) c.RecordSend("h" + i, T0.AddMinutes(i));
        c.RecordSend("hn", T0.AddMinutes(15));
        c.SendCount.ShouldBe(1);
        c.WindowStartedAt.ShouldBe(T0.AddMinutes(15));
    }

    [Fact]
    public void Correct_code_verifies_and_consumes()
    {
        var c = NewChallenge();
        c.RecordSend("h1", T0);
        c.Verify("h1", T0.AddSeconds(10));
        c.CodeHash.ShouldBeNull();
        Thrown(() => c.Verify("h1", T0.AddSeconds(20)), ErrorCodes.OTP_INVALID);
    }

    [Fact]
    public void Expired_code_throws_EXPIRED_and_does_not_count_attempt()
    {
        var c = NewChallenge();
        c.RecordSend("h1", T0);
        Thrown(() => c.Verify("h1", T0.AddMinutes(5).AddSeconds(1)), ErrorCodes.OTP_EXPIRED);
        c.FailedAttempts.ShouldBe(0);
    }

    [Fact]
    public void Wrong_code_reports_attempts_left()
    {
        var c = NewChallenge();
        c.RecordSend("h1", T0);
        var ex = Thrown(() => c.Verify("bad", T0.AddSeconds(5)), ErrorCodes.OTP_INVALID);
        ex.Params!["attemptsLeft"].ShouldBe(4);
        c.FailedAttempts.ShouldBe(1);
    }

    [Fact]
    public void Fifth_wrong_attempt_locks_for_15_minutes()
    {
        var c = NewChallenge();
        c.RecordSend("h1", T0);
        var now = T0.AddSeconds(5);
        for (var i = 0; i < 4; i++) Thrown(() => c.Verify("bad", now), ErrorCodes.OTP_INVALID);
        var ex = Thrown(() => c.Verify("bad", now), ErrorCodes.OTP_LOCKED);
        ex.Params!["lockedUntil"].ShouldBe(now.AddMinutes(15));
        Thrown(() => c.RecordSend("h2", now.AddMinutes(14)), ErrorCodes.OTP_LOCKED);
        Thrown(() => c.Verify("h1", now.AddMinutes(14)), ErrorCodes.OTP_LOCKED);
    }

    [Fact]
    public void Resend_does_not_reset_failed_attempts()
    {
        var c = NewChallenge();
        c.RecordSend("h1", T0);
        for (var i = 0; i < 4; i++) Thrown(() => c.Verify("bad", T0.AddSeconds(5)), ErrorCodes.OTP_INVALID);
        c.RecordSend("h2", T0.AddSeconds(61));
        c.FailedAttempts.ShouldBe(4);
        Thrown(() => c.Verify("bad", T0.AddSeconds(62)), ErrorCodes.OTP_LOCKED);
    }

    [Fact]
    public void After_lock_expires_attempts_and_window_reset()
    {
        var c = NewChallenge();
        c.RecordSend("h1", T0);
        var now = T0.AddSeconds(5);
        for (var i = 0; i < 5; i++) Should.Throw<DomainException>(() => c.Verify("bad", now));
        var lockedUntil = c.LockedUntil!.Value;
        c.RecordSend("h2", lockedUntil);
        c.FailedAttempts.ShouldBe(0);
        c.LockedUntil.ShouldBeNull();
        c.SendCount.ShouldBe(1);
    }

    [Fact]
    public void Successful_verify_resets_attempts_and_window()
    {
        var c = NewChallenge();
        c.RecordSend("h1", T0);
        Should.Throw<DomainException>(() => c.Verify("bad", T0.AddSeconds(5)));
        c.Verify("h1", T0.AddSeconds(10));
        c.FailedAttempts.ShouldBe(0);
        c.SendCount.ShouldBe(0);
        c.WindowStartedAt.ShouldBeNull();
    }

    [Fact]
    public void CommitStateChanges_flag_is_set_only_for_counted_attempt_and_new_lock()
    {
        var c = NewChallenge();
        var fresh = NewChallenge();
        Thrown(() => fresh.Verify("x", T0), ErrorCodes.OTP_INVALID).CommitStateChanges.ShouldBeFalse();

        c.RecordSend("h1", T0);
        Thrown(() => c.RecordSend("h2", T0.AddSeconds(5)), ErrorCodes.OTP_RESEND_TOO_SOON)
            .CommitStateChanges.ShouldBeFalse();

        var now = T0.AddSeconds(10);
        Thrown(() => c.Verify("bad", now), ErrorCodes.OTP_INVALID).CommitStateChanges.ShouldBeTrue();
        for (var i = 0; i < 3; i++) Should.Throw<DomainException>(() => c.Verify("bad", now));
        Thrown(() => c.Verify("bad", now), ErrorCodes.OTP_LOCKED).CommitStateChanges.ShouldBeTrue();
        Thrown(() => c.Verify("bad", now), ErrorCodes.OTP_LOCKED).CommitStateChanges.ShouldBeFalse();
        Thrown(() => c.RecordSend("h3", now), ErrorCodes.OTP_LOCKED).CommitStateChanges.ShouldBeFalse();

        var e = NewChallenge();
        e.RecordSend("h1", T0);
        Thrown(() => e.Verify("h1", T0.AddMinutes(6)), ErrorCodes.OTP_EXPIRED).CommitStateChanges.ShouldBeFalse();
    }
}
