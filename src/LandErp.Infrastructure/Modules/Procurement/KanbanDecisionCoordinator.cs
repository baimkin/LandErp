using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

/// <summary>
/// Coordinates the two explicitly coupled business commands without making ordinary
/// kanban moves change PropertyCase workflow state.
/// </summary>
internal static class KanbanDecisionCoordinator
{
    public static async Task ApplyRejectionAsync(LandErpDbContext db, PropertyCase propertyCase,
        Guid actorEmployeeId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        KanbanMembership[] memberships = await db.KanbanMemberships
            .Where(item => item.OrganizationId == propertyCase.OrganizationId
                && item.PropertyCaseId == propertyCase.Id && item.TransferredAt == null)
            .OrderBy(item => item.PipelineId).ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        if (memberships.Length == 0) return;

        Guid[] pipelineIds = memberships.Select(item => item.PipelineId).Distinct().ToArray();
        Dictionary<Guid, KanbanPipeline> pipelines = await db.KanbanPipelines
            .Where(item => item.OrganizationId == propertyCase.OrganizationId
                && pipelineIds.Contains(item.Id) && item.IsActive)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        KanbanStage[] stages = await db.KanbanStages
            .Where(item => item.OrganizationId == propertyCase.OrganizationId
                && pipelineIds.Contains(item.PipelineId))
            .ToArrayAsync(cancellationToken);

        foreach (Guid pipelineId in pipelineIds)
        {
            if (!pipelines.TryGetValue(pipelineId, out KanbanPipeline? pipeline))
                throw new ArgumentException("Воронка объекта недоступна. Обновите настройки канбана перед отклонением.");
            int targets = stages.Count(item => item.PipelineId == pipelineId && item.IsActive
                && item.IsRejectionTarget && item.Kind == KanbanStageKind.NegativeFinal);
            if (targets != 1)
                throw new ArgumentException($"В воронке «{pipeline.Name}» должна быть настроена одна цель системного отклонения.");
        }

        foreach (KanbanMembership membership in memberships)
        {
            KanbanPipeline pipeline = pipelines[membership.PipelineId];
            KanbanStage from = stages.Single(item => item.Id == membership.StageId);
            KanbanStage target = stages.Single(item => item.PipelineId == membership.PipelineId
                && item.IsActive && item.IsRejectionTarget && item.Kind == KanbanStageKind.NegativeFinal);
            if (from.Id == target.Id) continue;

            membership.StageId = target.Id;
            membership.StageEnteredAt = now;
            KanbanWorkspace.AddTransition(db, membership, pipeline, from, pipeline, target,
                actorEmployeeId, null, "BusinessReject", now);
            db.BusinessTimeline.Add(new()
            {
                Id = Guid.CreateVersion7(), OrganizationId = propertyCase.OrganizationId,
                ObjectType = "PropertyCase", ObjectId = propertyCase.Id,
                ActorEmployeeId = actorEmployeeId, Kind = "Kanban",
                Title = "Канбан переведён после отклонения",
                Body = $"{pipeline.Name}: {from.Name} → {target.Name}.", RecordedAt = now
            });
        }
    }

    public static async Task ResumeRejectedAsync(LandErpDbContext db, PropertyCase propertyCase,
        Guid actorEmployeeId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        KanbanMembership[] memberships = await db.KanbanMemberships
            .Where(item => item.OrganizationId == propertyCase.OrganizationId
                && item.PropertyCaseId == propertyCase.Id && item.TransferredAt == null)
            .OrderBy(item => item.PipelineId).ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        if (memberships.Length == 0) return;

        Guid[] membershipIds = memberships.Select(item => item.Id).ToArray();
        Guid[] pipelineIds = memberships.Select(item => item.PipelineId).Distinct().ToArray();
        Dictionary<Guid, KanbanPipeline> pipelines = await db.KanbanPipelines
            .Where(item => item.OrganizationId == propertyCase.OrganizationId
                && pipelineIds.Contains(item.Id) && item.IsActive)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        KanbanStage[] stages = await db.KanbanStages
            .Where(item => item.OrganizationId == propertyCase.OrganizationId
                && pipelineIds.Contains(item.PipelineId))
            .ToArrayAsync(cancellationToken);
        KanbanTransition[] history = await db.KanbanTransitions.AsNoTracking()
            .Where(item => item.OrganizationId == propertyCase.OrganizationId
                && item.PropertyCaseId == propertyCase.Id && membershipIds.Contains(item.MembershipId))
            .OrderByDescending(item => item.RecordedAt).ThenByDescending(item => item.Id)
            .ToArrayAsync(cancellationToken);

        foreach (KanbanMembership membership in memberships)
        {
            if (!pipelines.TryGetValue(membership.PipelineId, out KanbanPipeline? pipeline))
                throw new ArgumentException("Воронка объекта недоступна. Обновите настройки канбана перед возобновлением.");
            KanbanStage from = stages.Single(item => item.Id == membership.StageId);
            KanbanStage? target = history.Where(item => item.MembershipId == membership.Id && item.FromStageId != null)
                .Select(item => stages.SingleOrDefault(stage => stage.Id == item.FromStageId
                    && stage.PipelineId == membership.PipelineId && stage.IsActive
                    && stage.Kind == KanbanStageKind.Working))
                .FirstOrDefault(item => item != null);
            target ??= stages.SingleOrDefault(item => item.PipelineId == membership.PipelineId
                && item.IsActive && item.IsInitial && item.Kind == KanbanStageKind.Working)
                ?? throw new ArgumentException($"В воронке «{pipeline.Name}» нет доступной начальной рабочей стадии.");
            if (from.Id == target.Id) continue;

            membership.StageId = target.Id;
            membership.StageEnteredAt = now;
            KanbanWorkspace.AddTransition(db, membership, pipeline, from, pipeline, target,
                actorEmployeeId, null, "BusinessResume", now);
            db.BusinessTimeline.Add(new()
            {
                Id = Guid.CreateVersion7(), OrganizationId = propertyCase.OrganizationId,
                ObjectType = "PropertyCase", ObjectId = propertyCase.Id,
                ActorEmployeeId = actorEmployeeId, Kind = "Kanban",
                Title = "Канбан возвращён в работу",
                Body = $"{pipeline.Name}: {from.Name} → {target.Name}.", RecordedAt = now
            });
        }
    }
}
