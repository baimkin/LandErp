using LandErp.Application.Foundation.Files;
using Microsoft.AspNetCore.Components.Forms;

namespace LandErp.Server.Components.Procurement;

public sealed class CaseUploadBatch
{
    public sealed class Item(IBrowserFile file)
    {
        public IBrowserFile File { get; } = file;
        public Guid UploadId { get; } = Guid.CreateVersion7();
        public bool Complete { get; private set; }
        public string? Error { get; private set; }
        public string? Sha256 { get; set; }
        public string? Phase { get; set; }
        public int Percent { get; set; }
        public bool TooLarge => File.Size > FileUploadLimits.MaxRawFileBytes;
        internal void Confirm() { Complete = true; Error = null; }
        internal void Fail(string message) => Error = message;
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
        if (files.Count > FileUploadLimits.MaxBatchFiles)
            throw new ArgumentException($"Выберите не больше {FileUploadLimits.MaxBatchFiles} файлов за один раз.");
        Items = files.Select(file => new Item(file)).ToArray();
    }

    public async Task RunAsync(Func<Item, Task> upload, Action<Exception>? onError = null, Func<Task>? changed = null)
    {
        if (Busy) return;
        Busy = true;
        Started = true; StopRequested = false;
        try
        {
            // Read/upload only one file at a time. Retain each ID and skip confirmed files
            // when retrying a partial batch, including an ambiguous acknowledgement.
            foreach (Item item in Items.Where(item => !item.Complete))
            {
                if (StopRequested) break;
                if (item.TooLarge) { item.Fail(FileUploadLimits.TooLargeMessage); continue; }
                try { await upload(item); item.Confirm(); }
                catch (Exception exception)
                {
                    onError?.Invoke(exception);
                    item.Fail(exception is ArgumentException ? exception.Message
                        : "Загрузка не подтверждена. Повторите этот файл — новая копия не создаётся.");
                }
                if (changed != null) await changed();
            }
        }
        finally { Busy = false; }
    }
}
