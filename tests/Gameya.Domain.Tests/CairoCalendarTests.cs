using Gameya.Domain.Common;

namespace Gameya.Domain.Tests;

public class CairoCalendarTests
{
    [Fact]
    public void MonthlyDueDate_clamps_to_february_end() =>
        CairoCalendar.MonthlyDueDate(new DateOnly(2027, 1, 31), 1).ShouldBe(new DateOnly(2027, 2, 28));

    [Fact]
    public void MonthlyDueDate_does_not_drift_after_clamping() =>
        CairoCalendar.MonthlyDueDate(new DateOnly(2027, 1, 31), 2).ShouldBe(new DateOnly(2027, 3, 31));

    [Fact]
    public void MonthlyDueDate_handles_leap_year() =>
        CairoCalendar.MonthlyDueDate(new DateOnly(2028, 1, 31), 1).ShouldBe(new DateOnly(2028, 2, 29));

    [Fact]
    public void ToCairoDate_winter_is_plus_two() =>
        CairoCalendar.ToCairoDate(DateTimeOffset.Parse("2026-01-01T21:30:00Z")).ShouldBe(new DateOnly(2026, 1, 1));

    [Fact]
    public void ToCairoDate_summer_is_plus_three() =>
        CairoCalendar.ToCairoDate(DateTimeOffset.Parse("2026-07-01T21:30:00Z")).ShouldBe(new DateOnly(2026, 7, 2));

    [Fact]
    public void StartOfDayUtc_summer() =>
        CairoCalendar.StartOfDayUtc(new DateOnly(2026, 7, 2)).ShouldBe(DateTimeOffset.Parse("2026-07-01T21:00:00Z"));
}
