using Gameya.Application.Abstractions;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Behaviors;

/// <summary>
/// Commands only. Success: save and commit. A DomainException flagged CommitStateChanges also saves and
/// commits, then rethrows the same exception. Any other exception disposes the transaction uncommitted.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(IAppDbContext db)
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
        try
        {
            var response = await next(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return response;
        }
        catch (DomainException ex) when (ex.CommitStateChanges)
        {
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            throw;
        }
    }
}
