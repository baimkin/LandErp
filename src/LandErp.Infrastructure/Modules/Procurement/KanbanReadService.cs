using System.Text;
using System.Text.Json;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Workflow.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class KanbanWorkspace
{
    private sealed record CardCursor(int TaskRank, DateTimeOffset Due, string Number, Guid Id);
    private static CardCursor Key(KanbanCardView c) => new(c.NextTask == null ? 2 : c.NextTask.DueAt == null ? 1 : 0,
        c.NextTask?.DueAt ?? DateTimeOffset.MaxValue, c.BusinessNumber, c.Membership.Id);
    private static int Compare(CardCursor a, CardCursor b)
    {
        int n = a.TaskRank.CompareTo(b.TaskRank); if (n != 0) return n;
        n = a.Due.CompareTo(b.Due); if (n != 0) return n;
        n = string.Compare(a.Number, b.Number, StringComparison.Ordinal); return n != 0 ? n : a.Id.CompareTo(b.Id);
    }
    public async Task<KanbanBoardView> ReadBoardAsync(Subject subject, Guid pipelineId, IReadOnlyDictionary<Guid, string>? cursors, CancellationToken ct)
    {
        var a = await access.ResolveAsync(subject, ct);
        if (!a.CanReadProcurement) throw new AccessDeniedException();
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var p = await db.KanbanPipelines.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == a.OrganizationId && x.Id == pipelineId && x.IsActive, ct) ?? throw new AccessDeniedException();
        var stages = await db.KanbanStages.AsNoTracking().Where(x => x.OrganizationId == a.OrganizationId && x.PipelineId == p.Id && x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToArrayAsync(ct);
        var readable = ProcurementVisibility.ApplyRead(db.PropertyCases.AsNoTracking(), db, a);
        var rows = await (from m in db.KanbanMemberships.AsNoTracking()
                          join item in readable on m.PropertyCaseId equals item.Id
                          join assignment in db.WorkAssignments.AsNoTracking() on item.AssignmentId equals assignment.Id
                          join employee in db.Employees.AsNoTracking() on assignment.EmployeeId equals employee.Id
                          where m.OrganizationId == a.OrganizationId && m.PipelineId == p.Id && m.TransferredAt == null
                          select new { Membership = m, Case = item, Assignee = employee.DisplayName }).ToArrayAsync(ct);
        var ids = rows.Select(x => x.Case.Id).ToArray();
        // One batch of tasks for the entire visible membership set, no per-card detail calls.
        var tasks = await db.WorkTasks.AsNoTracking().Where(t => t.OrganizationId == a.OrganizationId && t.ObjectType == "PropertyCase"
            && ids.Contains(t.ObjectId) && !t.Completed && !t.Deleted).OrderBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenBy(t => t.RecordedAt).ThenBy(t => t.Id).ToArrayAsync(ct);
        var nearest = tasks.GroupBy(t => t.ObjectId).ToDictionary(g => g.Key, g => g.First());
        var workable = a.CanManageProcurement ? (await ProcurementVisibility.Apply(db.PropertyCases, db, a.ProcurementWorkContext)
            .Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToArrayAsync(ct)).ToHashSet() : [];
        var now = clock.GetUtcNow();
        var cards = rows.Select(x => {
            var t = nearest.GetValueOrDefault(x.Case.Id);
            return new KanbanCardView(x.Membership, x.Case.BusinessNumber, x.Case.WorkingTitle, x.Case.WorkingLocation, x.Case.WorkingPrice,
                x.Case.Currency, x.Case.WorkingAreaSquareMeters, x.Assignee, x.Case.StageId,
                t == null ? null : new(t.Id, t.Title, t.DueAt, t.DueHasTime, WorkTaskDeadline.Overdue(t, now)), workable.Contains(x.Case.Id));
        }).OrderBy(x => Key(x), Comparer<CardCursor>.Create(Compare)).ToArray();
        List<KanbanColumn> columns = [];
        foreach (var s in stages)
        {
            var all = cards.Where(x => x.Membership.StageId == s.Id).ToArray();
            IEnumerable<KanbanCardView> remaining = all;
            if (cursors?.TryGetValue(s.Id, out string? cursor) == true)
            {
                CardCursor key;
                try { key = JsonSerializer.Deserialize<CardCursor>(Encoding.UTF8.GetString(Convert.FromBase64String(cursor))) ?? throw new FormatException(); }
                catch (Exception ex) when (ex is FormatException or JsonException) { throw new ArgumentException("Обновите доску: некорректная позиция страницы."); }
                remaining = remaining.Where(x => Compare(Key(x), key) > 0);
            }
            var page = remaining.Take(51).ToArray();
            var slice = page.Take(50).ToArray();
            string? next = page.Length > 50 ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(Key(slice[^1]))) : null;
            columns.Add(new(s, slice, all.Length, all.Where(x => x.Price != null).GroupBy(x => x.Currency)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Price!.Value)), all.Count(x => x.Price == null), next));
        }
        return new(p, columns, cards.Length, columns.Where(x => x.Stage.IsHiddenOnBoard).Sum(x => x.Total), now);
    }

    public async Task<IReadOnlyList<KanbanCandidate>> CandidatesAsync(Subject subject, Guid pipelineId, string text, CancellationToken ct)
    {
        var a = await access.ResolveAsync(subject, ct);
        if (!a.CanManageProcurement) throw new AccessDeniedException();
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.KanbanPipelines.AnyAsync(x => x.Id == pipelineId && x.OrganizationId == a.OrganizationId && x.IsActive, ct)) throw new AccessDeniedException();
        var q = ProcurementVisibility.Apply(db.PropertyCases.AsNoTracking(), db, a.ProcurementWorkContext)
            .Where(x => !db.KanbanMemberships.Any(m => m.OrganizationId == a.OrganizationId && m.PropertyCaseId == x.Id && m.PipelineId == pipelineId && m.TransferredAt == null));
        if (!string.IsNullOrWhiteSpace(text)) q = q.Where(x => x.BusinessNumber.Contains(text.Trim()) || x.WorkingTitle.Contains(text.Trim()));
        return await q.OrderBy(x => x.BusinessNumber).Take(50).Select(x => new KanbanCandidate(x.Id, x.BusinessNumber, x.WorkingTitle,
            db.KanbanMemberships.Where(m => m.OrganizationId == a.OrganizationId && m.PropertyCaseId == x.Id && m.PipelineId == pipelineId).Select(m => (long?)m.Version).SingleOrDefault())).ToArrayAsync(ct);
    }
}
