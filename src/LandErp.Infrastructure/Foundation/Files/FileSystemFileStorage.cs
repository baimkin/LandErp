using System.Security.Cryptography;
using LandErp.Application.Foundation.Files;

namespace LandErp.Infrastructure.Foundation.Files;

/// <summary>Local/Test adapter for the provider-neutral storage contract. Production must configure another approved backend.</summary>
public sealed class FileSystemFileStorage(string root) : IFileStorage
{
    private readonly string root = Path.GetFullPath(root);

    public async Task<FileWriteResult> WriteAsync(Guid stableFileId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        string key = stableFileId.ToString("N") + ".bin";
        string path = Resolve(key);
        await using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            await stream.WriteAsync(content, cancellationToken);
        }

        string hash = Convert.ToHexString(SHA256.HashData(content.Span)).ToLowerInvariant();
        return new(key, hash, content.Length);
    }

    public Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(Resolve(storageKey), cancellationToken);

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
