using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;

namespace LandErp.Infrastructure.Modules.Procurement;

/// <summary>One transaction owns current responsibility, decision facts and history. No client-supplied organization/scope.</summary>
public sealed class ProcurementWorkspace(IDbContextFactory<LandErpDbContext> factory, IAccessControl access, TimeProvider time) : IProcurementWorkspace
{
    private sealed class Row
    {
        public Listing Listing { get; init; } = default!;
        public PropertyCase? Case { get; init; }
        public Assignment? Assignment { get; init; }
        public WorkTask? Task { get; init; }
    }
    private static IQueryable<Row> Visible(LandErpDbContext db, AccessContext context)
    {
        var rows = from listing in db.Listings
                   join item in db.PropertyCases on listing.Id equals item.ListingId into cases
                   from item in cases.DefaultIfEmpty()
                   join assignment in db.WorkAssignments on (item == null ? Guid.Empty : item.AssignmentId) equals assignment.Id into assignments
                   from assignment in assignments.DefaultIfEmpty()
                   join task in db.WorkTasks on (item == null ? Guid.Empty : item.WorkTaskId) equals task.Id into tasks
                   from task in tasks.DefaultIfEmpty()
                   where listing.OrganizationId == context.OrganizationId
                   select new Row { Listing = listing, Case = item, Assignment = assignment, Task = task };
        return context.Scope switch
        {
            AccessScope.Organization => rows,
            AccessScope.Department => rows.Where(row => context.DepartmentId != null && row.Listing.DepartmentId == context.DepartmentId),
            AccessScope.Team => rows.Where(row => context.TeamId != null && row.Listing.TeamId == context.TeamId),
            AccessScope.AssignedObjects => rows.Where(row => row.Assignment != null && row.Assignment.EmployeeId == context.EmployeeId),
            _ => rows.Where(row => row.Case != null && (row.Case.ManagerEmployeeId == context.EmployeeId || row.Assignment != null && row.Assignment.EmployeeId == context.EmployeeId))
        };
    }
    public async Task<ProcurementQueuePage> ReadQueueAsync(Subject subject, QueueFilter filter, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var query = Visible(db, context);
        if (filter.Text.Length > 200 || filter.Offset < 0 || filter.Size is < 1 or > 100) throw new ArgumentException("QUEUE_FILTER_INVALID");
        if (filter.Text.Length > 0) query = query.Where(row => (row.Listing.Title ?? "").Contains(filter.Text) || (row.Listing.Location ?? "").Contains(filter.Text) || row.Listing.ExternalId.Contains(filter.Text));
        if (filter.Source != null) query = query.Where(row => row.Listing.Source == filter.Source);
        if (filter.Stage.Length > 0) query = query.Where(row => (row.Case == null ? "new" : row.Case.StageId) == filter.Stage);
        else query = query.Where(row => row.Case == null || row.Case.StageId != "rejected" && row.Case.StageId != "approved" || row.Listing.DataRevision > row.Case.ReviewedDataRevision);
        if (filter.ChangedOnly) query = query.Where(row => row.Case != null && row.Listing.DataRevision > row.Case.ReviewedDataRevision);
        int total = await query.CountAsync(cancellationToken);
        Row[] rows = await query.OrderByDescending(row => row.Listing.ChangedAt).ThenBy(row => row.Listing.Id).Skip(filter.Offset).Take(filter.Size).ToArrayAsync(cancellationToken);
        var names = await db.Employees.Where(item => item.OrganizationId == context.OrganizationId).ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);
        return new(rows.Select(row => Item(row, names)).ToArray(), total);
    }
    public async Task<CaseCard> ReadCardAsync(Subject subject, Guid listingId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Row row = await Visible(db, context).SingleOrDefaultAsync(item => item.Listing.Id == listingId, cancellationToken) ?? throw new AccessDeniedException();
        var names = await db.Employees.Where(item => item.OrganizationId == context.OrganizationId).ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);
        var timeline = await db.BusinessTimeline.Where(item => item.OrganizationId == context.OrganizationId && item.ObjectType == "PropertyCase" && row.Case != null && item.ObjectId == row.Case.Id)
            .OrderByDescending(item => item.RecordedAt).ThenByDescending(item => item.Id).Take(200).ToArrayAsync(cancellationToken);
        var observations = await db.ListingObservations.Where(item => item.ListingId == listingId).OrderByDescending(item => item.ObservedAt).ThenByDescending(item => item.Id).Take(100).ToArrayAsync(cancellationToken);
        var heads = await TargetsAsync(db, context.OrganizationId, Permissions.HeadDecide, cancellationToken, row.Listing);
        var managers = await TargetsAsync(db, context.OrganizationId, Permissions.ManagerDecide, cancellationToken, row.Listing);
        bool manager = await AllowedAsync(subject, Permissions.ManagerDecide, cancellationToken) && (row.Case == null || row.Assignment?.EmployeeId == context.EmployeeId)
            && row.Case?.StageId != "pending_head" && (row.Case?.StageId is not ("approved" or "rejected") || row.Listing.DataRevision > row.Case.ReviewedDataRevision);
        bool head = await AllowedAsync(subject, Permissions.HeadDecide, cancellationToken) && row.Case?.StageId == "pending_head" && row.Assignment?.EmployeeId == context.EmployeeId
            && row.Case.ManagerEmployeeId != context.EmployeeId;
        return new(Item(row, names), row.Listing.Url, row.Listing.Description, row.Listing.SellerName, JsonSerializer.Deserialize<string[]>(row.Listing.PhotosJson)!,
            timeline.Select(item => new TimelineItem(item.Id, item.Kind, item.Title, item.Body, names.GetValueOrDefault(item.ActorEmployeeId, "Сотрудник"),
                item.TargetEmployeeId == null ? null : names.GetValueOrDefault(item.TargetEmployeeId.Value, "Сотрудник"), item.RecordedAt, item.EffectiveAt, item.DueAt)).ToArray(),
            observations.Select(item => new ObservationView(item.Id, item.ObservedAt, item.RecordedAt, JsonSerializer.Deserialize<ListingData>(item.PayloadJson, CollectionJson.Options)!, JsonSerializer.Deserialize<string[]>(item.ChangesJson)!)).ToArray(),
            heads.Where(item => item.EmployeeId != context.EmployeeId).ToArray(), managers, row.Case?.ManagerEmployeeId, manager, head);
    }
    public async Task DecideAsync(Subject subject, DecisionCommand command, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Action)) throw new ArgumentException("DECISION_INVALID");
        bool headAction = command.Action is ProcurementAction.Return or ProcurementAction.Approve;
        // Monitor/Reject belong to the current stage; a head cannot call the manager path by selecting another verb.
        AccessContext read = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var locked = await db.Listings.FromSqlInterpolated($"SELECT * FROM catalog.listings WHERE id={command.ListingId} AND organization_id={read.OrganizationId} FOR UPDATE").ToListAsync(cancellationToken);
        if (locked.Count == 0) throw new AccessDeniedException();
        Row row = await Visible(db, read).SingleOrDefaultAsync(item => item.Listing.Id == command.ListingId, cancellationToken) ?? throw new AccessDeniedException();
        PropertyCase? item = row.Case; Listing listing = locked[0];
        if ((item?.Version ?? 0) != command.ExpectedCaseVersion || listing.DataRevision != command.ExpectedDataRevision) throw new DbUpdateConcurrencyException();
        headAction |= item?.StageId == "pending_head";
        AccessContext context = await access.RequireAsync(subject, headAction ? Permissions.HeadDecide : Permissions.ManagerDecide, cancellationToken);
        if (item != null && row.Assignment?.EmployeeId != context.EmployeeId || headAction && (item?.StageId != "pending_head" || item.ManagerEmployeeId == context.EmployeeId)) throw new AccessDeniedException();
        if (!headAction && command.Action is ProcurementAction.Return or ProcurementAction.Approve || headAction && command.Action is not (ProcurementAction.Return or ProcurementAction.Approve or ProcurementAction.Monitor or ProcurementAction.Reject)) throw new AccessDeniedException();
        if (item?.StageId is "approved" or "rejected" && listing.DataRevision <= item.ReviewedDataRevision) throw new ArgumentException("Решение завершено. Для нового анализа нужны изменившиеся данные.");
        string reason = Text(command.Reason, command.Action != ProcurementAction.TakeWork);
        string clarification = Text(command.Clarification, command.Action is ProcurementAction.Return or ProcurementAction.Clarify);
        if (command.DueAt?.Offset != null && command.DueAt.Value.Offset != TimeSpan.Zero || command.DueAt < time.GetUtcNow() || command.DueAt > time.GetUtcNow().AddYears(2)) throw new ArgumentException("Укажите будущий срок в UTC.");
        string from = item?.StageId ?? "new";
        if (item == null)
        {
            await using var number = db.Database.GetDbConnection().CreateCommand(); number.Transaction = db.Database.CurrentTransaction!.GetDbTransaction(); number.CommandText = "SELECT nextval('procurement.property_case_numbers')";
            long businessNumber = (long)(await number.ExecuteScalarAsync(cancellationToken))!;
            Guid id = DataConventions.NewId(); Assignment assignment = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = id, EmployeeId = context.EmployeeId };
            WorkTask task = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = id, EmployeeId = context.EmployeeId, Title = "Первичный анализ", RecordedAt = time.GetUtcNow() };
            item = new()
            {
                Id = id,
                OrganizationId = context.OrganizationId,
                ListingId = listing.Id,
                BusinessNumber = "PC-" + businessNumber.ToString("D6", System.Globalization.CultureInfo.InvariantCulture),
                ManagerEmployeeId = context.EmployeeId,
                AssignmentId = assignment.Id,
                WorkTaskId = task.Id,
                RecordedAt = time.GetUtcNow()
            };
            db.PropertyCases.Add(item); db.WorkAssignments.Add(assignment); db.WorkTasks.Add(task); row = new() { Listing = listing, Case = item, Assignment = assignment, Task = task };
        }
        string stage = command.Action switch { ProcurementAction.TakeWork => "analysis", ProcurementAction.Monitor => "monitor", ProcurementAction.Clarify => "clarify", ProcurementAction.Reject => "rejected", ProcurementAction.Forward => "pending_head", ProcurementAction.Return => "returned", _ => "approved" };
        Guid target = context.EmployeeId;
        if (command.Action == ProcurementAction.Forward)
        {
            target = command.TargetEmployeeId ?? throw new ArgumentException("Выберите руководителя закупки.");
            if (target == context.EmployeeId || !(await TargetsAsync(db, context.OrganizationId, Permissions.HeadDecide, cancellationToken, listing)).Any(value => value.EmployeeId == target)) throw new AccessDeniedException();
            item.PendingApprovalId = DataConventions.NewId();
        }
        if (headAction)
        {
            if (command.Action == ProcurementAction.Approve && listing.DataRevision != item.ReviewedDataRevision) throw new ArgumentException("Данные изменились после передачи. Верните объект менеджеру для обновления анализа.");
            target = command.Action == ProcurementAction.Return ? command.TargetEmployeeId ?? item.ManagerEmployeeId : item.ManagerEmployeeId;
            if (!(await TargetsAsync(db, context.OrganizationId, Permissions.ManagerDecide, cancellationToken, listing)).Any(value => value.EmployeeId == target)) throw new AccessDeniedException();
            db.Approvals.Add(new()
            {
                Id = item.PendingApprovalId ?? throw new AccessDeniedException(),
                OrganizationId = context.OrganizationId,
                ObjectType = "PropertyCase",
                ObjectId = item.Id,
                RequesterEmployeeId = item.ManagerEmployeeId,
                ApproverEmployeeId = context.EmployeeId,
                Outcome = command.Action.ToString(),
                Reason = reason,
                ConsideredDataRevision = listing.DataRevision,
                ObjectVersion = item.Version,
                RecordedAt = time.GetUtcNow()
            });
            item.PendingApprovalId = null;
            if (command.Action == ProcurementAction.Return) item.ManagerEmployeeId = target;
        }
        item.StageId = stage; item.ReviewedDataRevision = listing.DataRevision;
        if (db.Entry(item).State != EntityState.Added) db.Entry(item).Property(value => value.Version).IsModified = true;
        row.Assignment!.EmployeeId = target; row.Task!.EmployeeId = target; row.Task.DueAt = command.DueAt;
        row.Task.Completed = stage is "approved" or "rejected";
        row.Task.Title = stage switch { "pending_head" => "Рассмотреть первичный анализ", "returned" => "Исправить / уточнить первичный анализ", "clarify" => "Уточнить данные объекта", "monitor" => "Наблюдать за объектом", "approved" => "Дальнейшая работа одобрена", "rejected" => "Объект отклонён", _ => "Первичный анализ" };
        string title = ActionLabel(command.Action, headAction);
        db.WorkflowTransitions.Add(new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase", ObjectId = item.Id, FromStageId = from, ToStageId = stage, Action = command.Action.ToString(), ActorEmployeeId = context.EmployeeId, ObjectVersion = item.Version + (db.Entry(item).State == EntityState.Added ? 0 : 1), RecordedAt = time.GetUtcNow() });
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(),
            OrganizationId = context.OrganizationId,
            ObjectType = "PropertyCase",
            ObjectId = item.Id,
            ActorEmployeeId = context.EmployeeId,
            Kind = "Decision",
            Title = title,
            Body = reason + (clarification.Length == 0 ? "" : "\nУточнить: " + clarification),
            TargetEmployeeId = target,
            DueAt = command.DueAt,
            RecordedAt = time.GetUtcNow()
        });
        if (target != context.EmployeeId) db.Notifications.Add(new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, EmployeeId = target, ObjectType = "PropertyCase", ObjectId = item.Id, Title = item.BusinessNumber + ": " + title, RecordedAt = time.GetUtcNow() });
        OrganizationWorkspace.AddAudit(db, context, subject, "Procurement" + command.Action, "PropertyCase", item.Id, new { From = from, To = stage, Target = target, command.DueAt, Reason = reason, Clarification = clarification, DataRevision = listing.DataRevision }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }
    public async Task AddNoteAsync(Subject subject, AddCaseNote command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Listings.FromSqlInterpolated($"SELECT * FROM catalog.listings WHERE id={command.ListingId} AND organization_id={context.OrganizationId} FOR UPDATE").LoadAsync(cancellationToken);
        Row row = await Visible(db, context).SingleOrDefaultAsync(value => value.Listing.Id == command.ListingId, cancellationToken) ?? throw new AccessDeniedException();
        if (row.Case == null || row.Assignment?.EmployeeId != context.EmployeeId) throw new AccessDeniedException();
        await access.RequireAsync(subject, row.Case.StageId == "pending_head" ? Permissions.HeadDecide : Permissions.ManagerDecide, cancellationToken);
        if (row.Case.Version != command.ExpectedCaseVersion) throw new DbUpdateConcurrencyException();
        if (command.EffectiveAt?.Offset != null && command.EffectiveAt.Value.Offset != TimeSpan.Zero || command.EffectiveAt > time.GetUtcNow().AddMinutes(5)) throw new ArgumentException("Укажите фактическое время контакта UTC.");
        string text = Text(command.Text, true); string result = Text(command.ContactResult, command.Contact);
        db.Entry(row.Case).Property(item => item.Version).IsModified = true;
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(),
            OrganizationId = context.OrganizationId,
            ObjectType = "PropertyCase",
            ObjectId = row.Case.Id,
            ActorEmployeeId = context.EmployeeId,
            Kind = command.Contact ? "Contact" : "Note",
            Title = command.Contact ? "Контакт с продавцом" : "Рабочая заметка",
            Body = text + (command.Contact ? "\nРезультат: " + result : ""),
            EffectiveAt = command.EffectiveAt,
            RecordedAt = time.GetUtcNow()
        });
        OrganizationWorkspace.AddAudit(db, context, subject, command.Contact ? "SellerContactRecorded" : "CaseNoteAdded", "PropertyCase", row.Case.Id, new { command.Contact, command.EffectiveAt }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<NotificationView>> ReadNotificationsAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken); await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Notifications.Where(item => item.OrganizationId == context.OrganizationId && item.EmployeeId == context.EmployeeId).OrderByDescending(item => item.RecordedAt).Take(50)
            .Select(item => new NotificationView(item.Id, item.ObjectId, db.PropertyCases.Where(value => value.Id == item.ObjectId && value.OrganizationId == context.OrganizationId).Select(value => value.ListingId).First(), item.Title, item.RecordedAt, item.ReadAt != null)).ToArrayAsync(cancellationToken);
    }
    public async Task MarkNotificationReadAsync(Subject subject, Guid id, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.QueueRead, cancellationToken); await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        InternalNotification item = await db.Notifications.SingleOrDefaultAsync(value => value.Id == id && value.OrganizationId == context.OrganizationId && value.EmployeeId == context.EmployeeId, cancellationToken) ?? throw new AccessDeniedException();
        item.ReadAt ??= time.GetUtcNow(); await db.SaveChangesAsync(cancellationToken);
    }
    private async Task<bool> AllowedAsync(Subject subject, string permission, CancellationToken cancellationToken) { try { await access.RequireAsync(subject, permission, cancellationToken); return true; } catch (AccessDeniedException) { return false; } }
    private static Task<DecisionTarget[]> TargetsAsync(LandErpDbContext db, Guid organization, string permission, CancellationToken cancellationToken, Listing? listing = null)
    {
        var query = from employee in db.Employees
                    join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
                    join grant in db.RolePermissions on assignment.RoleId equals grant.RoleId
                    where employee.OrganizationId == organization && employee.Active && grant.PermissionId == permission
                    select new { employee.Id, employee.DisplayName, assignment.Scope, assignment.OrgUnitId, assignment.TeamId };
        if (listing != null)
        {
            Guid? department = listing.DepartmentId; Guid? team = listing.TeamId;
            query = query.Where(item => item.Scope == AccessScope.Organization || item.Scope == AccessScope.Own || item.Scope == AccessScope.AssignedObjects
                || item.Scope == AccessScope.Department && department != null && item.OrgUnitId == department || item.Scope == AccessScope.Team && team != null && item.TeamId == team);
        }
        return query.Select(item => new { item.Id, item.DisplayName }).Distinct().OrderBy(item => item.DisplayName)
            .Select(item => new DecisionTarget(item.Id, item.DisplayName)).ToArrayAsync(cancellationToken);
    }
    private static QueueItem Item(Row row, Dictionary<Guid, string> names) => new(row.Listing.Id, row.Case?.Id, row.Case?.BusinessNumber, row.Listing.Title ?? "Название неизвестно", row.Listing.Source, row.Listing.Price, row.Listing.Currency, row.Listing.AreaSquareMeters, row.Listing.Location,
        row.Case?.StageId ?? "new", row.Assignment == null ? null : names.GetValueOrDefault(row.Assignment.EmployeeId, "Сотрудник"), row.Task?.DueAt,
        row.Case?.StageId == "returned" ? "Руководитель вернул: требуются исправления" : row.Listing.QueueReason,
        new[] { row.Listing.Price == null ? "цена" : null, row.Listing.AreaSquareMeters == null ? "площадь" : null, row.Listing.Location == null ? "местоположение" : null }.OfType<string>().ToArray(),
        row.Case != null && row.Listing.DataRevision > row.Case.ReviewedDataRevision, row.Listing.DataRevision, row.Case?.Version ?? 0);
    private static string Text(string text, bool required) => text.Length > 4000 || required && text.Trim().Length < 3 ? throw new ArgumentException("Укажите пояснение от 3 до 4000 символов.") : text.Trim();
    public static string ActionLabel(ProcurementAction action, bool head) => action switch { ProcurementAction.TakeWork => "В работу", ProcurementAction.Monitor => head ? "Руководитель: наблюдать" : "Наблюдать", ProcurementAction.Clarify => "Уточнить", ProcurementAction.Reject => head ? "Руководитель отклонил" : "Отклонить", ProcurementAction.Forward => "Передан руководителю", ProcurementAction.Return => "Возвращён менеджеру", _ => "Дальнейшая работа одобрена" };
}
