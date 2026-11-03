using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
namespace LandErp.Infrastructure.Modules.Procurement;
public sealed partial class ProcurementWorkspace
{
    public async Task<AttachmentMediaInfo> ReadAttachmentMediaInfoAsync(Subject subject,Guid attachmentId,CancellationToken cancellationToken)
    {
        var value=await RequireReadableAttachmentAsync(subject,attachmentId,cancellationToken,inline:true);
        RequireInline(value.File);
        return new(value.File.OriginalName,value.File.ContentType,value.File.SizeBytes,value.File.Sha256!);
    }
    public async Task<Stream> OpenAttachmentMediaAsync(Subject subject,Guid attachmentId,FileReadRange? range,CancellationToken cancellationToken)
    {
        var value=await RequireReadableAttachmentAsync(subject,attachmentId,cancellationToken,inline:true);
        RequireInline(value.File);
        if(range is {} r&&(r.Offset<0||r.Length<1||r.Offset>value.File.SizeBytes-r.Length))throw new ArgumentException("Некорректный диапазон материала.");
        if(fileStorage is not IStreamingFileStorage streaming)throw new FileStorageException("STORAGE_STREAM_UNAVAILABLE",false,"Просмотр материалов не подключён.");
        return await streaming.OpenReadAsync(value.File.StorageKey!,value.File.SizeBytes,value.File.Sha256!,range,cancellationToken);
    }
    private static void RequireInline(StoredFile file)
    {
        if(file.Status!=StoredFileStatus.Available||file.ExternalUrl!=null||file.StorageKey==null||!AttachmentMediaTypes.IsPreviewable(file.ContentType))throw new AccessDeniedException();
    }
}