using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;

namespace Gameya.Application.Gameyas;

public enum GameyaRole
{
    None,
    Member,
    Organizer,
}

public interface IGameyaAccess
{
    Task<GameyaRole> GetRoleAsync(Guid gameyaId, Guid userId, CancellationToken ct);
}

public sealed class GameyaAccess(IAppDbContext db) : IGameyaAccess
{
    public async Task<GameyaRole> GetRoleAsync(Guid gameyaId, Guid userId, CancellationToken ct)
    {
        var row = await db.Gameyas.FirstOrDefaultAsync(new GameyaRoleSpec(gameyaId, userId), ct);
        if (row is null)
        {
            return GameyaRole.None;
        }

        if (row.OrganizerId == userId)
        {
            return GameyaRole.Organizer;
        }

        return row.IsActiveMember ? GameyaRole.Member : GameyaRole.None;
    }
}
