using System.Linq.Expressions;
using FluentValidation;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;

namespace Gameya.Application.Gameyas;

/// <summary>
/// The rules shared by the join and accept-invite forms: at least one share and both checkboxes ticked.
/// Share counts, capacity and preferences are domain rules.
/// </summary>
internal static class JoinTermsValidation
{
    public static void Add<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, IReadOnlyList<ShareRequest>>> shares,
        Expression<Func<T, bool>> acceptRules,
        Expression<Func<T, bool>> acceptNoMoney)
    {
        validator.RuleFor(shares)
            .NotEmpty().WithErrorCode(ErrorCodes.VALIDATION_FAILED)
            .Must(s => s.All(share => share?.Preferences is not null)).WithErrorCode(ErrorCodes.VALIDATION_FAILED)
            .OverridePropertyName("shares");
        validator.RuleFor(acceptRules).Equal(true).WithErrorCode(ErrorCodes.VALIDATION_FAILED)
            .OverridePropertyName("acceptRules");
        validator.RuleFor(acceptNoMoney).Equal(true).WithErrorCode(ErrorCodes.VALIDATION_FAILED)
            .OverridePropertyName("acceptNoMoney");
    }
}
