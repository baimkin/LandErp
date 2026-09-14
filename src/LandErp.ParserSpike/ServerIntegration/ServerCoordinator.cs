using LandErp.Collector.Contracts.V1;
using LandErp.ParserSpike.LocalCollection;
using System.Text.Json;

namespace LandErp.ParserSpike.ServerIntegration;

/// <summary>Adapts one permitted work item to the unchanged local collection pipeline.</summary>
public sealed class ServerCoordinator(LocalStore store, QueueRunner runner, ServerOutbox outbox, ServerAdapter adapter) : IAsyncDisposable
{
    private readonly SemaphoreSlim commands = new(1, 1);
    private DateTimeOffset lastHeartbeat;
    public string Status { get; private set; } = "Server mode подключён. Local mode доступен независимо.";
    public LocalServerWork? CurrentWork => outbox.ReadWork();

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
            catch (ServerDeliveryException exception) when (exception.Code == "WORK_OR_IDEMPOTENCY_CONFLICT")
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
                await outbox.FlushAsync(adapter, token).ConfigureAwait(false); outbox.SaveWork(null);
                Status = "Сохранённый результат доставлен после обновления lease."; return;
            }
            outbox.SaveWork(new(work, null));
            SourceSite source = Enum.Parse<SourceSite>(work.Source.ToString());
            string key = SearchUrls.Normalize(work.SearchUrl, source).Key;
            SearchLink? link = store.Links().FirstOrDefault(item => item.Enabled && SearchUrls.Normalize(item.Url, item.Source).Key == key);
            link ??= store.SaveLink(work.Label, work.SearchUrl, selected: false, manualSource: source);
            CollectionSettings settings = store.Settings() with { MaxPages = work.MaxPages };
            _ = runner.StartAsync(settings, force: true, onlyLinkId: link.Id);
            CollectionJob local = store.Jobs(runner.BatchId).Single();
            outbox.SaveWork(new(work, local.Id)); lastHeartbeat = DateTimeOffset.UtcNow;
            Status = "Разрешённая работа запущена локально. CAPTCHA и авторизация — вручную.";
        }
        finally { commands.Release(); }
    }
    public async Task TickAsync(CancellationToken token)
    {
        if (DateTimeOffset.UtcNow - lastHeartbeat < TimeSpan.FromSeconds(45) || !await commands.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            lastHeartbeat = DateTimeOffset.UtcNow;
            LocalServerWork? work = outbox.ReadWork();
            if (work?.LocalJobId != null && !runner.IsRunning)
            {
                PrepareResult(work); await outbox.FlushAsync(adapter, token).ConfigureAwait(false);
                outbox.SaveWork(null); Status = "Работа и локальные наблюдения доставлены в Server.";
            }
            else
            {
                CollectionJob? job = store.Jobs().FirstOrDefault(item => item.Id == work?.LocalJobId);
                CollectionOutcome? status = job?.State == JobState.AwaitingManualAction
                    ? job.Reason.Contains("Captcha", StringComparison.OrdinalIgnoreCase) ? CollectionOutcome.Captcha
                        : job.Reason.Contains("Authentication", StringComparison.OrdinalIgnoreCase) ? CollectionOutcome.AuthenticationRequired
                        : job.Reason.Contains("RateLimited", StringComparison.OrdinalIgnoreCase) ? CollectionOutcome.RateLimited : CollectionOutcome.Unknown
                    : null;
                await adapter.HeartbeatAsync(work == null ? new() : new(work.Work.JobId, work.Work.LeaseId, status), token).ConfigureAwait(false);
            }
        }
        catch (ServerDeliveryException exception) { Status = exception.Message + ". Локальный сбор и результаты сохранены. Повторите доставку/получение работы."; }
        finally { commands.Release(); }
    }
    public async Task DeliverAsync(CancellationToken token)
    {
        await commands.WaitAsync(token).ConfigureAwait(false);
        try
        {
            LocalServerWork? work = outbox.ReadWork();
            if (work?.LocalJobId != null && !runner.IsRunning) PrepareResult(work);
            await outbox.FlushAsync(adapter, token).ConfigureAwait(false);
            if (!runner.IsRunning) outbox.SaveWork(null);
            Status = "Очередь доставки обработана. Локальные данные сохранены.";
        }
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
        CollectionOutcome outcome = job.State switch { JobState.Completed => CollectionOutcome.Success,
            JobState.LimitReached => CollectionOutcome.LimitReached, JobState.StoppedInterrupted => CollectionOutcome.Interrupted,
            _ => job.Reason.StartsWith("Captcha", StringComparison.Ordinal) ? CollectionOutcome.Captcha
                : job.Reason.StartsWith("AuthenticationRequired", StringComparison.Ordinal) ? CollectionOutcome.AuthenticationRequired
                : job.Reason.StartsWith("RateLimited", StringComparison.Ordinal) ? CollectionOutcome.RateLimited : CollectionOutcome.SourceError };
        List<CollectionResult> results = [];
        foreach (ObservationEnvelope[] chunk in observations.Chunk(10)) results.Add(new(Guid.CreateVersion7(), work.Work.JobId, work.Work.LeaseId, CollectionOutcome.Success, chunk, false));
        results.Add(new(Guid.CreateVersion7(), work.Work.JobId, work.Work.LeaseId, outcome, [], true));
        outbox.EnqueueMany(results);
    }
    public static ListingData Map(ListingObservation observation) => new()
    {
        Source = Enum.Parse<ListingSource>(observation.Source.ToString()), ExternalId = observation.ExternalId,
        Url = observation.Url, ObservedAt = observation.ObservedAtUtc, AdapterVersion = observation.AdapterVersion,
        Provenance = observation.Provenance, Title = Text(observation.Title), Location = Text(observation.Location),
        Description = Text(observation.Description), SellerName = Text(observation.SellerName), Price = Number(observation.Price),
        AreaSquareMeters = Number(observation.AreaSquareMeters), PhotoUrls = observation.PhotoUrls.Distinct(StringComparer.Ordinal).Take(100).ToArray(), Warnings = observation.Warnings
    };
    private static TextField Text(TextValue value) => new(Enum.Parse<FieldPresence>(value.Presence.ToString()), value.Raw);
    private static DecimalField Number(NumberValue value) => new(Enum.Parse<FieldPresence>(value.Presence.ToString()), value.Raw, value.Parsed);
    public async ValueTask DisposeAsync() { await commands.WaitAsync().ConfigureAwait(false); commands.Dispose(); GC.SuppressFinalize(this); }
}
