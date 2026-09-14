namespace LandErp.Application.Modules.Workflow.Domain;

public sealed class WorkflowStage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
public sealed class Assignment
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string ObjectType { get; set; } = "";
    public Guid ObjectId { get; set; }
    public Guid EmployeeId { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class WorkTask
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string ObjectType { get; set; } = "";
    public Guid ObjectId { get; set; }
    public string Title { get; set; } = "";
    public Guid EmployeeId { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long Version { get; set; } = 1;
}
public sealed class WorkflowTransition
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string ObjectType { get; set; } = "";
    public Guid ObjectId { get; set; }
    public string FromStageId { get; set; } = "";
    public string ToStageId { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid ActorEmployeeId { get; set; }
    public long ObjectVersion { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
public sealed class Approval
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string ObjectType { get; set; } = "";
    public Guid ObjectId { get; set; }
    public Guid RequesterEmployeeId { get; set; }
    public Guid ApproverEmployeeId { get; set; }
    public string Outcome { get; set; } = "";
    public string Reason { get; set; } = "";
    public long ConsideredDataRevision { get; set; }
    public long ObjectVersion { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
public sealed class BusinessTimelineEntry
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string ObjectType { get; set; } = "";
    public Guid ObjectId { get; set; }
    public Guid ActorEmployeeId { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public Guid? TargetEmployeeId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
}
public sealed class InternalNotification
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid EmployeeId { get; set; }
    public string ObjectType { get; set; } = "";
    public Guid ObjectId { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public long Version { get; set; } = 1;
}
