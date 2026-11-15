using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>
/// The public invite page (rate-limited per IP at the endpoint). An unknown, replaced or malformed code is the same
/// INVITE_CODE_INVALID, so the response says nothing about which codes once existed.
/// </summary>
[PublicAccess]
public sealed record GetInvitePreviewQuery(string Code) : IQuery<InvitePreviewDto>;

public sealed class GetInvitePreviewHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetInvitePreviewQuery, InvitePreviewDto>
{
    private const int CodeLength = 6;

    public async Task<InvitePreviewDto> Handle(GetInvitePreviewQuery request, CancellationToken cancellationToken)
    {
        // Same normalization as joining, so a code typed in lower case still works.
        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length != CodeLength)
        {
            throw new DomainException(ErrorCodes.INVITE_CODE_INVALID);
        }

        var viewerId = currentUser.IsAuthenticated ? currentUser.UserId : null;
        var row = await db.Gameyas.FirstOrDefaultAsync(
                new InvitePreviewRowSpec(code, viewerId ?? Guid.Empty), cancellationToken)
            ?? throw new DomainException(ErrorCodes.INVITE_CODE_INVALID);

        // Only the organizer's name is public; it is looked up by id and never with a phone.
        var cards = await UserCards.LoadAsync(db, [row.OrganizerId], cancellationToken);
        var available = row.ShareCount - row.AssignedShares - row.ReservedShares;
        var inviteId = viewerId is { } viewer && row.OrganizerId != viewer && !row.ViewerIsMember
            ? await ViewerInviteAsync(row.Id, viewer, cancellationToken)
            : null;

        return new InvitePreviewDto(
            row.Id,
            State(row.Status, available),
            row.Name,
            row.Description,
            row.ContributionAmount,
            row.Currency,
            row.ShareCount,
            row.Frequency,
            row.StartDate,
            GameyaCircle.EndDateFor(row.StartDate, row.Frequency, row.ShareCount),
            row.ContributionAmount * row.ShareCount,
            row.GraceDays,
            row.PayoutTiming,
            row.PayoutOffsetDays,
            row.OrderMethod,
            row.MaxSharesPerMember,
            available,
            row.RulesText,
            cards.NameOf(row.OrganizerId),
            viewerId is { } id ? Relationship(row, id, inviteId) : null,
            viewerId is not null && row.ViewerRequestTermsVersion is { } accepted && accepted < row.TermsVersion,
            inviteId);
    }

    /// <summary>The viewer's own Pending direct invite, matched by user id or by their phone.</summary>
    private async Task<Guid?> ViewerInviteAsync(Guid gameyaId, Guid viewerId, CancellationToken ct)
    {
        var me = await UserCards.LoadAsync(db, [viewerId], ct);
        if (!me.TryGetValue(viewerId, out var card) || card.Phone is null)
        {
            return null;
        }

        var ids = await db.PendingInvites.ListAsync(new ViewerPendingInviteSpec(gameyaId, viewerId, card.Phone), ct);
        return ids.Count > 0 ? ids[0] : null;
    }

    /// <summary>Open while recruiting with a free share; Cancelled once cancelled; otherwise not joinable (Full).</summary>
    private static string State(GameyaStatus status, int available) => status switch
    {
        GameyaStatus.Cancelled => "Cancelled",
        GameyaStatus.Recruiting when available > 0 => "Open",
        _ => "Full",
    };

    /// <summary>
    /// An invited viewer is pointed to their invite (which already holds their shares) rather than the join form, even
    /// when they also have a request pending.
    /// </summary>
    private static string Relationship(InvitePreviewRow row, Guid viewerId, Guid? inviteId)
    {
        if (row.OrganizerId == viewerId)
        {
            return "Organizer";
        }

        if (row.ViewerIsMember)
        {
            return "Member";
        }

        if (inviteId is not null)
        {
            return "Invited";
        }

        return row.ViewerRequestTermsVersion is null ? "None" : "RequestPending";
    }
}
