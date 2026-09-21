using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.IdentityAccess.Domain;

public sealed class PermissionDefinition
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class RolePermissionGrant
{
    public Guid RoleId { get; set; }
    public string PermissionId { get; set; } = "";
}

public sealed class EmployeeAccessSettings
{
    public Guid EmployeeId { get; set; }
    public IncomingAccessLevel IncomingAccess { get; set; }
    public ProcurementAccessLevel ProcurementAccess { get; set; }
    public AccessScope ProcurementReadScope { get; set; }
    public AccessScope ProcurementWorkScope { get; set; }
    public CollectionAccessLevel CollectionAccess { get; set; }
    public bool CanAssignInspections { get; set; }
    public bool CanPerformInspections { get; set; }
    public bool CanConfirmPurchase { get; set; }
    public bool CanManageTemplates { get; set; }
    public bool CanReadAudit { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class EmployeeInvitation
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public Guid ObjectId { get; set; }
    public string Changes { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}
