using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Collector.Contracts.V1;

namespace LandErp.Server.Components.Pages;

/// <summary>Only persisted result evidence determines a cause; Partial alone never proves a lost area.</summary>
public sealed record CollectionResultPresentation(string Label, string Tone, string Help, string Category, bool IsIssue)
{
    public static CollectionResultPresentation From(CollectionRunView? run) => run is null
        ? new("Ещё не запускался", "neutral", "Результат появится после первого запуска.", "", false)
        : From(run.State, run.ResultCode, run.ReasonCode, run.Warnings, run.AttentionRequired);

    public static CollectionResultPresentation From(CollectionJobView job) =>
        From(job.State, job.ResultCode, job.ReasonCode, job.Warnings, job.AttentionRequired);

    public static CollectionResultPresentation From(string state, string result, string reason, IEnumerable<string> warnings, bool attention)
    {
        if (state == "Pending") return new("В очереди", "info", "Ожидает свободный совместимый парсер.", "", false);
        if (state == "Leased" && result is not ("Captcha" or "AuthenticationRequired" or "RateLimited"))
            return new("Выполняется", "info", "Парсер собирает объявления. Результат появится после завершения.", "", false);
        // Explicit access evidence takes precedence over incidental area warnings.
        if (result == "Captcha") return new("Нужна CAPTCHA", "warning", "На компьютере парсера откройте вкладку источника, пройдите проверку и продолжите сбор.", "access", true);
        if (result == "AuthenticationRequired") return new("Нужен вход на сайт", "warning", "Войдите на сайт источника в локальном Parser и продолжите сбор.", "access", true);
        if (result == "RateLimited" || state == "RateLimited") return new("Источник ограничил доступ", "warning", "Источник ограничил запросы. Дождитесь снятия ограничения; частые повторы могут продлить его.", "access", true);
        var codes = warnings.Append(reason).ToHashSet(StringComparer.Ordinal);
        // A successful final result supersedes transient warnings collected while its page loaded.
        if (state == "Completed") return new("Собрано", "success", codes.Contains(CollectionResultReasonCodes.CountHintMismatch)
            ? "Выдача обработана. Количество отличается от подсказки источника." : "Выдача обработана, сбор завершён.", "", false);
        if (state == "LimitReached") return new("Достигнут лимит страниц", "neutral", "Собрано до настроенного предела страниц. Это штатное завершение.", "", false);
        if (codes.Contains("AREA_GEOMETRY_CHANGED")) return new("Контур изменился", "danger", "Откройте поиск на сайте, проверьте выделенную область и сохраните актуальную ссылку.", "area", true);
        if (codes.Contains("AREA_FILTERS_CHANGED")) return new("Условия поиска изменились", "danger", "Проверьте область и фильтры на сайте источника, затем сохраните актуальную ссылку.", "area", true);
        if (codes.Overlaps(["AREA_SELECTION_UNCONFIRMED", "AREA_REFERENCE_UNAVAILABLE", "AREA_GEOMETRY_UNAVAILABLE", "AREA_LOADING"]))
            return new("Область не подтверждена", "warning", "Парсер не подтвердил область. Проверьте загрузку карты и выделение; при необходимости замените ссылку.", "area", true);
        if (codes.Contains("AREA_COORDINATES_MISSING")) return new("Не хватает координат", "warning", "Не у всех объявлений получены координаты для проверки области. Проверенные объявления сохранены.", "collection", true);
        string help = reason switch
        {
            CollectionResultReasonCodes.CountHintMismatch => "Количество отличается от подсказки источника; полнота выдачи не подтверждена.",
            CollectionResultReasonCodes.EndNotConfirmed => "Конец выдачи не подтверждён. Проверенные объявления сохранены; повторите сбор.",
            CollectionResultReasonCodes.LoadingInterrupted => "Загрузка выдачи прервалась. Повторите после восстановления связи с источником.",
            CollectionResultReasonCodes.NetworkTimeout => "Источник не ответил вовремя. Повторите сбор позже.",
            CollectionResultReasonCodes.SourceUnavailable => "Источник временно недоступен. Повторите сбор позже.",
            CollectionResultReasonCodes.LayoutChanged => "Структура страницы изменилась. Требуется проверка Parser.",
            CollectionResultReasonCodes.InvalidSearchUrl => "Ссылка или условия поиска не прошли проверку. Проверьте источник и сохраните актуальную ссылку.",
            CollectionResultReasonCodes.InvalidSourceResponse => "Парсер не распознал ответ источника. Проверьте сайт и локальный Parser.",
            CollectionResultReasonCodes.AgentInterrupted => "Работа Parser прервалась. Проверьте подключение и повторите сбор.",
            CollectionResultReasonCodes.LeaseExpiredOrReplaced => "Назначение парсера устарело. Проверьте актуальный запуск перед повтором.",
            _ => "Точная причина не передана парсером. Проверьте источник и диагностику локального Parser перед повтором."
        };
        return new(state switch { "Partial" => "Неполный сбор", "Interrupted" => "Сбор прерван", "AwaitingManualAction" => "Нужно действие", _ => "Ошибка сбора" },
            state is "Partial" or "Interrupted" or "AwaitingManualAction" ? "warning" : "danger", help, "collection", attention || state is not ("Completed" or "LimitReached"));
    }
}
