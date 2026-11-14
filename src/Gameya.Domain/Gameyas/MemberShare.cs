using Gameya.Domain.Common;

namespace Gameya.Domain.Gameyas;

public class MemberShare : AuditableEntity
{
    private MemberShare()
    {
    }

    public Guid Id { get; private set; }
    public IReadOnlyList<int> Preferences { get; private set; } = [];

    internal static MemberShare Create(IReadOnlyList<int> preferences) =>
        new() { Id = Guid.CreateVersion7(), Preferences = [.. preferences] };
}
