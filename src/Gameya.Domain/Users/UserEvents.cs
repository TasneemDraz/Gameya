using Gameya.Domain.Common;

namespace Gameya.Domain.Users;

// Details never contain phone or email values.

public sealed record UserRegistered(Guid UserId, Language Language) : IActivityEvent
{
    public string Action => "USER_REGISTERED";
    public Guid? GameyaId => null;
    public string TargetType => "User";
    public string TargetId => UserId.ToString();
    public object? Details => new { language = Language.ToString() };
    public Guid? ActorUserIdOverride => UserId;
    public string? Note => null;
}

public sealed record ProfileSnapshot(string? FullName, string Language);

public sealed record ProfileUpdated(Guid UserId, ProfileSnapshot Old, ProfileSnapshot New) : IActivityEvent
{
    public string Action => "PROFILE_UPDATED";
    public Guid? GameyaId => null;
    public string TargetType => "User";
    public string TargetId => UserId.ToString();
    public object? Details => new
    {
        old = new { fullName = Old.FullName, language = Old.Language },
        @new = new { fullName = New.FullName, language = New.Language }
    };
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}

public sealed record EmailVerified(Guid UserId) : IActivityEvent
{
    public string Action => "EMAIL_VERIFIED";
    public Guid? GameyaId => null;
    public string TargetType => "User";
    public string TargetId => UserId.ToString();
    public object? Details => null;
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}

public sealed record LoggedOutAll(Guid UserId) : IActivityEvent
{
    public string Action => "LOGGED_OUT_ALL";
    public Guid? GameyaId => null;
    public string TargetType => "User";
    public string TargetId => UserId.ToString();
    public object? Details => null;
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}

public sealed record AccountDeleted(Guid UserId) : IActivityEvent
{
    public string Action => "ACCOUNT_DELETED";
    public Guid? GameyaId => null;
    public string TargetType => "User";
    public string TargetId => UserId.ToString();
    public object? Details => null;
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}
