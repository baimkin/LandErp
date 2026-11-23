using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Server.Security;
using Microsoft.Extensions.Logging;

namespace LandErp.Server.Components.Pages;

public partial class Collectors
{
    private readonly Dictionary<Guid, EnqueueAttempt> enqueueStates = [];
    private static readonly Action<ILogger, string, Guid, string, string, Exception?> LogEnqueueFailure =
        LoggerMessage.Define<string, Guid, string, string>(LogLevel.Warning, new EventId(1110, "COLLECTION_ENQUEUE_FAILED"),
            "Collection enqueue {OperationId}; search {SearchId}; phase {Phase}; failure type {FailureType}");
    private sealed class EnqueueAttempt
    {
        public CollectionEnqueueStage Stage { get; set; } = CollectionEnqueueStage.Sending;
        public int ReadVersion { get; set; }
        public string? Message { get; set; }
        public string? RefreshMessage { get; set; }
        public bool Checking { get; set; }
        public Guid? PreviousJobId { get; init; }
    }
    private CollectionRunAction RunAction(SearchView search, bool retry = true) => CollectionRunAction.For(search,
        enqueueStates.TryGetValue(search.Id, out var state) ? state.Stage : CollectionEnqueueStage.None,
        CanManageCollection, Busy, databaseReady, retry, archived: IsArchivedSearch(search));
    private string? QueueMessage(Guid id)
    {
        if (!enqueueStates.TryGetValue(id, out var state)) return null;
        return state.Message is null ? state.RefreshMessage : state.RefreshMessage is null ? state.Message : state.Message + " " + state.RefreshMessage;
    }
    private bool IsCheckingQueue(Guid id) => enqueueStates.TryGetValue(id, out var state) && state.Checking;

    private async Task EnqueueAsync(Guid id)
    {
        var search = FindSearch(id);
        if (search is null || !CanRun(search)) return;
        var attempt = new EnqueueAttempt { PreviousJobId = search.LastRun?.JobId };
        enqueueStates[id] = attempt; // Same SearchId shares state in both tabs; no second click during any await.
        StateHasChanged();
        string operation = Guid.CreateVersion7().ToString();
        try
        {
            var auth = await Authentication.GetAuthenticationStateAsync();
            if (auth.User.Identity?.IsAuthenticated != true) throw new AccessDeniedException();
            var subject = PermissionAuthorization.SubjectFrom(auth.User);
            await Administration.EnqueueAsync(subject, id, operation, lifetime.Token);
            attempt.Stage = CollectionEnqueueStage.Confirmed;
            attempt.ReadVersion = collectionReadVersion;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            bool rejected = exception is AccessDeniedException or ArgumentException;
            attempt.Stage = rejected ? CollectionEnqueueStage.Rejected : CollectionEnqueueStage.Uncertain;
            attempt.ReadVersion = collectionReadVersion;
            LogEnqueueFailure(Logger, operation, id, "Command", exception.GetType().Name, null);
            string reason = exception switch
            {
                AccessDeniedException => "Запуск отклонён. Проверьте доступ и состояние поиска.",
                ArgumentException => exception.Message,
                _ => "Ответ на запуск не получен. Сначала проверьте состояние задания."
            };
            attempt.Message = $"{reason} Номер обращения: {operation}.";
        }
        // The command result is rendered before the potentially slow health/history read.
        // Never use the page-wide Busy flag for an independent enqueue.
        if (!lifetime.IsCancellationRequested)
        {
            StateHasChanged();
            await RefreshQueueAsync(id);
        }
    }
    private async Task RefreshQueueAsync(Guid id)
    {
        if (!enqueueStates.TryGetValue(id, out var attempt) || attempt.Checking) return;
        attempt.Checking = true;
        attempt.RefreshMessage = null;
        try { await ReadAsync(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (exception is AccessDeniedException) { view = null; refreshMessage = "Доступ изменился. Обновите страницу."; }
            string operation = Guid.CreateVersion7().ToString();
            LogEnqueueFailure(Logger, operation, id, "Refresh", exception.GetType().Name, null);
            string message = attempt.Stage == CollectionEnqueueStage.Confirmed
                ? "Запуск подтверждён, но состояние не обновилось. Повторно отправлять задание не нужно."
                : "Состояние задания не обновилось. Проверьте его перед повторной отправкой.";
            attempt.RefreshMessage = $"{message} Номер обращения: {operation}.";
        }
        finally { attempt.Checking = false; }
        if (!lifetime.IsCancellationRequested) StateHasChanged();
    }
    private void ReconcileEnqueues(int readVersion)
    {
        foreach (var (id, attempt) in enqueueStates.ToArray())
        {
            // Only a read started after acknowledgement can replace the local confirmed/unknown state.
            if (attempt.Stage == CollectionEnqueueStage.Sending || readVersion <= attempt.ReadVersion) continue;
            var search = FindSearch(id);
            if (attempt.Stage == CollectionEnqueueStage.Uncertain && search is not null && search.ActiveRun is null
                && search.LastRun?.JobId == attempt.PreviousJobId)
            {
                attempt.Stage = CollectionEnqueueStage.Rejected;
                attempt.Message = "Активного задания не найдено. Можно повторить запуск. " + attempt.Message;
            }
            else if (attempt.Stage != CollectionEnqueueStage.Rejected || search?.ActiveRun is not null)
                enqueueStates.Remove(id);
        }
    }
}

public enum CollectionEnqueueStage { None, Sending, Confirmed, Uncertain, Rejected }
public sealed record CollectionRunAction(string Label, string Help, bool Disabled)
{
    public static CollectionRunAction For(SearchView search, CollectionEnqueueStage stage, bool canManage, bool busy, bool ready, bool retry = true, bool archived = false)
    {
        if (archived) return new("В архиве", "Зона в архиве. Новые запуски недоступны.", true);
        if (stage == CollectionEnqueueStage.Sending) return new("Отправляем…", "Добавляем задание в очередь.", true);
        if (search.ActiveRun is { } run) return run.State == "Pending"
            ? new("В очереди", "Ожидает свободный совместимый парсер.", true)
            : new("Выполняется", "Парсер выполняет задание.", true);
        if (stage == CollectionEnqueueStage.Confirmed) return new("В очереди", "Добавление в очередь подтверждено. Обновляем состояние.", true);
        if (stage == CollectionEnqueueStage.Uncertain) return new("Не подтверждено", "Проверьте состояние перед повторной отправкой.", true);
        if (!search.Enabled) return new("На паузе", "Запуск недоступен: поиск на паузе. Включите «Поиск активен» в настройках.", true);
        if (!canManage) return new(retry ? "Повторить" : "Запустить", "Нет права запускать сбор.", true);
        if (!ready) return new(retry ? "Повторить" : "Запустить", "База данных недоступна.", true);
        if (busy) return new(retry ? "Повторить" : "Запустить", "Сохраняются настройки страницы.", true);
        return new(retry ? "Повторить" : "Запустить", "Добавить задание в очередь.", false);
    }
}
