using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

public sealed record JoinRequestRow(
    Guid Id, Guid UserId, int AcceptedTermsVersion, IReadOnlyList<IReadOnlyList<int>> SharePreferences);

public sealed record JoinRequestListRow(int TermsVersion, IReadOnlyList<JoinRequestRow> Requests);

/// <summary>Pending join requests of a gam'eya, oldest first, with each requested share's preferences.</summary>
public sealed class JoinRequestRowsSpec : Specification<GameyaCircle, JoinRequestListRow>
{
    public JoinRequestRowsSpec(Guid gameyaId) =>
        Query
            .Where(g => g.Id == gameyaId)
            .Select(g => new JoinRequestListRow(
                g.TermsVersion,
                g.JoinRequests
                    .Where(r => r.Status == JoinRequestStatus.Pending)
                    .OrderBy(r => r.CreatedAt)
                    .ThenBy(r => r.Id)
                    .Select(r => new JoinRequestRow(
                        r.Id,
                        r.UserId,
                        r.AcceptedTermsVersion,
                        r.Shares.OrderBy(s => s.Id).Select(s => s.Preferences).ToList()))
                    .ToList()));
}
