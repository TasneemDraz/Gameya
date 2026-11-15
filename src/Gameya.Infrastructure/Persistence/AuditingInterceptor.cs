using Gameya.Application.Abstractions;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gameya.Infrastructure.Persistence;

/// <summary>
/// Writes the audit columns of every <see cref="AuditableEntity"/>, and touches the owning gam'eya when only its
/// children change, so the root's UPDATE carries the aggregate's row-version check.
/// </summary>
public sealed class AuditingInterceptor(IClock clock, ICurrentUser? currentUser) : SaveChangesInterceptor
{
    private static readonly HashSet<Type> ChildTypes =
        [typeof(Membership), typeof(MemberShare), typeof(JoinRequest), typeof(JoinRequestShare), typeof(PendingInvite)];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;

        var now = clock.UtcNow;
        var actor = currentUser?.UserId;
        context.ChangeTracker.DetectChanges();
        var entries = context.ChangeTracker.Entries<AuditableEntity>().ToList();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(e => e.CreatedAt).CurrentValue = now;
                    entry.Property(e => e.CreatedByUserId).CurrentValue = actor;
                    break;
                case EntityState.Modified:
                    Touch(entry, now, actor);
                    break;
            }
        }

        foreach (var entry in entries.Where(e =>
                     e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                     && ChildTypes.Contains(e.Metadata.ClrType)))
        {
            // Without the tracked root there is no row-version check, so the change must not be saved.
            var root = FindRoot(entries, entry) ?? throw new InvalidOperationException(
                $"A {entry.Metadata.ClrType.Name} was changed without its GameyaCircle being tracked. "
                + "Aggregate children must be changed through a tracked GameyaCircle.");
            if (root.State is EntityState.Unchanged or EntityState.Modified)
                Touch(root, now, actor);
        }
    }

    private static void Touch(EntityEntry<AuditableEntity> entry, DateTimeOffset now, Guid? actor)
    {
        entry.Property(e => e.UpdatedAt).CurrentValue = now;
        entry.Property(e => e.UpdatedAt).IsModified = true;
        entry.Property(e => e.UpdatedByUserId).CurrentValue = actor;
        entry.Property(e => e.UpdatedByUserId).IsModified = true;

        // Created* is written once on insert; restoring the original value also drops it from the UPDATE.
        entry.Property(e => e.CreatedAt).CurrentValue = entry.Property(e => e.CreatedAt).OriginalValue;
        entry.Property(e => e.CreatedAt).IsModified = false;
        entry.Property(e => e.CreatedByUserId).CurrentValue = entry.Property(e => e.CreatedByUserId).OriginalValue;
        entry.Property(e => e.CreatedByUserId).IsModified = false;
    }

    /// <summary>Follows foreign keys between aggregate types (never to users) up to the tracked gam'eya.</summary>
    private static EntityEntry<AuditableEntity>? FindRoot(
        List<EntityEntry<AuditableEntity>> tracked, EntityEntry<AuditableEntity> entry)
    {
        var current = entry;
        while (current is not null && current.Entity is not GameyaCircle)
        {
            EntityEntry<AuditableEntity>? principal = null;
            foreach (var fk in current.Metadata.GetForeignKeys())
            {
                var principalType = fk.PrincipalEntityType.ClrType;
                if (principalType != typeof(GameyaCircle) && !ChildTypes.Contains(principalType)) continue;

                principal = FindTracked(tracked, current, fk);
                if (principal is not null) break;
            }

            current = principal;
        }

        return current;
    }

    private static EntityEntry<AuditableEntity>? FindTracked(
        List<EntityEntry<AuditableEntity>> tracked, EntityEntry<AuditableEntity> dependent, IForeignKey fk)
    {
        // A deleted row may have had its foreign key cleared already; its original value still names the parent.
        var values = fk.Properties
            .Select(p => dependent.State == EntityState.Deleted
                ? dependent.Property(p.Name).OriginalValue
                : dependent.Property(p.Name).CurrentValue)
            .ToArray();
        if (values.Any(v => v is null)) return null;

        return tracked.FirstOrDefault(e =>
            e.Metadata == fk.PrincipalEntityType
            && fk.PrincipalKey.Properties.Select(p => e.Property(p.Name).CurrentValue).SequenceEqual(values));
    }
}
