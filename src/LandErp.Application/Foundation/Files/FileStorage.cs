namespace LandErp.Application.Foundation.Files;

public enum StoredFileStatus { PendingUpload, Available, UploadFailed, Quarantined, Rejected, Deleted }

/// <summary>Provider-neutral metadata. StorageKey is infrastructure-only and is never exposed by staff read models.</summary>
public sealed class StoredFile
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string OwnerModule { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string? StorageKey { get; set; }
    public string? ExternalUrl { get; set; }
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public StoredFileStatus Status { get; set; }
    public Guid CreatedByEmployeeId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long Version { get; set; } = 1;
}

public static class FileUploadLimits
{
    public const int MaxRawFileMegabytes = 8;
    public const int MaxRawFileBytes = MaxRawFileMegabytes * 1024 * 1024;
    // 8 MiB raw becomes ~10.67 MiB as base64 before JSON fields/escaping.
    public const long MaxJsonRequestBodyBytes = 12L * 1024 * 1024;
    public const string TooLargeMessage = "Файл не должен превышать 8 МБ.";

    public static bool IsRawFileSizeAllowed(long sizeBytes) => sizeBytes is > 0 and <= MaxRawFileBytes;

    public static void EnsureRawFileSize(long sizeBytes)
    {
        if (sizeBytes <= 0) throw new ArgumentException("Файл должен быть непустым.");
        if (sizeBytes > MaxRawFileBytes) throw new FileUploadLimitException();
    }
}

public sealed class FileUploadLimitException() : ArgumentException(FileUploadLimits.TooLargeMessage);

public sealed record FileWriteResult(string StorageKey, string Sha256, long SizeBytes);

/// <summary>Only system IDs and a bounded category reach provider paths; never addresses or original names.</summary>
public sealed record FileWriteRequest(Guid FileId, Guid OrganizationId, Guid CaseId, string Purpose, string ContentType);

public sealed class FileStorageException(string code, bool retryable, string message) : IOException(message)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

public interface IFileStorage
{
    Task<FileWriteResult> WriteAsync(Guid stableFileId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
    Task<FileWriteResult> WriteAsync(FileWriteRequest request, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        WriteAsync(request.FileId, content, cancellationToken);
    Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken);
}
