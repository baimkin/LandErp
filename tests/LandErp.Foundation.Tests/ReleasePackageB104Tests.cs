using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ReleasePackageB104Tests
{
    [TestMethod]
    public async Task DeactivationRequiresExplicitHandoverAndTransfersAllResponsibilities()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("B1-04 handover case", "Химки", null, 4_200_000m, 900m, "Synthetic B1-04 handover"),
            "b1-04-create", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        await fixture.Workspace.SaveCheckAsync(fixture.Manager,
            new(created.CaseId, null, card.Item.CaseVersion, null, CaseCheckLevel.Quick,
                "Открытая проверка B1-04", CaseCheckStatus.InProgress, fixture.ManagerEmployeeId, true,
                null, null, "Проверка в работе", false),
            "b1-04-check", CancellationToken.None);

        OrganizationView organization = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        EmployeeView source = organization.Employees.Single(item => item.Login == "manager-phase1@test.invalid");
        EmployeeView recipient = organization.Employees.Single(item => item.Login == "manager2-phase1@test.invalid");
        EmployeeWorkImpact impact = await fixture.Organization.ReadEmployeeWorkImpactAsync(
            fixture.Owner, source.Id, CancellationToken.None);
        Assert.AreEqual(1, impact.AffectedCases);
        Assert.AreEqual(1, impact.ManagedCases);
        Assert.AreEqual(1, impact.AssignedCases);
        Assert.AreEqual(1, impact.OpenTasks);
        Assert.AreEqual(1, impact.OpenChecks);
        Assert.AreEqual(0, impact.PendingApprovals);
        Assert.IsTrue(impact.Candidates.Any(item => item.EmployeeId == recipient.Id));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Organization.SetEmployeeActiveAsync(
            fixture.Owner, new(source.Id, source.EmployeeVersion, false), "b1-04-no-handover", CancellationToken.None));

        await fixture.Organization.SetEmployeeActiveAsync(fixture.Owner,
            new(source.Id, source.EmployeeVersion, false, recipient.Id, false),
            "b1-04-deactivate-handover", CancellationToken.None);

        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None));
        CaseCard recipientCard = await fixture.Workspace.ReadCardAsync(fixture.SecondManager, created.CaseId, CancellationToken.None);
        Assert.AreEqual(recipient.Id, recipientCard.ManagerEmployeeId);

        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        PropertyCase propertyCase = await db.PropertyCases.AsNoTracking().SingleAsync(item => item.Id == created.CaseId);
        Assignment assignment = await db.WorkAssignments.AsNoTracking().SingleAsync(item => item.Id == propertyCase.AssignmentId);
        WorkTask task = await db.WorkTasks.AsNoTracking().SingleAsync(item => item.Id == propertyCase.WorkTaskId);
        CaseCheck check = await db.CaseChecks.AsNoTracking().SingleAsync(item => item.PropertyCaseId == created.CaseId);
        Assert.AreEqual(recipient.Id, propertyCase.ManagerEmployeeId);
        Assert.AreEqual(recipient.Id, assignment.EmployeeId);
        Assert.AreEqual(recipient.Id, task.EmployeeId);
        Assert.AreEqual(recipient.Id, check.ResponsibleEmployeeId);
        Assert.AreEqual(source.Id, check.AuthorEmployeeId, "Handover must not rewrite historical authors.");
        Assert.IsTrue(await db.BusinessTimeline.AnyAsync(item => item.ObjectId == created.CaseId
            && item.Kind == "ResponsibilityTransferred" && item.TargetEmployeeId == recipient.Id));
        Assert.IsTrue(await db.AuditEvents.AnyAsync(item => item.ObjectId == source.Id
            && item.Action == "EmployeeWorkTransferred"));
        Assert.IsTrue(await db.AuditEvents.AnyAsync(item => item.ObjectId == source.Id
            && item.Action == "EmployeeDeactivated"));
    }

    [TestMethod]
    public async Task PendingApprovalHandoverMovesHeadResponsibilityWithoutChangingManager()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        OrganizationView structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        Guid newHeadUser = await ProcurementTestsHelper.InviteAsync(fixture.Services, fixture.Organization, fixture.Owner,
            structure, "Replacement Head", "replacement-head-b104@test.invalid", "ProcurementHead",
            fixture.DepartmentA, AccessScope.Department);
        structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        EmployeeView oldHead = structure.Employees.Single(item => item.Login == "head-phase1@test.invalid");
        EmployeeView newHead = structure.Employees.Single(item => item.Login == "replacement-head-b104@test.invalid");

        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("B1-04 approval case", "Химки", null, 5_000_000m, 1_000m, "Pending head handover"),
            "b1-04-approval-create", CancellationToken.None);
        CaseCard managerCard = await fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        await fixture.Workspace.DecideAsync(fixture.Manager,
            new(created.CaseId, managerCard.Item.CaseVersion, managerCard.Item.SourceRevision,
                ProcurementAction.Forward, "Передать руководителю", "", oldHead.Id, null),
            "b1-04-forward", CancellationToken.None);

        EmployeeWorkImpact impact = await fixture.Organization.ReadEmployeeWorkImpactAsync(
            fixture.Owner, oldHead.Id, CancellationToken.None);
        Assert.AreEqual(1, impact.AssignedCases);
        Assert.AreEqual(1, impact.OpenTasks);
        Assert.AreEqual(1, impact.PendingApprovals);
        Assert.IsTrue(impact.Candidates.Any(item => item.EmployeeId == newHead.Id));

        await fixture.Organization.SetEmployeeActiveAsync(fixture.Owner,
            new(oldHead.Id, oldHead.EmployeeVersion, false, newHead.Id, false),
            "b1-04-head-handover", CancellationToken.None);

        Subject replacementHead = new(newHeadUser, false);
        CaseCard headCard = await fixture.Workspace.ReadCardAsync(replacementHead, created.CaseId, CancellationToken.None);
        Assert.IsTrue(headCard.CanHeadDecide);
        Assert.AreEqual(fixture.ManagerEmployeeId, headCard.ManagerEmployeeId);

        await fixture.Workspace.DecideAsync(replacementHead,
            new(created.CaseId, headCard.Item.CaseVersion, headCard.Item.SourceRevision,
                ProcurementAction.Return, "Вернуть менеджеру", "Уточнить документы", fixture.ManagerEmployeeId, null),
            "b1-04-return-after-handover", CancellationToken.None);
        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        Assert.AreEqual(1, await db.Approvals.CountAsync(item => item.ObjectId == created.CaseId));
        Assert.IsNull((await db.PropertyCases.AsNoTracking().SingleAsync(item => item.Id == created.CaseId)).PendingApprovalId);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.ReadCardAsync(fixture.Head, created.CaseId, CancellationToken.None));
    }

    [TestMethod]
    public async Task EmergencyRevokeRemovesAccessAndAllowsLaterExplicitHandover()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("B1-04 emergency case", "Химки", null, 3_800_000m, 800m, "Emergency revoke"),
            "b1-04-emergency-create", CancellationToken.None);
        OrganizationView organization = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        EmployeeView source = organization.Employees.Single(item => item.Login == "manager-phase1@test.invalid");
        EmployeeView recipient = organization.Employees.Single(item => item.Login == "manager2-phase1@test.invalid");

        await fixture.Organization.SetEmployeeActiveAsync(fixture.Owner,
            new(source.Id, source.EmployeeVersion, false, null, true),
            "b1-04-emergency-revoke", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None));

        EmployeeWorkImpact pending = await fixture.Organization.ReadEmployeeWorkImpactAsync(
            fixture.Owner, source.Id, CancellationToken.None);
        Assert.IsTrue(pending.HasWork);
        Assert.IsTrue(pending.Candidates.Any(item => item.EmployeeId == recipient.Id));

        await fixture.Organization.TransferEmployeeWorkAsync(fixture.Owner,
            new(source.Id, recipient.Id), "b1-04-late-handover", CancellationToken.None);
        EmployeeWorkImpact after = await fixture.Organization.ReadEmployeeWorkImpactAsync(
            fixture.Owner, source.Id, CancellationToken.None);
        Assert.IsFalse(after.HasWork);
        Assert.AreEqual(created.CaseId,
            (await fixture.Workspace.ReadCardAsync(fixture.SecondManager, created.CaseId, CancellationToken.None)).Item.CaseId);

        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        Assert.IsTrue(await db.BusinessTimeline.AnyAsync(item => item.ObjectId == created.CaseId
            && item.Kind == "ResponsibilityHandoverPending"));
        Assert.IsTrue(await db.BusinessTimeline.AnyAsync(item => item.ObjectId == created.CaseId
            && item.Kind == "ResponsibilityTransferred"));
        Assert.IsTrue(await db.AuditEvents.AnyAsync(item => item.ObjectId == source.Id
            && item.Action == "EmployeeWorkHandoverPending"));
    }

    [TestMethod]
    public async Task ConcurrentNewAssignmentAndDeactivationNeverLeaveWorkOnDisabledEmployee()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        OrganizationView structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        _ = await ProcurementTestsHelper.InviteAsync(fixture.Services, fixture.Organization, fixture.Owner,
            structure, "Third Manager", "manager3-b104@test.invalid", "ProcurementManager",
            fixture.DepartmentA, AccessScope.Department);
        structure = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        EmployeeView target = structure.Employees.Single(item => item.Login == "manager2-phase1@test.invalid");
        EmployeeView replacement = structure.Employees.Single(item => item.Login == "manager3-b104@test.invalid");

        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("B1-04 race case", "Химки", null, 4_100_000m, 850m, "Race test"),
            "b1-04-race-create", CancellationToken.None);
        CaseCard card = await fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);

        Task<bool> assign = TryAssignmentAsync(() => fixture.Workspace.SaveNextActionAsync(fixture.Manager,
            new(created.CaseId, card.Item.CaseVersion, card.Item.NextActionVersion, WorkTaskType.Call,
                "Позвонить продавцу", "Конкурентное назначение", null, target.Id),
            "b1-04-race-assign", CancellationToken.None));
        Task deactivate = fixture.Organization.SetEmployeeActiveAsync(fixture.Owner,
            new(target.Id, target.EmployeeVersion, false, replacement.Id, false),
            "b1-04-race-disable", CancellationToken.None);
        await Task.WhenAll(assign, deactivate);

        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        Assert.IsFalse((await db.Employees.AsNoTracking().SingleAsync(item => item.Id == target.Id)).Active);
        Assert.AreEqual(0, await ActiveResponsibilityCountAsync(db, fixture.OrganizationId, target.Id));
        WorkTask task = await db.WorkTasks.AsNoTracking().SingleAsync(item => item.ObjectId == created.CaseId);
        Assert.AreNotEqual(target.Id, task.EmployeeId);
        if (await assign) Assert.AreEqual(replacement.Id, task.EmployeeId);
    }

    private static async Task<bool> TryAssignmentAsync(Func<Task> action)
    {
        try { await action(); return true; }
        catch (AccessDeniedException) { return false; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    private static async Task<int> ActiveResponsibilityCountAsync(LandErpDbContext db, Guid organizationId, Guid employeeId)
    {
        Guid[] activeCaseIds = await db.PropertyCases.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.StageId != "acquired" && item.StageId != "rejected")
            .Select(item => item.Id).ToArrayAsync();
        int managed = await db.PropertyCases.AsNoTracking().CountAsync(item => activeCaseIds.Contains(item.Id)
            && item.ManagerEmployeeId == employeeId);
        int assigned = await (from propertyCase in db.PropertyCases.AsNoTracking()
                              join assignment in db.WorkAssignments.AsNoTracking() on propertyCase.AssignmentId equals assignment.Id
                              where activeCaseIds.Contains(propertyCase.Id) && assignment.EmployeeId == employeeId
                              select propertyCase.Id).CountAsync();
        int tasks = await db.WorkTasks.AsNoTracking().CountAsync(item => activeCaseIds.Contains(item.ObjectId)
            && !item.Completed && item.EmployeeId == employeeId);
        int checks = await db.CaseChecks.AsNoTracking().CountAsync(item => activeCaseIds.Contains(item.PropertyCaseId)
            && item.ResponsibleEmployeeId == employeeId
            && item.Status != CaseCheckStatus.Passed && item.Status != CaseCheckStatus.Issue);
        return managed + assigned + tasks + checks;
    }
}
