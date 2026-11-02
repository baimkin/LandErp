using LandErp.Application.Foundation;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class ProcurementWorkspace
{
    public bool SupportsDirectAttachmentUploads => fileStorage is IDirectFileStorage { SupportsDirectUploads: true };
    private IDirectFileStorage DirectStorage => fileStorage as IDirectFileStorage
        ?? throw new FileStorageException("STORAGE_DIRECT_UNAVAILABLE", false, "Прямая загрузка не подключена.");
    private static string ValidateDirectMetadata(long size, string hash)
    {
        FileUploadLimits.EnsureRawFileSize(size);
        if (hash == null || hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)) throw new ArgumentException("Не удалось проверить SHA-256 файла.");
        return hash.ToLowerInvariant();
    }
    public async Task<CaseAttachmentUploadTicket> BeginAttachmentUploadAsync(Subject subject, BeginCaseAttachmentUpload command, string correlationId, CancellationToken cancellationToken)
    {
        string hash = ValidateDirectMetadata(command.SizeBytes, command.Sha256);
        if (!SupportsDirectAttachmentUploads) throw new FileStorageException("STORAGE_DIRECT_UNAVAILABLE", false, "Прямая загрузка не подключена.");
        if (command.Attachment.Content != null || command.Attachment.ExternalUrl != null || command.Attachment.UploadId is not Guid id || id == Guid.Empty)
            throw new ArgumentException("Передайте метаданные файла и постоянный ключ загрузки.");
        command = command with { Sha256 = hash };
        await AddAttachmentCoreAsync(subject, command.Attachment, correlationId, command, cancellationToken);
        return await PrepareExistingDirectAsync(subject, command.Attachment.CaseId, id, null, correlationId, cancellationToken);
    }
    public Task<CaseAttachmentUploadTicket> BeginAttachmentRetryAsync(Subject subject, BeginCaseAttachmentRetry command, string correlationId, CancellationToken cancellationToken)
    {
        command = command with { Sha256 = ValidateDirectMetadata(command.SizeBytes, command.Sha256) };
        return PrepareExistingDirectAsync(subject, command.CaseId, command.AttachmentId, command, correlationId, cancellationToken);
    }
    private async Task<(CaseAttachment Link, StoredFile File, PropertyCase Case, AccessContext Context)> RequireDirectAttachmentAsync(
        LandErpDbContext db, Subject subject, Guid caseId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var effective = await employeeAccess.ResolveAsync(subject, cancellationToken);
        var context = effective.OrganizationContext;
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={caseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        var value = await (from link in db.CaseAttachments join file in db.StoredFiles on link.StoredFileId equals file.Id
            where link.Id == attachmentId && link.PropertyCaseId == caseId && link.OrganizationId == context.OrganizationId && file.OrganizationId == context.OrganizationId
            select new { Link = link, File = file }).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
        PropertyCase propertyCase;
        if (value.Link.OwnerType is CaseAttachmentOwner.Inspection or CaseAttachmentOwner.InspectionItem && value.Link.DocumentRequirementId == null)
        {
            if (!effective.Settings.CanPerformInspections || !await db.SiteInspections.AnyAsync(x => x.PropertyCaseId == caseId
                && x.OrganizationId == context.OrganizationId && x.InspectorEmployeeId == context.EmployeeId && x.Status == InspectionStatus.Draft, cancellationToken)) throw new AccessDeniedException();
            propertyCase = await db.PropertyCases.SingleAsync(x => x.Id == caseId && x.OrganizationId == context.OrganizationId, cancellationToken);
            if (propertyCase.StageId is "rejected" or "monitor" or "acquired") throw new AccessDeniedException();
        }
        else
        {
            if (!effective.CanManageProcurement) throw new AccessDeniedException();
            context = effective.ProcurementWorkContext;
            propertyCase = await VisibleCases(db, context).Where(x => x.Case.Id == caseId).Select(x => x.Case).SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
        }
        await db.StoredFiles.FromSqlInterpolated($"SELECT * FROM foundation.stored_files WHERE id={value.File.Id} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        await db.Entry(value.File).ReloadAsync(cancellationToken);
        if (value.File.Status is StoredFileStatus.Deleted or StoredFileStatus.Quarantined or StoredFileStatus.Rejected)
            throw new ArgumentException("Вложение удалено, отклонено или находится на проверке.");
        return (value.Link, value.File, propertyCase, context);
    }
    private static FileWriteRequest DirectRequest(CaseAttachment link, StoredFile file) =>
        new(file.Id, file.OrganizationId, link.PropertyCaseId, link.Kind.ToString(), file.ContentType);
    private async Task<CaseAttachmentUploadTicket> PrepareExistingDirectAsync(Subject subject, Guid caseId, Guid id,
        BeginCaseAttachmentRetry? retry, string correlationId, CancellationToken cancellationToken)
    {
        if (!SupportsDirectAttachmentUploads) throw new FileStorageException("STORAGE_DIRECT_UNAVAILABLE", false, "Прямая загрузка не подключена.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var value = await RequireDirectAttachmentAsync(db, subject, caseId, id, cancellationToken);
        if (retry != null)
        {
            if (value.File.OriginalName != retry.OriginalName || value.File.ContentType != retry.ContentType
                || !string.Equals(value.File.Sha256, retry.Sha256, StringComparison.OrdinalIgnoreCase)
                || value.File.SizeBytes != 0 && value.File.SizeBytes != retry.SizeBytes)
                throw new ArgumentException("Выберите исходный файл: имя, тип, размер и SHA-256 должны совпадать.");
            value.File.SizeBytes = retry.SizeBytes;
        }
        if (value.File.Status == StoredFileStatus.Available) return new(id, null, true);
        ValidateDirectMetadata(value.File.SizeBytes, value.File.Sha256!);
        try
        {
            var upload = await DirectStorage.PrepareDirectUploadAsync(DirectRequest(value.Link, value.File), value.File.SizeBytes, value.File.Sha256!, cancellationToken);
            value.File.Status = StoredFileStatus.PendingUpload;
            await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
            return new(id, upload.UploadUrl, false);
        }
        catch (FileStorageException)
        {
            value.File.Status = StoredFileStatus.UploadFailed;
            await db.SaveChangesAsync(CancellationToken.None); await tx.CommitAsync(CancellationToken.None);
            throw;
        }
    }
    public async Task CompleteAttachmentUploadAsync(Subject subject, CompleteCaseAttachmentUpload command, string correlationId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var value = await RequireDirectAttachmentAsync(db, subject, command.CaseId, command.AttachmentId, cancellationToken);
        if (value.File.Status == StoredFileStatus.Available) return;
        ValidateDirectMetadata(value.File.SizeBytes, value.File.Sha256!);
        FileWriteResult confirmed;
        try { confirmed = await DirectStorage.ConfirmDirectUploadAsync(DirectRequest(value.Link, value.File), value.File.SizeBytes, value.File.Sha256!, cancellationToken); }
        catch (FileStorageException)
        {
            value.File.Status = StoredFileStatus.UploadFailed;
            await db.SaveChangesAsync(CancellationToken.None); await tx.CommitAsync(CancellationToken.None);
            throw;
        }
        // The provider, never the browser's claimed completion, authorizes Available.
        if (confirmed.SizeBytes != value.File.SizeBytes || !string.Equals(confirmed.Sha256, value.File.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new FileStorageException("STORAGE_INTEGRITY", false, "Размер или SHA-256 файла не совпали.");
        value.File.StorageKey = confirmed.StorageKey; value.File.Status = StoredFileStatus.Available;
        var now = time.GetUtcNow();
        if (value.Link.DocumentRequirementId is Guid requirementId)
        {
            var requirement = await db.CaseDocumentRequirements.SingleAsync(x => x.Id == requirementId && x.PropertyCaseId == command.CaseId, cancellationToken);
            requirement.Status = CaseDocumentStatus.Received; requirement.UpdatedByEmployeeId = value.Context.EmployeeId;
            requirement.UpdatedAt = now; requirement.Note = Optional(value.Link.Description, 2000) ?? requirement.Note;
            db.Entry(value.Case).Property(x => x.Version).IsModified = true;
        }
        OrganizationWorkspace.AddAudit(db, value.Context, subject, "CaseAttachmentAdded", "PropertyCase", command.CaseId,
            new { command.AttachmentId, value.Link.OwnerType, value.Link.Kind, Direct = true }, correlationId);
        db.BusinessTimeline.Add(new() { Id = DataConventions.NewId(), OrganizationId = value.Context.OrganizationId,
            ObjectType = "PropertyCase", ObjectId = command.CaseId, ActorEmployeeId = value.Context.EmployeeId,
            Kind = "Attachment", Title = "Добавлено вложение", Body = value.Link.Label, RecordedAt = now });
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
    }
    public async Task CancelAttachmentUploadAsync(Subject subject, CompleteCaseAttachmentUpload command, string correlationId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var value = await RequireDirectAttachmentAsync(db, subject, command.CaseId, command.AttachmentId, cancellationToken);
        // Never downgrade a concurrent successful completion, and never touch provider bytes.
        if (value.File.Status == StoredFileStatus.PendingUpload) value.File.Status = StoredFileStatus.UploadFailed;
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
    }
}