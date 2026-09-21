using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Modules.Procurement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class AccessV1Ap05HardeningTests
{
    [TestMethod]
    public async Task HandoverAcrossDepartmentsKeepsAssignedWorkReadableWithoutBroadeningDepartmentRead()
    {
        await using ProcurementTests.Phase1Fixture fixture =
            await ProcurementTests.Phase1Fixture.CreateAsync(includeSecondManager: true, includeTeams: true);

        Guid secondManagerId = fixture.EmployeeId("manager2-phase1@test.invalid");
        await fixture.SetExplicitAccessAsync(secondManagerId,
            Access(ProcurementAccessLevel.Manager, AccessScope.Department, AccessScope.AssignedObjects));

        ManualPropertyCaseResult transferred = await fixture.Workspace.CreateManualCaseAsync(
            fixture.Manager,
            new("AP-05 handover", "Химки", null, 4_000_000m, 1_200m,
                "Объект для межотдельской передачи ответственности"),
            "ap05-handover-case", CancellationToken.None);
        ManualPropertyCaseResult unrelated = await fixture.Workspace.CreateManualCaseAsync(
            fixture.Head,
            new("AP-05 unrelated", "Химки", null, 5_000_000m, 1_500m,
                "Контрольный объект другого сотрудника отдела А"),
            "ap05-unrelated-case", CancellationToken.None);

        EmployeeWorkImpact impact = await fixture.Organization.ReadEmployeeWorkImpactAsync(
            fixture.Owner, fixture.ManagerEmployeeId, CancellationToken.None);
        Assert.IsTrue(impact.Candidates.Any(item => item.EmployeeId == secondManagerId),
            "AssignedObjects WorkScope must remain a valid handover target across departments.");

        await fixture.Organization.TransferEmployeeWorkAsync(
            fixture.Owner, new(fixture.ManagerEmployeeId, secondManagerId),
            "ap05-handover", CancellationToken.None);

        ProcurementQueuePage queue = await fixture.Workspace.ReadQueueAsync(
            fixture.SecondManager, new(), CancellationToken.None);
        Assert.IsTrue(queue.Items.Any(item => item.CaseId == transferred.CaseId),
            "A case the employee can work must remain visible after cross-department handover.");
        Assert.IsFalse(queue.Items.Any(item => item.CaseId == unrelated.CaseId),
            "Work-implied read must not broaden Department read to unrelated cases.");

        CaseCard card = await fixture.Workspace.ReadCardAsync(
            fixture.SecondManager, transferred.CaseId, CancellationToken.None);
        await fixture.Workspace.AddNoteAsync(
            fixture.SecondManager,
            new(transferred.CaseId, card.Item.CaseVersion,
                "AP-05: назначенный объект доступен и для чтения, и для работы.", false, "", null),
            "ap05-assigned-note", CancellationToken.None);

        ProcurementQueueV2ReadService queueV2 =
            new(fixture.Factory, fixture.Access, TimeProvider.System);
        ProcurementQueueV2Page v2 = await queueV2.ReadPageAsync(
            fixture.SecondManager, new(), CancellationToken.None);
        Assert.IsTrue(v2.Items.Any(item => item.CaseId == transferred.CaseId));
        Assert.IsFalse(v2.Items.Any(item => item.CaseId == unrelated.CaseId));
    }

    [TestMethod]
    public async Task ForwardAcrossDepartmentsKeepsAssignedHeadCaseReadableAndReturnable()
    {
        await using ProcurementTests.Phase1Fixture fixture =
            await ProcurementTests.Phase1Fixture.CreateAsync(includeSecondManager: true, includeTeams: true);

        Guid secondManagerId = fixture.EmployeeId("manager2-phase1@test.invalid");
        await fixture.SetExplicitAccessAsync(secondManagerId,
            Access(ProcurementAccessLevel.Head, AccessScope.Department, AccessScope.AssignedObjects));

        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(
            fixture.Manager,
            new("AP-05 forward", "Химки", null, 4_500_000m, 1_300m,
                "Объект для межотдельского согласования"),
            "ap05-forward-case", CancellationToken.None);
        CaseCard managerCard = await fixture.Workspace.ReadCardAsync(
            fixture.Manager, created.CaseId, CancellationToken.None);

        await fixture.Workspace.DecideAsync(
            fixture.Manager,
            new(created.CaseId, managerCard.Item.CaseVersion, managerCard.Item.SourceRevision,
                ProcurementAction.Forward, "Передать руководителю другого отдела", "",
                secondManagerId, null),
            "ap05-forward", CancellationToken.None);

        CaseCard forwarded = await fixture.Workspace.ReadCardAsync(
            fixture.SecondManager, created.CaseId, CancellationToken.None);
        Assert.IsTrue(forwarded.CanHeadDecide,
            "The assigned Head must be able to read and decide the forwarded case.");

        await fixture.Workspace.DecideAsync(
            fixture.SecondManager,
            new(created.CaseId, forwarded.Item.CaseVersion, forwarded.Item.SourceRevision,
                ProcurementAction.Return, "Вернуть менеджеру после проверки",
                "Нужно уточнить условия подъезда", fixture.ManagerEmployeeId, null),
            "ap05-return", CancellationToken.None);

        CaseCard returned = await fixture.Workspace.ReadCardAsync(
            fixture.Manager, created.CaseId, CancellationToken.None);
        Assert.AreEqual("returned", returned.Item.Stage);
    }

    private static EmployeeAccessConfiguration Access(
        ProcurementAccessLevel procurement,
        AccessScope readScope,
        AccessScope workScope) =>
        new(IncomingAccessLevel.None, procurement, readScope, workScope,
            CollectionAccessLevel.None,
            CanAssignInspections: false,
            CanPerformInspections: false,
            CanConfirmPurchase: false,
            CanManageTemplates: false,
            CanReadAudit: false);
}
