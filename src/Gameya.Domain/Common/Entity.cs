namespace Gameya.Domain.Common;

public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent e) => _domainEvents.Add(e);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
