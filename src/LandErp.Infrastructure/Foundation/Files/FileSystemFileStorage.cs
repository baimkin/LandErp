using System.Security.Cryptography;
using LandErp.Application.Foundation.Files;

namespace LandErp.Infrastructure.Foundation.Files;

/// <summary>Local/Test adapter for the provider-neutral storage contract. Production must configure another approved backend.</summary>
public sealed class FileSystemFileStorage(string root) : IFileStorage, IFileStorageHealth
{
    private readonly string root = Path.GetFullPath(root);

    public async Task<FileWriteResult> WriteAsync(Guid stableFileId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        string hash = Convert.ToHexString(SHA256.HashData(content.Span)).ToLowerInvariant();
        string key = $"{stableFileId:N}_{hash}.bin";
        string path = Resolve(key);
        if (File.Exists(path))
        {
            await EnsureExistingContentAsync(path, hash, content.Length, cancellationToken);
            return new(key, hash, content.Length);
        }

        string temporaryPath = Resolve($".{key}.{Guid.CreateVersion7():N}.tmp");
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            try { File.Move(temporaryPath, path, false); }
            catch (IOException) when (File.Exists(path))
            {
                await EnsureExistingContentAsync(path, hash, content.Length, cancellationToken);
            }

            return new(key, hash, content.Length);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(Resolve(storageKey), cancellationToken);

    private static async Task EnsureExistingContentAsync(string path, string expectedHash, int expectedSize, CancellationToken cancellationToken)
    {
        FileInfo info = new(path);
        if (info.Length != expectedSize)
            throw new FileStorageException("STORAGE_CONTENT_CONFLICT", false, "Сохранённый файл не соответствует ожидаемому содержимому.");
        byte[] existing = await File.ReadAllBytesAsync(path, cancellationToken);
        string actualHash = Convert.ToHexString(SHA256.HashData(existing)).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            throw new FileStorageException("STORAGE_CONTENT_CONFLICT", false, "Сохранённый файл не соответствует ожидаемому содержимому.");
    }

    public Task<FileStorageHealth> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(root))
            return Task.FromResult(new FileStorageHealth(false, "STORAGE_LOCAL_MISSING"));
        try
        {
            using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator();
            _ = entries.MoveNext();
            return Task.FromResult(new FileStorageHealth(true, "STORAGE_READY"));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(new FileStorageHealth(false, "STORAGE_LOCAL_UNAVAILABLE"));
        }
        catch (IOException)
        {
            return Task.FromResult(new FileStorageHealth(false, "STORAGE_LOCAL_UNAVAILABLE"));
        }
    }

    private string Resolve(string key)
    {
        if (key.Length == 0 || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(key) != key)
            throw new InvalidOperationException("Некорректный ключ файлового хранилища.");
        string path = Path.GetFullPath(Path.Combine(root, key));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Некорректный ключ файлового хранилища.");
        return path;
    }
}
