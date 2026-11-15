namespace Gameya.Domain.Common;

/// <summary>Audit columns are written by the persistence interceptor, never by domain code.</summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
}
