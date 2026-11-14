using Gameya.Domain.Common;
using Gameya.Domain.Users;

namespace Gameya.Domain.Gameyas;

public class PendingInvite : AuditableEntity
{
    private PendingInvite()
    {
    }

    public Guid Id { get; private set; }
    public Guid GameyaId { get; private set; }
    public PhoneNumber Phone { get; private set; } = null!;
    public int ShareCount { get; private set; }
    public Guid? UserId { get; private set; }
    public InviteStatus Status { get; private set; }

    internal static PendingInvite Create(Guid gameyaId, PhoneNumber phone, Guid? userId, int shareCount) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            GameyaId = gameyaId,
            Phone = phone,
            UserId = userId,
            ShareCount = shareCount,
            Status = InviteStatus.Pending,
        };

    internal void Accept(Guid userId)
    {
        Status = InviteStatus.Accepted;
        UserId = userId;
    }

    internal void Decline(Guid userId)
    {
        Status = InviteStatus.Declined;
        UserId = userId;
    }

    internal void Revoke() => Status = InviteStatus.Revoked;
}
