using Gameya.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Gameya.Application.Behaviors;

/// <summary>Scoped collector for <see cref="ITransactionCallbacks"/>; <see cref="TransactionBehavior{TRequest,TResponse}"/> runs it.</summary>
public sealed class TransactionCallbacks : ITransactionCallbacks
{
    private readonly List<Func<CancellationToken, Task>> _committed = [];
    private readonly List<Func<CancellationToken, Task>> _rolledBack = [];

    public void OnCommitted(Func<CancellationToken, Task> action) => _committed.Add(action);

    public void OnRolledBack(Func<CancellationToken, Task> action) => _rolledBack.Add(action);

    public Task RunCommittedAsync(ILogger logger) => RunAsync(_committed, "committed", logger);

    public Task RunRolledBackAsync(ILogger logger) => RunAsync(_rolledBack, "rolled-back", logger);

    private static async Task RunAsync(List<Func<CancellationToken, Task>> actions, string outcome, ILogger logger)
    {
        foreach (var action in actions.ToList())
        {
            try
            {
                await action(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Swallowed on purpose: the transaction outcome is already decided.
                logger.LogWarning(ex, "A {Outcome} transaction callback failed", outcome);
            }
        }

        actions.Clear();
    }
}
