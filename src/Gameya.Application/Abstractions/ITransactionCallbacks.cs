namespace Gameya.Application.Abstractions;

/// <summary>
/// Lets a command handler register side effects on non-transactional resources (such as files) that must
/// follow the outcome of the surrounding database transaction. Actions are best-effort: a failure is logged
/// and never changes the response or the exception that propagates.
/// </summary>
public interface ITransactionCallbacks
{
    /// <summary>Runs only after the transaction commits (including a commit that precedes a rethrown DomainException).</summary>
    void OnCommitted(Func<CancellationToken, Task> action);

    /// <summary>Runs only when the transaction does not commit.</summary>
    void OnRolledBack(Func<CancellationToken, Task> action);
}
