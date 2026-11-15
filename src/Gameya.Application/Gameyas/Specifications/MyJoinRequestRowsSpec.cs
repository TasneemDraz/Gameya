using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

public sealed record MyJoinRequestRow(
    Guid GameyaId,
    string Name,
    decimal ContributionAmount,
    int GameyaShareCount,
    int TermsVersion,
    int AcceptedTermsVersion,
    int ShareCount);

/// <summary>
/// Gam'eyas in which the user has a Pending join request, by name, with that request's shares and accepted terms. A
/// filtered unique index allows at most one pending request per user and gam'eya, so each gam'eya yields one row.
/// </summary>
public sealed class MyJoinRequestRowsSpec : Specification<GameyaCircle, MyJoinRequestRow>
{
    public MyJoinRequestRowsSpec(Guid userId) =>
        Query
            .Where(g => g.JoinRequests.Any(r => r.UserId == userId && r.Status == JoinRequestStatus.Pending))
            .OrderBy(g => g.Name)
            .Select(g => new MyJoinRequestRow(
                g.Id,
                g.Name,
                g.ContributionAmount,
                g.ShareCount,
                g.TermsVersion,
                g.JoinRequests
                    .Where(r => r.UserId == userId && r.Status == JoinRequestStatus.Pending)
                    .Select(r => r.AcceptedTermsVersion)
                    .First(),
                g.JoinRequests
                    .Where(r => r.UserId == userId && r.Status == JoinRequestStatus.Pending)
                    .Select(r => r.Shares.Count)
                    .First()));
}
