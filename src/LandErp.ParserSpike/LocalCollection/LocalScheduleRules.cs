using System.Globalization;

namespace LandErp.ParserSpike.LocalCollection;

public static class LocalScheduleRules
{
    public static DateTimeOffset? Next(LocalSchedule schedule, DateTimeOffset after)
    {
        schedule.Validate();
        if (!schedule.Enabled || schedule.Kind == LocalScheduleKind.Manual) return null;
        if (schedule.Kind == LocalScheduleKind.Interval) return after.AddMinutes(schedule.IntervalMinutes!.Value);
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        DateTime localAfter = TimeZoneInfo.ConvertTime(after, zone).DateTime;
        foreach (int day in Enumerable.Range(0, 8))
            foreach (string value in schedule.FixedTimes!.Order(StringComparer.Ordinal))
            {
                TimeOnly clock = TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
                DateTime local = DateOnly.FromDateTime(localAfter).AddDays(day).ToDateTime(clock, DateTimeKind.Unspecified);
                if (local <= localAfter || zone.IsInvalidTime(local)) continue;
                return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
            }
        throw new InvalidOperationException("LOCAL_SCHEDULE_NEXT_NOT_FOUND");
    }
}
