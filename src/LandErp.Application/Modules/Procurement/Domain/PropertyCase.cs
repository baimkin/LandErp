namespace LandErp.Application.Modules.Procurement.Domain;

public sealed class PropertyCase
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ListingId { get; set; }
    public string BusinessNumber { get; set; } = "";
    public string StageId { get; set; } = "analysis";
    public Guid ManagerEmployeeId { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid WorkTaskId { get; set; }
    public Guid? PendingApprovalId { get; set; }
    public long ReviewedDataRevision { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long Version { get; set; } = 1;
}
