using System.Text.Json;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using MediatR;

namespace Gameya.Application.Gameyas;

/// <summary>Details for a member or the organizer; the invite code and request count are the organizer's only.</summary>
[RequiresGameyaMember]
public sealed record GetGameyaQuery(Guid GameyaId) : IQuery<GameyaDto>, IGameyaRequest;

public sealed class GetGameyaHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetGameyaQuery, GameyaDto>
{
    public async Task<GameyaDto> Handle(GetGameyaQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var row = await db.Gameyas.FirstOrDefaultAsync(new GameyaDetailRowSpec(request.GameyaId, userId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);

        var isOrganizer = row.OrganizerId == userId;
        var needsReaccept = row.MyAcceptedTermsVersion is { } accepted && accepted < row.TermsVersion;
        var diff = needsReaccept
            ? await TermsDiffAsync(row, row.MyAcceptedTermsVersion!.Value, cancellationToken)
            : null;

        return new GameyaDto(
            row.Id,
            row.Name,
            row.Description,
            row.ContributionAmount,
            row.ShareCount,
            row.Frequency,
            row.StartDate,
            row.GraceDays,
            row.PayoutTiming,
            row.PayoutOffsetDays,
            row.OrderMethod,
            row.MaxSharesPerMember,
            row.OrganizerParticipates,
            row.RulesText,
            row.Status,
            row.TermsVersion,
            row.Currency,
            row.ContributionAmount * row.ShareCount,
            row.ShareCount,
            GameyaCircle.EndDateFor(row.StartDate, row.Frequency, row.ShareCount),
            row.ContributionAmount * row.ShareCount,
            row.AssignedShares,
            row.ReservedShares,
            row.ShareCount - row.AssignedShares - row.ReservedShares,
            isOrganizer ? GameyaRole.Organizer : GameyaRole.Member,
            row.MyShareCount,
            needsReaccept,
            isOrganizer ? row.InviteCode : null,
            isOrganizer ? row.PendingJoinRequestCount : null,
            diff);
    }

    /// <summary>
    /// For each money field, the value from the earliest terms bump after the member's accepted version, which is
    /// the value they agreed to. A field changed and later changed back needs no banner, so it stays null.
    /// </summary>
    private async Task<TermsDiffDto?> TermsDiffAsync(GameyaDetailRow row, int acceptedVersion, CancellationToken ct)
    {
        decimal? oldAmount = null;
        int? oldShareCount = null;
        GameyaFrequency? oldFrequency = null;

        foreach (var json in await db.ActivityLog.ListAsync(new TermsChangeEntriesSpec(row.Id), ct))
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("termsVersion", out var version) || version.GetInt32() <= acceptedVersion
                || !root.TryGetProperty("changed", out var changed))
            {
                continue;
            }

            if (oldAmount is null && changed.TryGetProperty("contributionAmount", out var amount))
            {
                oldAmount = amount.GetProperty("old").GetDecimal();
            }

            if (oldShareCount is null && changed.TryGetProperty("shareCount", out var shares))
            {
                oldShareCount = shares.GetProperty("old").GetInt32();
            }

            if (oldFrequency is null && changed.TryGetProperty("frequency", out var frequency))
            {
                oldFrequency = Enum.Parse<GameyaFrequency>(frequency.GetProperty("old").GetString()!);
            }
        }

        if (oldAmount == row.ContributionAmount) oldAmount = null;
        if (oldShareCount == row.ShareCount) oldShareCount = null;
        if (oldFrequency == row.Frequency) oldFrequency = null;

        return oldAmount is null && oldShareCount is null && oldFrequency is null
            ? null
            : new TermsDiffDto(oldAmount, oldShareCount, oldFrequency);
    }
}
