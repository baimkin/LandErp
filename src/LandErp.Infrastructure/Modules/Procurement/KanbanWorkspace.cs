using System.Text.Json;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class KanbanWorkspace(IDbContextFactory<LandErpDbContext> factory,
    IEmployeeAccessService access, TimeProvider clock) : IKanbanWorkspace
{
    private static async Task<bool> CanConfigure(LandErpDbContext db, EffectiveEmployeeAccess a, Subject subject, CancellationToken ct) =>
        await (from e in db.Employees join assignment in db.EmployeeAssignments on e.Id equals assignment.EmployeeId
               join role in db.Roles on assignment.RoleId equals role.Id join user in db.Users on e.UserId equals user.Id
               where e.Id == a.EmployeeId && e.OrganizationId == a.OrganizationId && e.Active
                   && (role.Name == "ProcurementHead" || ((role.Name == "Owner" || role.Name == "Administrator") && user.TwoFactorEnabled && subject.MultiFactorAuthenticated))
               select e.Id).AnyAsync(ct);

    public async Task<KanbanConfiguration> ReadConfigurationAsync(Subject subject, CancellationToken ct)
    {
        var a = await access.ResolveAsync(subject, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        bool configure = await CanConfigure(db, a, subject, ct);
        if (!a.CanReadProcurement && !configure) throw new AccessDeniedException();
        return new(await db.KanbanPipelines.AsNoTracking().Where(x => x.OrganizationId == a.OrganizationId && x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToArrayAsync(ct),
            await db.KanbanStages.AsNoTracking().Where(x => x.OrganizationId == a.OrganizationId && x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToArrayAsync(ct),
            await db.KanbanTunnels.AsNoTracking().Where(x => x.OrganizationId == a.OrganizationId && x.IsActive).ToArrayAsync(ct), configure, a.CanManageProcurement);
    }

    public async Task<Guid> SavePipelineAsync(Subject subject, SaveKanbanPipeline c, string correlationId, CancellationToken ct)
    {
        var a = await access.ResolveAsync(subject, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, a.OrganizationId, ct);
        if (!await CanConfigure(db, a, subject, ct)) throw new AccessDeniedException();
        var pipelines = await db.KanbanPipelines.Where(x => x.OrganizationId == a.OrganizationId).ToListAsync(ct);
        var stages = await db.KanbanStages.Where(x => x.OrganizationId == a.OrganizationId).ToListAsync(ct);
        var tunnels = await db.KanbanTunnels.Where(x => x.OrganizationId == a.OrganizationId).ToListAsync(ct);
        bool create = c.PipelineId == Guid.Empty;
        var p = create ? new KanbanPipeline { Id = Guid.CreateVersion7(), OrganizationId = a.OrganizationId }
            : pipelines.SingleOrDefault(x => x.Id == c.PipelineId && x.IsActive) ?? throw new AccessDeniedException();
        if (!create && p.Version != c.ExpectedVersion) throw new DbUpdateConcurrencyException("Настройки изменены другим пользователем. Откройте их заново.");
        if (string.IsNullOrWhiteSpace(c.Name) || c.Name.Trim().Length > 120) throw new ArgumentException("Название воронки: от 1 до 120 символов.");
        if (!c.IsActive)
        {
            if (create || p.IsDefault || await db.KanbanMemberships.AnyAsync(x => x.PipelineId == p.Id && x.TransferredAt == null, ct)
                || tunnels.Any(t => t.IsActive && (t.TargetPipelineId == p.Id || stages.Any(s => s.Id == t.SourceStageId && s.PipelineId == p.Id))))
                throw new ArgumentException("Нельзя удалить основную воронку, воронку с объектами или активными туннелями.");
            p.IsActive = false;
        }
        else
        {
            if (c.Stages.Count == 0 || c.Stages.Count(s => s.IsInitial) != 1
                || c.Stages.Count(s => s.IsRejectionTarget) != 1
                || c.Stages.Any(s => s.Id == Guid.Empty || !Enum.IsDefined(s.Kind) || s.IsInitial && s.Kind != KanbanStageKind.Working
                    || s.IsRejectionTarget && s.Kind != KanbanStageKind.NegativeFinal
                    || string.IsNullOrWhiteSpace(s.Name) || s.Name.Trim().Length > 120 || s.Description.Length > 2000
                    || !new[] { "info", "accent", "success", "warning", "danger", "purple" }.Contains(s.ColorKey))
                || c.Stages.Select(s => s.Id).Distinct().Count() != c.Stages.Count)
                throw new ArgumentException("Нужны одна начальная рабочая стадия и одна отрицательная цель системного отклонения, уникальные стадии и заполненные названия (до 120 символов).");
            var oldStages = stages.Where(s => s.PipelineId == p.Id && s.IsActive).ToArray();
            foreach (var removed in oldStages.Where(s => !c.Stages.Any(d => d.Id == s.Id)))
            {
                if (removed.IsInitial || tunnels.Any(t => t.IsActive && t.SourceStageId == removed.Id))
                    throw new ArgumentException("Сначала смените начальную стадию или отключите её туннель.");
                if (await db.KanbanMemberships.AnyAsync(m => m.StageId == removed.Id && m.TransferredAt == null, ct))
                    throw new ArgumentException("Нельзя удалить этап: в нём есть объекты");
            }
            foreach (var draft in c.Stages)
                if (stages.Any(s => s.Id == draft.Id && (s.PipelineId != p.Id || !s.IsActive))) throw new AccessDeniedException();

            // Release filtered unique slots within this transaction before assigning a different initial/default.
            var initial = oldStages.SingleOrDefault(s => s.IsInitial);
            if (initial != null && initial.Id != c.Stages.Single(s => s.IsInitial).Id) initial.IsInitial = false;
            var rejectionTarget = oldStages.SingleOrDefault(s => s.IsRejectionTarget);
            if (rejectionTarget != null && rejectionTarget.Id != c.Stages.Single(s => s.IsRejectionTarget).Id)
                rejectionTarget.IsRejectionTarget = false;
            if (c.IsDefault) foreach (var other in pipelines.Where(x => x.IsDefault && x.Id != p.Id)) other.IsDefault = false;
            else if (p.IsDefault) throw new ArgumentException("Назначьте другую основную воронку; организация не может остаться без основной.");
            await db.SaveChangesAsync(ct);
            if (create) { db.KanbanPipelines.Add(p); pipelines.Add(p); }
            p.Name = c.Name.Trim(); p.SortOrder = c.SortOrder; p.IsDefault = c.IsDefault;
            foreach (var removed in oldStages.Where(s => !c.Stages.Any(d => d.Id == s.Id))) removed.IsActive = false;
            for (int i = 0; i < c.Stages.Count; i++)
            {
                var d = c.Stages[i];
                var s = stages.SingleOrDefault(x => x.Id == d.Id);
                if (s == null) { s = new() { Id = d.Id, OrganizationId = a.OrganizationId, PipelineId = p.Id }; stages.Add(s); db.KanbanStages.Add(s); }
                s.Name = d.Name.Trim(); s.Description = d.Description.Trim(); s.ColorKey = d.ColorKey; s.SortOrder = i;
                s.Kind = d.Kind; s.IsInitial = d.IsInitial; s.IsHiddenOnBoard = d.IsHiddenOnBoard;
                s.IsRejectionTarget = d.IsRejectionTarget;
            }
            ApplyTunnels(db, a.OrganizationId, p, c.Tunnels, pipelines, stages, tunnels);
        }
        if (!create) db.Entry(p).Property(x => x.Version).IsModified = true;
        OrganizationWorkspace.AddAudit(db, a.OrganizationContext, subject, "KanbanConfigured", "KanbanPipeline", p.Id,
            new { PreviousVersion = c.ExpectedVersion, Configuration = c }, correlationId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return p.Id;
    }

    public async Task<KanbanCommandResult> MoveAsync(Subject subject, MoveKanbanCard c, string correlationId, CancellationToken ct)
    {
        var a = await access.ResolveAsync(subject, ct);
        if (!a.CanManageProcurement) throw new AccessDeniedException();
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, a.OrganizationId, ct);
        a = await access.ResolveAsync(subject, ct);
        if (!a.CanManageProcurement) throw new AccessDeniedException();
        await RequireActive(db, a, ct);
        var replay = await ProcurementCommandReplay.BeginAsync(db, a.OrganizationContext, subject, c.CommandId, "KanbanMoved", c, ct);
        var m = await db.KanbanMemberships.SingleOrDefaultAsync(x => x.Id == c.MembershipId && x.OrganizationId == a.OrganizationId, ct) ?? throw new AccessDeniedException();
        if (!await ProcurementVisibility.Apply(db.PropertyCases, db, a.ProcurementWorkContext).AnyAsync(x => x.Id == m.PropertyCaseId, ct)) throw new AccessDeniedException();
        if (replay.ExistingResultId != null) return await ReplayResult(db, replay.AuditId, ct);
        var p = await db.KanbanPipelines.SingleAsync(x => x.Id == m.PipelineId && x.OrganizationId == a.OrganizationId, ct);
        var from = await db.KanbanStages.SingleAsync(x => x.Id == m.StageId, ct);
        if (m.Version != c.ExpectedMembershipVersion || p.Version != c.ExpectedPipelineVersion || m.TransferredAt != null)
            throw new DbUpdateConcurrencyException($"Положение или настройки изменились: «{p.Name}», стадия «{from.Name}». Обновите данные.");
        var target = await db.KanbanStages.SingleOrDefaultAsync(x => x.Id == c.TargetStageId && x.OrganizationId == a.OrganizationId && x.PipelineId == p.Id && x.IsActive, ct);
        if (!p.IsActive || target == null) throw new ArgumentException("Целевая стадия недоступна. Обновите доску.");
        KanbanCommandResult result = new(m.Id, p.Id, target.Id, $"Стадия: {target.Name}");
        if (m.StageId != target.Id)
        {
            DateTimeOffset now = clock.GetUtcNow();
            // Validate tunnel destination before changing either position. Failure rolls back everything.
            var tunnel = await db.KanbanTunnels.SingleOrDefaultAsync(t => t.OrganizationId == a.OrganizationId && t.SourceStageId == target.Id && t.IsActive, ct);
            if (tunnel != null) result = await RunTunnel(db, a, m, p, target, tunnel, c.CommandId, now, ct);
            m.StageId = target.Id; m.StageEnteredAt = now;
            AddTransition(db, m, p, from, p, target, a.EmployeeId, c.CommandId, "Move", now);
            db.BusinessTimeline.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = a.OrganizationId, ObjectType = "PropertyCase", ObjectId = m.PropertyCaseId,
                ActorEmployeeId = a.EmployeeId, Kind = "Kanban", Title = "Изменён статус работы", Body = $"{p.Name}: {from.Name} → {target.Name}. {result.Message}", RecordedAt = now });
        }
        replay.Record(db, a.OrganizationContext, subject, m.PropertyCaseId, m.Id, new { Result = result }, correlationId, clock.GetUtcNow());
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }

    public async Task<KanbanCommandResult> AddAsync(Subject subject, AddKanbanCase c, string correlationId, CancellationToken ct)
    {
        var a = await access.ResolveAsync(subject, ct);
        if (!a.CanManageProcurement) throw new AccessDeniedException();
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, a.OrganizationId, ct);
        a = await access.ResolveAsync(subject, ct);
        if (!a.CanManageProcurement) throw new AccessDeniedException();
        await RequireActive(db, a, ct);
        var replay = await ProcurementCommandReplay.BeginAsync(db, a.OrganizationContext, subject, c.CommandId, "KanbanJoined", c, ct);
        if (!await ProcurementVisibility.Apply(db.PropertyCases, db, a.ProcurementWorkContext).AnyAsync(x => x.Id == c.CaseId, ct)) throw new AccessDeniedException();
        if (replay.ExistingResultId != null) return await ReplayResult(db, replay.AuditId, ct);
        var p = await db.KanbanPipelines.SingleOrDefaultAsync(x => x.OrganizationId == a.OrganizationId && x.Id == c.PipelineId && x.IsActive, ct) ?? throw new AccessDeniedException();
        if (p.Version != c.ExpectedPipelineVersion) throw new DbUpdateConcurrencyException();
        var s = await db.KanbanStages.SingleAsync(x => x.PipelineId == p.Id && x.IsActive && x.IsInitial, ct);
        var m = await db.KanbanMemberships.SingleOrDefaultAsync(x => x.OrganizationId == a.OrganizationId && x.PipelineId == p.Id && x.PropertyCaseId == c.CaseId, ct);
        var now = clock.GetUtcNow();
        string kind = "Join";
        if (m == null)
        {
            if (c.ExpectedMembershipVersion != null) throw new DbUpdateConcurrencyException();
            m = NewMembership(a.OrganizationId, c.CaseId, p, s, now); db.KanbanMemberships.Add(m);
        }
        else
        {
            if (m.TransferredAt == null) throw new ArgumentException("Объект уже участвует в этой воронке.");
            if (m.Version != c.ExpectedMembershipVersion) throw new DbUpdateConcurrencyException("Для возврата прежнего участия обновите список объектов.");
            m.TransferredAt = null; m.StageExitedAt = null; m.StageId = s.Id; m.StageEnteredAt = now; kind = "Resume";
        }
        AddTransition(db, m, null, null, p, s, a.EmployeeId, c.CommandId, kind, now);
        var result = new KanbanCommandResult(m.Id, p.Id, s.Id, $"Объект в воронке «{p.Name}», стадия «{s.Name}»");
        replay.Record(db, a.OrganizationContext, subject, c.CaseId, m.Id, new { Result = result }, correlationId, now);
        db.BusinessTimeline.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = a.OrganizationId, ObjectType = "PropertyCase", ObjectId = c.CaseId,
            ActorEmployeeId = a.EmployeeId, Kind = "Kanban", Title = kind == "Resume" ? "Возвращено участие в воронке" : "Добавлено участие в воронке", Body = result.Message, RecordedAt = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }

    private static async Task RequireActive(LandErpDbContext db, EffectiveEmployeeAccess a, CancellationToken ct)
    { if (!await db.Employees.AnyAsync(e => e.Id == a.EmployeeId && e.OrganizationId == a.OrganizationId && e.Active, ct)) throw new AccessDeniedException(); }
    private static async Task<KanbanCommandResult> ReplayResult(LandErpDbContext db, Guid id, CancellationToken ct)
    {
        string json = await db.AuditEvents.Where(x => x.Id == id).Select(x => x.Changes).SingleAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("Result").Deserialize<KanbanCommandResult>()!;
    }
    internal static KanbanMembership NewMembership(Guid org, Guid caseId, KanbanPipeline p, KanbanStage s, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), OrganizationId = org, PropertyCaseId = caseId, PipelineId = p.Id, StageId = s.Id, StageEnteredAt = now, JoinedAt = now };
    internal static void AddTransition(LandErpDbContext db, KanbanMembership m, KanbanPipeline? fromP, KanbanStage? fromS,
        KanbanPipeline toP, KanbanStage toS, Guid? actor, Guid? command, string kind, DateTimeOffset now, Guid? tunnel = null) =>
        db.KanbanTransitions.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = m.OrganizationId, MembershipId = m.Id, PropertyCaseId = m.PropertyCaseId,
            FromPipelineId = fromP?.Id, FromStageId = fromS?.Id, FromPipelineName = fromP?.Name ?? "", FromStageName = fromS?.Name ?? "",
            ToPipelineId = toP.Id, ToStageId = toS.Id, ToPipelineName = toP.Name, ToStageName = toS.Name,
            ActorEmployeeId = actor, CommandId = command, Kind = kind, RecordedAt = now, TunnelId = tunnel });
}

