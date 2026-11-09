using System.Runtime.ExceptionServices;
using Gameya.Application.Abstractions;
using Gameya.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Gameya.Application.Behaviors;

/// <summary>
/// Commands only. Success: save and commit. A DomainException flagged CommitStateChanges also saves and
/// commits, then rethrows the same exception. Any other exception disposes the transaction uncommitted.
/// Registered <see cref="ITransactionCallbacks"/> run once the outcome is known (best-effort).
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(
    IAppDbContext db, TransactionCallbacks callbacks, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
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

        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        var committed = false;
        DomainException? committedFailure = null;
        TResponse? response = default;
        try
        {
            try
            {
                response = await next(cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                committed = true;
            }
            catch (DomainException ex) when (ex.CommitStateChanges)
            {
                // State must persist even if the client aborted the request.
                await db.SaveChangesAsync(CancellationToken.None);
                await tx.CommitAsync(CancellationToken.None);
                committed = true;
                committedFailure = ex;
            }
        }
        catch
        {
            if (!committed)
            {
                await callbacks.RunRolledBackAsync(logger);
            }

            throw;
        }

        await callbacks.RunCommittedAsync(logger);
        if (committedFailure is not null)
        {
            ExceptionDispatchInfo.Throw(committedFailure);
        }

        return response!;
    }
}
