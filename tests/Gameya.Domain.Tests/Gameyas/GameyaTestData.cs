using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using Shouldly;

namespace Gameya.Domain.Tests.Gameyas;

internal static class GameyaTestData
{
    public static readonly DateOnly Today = new(2026, 11, 14);
    public static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    public static GameyaSettings Settings() => new(
        Name: "Family circle",
        Description: null,
        ContributionAmount: 1_000m,
        ShareCount: 10,
        Frequency: GameyaFrequency.Monthly,
        StartDate: Today.AddDays(7),
        GraceDays: 3,
        PayoutTiming: PayoutTiming.AfterFullCollection,
        PayoutOffsetDays: null,
        OrderMethod: OrderMethod.ByRequest,
        MaxSharesPerMember: 2,
        OrganizerParticipates: true,
        RulesText: null);

    public static DomainException Thrown(Action act, string code)
    {
        var ex = Should.Throw<DomainException>(act);
        ex.Code.ShouldBe(code);
        return ex;
    }
}
