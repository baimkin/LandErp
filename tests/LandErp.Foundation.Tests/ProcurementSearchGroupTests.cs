using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Overview.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Overview;
using LandErp.Server.Components.Procurement;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ProcurementSearchGroupTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static CreateManualPropertyCase Command(Guid? group = null) =>
        new("Ручной участок", "Подольск", null, 3_000_000m, 1000m, "Звонок собственника", Guid.CreateVersion7(), group);

    private static async Task<Guid> Group(ProcurementTests.Phase1Fixture f, string name, Guid? organization = null, bool active = true)
    {
        var id = Guid.CreateVersion7();
        await using var db = f.Sandbox.Context();
        db.SearchGroups.Add(new() { Id = id, OrganizationId = organization ?? f.OrganizationId,
            Name = name, Active = active, RecordedAt = DateTimeOffset.UtcNow });
        db.SearchGroupMarketSettings.Add(new() { Id = Guid.CreateVersion7(), OrganizationId = organization ?? f.OrganizationId,
            SearchGroupId = id, DemandTestPricePerSotka = 110_000m, TargetPurchasePricePerSotka = 90_000m });
        await db.SaveChangesAsync();
        return id;
    }

    private static GroupMarketService Markets(ProcurementTests.Phase1Fixture f, NpgsqlDataSource source) =>
        new(f.Factory, new EmployeeAccessService(f.Factory), new IncomingFilterPresetService(source, new EmployeeAccessService(f.Factory)));

    [TestMethod]
    public async Task CreationExistingCaseChangesAndReplayKeepCatalogAndMedianUntouched()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid a = await Group(f, "Подольск"), b = await Group(f, "Другая зона");
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var markets = Markets(f, source);
        var baseline = await markets.ReadProcurementAsync(f.Manager, a, Ct);
        var command = Command(a);
        var created = await f.Workspace.CreateManualCaseAsync(f.Manager, command, "group-create", Ct);
        Assert.AreEqual(created, await f.Workspace.CreateManualCaseAsync(f.Manager, command, "group-retry", Ct));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.CreateManualCaseAsync(f.Manager,
            command with { SearchGroupId = b }, "group-reused", Ct));
        var card = await f.Workspace.ReadCardAsync(f.Manager, created.CaseId, Ct);
        Assert.AreEqual(a, card.ManualSearchGroup!.Id);
        Assert.AreEqual(baseline, (await markets.ReadCaseAsync(f.Manager, created.CaseId, Ct)).Single());
        Assert.AreEqual(110_000m, baseline!.DemandTestPricePerSotka);
        Assert.AreEqual(90_000m, baseline.TargetPurchasePricePerSotka);
        Assert.IsNull(baseline.MedianPricePerSotka);
        Assert.AreEqual(0, baseline.ParticipantCount);

        var legacyCommand = Command();
        var legacy = await f.Workspace.CreateManualCaseAsync(f.Manager, legacyCommand, "legacy-create", Ct);
        await using (var check = f.Sandbox.Context())
        {
            // Existing persisted replay hashes use the old DTO without SearchGroupId.
            string oldHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
                legacyCommand.Title, legacyCommand.Location, legacyCommand.CadastralNumber, legacyCommand.Price,
                legacyCommand.AreaSquareMeters, legacyCommand.Comment, CommandId = (Guid?)null })));
            using var receipt = JsonDocument.Parse((await check.AuditEvents.SingleAsync(x => x.Id == legacyCommand.CommandId)).Changes);
            Assert.AreEqual(oldHash, receipt.RootElement.GetProperty("CommandReplay").GetProperty("PayloadHash").GetString());
        }
        Assert.AreEqual(legacy, await f.Workspace.CreateManualCaseAsync(f.Manager, legacyCommand, "legacy-retry", Ct));
        card = await f.Workspace.ReadCardAsync(f.Manager, legacy.CaseId, Ct);
        Assert.IsNull(card.ManualSearchGroup);
        foreach (Guid? group in new Guid?[] { a, b, null })
        {
            long version = card.Item.CaseVersion;
            await f.Workspace.SetSearchGroupAsync(f.Manager, new(legacy.CaseId, version, group), "group-change", Ct);
            card = await f.Workspace.ReadCardAsync(f.Manager, legacy.CaseId, Ct);
            Assert.AreEqual(group, card.ManualSearchGroup?.Id);
            Assert.AreEqual(version + 1, card.Item.CaseVersion);
        }
        await f.Workspace.SetSearchGroupAsync(f.Manager, new(legacy.CaseId, card.Item.CaseVersion, null), "group-noop", Ct);
        await using var db = f.Sandbox.Context();
        Assert.AreEqual(3, await db.AuditEvents.CountAsync(x => x.ObjectId == legacy.CaseId && x.Action == "PropertyCaseSearchGroupChanged"));
        Assert.AreEqual(3, await db.BusinessTimeline.CountAsync(x => x.ObjectId == legacy.CaseId && x.Kind == "SearchGroupChanged"));
        Assert.AreEqual(card.Item.CaseVersion, (await db.PropertyCases.SingleAsync(x => x.Id == legacy.CaseId)).Version);
        Assert.AreEqual(0, await db.Listings.CountAsync());
        Assert.AreEqual(0, await db.ListingObservations.CountAsync());
        Assert.AreEqual(0, await db.CollectionJobs.CountAsync());
        Assert.AreEqual(baseline, await markets.ReadProcurementAsync(f.Manager, a, Ct));
    }

    [TestMethod]
    public async Task ExplicitGroupOverridesSourcesAndArchiveNeverEnablesFallback()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        var pair = await f.IngestMarketplacePairAsync();
        Guid a = await Group(f, "Источник"), b = await Group(f, "Подольск");
        await using (var db = f.Sandbox.Context())
        {
            foreach (var search in await db.SearchConfigurations.ToArrayAsync()) search.SearchGroupId = a;
            foreach (var listing in await db.Listings.ToArrayAsync()) listing.IncludeInCalculation = true;
            await db.SaveChangesAsync();
        }
        Guid id = (await f.Workspace.TakeToWorkAsync(f.Manager, new(pair.AvitoId), "take", Ct)).CaseId;
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var markets = Markets(f, source);
        var queues = new LandErp.Infrastructure.Modules.Procurement.ProcurementQueueV2ReadService(f.Factory, TimeProvider.System);
        var original = await markets.ReadProcurementAsync(f.Manager, a, Ct);
        Assert.AreEqual(a, (await markets.ReadCaseAsync(f.Manager, id, Ct)).Single().SearchGroupId);
        foreach (Guid target in new[] { b, a, b })
        {
            var card = await f.Workspace.ReadCardAsync(f.Manager, id, Ct);
            await f.Workspace.SetSearchGroupAsync(f.Manager, new(id, card.Item.CaseVersion, target), "assign", Ct);
            Assert.AreEqual(await markets.ReadProcurementAsync(f.Manager, target, Ct), (await markets.ReadCaseAsync(f.Manager, id, Ct)).Single());
            Assert.AreEqual(1, (await queues.ReadPageAsync(f.Manager, new(SearchGroupId: target), Ct)).Total);
            Assert.AreEqual(0, (await queues.ReadPageAsync(f.Manager, new(SearchGroupId: target == a ? b : a), Ct)).Total);
        }
        await using (var db = f.Sandbox.Context())
        {
            (await db.SearchGroups.SingleAsync(x => x.Id == b)).Active = false;
            await db.SaveChangesAsync();
        }
        var archived = await f.Workspace.ReadCardAsync(f.Manager, id, Ct);
        Assert.AreEqual(b, archived.ManualSearchGroup!.Id);
        Assert.IsFalse(archived.ManualSearchGroup.Active);
        Assert.AreEqual(0, (await markets.ReadCaseAsync(f.Manager, id, Ct)).Count);
        Assert.AreEqual(0, (await queues.ReadPageAsync(f.Manager, new(SearchGroupId: a), Ct)).Total);
        Assert.IsFalse((await markets.ReadProcurementGroupsAsync(f.Manager, Ct)).Any(x => x.Id == b));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.CreateManualCaseAsync(f.Manager, Command(b), "archive-create", Ct));
        await f.Workspace.SetSearchGroupAsync(f.Manager, new(id, archived.Item.CaseVersion, null), "clear", Ct);
        Assert.AreEqual(a, (await markets.ReadCaseAsync(f.Manager, id, Ct)).Single().SearchGroupId);
        Assert.AreEqual(1, (await queues.ReadPageAsync(f.Manager, new(SearchGroupId: a), Ct)).Total);
        Assert.AreEqual(original, await markets.ReadProcurementAsync(f.Manager, a, Ct));
    }

    [TestMethod]
    public async Task ServerRejectsForeignArchivedReadOnlyAndStaleWritesAndConcurrentChangeHasOneWinner()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        Guid a = await Group(f, "Подольск"), b = await Group(f, "Другая зона"), archived = await Group(f, "Архив", active: false);
        Guid foreignOrg;
        await using (var db = f.Sandbox.Context()) foreignOrg = await db.Organizations.Where(x => x.Id != f.OrganizationId).Select(x => x.Id).FirstAsync();
        Guid foreign = await Group(f, "Чужая зона", foreignOrg);
        Guid id = (await f.Workspace.CreateManualCaseAsync(f.Manager, Command(), "create", Ct)).CaseId;
        var card = await f.Workspace.ReadCardAsync(f.Manager, id, Ct);
        foreach (Guid invalid in new[] { foreign, Guid.CreateVersion7() })
        {
            await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.SetSearchGroupAsync(f.Manager, new(id, card.Item.CaseVersion, invalid), "forbidden", Ct));
            await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.CreateManualCaseAsync(f.Manager, Command(invalid), "forbidden-create", Ct));
        }
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Workspace.SetSearchGroupAsync(f.Manager, new(id, card.Item.CaseVersion, archived), "archived", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.SetSearchGroupAsync(f.ForeignOwner, new(id, card.Item.CaseVersion, a), "foreign-case", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.SetSearchGroupAsync(f.SecondManager, new(id, card.Item.CaseVersion, a), "hidden-case", Ct));
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, ProcurementTestsHelper.ProcurementManagerAccess(AccessScope.Department) with { ProcurementAccess = ProcurementAccessLevel.Read });
        Assert.IsFalse((await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).CanManageDossier);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.SetSearchGroupAsync(f.Manager, new(id, card.Item.CaseVersion, a), "readonly", Ct));
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, ProcurementTestsHelper.ProcurementManagerAccess(AccessScope.Department));
        async Task<bool> Assign(Guid group)
        {
            try { await f.Workspace.SetSearchGroupAsync(f.Manager, new(id, card.Item.CaseVersion, group), "race", Ct); return true; }
            catch (DbUpdateConcurrencyException) { return false; }
        }
        var outcomes = await Task.WhenAll(Assign(a), Assign(b));
        Assert.AreEqual(1, outcomes.Count(x => x));
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => f.Workspace.SetSearchGroupAsync(f.Manager, new(id, card.Item.CaseVersion, null), "stale", Ct));
        await using var check = f.Sandbox.Context();
        Assert.AreEqual(1, await check.PropertyCases.CountAsync());
        Assert.AreEqual(1, await check.AuditEvents.CountAsync(x => x.ObjectId == id && x.Action == "PropertyCaseSearchGroupChanged"));
    }

    [TestMethod]
    public async Task MigrationUpgradesPreviousSchemaPreservingExistingCaseAndRuntimeCanAssign()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = (await f.Workspace.CreateManualCaseAsync(f.Manager, Command(), "before-migration", Ct)).CaseId;
        var before = await f.Workspace.ReadCardAsync(f.Manager, id, Ct);
        await using (var db = f.Sandbox.Context())
        {
            // Only this owned disposable database is moved to the previous schema.
            await db.GetService<IMigrator>().MigrateAsync("20261009094850_ListingComments");
            await db.Database.MigrateAsync();
            Assert.IsFalse(db.Database.HasPendingModelChanges());
        }
        var after = await f.Workspace.ReadCardAsync(f.Manager, id, Ct);
        Assert.AreEqual(before.Item.BusinessNumber, after.Item.BusinessNumber);
        Assert.AreEqual(before.Item.Title, after.Item.Title);
        Assert.AreEqual(before.Item.CaseVersion, after.Item.CaseVersion);
        Assert.AreEqual(before.Item.Price, after.Item.Price);
        Assert.AreEqual(before.Item.Area, after.Item.Area);
        Assert.IsNull(after.ManualSearchGroup);
        Guid group = await Group(f, "Подольск");
        await f.Workspace.SetSearchGroupAsync(f.Manager, new(id, after.Item.CaseVersion, group), "after-migration", Ct);
        Assert.AreEqual(group, (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).ManualSearchGroup!.Id);
    }

    [TestMethod]
    public async Task RealPickerEditsExistingCaseClearsAndShowsArchivedAssignmentWithoutBrowser()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid group = await Group(f, "Подольск");
        Guid id = (await f.Workspace.CreateManualCaseAsync(f.Manager, Command(), "picker-create", Ct)).CaseId;
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = new PickerActivator();
        var registrations = new ServiceCollection().AddLogging();
        registrations.AddSingleton<IComponentActivator>(activator);
        registrations.AddSingleton<IProcurementWorkspace>(f.Workspace);
        registrations.AddSingleton<IGroupMarketService>(Markets(f, source));
        await using var services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            ParameterView Parameters(CaseCard card) => ParameterView.FromDictionary(new Dictionary<string, object?> {
                [nameof(CaseSearchGroupPicker.Card)] = card, [nameof(CaseSearchGroupPicker.Subject)] = f.Manager,
                [nameof(CaseSearchGroupPicker.Saved)] = EventCallback.Factory.Create(this, async () =>
                    await activator.Picker!.SetParametersAsync(ParametersWithoutCallback(await f.Workspace.ReadCardAsync(f.Manager, id, Ct)))) });
            ParameterView ParametersWithoutCallback(CaseCard card) => ParameterView.FromDictionary(new Dictionary<string, object?> {
                [nameof(CaseSearchGroupPicker.Card)] = card });
            var rendered = await renderer.RenderComponentAsync<CaseSearchGroupPicker>(Parameters(await f.Workspace.ReadCardAsync(f.Manager, id, Ct)));
            var picker = activator.Picker!;
            await Invoke(picker, "EditAsync");
            Render(picker);
            StringAssert.Contains(WebUtility.HtmlDecode(rendered.ToHtmlString()), "Подольск");
            typeof(CaseSearchGroupPicker).GetField("draft", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(picker, group);
            await Invoke(picker, "SaveAsync");
            Render(picker);
            StringAssert.Contains(WebUtility.HtmlDecode(rendered.ToHtmlString()), "Подольск");
            Assert.AreEqual(group, (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).ManualSearchGroup!.Id);
            await using (var db = f.Sandbox.Context()) { (await db.SearchGroups.SingleAsync(x => x.Id == group)).Active = false; await db.SaveChangesAsync(); }
            await picker.SetParametersAsync(Parameters(await f.Workspace.ReadCardAsync(f.Manager, id, Ct)));
            StringAssert.Contains(WebUtility.HtmlDecode(rendered.ToHtmlString()), "Группа в архиве");
            await Invoke(picker, "EditAsync");
            typeof(CaseSearchGroupPicker).GetField("draft", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(picker, null);
            await Invoke(picker, "SaveAsync");
            Assert.IsNull((await f.Workspace.ReadCardAsync(f.Manager, id, Ct)).ManualSearchGroup);
            var readOnly = (await f.Workspace.ReadCardAsync(f.Manager, id, Ct)) with { CanManageDossier = false };
            await picker.SetParametersAsync(Parameters(readOnly));
            Assert.IsFalse(WebUtility.HtmlDecode(rendered.ToHtmlString()).Contains("Изменить", StringComparison.Ordinal));
        });
    }

    private static async Task Invoke(object component, string name) =>
        await (Task)component.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null)!;
    private static void Render(object component) => typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
    private sealed class PickerActivator : IComponentActivator
    {
        public CaseSearchGroupPicker? Picker { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is CaseSearchGroupPicker picker) Picker = picker;
            return component;
        }
    }
}
