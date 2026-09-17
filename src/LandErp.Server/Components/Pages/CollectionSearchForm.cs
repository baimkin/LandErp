using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;

namespace LandErp.Server.Components.Pages;

/// <summary>Form units and editable time rows; authoritative scheduling stays on Server.</summary>
public sealed class CollectionSearchForm : IValidatableObject
{
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
            ScheduleKind == CollectionScheduleKind.FixedTimes ? Times.Select(x => x.Value).Order().ToArray() : []);
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ScheduleKind == CollectionScheduleKind.Interval &&
            (IntervalUnit is not (1 or 60 or 1440) || (long)IntervalValue * IntervalUnit is < 5 or > 10080))
            yield return new("Интервал должен быть от 5 минут до 7 дней.");
        if (ScheduleKind == CollectionScheduleKind.FixedTimes)
        {
            if (Times.Count is < 1 or > 12 || Times.Any(x => !TimeOnly.TryParseExact(x.Value, "HH:mm",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
                yield return new("Укажите от 1 до 12 времён запуска.");
            if (Times.Select(x => x.Value).Distinct().Count() != Times.Count)
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
            Times = search.FixedTimes.Length > 0 ? search.FixedTimes.Select(x => new TimeRow(x)).ToList() : [new("09:00")]
        };
    }

    public sealed class TimeRow(string value) { public string Value { get; set; } = value; }
}
