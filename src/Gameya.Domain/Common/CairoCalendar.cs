namespace Gameya.Domain.Common;

public static class CairoCalendar
{
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    public static DateOnly ToCairoDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, Zone).DateTime);

    public static DateTimeOffset StartOfDayUtc(DateOnly cairoDate)
    {
        var local = cairoDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local))
            local = local.AddHours(1);
        var offset = Zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static DateOnly MonthlyDueDate(DateOnly start, int monthOffset)
    {
        var first = new DateOnly(start.Year, start.Month, 1).AddMonths(monthOffset);
        var day = Math.Min(start.Day, DateTime.DaysInMonth(first.Year, first.Month));
        return new DateOnly(first.Year, first.Month, day);
    }
}
