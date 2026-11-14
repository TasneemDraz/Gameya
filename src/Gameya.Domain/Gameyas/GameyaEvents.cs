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

public sealed record JoinRequested(Guid GameyaId, Guid RequestId, Guid UserId, int ShareCount) : IActivityEvent
{
    public string Action => "JOIN_REQUESTED";
    Guid? IActivityEvent.GameyaId => GameyaId;
    public string TargetType => "JoinRequest";
    public string TargetId => RequestId.ToString();
    public object? Details => new { userId = UserId, shareCount = ShareCount };
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}

public sealed record JoinRequestWithdrawn(Guid GameyaId, Guid RequestId) : IActivityEvent
{
    public string Action => "JOIN_REQUEST_WITHDRAWN";
    Guid? IActivityEvent.GameyaId => GameyaId;
    public string TargetType => "JoinRequest";
    public string TargetId => RequestId.ToString();
    public object? Details => null;
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}

public sealed record JoinRequestApproved(Guid GameyaId, Guid RequestId, Guid UserId, int ShareCount) : IActivityEvent
{
    public string Action => "JOIN_REQUEST_APPROVED";
    Guid? IActivityEvent.GameyaId => GameyaId;
    public string TargetType => "JoinRequest";
    public string TargetId => RequestId.ToString();
    public object? Details => new { userId = UserId, shareCount = ShareCount };
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}

public sealed record JoinRequestRejected(Guid GameyaId, Guid RequestId, string? Reason) : IActivityEvent
{
    public string Action => "JOIN_REQUEST_REJECTED";
    Guid? IActivityEvent.GameyaId => GameyaId;
    public string TargetType => "JoinRequest";
    public string TargetId => RequestId.ToString();
    public object? Details => null;
    public Guid? ActorUserIdOverride => null;
    public string? Note => Reason;
}

public sealed record MemberInvited(Guid GameyaId, Guid InviteId, int ShareCount, Guid? RegisteredUserId) : IActivityEvent
{
    public string Action => "MEMBER_INVITED";
    Guid? IActivityEvent.GameyaId => GameyaId;
    public string TargetType => "PendingInvite";
    public string TargetId => InviteId.ToString();
    public object? Details => new { inviteId = InviteId, shareCount = ShareCount, registeredUserId = RegisteredUserId };
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}

public sealed record InviteRevoked(Guid GameyaId, Guid InviteId) : IActivityEvent
{
    public string Action => "INVITE_REVOKED";
    Guid? IActivityEvent.GameyaId => GameyaId;
    public string TargetType => "PendingInvite";
    public string TargetId => InviteId.ToString();
    public object? Details => null;
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}

public sealed record InviteAccepted(Guid GameyaId, Guid InviteId, Guid UserId) : IActivityEvent
{
    public string Action => "INVITE_ACCEPTED";
    Guid? IActivityEvent.GameyaId => GameyaId;
    public string TargetType => "PendingInvite";
    public string TargetId => InviteId.ToString();
    public object? Details => new { inviteId = InviteId, userId = UserId };
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}

public sealed record InviteDeclined(Guid GameyaId, Guid InviteId, Guid UserId) : IActivityEvent
{
    public string Action => "INVITE_DECLINED";
    Guid? IActivityEvent.GameyaId => GameyaId;
    public string TargetType => "PendingInvite";
    public string TargetId => InviteId.ToString();
    public object? Details => new { inviteId = InviteId, userId = UserId };
    public Guid? ActorUserIdOverride => null;
    public string? Note => null;
}
