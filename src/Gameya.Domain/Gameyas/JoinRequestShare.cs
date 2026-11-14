using Gameya.Domain.Common;

namespace Gameya.Domain.Gameyas;

public class JoinRequestShare : AuditableEntity
{
    private JoinRequestShare()
    {
    }

    public Guid Id { get; private set; }
    public IReadOnlyList<int> Preferences { get; private set; } = [];

    internal static JoinRequestShare Create(IReadOnlyList<int> preferences) =>
        new() { Id = Guid.CreateVersion7(), Preferences = [.. preferences] };
}
