using LandErp.Collector.Contracts.V1;
using LandErp.ParserSpike.LocalCollection;
using System.Text.Json;

namespace LandErp.ParserSpike.ServerIntegration;

/// <summary>Adapts one permitted work item to the unchanged local collection pipeline.</summary>
public sealed class ServerCoordinator(LocalStore store, QueueRunner runner, ServerOutbox outbox, ServerAdapter adapter) : IAsyncDisposable
{
    private readonly SemaphoreSlim commands = new(1, 1);
    private DateTimeOffset nextPoll;
    private int emptyClaims;
    private volatile bool acceptNewWork = true;
    public bool AcceptNewWork { get => acceptNewWork; set => acceptNewWork = value; }
    public string Status { get; private set; } = "Server mode подключён. Local mode доступен независимо.";
    public DeliverySummary? LastDelivery { get; private set; }
    public LocalServerWork? CurrentWork => outbox.ReadWork();
    public Task<CollectorSearchView> UpdateSearchAsync(UpdateCollectorSearch command, CancellationToken token) => adapter.UpdateSearchAsync(command, token);
    public Task<CollectorGroupView> UpdateGroupAsync(UpdateCollectorGroup command, CancellationToken token) => adapter.UpdateGroupAsync(command, token);
    public Task EnqueueSearchAsync(Guid searchId, CancellationToken token) => adapter.EnqueueSearchAsync(searchId, token);

    public async Task StartWorkAsync(CancellationToken token)
    {
        await commands.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (runner.IsRunning) throw new InvalidOperationException("Сначала завершите или остановите текущий локальный сбор.");
            await adapter.RegisterAsync(token).ConfigureAwait(false);
            LocalServerWork? old = outbox.ReadWork();
            if (old?.LocalJobId != null) PrepareResult(old);
            try { await outbox.FlushAsync(adapter, token).ConfigureAwait(false); }
            catch (ServerDeliveryException exception) when (exception.Code == CollectorErrorCodes.LeaseExpiredOrReplaced)
            { /* Reclaim below; all local observations remain available. */ }
            if (outbox.Pending().Length == 0) { outbox.SaveWork(null); old = null; }
            CollectionWork? work = await adapter.ClaimAsync(token).ConfigureAwait(false);
            if (work == null) { Status = "Нет новой разрешённой работы. Local mode доступен."; return; }
            if (old != null && old.Work.JobId != work.JobId)
                throw new InvalidOperationException("Сначала разрешите конфликт доставки прежней работы; локальные результаты сохранены.");
            outbox.SupersedeLease(work.JobId, work.LeaseId);
            if (old?.LocalJobId != null)
            {
                LocalServerWork renewed = new(work, old.LocalJobId); outbox.SaveWork(renewed); PrepareResult(renewed);
                await outbox.FlushAsync(adapter, token).ConfigureAwait(false);
                LastDelivery = outbox.Summary(work.JobId); outbox.SaveWork(null);
                Status = LastDelivery?.Display ?? "Сохранённый результат доставлен после обновления lease."; return;
            }
            StartClaimedWork(work, token);
        }
        finally { commands.Release(); }
    }
    public async Task TickAsync(CancellationToken token)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now < nextPoll || !await commands.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            LocalServerWork? work = outbox.ReadWork();
            if (work == null && outbox.Pending().Length == 0 && !runner.IsRunning)
            {
                await adapter.RegisterAsync(token).ConfigureAwait(false);
                if (!AcceptNewWork)
                {
                    await adapter.HeartbeatAsync(new(RuntimeState: AgentRuntimeState.Idle), token).ConfigureAwait(false);
                    nextPoll = now.AddSeconds(45); Status = "На связи. Автоматическая работа выключена."; return;
                }
                CollectionWork? claimed = await adapter.ClaimAsync(token).ConfigureAwait(false);
                if (claimed == null)
                {
                    emptyClaims = Math.Min(emptyClaims + 1, 5);
                    int delay = Math.Min(120, 5 * (1 << emptyClaims)) + Random.Shared.Next(0, 5);
                    nextPoll = now.AddSeconds(delay);
                    Status = "Подключено. Ожидаем новое задание."; return;
                }
                emptyClaims = 0;
                StartClaimedWork(claimed, token); return;
            }
            if (work is { LocalJobId: null } && !runner.IsRunning && AcceptNewWork)
            {
                // Revalidate ownership after stop/restart; the persisted lease may already have expired.
                CollectionWork? claimed = await adapter.ClaimAsync(token).ConfigureAwait(false);
                if (claimed == null) { outbox.SaveWork(null); Status = "Ожидаем новое задание."; return; }
                StartClaimedWork(claimed, token); return;
            }
            if (work is { LocalJobId: null } && !AcceptNewWork)
            {
                // A claim that raced with Stop is completed explicitly without opening the source.
                outbox.Enqueue(new(Guid.CreateVersion7(), work.Work.JobId, work.Work.LeaseId, CollectionOutcome.Interrupted, [], true,
                    CollectionResultReasonCodes.AgentInterrupted, [], new(0, null, false, false, 0, 0, work.Work.MaxPages)));
                await outbox.FlushAsync(adapter, token).ConfigureAwait(false); outbox.SaveWork(null);
                nextPoll = DateTimeOffset.UtcNow;
                Status = "Остановлено. Новые задания не запускаются."; return;
            }
            if (work?.LocalJobId != null && !runner.IsRunning)
            {
                if (work.ResultReasonCode == CollectionResultReasonCodes.LeaseExpiredOrReplaced)
                {
                    CollectionWork? reclaimed = await adapter.ClaimAsync(token).ConfigureAwait(false);
                    if (reclaimed?.JobId == work.Work.JobId)
                    {
                        outbox.SupersedeLease(reclaimed.JobId, reclaimed.LeaseId);
                        LocalServerWork renewed = new(reclaimed, work.LocalJobId); outbox.SaveWork(renewed);
                        PrepareResult(renewed); await outbox.FlushAsync(adapter, token).ConfigureAwait(false);
                        LastDelivery = outbox.Summary(reclaimed.JobId);
                        outbox.SaveWork(null); nextPoll = DateTimeOffset.UtcNow;
                        Status = LastDelivery?.Display ?? "Сохранённый результат доставлен после обновления lease."; return;
                    }
                    PrepareResult(work); outbox.RetainLocally(work.Work.JobId, work.ResultReasonCode); outbox.SaveWork(null);
                    if (reclaimed != null) { StartClaimedWork(reclaimed, token); return; }
                    nextPoll = DateTimeOffset.UtcNow.AddSeconds(2); Status = "Старое выполнение завершено; ожидаем повтор от Server."; return;
                }
                PrepareResult(work); await outbox.FlushAsync(adapter, token).ConfigureAwait(false);
                LastDelivery = outbox.Summary(work.Work.JobId);
                outbox.SaveWork(null); nextPoll = DateTimeOffset.UtcNow;
                Status = LastDelivery?.Display ?? "Работа и локальные наблюдения доставлены в Server.";
            }
            else
            {
                CollectionJob? job = store.Jobs().FirstOrDefault(item => item.Id == work?.LocalJobId);
                CollectionOutcome? status = job?.State == JobState.AwaitingManualAction
                    ? job.Reason.Contains("Captcha", StringComparison.OrdinalIgnoreCase) ? CollectionOutcome.Captcha
                        : job.Reason.Contains("Authentication", StringComparison.OrdinalIgnoreCase) ? CollectionOutcome.AuthenticationRequired
                        : job.Reason.Contains("RateLimited", StringComparison.OrdinalIgnoreCase) ? CollectionOutcome.RateLimited : CollectionOutcome.Unknown
                    : null;
                SourceRuntimeState? sourceState = job == null ? null : job.State == JobState.AwaitingManualAction
                    ? status switch
                    {
                        CollectionOutcome.Captcha => SourceRuntimeState.Captcha,
                        CollectionOutcome.AuthenticationRequired => SourceRuntimeState.AuthenticationRequired,
                        CollectionOutcome.RateLimited => SourceRuntimeState.RateLimited,
                        _ => SourceRuntimeState.Unknown
                    }
                    : SourceRuntimeState.Ready;
                PageJournal[] journal = job == null ? [] : store.Journal(job.Id);
                CollectionProgress? progress = job == null ? null : new(job.Page, job.Limit, journal.Sum(item => item.Count), null,
                    job.State == JobState.AwaitingManualAction ? CollectionProgressPhase.WaitingForUser : CollectionProgressPhase.ReadingPage,
                    journal.Length == 0 ? null : journal.Max(item => item.Time));
                AgentRuntimeState runtime = work == null ? AgentRuntimeState.Idle
                    : job?.State == JobState.AwaitingManualAction ? AgentRuntimeState.AwaitingManualAction : AgentRuntimeState.Parsing;
                await adapter.HeartbeatAsync(work == null ? new(RuntimeState: runtime)
                    : new(work.Work.JobId, work.Work.LeaseId, status, runtime, sourceState, progress), token).ConfigureAwait(false);
                nextPoll = now.AddSeconds(45);
            }
        }
        catch (ServerDeliveryException exception)
        {
            if (exception.Code is "LEASE_EXPIRED" or "LEASE_REPLACED")
            {
                LocalServerWork? rejected = outbox.ReadWork();
                if (rejected?.LocalJobId == null)
                {
                    if (rejected != null) outbox.RetainLocally(rejected.Work.JobId, exception.Code);
                    outbox.SaveWork(null); nextPoll = now.AddSeconds(2);
                    Status = "Lease изменился до запуска. Результаты сохранены на компьютере; ожидаем следующую работу Server."; return;
                }
                outbox.SaveWork(rejected with { ResultReasonCode = CollectionResultReasonCodes.LeaseExpiredOrReplaced });
                runner.Stop();
                nextPoll = now.AddSeconds(2);
                Status = "Lease изменился. Полезные данные сохранены; автоматически сверяемся с Server.";
                return;
            }
            if (exception.Code is "RESULT_SUPERSEDED" or "WORK_NOT_ACTIVE")
            {
                runner.Stop(); LocalServerWork? rejected = outbox.ReadWork();
                if (!runner.IsRunning && rejected != null)
                {
                    PrepareResult(rejected with { ResultReasonCode = CollectionResultReasonCodes.LeaseExpiredOrReplaced });
                    outbox.RetainLocally(rejected.Work.JobId, exception.Code); outbox.SaveWork(null);
                }
                nextPoll = now.AddSeconds(2);
                Status = "Сервер больше не принимает это задание. Результаты сохранены на компьютере; ожидаем новое задание.";
                return;
            }
            nextPoll = now.AddSeconds(15 + Random.Shared.Next(0, 6));
            Status = exception.Retryable ? "Нет связи с сервером. Результаты сохранены; повторим подключение автоматически."
                : "Сервер отклонил запрос: " + exception.Code + ". Результаты сохранены на компьютере.";
        }
        finally { commands.Release(); }
    }
    private void StartClaimedWork(CollectionWork work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        outbox.SaveWork(new(work, null));
        // Stop can be clicked while claim is in flight. Keep the lease, but never start a browser after that click.
        if (!AcceptNewWork) return;
        SourceSite source = Enum.Parse<SourceSite>(work.Source.ToString());
        SearchLink link = store.EnsureServerWorkLink(work.Label, work.SearchUrl, source);
        CollectionSettings settings = store.Settings() with { MaxPages = work.MaxPages };
        _ = runner.StartAsync(settings, force: true, onlyLinkId: link.Id, serverManaged: true);
        CollectionJob local = store.Jobs(runner.BatchId).Single();
        outbox.SaveWork(new(work, local.Id)); nextPoll = DateTimeOffset.UtcNow.AddSeconds(45);
        Status = "Разрешённая работа запущена локально. CAPTCHA и авторизация — вручную.";
    }
    public async Task DeliverAsync(CancellationToken token)
    {
        await commands.WaitAsync(token).ConfigureAwait(false);
        try
        {
            LocalServerWork? work = outbox.ReadWork();
            if (work?.LocalJobId != null && !runner.IsRunning) PrepareResult(work);
            await outbox.FlushAsync(adapter, token).ConfigureAwait(false);
            if (!runner.IsRunning)
            {
                if (work != null) LastDelivery = outbox.Summary(work.Work.JobId);
                outbox.SaveWork(null);
            }
            Status = LastDelivery?.Display ?? "Очередь доставки обработана. Локальные данные сохранены.";
        }
        finally { commands.Release(); }
    }
    public async Task<CollectorWorkspace> ReadWorkspaceAsync(CancellationToken token)
    {
        await commands.WaitAsync(token).ConfigureAwait(false);
        try { return await adapter.ReadWorkspaceAsync(token).ConfigureAwait(false); }
        finally { commands.Release(); }
    }
    public async Task<CollectorGroupView> CreateGroupAsync(CreateCollectorGroup command, CancellationToken token)
    {
        await commands.WaitAsync(token).ConfigureAwait(false);
        try { return await adapter.CreateGroupAsync(command, token).ConfigureAwait(false); }
        finally { commands.Release(); }
    }
    public async Task<CollectorSearchView> CreateSearchAsync(CreateCollectorSearch command, CancellationToken token)
    {
        await commands.WaitAsync(token).ConfigureAwait(false);
        try { return await adapter.CreateSearchAsync(command, token).ConfigureAwait(false); }
        finally { commands.Release(); }
    }
    private void PrepareResult(LocalServerWork work)
    {
        if (outbox.HasDelivery(work.Work.JobId, work.Work.LeaseId) || work.LocalJobId == null) return;
        CollectionJob job = store.Jobs().Single(item => item.Id == work.LocalJobId);
        if (job.State is JobState.Running or JobState.Pending or JobState.AwaitingManualAction or JobState.PausedByUser) return;
        using MemoryStream stream = new(); store.ExportJob(job.Id, stream);
        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        ObservationEnvelope[] observations = document.RootElement.GetProperty("observations").EnumerateArray().Select(item =>
            new ObservationEnvelope(item.GetProperty("resultId").GetString()!, Map(LocalJson.Read<ListingObservation>(item.GetProperty("observation").GetRawText())))).ToArray();
        CollectionResult[] results = CollectionResultClassifier.Build(work.Work.JobId, work.Work.LeaseId, job,
            observations, store.Journal(job.Id), store.ReadMapScope(job.Id), store.Completion(job.Id), work.ResultReasonCode);
        outbox.EnqueueMany(results);
    }
    public static ListingData Map(ListingObservation observation) => new()
    {
        Source = Enum.Parse<ListingSource>(observation.Source.ToString()), ExternalId = observation.ExternalId,
        Url = observation.Url, ObservedAt = observation.ObservedAtUtc, AdapterVersion = observation.AdapterVersion,
        Provenance = observation.Provenance, Title = Text(observation.Title), Location = Text(observation.Location),
        Description = Text(observation.Description), SellerName = Text(observation.SellerName),
        CadastralNumber = Text(observation.CadastralNumber), SourcePublishedAt = observation.SourcePublishedAtUtc,
        Latitude = observation.Latitude.Parsed, Longitude = observation.Longitude.Parsed,
        DeclaredLandTypes = observation.DeclaredLandTypes.Select(value => Enum.Parse<ListingLandType>(value.ToString())).Distinct().ToArray(),
        Price = Number(observation.Price), AreaSquareMeters = Number(observation.AreaSquareMeters),
        PhotoUrls = observation.PhotoUrls.Distinct(StringComparer.Ordinal).Take(100).ToArray(), Warnings = observation.Warnings
    };
    private static TextField Text(TextValue value) => new(Enum.Parse<FieldPresence>(value.Presence.ToString()), value.Raw);
    private static DecimalField Number(NumberValue value) => new(Enum.Parse<FieldPresence>(value.Presence.ToString()), value.Raw, value.Parsed);
    public async ValueTask DisposeAsync() { await commands.WaitAsync().ConfigureAwait(false); commands.Dispose(); GC.SuppressFinalize(this); }
}
