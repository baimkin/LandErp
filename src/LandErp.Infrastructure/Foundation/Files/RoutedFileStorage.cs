using LandErp.Application.Foundation.Files;

namespace LandErp.Infrastructure.Foundation.Files;

/// <summary>Writes use the selected provider. Existing keys retain their provider;
/// cloud errors never fall back to local files with a coincidentally matching name.</summary>
public sealed class RoutedFileStorage(IFileStorage writer, IFileStorage? cloud, IFileStorage? local) : IFileStorage, IFileStorageHealth
{
    public Task<FileWriteResult> WriteAsync(Guid stableFileId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        writer.WriteAsync(stableFileId, content, cancellationToken);

    public Task<FileWriteResult> WriteAsync(FileWriteRequest request, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        writer.WriteAsync(request, content, cancellationToken);

    public Task<FileStorageHealth> CheckAsync(CancellationToken cancellationToken) =>
        writer is IFileStorageHealth health
            ? health.CheckAsync(cancellationToken)
            : Task.FromResult(new FileStorageHealth(false, "STORAGE_HEALTH_UNAVAILABLE"));

    public Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (storageKey.StartsWith("yd1:", StringComparison.Ordinal))
            return (cloud ?? throw new FileStorageException("STORAGE_DISCONNECTED", false, "Яндекс Диск отключён. Подключите хранилище для чтения этого файла."))
                .ReadAsync(storageKey, cancellationToken);
        // Legacy local files have exactly the original GUID.bin layout.
        if (storageKey.Length != 36 || !storageKey.EndsWith(".bin", StringComparison.Ordinal)
            || !Guid.TryParseExact(storageKey[..32], "N", out _))
            throw new FileStorageException("STORAGE_KEY_INVALID", false, "Некорректный ключ файлового хранилища.");
        return (local ?? throw new FileStorageException("STORAGE_LOCAL_UNAVAILABLE", false, "Локальное хранилище этого файла не подключено."))
            .ReadAsync(storageKey, cancellationToken);
    }
}
