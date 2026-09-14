using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Organization.Domain;

public sealed class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string BusinessTimeZone { get; set; } = "Europe/Moscow";
}

public sealed class OrgUnit
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public long Version { get; set; } = 1;
}

public sealed class Position
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public long Version { get; set; } = 1;
}

public sealed class Team
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid OrgUnitId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Employee
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public bool Active { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class EmployeeAssignment
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? OrgUnitId { get; set; }
    public Guid? PositionId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? ManagerEmployeeId { get; set; }
    public Guid RoleId { get; set; }
    public AccessScope Scope { get; set; }
    public long Version { get; set; } = 1;
}
