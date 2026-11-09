namespace Gameya.Application.Abstractions;

/// <summary>
/// Reports whether a user still takes part in a recruiting or running gameya, which blocks account deletion.
/// Sub-project 2 replaces the foundation's always-false implementation.
/// </summary>
public interface IActiveParticipationChecker
{
    Task<bool> HasRecruitingOrActiveGameyasAsync(Guid userId, CancellationToken cancellationToken);
}
