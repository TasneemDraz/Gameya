using Ardalis.Specification;
using Gameya.Domain.Activity;

namespace Gameya.Application.Gameyas.Specifications;

/// <summary>The details of every settings change of a gam'eya, oldest first; the handler picks the terms bumps.</summary>
public sealed class TermsChangeEntriesSpec : Specification<ActivityLogEntry, string>
{
    public const string SettingsChangedAction = "GAMEYA_SETTINGS_CHANGED";

    public TermsChangeEntriesSpec(Guid gameyaId) =>
        Query
            .Where(e => e.GameyaId == gameyaId && e.Action == SettingsChangedAction)
            .OrderBy(e => e.Id)
            .Select(e => e.DetailsJson);
}
