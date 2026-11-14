using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;

namespace Gameya.Application.Gameyas;

/// <summary>
/// FR-AUTH-04's real rule: a user who organizes a Draft, Recruiting or Active gam'eya, or holds an Active
/// membership in a Recruiting or Active one, cannot delete their account.
/// </summary>
public sealed class GameyaParticipationChecker(IAppDbContext db) : IActiveParticipationChecker
{
    public Task<bool> HasRecruitingOrActiveGameyasAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Gameyas.AnyAsync(new ParticipationSpec(userId), cancellationToken);
}
