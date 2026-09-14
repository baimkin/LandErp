using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Collection;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ProcurementTests
{
    [TestMethod]
    public async Task ManagerHeadForwardReturnScopesRevisionsHistoryAuditAndRestart()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync(); await using LandErpDbContext db = sandbox.Context(); await db.Database.MigrateAsync();
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "owner-d@test.invalid", "Procurement tests");
        Guid foreignId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "foreign-d@test.invalid", "Other organization");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId); await IdentityOrganizationTests.EnableMfaAsync(bootstrap, foreignId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IOrganizationWorkspace organization = scope.ServiceProvider.GetRequiredService<IOrganizationWorkspace>();
        IAccessControl access = scope.ServiceProvider.GetRequiredService<IAccessControl>();
        IDbContextFactory<LandErpDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        Subject owner = new(ownerId, true);
        await organization.CreateDepartmentAsync(owner, "Закупка", "test", CancellationToken.None);
        OrganizationView structure = await organization.ReadAsync(owner, CancellationToken.None); Guid department = structure.Departments.Single().Id;
        Guid headId = await InviteAsync(services, organization, owner, structure, "Руководитель", "head-d@test.invalid", "ProcurementHead", department, AccessScope.Department);
        Guid managerId = await InviteAsync(services, organization, owner, structure, "Менеджер", "manager-d@test.invalid", "ProcurementManager", department, AccessScope.Department);
        Guid otherManagerId = await InviteAsync(services, organization, owner, structure, "Другой менеджер", "other-manager-d@test.invalid", "ProcurementManager", department, AccessScope.Department);
        Subject manager = new(managerId, false); Subject head = new(headId, false); Subject other = new(otherManagerId, false);
        CollectionAdministration admin = new(factory, access, TimeProvider.System); CollectorGateway gateway = new(factory, TimeProvider.System);
        AgentCredential agent = await admin.CreateAgentAsync(owner, "D Collector", "test", CancellationToken.None);
        await gateway.RegisterAsync(agent, new(1, "control", [ListingSource.Avito]), CancellationToken.None);
        await admin.CreateSearchAsync(owner, new(agent.AgentId, department, null, "D Control", ListingSource.Avito, "https://www.avito.ru/moskva/zemelnye_uchastki", 1), "test", CancellationToken.None);
        Guid searchId = (await admin.ReadAsync(owner, CancellationToken.None)).Searches.Single().Id; await admin.EnqueueAsync(owner, searchId, "test", CancellationToken.None);
        CollectionWork work = (await gateway.ClaimAsync(agent, CancellationToken.None))!;
        ListingData data = new()
        {
            Source = ListingSource.Avito,
            ExternalId = "99887766",
            Url = "https://www.avito.ru/moskva/zemelnye_uchastki/uchastok_99887766",
            ObservedAt = DateTimeOffset.UtcNow,
            AdapterVersion = "control",
            Provenance = "Control",
            Title = new(FieldPresence.Present, "Участок закупки"),
            Price = new(FieldPresence.Present, "2000000 ₽", 2000000m),
            AreaSquareMeters = new(FieldPresence.Present, "1200 м²", 1200m),
            Location = new(FieldPresence.Present, "Контрольная локация")
        };
        await gateway.AcceptAsync(agent, new(Guid.CreateVersion7(), work.JobId, work.LeaseId, CollectionOutcome.Success, [new("first-d", data)], false), CancellationToken.None);
        Guid listingId = await db.Listings.Select(item => item.Id).SingleAsync();
        ProcurementWorkspace workspace = new(factory, access, TimeProvider.System);
        Assert.AreEqual(1, (await workspace.ReadQueueAsync(manager, new(), CancellationToken.None)).Total);
        Assert.AreEqual(0, (await workspace.ReadQueueAsync(new(foreignId, true), new(), CancellationToken.None)).Total);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.ReadCardAsync(new(foreignId, true), listingId, CancellationToken.None));
        CaseCard card = await workspace.ReadCardAsync(manager, listingId, CancellationToken.None); Assert.IsTrue(card.CanManagerDecide); Assert.IsFalse(card.CanHeadDecide);
        DecisionCommand take = new(listingId, 0, 1, ProcurementAction.TakeWork, "Первичный анализ", "", null, null);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.DecideAsync(head, take, "test", CancellationToken.None));
        await workspace.DecideAsync(manager, take, "test", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => workspace.DecideAsync(manager, take, "test", CancellationToken.None));
        card = await workspace.ReadCardAsync(manager, listingId, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.DecideAsync(other, take with { ExpectedCaseVersion = card.Item.CaseVersion }, "test", CancellationToken.None));
        await workspace.AddNoteAsync(manager, new(listingId, card.Item.CaseVersion, "Проверено расположение", false, "", null), "test", CancellationToken.None);
        card = await workspace.ReadCardAsync(manager, listingId, CancellationToken.None);
        await workspace.AddNoteAsync(manager, new(listingId, card.Item.CaseVersion, "Продавец: ручной контакт", true, "Договорились уточнить подъезд", DateTimeOffset.UtcNow.AddMinutes(-5)), "test", CancellationToken.None);
        card = await workspace.ReadCardAsync(manager, listingId, CancellationToken.None); Guid headEmployee = (await organization.ReadAsync(owner, CancellationToken.None)).Employees.Single(item => item.Login == "head-d@test.invalid").Id;
        DecisionCommand forward = new(listingId, card.Item.CaseVersion, 1, ProcurementAction.Forward, "Первичный анализ выполнен", "", headEmployee, null);
        await workspace.DecideAsync(manager, forward, "test", CancellationToken.None);
        card = await workspace.ReadCardAsync(head, listingId, CancellationToken.None); Assert.IsTrue(card.CanHeadDecide); Assert.IsFalse(card.CanManagerDecide);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.DecideAsync(manager, new(listingId, card.Item.CaseVersion, 1, ProcurementAction.Approve, "Одобрить", "", null, null), "test", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => workspace.DecideAsync(head, new(listingId, card.Item.CaseVersion, 1, ProcurementAction.Return, "", "", null, null), "test", CancellationToken.None));
        Guid managerEmployee = (await organization.ReadAsync(owner, CancellationToken.None)).Employees.Single(item => item.Login == "manager-d@test.invalid").Id; DateTimeOffset due = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeMilliseconds());
        await workspace.DecideAsync(head, new(listingId, card.Item.CaseVersion, 1, ProcurementAction.Return, "Нет данных о подъезде", "Уточнить наличие дороги", managerEmployee, due), "test", CancellationToken.None);
        card = await workspace.ReadCardAsync(manager, listingId, CancellationToken.None); Assert.AreEqual("returned", card.Item.Stage); Assert.AreEqual(due, card.Item.DueAt);
        Assert.IsTrue(card.Timeline.Any(item => item.Body.Contains("Уточнить наличие дороги", StringComparison.Ordinal) && item.Target == "Менеджер"));
        Assert.IsTrue(card.Timeline.Any(item => item.Kind == "Contact" && item.EffectiveAt != null));
        Assert.IsTrue((await workspace.ReadNotificationsAsync(manager, CancellationToken.None)).Any(item => !item.Read));
        foreach (var notification in await workspace.ReadNotificationsAsync(manager, CancellationToken.None)) await workspace.MarkNotificationReadAsync(manager, notification.Id, CancellationToken.None);
        Assert.IsTrue((await workspace.ReadNotificationsAsync(manager, CancellationToken.None)).All(item => item.Read));
        await workspace.DecideAsync(manager, forward with { ExpectedCaseVersion = card.Item.CaseVersion }, "test", CancellationToken.None);
        await gateway.AcceptAsync(agent, new(Guid.CreateVersion7(), work.JobId, work.LeaseId, CollectionOutcome.Success, [new("changed-d", data with { ObservedAt = data.ObservedAt.AddSeconds(1), Price = new(FieldPresence.Present, "1900000 ₽", 1900000m) })], true), CancellationToken.None);
        card = await workspace.ReadCardAsync(head, listingId, CancellationToken.None); Assert.IsTrue(card.Item.Changed);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => workspace.DecideAsync(head, new(listingId, card.Item.CaseVersion, 1, ProcurementAction.Approve, "Одобрить", "", null, null), "test", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => workspace.DecideAsync(head, new(listingId, card.Item.CaseVersion, 2, ProcurementAction.Approve, "Одобрить", "", null, null), "test", CancellationToken.None));
        await workspace.DecideAsync(head, new(listingId, card.Item.CaseVersion, 2, ProcurementAction.Return, "Изменилась цена", "Обновить первичный анализ", managerEmployee, null), "test", CancellationToken.None);
        card = await workspace.ReadCardAsync(manager, listingId, CancellationToken.None);
        await workspace.DecideAsync(manager, forward with { ExpectedCaseVersion = card.Item.CaseVersion, ExpectedDataRevision = 2 }, "test", CancellationToken.None);
        card = await workspace.ReadCardAsync(head, listingId, CancellationToken.None);
        await workspace.DecideAsync(head, new(listingId, card.Item.CaseVersion, 2, ProcurementAction.Approve, "Можно продолжать проверку", "", null, null), "test", CancellationToken.None);
        await using LandErpDbContext restarted = sandbox.Context(true);
        Assert.AreEqual("approved", (await restarted.PropertyCases.SingleAsync()).StageId); Assert.AreEqual(3, await restarted.Approvals.CountAsync());
        Assert.IsTrue(await restarted.AuditEvents.AnyAsync(item => item.Action == "ProcurementReturn"));
        Assert.AreEqual(9, await restarted.BusinessTimeline.CountAsync());
        Assert.AreEqual(0, (await workspace.ReadQueueAsync(manager, new(), CancellationToken.None)).Total);
        await using Npgsql.NpgsqlConnection runtime = new(sandbox.RuntimeConnection); await runtime.OpenAsync();
        foreach (string table in new[] { "workflow.approvals", "workflow.transitions", "foundation.business_timeline", "catalog.observations" })
        { await using Npgsql.NpgsqlCommand forbidden = new("DELETE FROM " + table, runtime); var error = await Assert.ThrowsExactlyAsync<Npgsql.PostgresException>(() => forbidden.ExecuteNonQueryAsync()); Assert.AreEqual("42501", error.SqlState); }
        await sandbox.BackupRestoreAsync();
        await admin.EnqueueAsync(owner, searchId, "test", CancellationToken.None);
        CollectionWork uiWork = (await gateway.ClaimAsync(agent, CancellationToken.None))!;
        await gateway.AcceptAsync(agent, new(Guid.CreateVersion7(), uiWork.JobId, uiWork.LeaseId, CollectionOutcome.Success, [new("ui-changed-d", data with { ObservedAt = data.ObservedAt.AddSeconds(2), Price = new(FieldPresence.Present, "1800000 ₽", 1800000m) })], true), CancellationToken.None);
        await ProcurementUiScenario.RunAsync(sandbox, listingId);
        await organization.CreateTeamAsync(owner, department, "Команда А", "test", CancellationToken.None);
        await organization.CreateTeamAsync(owner, department, "Команда Б", "test", CancellationToken.None);
        structure = await organization.ReadAsync(owner, CancellationToken.None); Guid teamA = structure.Teams.Single(item => item.Name == "Команда А").Id; Guid teamB = structure.Teams.Single(item => item.Name == "Команда Б").Id;
        await admin.CreateSearchAsync(owner, new(agent.AgentId, department, teamA, "Team control", ListingSource.Avito, "https://www.avito.ru/moskva/zemelnye_uchastki", 1), "test", CancellationToken.None);
        Guid teamSearch = (await admin.ReadAsync(owner, CancellationToken.None)).Searches.Single(item => item.Label == "Team control").Id;
        await admin.EnqueueAsync(owner, teamSearch, "test", CancellationToken.None); CollectionWork teamWork = (await gateway.ClaimAsync(agent, CancellationToken.None))!;
        ListingData teamData = data with { ExternalId = "99887767", Url = "https://www.avito.ru/moskva/zemelnye_uchastki/uchastok_99887767" };
        await gateway.AcceptAsync(agent, new(Guid.CreateVersion7(), teamWork.JobId, teamWork.LeaseId, CollectionOutcome.Success, [new("team-d", teamData)], true), CancellationToken.None);
        Guid teamListing = await db.Listings.Where(item => item.ExternalId == "99887767").Select(item => item.Id).SingleAsync();
        await ChangeScopeAsync(organization, owner, "other-manager-d@test.invalid", AccessScope.Team, teamA);
        Assert.AreEqual(1, (await workspace.ReadQueueAsync(other, new(), CancellationToken.None)).Total);
        await ChangeScopeAsync(organization, owner, "other-manager-d@test.invalid", AccessScope.Team, teamB);
        Assert.AreEqual(0, (await workspace.ReadQueueAsync(other, new(), CancellationToken.None)).Total);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.ReadCardAsync(other, teamListing, CancellationToken.None));
        await ChangeScopeAsync(organization, owner, "manager-d@test.invalid", AccessScope.Own, null);
        Assert.AreEqual(0, (await workspace.ReadQueueAsync(manager, new(), CancellationToken.None)).Total);
        await ChangeScopeAsync(organization, owner, "manager-d@test.invalid", AccessScope.Department, null);
        foreach (ProcurementAction action in new[] { ProcurementAction.Clarify, ProcurementAction.Monitor, ProcurementAction.TakeWork, ProcurementAction.Forward })
        { card = await workspace.ReadCardAsync(manager, teamListing, CancellationToken.None); await workspace.DecideAsync(manager, new(teamListing, card.Item.CaseVersion, 1, action, "Контроль решения", "Уточнить дорогу", action == ProcurementAction.Forward ? headEmployee : null, null), "test", CancellationToken.None); }
        await ChangeScopeAsync(organization, owner, "manager-d@test.invalid", AccessScope.AssignedObjects, null);
        Assert.AreEqual(0, (await workspace.ReadQueueAsync(manager, new(), CancellationToken.None)).Total);
        await ChangeScopeAsync(organization, owner, "manager-d@test.invalid", AccessScope.Own, null);
        Assert.AreEqual(1, (await workspace.ReadQueueAsync(manager, new(), CancellationToken.None)).Total);
        card = await workspace.ReadCardAsync(head, teamListing, CancellationToken.None);
        await workspace.DecideAsync(head, new(teamListing, card.Item.CaseVersion, 1, ProcurementAction.Monitor, "Нужны изменения источника", "", null, null), "test", CancellationToken.None);
        foreach (ProcurementAction action in new[] { ProcurementAction.TakeWork, ProcurementAction.Forward })
        { card = await workspace.ReadCardAsync(manager, teamListing, CancellationToken.None); await workspace.DecideAsync(manager, new(teamListing, card.Item.CaseVersion, 1, action, "Повторный анализ", "", action == ProcurementAction.Forward ? headEmployee : null, null), "test", CancellationToken.None); }
        card = await workspace.ReadCardAsync(head, teamListing, CancellationToken.None);
        await workspace.DecideAsync(head, new(teamListing, card.Item.CaseVersion, 1, ProcurementAction.Reject, "Нет подходящего подъезда", "", null, null), "test", CancellationToken.None);
        Assert.AreEqual("rejected", (await workspace.ReadCardAsync(manager, teamListing, CancellationToken.None)).Item.Stage);
        await ChangeScopeAsync(organization, owner, "manager-d@test.invalid", AccessScope.Department, null);
        await admin.EnqueueAsync(owner, teamSearch, "test", CancellationToken.None);
        CollectionWork rejectWork = (await gateway.ClaimAsync(agent, CancellationToken.None))!;
        await gateway.AcceptAsync(agent, new(Guid.CreateVersion7(), rejectWork.JobId, rejectWork.LeaseId, CollectionOutcome.Success,
            [new("manager-reject-d", teamData with { ExternalId = "99887768", Url = "https://www.avito.ru/moskva/zemelnye_uchastki/uchastok_99887768" })], true), CancellationToken.None);
        Guid rejectListing = await db.Listings.Where(item => item.ExternalId == "99887768").Select(item => item.Id).SingleAsync();
        await workspace.DecideAsync(manager, new(rejectListing, 0, 1, ProcurementAction.Reject, "Не соответствует первичным требованиям", "", null, null), "test", CancellationToken.None);
        Assert.AreEqual("rejected", (await workspace.ReadCardAsync(manager, rejectListing, CancellationToken.None)).Item.Stage);
    }
    private static async Task ChangeScopeAsync(IOrganizationWorkspace organization, Subject owner, string login, AccessScope accessScope, Guid? team)
    { var employee = (await organization.ReadAsync(owner, CancellationToken.None)).Employees.Single(item => item.Login == login); await organization.ChangeAssignmentAsync(owner, new(employee.Id, employee.DepartmentId, employee.PositionId, team, employee.ManagerId, employee.RoleId, accessScope, employee.Version), "test", CancellationToken.None); }
    internal static async Task<Guid> InviteAsync(ServiceProvider services, IOrganizationWorkspace workspace, Subject owner, OrganizationView structure, string name, string email, string role, Guid department, AccessScope scope)
    {
        var invitation = await workspace.InviteAsync(owner, new(name, email, department, null, null, null, structure.Roles.Single(item => item.Name == role).Id, scope), "test", CancellationToken.None);
        await using AsyncServiceScope activation = services.CreateAsyncScope();
        await activation.ServiceProvider.GetRequiredService<AccountActivation>().ActivateAsync(invitation.InvitationId, invitation.OneTimeToken, "Synthetic1!PasswordForTests", CancellationToken.None);
        return await activation.ServiceProvider.GetRequiredService<LandErpDbContext>().Users.Where(item => item.Email == email).Select(item => item.Id).SingleAsync();
    }
}
