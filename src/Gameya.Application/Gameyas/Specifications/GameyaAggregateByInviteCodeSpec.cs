using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

/// <summary>The tracked aggregate (live children only) holding the given invite code.</summary>
public sealed class GameyaAggregateByInviteCodeSpec : Specification<GameyaCircle>
{
    public GameyaAggregateByInviteCodeSpec(string code)
    {
        Query.Where(g => g.InviteCode == code);
        Query.IncludeLiveChildren();
    }
}
