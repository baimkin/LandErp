namespace LandErp.Application.Modules.IdentityAccess.Contracts;

public enum AccessScope { Own, AssignedObjects, Team, Department, Organization }

public sealed record Subject(Guid UserId, bool MultiFactorAuthenticated);
public sealed record AccessContext(Guid EmployeeId, Guid OrganizationId, Guid? DepartmentId,
    Guid? TeamId, AccessScope Scope);

public interface IAccessControl
{
    Task<AccessContext> RequireAsync(Subject subject, string permission, CancellationToken cancellationToken);
}

public sealed class AccessDeniedException : Exception
{
    public AccessDeniedException() : base("Нет права на действие или область данных.") { }
}

public static class Permissions
{
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string OrganizationManage = "organization.manage";
    public const string RolesManage = "roles.manage";
    public const string AuditRead = "audit.read";
    public const string AgentsManage = "agents.manage";
    public const string CollectionManage = "searches.manage";
    public const string QueueRead = "manager_queue.read";
    public const string ManagerDecide = "manager_decisions.create";
    public const string HeadDecide = "procurement_approvals.decide";
}
