using Gameya.Application.Abstractions;

namespace Gameya.Infrastructure.Participation;

/// <summary>Foundation placeholder: no gameyas exist yet, so nobody participates in one.</summary>
public sealed class NoParticipationChecker : IActiveParticipationChecker
{
    public Task<bool> HasRecruitingOrActiveGameyasAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
