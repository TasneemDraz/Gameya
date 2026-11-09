using Gameya.Domain.Activity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Gameya.Infrastructure.Persistence;

/// <summary>Second line of defence behind the database REVOKE: the activity log is append-only.</summary>
public sealed class ActivityLogImmutabilityInterceptor : SaveChangesInterceptor
{
    public static readonly ActivityLogImmutabilityInterceptor Instance = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Guard(DbContext? context)
    {
        if (context is null) return;

        var violated = context.ChangeTracker.Entries<ActivityLogEntry>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (violated)
            throw new InvalidOperationException("The activity log is append-only; entries cannot be modified or deleted.");
    }
}
