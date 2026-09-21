using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Domain;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Persistence;

namespace LandErp.Infrastructure.Modules.Procurement;

internal enum ProcurementRecipientAccess
{
    CurrentVisibility,
    BecomesCaseAssignee,
    BecomesManager,
    BecomesManagerAndCaseAssignee
}

internal static class ProcurementVisibility
{
    public static IQueryable<PropertyCase> Apply(IQueryable<PropertyCase> cases, LandErpDbContext db, AccessContext context)
    {
        IQueryable<PropertyCase> organizationCases = cases.Where(item => item.OrganizationId == context.OrganizationId);
        return context.Scope switch
        {
            AccessScope.Organization => organizationCases,
            AccessScope.Department => organizationCases.Where(item => context.DepartmentId != null && item.DepartmentId == context.DepartmentId),
            AccessScope.Team => organizationCases.Where(item => context.TeamId != null && item.TeamId == context.TeamId),
            AccessScope.AssignedObjects => organizationCases.Where(item => db.WorkAssignments.Any(assignment => assignment.Id == item.AssignmentId
                && assignment.EmployeeId == context.EmployeeId)),
            _ => organizationCases.Where(item => item.ManagerEmployeeId == context.EmployeeId)
        };
    }

    /// <summary>
    /// Procurement work always implies read for the same concrete case. This matters for
    /// responsibility-based Own/AssignedObjects scopes, which are not strict subsets of
    /// Team/Department when work is handed over across organization boundaries.
    /// </summary>
    public static IQueryable<PropertyCase> ApplyRead(
        IQueryable<PropertyCase> cases, LandErpDbContext db, EffectiveEmployeeAccess access)
    {
        IQueryable<PropertyCase> readable = Apply(cases, db, access.ProcurementReadContext);
        if (!access.CanManageProcurement
            || access.Settings.ProcurementWorkScope == access.Settings.ProcurementReadScope)
            return readable;

        IQueryable<PropertyCase> workable = Apply(cases, db, access.ProcurementWorkContext);
        return readable.Union(workable);
    }

    public static bool CanSeeAfterResponsibility(PropertyCase propertyCase, EmployeeAssignment assignment,
        Guid managerEmployeeId, Guid caseAssigneeId) => assignment.Scope switch
    {
        AccessScope.Organization => true,
        AccessScope.Department => propertyCase.DepartmentId != null && assignment.OrgUnitId == propertyCase.DepartmentId,
        AccessScope.Team => propertyCase.TeamId != null && assignment.TeamId == propertyCase.TeamId,
        AccessScope.AssignedObjects => assignment.EmployeeId == caseAssigneeId,
        _ => assignment.EmployeeId == managerEmployeeId
    };

    public static bool CanSeeAfterResponsibility(PropertyCase propertyCase, AccessContext context,
        Guid managerEmployeeId, Guid caseAssigneeId) => context.Scope switch
    {
        AccessScope.Organization => propertyCase.OrganizationId == context.OrganizationId,
        AccessScope.Department => propertyCase.OrganizationId == context.OrganizationId
            && propertyCase.DepartmentId != null && context.DepartmentId == propertyCase.DepartmentId,
        AccessScope.Team => propertyCase.OrganizationId == context.OrganizationId
            && propertyCase.TeamId != null && context.TeamId == propertyCase.TeamId,
        AccessScope.AssignedObjects => propertyCase.OrganizationId == context.OrganizationId
            && context.EmployeeId == caseAssigneeId,
        _ => propertyCase.OrganizationId == context.OrganizationId && context.EmployeeId == managerEmployeeId
    };

    public static bool CanReceiveNewCase(AccessContext context) => context.Scope switch
    {
        AccessScope.Department => context.DepartmentId != null,
        AccessScope.Team => context.TeamId != null,
        _ => true
    };

    public static IQueryable<EmployeeAssignment> EligibleRecipientAssignments(
        LandErpDbContext db,
        PropertyCase propertyCase,
        Guid currentCaseAssigneeId,
        ProcurementRecipientAccess access)
    {
        IQueryable<EmployeeAssignment> assignments =
            from assignment in db.EmployeeAssignments
            join employee in db.Employees on assignment.EmployeeId equals employee.Id
            where employee.OrganizationId == propertyCase.OrganizationId && employee.Active
            select assignment;

        return assignments.Where(assignment =>
            assignment.Scope == AccessScope.Organization
            || assignment.Scope == AccessScope.Department
                && propertyCase.DepartmentId != null && assignment.OrgUnitId == propertyCase.DepartmentId
            || assignment.Scope == AccessScope.Team
                && propertyCase.TeamId != null && assignment.TeamId == propertyCase.TeamId
            || assignment.Scope == AccessScope.Own
                && (assignment.EmployeeId == propertyCase.ManagerEmployeeId
                    || access == ProcurementRecipientAccess.BecomesManager
                    || access == ProcurementRecipientAccess.BecomesManagerAndCaseAssignee)
            || assignment.Scope == AccessScope.AssignedObjects
                && (assignment.EmployeeId == currentCaseAssigneeId
                    || access == ProcurementRecipientAccess.BecomesCaseAssignee
                    || access == ProcurementRecipientAccess.BecomesManagerAndCaseAssignee));
    }
}
