using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.Overview;
using LandErp.Infrastructure.Modules.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class CaseTasksTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task IndependentTasksEditCompleteDeleteReplayAndAudit()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await Create(f);
        var empty = await f.Workspace.ReadTasksAsync(f.Manager, id, Ct);
        Assert.AreEqual(0, empty.Tasks.Count);
        Assert.AreEqual(f.ManagerEmployeeId, empty.DefaultEmployeeId);
        var first = Command(id, empty.CaseVersion, f.ManagerEmployeeId, "Позвонить");
        Guid a = await f.Workspace.ChangeTaskAsync(f.Manager, first, "first", Ct);
        Assert.AreEqual(a, await f.Workspace.ChangeTaskAsync(f.Manager, first, "replay", Ct));
        var current = await f.Workspace.ReadTasksAsync(f.Manager, id, Ct);
        Guid b = await f.Workspace.ChangeTaskAsync(f.Manager, Command(id, current.CaseVersion, f.ManagerEmployeeId, "Документы"), "second", Ct);
        current = await f.Workspace.ReadTasksAsync(f.Manager, id, Ct);
        Assert.AreEqual(2, current.Tasks.Count(x => !x.Completed));
        var original = current.Tasks.Single(x => x.Id == a);
        var edit = first with { TaskId = a, ExpectedCaseVersion = current.CaseVersion, ExpectedTaskVersion = original.Version,
            Title = "Позвонить завтра", Description = "Сохранённые подробности", CommandId = Guid.CreateVersion7() };
        await f.Workspace.ChangeTaskAsync(f.Manager, edit, "edit", Ct);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => f.Workspace.ChangeTaskAsync(f.Manager,
            edit with { CommandId = Guid.CreateVersion7() }, "stale", Ct));
        current = await f.Workspace.ReadTasksAsync(f.Manager, id, Ct);
        var complete = Edit(current, a, id, CaseTaskAction.Complete);
        await f.Workspace.ChangeTaskAsync(f.Manager, complete, "complete", Ct);
        await f.Workspace.ChangeTaskAsync(f.Manager, complete, "repeat-complete", Ct);
        current = await f.Workspace.ReadTasksAsync(f.Manager, id, Ct);
        Assert.IsTrue(current.Tasks.Single(x => x.Id == a).Completed);
        Assert.AreEqual("Сохранённые подробности", current.Tasks.Single(x => x.Id == a).Description);
        Assert.IsFalse(current.Tasks.Single(x => x.Id == b).Completed);
        var delete = Edit(current, b, id, CaseTaskAction.Delete);
        await f.Workspace.ChangeTaskAsync(f.Manager, delete, "delete", Ct);
        await f.Workspace.ChangeTaskAsync(f.Manager, delete, "repeat-delete", Ct);
        Assert.AreEqual(0, (await f.Workspace.ReadTasksAsync(f.Manager, id, Ct)).Tasks.Count(x => !x.Completed));
        await using var db = await f.Factory.CreateDbContextAsync();
        Assert.IsTrue((await db.WorkTasks.SingleAsync(x => x.Id == b)).Deleted);
        Assert.AreEqual(5, await db.BusinessTimeline.CountAsync(x => x.ObjectId == id && x.Kind.StartsWith("CaseTask")));
        Assert.AreEqual(5, await db.AuditEvents.CountAsync(x => x.ObjectId == id && x.Action.StartsWith("CaseTask")));
        var queue = await new ProcurementQueueV2ReadService(f.Factory, TimeProvider.System).ReadPageAsync(f.Manager, new(), Ct);
        Assert.AreEqual("Нет задач", queue.Items.Single().NextActionTitle);
    }

    [TestMethod]
    public async Task MoscowDeadlinesSlicesNearestAndOverviewCountObjectsVersusTasks()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await Create(f);
        var clock = new Clock();
        var workspace = new ProcurementWorkspace(f.Factory, clock, f.FileStorage);
        var reader = new ProcurementQueueV2ReadService(f.Factory, clock);
        DateTimeOffset day = WorkTaskDeadline.DayStart(Now);
        async Task<Guid> Add(string title, DateTimeOffset? due, bool hasTime)
        {
            var view = await workspace.ReadTasksAsync(f.Manager, id, Ct);
            return await workspace.ChangeTaskAsync(f.Manager, Command(id, view.CaseVersion, f.ManagerEmployeeId, title)
                with { DueAt = due, DueHasTime = hasTime }, "add", Ct);
        }
        Guid dateOnly = await Add("На сегодня без времени", day, false);
        Guid urgent = await Add("Срочная с точным временем", Now.AddMinutes(-1), true);
        await Add("Завтра", day.AddDays(1), false);
        await Add("Без срока", null, false);
        var tasks = await workspace.ReadTasksAsync(f.Manager, id, Ct);
        Assert.IsFalse(tasks.Tasks.Single(x => x.Id == dateOnly).Overdue);
        Assert.IsTrue(tasks.Tasks.Single(x => x.Id == urgent).Overdue);
        Assert.AreEqual(urgent, tasks.Tasks[0].Id);
        var today = await reader.ReadPageAsync(f.Manager, new(DueTodayOnly: true), Ct);
        Assert.AreEqual(1, today.Total); Assert.AreEqual(1, today.Summary.DueToday); Assert.AreEqual(1, today.Summary.Overdue);
        Assert.AreEqual("Срочная с точным временем", today.Items.Single().NextActionTitle);
        Assert.AreEqual(1, (await reader.ReadPageAsync(f.Manager, new(OverdueOnly: true), Ct)).Total);
        var overview = await new OverviewService(f.Factory, clock).ReadAsync(f.Manager, Ct);
        Assert.AreEqual(4, overview.MyWorkTotal);
        Assert.AreEqual(1, overview.Team.Single().OverdueCases);
        Assert.IsFalse(WorkTaskDeadline.Format(day, false).Contains("00:00", StringComparison.Ordinal));
        Assert.IsFalse(WorkTaskDeadline.Overdue(new() { DueAt = day, DueHasTime = false }, day.AddDays(1).AddTicks(-1)));
        Assert.IsTrue(WorkTaskDeadline.Overdue(new() { DueAt = day, DueHasTime = false }, day.AddDays(1)));
        Assert.IsFalse(WorkTaskDeadline.Overdue(new() { DueAt = Now }, Now));
        Assert.IsTrue(WorkTaskDeadline.Overdue(new() { DueAt = Now }, Now.AddTicks(1)));
    }

    [TestMethod]
    public async Task OrganizationVisibilityAssigneeAndClosedCaseGuards()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        Guid id = await Create(f);
        var view = await f.Workspace.ReadTasksAsync(f.Manager, id, Ct);
        var command = Command(id, view.CaseVersion, f.ManagerEmployeeId, "Доступ");
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.ChangeTaskAsync(f.ForeignOwner, command, "foreign", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.ChangeTaskAsync(f.SecondManager, command, "invisible", Ct));
        Guid second = f.EmployeeId("manager2-phase1@test.invalid");
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.ChangeTaskAsync(f.Manager, command with { EmployeeId = second }, "assignee", Ct));
        Guid task = await f.Workspace.ChangeTaskAsync(f.Manager, command, "valid", Ct);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.ChangeTaskAsync(f.Manager, command with { Title = "Иной payload" }, "reused", Ct));
        Guid other = await Create(f);
        var otherView = await f.Workspace.ReadTasksAsync(f.Manager, other, Ct);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.ChangeTaskAsync(f.Manager,
            Command(other, otherView.CaseVersion, f.ManagerEmployeeId, "Чужая задача") with { TaskId = task, ExpectedTaskVersion = 1 }, "other-case", Ct));
        await using (var db = await f.Factory.CreateDbContextAsync())
        {
            var item = await db.PropertyCases.SingleAsync(x => x.Id == id); item.StageId = "acquired"; await db.SaveChangesAsync();
        }
        view = await f.Workspace.ReadTasksAsync(f.Manager, id, Ct);
        Assert.IsFalse(view.CanEdit);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.ChangeTaskAsync(f.Manager, Edit(view, task, id, CaseTaskAction.Delete), "closed", Ct));
    }

    [TestMethod]
    public async Task LegacyTaskAndUserTasksSurviveForwardAndReturn()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await Create(f);
        Guid legacy;
        await using (var db = await f.Factory.CreateDbContextAsync())
        {
            var item = await db.PropertyCases.SingleAsync(x => x.Id == id);
            var task = await db.WorkTasks.SingleAsync(x => x.Id == item.WorkTaskId);
            task.Title = "Прежнее действие"; task.Description = "Прежние подробности"; task.Completed = false;
            task.IsUserTask = true; task.DueAt = Now; legacy = task.Id; await db.SaveChangesAsync();
        }
        var view = await f.Workspace.ReadTasksAsync(f.Manager, id, Ct);
        Guid userTask = await f.Workspace.ChangeTaskAsync(f.Manager, Command(id, view.CaseVersion, f.ManagerEmployeeId, "Новая задача"), "add", Ct);
        var card = await f.Workspace.ReadCardAsync(f.Manager, id, Ct);
        Guid head = f.EmployeeId("head-phase1@test.invalid");
        await f.Workspace.DecideAsync(f.Manager, new(id, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Forward, "Первичный анализ выполнен", "", head, null), "forward", Ct);
        card = await f.Workspace.ReadCardAsync(f.Head, id, Ct);
        await f.Workspace.DecideAsync(f.Head, new(id, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Return, "Уточните документы", "Проверить дорогу", f.ManagerEmployeeId, null), "return", Ct);
        view = await f.Workspace.ReadTasksAsync(f.Manager, id, Ct);
        Assert.IsFalse(view.Tasks.Single(x => x.Id == userTask).Completed);
        var preserved = view.Tasks.Single(x => x.Id == legacy);
        Assert.AreEqual("Прежнее действие", preserved.Title); Assert.AreEqual(Now, preserved.DueAt);
        Assert.AreEqual("Прежние подробности", preserved.Description); Assert.AreEqual(f.ManagerEmployeeId, preserved.EmployeeId);
        Assert.AreEqual(3, view.Tasks.Count(x => !x.Completed));
    }

    [TestMethod]
    public async Task MigrationPreservesLegacyValuesAndCommentsAndAssigneeFilterSeesNonNearestTask()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        Guid id = await Create(f);
        Guid legacy;
        long version;
        DateTimeOffset exact = Now.AddSeconds(37);
        await using (var db = await f.Factory.CreateDbContextAsync())
        {
            var item = await db.PropertyCases.SingleAsync(x => x.Id == id);
            var task = await db.WorkTasks.SingleAsync(x => x.Id == item.WorkTaskId);
            legacy = task.Id; task.Completed = false; task.Title = "Первичный анализ";
            task.Description = "Не удалять старые данные"; task.DueAt = exact;
            await db.SaveChangesAsync(); version = task.Version;
        }
        // Только БД текущей disposable fixture: проверяем настоящий upgrade старой схемы.
        await using (var db = f.Sandbox.Context())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260927010000_CaseCheckRichResults");
            await migrator.MigrateAsync();
            Assert.IsFalse(db.Database.HasPendingModelChanges());
            var task = await db.WorkTasks.SingleAsync(x => x.Id == legacy);
            Assert.IsTrue(task.IsUserTask); Assert.IsTrue(task.DueHasTime); Assert.IsFalse(task.Deleted);
            Assert.AreEqual(exact, task.DueAt); Assert.AreEqual(version, task.Version);
            Assert.AreEqual("Первичный анализ", task.Title); Assert.AreEqual("Не удалять старые данные", task.Description);
            Assert.AreEqual(f.ManagerEmployeeId, task.EmployeeId);
            int comments = await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM pg_attribute a
                WHERE a.attrelid='workflow.work_tasks'::regclass
                  AND a.attname IN ('deleted','is_user_task','due_has_time')
                  AND col_description(a.attrelid,a.attnum) IS NOT NULL
                """).SingleAsync();
            Assert.AreEqual(3, comments);
        }
        var view = await f.Workspace.ReadTasksAsync(f.Manager, id, Ct);
        Guid second = f.EmployeeId("manager2-phase1@test.invalid");
        await f.Workspace.ChangeTaskAsync(f.Manager, Command(id, view.CaseVersion, second, "Вторая без срока"), "other-assignee", Ct);
        var reader = new ProcurementQueueV2ReadService(f.Factory, new Clock());
        var page = await reader.ReadPageAsync(f.Manager, new(AssigneeId: second), Ct);
        Assert.AreEqual(1, page.Total); Assert.AreEqual(2, page.Assignees.Count);
        Assert.AreEqual("Первичный анализ", page.Items.Single().NextActionTitle);
        Assert.AreEqual(1, (await new OverviewService(f.Factory, new Clock()).ReadAsync(f.SecondManager, Ct)).MyWorkTotal);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ClosingPreservesUserTasksAndExcludesThemFromWorkUntilResume(bool purchase)
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await f.CreateUnlinkedManualAsync();
        Guid id = (await f.Workspace.TakeToWorkAsync(f.Manager, new(sourceId), "take", Ct)).CaseId;
        Guid legacy;
        await using (var db = await f.Factory.CreateDbContextAsync())
        {
            var item = await db.PropertyCases.SingleAsync(x => x.Id == id);
            var task = await db.WorkTasks.SingleAsync(x => x.Id == item.WorkTaskId);
            task.Title = "Сохранённая старая задача"; task.Description = "Не менять при закрытии";
            task.Completed = false; task.IsUserTask = true; task.DueAt = Now.AddHours(-1);
            legacy = task.Id; await db.SaveChangesAsync();
        }
        var clock = new Clock();
        var workspace = new ProcurementWorkspace(f.Factory, clock, f.FileStorage);
        var before = await workspace.ReadTasksAsync(f.Manager, id, Ct);
        Guid second = await workspace.ChangeTaskAsync(f.Manager,
            Command(id, before.CaseVersion, f.ManagerEmployeeId, "Вторая задача на дату")
                with { DueAt = WorkTaskDeadline.DayStart(Now), DueHasTime = false }, "second", Ct);
        before = await workspace.ReadTasksAsync(f.Manager, id, Ct);
        var card = await workspace.ReadCardAsync(f.Manager, id, Ct);
        if (purchase)
        {
            await f.SetExplicitAccessAsync(f.EmployeeId("head-phase1@test.invalid"),
                ProcurementTestsHelper.ProcurementHeadAccess(AccessScope.Organization) with { CanConfirmPurchase = true });
            var command = new MarkCaseAcquired(id, card.Item.CaseVersion, 900000m,
                DateOnly.FromDateTime(Now.UtcDateTime), "Покупка T-01");
            await workspace.MarkAcquiredAsync(f.Head, command, "purchase", Ct);
            await workspace.MarkAcquiredAsync(f.Head, command, "purchase-retry", Ct);
        }
        else
            await workspace.DecideAsync(f.Manager, new(id, card.Item.CaseVersion, card.Item.SourceRevision,
                ProcurementAction.Reject, "Объект сейчас не подходит", "", null, null), "reject", Ct);

        var after = await workspace.ReadTasksAsync(f.Manager, id, Ct);
        foreach (var old in before.Tasks)
            Assert.AreEqual(old, after.Tasks.Single(x => x.Id == old.Id));
        await using (var db = await f.Factory.CreateDbContextAsync())
        {
            var item = await db.PropertyCases.SingleAsync(x => x.Id == id);
            Assert.AreNotEqual(legacy, item.WorkTaskId);
            var system = await db.WorkTasks.SingleAsync(x => x.Id == item.WorkTaskId);
            Assert.IsFalse(system.IsUserTask); Assert.IsTrue(system.Completed);
            Assert.AreEqual(0, await db.BusinessTimeline.CountAsync(x => x.ObjectId == id
                && (x.Kind == "CaseTaskComplete" || x.Kind == "CaseTaskDelete")));
            Assert.AreEqual(1, await db.WorkflowTransitions.CountAsync(x => x.ObjectId == id
                && x.Action == (purchase ? "Acquire" : "Reject")));
        }
        var reader = new ProcurementQueueV2ReadService(f.Factory, clock);
        var queue = await reader.ReadPageAsync(f.Manager, new(), Ct);
        Assert.AreEqual(0, queue.Total); Assert.AreEqual(0, queue.Summary.DueToday); Assert.AreEqual(0, queue.Summary.Overdue);
        string stage = purchase ? "acquired" : "rejected";
        Assert.AreEqual(0, (await reader.ReadPageAsync(f.Manager, new(Stage: stage, DueTodayOnly: true), Ct)).Total);
        Assert.AreEqual(0, (await reader.ReadPageAsync(f.Manager, new(Stage: stage, OverdueOnly: true), Ct)).Total);
        var overview = await new OverviewService(f.Factory, clock).ReadAsync(f.Manager, Ct);
        Assert.IsFalse(overview.MyWork.Any(x => x.Key == legacy.ToString() || x.Key == second.ToString()));
        Assert.AreEqual(0, overview.Team.Sum(x => x.OverdueCases));

        if (!purchase)
        {
            var source = (await workspace.ReadItemAsync(f.Manager, sourceId, Ct)).Item;
            await workspace.ResumeCaseAsync(f.Manager, new(sourceId, source.Version), "resume", Ct);
            var resumed = await workspace.ReadTasksAsync(f.Manager, id, Ct);
            foreach (var old in before.Tasks)
                Assert.AreEqual(old, resumed.Tasks.Single(x => x.Id == old.Id));
            queue = await reader.ReadPageAsync(f.Manager, new(), Ct);
            Assert.AreEqual(1, queue.Total); Assert.AreEqual(1, queue.Summary.DueToday); Assert.AreEqual(1, queue.Summary.Overdue);
            overview = await new OverviewService(f.Factory, clock).ReadAsync(f.Manager, Ct);
            Assert.IsTrue(overview.MyWork.Any(x => x.Key == legacy.ToString()));
            Assert.IsTrue(overview.MyWork.Any(x => x.Key == second.ToString()));
        }
    }

    private static async Task<Guid> Create(ProcurementTests.Phase1Fixture f) =>
        (await f.Workspace.CreateManualCaseAsync(f.Manager, new("Объект T-01", "Москва", null, 1000000m, 900m, "Тест задач", Guid.CreateVersion7()), "create", Ct)).CaseId;
    private static ChangeCaseTask Command(Guid id, long version, Guid employee, string title) =>
        new(id, version, null, 0, CaseTaskAction.Save, title, "", WorkTaskType.General, employee, null, false, Guid.CreateVersion7());
    private static ChangeCaseTask Edit(CaseTasksView view, Guid taskId, Guid caseId, CaseTaskAction action)
    {
        var task = view.Tasks.Single(x => x.Id == taskId);
        return new(caseId, view.CaseVersion, task.Id, task.Version, action, task.Title, task.Description, task.Type,
            task.EmployeeId, task.DueAt, task.DueHasTime, Guid.CreateVersion7());
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
