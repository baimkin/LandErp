using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class ProcurementQueueV2ReadService
{
    private sealed record AttachmentDb(Guid Id, CaseAttachmentOwner OwnerType, Guid? NegotiationId,
        Guid? InspectionId, Guid? InspectionItemId, CaseAttachmentKind Kind, string Label, string Description,
        string OriginalName, string ContentType, long SizeBytes, StoredFileStatus Status, DateTimeOffset RecordedAt);

    public async Task<ProcurementNegotiationHistoryPage> ReadNegotiationsAsync(Subject subject, Guid caseId,
        int offset, int size, CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await RequireReadAsync(subject, cancellationToken);
        AccessContext context = effective.ProcurementReadContext;
        await using LandErpDbContext db = await _factory.CreateDbContextAsync(cancellationToken);
        if (!await VisibleReadCases(db, effective).AnyAsync(row => row.Case.Id == caseId, cancellationToken))
            throw new AccessDeniedException();

        offset = Math.Max(0, offset);
        size = Math.Clamp(size, 1, 100);
        IQueryable<CaseNegotiation> query = db.CaseNegotiations.AsNoTracking().Where(item => item.PropertyCaseId == caseId);
        int total = await query.CountAsync(cancellationToken);
        CaseNegotiation[] rows = await query.OrderByDescending(item => item.EffectiveAt).ThenByDescending(item => item.RecordedAt)
            .Skip(offset).Take(size).ToArrayAsync(cancellationToken);
        Guid[] authorIds = rows.Select(item => item.AuthorEmployeeId).Distinct().ToArray();
        Dictionary<Guid, string> authors = await db.Employees.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId && authorIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);
        Guid[] negotiationIds = rows.Select(item => item.Id).ToArray();
        AttachmentDb[] caseAttachments = negotiationIds.Length == 0 ? []
            : await AttachmentRows(db, context.OrganizationId, caseId).ToArrayAsync(cancellationToken);
        AttachmentDb[] attachments = caseAttachments
            .Where(item => item.OwnerType == CaseAttachmentOwner.Negotiation && item.NegotiationId != null
                && negotiationIds.Contains(item.NegotiationId.Value))
            .OrderByDescending(item => item.RecordedAt).ToArray();
        Dictionary<Guid, ProcurementQueueV2Attachment[]> byNegotiation = attachments
            .GroupBy(item => item.NegotiationId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(ProjectAttachment).ToArray());

        ProcurementNegotiationHistoryItem[] items = rows.Select(item => new ProcurementNegotiationHistoryItem(
            item.Id, item.EffectiveAt, item.RecordedAt, item.Channel, item.Contact, item.Outcome, item.Conditions,
            item.Comment, item.NextStep, item.NextStepDueAt, item.SellerPrice, item.BuyerOffer, item.AgreedPrice,
            item.Currency, authors.GetValueOrDefault(item.AuthorEmployeeId, "Сотрудник"),
            byNegotiation.GetValueOrDefault(item.Id, []))).ToArray();
        return new(items, total, offset, size);
    }

    public async Task<ProcurementInspectionReport?> ReadInspectionReportAsync(Subject subject, Guid caseId,
        CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await RequireReadAsync(subject, cancellationToken);
        AccessContext context = effective.ProcurementReadContext;
        await using LandErpDbContext db = await _factory.CreateDbContextAsync(cancellationToken);
        if (!await VisibleReadCases(db, effective).AnyAsync(row => row.Case.Id == caseId, cancellationToken))
            throw new AccessDeniedException();

        SiteInspection? inspection = await db.SiteInspections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PropertyCaseId == caseId, cancellationToken);
        if (inspection == null) return null;
        SiteInspectionItem[] rows = await db.SiteInspectionItems.AsNoTracking().Where(item => item.InspectionId == inspection.Id)
            .OrderBy(item => item.SortOrderSnapshot).ThenBy(item => item.Id).ToArrayAsync(cancellationToken);
        AttachmentDb[] attachments = (await AttachmentRows(db, context.OrganizationId, caseId).ToArrayAsync(cancellationToken))
            .Where(item => item.OwnerType == CaseAttachmentOwner.Inspection && item.InspectionId == inspection.Id
                || item.OwnerType == CaseAttachmentOwner.InspectionItem && item.InspectionItemId != null)
            .OrderByDescending(item => item.RecordedAt).ToArray();
        Dictionary<Guid, ProcurementQueueV2Attachment[]> byItem = attachments.Where(item => item.InspectionItemId != null)
            .GroupBy(item => item.InspectionItemId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(ProjectAttachment).ToArray());
        ProcurementQueueV2Attachment[] general = attachments.Where(item => item.InspectionId == inspection.Id)
            .Select(ProjectAttachment).ToArray();
        string inspector = await db.Employees.AsNoTracking()
            .Where(item => item.OrganizationId == context.OrganizationId && item.Id == inspection.InspectorEmployeeId)
            .Select(item => item.DisplayName).SingleOrDefaultAsync(cancellationToken) ?? "Сотрудник";

        ProcurementInspectionReportItem[] items = rows.Select(item => new ProcurementInspectionReportItem(
            item.Id, item.TitleSnapshot, item.SortOrderSnapshot, item.Status, item.Answer, item.UnitSnapshot, item.Note,
            item.Status == InspectionItemStatus.Answered && item.NormalAnswerSnapshot.Length > 0
                && !string.Equals(item.Answer, item.NormalAnswerSnapshot, StringComparison.OrdinalIgnoreCase),
            byItem.GetValueOrDefault(item.Id, []))).ToArray();
        return new(inspection.Id, inspection.Status, inspection.OverallConclusion, inspection.PreliminaryDecision,
            inspector, inspection.StartedAt, inspection.CompletedAt, items, general);
    }

    private static IQueryable<AttachmentDb> AttachmentRows(LandErpDbContext db, Guid organizationId, Guid caseId) =>
        from attachment in db.CaseAttachments.AsNoTracking()
        join file in db.StoredFiles.AsNoTracking() on attachment.StoredFileId equals file.Id
        where attachment.OrganizationId == organizationId && file.OrganizationId == organizationId
            && attachment.PropertyCaseId == caseId
        select new AttachmentDb(attachment.Id, attachment.OwnerType, attachment.NegotiationId,
            attachment.InspectionId, attachment.InspectionItemId, attachment.Kind, attachment.Label,
            attachment.Description, file.OriginalName, file.ContentType, file.SizeBytes, file.Status, attachment.RecordedAt);

    private static ProcurementQueueV2Attachment ProjectAttachment(AttachmentDb item) => new(
        item.Id, item.Kind, item.Label, item.Description, item.OriginalName, item.ContentType,
        item.SizeBytes, item.Status, item.RecordedAt);
}
