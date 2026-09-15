using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Collection;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ProcurementTests
{
    [TestMethod]
    public async Task ManualAndMarketplaceSourcesUseOneIndependentCaseAndCaseIdWorkflow()
    {
        await using Phase1Fixture fixture = await Phase1Fixture.CreateAsync(includeSecondManager: false, includeTeams: false);
        ProcurementWorkspace workspace = fixture.Workspace;
        int agentsBefore = await fixture.CountAsync(db => db.CollectorAgents.CountAsync());
        int jobsBefore = await fixture.CountAsync(db => db.CollectionJobs.CountAsync());
        int observationsBefore = await fixture.CountAsync(db => db.ListingObservations.CountAsync());

        Guid manualId = await workspace.CreateManualAsync(fixture.Manager,
            new(CatalogSource.Telegram, "Участок из Telegram", "Химки", 2_500_000m, 1_500m, null, null,
                "50:10:0000000:1", "Сообщение собственника", "Получено в рабочей группе Telegram"), "manual", CancellationToken.None);
        CatalogItemView manual = (await workspace.ReadIncomingAsync(fixture.Manager, new(), CancellationToken.None)).Items.Single(item => item.Id == manualId);
        Assert.IsNull(manual.Url); Assert.IsNull(manual.ExternalId); Assert.AreEqual(CatalogIngestionKind.Employee, manual.IngestionKind);
        Assert.AreEqual(agentsBefore, await fixture.CountAsync(db => db.CollectorAgents.CountAsync()));
        Assert.AreEqual(jobsBefore, await fixture.CountAsync(db => db.CollectionJobs.CountAsync()));
        Assert.AreEqual(observationsBefore, await fixture.CountAsync(db => db.ListingObservations.CountAsync()));

        await workspace.SetDispositionAsync(fixture.Manager, new(manualId, manual.Version, CatalogDisposition.Monitoring, "Ждём снижения цены"), "monitor", CancellationToken.None);
        manual = (await workspace.ReadIncomingAsync(fixture.Manager, new(Disposition: CatalogDisposition.Monitoring), CancellationToken.None)).Items.Single();
        await workspace.SetDispositionAsync(fixture.Manager, new(manualId, manual.Version, CatalogDisposition.Incoming, "Условия снова интересны"), "resume", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.TakeToWorkAsync(fixture.Head, new(manualId), "head", CancellationToken.None));

        TakeToWorkResult created = await workspace.TakeToWorkAsync(fixture.Manager, new(manualId), "take", CancellationToken.None);
        TakeToWorkResult repeated = await workspace.TakeToWorkAsync(fixture.Manager, new(manualId), "repeat", CancellationToken.None);
        Assert.IsTrue(created.Created); Assert.IsFalse(repeated.Created); Assert.AreEqual(created.CaseId, repeated.CaseId);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.PropertyCases.CountAsync()));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.PropertyCaseSourceLinks.CountAsync(item => item.Confirmed)));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.BusinessTimeline.CountAsync(item => item.Title == "Взят в работу")));
        Assert.AreEqual(0, (await workspace.ReadQueueAsync(fixture.ForeignOwner, new(), CancellationToken.None)).Total);

        (Guid avitoId, Guid cianId, AgentCredential agent, CollectionAdministration administration) = await fixture.IngestMarketplacePairAsync();
        await workspace.TakeToWorkAsync(fixture.Manager, new(avitoId, created.CaseId), "link-avito", CancellationToken.None);
        await workspace.TakeToWorkAsync(fixture.Manager, new(cianId, created.CaseId), "link-cian", CancellationToken.None);
        CaseCard card = await workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { CatalogSource.Telegram, CatalogSource.Avito, CatalogSource.Cian }, card.Sources.Select(item => item.Source).ToArray());
        Assert.AreEqual(3, card.Sources.Count); Assert.AreEqual(2_500_000m, card.Item.Price);
        Assert.AreEqual(created.CaseId, await workspace.ResolveLegacyListingAsync(fixture.Manager, avitoId, CancellationToken.None));
        Guid unlinkedId = await fixture.CreateUnlinkedManualAsync();
        Assert.IsNull(await workspace.ResolveLegacyListingAsync(fixture.Manager, unlinkedId, CancellationToken.None));

        Guid headEmployee = fixture.EmployeeId("head-phase1@test.invalid");
        await workspace.DecideAsync(fixture.Manager, new(created.CaseId, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Forward, "Первичный анализ выполнен", "", headEmployee, null), "forward", CancellationToken.None);
        card = await workspace.ReadCardAsync(fixture.Head, created.CaseId, CancellationToken.None);
        Assert.IsTrue(card.CanHeadDecide);
        await workspace.DecideAsync(fixture.Head, new(created.CaseId, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Return, "Нужно уточнить подъезд", "Получить документ на дорогу", fixture.ManagerEmployeeId, null), "return", CancellationToken.None);
        card = await workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        await workspace.DecideAsync(fixture.Manager, new(created.CaseId, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Forward, "Уточнения выполнены", "", headEmployee, null), "reforward", CancellationToken.None);
        card = await workspace.ReadCardAsync(fixture.Head, created.CaseId, CancellationToken.None);
        await workspace.DecideAsync(fixture.Head, new(created.CaseId, card.Item.CaseVersion, card.Item.SourceRevision,
            ProcurementAction.Approve, "Можно продолжать работу", "", null, null), "approve", CancellationToken.None);

        decimal workingPrice = (await fixture.ReadCaseAsync(created.CaseId)).WorkingPrice!.Value;
        await fixture.IngestChangedAvitoAsync(agent, administration, 1_700_000m);
        card = await workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None);
        Assert.IsTrue(card.Item.Changed); Assert.AreEqual(workingPrice, card.Item.Price, "Source change must not overwrite case-owned facts.");
        CatalogItemView avito = (await workspace.ReadIncomingAsync(fixture.Manager, new(Disposition: null), CancellationToken.None)).Items.Single(item => item.Id == avitoId);
        await workspace.SetDispositionAsync(fixture.Manager, new(avitoId, avito.Version, CatalogDisposition.RemovedAtSource, "Объявление снято источником"), "removed", CancellationToken.None);
        Assert.AreEqual(created.CaseId, (await workspace.ReadCardAsync(fixture.Manager, created.CaseId, CancellationToken.None)).Item.CaseId);

        Guid otherCaseId = await fixture.InsertIndependentCaseAsync("Независимый PropertyCase");
        CaseCard independent = await workspace.ReadCardAsync(fixture.Manager, otherCaseId, CancellationToken.None);
        Assert.AreEqual(0, independent.Sources.Count);
        await Assert.ThrowsExactlyAsync<DbUpdateException>(async () =>
        {
            await using LandErpDbContext db = await fixture.Factory.CreateDbContextAsync();
            db.PropertyCaseSourceLinks.Add(new()
            {
                Id = DataConventions.NewId(),
                OrganizationId = fixture.OrganizationId,
                PropertyCaseId = otherCaseId,
                CatalogItemId = manualId,
                Confirmed = true,
                Provenance = "test",
                ReviewedDataRevision = 1,
                RecordedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        });
        await fixture.Sandbox.BackupRestoreAsync();
    }

    [TestMethod]
    public async Task IncomingUiCreatesCaseUsesCanonicalRouteAndSurvivesRestart()
    {
        await using Phase1Fixture fixture = await Phase1Fixture.CreateAsync(includeSecondManager: false, includeTeams: false);
        Guid unlinkedId = await fixture.CreateUnlinkedManualAsync();
        await ProcurementUiScenario.RunPhase1Async(fixture.Sandbox, "manager-phase1@test.invalid", unlinkedId);
    }

    [TestMethod]
    public async Task ConcurrentTakeToWorkCreatesExactlyOneCaseAndLink()
    {
        await using Phase1Fixture fixture = await Phase1Fixture.CreateAsync(includeSecondManager: true, includeTeams: false);
        Guid itemId = await fixture.CreateUnlinkedManualAsync();
        ProcurementWorkspace first = new(fixture.Factory, fixture.Access, TimeProvider.System);
        ProcurementWorkspace second = new(fixture.Factory, fixture.Access, TimeProvider.System);
        TakeToWorkResult[] results = await Task.WhenAll(
            first.TakeToWorkAsync(fixture.Manager, new(itemId), "concurrent-a", CancellationToken.None),
            second.TakeToWorkAsync(fixture.SecondManager, new(itemId), "concurrent-b", CancellationToken.None));
        Assert.AreEqual(results[0].CaseId, results[1].CaseId);
        Assert.AreEqual(1, results.Count(item => item.Created));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.PropertyCases.CountAsync()));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.PropertyCaseSourceLinks.CountAsync(item => item.Confirmed)));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.WorkAssignments.CountAsync()));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.WorkflowTransitions.CountAsync(item => item.Action == "TakeWork")));
    }

    [TestMethod]
    public async Task CaseScopesUseResponsibilityAndCatalogRemainsOrganizationShared()
    {
        await using Phase1Fixture fixture = await Phase1Fixture.CreateAsync(includeSecondManager: true, includeTeams: true);
        await fixture.ChangeScopeAsync("manager-phase1@test.invalid", AccessScope.Team, fixture.TeamA);
        await fixture.ChangeScopeAsync("manager2-phase1@test.invalid", AccessScope.Team, fixture.TeamB);
        Guid itemId = await fixture.CreateUnlinkedManualAsync();
        Assert.AreEqual(1, (await fixture.Workspace.ReadIncomingAsync(fixture.SecondManager, new(), CancellationToken.None)).Total,
            "Catalog is organization-shared even for a narrow employee scope.");
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(itemId), "scope", CancellationToken.None);
        Assert.AreEqual(1, (await fixture.Workspace.ReadQueueAsync(fixture.Manager, new(), CancellationToken.None)).Total);
        Assert.AreEqual(0, (await fixture.Workspace.ReadQueueAsync(fixture.SecondManager, new(), CancellationToken.None)).Total);

        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            Listing source = await db.Listings.SingleAsync(item => item.Id == itemId);
            source.DepartmentId = fixture.DepartmentB; source.TeamId = fixture.TeamB;
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(1, (await fixture.Workspace.ReadQueueAsync(fixture.Manager, new(), CancellationToken.None)).Total,
            "Changing legacy Listing routing must not change case access.");
        Assert.AreEqual(0, (await fixture.Workspace.ReadQueueAsync(fixture.SecondManager, new(), CancellationToken.None)).Total);
        await fixture.ChangeScopeAsync("manager-phase1@test.invalid", AccessScope.Department, null);
        Assert.AreEqual(1, (await fixture.Workspace.ReadQueueAsync(fixture.Manager, new(), CancellationToken.None)).Total);
        await fixture.ChangeScopeAsync("manager-phase1@test.invalid", AccessScope.Own, null);
        Assert.AreEqual(1, (await fixture.Workspace.ReadQueueAsync(fixture.Manager, new(), CancellationToken.None)).Total);
        await fixture.ChangeScopeAsync("manager-phase1@test.invalid", AccessScope.AssignedObjects, null);
        Assert.AreEqual(1, (await fixture.Workspace.ReadQueueAsync(fixture.Manager, new(), CancellationToken.None)).Total);
        Assert.AreEqual(taken.CaseId, (await fixture.Workspace.ReadQueueAsync(fixture.Owner, new(), CancellationToken.None)).Items.Single().CaseId);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.ReadCardAsync(fixture.SecondManager, taken.CaseId, CancellationToken.None));
    }

    [TestMethod]
    public async Task LegacyStage1MigrationBackfillsLinksFactsScopeAndProvenance()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext db = sandbox.Context())
        {
            IMigrator migrator = db.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260914201752_ProcurementResponsibilityComment");
        }
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "legacy-owner@test.invalid", "Legacy migration");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId);
        IOrganizationWorkspace organization = bootstrap.GetRequiredService<IOrganizationWorkspace>();
        Subject owner = new(ownerId, true);
        await organization.CreateDepartmentAsync(owner, "Закупка", "legacy", CancellationToken.None);
        OrganizationView structure = await organization.ReadAsync(owner, CancellationToken.None);
        Guid department = structure.Departments.Single().Id;
        Guid managerUser = await ProcurementTestsHelper.InviteAsync(bootstrap, organization, owner, structure, "Legacy manager", "legacy-manager@test.invalid", "ProcurementManager", department, AccessScope.Department);
        Guid organizationId = await bootstrap.GetRequiredService<LandErpDbContext>().Organizations
            .Where(item => item.Name == "Legacy migration").Select(item => item.Id).SingleAsync();
        Guid employeeId = (await organization.ReadAsync(owner, CancellationToken.None)).Employees.Single(item => item.Login == "legacy-manager@test.invalid").Id;
        Guid listingId = Guid.CreateVersion7(), caseId = Guid.CreateVersion7(), assignmentId = Guid.CreateVersion7(), taskId = Guid.CreateVersion7(), timelineId = Guid.CreateVersion7();
        await using (NpgsqlConnection connection = new(sandbox.MigratorConnection))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = new("""
                INSERT INTO catalog.listings
                  (id,organization_id,department_id,team_id,source,external_id,url,title,price,currency,area_square_meters,location,description,seller_name,photos_json,first_observed_at,last_observed_at,recorded_at,changed_at,queue_reason,data_revision,version)
                VALUES (@listing,@org,@department,NULL,'Avito','legacy-1','https://www.avito.ru/legacy','Legacy title',1234567,'RUB',1400,'Legacy place',NULL,NULL,'[]',now(),now(),now(),now(),'Legacy',2,1);
                INSERT INTO workflow.assignments (id,organization_id,object_type,object_id,employee_id,version) VALUES (@assignment,@org,'PropertyCase',@case,@employee,1);
                INSERT INTO workflow.work_tasks (id,organization_id,object_type,object_id,title,employee_id,completed,due_at,recorded_at,version) VALUES (@task,@org,'PropertyCase',@case,'Legacy task',@employee,false,NULL,now(),1);
                INSERT INTO procurement.property_cases (id,organization_id,listing_id,business_number,stage_id,manager_employee_id,assignment_id,work_task_id,pending_approval_id,reviewed_data_revision,recorded_at,version)
                VALUES (@case,@org,@listing,'PC-009999','analysis',@employee,@assignment,@task,NULL,1,now(),4);
                INSERT INTO foundation.business_timeline (id,organization_id,object_type,object_id,actor_employee_id,kind,title,body,target_employee_id,recorded_at,effective_at,due_at)
                VALUES (@timeline,@org,'PropertyCase',@case,@employee,'Note','Legacy history','Preserve me',NULL,now(),NULL,NULL);
                """, connection);
            command.Parameters.AddWithValue("listing", listingId); command.Parameters.AddWithValue("org", organizationId);
            command.Parameters.AddWithValue("department", department); command.Parameters.AddWithValue("case", caseId);
            command.Parameters.AddWithValue("employee", employeeId); command.Parameters.AddWithValue("assignment", assignmentId);
            command.Parameters.AddWithValue("task", taskId); command.Parameters.AddWithValue("timeline", timelineId);
            await command.ExecuteNonQueryAsync();
        }
        await using (LandErpDbContext db = sandbox.Context()) { await db.Database.MigrateAsync(); Assert.IsFalse(db.Database.HasPendingModelChanges()); }
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        await using AsyncServiceScope serviceScope = services.CreateAsyncScope();
        IDbContextFactory<LandErpDbContext> factory = serviceScope.ServiceProvider.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        ProcurementWorkspace workspace = new(factory, serviceScope.ServiceProvider.GetRequiredService<IAccessControl>(), TimeProvider.System);
        CaseCard card = await workspace.ReadCardAsync(new(managerUser, false), caseId, CancellationToken.None);
        Assert.AreEqual("PC-009999", card.Item.BusinessNumber); Assert.AreEqual("Legacy title", card.Item.Title);
        Assert.AreEqual(1, card.Sources.Count); Assert.AreEqual(1234567m, card.Item.Price);
        await using LandErpDbContext verify = sandbox.Context();
        PropertyCase migrated = await verify.PropertyCases.SingleAsync(item => item.Id == caseId);
        Assert.IsNull(migrated.ListingId); Assert.AreEqual(department, migrated.DepartmentId);
        PropertyCaseSourceLink link = await verify.PropertyCaseSourceLinks.SingleAsync();
        Assert.AreEqual("Migration from legacy PropertyCase.ListingId", link.Provenance); Assert.AreEqual(1L, link.ReviewedDataRevision);
        Assert.IsTrue(await verify.BusinessTimeline.AnyAsync(item => item.Id == timelineId && item.Body == "Preserve me"));
        Assert.AreEqual(caseId, await workspace.ResolveLegacyListingAsync(new(managerUser, false), listingId, CancellationToken.None));
    }

    private sealed class Phase1Fixture : IAsyncDisposable
    {
        public PostgresSandbox Sandbox { get; private init; } = default!;
        public ServiceProvider Services { get; private init; } = default!;
        public AsyncServiceScope Scope { get; private init; }
        public IOrganizationWorkspace Organization { get; private init; } = default!;
        public IAccessControl Access { get; private init; } = default!;
        public IDbContextFactory<LandErpDbContext> Factory { get; private init; } = default!;
        public ProcurementWorkspace Workspace { get; private init; } = default!;
        public Subject Owner { get; private init; } = default!;
        public Subject ForeignOwner { get; private init; } = default!;
        public Subject Manager { get; private init; } = default!;
        public Subject SecondManager { get; private init; } = default!;
        public Subject Head { get; private init; } = default!;
        public Guid OrganizationId { get; private init; }
        public Guid DepartmentA { get; private init; }
        public Guid DepartmentB { get; private init; }
        public Guid? TeamA { get; private init; }
        public Guid? TeamB { get; private init; }
        public Guid ManagerEmployeeId => EmployeeId("manager-phase1@test.invalid");

        public static async Task<Phase1Fixture> CreateAsync(bool includeSecondManager, bool includeTeams)
        {
            PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
            await using (LandErpDbContext db = sandbox.Context()) { await db.Database.MigrateAsync(); Assert.IsFalse(db.Database.HasPendingModelChanges()); }
            await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
            Guid ownerId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "owner-phase1@test.invalid", "Phase 1");
            Guid foreignId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "foreign-phase1@test.invalid", "Foreign Phase 1");
            await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerId); await IdentityOrganizationTests.EnableMfaAsync(bootstrap, foreignId);
            IOrganizationWorkspace organization = bootstrap.GetRequiredService<IOrganizationWorkspace>(); Subject owner = new(ownerId, true);
            await organization.CreateDepartmentAsync(owner, "Закупка А", "setup", CancellationToken.None);
            await organization.CreateDepartmentAsync(owner, "Закупка Б", "setup", CancellationToken.None);
            OrganizationView structure = await organization.ReadAsync(owner, CancellationToken.None);
            Guid departmentA = structure.Departments.Single(item => item.Name == "Закупка А").Id;
            Guid departmentB = structure.Departments.Single(item => item.Name == "Закупка Б").Id;
            if (includeTeams)
            {
                await organization.CreateTeamAsync(owner, departmentA, "Команда А", "setup", CancellationToken.None);
                await organization.CreateTeamAsync(owner, departmentB, "Команда Б", "setup", CancellationToken.None);
                structure = await organization.ReadAsync(owner, CancellationToken.None);
            }
            Guid manager = await ProcurementTestsHelper.InviteAsync(bootstrap, organization, owner, structure, "Manager", "manager-phase1@test.invalid", "ProcurementManager", departmentA, AccessScope.Department);
            Guid second = includeSecondManager ? await ProcurementTestsHelper.InviteAsync(bootstrap, organization, owner, structure, "Manager 2", "manager2-phase1@test.invalid", "ProcurementManager", includeTeams ? departmentB : departmentA, AccessScope.Department) : Guid.Empty;
            Guid head = await ProcurementTestsHelper.InviteAsync(bootstrap, organization, owner, structure, "Head", "head-phase1@test.invalid", "ProcurementHead", departmentA, AccessScope.Organization);
            Guid organizationId = await bootstrap.GetRequiredService<LandErpDbContext>().Organizations
                .Where(item => item.Name == "Phase 1").Select(item => item.Id).SingleAsync();
            await sandbox.GrantRuntimeAsync();
            ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
            AsyncServiceScope scope = services.CreateAsyncScope();
            IDbContextFactory<LandErpDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
            IAccessControl access = scope.ServiceProvider.GetRequiredService<IAccessControl>();
            return new()
            {
                Sandbox = sandbox,
                Services = services,
                Scope = scope,
                Organization = scope.ServiceProvider.GetRequiredService<IOrganizationWorkspace>(),
                Access = access,
                Factory = factory,
                Workspace = new(factory, access, TimeProvider.System),
                Owner = owner,
                ForeignOwner = new(foreignId, true),
                Manager = new(manager, false),
                SecondManager = new(second, false),
                Head = new(head, false),
                OrganizationId = organizationId,
                DepartmentA = departmentA,
                DepartmentB = departmentB,
                TeamA = includeTeams ? structure.Teams.Single(item => item.Name == "Команда А").Id : null,
                TeamB = includeTeams ? structure.Teams.Single(item => item.Name == "Команда Б").Id : null
            };
        }

        public Guid EmployeeId(string login) => Organization.ReadAsync(Owner, CancellationToken.None).GetAwaiter().GetResult().Employees.Single(item => item.Login == login).Id;
        public async Task<int> CountAsync(Func<LandErpDbContext, Task<int>> count) { await using LandErpDbContext db = await Factory.CreateDbContextAsync(); return await count(db); }
        public async Task<PropertyCase> ReadCaseAsync(Guid id) { await using LandErpDbContext db = await Factory.CreateDbContextAsync(); return await db.PropertyCases.AsNoTracking().SingleAsync(item => item.Id == id); }
        public Task<Guid> CreateUnlinkedManualAsync() => Workspace.CreateManualAsync(Manager,
            new(CatalogSource.Other, "Ручное предложение", "Москва", null, null, null, null, null, null, "Создано для проверки"), "manual", CancellationToken.None);

        public async Task<(Guid AvitoId, Guid CianId, AgentCredential Agent, CollectionAdministration Administration)> IngestMarketplacePairAsync()
        {
            CollectionAdministration administration = new(Factory, Access, TimeProvider.System);
            CollectorGateway gateway = new(Factory, TimeProvider.System);
            AgentCredential agent = await administration.CreateAgentAsync(Owner, "Phase 1 Collector", "test", CancellationToken.None);
            await gateway.RegisterAsync(agent, new(1, "phase1", [ListingSource.Avito, ListingSource.Cian]), CancellationToken.None);
            foreach (ListingSource source in new[] { ListingSource.Avito, ListingSource.Cian })
            {
                await administration.CreateSearchAsync(Owner, new(agent.AgentId, DepartmentB, TeamB, "Phase1 " + source, source,
                    source == ListingSource.Avito ? "https://www.avito.ru/moskva/zemelnye_uchastki" : "https://www.cian.ru/cat.php?deal_type=sale", 1), "test", CancellationToken.None);
                Guid searchId = (await administration.ReadAsync(Owner, CancellationToken.None)).Searches.Single(item => item.Label == "Phase1 " + source).Id;
                await administration.EnqueueAsync(Owner, searchId, "test", CancellationToken.None);
                CollectionWork work = (await gateway.ClaimAsync(agent, CancellationToken.None))!;
                await gateway.AcceptAsync(agent, new(Guid.CreateVersion7(), work.JobId, work.LeaseId, CollectionOutcome.Success,
                    [new("phase1-" + source, Data(source, source == ListingSource.Avito ? "10001" : "20001", 2_000_000m))], true), CancellationToken.None);
            }
            await using LandErpDbContext db = await Factory.CreateDbContextAsync();
            Guid avito = await db.Listings.Where(item => item.Source == CatalogSource.Avito).Select(item => item.Id).SingleAsync();
            Guid cian = await db.Listings.Where(item => item.Source == CatalogSource.Cian).Select(item => item.Id).SingleAsync();
            return (avito, cian, agent, administration);
        }

        public async Task IngestChangedAvitoAsync(AgentCredential agent, CollectionAdministration administration, decimal price)
        {
            CollectorGateway gateway = new(Factory, TimeProvider.System);
            Guid search = (await administration.ReadAsync(Owner, CancellationToken.None)).Searches.Single(item => item.Label == "Phase1 Avito").Id;
            await administration.EnqueueAsync(Owner, search, "test", CancellationToken.None);
            CollectionWork work = (await gateway.ClaimAsync(agent, CancellationToken.None))!;
            await gateway.AcceptAsync(agent, new(Guid.CreateVersion7(), work.JobId, work.LeaseId, CollectionOutcome.Success,
                [new("phase1-avito-changed", Data(ListingSource.Avito, "10001", price, DateTimeOffset.UtcNow.AddMinutes(1)))], true), CancellationToken.None);
        }

        public async Task<Guid> InsertIndependentCaseAsync(string title)
        {
            Guid id = DataConventions.NewId(); Guid assignmentId = DataConventions.NewId(); Guid taskId = DataConventions.NewId();
            await using LandErpDbContext db = await Factory.CreateDbContextAsync();
            db.WorkAssignments.Add(new() { Id = assignmentId, OrganizationId = OrganizationId, ObjectType = "PropertyCase", ObjectId = id, EmployeeId = ManagerEmployeeId });
            db.WorkTasks.Add(new() { Id = taskId, OrganizationId = OrganizationId, ObjectType = "PropertyCase", ObjectId = id, EmployeeId = ManagerEmployeeId, Title = "Первичный анализ", RecordedAt = DateTimeOffset.UtcNow });
            db.PropertyCases.Add(new()
            {
                Id = id,
                OrganizationId = OrganizationId,
                BusinessNumber = "PC-INDEPENDENT",
                WorkingTitle = title,
                FactsProvenance = "Direct import",
                DepartmentId = DepartmentA,
                ManagerEmployeeId = ManagerEmployeeId,
                AssignmentId = assignmentId,
                WorkTaskId = taskId,
                RecordedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(); return id;
        }

        public async Task ChangeScopeAsync(string login, AccessScope scope, Guid? team)
        {
            EmployeeView employee = (await Organization.ReadAsync(Owner, CancellationToken.None)).Employees.Single(item => item.Login == login);
            await Organization.ChangeAssignmentAsync(Owner, new(employee.Id, employee.DepartmentId, employee.PositionId, team,
                employee.ManagerId, employee.RoleId, scope, employee.Version), "scope", CancellationToken.None);
        }

        private static ListingData Data(ListingSource source, string id, decimal price, DateTimeOffset? observed = null) => new()
        {
            Source = source,
            ExternalId = id,
            Url = source == ListingSource.Avito ? $"https://www.avito.ru/moskva/zemelnye_uchastki/{id}" : $"https://www.cian.ru/sale/suburban/{id}/",
            ObservedAt = observed ?? DateTimeOffset.UtcNow,
            AdapterVersion = "phase1",
            Provenance = "Phase1 test",
            Title = new(FieldPresence.Present, source + " источник"),
            Price = new(FieldPresence.Present, price + " ₽", price),
            AreaSquareMeters = new(FieldPresence.Present, "1500 м²", 1500m),
            Location = new(FieldPresence.Present, "Химки")
        };

        public async ValueTask DisposeAsync() { await Scope.DisposeAsync(); await Services.DisposeAsync(); await Sandbox.DisposeAsync(); }
    }
}

internal static class ProcurementTestsHelper
{
    public static async Task<Guid> InviteAsync(ServiceProvider services, IOrganizationWorkspace workspace, Subject owner,
        OrganizationView structure, string name, string email, string role, Guid department, AccessScope scope)
    {
        InvitationResult invitation = await workspace.InviteAsync(owner, new(name, email, department, null, null, null,
            structure.Roles.Single(item => item.Name == role).Id, scope), "test", CancellationToken.None);
        await using AsyncServiceScope activation = services.CreateAsyncScope();
        await activation.ServiceProvider.GetRequiredService<AccountActivation>().ActivateAsync(invitation.InvitationId,
            invitation.OneTimeToken, "Synthetic1!PasswordForTests", CancellationToken.None);
        return await activation.ServiceProvider.GetRequiredService<LandErpDbContext>().Users.Where(item => item.Email == email).Select(item => item.Id).SingleAsync();
    }
}
