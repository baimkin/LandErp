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
public sealed class ReleasePackageB102Tests
{
    [TestMethod]
    public async Task AcquiredCaseRejectsOrdinaryDecisionAndNextActionWithoutChangingTerminalState()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("B1-02 terminal case", "Химки", null, 4_000_000m, 900m, "Synthetic B1-02 case"),
            "b1-02-create", CancellationToken.None);
        CaseCard headBefore = await fixture.Workspace.ReadCardAsync(fixture.Head, created.CaseId, CancellationToken.None);
        DateOnly acquiredDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        await fixture.Workspace.MarkAcquiredAsync(fixture.Head,
            new(created.CaseId, headBefore.Item.CaseVersion, 3_900_000m, acquiredDate, "Покупка подтверждена"),
            "b1-02-acquire", CancellationToken.None);

        CaseCard managerCard = await fixture.Workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        Assert.AreEqual("acquired", managerCard.Item.Stage);
        Assert.IsFalse(managerCard.CanManagerDecide);
        ProcurementQueueV2ReadService read = new(fixture.Factory, TimeProvider.System);
        ProcurementQueueV2Detail detail = await read.ReadDetailAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        Assert.IsFalse(detail.CanManagerDecide);

        long taskVersion;
        string taskTitle;
        int transitions;
        int timeline;
        int audits;
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            var task = await db.WorkTasks.AsNoTracking().SingleAsync(item => item.ObjectId == created.CaseId);
            Assert.IsTrue(task.Completed);
            taskVersion = task.Version;
            taskTitle = task.Title;
            transitions = await db.WorkflowTransitions.CountAsync(item => item.ObjectId == created.CaseId);
            timeline = await db.BusinessTimeline.CountAsync(item => item.ObjectId == created.CaseId);
            audits = await db.AuditEvents.CountAsync(item => item.ObjectId == created.CaseId);
        }

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.DecideAsync(fixture.Manager,
            new(created.CaseId, managerCard.Item.CaseVersion, managerCard.Item.SourceRevision,
                ProcurementAction.Monitor, "Обычное решение после покупки", "", null, null),
            "b1-02-decision-after-acquired", CancellationToken.None));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.SaveNextActionAsync(fixture.Manager,
            new(created.CaseId, managerCard.Item.CaseVersion, taskVersion, WorkTaskType.Call,
                "Позвонить после покупки", "Этот шаг не должен открыться", null, fixture.ManagerEmployeeId),
            "b1-02-next-after-acquired", CancellationToken.None));

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            PropertyCase propertyCase = await db.PropertyCases.AsNoTracking().SingleAsync(item => item.Id == created.CaseId);
            var task = await db.WorkTasks.AsNoTracking().SingleAsync(item => item.ObjectId == created.CaseId);
            Assert.AreEqual("acquired", propertyCase.StageId);
            Assert.IsTrue(task.Completed);
            Assert.AreEqual(taskVersion, task.Version);
            Assert.AreEqual(taskTitle, task.Title);
            Assert.AreEqual(transitions, await db.WorkflowTransitions.CountAsync(item => item.ObjectId == created.CaseId));
            Assert.AreEqual(timeline, await db.BusinessTimeline.CountAsync(item => item.ObjectId == created.CaseId));
            Assert.AreEqual(audits, await db.AuditEvents.CountAsync(item => item.ObjectId == created.CaseId));
        }

        CaseCard correctionCard = await fixture.Workspace.ReadCardAsync(fixture.Head, created.CaseId, CancellationToken.None);
        await fixture.Workspace.CorrectAcquisitionAsync(fixture.Head,
            new(created.CaseId, correctionCard.Item.CaseVersion, 3_850_000m, acquiredDate,
                "Уточнено по договору", "Исправлена фактическая цена"),
            "b1-02-correct-acquisition", CancellationToken.None);
        CaseCard corrected = await fixture.Workspace.ReadCardAsync(fixture.Head, created.CaseId, CancellationToken.None);
        Assert.AreEqual("acquired", corrected.Item.Stage);
        Assert.AreEqual(3_850_000m, corrected.AcquisitionPrice);
    }

    [TestMethod]
    public async Task ExpiredCheckKeepsOriginalDeadlineWhenClosedButRejectsNewPastDeadline()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager,
            new("B1-02 overdue check", "Химки", null, 4_000_000m, 900m, "Synthetic overdue check"),
            "b1-02-check-case", CancellationToken.None);
        DateTimeOffset t0 = new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset originalDue = t0.AddHours(1);
        ProcurementWorkspace beforeDue = NewWorkspace(fixture, t0);
        CaseCard card = await beforeDue.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        await beforeDue.SaveCheckAsync(fixture.Manager,
            new(created.CaseId, null, card.Item.CaseVersion, null, CaseCheckLevel.Quick,
                "Проверка с дедлайном", CaseCheckStatus.InProgress, fixture.ManagerEmployeeId, true,
                originalDue, null, "Проверка начата", false),
            "b1-02-check-create", CancellationToken.None);

        card = await beforeDue.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        CheckView check = card.Checks.Single();
        ProcurementWorkspace afterDue = NewWorkspace(fixture, t0.AddHours(2));
        await afterDue.SaveCheckAsync(fixture.Manager,
            new(created.CaseId, check.Id, card.Item.CaseVersion, check.Version, check.Level,
                check.Title, CaseCheckStatus.Passed, null, false, originalDue, check.Cost,
                "Проверка завершена после срока", check.Blocker),
            "b1-02-check-close-overdue", CancellationToken.None);

        card = await afterDue.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        check = card.Checks.Single();
        Assert.AreEqual(CaseCheckStatus.Passed, check.Status);
        Assert.AreEqual(originalDue, check.DueAt);

        DateTimeOffset anotherPastDue = t0.AddMinutes(90);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => afterDue.SaveCheckAsync(fixture.Manager,
            new(created.CaseId, check.Id, card.Item.CaseVersion, check.Version, check.Level,
                check.Title, check.Status, null, false, anotherPastDue, check.Cost,
                "Попытка переписать исторический срок", check.Blocker),
            "b1-02-check-change-past", CancellationToken.None));

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => afterDue.SaveCheckAsync(fixture.Manager,
            new(created.CaseId, null, card.Item.CaseVersion, null, CaseCheckLevel.Quick,
                "Новая просроченная проверка", CaseCheckStatus.Planned, fixture.ManagerEmployeeId, true,
                t0.AddMinutes(30), null, "", false),
            "b1-02-check-new-past", CancellationToken.None));

        CaseCard final = await afterDue.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        CheckView finalCheck = final.Checks.Single();
        Assert.AreEqual(originalDue, finalCheck.DueAt);
        Assert.AreEqual(CaseCheckStatus.Passed, finalCheck.Status);
        Assert.AreEqual(2, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item =>
            item.ObjectId == created.CaseId && item.Action == "CaseCheckSaved")));
        Assert.AreEqual(2, await fixture.CountAsync(db => db.BusinessTimeline.CountAsync(item =>
            item.ObjectId == created.CaseId && item.Kind == "Check")));
    }

    private static ProcurementWorkspace NewWorkspace(ProcurementTests.Phase1Fixture fixture, DateTimeOffset now) =>
        new(fixture.Factory, new FixedTimeProvider(now), fixture.FileStorage);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
