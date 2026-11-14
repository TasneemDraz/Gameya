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
            viewerId is { } id ? Relationship(row, id) : null,
            viewerId is not null && row.ViewerRequestTermsVersion is { } accepted && accepted < row.TermsVersion);
    }

    /// <summary>Open while recruiting with a free share; Cancelled once cancelled; otherwise not joinable (Full).</summary>
    private static string State(GameyaStatus status, int available) => status switch
    {
        GameyaStatus.Cancelled => "Cancelled",
        GameyaStatus.Recruiting when available > 0 => "Open",
        _ => "Full",
    };

    private static string Relationship(InvitePreviewRow row, Guid viewerId)
    {
        if (row.OrganizerId == viewerId)
        {
            return "Organizer";
        }

        if (row.ViewerIsMember)
        {
            return "Member";
        }

        return row.ViewerRequestTermsVersion is null ? "None" : "RequestPending";
    }
}
