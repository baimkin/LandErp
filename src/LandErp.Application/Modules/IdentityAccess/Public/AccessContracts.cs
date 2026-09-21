namespace LandErp.Application.Modules.IdentityAccess.Contracts;

public enum AccessScope { Own, AssignedObjects, Team, Department, Organization }

public enum IncomingAccessLevel { None, Read, Process }
public enum ProcurementAccessLevel { None, Read, Manager, Head }
public enum CollectionAccessLevel { None, Read, Manage }
public enum EmployeeAccessSource { Configured, LegacyPermissions, SystemOwner }

public sealed record EmployeeAccessConfiguration(
    IncomingAccessLevel IncomingAccess,
    ProcurementAccessLevel ProcurementAccess,
    AccessScope ProcurementReadScope,
    AccessScope ProcurementWorkScope,
    CollectionAccessLevel CollectionAccess,
    bool CanAssignInspections,
    bool CanPerformInspections,
    bool CanConfirmPurchase,
    bool CanManageTemplates,
    bool CanReadAudit);

public static class EmployeeAccessRules
{
    public static void Validate(EmployeeAccessConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.IncomingAccess)
            || !Enum.IsDefined(settings.ProcurementAccess)
            || !Enum.IsDefined(settings.CollectionAccess)
            || !Enum.IsDefined(settings.ProcurementReadScope)
            || !Enum.IsDefined(settings.ProcurementWorkScope))
        {
            throw new ArgumentException("Настройки доступа содержат неизвестное значение.");
        }

        if (!ContainsScope(settings.ProcurementReadScope, settings.ProcurementWorkScope))
            throw new ArgumentException("Область работы закупки не может быть шире области просмотра.");
    }

    public static bool ContainsScope(AccessScope allowedScope, AccessScope requestedScope)
    {
        if (!Enum.IsDefined(allowedScope) || !Enum.IsDefined(requestedScope))
            throw new ArgumentException("Неизвестная область доступа.");
        return ScopeRank(requestedScope) <= ScopeRank(allowedScope);
    }

    private static int ScopeRank(AccessScope scope) => scope switch
    {
        AccessScope.Own => 0,
        AccessScope.AssignedObjects => 1,
        AccessScope.Team => 2,
        AccessScope.Department => 3,
        AccessScope.Organization => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };
}

public sealed record Subject(Guid UserId, bool MultiFactorAuthenticated);
public sealed record AccessContext(Guid EmployeeId, Guid OrganizationId, Guid? DepartmentId,
    Guid? TeamId, AccessScope Scope);

public sealed record EffectiveEmployeeAccess(
    Guid EmployeeId,
    Guid OrganizationId,
    Guid? DepartmentId,
    Guid? TeamId,
    EmployeeAccessConfiguration Settings,
    EmployeeAccessSource Source)
{
    public bool IsSystemOwner => Source == EmployeeAccessSource.SystemOwner;
    public bool CanReadIncoming => Settings.IncomingAccess >= IncomingAccessLevel.Read;
    public bool CanProcessIncoming => Settings.IncomingAccess >= IncomingAccessLevel.Process;
    public bool CanReadProcurement => Settings.ProcurementAccess >= ProcurementAccessLevel.Read;
    public bool CanManageProcurement => Settings.ProcurementAccess >= ProcurementAccessLevel.Manager;
    public bool CanHeadProcurement => Settings.ProcurementAccess >= ProcurementAccessLevel.Head;
    public bool CanReadCollection => Settings.CollectionAccess >= CollectionAccessLevel.Read;
    public bool CanManageCollection => Settings.CollectionAccess >= CollectionAccessLevel.Manage;
    public AccessContext OrganizationContext =>
        new(EmployeeId, OrganizationId, DepartmentId, TeamId, AccessScope.Organization);
    public AccessContext ProcurementReadContext =>
        new(EmployeeId, OrganizationId, DepartmentId, TeamId, Settings.ProcurementReadScope);
    public AccessContext ProcurementWorkContext =>
        new(EmployeeId, OrganizationId, DepartmentId, TeamId, Settings.ProcurementWorkScope);
}

public interface IEmployeeAccessService
{
    Task<EffectiveEmployeeAccess> ResolveAsync(Subject subject, CancellationToken cancellationToken);
    Task<IReadOnlyList<EffectiveEmployeeAccess>> ResolveActiveEmployeesAsync(
        Guid organizationId, CancellationToken cancellationToken);
    void Validate(EmployeeAccessConfiguration settings);
}

public interface IAccessControl
{
    Task<AccessContext> ResolveAsync(Subject subject, CancellationToken cancellationToken);
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
    public const string CollectionRead = "collection.read";
    public const string CollectionManage = "searches.manage";
    public const string QueueRead = "manager_queue.read";
    public const string ManagerDecide = "manager_decisions.create";
    public const string HeadDecide = "procurement_approvals.decide";
    public const string PurchaseConfirm = "procurement_purchase.confirm";
    public const string InspectionRead = "inspections.read";
    public const string InspectionRequest = "inspections.request";
    public const string InspectionPerform = "inspections.perform";
}
