namespace LandErp.Application.Modules.Procurement.Domain;

public enum KanbanStageKind { Working, PositiveFinal, NegativeFinal }
public enum KanbanTunnelMode { Transfer, Parallel }

// These positions are deliberately independent of PropertyCase.StageId and workflow tasks.
public sealed class KanbanPipeline
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}
public sealed class KanbanStage
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PipelineId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ColorKey { get; set; } = "info";
    public int SortOrder { get; set; }
    public bool IsInitial { get; set; }
    public KanbanStageKind Kind { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsHiddenOnBoard { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class KanbanMembership
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PropertyCaseId { get; set; }
    public Guid PipelineId { get; set; }
    public Guid StageId { get; set; }
    public DateTimeOffset StageEnteredAt { get; set; }
    public DateTimeOffset? StageExitedAt { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? TransferredAt { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class KanbanTransition
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid MembershipId { get; set; }
    public Guid PropertyCaseId { get; set; }
    public Guid? FromPipelineId { get; set; }
    public Guid? FromStageId { get; set; }
    public Guid ToPipelineId { get; set; }
    public Guid ToStageId { get; set; }
    public string FromPipelineName { get; set; } = "";
    public string FromStageName { get; set; } = "";
    public string ToPipelineName { get; set; } = "";
    public string ToStageName { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
    public Guid? ActorEmployeeId { get; set; }
    public Guid? CommandId { get; set; }
    public Guid? TunnelId { get; set; }
    public string Kind { get; set; } = "Move";
}
public sealed class KanbanTunnel
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid SourceStageId { get; set; }
    public Guid TargetPipelineId { get; set; }
    public KanbanTunnelMode Mode { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}
