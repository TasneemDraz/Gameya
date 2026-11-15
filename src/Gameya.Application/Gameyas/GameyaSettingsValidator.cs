using System.Linq.Expressions;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas;

/// <summary>
/// The context-free FR-GAM-01 field rules, for clean 400 responses. The domain enforces the same rules and stays
/// the source of truth; rules that need the stored gam'eya (start date on edit, share floor) live there only.
/// </summary>
public sealed class GameyaSettingsValidator : AbstractValidator<GameyaSettings>
{
    private const string Invalid = ErrorCodes.VALIDATION_FAILED;

    private static readonly GameyaSettingsValidator Instance = new();

    public GameyaSettingsValidator()
    {
        RuleFor(s => s.Name).Must(n => n?.Trim().Length is >= 3 and <= 60).WithErrorCode(Invalid);
        RuleFor(s => s.Description).MaximumLength(500).WithErrorCode(Invalid);
        RuleFor(s => s.ContributionAmount)
            .InclusiveBetween(100m, 1_000_000m).WithErrorCode(Invalid)
            .Must(a => a == decimal.Truncate(a)).WithErrorCode(Invalid);
        RuleFor(s => s.ShareCount).InclusiveBetween(2, 50).WithErrorCode(Invalid);
        RuleFor(s => s.Frequency).IsInEnum().WithErrorCode(Invalid);
        RuleFor(s => s.GraceDays).InclusiveBetween(0, 10).WithErrorCode(Invalid);
        RuleFor(s => s.PayoutTiming).IsInEnum().WithErrorCode(Invalid);
        RuleFor(s => s.PayoutOffsetDays)
            .Must((s, days) => s.PayoutTiming == PayoutTiming.FixedDate ? days is >= 0 and <= 10 : days is null)
            .WithErrorCode(Invalid);
        RuleFor(s => s.OrderMethod).IsInEnum().WithErrorCode(Invalid);
        RuleFor(s => s.MaxSharesPerMember).InclusiveBetween(1, 5).WithErrorCode(Invalid);
        RuleFor(s => s.RulesText).MaximumLength(2000).WithErrorCode(Invalid);
    }

    /// <summary>
    /// Validates the settings of a command and reports failures under top-level camelCase keys
    /// (<c>errors.contributionAmount</c>, not <c>Settings.ContributionAmount</c>), matching the flat request body.
    /// </summary>
    public static void Into<T>(GameyaSettings? settings, ValidationContext<T> context)
    {
        if (settings is null)
        {
            context.AddFailure(new ValidationFailure("settings", Invalid) { ErrorCode = Invalid });
            return;
        }

        foreach (var failure in Instance.Validate(settings).Errors)
        {
            context.AddFailure(new ValidationFailure(
                JsonNamingPolicy.CamelCase.ConvertName(failure.PropertyName), failure.ErrorMessage)
            {
                ErrorCode = failure.ErrorCode,
            });
        }
    }

    /// <summary>Organizer shares: 1..max when participating, 0 otherwise.</summary>
    public static void OrganizerShares<T>(
        AbstractValidator<T> validator, Func<T, GameyaSettings?> settings, Expression<Func<T, int>> organizerShares)
    {
        validator.RuleFor(organizerShares)
            .Must((c, n) => settings(c) is not { } s || (s.OrganizerParticipates ? n >= 1 : n == 0))
            .WithErrorCode(Invalid)
            .Must((c, n) => settings(c) is not { } s || n <= s.MaxSharesPerMember)
            .WithErrorCode(ErrorCodes.MAX_SHARES_EXCEEDED)
            .OverridePropertyName("organizerShares");
    }
}
