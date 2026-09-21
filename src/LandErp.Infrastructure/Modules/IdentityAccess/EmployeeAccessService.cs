using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.IdentityAccess;

/// <summary>
/// Resolves the effective Access V1 settings used by workflow authorization.
/// After AP-04 every non-Owner employee must have explicit EmployeeAccessSettings; missing settings fail closed.
/// </summary>
public sealed class EmployeeAccessService(IDbContextFactory<LandErpDbContext> factory) : IEmployeeAccessService
{
    private sealed record EmployeeRow(Guid Id, Guid OrganizationId, Guid? DepartmentId, Guid? TeamId,
        string RoleName);

    public void Validate(EmployeeAccessConfiguration settings) => EmployeeAccessRules.Validate(settings);

    public async Task<EffectiveEmployeeAccess> ResolveAsync(Subject subject, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        EmployeeRow? row = await (from employee in db.Employees.AsNoTracking()
                                  join assignment in db.EmployeeAssignments.AsNoTracking() on employee.Id equals assignment.EmployeeId
                                  join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                                  where employee.UserId == subject.UserId && employee.Active
                                  select new EmployeeRow(employee.Id, employee.OrganizationId, assignment.OrgUnitId,
                                      assignment.TeamId, role.Name!))
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
                                        assignment.TeamId, role.Name!))
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
                                        assignment.TeamId, role.Name!))
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

        List<EffectiveEmployeeAccess> result = new(rows.Count);
        foreach (EmployeeRow row in rows)
        {
            if (string.Equals(row.RoleName, "Owner", StringComparison.Ordinal))
            {
                result.Add(Effective(row.Id, row.OrganizationId, row.DepartmentId, row.TeamId,
                    OwnerConfiguration(), EmployeeAccessSource.SystemOwner));
                continue;
            }

            if (!configured.TryGetValue(row.Id, out EmployeeAccessSettings? explicitSettings))
                throw new AccessDeniedException();

            EmployeeAccessConfiguration settings = ToConfiguration(explicitSettings);
            Validate(settings);
            result.Add(Effective(row.Id, row.OrganizationId, row.DepartmentId, row.TeamId,
                settings, EmployeeAccessSource.Configured));
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

}
