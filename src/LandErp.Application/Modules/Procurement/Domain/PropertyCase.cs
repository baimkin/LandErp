namespace LandErp.Application.Modules.Procurement.Domain;

public sealed class PropertyCase
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    /// <summary>Compatibility-only pointer retained until Phase 9. New business code uses source links.</summary>
    public Guid? ListingId { get; set; }
    public string BusinessNumber { get; set; } = "";
    public string WorkingTitle { get; set; } = "";
    public decimal? WorkingPrice { get; set; }
    public string Currency { get; set; } = "RUB";
    public decimal? WorkingAreaSquareMeters { get; set; }
    public string? WorkingLocation { get; set; }
    public string? CadastralNumber { get; set; }
    public string FactsProvenance { get; set; } = "System";
    public Guid? DepartmentId { get; set; }
    public Guid? TeamId { get; set; }
    public string StageId { get; set; } = "analysis";
    public Guid ManagerEmployeeId { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid WorkTaskId { get; set; }
    public Guid? PendingApprovalId { get; set; }
    public long ReviewedDataRevision { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class PropertyCaseSourceLink
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PropertyCaseId { get; set; }
    public Guid CatalogItemId { get; set; }
    public bool Confirmed { get; set; } = true;
    public string RelationType { get; set; } = "Source";
    public Guid? ActorEmployeeId { get; set; }
    public string Provenance { get; set; } = "User";
    public long ReviewedDataRevision { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
