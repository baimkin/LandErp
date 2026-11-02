using LandErp.Application.Foundation.Files;
using System.Net;

namespace LandErp.Infrastructure.Foundation.Files;

public sealed partial class YandexDiskFileStorage
{
    public bool SupportsDirectUploads => true;
    private (string Relative, string Path, string Directory) DirectLocation(FileWriteRequest request, long sizeBytes, string sha256)
    {
        FileUploadLimits.EnsureRawFileSize(sizeBytes);
        if (sizeBytes > options.MaxFileBytes) throw new FileStorageException("STORAGE_SIZE", false, "Размер превышает лимит подключённого хранилища. Проверьте MaxFileBytes.");
        if (request.FileId == Guid.Empty || request.OrganizationId == Guid.Empty || request.CaseId == Guid.Empty
            || sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigit)) throw new ArgumentException("Некорректные параметры загрузки.");
        string category = request.Purpose switch { "Photo" => "Photos", "Document" => "Documents", "Video" => "Video", "Audio" => "Audio", _ => throw new ArgumentException("Тип прямой загрузки не поддерживается.") };
        string directory = $"Objects/{request.OrganizationId:N}/{request.CaseId:N}/{category}";
        string relative = $"{directory}/{request.FileId:N}_{sha256.ToLowerInvariant()}{Extension(request.ContentType)}";
        return (relative, options.Root + "/" + relative, options.Root + "/" + directory);
    }
    public async Task<DirectFileUpload> PrepareDirectUploadAsync(FileWriteRequest request, long sizeBytes, string sha256, CancellationToken ct)
    {
        var location = DirectLocation(request, sizeBytes, sha256);
        await EnsureDirectoryAsync(location.Directory, ct);
        var existing = await MetadataAsync(location.Path, ct);
        if (existing != null && existing.Type == "file" && existing.Size > 0 && existing.Sha256.Length == 64)
        {
            Verify(existing, sizeBytes, sha256);
            return new(null); // A lost PUT acknowledgement needs only server confirmation.
        }
        using var link = await ApiJsonAsync(HttpMethod.Get, "/upload", location.Path, "&overwrite=false", ct);
        return new(TransferUri(link, "PUT").AbsoluteUri);
    }
    public async Task<FileWriteResult> ConfirmDirectUploadAsync(FileWriteRequest request, long sizeBytes, string sha256, CancellationToken ct)
    {
        var location = DirectLocation(request, sizeBytes, sha256);
        // Video metadata may lag behind an acknowledged PUT. Missing hash is pending, not corruption.
        for (int attempt = 0; attempt < 30; attempt++)
        {
            var resource = await MetadataAsync(location.Path, ct);
            if (resource != null && resource.Type == "file" && resource.Size > 0 && resource.Sha256.Length == 64)
            {
                Verify(resource, sizeBytes, sha256);
                return new(Key(location.Relative), sha256.ToLowerInvariant(), sizeBytes);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        throw Error("STORAGE_UPLOAD_PENDING", true, "Яндекс Диск пока не подтвердил сохранение файла. Повторите подтверждение позже.");
    }
}