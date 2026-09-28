using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class ProcurementWorkspace
{
    public async Task DeleteRichNoteAsync(Subject subject, DeleteCaseRichNote command, string correlationId, CancellationToken cancellationToken)
    {
        var effective = await RequireProcurementAsync(subject, ProcurementAccessLevel.Manager, cancellationToken);
        var context = effective.ProcurementWorkContext;
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        _ = await VisibleCases(db, context).SingleOrDefaultAsync(r => r.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        var note = await db.CaseRichNotes.SingleOrDefaultAsync(n => n.Id == command.NoteId && n.PropertyCaseId == command.CaseId && n.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (note.Section == CaseNoteSection.Working) throw new ArgumentException("Рабочий текст редактируется в своём разделе.");
        if (note.Deleted) return;
        if (note.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException();
        note.Deleted = true;
        note.Version++;
        note.UpdatedAt = time.GetUtcNow();
        note.UpdatedByEmployeeId = context.EmployeeId;
        OrganizationWorkspace.AddAudit(db, context, subject, "CaseRichNoteChanged", "PropertyCase", command.CaseId,
            new { NoteId = note.Id, Section = NoteTitle(note.Section), Previous = note.DocumentJson, Current = CaseNoteDocument.Empty, Deleted = true, note.Version }, correlationId);
        db.BusinessTimeline.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = command.CaseId,
            ActorEmployeeId = context.EmployeeId, Kind = "Note", Title = "Удалена заметка к проверкам", Body = NoteTitle(note.Section), RecordedAt = note.UpdatedAt });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReassignManagerAsync(Subject subject, ReassignCaseManager command, string correlationId, CancellationToken cancellationToken)
    {
        // Head includes the system Owner. A Manager cannot bypass the UI by calling the command.
        var effective = await RequireProcurementAsync(subject, ProcurementAccessLevel.Head, cancellationToken);
        var context = effective.ProcurementWorkContext;
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        await EnsureActiveEmployeeAsync(db, context.EmployeeId, cancellationToken);
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        var row = await VisibleCases(db, context).SingleOrDefaultAsync(r => r.Case.Id == command.CaseId, cancellationToken) ?? throw new AccessDeniedException();
        if (row.Case.Version != command.ExpectedCaseVersion) throw new DbUpdateConcurrencyException();
        // Pending approval keeps its assigned head; changing the manager does not approve or return the case.
        bool pending = row.Case.StageId == "pending_head";
        var targets = await TargetsAsync(db, row.Case, row.Assignment.EmployeeId, ProcurementAccessLevel.Manager,
            pending ? ProcurementRecipientAccess.BecomesManager : ProcurementRecipientAccess.BecomesManagerAndCaseAssignee, cancellationToken);
        var target = targets.SingleOrDefault(t => t.EmployeeId == command.EmployeeId) ?? throw new AccessDeniedException();
        if (pending && row.Assignment.EmployeeId == command.EmployeeId)
            throw new ArgumentException("Нельзя назначить менеджером руководителя, который сейчас согласует этот объект.");
        Guid previous = row.Case.ManagerEmployeeId;
        if (previous == command.EmployeeId) return;
        string previousName = await db.Employees.Where(e => e.Id == previous).Select(e => e.DisplayName).SingleAsync(cancellationToken);
        row.Case.ManagerEmployeeId = command.EmployeeId;
        if (!pending) row.Assignment.EmployeeId = command.EmployeeId;
        // Existing tasks retain their assignees and results. Assignment is the current workflow actor, not a user task.
        db.Entry(row.Case).Property(c => c.Version).IsModified = true;
        OrganizationWorkspace.AddAudit(db, context, subject, "CaseManagerReassigned", "PropertyCase", command.CaseId,
            new { PreviousEmployeeId = previous, EmployeeId = command.EmployeeId, Previous = previousName, Current = target.Name }, correlationId);
        db.BusinessTimeline.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = command.CaseId,
            ActorEmployeeId = context.EmployeeId, TargetEmployeeId = command.EmployeeId, Kind = "Assignment", Title = "Изменён ответственный за объект",
            Body = previousName + " → " + target.Name, RecordedAt = time.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}