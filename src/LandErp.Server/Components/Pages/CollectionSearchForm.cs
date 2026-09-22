using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;

namespace LandErp.Server.Components.Pages;

/// <summary>Form units and editable time rows; authoritative scheduling stays on Server.</summary>
public sealed class CollectionSearchForm : IValidatableObject
{
    private static readonly string[] AcceptedTimeFormats = ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF"];

    [Required(ErrorMessage = "Введите название поиска."), MaxLength(200)]
    public string Label { get; set; } = "";
    [Required(ErrorMessage = "Вставьте ссылку поиска."), Url]
    public string Url { get; set; } = "";
    public CatalogSource Source { get; set; } = CatalogSource.Avito;
    [Range(1, 100, ErrorMessage = "Укажите от 1 до 100 страниц.")]
    public int MaxPages { get; set; } = 10;
    public Guid? GroupId { get; set; }
    public CollectionScheduleKind ScheduleKind { get; set; }
    public int IntervalValue { get; set; } = 1;
    public int IntervalUnit { get; set; } = 60;
    public List<TimeRow> Times { get; set; } = [new("09:00")];
    public bool Enabled { get; set; } = true;
    public bool RunImmediately { get; set; }

    public CollectionSchedule Schedule()
    {
        var errors = Validate(new ValidationContext(this)).ToArray();
        if (errors.Length > 0) throw new ArgumentException(errors[0].ErrorMessage);
        return new(ScheduleKind,
            ScheduleKind == CollectionScheduleKind.Interval ? IntervalValue * IntervalUnit : null,
            ScheduleKind == CollectionScheduleKind.FixedTimes
                ? Times.Select(x => NormalizeTime(x.Value)).Order(StringComparer.Ordinal).ToArray()
                : []);
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ScheduleKind == CollectionScheduleKind.Interval &&
            (IntervalUnit is not (1 or 60 or 1440) || (long)IntervalValue * IntervalUnit is < 5 or > 10080))
            yield return new("Интервал должен быть от 5 минут до 7 дней.");
        if (ScheduleKind == CollectionScheduleKind.FixedTimes)
        {
            if (Times.Count is < 1 or > 12)
            {
                yield return new("Укажите от 1 до 12 времён запуска.");
                yield break;
            }

            string[] normalized = new string[Times.Count];
            for (int index = 0; index < Times.Count; index++)
            {
                if (!TryNormalizeTime(Times[index].Value, out normalized[index]))
                {
                    yield return new("Укажите корректное время запуска в формате ЧЧ:ММ.");
                    yield break;
                }
            }

            if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
                yield return new("Время запуска не должно повторяться.");
        }
    }

    public static CollectionSearchForm From(SearchView search)
    {
        int minutes = search.IntervalMinutes ?? 60;
        int unit = minutes % 1440 == 0 ? 1440 : minutes % 60 == 0 ? 60 : 1;
        return new()
        {
            Label = search.Label, Url = search.Url, Source = search.Source, MaxPages = search.MaxPages,
            GroupId = search.GroupId, Enabled = search.Enabled, ScheduleKind = search.ScheduleKind,
            IntervalValue = minutes / unit, IntervalUnit = unit,
            Times = search.FixedTimes.Length > 0
                ? search.FixedTimes.Select(x => new TimeRow(NormalizeLoadedTime(x))).ToList()
                : [new("09:00")]
        };
    }

    private static bool TryNormalizeTime(string value, out string normalized)
    {
        string candidate = value.Trim();
        foreach (string format in AcceptedTimeFormats)
        {
            if (!TimeOnly.TryParseExact(candidate, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time)
                || time.Ticks % TimeSpan.TicksPerMinute != 0)
                continue;

            normalized = time.ToString("HH:mm", CultureInfo.InvariantCulture);
            return true;
        }

        normalized = "";
        return false;
    }

    private static string NormalizeLoadedTime(string value) =>
        TryNormalizeTime(value, out string normalized) ? normalized : value;

    private static string NormalizeTime(string value) =>
        TryNormalizeTime(value, out string normalized)
            ? normalized
            : throw new ArgumentException("Укажите корректное время запуска в формате ЧЧ:ММ.");

    public sealed class TimeRow(string value) { public string Value { get; set; } = value; }
}
