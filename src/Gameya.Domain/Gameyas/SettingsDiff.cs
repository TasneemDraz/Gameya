namespace Gameya.Domain.Gameyas;

/// <summary>Builds the old-to-new map logged when settings change; keys are camelCase field names.</summary>
internal static class SettingsDiff
{
    public static Dictionary<string, object?> Build(
        GameyaSettings old, int oldOrganizerShares, GameyaSettings now, int newOrganizerShares)
    {
        var changed = new Dictionary<string, object?>();
        Add(changed, "name", old.Name, now.Name);
        Add(changed, "description", old.Description, now.Description);
        Add(changed, "contributionAmount", old.ContributionAmount, now.ContributionAmount);
        Add(changed, "shareCount", old.ShareCount, now.ShareCount);
        Add(changed, "frequency", old.Frequency.ToString(), now.Frequency.ToString());
        Add(changed, "startDate", Iso(old.StartDate), Iso(now.StartDate));
        Add(changed, "graceDays", old.GraceDays, now.GraceDays);
        Add(changed, "payoutTiming", old.PayoutTiming.ToString(), now.PayoutTiming.ToString());
        Add(changed, "payoutOffsetDays", old.PayoutOffsetDays, now.PayoutOffsetDays);
        Add(changed, "orderMethod", old.OrderMethod.ToString(), now.OrderMethod.ToString());
        Add(changed, "maxSharesPerMember", old.MaxSharesPerMember, now.MaxSharesPerMember);
        Add(changed, "organizerParticipates", old.OrganizerParticipates, now.OrganizerParticipates);
        Add(changed, "rulesText", old.RulesText, now.RulesText);
        Add(changed, "organizerShares", oldOrganizerShares, newOrganizerShares);
        return changed;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");

    private static void Add<T>(Dictionary<string, object?> changed, string key, T old, T now)
    {
        if (!EqualityComparer<T>.Default.Equals(old, now))
            changed[key] = new Dictionary<string, object?> { ["old"] = old, ["new"] = now };
    }
}
