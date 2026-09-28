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
    public async Task<CaseRichNoteView> SaveRichNoteAsync(Subject subject, SaveCaseRichNote command,
        string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Section) || command.ExpectedVersion < 0) throw new ArgumentException("Некорректная секция или версия.");
        var effective = await RequireProcurementAsync(subject, ProcurementAccessLevel.Manager, cancellationToken);
        var context = effective.ProcurementWorkContext;
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serializes first insert and visibility/ownership changes; each section has its own version.
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE")
            .LoadAsync(cancellationToken);
        _ = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId, cancellationToken)
            ?? throw new AccessDeniedException();
        ValidatedCaseNote document = CaseNoteDocument.Validate(command.DocumentJson);
        await ValidateNoteImagesAsync(db, context.OrganizationId, command.CaseId, document, null, cancellationToken);
        CaseRichNote? note = await db.CaseRichNotes.SingleOrDefaultAsync(item => item.PropertyCaseId == command.CaseId
            && item.OrganizationId == context.OrganizationId && item.Section == command.Section
            && (command.Section == CaseNoteSection.Working || item.Id == command.NoteId), cancellationToken);
        if (note?.Deleted == true) throw new DbUpdateConcurrencyException("Заметка уже удалена.");
        if (command.Section != CaseNoteSection.Working && (command.NoteId is null || command.NoteId == Guid.Empty))
            throw new ArgumentException("Не указан идентификатор заметки.");
        // Stable client ID makes retry after a lost response safe without creating another note.
        if (note != null && command.ExpectedVersion == 0 && command.Section != CaseNoteSection.Working
            && note.DocumentJson == document.Json)
        {
            string existingAuthor = await db.Employees.Where(e => e.Id == note.UpdatedByEmployeeId).Select(e => e.DisplayName).SingleAsync(cancellationToken);
            return NoteView(note, existingAuthor);
        }
        if ((note?.Version ?? 0) != command.ExpectedVersion) throw new DbUpdateConcurrencyException("Текст уже изменён другим сотрудником.");
        string before = note?.DocumentJson ?? CaseNoteDocument.Empty;
        if (note == null)
        {
            note = new() { Id = command.NoteId ?? Guid.CreateVersion7(), OrganizationId = context.OrganizationId,
                PropertyCaseId = command.CaseId, Section = command.Section };
            db.CaseRichNotes.Add(note);
        }
        else note.Version++;
        note.DocumentJson = document.Json;
        note.UpdatedAt = time.GetUtcNow();
        note.UpdatedByEmployeeId = context.EmployeeId;
        OrganizationWorkspace.AddAudit(db, context, subject, "CaseRichNoteChanged", "PropertyCase", command.CaseId,
            new { NoteId = note.Id, Section = NoteTitle(command.Section), Previous = before, Current = document.Json, note.Version }, correlationId);
        db.BusinessTimeline.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = context.OrganizationId,
            ObjectType = "PropertyCase", ObjectId = command.CaseId, ActorEmployeeId = context.EmployeeId,
            Kind = "Note", Title = command.Section == CaseNoteSection.Working ? "Обновлён текст секции" : command.ExpectedVersion == 0 ? "Добавлена заметка к проверкам" : "Изменена заметка к проверкам", Body = NoteTitle(command.Section), RecordedAt = note.UpdatedAt });
        await db.SaveChangesAsync(cancellationToken);
        string author = await db.Employees.Where(item => item.Id == context.EmployeeId).Select(item => item.DisplayName).SingleAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoteView(note, author);
    }

    private static async Task ValidateNoteImagesAsync(LandErpDbContext db, Guid organizationId, Guid caseId,
        ValidatedCaseNote document, Guid? checkId, CancellationToken cancellationToken)
    {
        Guid[] ids = document.AttachmentIds.ToArray();
        Guid[] imageIds = document.ImageIds.ToArray();
        if (ids.Length > 0)
        {
            int available = await (from link in db.CaseAttachments
                                   join file in db.StoredFiles on link.StoredFileId equals file.Id
                                   where ids.Contains(link.Id) && link.OrganizationId == organizationId
                                       && file.OrganizationId == organizationId && link.PropertyCaseId == caseId
                                       && (link.OwnerType == CaseAttachmentOwner.Case || (checkId != null && link.OwnerType == CaseAttachmentOwner.Check && link.CheckId == checkId))
                                       && file.Status == StoredFileStatus.Available && file.ExternalUrl == null && file.StorageKey != null
                                       && (!imageIds.Contains(link.Id) || link.Kind == CaseAttachmentKind.Photo &&
                                           (file.ContentType == "image/png" || file.ContentType == "image/jpeg"
                                           || file.ContentType == "image/webp" || file.ContentType == "image/gif"))
                                   select link.Id).CountAsync(cancellationToken);
            if (available != ids.Length) throw new ArgumentException("Файл недоступен или не принадлежит вложениям этого объекта.");
        }
    }

    private static CaseRichNoteView NoteView(CaseRichNote note, string author) =>
        new(note.Section, note.DocumentJson, CaseNoteDocument.Validate(note.DocumentJson).Html, note.Version, author, note.UpdatedAt) { Id = note.Id };

    private static string NoteTitle(CaseNoteSection section) => section switch
    {
        CaseNoteSection.Working => "Рабочие заметки и расчёты",
        CaseNoteSection.QuickChecks => "Базовые проверки — свободный текст",
        _ => "Глубокая проверка — свободный текст"
    };
}
