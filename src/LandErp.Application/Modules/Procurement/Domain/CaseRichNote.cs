namespace LandErp.Application.Modules.Procurement.Domain;

public enum CaseNoteSection { Working, QuickChecks, DeepChecks }

/// <summary>Current section document; previous values belong to the existing append-only audit.</summary>
public sealed class CaseRichNote
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PropertyCaseId { get; set; }
    public CaseNoteSection Section { get; set; }
    public string DocumentJson { get; set; } = "";
    public Guid UpdatedByEmployeeId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool Deleted { get; set; }
    public long Version { get; set; } = 1;
}
