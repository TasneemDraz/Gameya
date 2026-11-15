using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

public sealed record MemberRow(Guid UserId, int ShareCount, int AcceptedTermsVersion);

public sealed record MemberListRow(Guid OrganizerId, int TermsVersion, IReadOnlyList<MemberRow> Members);

/// <summary>The Active memberships of a gam'eya, oldest first, with what the handler needs to flag stale terms.</summary>
public sealed class MemberRowsSpec : Specification<GameyaCircle, MemberListRow>
{
    public MemberRowsSpec(Guid gameyaId) =>
        Query
            .Where(g => g.Id == gameyaId)
            .Select(g => new MemberListRow(
                g.OrganizerId,
                g.TermsVersion,
                g.Memberships
                    .Where(m => m.Status == MembershipStatus.Active)
                    .OrderBy(m => m.JoinedAt)
                    .ThenBy(m => m.Id)
                    .Select(m => new MemberRow(m.UserId, m.Shares.Count, m.AcceptedTermsVersion))
                    .ToList()));
}
