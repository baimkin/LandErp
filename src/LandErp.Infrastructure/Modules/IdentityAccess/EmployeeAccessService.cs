using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.IdentityAccess;

/// <summary>
/// Resolves Access V1 settings without switching existing authorization checks.
/// Missing explicit settings deliberately fall back to the current permission model until AP-02.
/// </summary>
public sealed class EmployeeAccessService(IDbContextFactory<LandErpDbContext> factory) : IEmployeeAccessService
{
    public void Validate(EmployeeAccessConfiguration settings) => EmployeeAccessRules.Validate(settings);

    public async Task<EffectiveEmployeeAccess> ResolveAsync(Subject subject, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var employee = await (from value in db.Employees.AsNoTracking()
                              join assignment in db.EmployeeAssignments.AsNoTracking() on value.Id equals assignment.EmployeeId
                              join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                              where value.UserId == subject.UserId && value.Active
                              select new
                              {
                                  value.Id,
                                  value.OrganizationId,
                                  assignment.OrgUnitId,
                                  assignment.TeamId,
                                  assignment.RoleId,
                                  assignment.Scope,
                                  RoleName = role.Name
                              }).SingleOrDefaultAsync(cancellationToken);

        if (employee == null) throw new AccessDeniedException();

        if (string.Equals(employee.RoleName, "Owner", StringComparison.Ordinal))
            return Effective(employee.Id, employee.OrganizationId, employee.OrgUnitId, employee.TeamId,
                OwnerConfiguration(), EmployeeAccessSource.SystemOwner);

        EmployeeAccessSettings? configured = await db.EmployeeAccessSettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.EmployeeId == employee.Id, cancellationToken);
        if (configured != null)
        {
            EmployeeAccessConfiguration settings = ToConfiguration(configured);
            Validate(settings);
            return Effective(employee.Id, employee.OrganizationId, employee.OrgUnitId, employee.TeamId,
                settings, EmployeeAccessSource.Configured);
        }

        string[] permissions = await db.RolePermissions.AsNoTracking()
            .Where(item => item.RoleId == employee.RoleId)
            .Select(item => item.PermissionId)
            .ToArrayAsync(cancellationToken);
        EmployeeAccessConfiguration legacy = LegacyConfiguration(permissions, employee.Scope);
        Validate(legacy);
        return Effective(employee.Id, employee.OrganizationId, employee.OrgUnitId, employee.TeamId,
            legacy, EmployeeAccessSource.LegacyPermissions);
    }

    private static EffectiveEmployeeAccess Effective(Guid employeeId, Guid organizationId, Guid? departmentId,
        Guid? teamId, EmployeeAccessConfiguration settings, EmployeeAccessSource source) =>
        new(employeeId, organizationId, departmentId, teamId, settings, source);

    private static EmployeeAccessConfiguration ToConfiguration(EmployeeAccessSettings value) =>
        new(value.IncomingAccess, value.ProcurementAccess, value.ProcurementReadScope, value.ProcurementWorkScope,
            value.CollectionAccess, value.CanAssignInspections, value.CanPerformInspections,
            value.CanConfirmPurchase, value.CanManageTemplates, value.CanReadAudit);

    private static EmployeeAccessConfiguration OwnerConfiguration() =>
        new(IncomingAccessLevel.Process, ProcurementAccessLevel.Head,
            AccessScope.Organization, AccessScope.Organization, CollectionAccessLevel.Manage,
            CanAssignInspections: true, CanPerformInspections: true, CanConfirmPurchase: true,
            CanManageTemplates: true, CanReadAudit: true);

    private static EmployeeAccessConfiguration LegacyConfiguration(IEnumerable<string> permissionValues, AccessScope scope)
    {
        HashSet<string> permissions = permissionValues.ToHashSet(StringComparer.Ordinal);
        bool Has(string permission) => permissions.Contains(permission);

        IncomingAccessLevel incoming = Has(Permissions.ManagerDecide)
            ? IncomingAccessLevel.Process
            : Has(Permissions.QueueRead) ? IncomingAccessLevel.Read : IncomingAccessLevel.None;

        ProcurementAccessLevel procurement = Has(Permissions.HeadDecide)
            ? ProcurementAccessLevel.Head
            : Has(Permissions.ManagerDecide)
                ? ProcurementAccessLevel.Manager
                : Has(Permissions.QueueRead) ? ProcurementAccessLevel.Read : ProcurementAccessLevel.None;

        CollectionAccessLevel collection = Has(Permissions.CollectionManage) || Has(Permissions.AgentsManage)
            ? CollectionAccessLevel.Manage
            : Has(Permissions.CollectionRead) ? CollectionAccessLevel.Read : CollectionAccessLevel.None;

        return new(incoming, procurement, scope, scope, collection,
            CanAssignInspections: Has(Permissions.InspectionRequest),
            CanPerformInspections: Has(Permissions.InspectionPerform),
            CanConfirmPurchase: Has(Permissions.PurchaseConfirm),
            CanManageTemplates: Has(Permissions.ManagerDecide) || Has(Permissions.HeadDecide),
            CanReadAudit: Has(Permissions.AuditRead));
    }
}
