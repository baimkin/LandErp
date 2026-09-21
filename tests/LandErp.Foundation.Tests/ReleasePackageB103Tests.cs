using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ReleasePackageB103Tests
{
    [TestMethod]
    public async Task RecipientListsMatchPostAssignmentVisibilityAndStaleTargetsAreRejected()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, true);
        OrganizationView structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        Guid headAssignedUser = await ProcurementTestsHelper.InviteAsync(fixture.Services, fixture.Organization, fixture.Owner,
            structure, "Head Assigned", "head-assigned-b103@test.invalid", "ProcurementHead", fixture.DepartmentA, AccessScope.AssignedObjects);
        _ = await ProcurementTestsHelper.InviteAsync(fixture.Services, fixture.Organization, fixture.Owner,
            structure, "Head Own", "head-own-b103@test.invalid", "ProcurementHead", fixture.DepartmentA, AccessScope.Own);
        Guid managerOwnUser = await ProcurementTestsHelper.InviteAsync(fixture.Services, fixture.Organization, fixture.Owner,
            structure, "Manager Own", "manager-own-b103@test.invalid", "ProcurementManager", fixture.DepartmentA, AccessScope.Own);
        Guid managerAssignedUser = await ProcurementTestsHelper.InviteAsync(fixture.Services, fixture.Organization, fixture.Owner,
            structure, "Manager Assigned", "manager-assigned-b103@test.invalid", "ProcurementManager", fixture.DepartmentA, AccessScope.AssignedObjects);

        structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        Guid headAssignedEmployee = structure.Employees.Single(item => item.Login == "head-assigned-b103@test.invalid").Id;
        Guid headOwnEmployee = structure.Employees.Single(item => item.Login == "head-own-b103@test.invalid").Id;
        Guid managerOwnEmployee = structure.Employees.Single(item => item.Login == "manager-own-b103@test.invalid").Id;
        Guid managerAssignedEmployee = structure.Employees.Single(item => item.Login == "manager-assigned-b103@test.invalid").Id;
        Subject headAssigned = new(headAssignedUser, false);
        Subject managerOwn = new(managerOwnUser, false);
        Subject managerAssigned = new(managerAssignedUser, false);

        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("B1-03 recipient visibility", "Химки", null, 4_000_000m, 900m, "Synthetic recipient test"),
            "b1-03-case", CancellationToken.None);
        CaseCard managerCard = await fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);

        Assert.IsTrue(managerCard.Heads.Any(item => item.EmployeeId == headAssignedEmployee));
        Assert.IsFalse(managerCard.Heads.Any(item => item.EmployeeId == headOwnEmployee));
        Assert.IsFalse(managerCard.Assignees.Any(item => item.EmployeeId == headAssignedEmployee));
        Assert.IsFalse(managerCard.Assignees.Any(item => item.EmployeeId == managerAssignedEmployee));

        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.DecideAsync(fixture.Manager,
            new(created.CaseId, managerCard.Item.CaseVersion, managerCard.Item.SourceRevision,
                ProcurementAction.Forward, "Устаревшая форма", "", headOwnEmployee, null),
            "b1-03-stale-forward", CancellationToken.None));

        await fixture.Workspace.DecideAsync(fixture.Manager,
            new(created.CaseId, managerCard.Item.CaseVersion, managerCard.Item.SourceRevision,
                ProcurementAction.Forward, "Передать допустимому руководителю", "", headAssignedEmployee, null),
            "b1-03-forward", CancellationToken.None);
        CaseCard headCard = await fixture.Workspace.ReadCardAsync(headAssigned, created.CaseId, CancellationToken.None);
        Assert.IsTrue(headCard.CanHeadDecide);
        Assert.IsTrue(headCard.Managers.Any(item => item.EmployeeId == managerOwnEmployee));

        await fixture.Workspace.DecideAsync(headAssigned,
            new(created.CaseId, headCard.Item.CaseVersion, headCard.Item.SourceRevision,
                ProcurementAction.Return, "Вернуть новому менеджеру", "Проверить документы", managerOwnEmployee, null),
            "b1-03-return", CancellationToken.None);
        CaseCard ownCard = await fixture.Workspace.ReadCardAsync(managerOwn, created.CaseId, CancellationToken.None);
        Assert.AreEqual(managerOwnEmployee, ownCard.ManagerEmployeeId);
        Assert.IsTrue(ownCard.Assignees.Any(item => item.EmployeeId == managerOwnEmployee));
        Assert.IsFalse(ownCard.Assignees.Any(item => item.EmployeeId == managerAssignedEmployee));

        ProcurementQueueV2ReadService read = new(fixture.Factory, TimeProvider.System);
        ProcurementQueueV2Detail detail = await read.ReadDetailAsync(managerOwn, created.CaseId, CancellationToken.None);
        Assert.IsFalse(detail.AvailableAssignees.Any(item => item.Id == managerAssignedEmployee));
        Assert.IsTrue(detail.AvailableAssignees.Any(item => item.Id == managerOwnEmployee));

        ManualPropertyCaseResult unrelated = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("B1-03 unrelated case", "Истра", null, 5_000_000m, 1_000m, "Unrelated"),
            "b1-03-unrelated", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.ReadCardAsync(managerOwn, unrelated.CaseId, CancellationToken.None));

        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.SaveNextActionAsync(managerOwn,
            new(created.CaseId, ownCard.Item.CaseVersion, ownCard.Item.NextActionVersion, WorkTaskType.Call,
                "Недоступный исполнитель", "Stale payload must be rejected", null, managerAssignedEmployee),
            "b1-03-invalid-task-recipient", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.SaveCheckAsync(managerOwn,
            new(created.CaseId, null, ownCard.Item.CaseVersion, null, CaseCheckLevel.Quick,
                "Недоступный ответственный", CaseCheckStatus.Planned, managerAssignedEmployee, true,
                null, null, "", false),
            "b1-03-invalid-check-recipient", CancellationToken.None));

        Guid organizationHead = fixture.EmployeeId("head-phase1@test.invalid");
        await fixture.Workspace.SaveNextActionAsync(managerOwn,
            new(created.CaseId, ownCard.Item.CaseVersion, ownCard.Item.NextActionVersion, WorkTaskType.Call,
                "Допустимый исполнитель", "Organization-scope recipient can open the case", null, organizationHead),
            "b1-03-valid-task-recipient", CancellationToken.None);
        Assert.AreEqual(created.CaseId,
            (await fixture.Workspace.ReadCardAsync(fixture.Head, created.CaseId, CancellationToken.None)).Item.CaseId);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.ReadCardAsync(managerAssigned, created.CaseId, CancellationToken.None));
    }

    [TestMethod]
    public async Task ConcurrentOwnerDisableAndRoleRemovalAlwaysLeaveAnActiveOwner()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        OrganizationView structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        _ = await ProcurementTestsHelper.InviteAsync(fixture.Services, fixture.Organization, fixture.Owner,
            structure, "Second Owner", "owner2-b103@test.invalid", "Owner", fixture.DepartmentA, AccessScope.Organization);
        Guid adminUser = await ProcurementTestsHelper.InviteAsync(fixture.Services, fixture.Organization, fixture.Owner,
            structure, "B1-03 Admin", "admin-b103@test.invalid", "Administrator", fixture.DepartmentA, AccessScope.Organization);
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            var admin = await db.Users.SingleAsync(item => item.Id == adminUser);
            admin.TwoFactorEnabled = true;
            await db.SaveChangesAsync();
        }
        Subject adminActor = new(adminUser, true);

        structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        EmployeeView[] owners = structure.Employees.Where(item => item.Role == "Owner").ToArray();
        Assert.AreEqual(2, owners.Length);
        bool[] disables = await Task.WhenAll(owners.Select(owner =>
            TryInvariantChangeAsync(() => fixture.Organization.SetEmployeeActiveAsync(adminActor,
                new(owner.Id, owner.EmployeeVersion, false), "b1-03-concurrent-disable", CancellationToken.None))));
        Assert.AreEqual(1, disables.Count(value => value));
        Assert.AreEqual(1, await ActiveOwnerCountAsync(fixture));

        structure = await fixture.Organization.ReadAsync(adminActor, CancellationToken.None);
        EmployeeView disabled = structure.Employees.Single(item => item.Role == "Owner" && item.State == EmployeeState.Disabled);
        await fixture.Organization.SetEmployeeActiveAsync(adminActor,
            new(disabled.Id, disabled.EmployeeVersion, true), "b1-03-restore-owner", CancellationToken.None);
        Assert.AreEqual(2, await ActiveOwnerCountAsync(fixture));

        structure = await fixture.Organization.ReadAsync(adminActor, CancellationToken.None);
        owners = structure.Employees.Where(item => item.Role == "Owner" && item.State == EmployeeState.Active).ToArray();
        EmployeeView demoteTarget = owners.FirstOrDefault(item => item.Login == "owner2-b103@test.invalid") ?? owners[0];
        EmployeeView disableTarget = owners.Single(item => item.Id != demoteTarget.Id);
        Guid managerRole = structure.Roles.Single(item => item.Name == "ProcurementManager").Id;

        Task<bool> disable = TryInvariantChangeAsync(() => fixture.Organization.SetEmployeeActiveAsync(adminActor,
            new(disableTarget.Id, disableTarget.EmployeeVersion, false), "b1-03-mixed-disable", CancellationToken.None));
        Task<bool> demote = TryInvariantChangeAsync(() => fixture.Organization.ChangeAssignmentAsync(adminActor,
            new(demoteTarget.Id, demoteTarget.DepartmentId, demoteTarget.PositionId, demoteTarget.TeamId,
                demoteTarget.ManagerId, managerRole, AccessScope.Organization, demoteTarget.Version),
            "b1-03-mixed-role", CancellationToken.None));
        bool[] mixed = await Task.WhenAll(disable, demote);
        Assert.AreEqual(1, mixed.Count(value => value));
        Assert.AreEqual(1, await ActiveOwnerCountAsync(fixture));
    }

    [TestMethod]
    public async Task SharedTemplatePolicyIsSeparatedButEffectiveRightsRemainUnchanged()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("B1-03 template policy", "Химки", null, 4_000_000m, 900m, "Template decision seam"),
            "b1-03-template-case", CancellationToken.None);
        CaseCard managerCard = await fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        CaseCard headCard = await fixture.Workspace.ReadCardAsync(fixture.Head, created.CaseId, CancellationToken.None);
        Assert.IsTrue(managerCard.CanManageDossier);
        Assert.IsTrue(managerCard.CanManageTemplates);
        Assert.IsTrue(headCard.CanManageDossier);
        Assert.IsTrue(headCard.CanManageTemplates);

        await fixture.Workspace.SaveCheckTemplateAsync(fixture.Manager,
            new(null, null, "B1-03 текущая политика", CaseCheckLevel.Quick,
                "До отдельного продуктового решения Manager/Head сохраняют прежние права.", 999, true),
            "b1-03-template-manager", CancellationToken.None);
        CaseCard refreshed = await fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        Assert.IsTrue(refreshed.CheckTemplates.Any(item => item.Title == "B1-03 текущая политика"));
    }

    private static async Task<bool> TryInvariantChangeAsync(Func<Task> action)
    {
        try { await action(); return true; }
        catch (ArgumentException) { return false; }
    }

    private static async Task<int> ActiveOwnerCountAsync(ProcurementTests.Phase1Fixture fixture)
    {
        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        return await (from employee in db.Employees
                      join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
                      join role in db.Roles on assignment.RoleId equals role.Id
                      where employee.OrganizationId == fixture.OrganizationId && employee.Active && role.Name == "Owner"
                      select employee.Id).CountAsync();
    }
}
