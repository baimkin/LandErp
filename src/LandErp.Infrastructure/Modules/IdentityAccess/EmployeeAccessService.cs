using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.IdentityAccess;

/// <summary>
/// Resolves the effective Access V1 settings used by workflow authorization.
/// Missing explicit settings deliberately fall back to the current permission model during the AP-02/AP-03 transition.
/// </summary>
public sealed class EmployeeAccessService(IDbContextFactory<LandErpDbContext> factory) : IEmployeeAccessService
{
    private sealed record EmployeeRow(Guid Id, Guid OrganizationId, Guid? DepartmentId, Guid? TeamId,
        Guid RoleId, AccessScope Scope, string RoleName);

    public void Validate(EmployeeAccessConfiguration settings) => EmployeeAccessRules.Validate(settings);

    public async Task<EffectiveEmployeeAccess> ResolveAsync(Subject subject, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        EmployeeRow? row = await (from employee in db.Employees.AsNoTracking()
                                  join assignment in db.EmployeeAssignments.AsNoTracking() on employee.Id equals assignment.EmployeeId
                                  join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                                  where employee.UserId == subject.UserId && employee.Active
                                  select new EmployeeRow(employee.Id, employee.OrganizationId, assignment.OrgUnitId,
                                      assignment.TeamId, assignment.RoleId, assignment.Scope, role.Name!))
            .SingleOrDefaultAsync(cancellationToken);
        if (row == null) throw new AccessDeniedException();
        return (await ResolveRowsAsync(db, [row], cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<EffectiveEmployeeAccess>> ResolveActiveEmployeesAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        EmployeeRow[] rows = await (from employee in db.Employees.AsNoTracking()
                                    join assignment in db.EmployeeAssignments.AsNoTracking() on employee.Id equals assignment.EmployeeId
                                    join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                                    where employee.OrganizationId == organizationId && employee.Active
                                    select new EmployeeRow(employee.Id, employee.OrganizationId, assignment.OrgUnitId,
                                        assignment.TeamId, assignment.RoleId, assignment.Scope, role.Name!))
            .ToArrayAsync(cancellationToken);
        return await ResolveRowsAsync(db, rows, cancellationToken);
    }

    public async Task<IReadOnlyList<EffectiveEmployeeAccess>> ResolveEmployeesAsync(
        Guid organizationId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        if (employeeIds.Count == 0) return [];
        Guid[] ids = employeeIds.Distinct().ToArray();
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        EmployeeRow[] rows = await (from employee in db.Employees.AsNoTracking()
                                    join assignment in db.EmployeeAssignments.AsNoTracking() on employee.Id equals assignment.EmployeeId
                                    join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                                    where employee.OrganizationId == organizationId && ids.Contains(employee.Id)
                                    select new EmployeeRow(employee.Id, employee.OrganizationId, assignment.OrgUnitId,
                                        assignment.TeamId, assignment.RoleId, assignment.Scope, role.Name!))
            .ToArrayAsync(cancellationToken);
        return await ResolveRowsAsync(db, rows, cancellationToken);
    }

    private async Task<IReadOnlyList<EffectiveEmployeeAccess>> ResolveRowsAsync(
        LandErpDbContext db, IReadOnlyCollection<EmployeeRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];
        Guid[] employeeIds = rows.Select(item => item.Id).ToArray();
        Dictionary<Guid, EmployeeAccessSettings> configured = await db.EmployeeAccessSettings.AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId))
            .ToDictionaryAsync(item => item.EmployeeId, cancellationToken);
        Guid[] roleIds = rows.Select(item => item.RoleId).Distinct().ToArray();
        var grantRows = await db.RolePermissions.AsNoTracking()
            .Where(item => roleIds.Contains(item.RoleId))
            .Select(item => new { item.RoleId, item.PermissionId })
            .ToArrayAsync(cancellationToken);
        Dictionary<Guid, string[]> permissions = grantRows.GroupBy(item => item.RoleId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.PermissionId).ToArray());

        List<EffectiveEmployeeAccess> result = new(rows.Count);
        foreach (EmployeeRow row in rows)
        {
            EmployeeAccessConfiguration settings;
            EmployeeAccessSource source;
            if (string.Equals(row.RoleName, "Owner", StringComparison.Ordinal))
            {
                settings = OwnerConfiguration();
                source = EmployeeAccessSource.SystemOwner;
            }
            else if (configured.TryGetValue(row.Id, out EmployeeAccessSettings? explicitSettings))
            {
                settings = ToConfiguration(explicitSettings);
                Validate(settings);
                source = EmployeeAccessSource.Configured;
            }
            else
            {
                settings = LegacyConfiguration(permissions.GetValueOrDefault(row.RoleId) ?? [], row.Scope);
                Validate(settings);
                source = EmployeeAccessSource.LegacyPermissions;
            }

            result.Add(Effective(row.Id, row.OrganizationId, row.DepartmentId, row.TeamId, settings, source));
        }

        return result;
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
