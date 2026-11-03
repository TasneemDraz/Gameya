using System.Security.Cryptography;
using System.Text;
using Gameya.Domain.Common;
using Gameya.Domain.Users;

namespace Gameya.Domain.Auth;

public class OtpChallenge : Entity
{
    private OtpChallenge()
    {
        Phone = null!;
    }

    private OtpChallenge(PhoneNumber phone) => Phone = phone;

    public PhoneNumber Phone { get; private set; }
    public string? CodeHash { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public int FailedAttempts { get; private set; }
    public int SendCount { get; private set; }
    public DateTimeOffset? WindowStartedAt { get; private set; }
    public DateTimeOffset? LastSentAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset ResendAvailableAt =>
        (LastSentAt ?? DateTimeOffset.MinValue) + OtpRules.ResendCooldown;

    public static OtpChallenge Create(PhoneNumber phone) => new(phone);

    public void RecordSend(string codeHash, DateTimeOffset now)
    {
        EnsureNotLocked(now);

        if (WindowStartedAt is { } start && now >= start + OtpRules.SendWindow)
            ResetWindow();

        if (LastSentAt is not null && now < ResendAvailableAt)
            throw new DomainException(ErrorCodes.OTP_RESEND_TOO_SOON,
                new Dictionary<string, object?> { ["retryAt"] = ResendAvailableAt });

        if (SendCount >= OtpRules.MaxSendsPerWindow)
            throw new DomainException(ErrorCodes.OTP_RESEND_LIMIT,
                new Dictionary<string, object?> { ["retryAt"] = WindowStartedAt!.Value + OtpRules.SendWindow });

        WindowStartedAt ??= now;
        SendCount++;
        LastSentAt = now;
        CodeHash = codeHash;
        ExpiresAt = now + OtpRules.CodeLifetime;
    }

    public void Verify(string submittedHash, DateTimeOffset now)
    {
        EnsureNotLocked(now);

        if (CodeHash is null)
            throw new DomainException(ErrorCodes.OTP_INVALID,
                new Dictionary<string, object?> { ["attemptsLeft"] = OtpRules.MaxFailedAttempts - FailedAttempts });

        if (now > ExpiresAt!.Value)
            throw new DomainException(ErrorCodes.OTP_EXPIRED);

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(submittedHash), Encoding.UTF8.GetBytes(CodeHash));

        if (!matches)
        {
            FailedAttempts++;
            if (FailedAttempts >= OtpRules.MaxFailedAttempts)
            {
                LockedUntil = now + OtpRules.LockDuration;
                throw new DomainException(ErrorCodes.OTP_LOCKED,
                    new Dictionary<string, object?> { ["lockedUntil"] = LockedUntil });
            }

            throw new DomainException(ErrorCodes.OTP_INVALID,
                new Dictionary<string, object?> { ["attemptsLeft"] = OtpRules.MaxFailedAttempts - FailedAttempts });
        }

        CodeHash = null;
        ExpiresAt = null;
        FailedAttempts = 0;
        ResetWindow();
    }

    private void EnsureNotLocked(DateTimeOffset now)
    {
        if (LockedUntil is not { } until) return;

        if (now < until)
            throw new DomainException(ErrorCodes.OTP_LOCKED,
                new Dictionary<string, object?> { ["lockedUntil"] = until });

        LockedUntil = null;
        FailedAttempts = 0;
        ResetWindow();
    }

    private void ResetWindow()
    {
        SendCount = 0;
        WindowStartedAt = null;
    }
}
