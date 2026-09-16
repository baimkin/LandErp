using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Application.Modules.Organization.Contracts;

public sealed record NamedItem(Guid Id, string Name);
public sealed record DepartmentView(Guid Id, string Name, string Description, Guid? ManagerId, string? Manager,
    bool Active, int TeamCount, int EmployeeCount, long Version);
public sealed record TeamView(Guid Id, Guid DepartmentId, string Name, string Description, Guid? ManagerId,
    string? Manager, bool Active, int EmployeeCount, long Version);
public sealed record PositionView(Guid Id, string Name, string Description, bool Active, int EmployeeCount,
    string[] Departments, long Version);
public enum EmployeeState { Active, PendingActivation, Disabled }
public sealed record EmployeeView(Guid Id, string Name, string Login, EmployeeState State, bool MustChangePassword,
    Guid? DepartmentId, Guid? PositionId, Guid? TeamId, Guid? ManagerId, Guid RoleId,
    string Role, AccessScope Scope, long Version, long EmployeeVersion);
public sealed record OrganizationView(string Name, IReadOnlyList<DepartmentView> Departments,
    IReadOnlyList<PositionView> Positions, IReadOnlyList<TeamView> Teams,
    IReadOnlyList<NamedItem> Roles, IReadOnlyList<EmployeeView> Employees);

public sealed record SaveDepartment(Guid? Id, long? ExpectedVersion, string Name, string Description, Guid? ManagerId);
public sealed record SaveTeam(Guid? Id, long? ExpectedVersion, Guid DepartmentId, string Name, string Description, Guid? ManagerId);
public sealed record SavePosition(Guid? Id, long? ExpectedVersion, string Name, string Description);
public sealed record SetOrganizationItemActive(Guid Id, long ExpectedVersion, bool Active);
public sealed record CreateEmployee(string Name, string Login, Guid? DepartmentId, Guid? PositionId,
    Guid? TeamId, Guid? ManagerId, Guid RoleId, AccessScope Scope, bool MustChangePassword = true);
public sealed record TemporaryCredential(Guid EmployeeId, string Login, string Password);
public sealed record InviteEmployee(string Name, string Login, Guid? DepartmentId,
    Guid? PositionId, Guid? TeamId, Guid? ManagerId, Guid RoleId, AccessScope Scope);
public sealed record InvitationResult(Guid InvitationId, string OneTimeToken);
public sealed record ChangeAssignment(Guid EmployeeId, Guid? DepartmentId, Guid? PositionId,
    Guid? TeamId, Guid? ManagerId, Guid RoleId, AccessScope Scope, long ExpectedVersion);
public sealed record SetEmployeeActive(Guid EmployeeId, long ExpectedVersion, bool Active);
public interface IOrganizationWorkspace
{
    Task<OrganizationView> ReadAsync(Subject subject, CancellationToken cancellationToken);
    Task CreateDepartmentAsync(Subject subject, string name, string correlationId, CancellationToken cancellationToken);
    Task CreateTeamAsync(Subject subject, Guid departmentId, string name, string correlationId, CancellationToken cancellationToken);
    Task CreatePositionAsync(Subject subject, string name, string correlationId, CancellationToken cancellationToken);
    Task SaveDepartmentAsync(Subject subject, SaveDepartment command, string correlationId, CancellationToken cancellationToken);
    Task SetDepartmentActiveAsync(Subject subject, SetOrganizationItemActive command, string correlationId, CancellationToken cancellationToken);
    Task SaveTeamAsync(Subject subject, SaveTeam command, string correlationId, CancellationToken cancellationToken);
    Task SetTeamActiveAsync(Subject subject, SetOrganizationItemActive command, string correlationId, CancellationToken cancellationToken);
    Task SavePositionAsync(Subject subject, SavePosition command, string correlationId, CancellationToken cancellationToken);
    Task SetPositionActiveAsync(Subject subject, SetOrganizationItemActive command, string correlationId, CancellationToken cancellationToken);
    Task<TemporaryCredential> CreateEmployeeAsync(Subject subject, CreateEmployee command, string correlationId, CancellationToken cancellationToken);
    Task<TemporaryCredential> ResetTemporaryPasswordAsync(Subject subject, Guid employeeId, string correlationId, CancellationToken cancellationToken);
    Task SetEmployeeActiveAsync(Subject subject, SetEmployeeActive command, string correlationId, CancellationToken cancellationToken);
    Task<InvitationResult> InviteAsync(Subject subject, InviteEmployee command, string correlationId, CancellationToken cancellationToken);
    Task ChangeAssignmentAsync(Subject subject, ChangeAssignment command, string correlationId, CancellationToken cancellationToken);
}
