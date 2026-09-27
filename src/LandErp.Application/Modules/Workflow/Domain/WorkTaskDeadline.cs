using System.Globalization;

namespace LandErp.Application.Modules.Workflow.Domain;

public static class WorkTaskDeadline
{
    public static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
    public static DateTimeOffset DayStart(DateTimeOffset now) =>
        new(TimeZoneInfo.ConvertTimeToUtc(TimeZoneInfo.ConvertTime(now, Moscow).Date, Moscow));
    public static bool Overdue(WorkTask task, DateTimeOffset now) => !task.Completed && !task.Deleted
        && task.DueAt < (task.DueHasTime ? now : DayStart(now));
    public static string Format(DateTimeOffset? due, bool hasTime) => due == null ? "Без срока"
        : TimeZoneInfo.ConvertTime(due.Value, Moscow).ToString(hasTime ? "dd.MM.yyyy HH:mm 'МСК'" : "dd.MM.yyyy", CultureInfo.InvariantCulture);
}
