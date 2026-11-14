using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

/// <summary>The gam'eya holding the given invite code (cancelled ones keep theirs).</summary>
public sealed class GameyaByInviteCodeSpec : Specification<GameyaCircle>
{
    public GameyaByInviteCodeSpec(string code) => Query.Where(g => g.InviteCode == code);
}
