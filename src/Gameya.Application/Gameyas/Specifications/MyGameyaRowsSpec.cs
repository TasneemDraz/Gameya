using Ardalis.Specification;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas.Specifications;

public sealed record MyGameyaRow(
    Guid Id,
    string Name,
    GameyaStatus Status,
    Guid OrganizerId,
    decimal ContributionAmount,
    int ShareCount,
    int TermsVersion,
    int MyShareCount,
    int? MyAcceptedTermsVersion);

/// <summary>Gam'eyas the user organizes or holds an Active membership in, with their own shares and terms.</summary>
public sealed class MyGameyaRowsSpec : Specification<GameyaCircle, MyGameyaRow>
{
    public MyGameyaRowsSpec(Guid userId) =>
        Query
            .Where(g => g.OrganizerId == userId
                || g.Memberships.Any(m => m.UserId == userId && m.Status == MembershipStatus.Active))
            .Select(g => new MyGameyaRow(
                g.Id,
                g.Name,
                g.Status,
                g.OrganizerId,
                g.ContributionAmount,
                g.ShareCount,
                g.TermsVersion,
                g.Memberships
                    .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
                    .SelectMany(m => m.Shares)
                    .Count(),
                g.Memberships
                    .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
                    .Select(m => (int?)m.AcceptedTermsVersion)
                    .FirstOrDefault()));
}
