using Gameya.Domain.Common;

namespace Gameya.Domain.Gameyas;

public class JoinRequest : AuditableEntity
{
    private readonly List<JoinRequestShare> _shares = [];

    private JoinRequest()
    {
    }

    public Guid Id { get; private set; }
    public Guid GameyaId { get; private set; }
    public Guid UserId { get; private set; }
    public int AcceptedTermsVersion { get; private set; }
    public JoinRequestStatus Status { get; private set; }
    public string? RejectReason { get; private set; }
    public IReadOnlyList<JoinRequestShare> Shares => _shares;

    internal static JoinRequest Create(
        Guid gameyaId, Guid userId, IReadOnlyList<ShareRequest> shares, int termsVersion)
    {
        var request = new JoinRequest
        {
            Id = Guid.CreateVersion7(),
            GameyaId = gameyaId,
            UserId = userId,
            AcceptedTermsVersion = termsVersion,
            Status = JoinRequestStatus.Pending,
        };
        request._shares.AddRange(shares.Select(s => JoinRequestShare.Create(s.Preferences)));
        return request;
    }

    internal void AcceptTerms(int termsVersion) => AcceptedTermsVersion = termsVersion;

    internal void Withdraw() => Status = JoinRequestStatus.Withdrawn;

    internal void Approve() => Status = JoinRequestStatus.Approved;

    internal void Reject(string? reason)
    {
        Status = JoinRequestStatus.Rejected;
        RejectReason = reason;
    }
}
