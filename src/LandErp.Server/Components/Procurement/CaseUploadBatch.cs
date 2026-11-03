using System.Diagnostics;
using LandErp.Application.Foundation.Files;
using Microsoft.AspNetCore.Components.Forms;

namespace LandErp.Server.Components.Procurement;

public enum CaseUploadStage { Queued, Checking, Preparing, Sending, WaitingForResponse, Confirming, Complete, Failed }

public sealed class CaseUploadBatch
{
    public const int MaxParallelTransfers = 5;
    public sealed class Item(IBrowserFile file)
    {
        public IBrowserFile File { get; } = file;
        public Guid UploadId { get; } = Guid.CreateVersion7();
        public bool Complete { get; private set; }
        public string? Error { get; private set; }
        public string? Sha256 { get; set; }
        public string? Phase { get; set; }
        public int Percent { get; set; }
        public CaseUploadStage Stage { get; private set; } = CaseUploadStage.Queued;
        public long Loaded { get; set; }
        public double BytesPerSecond { get; set; }
        public long LastProgressAt { get; set; }
        private readonly Dictionary<CaseUploadStage, TimeSpan> durations = [];
        private long started, stageStarted;
        private TimeSpan elapsed;
        public TimeSpan Elapsed => started == 0 ? TimeSpan.Zero : Complete || Error != null ? elapsed : Stopwatch.GetElapsedTime(started);
        public TimeSpan Duration(CaseUploadStage stage) => durations.GetValueOrDefault(stage) +
            (started != 0 && Stage == stage && !Complete && Error == null ? Stopwatch.GetElapsedTime(stageStarted) : TimeSpan.Zero);
        public double CurrentSpeed => Stage == CaseUploadStage.Sending && LastProgressAt != 0 && Stopwatch.GetElapsedTime(LastProgressAt).TotalSeconds < 3 ? BytesPerSecond : 0;
        public bool TooLarge => File.Size > FileUploadLimits.MaxRawFileBytes;
        public void SetStage(CaseUploadStage stage)
        {
            if (started != 0) durations[Stage] = durations.GetValueOrDefault(Stage) + Stopwatch.GetElapsedTime(stageStarted);
            Stage = stage; stageStarted = Stopwatch.GetTimestamp(); Phase = null; Percent = 0; BytesPerSecond = 0;
        }
        public void SetTransferTimings(double sendingMs, double responseMs)
        {
            durations[CaseUploadStage.Sending] = TimeSpan.FromMilliseconds(Math.Max(0, sendingMs));
            durations[CaseUploadStage.WaitingForResponse] = TimeSpan.FromMilliseconds(Math.Max(0, responseMs));
        }
        internal void StartAttempt()
        {
            Error = null; Phase = null; durations.Clear(); Stage = CaseUploadStage.Queued;
            started = stageStarted = Stopwatch.GetTimestamp(); elapsed = TimeSpan.Zero;
            Loaded = 0; BytesPerSecond = 0; LastProgressAt = 0; Percent = 0;
        }
        internal void Confirm() { elapsed = Elapsed; SetStage(CaseUploadStage.Complete); Complete = true; Error = null; }
        internal void Fail(string message) { elapsed = Elapsed; SetStage(CaseUploadStage.Failed); Error = message; }
    }
    public IReadOnlyList<Item> Items { get; private set; } = [];
    public bool Busy { get; private set; }
    public bool Started { get; private set; }
    public bool StopRequested { get; private set; }
    public void Stop() => StopRequested = true;
    public bool HasPending => Items.Any(item => !item.Complete && !item.TooLarge && item.File.Size > 0);
    public void Select(IReadOnlyList<IBrowserFile> files)
    {
        if (Busy || Started) throw new InvalidOperationException("Сначала завершите текущую загрузку.");
        if (files.Count > FileUploadLimits.MaxBatchFiles) throw new ArgumentException($"Выберите не больше {FileUploadLimits.MaxBatchFiles} файлов за один раз.");
        Items = files.Select(file => new Item(file)).ToArray();
    }
    private Item[] BeginAttempt()
    {
        Busy = true; Started = true; StopRequested = false;
        var pending = Items.Where(x => !x.Complete).ToArray();
        foreach (var item in pending) item.StartAttempt();
        return pending;
    }
    private static void Reject(Item item, Exception error, Action<Exception>? onError)
    {
        onError?.Invoke(error);
        item.Fail(error is ArgumentException ? error.Message : "Загрузка не подтверждена. Повторите этот файл — новая копия не создаётся.");
    }
    public async Task RunAsync(Func<Item, Task> upload, Action<Exception>? onError = null, Func<Task>? changed = null)
    {
        if (Busy) return;
        var pending = BeginAttempt();
        try
        {
            foreach (var item in pending)
            {
                if (StopRequested) break;
                if (item.TooLarge) { item.Fail(FileUploadLimits.TooLargeMessage); continue; }
                try { await upload(item); item.Confirm(); } catch (Exception error) { Reject(item, error, onError); }
                if (changed != null) await changed();
            }
        }
        finally { Busy = false; }
    }
    public async Task RunParallelAsync(Func<Item, Task> check, Func<Item, Task> transfer,
        Func<Item, Task> confirm, Func<Task>? changed = null)
    {
        if (Busy) return;
        var pending = BeginAttempt();
        using var slots = new SemaphoreSlim(MaxParallelTransfers);
        using var hash = new SemaphoreSlim(1);
        async Task Upload(Item item)
        {
            if (item.TooLarge) { item.Fail(FileUploadLimits.TooLargeMessage); return; }
            await slots.WaitAsync();
            bool transferred = false;
            try
            {
                if (StopRequested) throw new ArgumentException("Загрузка остановлена. Можно повторить.");
                await hash.WaitAsync();
                try { if (StopRequested) throw new ArgumentException("Загрузка остановлена. Можно повторить."); await check(item); } finally { hash.Release(); }
                if (StopRequested) throw new ArgumentException("Загрузка остановлена. Можно повторить.");
                await transfer(item); transferred = true;
            }
            catch (Exception error) { Reject(item, error, null); }
            finally { slots.Release(); }
            // Provider confirmation must never occupy a network/hash slot.
            if (transferred)
            {
                try { await confirm(item); item.Confirm(); } catch (Exception error) { Reject(item, error, null); }
            }
            if (changed != null) await changed();
        }
        try { await Task.WhenAll(pending.Select(Upload)); }
        finally { Busy = false; }
    }
}