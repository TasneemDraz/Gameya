using System.Security.Cryptography;
using System.Text;
using Gameya.Domain.Common;

namespace Gameya.Domain.Users;

public class User : Entity
{
    public static readonly TimeSpan EmailTokenLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan EmailSendWindow = TimeSpan.FromHours(1);
    public const int MaxEmailSendsPerWindow = 3;

    private readonly List<string> _roles = [];

    private User()
    {
    }

    public Guid Id { get; private set; }
    public PhoneNumber? Phone { get; private set; }
    public string? FullName { get; private set; }
    public bool ProfileComplete { get; private set; }
    public Language Language { get; private set; }
    public string? Email { get; private set; }
    public DateTimeOffset? EmailVerifiedAt { get; private set; }
    public string? PendingEmail { get; private set; }
    public string? PendingEmailTokenHash { get; private set; }
    public DateTimeOffset? PendingEmailExpiresAt { get; private set; }
    public string? PhotoKey { get; private set; }
    public bool IsBlocked { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt is not null;
    public IReadOnlyCollection<string> Roles => _roles;
    public List<DateTimeOffset> EmailSendTimes { get; private set; } = [];

    public static User Register(Guid id, PhoneNumber phone, Language language, DateTimeOffset now)
    {
        var user = new User { Id = id, Phone = phone, Language = language, CreatedAt = now };
        user.Raise(new UserRegistered(id, language));
        return user;
    }

    public void SetProfile(string fullName, Language language, DateTimeOffset now)
    {
        var name = (fullName ?? string.Empty).Trim();
        if (name.Length is < 2 or > 60)
            throw new DomainException(ErrorCodes.NAME_INVALID);

        var old = new ProfileSnapshot(FullName, Language.ToString());
        var changed = FullName != name || Language != language;

        FullName = name;
        Language = language;
        ProfileComplete = true;

        if (changed)
            Raise(new ProfileUpdated(Id, old, new ProfileSnapshot(name, language.ToString())));
    }

    public void SetPhoto(string? photoKey, DateTimeOffset now) => PhotoKey = photoKey;

    public void RequestEmailChange(string email, string tokenHash, DateTimeOffset now)
    {
        EnsureEmailSendAllowed(now);
        PendingEmail = email.Trim();
        StartToken(tokenHash, now);
    }

    public void ResendEmailVerification(string tokenHash, DateTimeOffset now)
    {
        if (PendingEmail is null)
            throw new DomainException(ErrorCodes.EMAIL_TOKEN_INVALID);

        EnsureEmailSendAllowed(now);
        StartToken(tokenHash, now);
    }

    public void VerifyEmail(string tokenHash, DateTimeOffset now)
    {
        if (PendingEmail is null || PendingEmailTokenHash is null
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(tokenHash), Encoding.UTF8.GetBytes(PendingEmailTokenHash)))
            throw new DomainException(ErrorCodes.EMAIL_TOKEN_INVALID);

        if (now > PendingEmailExpiresAt!.Value)
            throw new DomainException(ErrorCodes.EMAIL_TOKEN_EXPIRED);

        Email = PendingEmail;
        EmailVerifiedAt = now;
        ClearPendingEmail();
        Raise(new EmailVerified(Id));
    }

    public void Delete(DateTimeOffset now)
    {
        DeletedAt = now;
        Phone = null;
        FullName = null;
        Email = null;
        PhotoKey = null;
        ClearPendingEmail();
        EmailSendTimes.Clear();
        Raise(new AccountDeleted(Id));
    }

    public void Block() => IsBlocked = true;

    public void Unblock() => IsBlocked = false;

    public void MarkLoggedOutEverywhere(DateTimeOffset now) => Raise(new LoggedOutAll(Id));

    // Checks the limit without mutating anything, so a throw leaves state untouched.
    private void EnsureEmailSendAllowed(DateTimeOffset now)
    {
        var recent = EmailSendTimes.Where(t => now - t < EmailSendWindow).OrderBy(t => t).ToList();
        if (recent.Count >= MaxEmailSendsPerWindow)
            throw new DomainException(ErrorCodes.EMAIL_RESEND_LIMIT,
                new Dictionary<string, object?> { ["retryAt"] = recent[0] + EmailSendWindow });
    }

    private void StartToken(string tokenHash, DateTimeOffset now)
    {
        EmailSendTimes = EmailSendTimes.Where(t => now - t < EmailSendWindow).ToList();
        EmailSendTimes.Add(now);
        PendingEmailTokenHash = tokenHash;
        PendingEmailExpiresAt = now + EmailTokenLifetime;
    }

    private void ClearPendingEmail()
    {
        PendingEmail = null;
        PendingEmailTokenHash = null;
        PendingEmailExpiresAt = null;
    }
}
