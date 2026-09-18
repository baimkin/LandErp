using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Domain;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Persistence;

namespace LandErp.Infrastructure.Modules.Procurement;

internal enum ProcurementRecipientAccess
{
    CurrentVisibility,
    BecomesCaseAssignee,
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
                    || access == ProcurementRecipientAccess.BecomesManagerAndCaseAssignee)
            || assignment.Scope == AccessScope.AssignedObjects
                && (assignment.EmployeeId == currentCaseAssigneeId
                    || access == ProcurementRecipientAccess.BecomesCaseAssignee
                    || access == ProcurementRecipientAccess.BecomesManagerAndCaseAssignee));
    }
}
