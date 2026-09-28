using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class KanbanWorkspace
{
    private static void ApplyTunnels(LandErpDbContext db, Guid org, KanbanPipeline p, IReadOnlyList<KanbanTunnelDraft> drafts,
        List<KanbanPipeline> pipelines, List<KanbanStage> stages, List<KanbanTunnel> tunnels)
    {
        if (drafts.Select(x => x.SourceStageId).Distinct().Count() != drafts.Count) throw new ArgumentException("У стадии может быть только один туннель.");
        foreach (var old in tunnels.Where(t => t.IsActive && stages.Any(s => s.Id == t.SourceStageId && s.PipelineId == p.Id))) old.IsActive = false;
        foreach (var d in drafts)
        {
            var source = stages.SingleOrDefault(s => s.Id == d.SourceStageId && s.PipelineId == p.Id && s.IsActive);
            var target = pipelines.SingleOrDefault(x => x.Id == d.TargetPipelineId && x.IsActive);
            if (source == null || source.IsInitial || source.Kind == KanbanStageKind.Working || target == null || target.Id == p.Id
                || !Enum.IsDefined(d.Mode) || !stages.Any(s => s.PipelineId == target.Id && s.IsInitial && s.IsActive))
                throw new ArgumentException("Туннель ведёт из конечной стадии в начальную другой активной воронки.");
            var t = tunnels.FirstOrDefault(x => x.SourceStageId == source.Id);
            if (t == null) { t = new() { Id = Guid.CreateVersion7(), OrganizationId = org, SourceStageId = source.Id }; db.KanbanTunnels.Add(t); tunnels.Add(t); }
            t.IsActive = true; t.TargetPipelineId = target.Id; t.Mode = d.Mode;
        }
        var edges = tunnels.Where(t => t.IsActive).Select(t => (Source: stages.Single(s => s.Id == t.SourceStageId), Tunnel: t)).ToArray();
        if (edges.Any(e => !e.Source.IsActive || e.Source.IsInitial || e.Source.Kind == KanbanStageKind.Working))
            throw new ArgumentException("Активный туннель требует конечной стадии. Отключите его перед изменением типа.");
        bool Cycle(Guid node, HashSet<Guid> visiting, HashSet<Guid> done)
        {
            if (visiting.Contains(node)) return true;
            if (!done.Add(node)) return false;
            visiting.Add(node);
            foreach (var edge in edges.Where(e => e.Source.PipelineId == node)) if (Cycle(edge.Tunnel.TargetPipelineId, visiting, done)) return true;
            visiting.Remove(node); return false;
        }
        if (pipelines.Any(x => Cycle(x.Id, [], []))) throw new ArgumentException("Туннель создаёт цикл между воронками.");
    }

    private static async Task<KanbanCommandResult> RunTunnel(LandErpDbContext db, EffectiveEmployeeAccess a, KanbanMembership source,
        KanbanPipeline sourcePipeline, KanbanStage final, KanbanTunnel tunnel, Guid command, DateTimeOffset now, CancellationToken ct)
    {
        var p = await db.KanbanPipelines.SingleOrDefaultAsync(x => x.OrganizationId == a.OrganizationId && x.Id == tunnel.TargetPipelineId && x.IsActive, ct);
        var s = p == null ? null : await db.KanbanStages.SingleOrDefaultAsync(x => x.OrganizationId == a.OrganizationId && x.PipelineId == p.Id && x.IsActive && x.IsInitial, ct);
        if (p == null || s == null || s.Kind != KanbanStageKind.Working || final.Kind == KanbanStageKind.Working || sourcePipeline.Id == p.Id)
            throw new ArgumentException("Цель туннеля недоступна. Перемещение не выполнено.");
        var existing = await db.KanbanMemberships.SingleOrDefaultAsync(x => x.OrganizationId == a.OrganizationId && x.PropertyCaseId == source.PropertyCaseId && x.PipelineId == p.Id, ct);
        if (existing != null)
        {
            string stage = await db.KanbanStages.Where(x => x.Id == existing.StageId).Select(x => x.Name).SingleAsync(ct);
            throw new ArgumentException(existing.TransferredAt == null
                ? $"Этот объект уже находится в воронке «{p.Name}», стадия «{stage}». Перемещение не выполнено."
                : $"У объекта есть прежнее переданное участие в воронке «{p.Name}», стадия «{stage}». Используйте явный возврат участия.");
        }
        var joined = NewMembership(a.OrganizationId, source.PropertyCaseId, p, s, now);
        db.KanbanMemberships.Add(joined);
        AddTransition(db, joined, sourcePipeline, final, p, s, a.EmployeeId, command, tunnel.Mode.ToString(), now, tunnel.Id);
        if (tunnel.Mode == KanbanTunnelMode.Transfer) { source.TransferredAt = now; source.StageExitedAt = now; }
        return new(source.Id, p.Id, s.Id, $"{(tunnel.Mode == KanbanTunnelMode.Transfer ? "Передано" : "Добавлено параллельно")}: «{p.Name}», стадия «{s.Name}»");
    }
}
