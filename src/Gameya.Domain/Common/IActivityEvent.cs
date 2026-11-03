namespace Gameya.Domain.Common;

public interface IActivityEvent : IDomainEvent
{
    string Action { get; }
    Guid? GameyaId { get; }
    string TargetType { get; }
    string TargetId { get; }
    object? Details { get; }
    Guid? ActorUserIdOverride { get; }
    string? Note { get; }
}
