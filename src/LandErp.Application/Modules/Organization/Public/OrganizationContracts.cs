using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Organization.Contracts;

public sealed record NamedItem(Guid Id, string Name);
public sealed record EmployeeView(Guid Id, string Name, string Login, bool Active,
    Guid? DepartmentId, Guid? PositionId, Guid? TeamId, Guid? ManagerId, Guid RoleId,
    string Role, AccessScope Scope, long Version);
public sealed record OrganizationView(string Name, IReadOnlyList<NamedItem> Departments,
    IReadOnlyList<NamedItem> Positions, IReadOnlyList<NamedItem> Teams,
    IReadOnlyList<NamedItem> Roles, IReadOnlyList<EmployeeView> Employees);
public sealed record InviteEmployee(string Name, string Login, Guid? DepartmentId,
    Guid? PositionId, Guid? TeamId, Guid? ManagerId, Guid RoleId, AccessScope Scope);
public sealed record InvitationResult(Guid InvitationId, string OneTimeToken);
public sealed record ChangeAssignment(Guid EmployeeId, Guid? DepartmentId, Guid? PositionId,
    Guid? TeamId, Guid? ManagerId, Guid RoleId, AccessScope Scope, long ExpectedVersion);
public sealed record AuditView(DateTimeOffset RecordedAt, string Action, string ObjectType,
    Guid ObjectId, Guid ActorId, string Changes);

public interface IOrganizationWorkspace
{
    Task<OrganizationView> ReadAsync(Subject subject, CancellationToken cancellationToken);
    Task CreateDepartmentAsync(Subject subject, string name, string correlationId, CancellationToken cancellationToken);
    Task CreatePositionAsync(Subject subject, string name, string correlationId, CancellationToken cancellationToken);
    Task CreateTeamAsync(Subject subject, Guid departmentId, string name, string correlationId, CancellationToken cancellationToken);
    Task<InvitationResult> InviteAsync(Subject subject, InviteEmployee command, string correlationId, CancellationToken cancellationToken);
    Task ChangeAssignmentAsync(Subject subject, ChangeAssignment command, string correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditView>> ReadAuditAsync(Subject subject, CancellationToken cancellationToken);
}
