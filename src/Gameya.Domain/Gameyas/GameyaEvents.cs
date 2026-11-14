using Gameya.Domain.Common;

namespace Gameya.Domain.Gameyas;

public sealed record GameyaCreated(Guid GameyaId, int ShareCount, int OrganizerShares) : IActivityEvent
{
    public string Action => "GAMEYA_CREATED";
    Guid? IActivityEvent.GameyaId => GameyaId;
    public string TargetType => "Gameya";
    public string TargetId => GameyaId.ToString();
    public object? Details => new { shareCount = ShareCount, organizerShares = OrganizerShares };
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}

public sealed record GameyaOpened(Guid GameyaId) : IActivityEvent
{
    public string Action => "GAMEYA_OPENED";
    Guid? IActivityEvent.GameyaId => GameyaId;
    public string TargetType => "Gameya";
    public string TargetId => GameyaId.ToString();
    public object? Details => null;
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}

public sealed record InviteCodeRegenerated(Guid GameyaId) : IActivityEvent
{
    public string Action => "INVITE_CODE_REGENERATED";
    Guid? IActivityEvent.GameyaId => GameyaId;
    public string TargetType => "Gameya";
    public string TargetId => GameyaId.ToString();
    public object? Details => null;
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}
