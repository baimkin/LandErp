using System.Globalization;
using System.Text.Json;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;

namespace LandErp.Infrastructure.Modules.Collection;

internal static class CollectionScheduleRules
{
    public static void Apply(SearchConfiguration search, CollectionSchedule? schedule, DateTimeOffset now, string timeZoneId)
    {
        schedule ??= new(CollectionScheduleKind.Manual);
        search.ScheduleKind = schedule.Kind;
        search.IntervalMinutes = null;
        search.FixedTimesJson = "[]";
        if (schedule.Kind == CollectionScheduleKind.Interval)
        {
            if (schedule.IntervalMinutes is < 5 or > 10080) throw new ArgumentException("Интервал должен быть от 5 минут до 7 дней.");
            search.IntervalMinutes = schedule.IntervalMinutes;
        }
        else if (schedule.Kind == CollectionScheduleKind.FixedTimes)
        {
            string[] values = (schedule.FixedTimes ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (values.Length is < 1 or > 12 || values.Any(value => !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                throw new ArgumentException("Укажите от 1 до 12 моментов времени в формате ЧЧ:ММ.");
            search.FixedTimesJson = JsonSerializer.Serialize(values);
        }
        else if (schedule.Kind != CollectionScheduleKind.Manual) throw new ArgumentException("Тип расписания не поддерживается.");
        search.NextRunAt = search.Enabled ? Next(search, now, timeZoneId) : null;
    }

    public static DateTimeOffset? Next(SearchConfiguration search, DateTimeOffset after, string timeZoneId)
    {
        if (!search.Enabled || search.ScheduleKind == CollectionScheduleKind.Manual) return null;
        if (search.ScheduleKind == CollectionScheduleKind.Interval) return after.AddMinutes(search.IntervalMinutes!.Value);
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        string[] values = JsonSerializer.Deserialize<string[]>(search.FixedTimesJson) ?? [];
        DateTime localNow = TimeZoneInfo.ConvertTime(after, zone).DateTime;
        for (int day = 0; day < 8; day++)
        {
            foreach (string value in values)
            {
                TimeOnly clock = TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
                DateTime local = DateOnly.FromDateTime(localNow).AddDays(day).ToDateTime(clock, DateTimeKind.Unspecified);
                if (local <= localNow || zone.IsInvalidTime(local)) continue;
                return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
            }
        }
        throw new InvalidOperationException("Не удалось вычислить следующий запуск.");
    }

    public static string Display(SearchConfiguration search) => search.ScheduleKind switch
    {
        CollectionScheduleKind.Manual => "Вручную",
        CollectionScheduleKind.Interval => $"Каждые {search.IntervalMinutes} мин.",
        _ => "Ежедневно: " + string.Join(", ", JsonSerializer.Deserialize<string[]>(search.FixedTimesJson) ?? [])
    };
}
