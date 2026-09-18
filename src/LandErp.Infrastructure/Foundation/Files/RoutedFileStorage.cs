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
        // Read both legacy GUID.bin keys and content-addressed GUID_hash.bin keys.
        if (!IsLocalKey(storageKey))
            throw new FileStorageException("STORAGE_KEY_INVALID", false, "Некорректный ключ файлового хранилища.");
        return (local ?? throw new FileStorageException("STORAGE_LOCAL_UNAVAILABLE", false, "Локальное хранилище этого файла не подключено."))
            .ReadAsync(storageKey, cancellationToken);
    }

    private static bool IsLocalKey(string key)
    {
        if (key.Length < 36 || !key.EndsWith(".bin", StringComparison.Ordinal)
            || !Guid.TryParseExact(key.AsSpan(0, 32), "N", out _))
            return false;
        if (key.Length == 36) return true;
        if (key.Length != 101 || key[32] != '_') return false;
        foreach (char value in key.AsSpan(33, 64))
            if (!Uri.IsHexDigit(value)) return false;
        return true;
    }
}
