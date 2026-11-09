using System.Text.Json;
using Gameya.Domain.Common;

namespace Gameya.Domain.Activity;

public sealed class ActivityLogEntry
{
    private ActivityLogEntry()
    {
        ActorRole = null!;
        Action = null!;
        TargetType = null!;
        TargetId = null!;
        DetailsJson = null!;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public long Id { get; private set; }
    public Guid? GameyaId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string ActorRole { get; private set; }
    public string Action { get; private set; }
    public string TargetType { get; private set; }
    public string TargetId { get; private set; }
    public string DetailsJson { get; private set; }
    public string? Note { get; private set; }

    public static ActivityLogEntry From(IActivityEvent e, Guid? actorUserId, string actorRole, DateTimeOffset now) => new()
    {
        GameyaId = e.GameyaId,
        OccurredAt = now,
        ActorUserId = e.ActorUserIdOverride ?? actorUserId,
        ActorRole = actorRole,
        Action = e.Action,
        TargetType = e.TargetType,
        TargetId = e.TargetId,
        DetailsJson = e.Details is null ? "{}" : JsonSerializer.Serialize(e.Details, JsonOptions),
        Note = e.Note
    };
}
