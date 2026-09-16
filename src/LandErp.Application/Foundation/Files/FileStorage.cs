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

public sealed record FileWriteResult(string StorageKey, string Sha256, long SizeBytes);

public interface IFileStorage
{
    Task<FileWriteResult> WriteAsync(Guid stableFileId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
    Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken);
}
