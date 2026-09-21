using System.Text.Json;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ProcurementCommandReplayTests
{
    [TestMethod]
    public async Task ManualCaseConcurrentRetryCreatesOneCompleteAggregateAndDurableReceipt()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        CreateManualPropertyCase command = ManualCommand(Guid.CreateVersion7());
        ProcurementWorkspace secondWorkspace = NewWorkspace(fixture);
        ManualPropertyCaseResult[] results = await Task.WhenAll(
            fixture.Workspace.CreateManualCaseAsync(fixture.Manager, command, "first-attempt", CancellationToken.None),
            secondWorkspace.CreateManualCaseAsync(fixture.Manager, command, "concurrent-attempt", CancellationToken.None));
        Assert.AreEqual(results[0], results[1]);
        Guid caseId = results[0].CaseId;
        PropertyCase beforeReplay = await fixture.ReadCaseAsync(caseId);
        ManualPropertyCaseResult replayed = await NewWorkspace(fixture).CreateManualCaseAsync(
            fixture.Manager, command, "new-service-instance", CancellationToken.None);
        Assert.AreEqual(results[0], replayed);
        Assert.AreEqual(beforeReplay.Version, (await fixture.ReadCaseAsync(caseId)).Version);

        await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
        Assert.AreEqual(1, await db.PropertyCases.CountAsync());
        Assert.AreEqual(1, await db.WorkAssignments.CountAsync(item => item.ObjectId == caseId));
        Assert.AreEqual(1, await db.WorkTasks.CountAsync(item => item.ObjectId == caseId));
        Assert.AreEqual(5, await db.CaseDocumentRequirements.CountAsync(item => item.PropertyCaseId == caseId));
        Assert.AreEqual(1, await db.WorkflowTransitions.CountAsync(item => item.ObjectId == caseId));
        Assert.AreEqual(1, await db.BusinessTimeline.CountAsync(item => item.ObjectId == caseId && item.Kind == "Created"));
        Assert.AreEqual(0, await db.PropertyCaseSourceLinks.CountAsync());
        Assert.AreEqual(0, await db.Listings.CountAsync());
        var audit = await db.AuditEvents.SingleAsync(item => item.Id == command.CommandId);
        Assert.AreEqual("PropertyCaseCreatedManually", audit.Action);
        Assert.AreEqual(fixture.OrganizationId, audit.OrganizationId);
        Assert.AreEqual(fixture.Manager.UserId, audit.ActorId);
        using JsonDocument details = JsonDocument.Parse(audit.Changes);
        Assert.AreEqual(command.Title, details.RootElement.GetProperty("Title").GetString());
        JsonElement receipt = details.RootElement.GetProperty("CommandReplay");
        Assert.AreEqual(1, receipt.GetProperty("Version").GetInt32());
        Assert.AreEqual(caseId, receipt.GetProperty("ResultId").GetGuid());
        Assert.AreEqual(64, receipt.GetProperty("PayloadHash").GetString()!.Length);
    }

    [TestMethod]
    public async Task RetryReturnsOriginalIdentityEvenAfterWorkingFactsChange()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        CreateManualPropertyCase command = ManualCommand(Guid.CreateVersion7());
        ManualPropertyCaseResult original = await fixture.Workspace.CreateManualCaseAsync(
            fixture.Manager, command, "create", CancellationToken.None);
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            PropertyCase propertyCase = await db.PropertyCases.SingleAsync(item => item.Id == original.CaseId);
            propertyCase.WorkingTitle = "Уточнённые рабочие данные";
            await db.SaveChangesAsync();
        }
        PropertyCase before = await fixture.ReadCaseAsync(original.CaseId);
        ManualPropertyCaseResult repeated = await NewWorkspace(fixture).CreateManualCaseAsync(
            fixture.Manager, command, "retry-after-edit", CancellationToken.None);
        Assert.AreEqual(original, repeated);
        PropertyCase after = await fixture.ReadCaseAsync(original.CaseId);
        Assert.AreEqual(before.WorkingTitle, after.WorkingTitle);
        Assert.AreEqual(before.Version, after.Version);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item => item.Id == command.CommandId)));
    }

    [TestMethod]
    public async Task SameCommandCannotBeUsedForAnotherPayloadActorOrganizationOrAction()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        CreateManualPropertyCase command = ManualCommand(Guid.CreateVersion7());
        ManualPropertyCaseResult result = await fixture.Workspace.CreateManualCaseAsync(
            fixture.Manager, command, "create", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.CreateManualCaseAsync(
            fixture.Manager, command with { Title = "Другой объект" }, "different-payload", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.CreateManualCaseAsync(
            fixture.SecondManager, command, "other-actor", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.CreateManualCaseAsync(
            fixture.ForeignOwner, command, "other-organization", CancellationToken.None));
        PropertyCase propertyCase = await fixture.ReadCaseAsync(result.CaseId);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.AddNoteAsync(fixture.Manager,
            new(result.CaseId, propertyCase.Version, "Новая заметка", false, "", null, command.CommandId),
            "different-action", CancellationToken.None));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.PropertyCases.CountAsync()));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item => item.Id == command.CommandId)));
        Assert.AreEqual(0, await fixture.CountAsync(db => db.BusinessTimeline.CountAsync(item => item.Kind == "Note")));
    }

    [TestMethod]
    public async Task InvalidCreationDoesNotConsumeCommandAndDifferentIdsRemainSeparateAdditions()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        CreateManualPropertyCase command = ManualCommand(Guid.CreateVersion7());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.CreateManualCaseAsync(
            fixture.Manager, command with { Title = "x" }, "invalid", CancellationToken.None));
        Assert.AreEqual(0, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item => item.Id == command.CommandId)));
        ManualPropertyCaseResult first = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager, command, "corrected", CancellationToken.None);
        ManualPropertyCaseResult second = await fixture.Workspace.CreateManualCaseAsync(
            fixture.Manager, command with { CommandId = Guid.CreateVersion7() }, "intentional-new-addition", CancellationToken.None);
        Assert.AreNotEqual(first.CaseId, second.CaseId, "Equal content is not itself a duplicate command.");
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.CreateManualCaseAsync(
            fixture.Manager, command with { CommandId = Guid.Empty }, "empty-command-id", CancellationToken.None));
        Assert.AreEqual(2, await fixture.CountAsync(db => db.PropertyCases.CountAsync()));
    }

    [TestMethod]
    public async Task NotesAndLegacyContactVariantReplayWithoutNewFactsOrVersionBumps()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(
            fixture.Manager, ManualCommand(Guid.CreateVersion7()), "create", CancellationToken.None);
        foreach (bool contact in new[] { false, true })
        {
            PropertyCase propertyCase = await fixture.ReadCaseAsync(created.CaseId);
            AddCaseNote command = new(created.CaseId, propertyCase.Version,
                contact ? "Разговор с собственником" : "Рабочая заметка", contact,
                contact ? "Договорились о встрече" : "", contact ? DateTimeOffset.UtcNow : null, Guid.CreateVersion7());
            await Task.WhenAll(
                fixture.Workspace.AddNoteAsync(fixture.Manager, command, "note-first", CancellationToken.None),
                NewWorkspace(fixture).AddNoteAsync(fixture.Manager, command, "note-concurrent", CancellationToken.None));
            PropertyCase after = await fixture.ReadCaseAsync(created.CaseId);
            Assert.AreEqual(propertyCase.Version + 1, after.Version);
            await NewWorkspace(fixture).AddNoteAsync(fixture.Manager,
                command with { ExpectedCaseVersion = after.Version }, "note-reloaded-version", CancellationToken.None);
            Assert.AreEqual(after.Version, (await fixture.ReadCaseAsync(created.CaseId)).Version);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.AddNoteAsync(fixture.Manager,
                command with { Text = "Изменённое содержимое" }, "note-conflict", CancellationToken.None));
            Assert.AreEqual(1, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item => item.Id == command.CommandId)));
        }
        Assert.AreEqual(1, await fixture.CountAsync(db => db.BusinessTimeline.CountAsync(item => item.ObjectId == created.CaseId && item.Kind == "Note")));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.BusinessTimeline.CountAsync(item => item.ObjectId == created.CaseId && item.Kind == "Contact")));
    }

    [TestMethod]
    public async Task FirstWriteVersionConflictRollsBackReceiptAndCanBeCorrectedWithSameId()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(
            fixture.Manager, ManualCommand(Guid.CreateVersion7()), "create", CancellationToken.None);
        PropertyCase propertyCase = await fixture.ReadCaseAsync(created.CaseId);
        AddCaseNote command = new(created.CaseId, propertyCase.Version + 10, "Заметка после конфликта", false, "", null, Guid.CreateVersion7());
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => fixture.Workspace.AddNoteAsync(
            fixture.Manager, command, "stale", CancellationToken.None));
        Assert.AreEqual(0, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item => item.Id == command.CommandId)));
        Assert.AreEqual(propertyCase.Version, (await fixture.ReadCaseAsync(created.CaseId)).Version);
        await fixture.Workspace.AddNoteAsync(fixture.Manager, command with { ExpectedCaseVersion = propertyCase.Version },
            "correct-version", CancellationToken.None);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.BusinessTimeline.CountAsync(item => item.ObjectId == created.CaseId && item.Kind == "Note")));
    }

    [TestMethod]
    public async Task NegotiationRetryReturnsOriginalIdAndDoesNotAppendHistoryTwice()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        ManualPropertyCaseResult created = await fixture.Workspace.CreateManualCaseAsync(
            fixture.Manager, ManualCommand(Guid.CreateVersion7()), "create", CancellationToken.None);
        PropertyCase propertyCase = await fixture.ReadCaseAsync(created.CaseId);
        AddNegotiation command = new(created.CaseId, propertyCase.Version, 4_000_000m, 3_800_000m, null,
            "Телефон", "Собственник", "Назначили встречу", "", "Первый контакт", "", null,
            DateTimeOffset.UtcNow, Guid.CreateVersion7());
        Guid[] results = await Task.WhenAll(
            fixture.Workspace.AddNegotiationWithIdAsync(fixture.Manager, command, "contact-first", CancellationToken.None),
            NewWorkspace(fixture).AddNegotiationWithIdAsync(fixture.Manager, command, "contact-concurrent", CancellationToken.None));
        Assert.AreEqual(results[0], results[1]);
        PropertyCase after = await fixture.ReadCaseAsync(created.CaseId);
        Assert.AreEqual(propertyCase.Version + 1, after.Version);
        // Ordinary acknowledgement replay is not a new mutation even after a stage changes.
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            PropertyCase item = await db.PropertyCases.SingleAsync(value => value.Id == created.CaseId);
            item.StageId = "monitor";
            await db.SaveChangesAsync();
        }
        long versionBeforeReplay = (await fixture.ReadCaseAsync(created.CaseId)).Version;
        Guid retried = await NewWorkspace(fixture).AddNegotiationWithIdAsync(fixture.Manager,
            command with { ExpectedCaseVersion = versionBeforeReplay }, "contact-after-stage-change", CancellationToken.None);
        Assert.AreEqual(results[0], retried);
        Assert.AreEqual(versionBeforeReplay, (await fixture.ReadCaseAsync(created.CaseId)).Version);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Workspace.AddNegotiationAsync(fixture.Manager,
            command with { Comment = "Другое содержимое" }, "contact-conflict", CancellationToken.None));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.CaseNegotiations.CountAsync(item => item.PropertyCaseId == created.CaseId)));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.BusinessTimeline.CountAsync(item => item.ObjectId == created.CaseId && item.Kind == "Negotiation")));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item => item.Id == command.CommandId)));
    }

    [TestMethod]
    public async Task ReplayStillRequiresCurrentScopeAndActiveEmployee()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        CreateManualPropertyCase command = ManualCommand(Guid.CreateVersion7());
        await fixture.Workspace.CreateManualCaseAsync(fixture.Manager, command, "create", CancellationToken.None);
        Guid employeeId = fixture.ManagerEmployeeId;
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            var assignment = await db.EmployeeAssignments.SingleAsync(item => item.EmployeeId == employeeId);
            assignment.OrgUnitId = fixture.DepartmentB;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => NewWorkspace(fixture).CreateManualCaseAsync(
            fixture.Manager, command, "scope-revoked", CancellationToken.None));
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            var employee = await db.Employees.SingleAsync(item => item.Id == employeeId);
            employee.Active = false;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => NewWorkspace(fixture).CreateManualCaseAsync(
            fixture.Manager, command, "employee-disabled", CancellationToken.None));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.PropertyCases.CountAsync()));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item => item.Id == command.CommandId)));
    }

    [TestMethod]
    public async Task LegacyCommandsWithoutIdRemainCompatibleButDoNotPromiseReplay()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        CreateManualPropertyCase command = ManualCommand(null);
        ManualPropertyCaseResult first = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager, command, "legacy-1", CancellationToken.None);
        ManualPropertyCaseResult second = await fixture.Workspace.CreateManualCaseAsync(fixture.Manager, command, "legacy-2", CancellationToken.None);
        Assert.AreNotEqual(first.CaseId, second.CaseId);
        Assert.AreEqual(2, await fixture.CountAsync(db => db.PropertyCases.CountAsync()));
    }

    private static CreateManualPropertyCase ManualCommand(Guid? id) => new(
        "Объект для проверки B1-01", "Химки", "50:10:0000000:101", 4_000_000m, 900m, "Синтетический тест B1-01", id);

    private static ProcurementWorkspace NewWorkspace(ProcurementTests.Phase1Fixture fixture) =>
        new(fixture.Factory, TimeProvider.System, fixture.FileStorage);
}
