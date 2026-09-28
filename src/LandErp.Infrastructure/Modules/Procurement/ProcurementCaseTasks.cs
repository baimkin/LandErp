using LandErp.Application.Foundation;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class ProcurementWorkspace
{
    public async Task<CaseTasksView> ReadTasksAsync(Subject subject, Guid caseId, CancellationToken cancellationToken)
    {
        var access = await RequireProcurementAsync(subject, ProcurementAccessLevel.Read, cancellationToken);
        var context = access.ProcurementReadContext;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        Row row = await VisibleReadCases(db, access).SingleOrDefaultAsync(x => x.Case.Id == caseId, cancellationToken)
            ?? throw new AccessDeniedException();
        var assignees = await TargetsAsync(db, row.Case, row.Assignment.EmployeeId, ProcurementAccessLevel.Manager,
            ProcurementRecipientAccess.CurrentVisibility, cancellationToken);
        bool canEdit = access.CanManageProcurement && row.Case.StageId != "acquired"
            && await VisibleCases(db, access.ProcurementWorkContext).AnyAsync(x => x.Case.Id == caseId, cancellationToken);
        var tasks = await (from task in db.WorkTasks.AsNoTracking()
                           join employee in db.Employees on task.EmployeeId equals employee.Id
                           where task.OrganizationId == context.OrganizationId && task.ObjectType == "PropertyCase"
                               && task.ObjectId == caseId && !task.Deleted && task.Title != ""
                           select new { Task = task, employee.DisplayName }).ToArrayAsync(cancellationToken);
        var card = await ReadCardAsync(subject, caseId, cancellationToken);
        var completedNames = await db.Employees.Where(x => x.OrganizationId == context.OrganizationId)
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var now = time.GetUtcNow();
        return new(row.Case.Version, row.Assignment.EmployeeId, canEdit, assignees,
            tasks.OrderBy(x => x.Task.Completed).ThenByDescending(x => WorkTaskDeadline.Overdue(x.Task, now))
                .ThenBy(x => x.Task.DueAt == null).ThenBy(x => x.Task.DueAt).ThenBy(x => x.Task.RecordedAt).ThenBy(x => x.Task.Id)
                .Select(x => new CaseTaskView(x.Task.Id, x.Task.Title, x.Task.Description, x.Task.Type, x.Task.EmployeeId,
                    x.DisplayName, x.Task.DueAt, x.Task.DueHasTime, x.Task.Completed, WorkTaskDeadline.Overdue(x.Task, now), x.Task.Version) {
                        ResultDocumentJson = x.Task.ResultDocumentJson,
                        ResultHtml = x.Task.ResultDocumentJson == null ? null : CaseNoteDocument.Validate(x.Task.ResultDocumentJson).Html,
                        CompletedAt = x.Task.CompletedAt,
                        CompletedBy = x.Task.CompletedByEmployeeId is Guid who ? completedNames.GetValueOrDefault(who, "Сотрудник") : null,
                        SourceCommunication = card.Negotiations.FirstOrDefault(n => n.Id == x.Task.SourceNegotiationId),
                        SourceAttachments = card.Attachments.Where(a => a.OwnerType == LandErp.Application.Modules.Procurement.Domain.CaseAttachmentOwner.Negotiation && a.OwnerId == x.Task.SourceNegotiationId).ToArray()
                    }).ToArray());
    }

    public async Task<Guid> ChangeTaskAsync(Subject subject, ChangeCaseTask command, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Action)) throw new ArgumentException("Неизвестное действие с задачей.");
        var access = await RequireProcurementAsync(subject, ProcurementAccessLevel.Manager, cancellationToken);
        var context = access.ProcurementWorkContext;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        await EnsureActiveEmployeeAsync(db, context.EmployeeId, cancellationToken);
        var replay = await ProcurementCommandReplay.BeginAsync(db, context, subject, command.CommandId,
            "CaseTask" + command.Action, command with { ExpectedCaseVersion = 0, ExpectedTaskVersion = 0, CommandId = Guid.Empty }, cancellationToken);
        await db.PropertyCases.FromSqlInterpolated($"SELECT * FROM procurement.property_cases WHERE id={command.CaseId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(x => x.Case.Id == command.CaseId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (replay.ExistingResultId is Guid previous)
        {
            if (replay.ExistingCaseId != row.Case.Id) throw new AccessDeniedException();
            await transaction.CommitAsync(cancellationToken);
            return previous;
        }
        if (row.Case.Version != command.ExpectedCaseVersion) throw new DbUpdateConcurrencyException();
        if (row.Case.StageId == "acquired")
            throw new ArgumentException("Закупка уже завершена. Изменение задач недоступно.");
        WorkTask task;
        bool created = command.TaskId == null;
        if (created)
        {
            if (command.Action != CaseTaskAction.Save) throw new ArgumentException("Выберите задачу.");
            task = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
                ObjectType = "PropertyCase", ObjectId = row.Case.Id, RecordedAt = time.GetUtcNow(), IsUserTask = true };
            db.WorkTasks.Add(task);
        }
        else
        {
            task = await db.WorkTasks.SingleOrDefaultAsync(x => x.Id == command.TaskId && x.OrganizationId == context.OrganizationId
                && x.ObjectType == "PropertyCase" && x.ObjectId == row.Case.Id, cancellationToken) ?? throw new AccessDeniedException();
            if (task.Version != command.ExpectedTaskVersion) throw new DbUpdateConcurrencyException();
            if (task.Completed || task.Deleted) throw new ArgumentException("Задача уже выполнена или удалена.");
        }
        var before = new { task.Title, task.Description, task.Type, task.EmployeeId, task.DueAt, task.DueHasTime, task.Completed, task.Deleted };
        if (command.Action == CaseTaskAction.Save)
        {
            await ApplyTaskDetailsAsync(db, row, task, command, cancellationToken);
        }
        else if (command.Action == CaseTaskAction.Complete)
        {
            if (command.ResultDocumentJson != null)
            {
                var result = CaseNoteDocument.Validate(command.ResultDocumentJson);
                await ValidateNoteImagesAsync(db, context.OrganizationId, row.Case.Id, result, null, cancellationToken);
                task.ResultDocumentJson = result.Json;
            }
            task.Completed = true;
            task.CompletedAt = time.GetUtcNow();
            task.CompletedByEmployeeId = context.EmployeeId;
        }
        else task.Deleted = true;
        db.Entry(row.Case).Property(x => x.Version).IsModified = true;
        string label = command.Action switch { CaseTaskAction.Complete => "Задача выполнена", CaseTaskAction.Delete => "Задача удалена", _ => created ? "Задача добавлена" : "Задача изменена" };
        List<string> changed = [];
        if(command.Action == CaseTaskAction.Save && !created)
        {
            if(before.Title != task.Title) changed.Add($"Название: {before.Title} → {task.Title}");
            if(before.Description != task.Description) changed.Add($"Описание: {before.Description} → {task.Description}");
            if(before.DueAt != task.DueAt || before.DueHasTime != task.DueHasTime) changed.Add($"Срок: {WorkTaskDeadline.Format(before.DueAt,before.DueHasTime)} → {WorkTaskDeadline.Format(task.DueAt,task.DueHasTime)}");
        }
        string changeDetail = changed.Count == 0 ? "" : "\nИзменения:\n" + string.Join("\n",changed);
        db.BusinessTimeline.Add(new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            ObjectType = "PropertyCase", ObjectId = row.Case.Id, ActorEmployeeId = context.EmployeeId,
            Kind = "CaseTask" + command.Action, TaskId = task.Id, Title = label, Body = task.Title + "\n" + (command.Action == CaseTaskAction.Complete ? (task.ResultDocumentJson == null ? "Без отчёта" : CaseNoteDocument.PlainText(task.ResultDocumentJson)) : WorkTaskDeadline.Format(task.DueAt, task.DueHasTime)) + changeDetail,
            TargetEmployeeId = task.EmployeeId, RecordedAt = time.GetUtcNow() });
        replay.Record(db, context, subject, row.Case.Id, task.Id,
            new { TaskId = task.Id, Before = before, After = new { task.Title, task.Description, task.Type, task.EmployeeId,
                task.DueAt, task.DueHasTime, task.Completed, task.Deleted, task.ResultDocumentJson, task.CompletedAt, task.CompletedByEmployeeId } }, correlationId, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return task.Id;
    }
    // Один набор правил T-01 для отдельной задачи и атомарной записи общения T-02.
    private async Task ApplyTaskDetailsAsync(LandErpDbContext db, Row row, WorkTask task,
        ChangeCaseTask command, CancellationToken cancellationToken)
    {
            string title = Required(command.Title, 1, 512, "Укажите название задачи (до 512 символов).");
            if (!Enum.IsDefined(command.Type) || command.Description.Length > 4000) throw new ArgumentException("Проверьте тип и подробности задачи.");
            var assignees = await TargetsAsync(db, row.Case, row.Assignment.EmployeeId, ProcurementAccessLevel.Manager,
                ProcurementRecipientAccess.CurrentVisibility, cancellationToken);
            if (!assignees.Any(x => x.EmployeeId == command.EmployeeId)) throw new AccessDeniedException();
            if (command.DueAt is DateTimeOffset due && (due.Offset != TimeSpan.Zero || due > time.GetUtcNow().AddYears(2)
                || !command.DueHasTime && due != WorkTaskDeadline.DayStart(due)))
                throw new ArgumentException("Проверьте срок: дата по МСК или точное время UTC.");
            task.Title = title; task.Description = command.Description; task.Type = command.Type;
            task.EmployeeId = command.EmployeeId; task.DueAt = command.DueAt; task.DueHasTime = command.DueHasTime;
            task.IsUserTask = true;
    }

    private static void EnsureSystemTask(LandErpDbContext db, Row row, DateTimeOffset now)
    {
        if (row.Task.IsUserTask)
        {
            // WorkTaskId остаётся ссылкой workflow, не указателем ближайшей задачи.
            row.Task = new WorkTask { Id = DataConventions.NewId(), OrganizationId = row.Case.OrganizationId,
                ObjectType = "PropertyCase", ObjectId = row.Case.Id, EmployeeId = row.Assignment.EmployeeId, RecordedAt = now };
            db.WorkTasks.Add(row.Task);
            row.Case.WorkTaskId = row.Task.Id;
        }
        row.Task.Deleted = false;
        row.Task.DueHasTime = true;
    }

}
