using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LandErp.Collector.Contracts.V1;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Procurement.Domain;

namespace LandErp.Server.Components.Procurement;

public static class ProcurementLabels
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    public static string Stage(string stage) => stage switch { "new" => "Новое", "analysis" => "В работе", "clarify" => "Уточнить", "monitor" => "Наблюдать", "rejected" => "Отклонён", "pending_head" => "У руководителя", "returned" => "Возвращён", "approved" => "Одобрен", "negotiation" => "Переговоры и проверки", "acquired" => "Куплено", _ => "Неизвестный этап" };
    public static string Money(decimal? money) => money?.ToString(money % 1 == 0 ? "N0" : "N2", Russian) + (money == null ? "Цена неизвестна" : " ₽");
    public static string Area(decimal? area) => area?.ToString("N2", Russian) + (area == null ? "Площадь неизвестна" : " м²");
    public static string Presence(FieldPresence presence) => presence switch { FieldPresence.NotInspected => "Не проверено", FieldPresence.Absent => "Поле отсутствует", FieldPresence.Empty => "Пустое поле", FieldPresence.Present => "Получено", _ => "Не удалось распознать" };
    public static string Source(CatalogSource source) => source switch { CatalogSource.Telegram => "Telegram", CatalogSource.Referral => "Рекомендация", CatalogSource.Agent => "Агент", CatalogSource.DirectOwner => "Собственник", CatalogSource.Manual => "Вручную", CatalogSource.Other => "Другой", _ => source.ToString() };
    public static string CheckLevel(CaseCheckLevel level) => level == CaseCheckLevel.Quick ? "Базовые проверки" : "Глубокая проверка";
    public static string CheckStatus(CaseCheckStatus status) => status switch { CaseCheckStatus.Planned => "Запланирована", CaseCheckStatus.InProgress => "В работе", CaseCheckStatus.Passed => "Пройдена", CaseCheckStatus.Issue => "Есть вопросы", _ => "Заблокирована" };
    public static string AttachmentKind(CaseAttachmentKind kind) => kind switch { CaseAttachmentKind.Photo => "Фото", CaseAttachmentKind.Document => "Документ", CaseAttachmentKind.Video => "Видео", CaseAttachmentKind.Audio => "Аудио", _ => "Ссылка" };
    public static string FileStatus(StoredFileStatus status) => status switch { StoredFileStatus.PendingUpload => "Загружается", StoredFileStatus.Available => "Доступно", StoredFileStatus.UploadFailed => "Нужна повторная загрузка", StoredFileStatus.Quarantined => "На проверке", StoredFileStatus.Rejected => "Отклонено", _ => "Удалено" };
    public static string Fact(CaseFactField field) => field switch { CaseFactField.Title => "Название", CaseFactField.Price => "Цена", CaseFactField.AreaSquareMeters => "Площадь", CaseFactField.Location => "Местоположение", _ => "Кадастровый номер" };
    public static string Accept(CaseAttachmentKind kind) => kind switch { CaseAttachmentKind.Photo => "image/*", CaseAttachmentKind.Video => "video/*", CaseAttachmentKind.Audio => "audio/*", _ => ".pdf,.txt,.csv,.docx,.xlsx" };
    public static string Time(DateTimeOffset? instant) => instant == null ? "—" : TimeZoneInfo.ConvertTime(instant.Value, TimeZoneInfo.FindSystemTimeZoneById(LandErp.Application.Foundation.DataConventions.BusinessTimeZoneId)).ToString("dd.MM.yyyy HH:mm", Russian) + " МСК";
    public static DateTimeOffset? ParseLocal(string value)
    {
        if (value.Length == 0) return null;
        if (!DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local)) throw new ArgumentException("Проверьте дату и время срока.");
        DateTime utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById(LandErp.Application.Foundation.DataConventions.BusinessTimeZoneId));
        return new(utc);
    }
    public static string LocalInput(DateTimeOffset? instant) => instant == null ? "" : TimeZoneInfo.ConvertTime(instant.Value,
        TimeZoneInfo.FindSystemTimeZoneById(LandErp.Application.Foundation.DataConventions.BusinessTimeZoneId)).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
}
public sealed class DecisionDraft
{
    [Required, MaxLength(4000)] public string Reason { get; set; } = "";
    [MaxLength(4000)] public string Clarification { get; set; } = "";
    public Guid? Target { get; set; }
    public string Due { get; set; } = "";
}
