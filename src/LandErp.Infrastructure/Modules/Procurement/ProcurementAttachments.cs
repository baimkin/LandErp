using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class ProcurementWorkspace
{
    public async Task DeleteAttachmentAsync(Subject subject, DeleteCaseAttachment command, string correlationId,
        CancellationToken cancellationToken)
    {
        // Same permission/scope as CanManageDossier; an inspection assignment alone is insufficient.
        var effective = await RequireProcurementAsync(subject, ProcurementAccessLevel.Manager, cancellationToken);
        var context = effective.ProcurementWorkContext;
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PropertyCases.FromSqlInterpolated(
            $"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE")
            .LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken)
            ?? throw new AccessDeniedException();
        var value = await (from link in db.CaseAttachments
                           join file in db.StoredFiles on link.StoredFileId equals file.Id
                           where link.Id == command.AttachmentId && link.PropertyCaseId == command.CaseId
                               && link.OrganizationId == context.OrganizationId && file.OrganizationId == context.OrganizationId
                           select new { Link = link, File = file }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new AccessDeniedException();
        await db.StoredFiles.FromSqlInterpolated(
            $"SELECT * FROM foundation.stored_files WHERE id={value.File.Id} AND organization_id={context.OrganizationId} FOR UPDATE")
            .LoadAsync(cancellationToken);
        await db.Entry(value.File).ReloadAsync(cancellationToken);
        if (value.File.Status == StoredFileStatus.Deleted) return;
        if (value.File.Status == StoredFileStatus.PendingUpload)
            throw new ArgumentException("Дождитесь завершения загрузки или восстановите её перед удалением.");
        if (value.File.Status is StoredFileStatus.Quarantined or StoredFileStatus.Rejected)
            throw new ArgumentException("Материал на проверке или отклонён. Его состояние нельзя менять удалением.");

        if (value.File.StorageKey == null && value.File.ExternalUrl == null)
            throw new ArgumentException("Файл ещё не сохранён в хранилище. Повторите загрузку перед удалением.");

        StoredFileStatus previousStatus = value.File.Status;
        value.File.Status = StoredFileStatus.Deleted;
        DateTimeOffset now = time.GetUtcNow();
        if (value.Link.DocumentRequirementId is Guid requirementId)
        {
            bool remaining = await (from link in db.CaseAttachments
                                   join file in db.StoredFiles on link.StoredFileId equals file.Id
                                   where link.DocumentRequirementId == requirementId && link.Id != value.Link.Id
                                       && link.OrganizationId == context.OrganizationId && file.Status == StoredFileStatus.Available
                                   select link.Id).AnyAsync(cancellationToken);
            var requirement = await db.CaseDocumentRequirements.SingleAsync(item => item.Id == requirementId, cancellationToken);
            if (!remaining && requirement.Status is CaseDocumentStatus.Received or CaseDocumentStatus.Verified)
            {
                requirement.Status = CaseDocumentStatus.Missing;
                requirement.UpdatedAt = now;
                requirement.UpdatedByEmployeeId = context.EmployeeId;
                db.Entry(row.Case).Property(item => item.Version).IsModified = true;
            }
        }
        // Preserve storage key, hash, bytes and immutable attachment metadata. Never call the provider.
        db.BusinessTimeline.Add(new()
        {
            Id = value.Link.Id, OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = command.CaseId,
            ActorEmployeeId = context.EmployeeId, Kind = "AttachmentDeleted", Title = "Удалено вложение",
            Body = value.Link.Label, RecordedAt = now
        });
        OrganizationWorkspace.AddAudit(db, context, subject, "CaseAttachmentDeleted", "PropertyCase", command.CaseId,
            new { command.AttachmentId, value.Link.Label, PreviousStatus = previousStatus, Status = StoredFileStatus.Deleted }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
