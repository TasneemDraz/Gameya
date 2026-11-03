using Gameya.Application.Abstractions;
using Gameya.Domain.Activity;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Behaviors;

/// <summary>
/// Commands only. Drains activity events from tracked entities into the activity log. It sits inside the
/// transaction, so it also drains when the handler throws a CommitStateChanges DomainException, letting
/// the transaction behavior persist the entries together with the state change.
/// </summary>
public sealed class ActivityLogBehavior<TRequest, TResponse>(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICommandMarker)
        {
            return await next(cancellationToken);
        }

        TResponse response;
        try
        {
            response = await next(cancellationToken);
        }
        catch (DomainException ex) when (ex.CommitStateChanges)
        {
            Drain();
            throw;
        }

        Drain();
        return response;
    }

    private void Drain()
    {
        var now = clock.UtcNow;
        foreach (var entity in db.TrackedEntitiesWithEvents().ToList())
        {
            foreach (var activity in entity.DomainEvents.OfType<IActivityEvent>())
            {
                var actor = activity.ActorUserIdOverride ?? currentUser.UserId;
                db.ActivityLog.Add(ActivityLogEntry.From(activity, actor, actor is null ? "System" : "User", now));
            }

            entity.ClearDomainEvents();
        }
    }
}
