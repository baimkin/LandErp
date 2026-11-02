using System.Net;
using System.Reflection;
using System.Security.Claims;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Overview.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Overview;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Server.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass, TestCategory("PostgreSQL")]
public sealed class ProcurementPipelineSearchTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static KanbanWorkspace Board(ProcurementTests.Phase1Fixture f) =>
        new(f.Factory, new EmployeeAccessService(f.Factory), TimeProvider.System);
    private static ProcurementQueueV2ReadService Table(ProcurementTests.Phase1Fixture f) => new(f.Factory, TimeProvider.System);
    private static async Task<Guid> Create(ProcurementTests.Phase1Fixture f, Subject actor, string title, string location) =>
        (await f.Workspace.CreateManualCaseAsync(actor,
            new(title, location, null, 100m, 900m, "Тест поиска", Guid.CreateVersion7()), "pipeline-search", Ct)).CaseId;

    [TestMethod]
    public async Task BothViewsSearchWorkingFactsContactsAndConfirmedSourcesWithIdenticalSemantics()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var pair = await f.IngestMarketplacePairAsync();
        Guid sourceCase = (await f.Workspace.TakeToWorkAsync(f.Manager, new(pair.AvitoId), "search-source", Ct)).CaseId;
        Guid direct = await Create(f, f.Manager, "Участок у озера", "Химки, Фирсановка");
        await Create(f, f.Manager, "Другой объект", "Москва");
        var card = await f.Workspace.ReadCardAsync(f.Manager, direct, Ct);
        await f.Workspace.AddNegotiationWithIdAsync(f.Manager,
            new(direct, card.Item.CaseVersion, null, null, null, "Телефон", "Контакт Светлана",
                "Обсудили участок", "", "", "", null, DateTimeOffset.UtcNow), "search-contact", Ct);
        await using (var db = f.Sandbox.Context())
        {
            var item = await db.PropertyCases.SingleAsync(x => x.Id == direct);
            item.BusinessNumber = "PC-SEARCH-001";
            item.CadastralNumber = "50:10:1234567:89";
            (await db.PropertyCases.SingleAsync(x => x.Id == sourceCase)).WorkingLocation = "Тверь";
            var source = await db.Listings.SingleAsync(x => x.Id == pair.AvitoId);
            source.Title = "Исходное предложение у реки";
            source.SellerName = "Продавец Валентин";
            await db.SaveChangesAsync();
        }
        var board = Board(f);
        Guid pipeline = (await board.ReadConfigurationAsync(f.Owner, Ct)).Pipelines.Single().Id;
        (string Text, Guid Expected)[] terms = [
            ("  хИмКи  ", direct), ("озера", direct), ("pc-search-001", direct),
            ("50:10:1234567:89", direct), (direct.ToString(), direct), ("светлана", direct),
            ("10001", sourceCase), ("исходное предложение", sourceCase),
            ("валентин", sourceCase), (pair.AvitoId.ToString(), sourceCase)];
        foreach (var term in terms)
        {
            var b = await board.ReadBoardAsync(f.Manager, pipeline, null, term.Text, null, Ct);
            var t = await Table(f).ReadPageAsync(f.Manager, new(Text: term.Text, PipelineId: pipeline), Ct);
            var general = await Table(f).ReadPageAsync(f.Manager, new(Text: term.Text), Ct);
            Assert.AreEqual(1, b.Total, term.Text);
            Assert.AreEqual(term.Expected, b.Columns.SelectMany(c => c.Cards).Single().Membership.PropertyCaseId, term.Text);
            Assert.AreEqual(term.Expected, t.Items.Single().CaseId, term.Text);
            Assert.AreEqual(term.Expected, general.Items.Single().CaseId, term.Text);
        }
        await using (var db = f.Sandbox.Context())
        {
            (await db.PropertyCaseSourceLinks.SingleAsync(x => x.PropertyCaseId == sourceCase)).Confirmed = false;
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(0, (await board.ReadBoardAsync(f.Manager, pipeline, null, "валентин", null, Ct)).Total);
        Assert.AreEqual(0, (await Table(f).ReadPageAsync(f.Manager, new(Text: "валентин", PipelineId: pipeline), Ct)).Total);
    }

    [TestMethod]
    public async Task SearchIntersectsManagerDepartmentOrganizationAndPipelineVisibility()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        Guid first = await Create(f, f.Manager, "Первый", "Химки");
        Guid second = await Create(f, f.SecondManager, "Второй", "Химки");
        var board = Board(f);
        Guid pipeline = (await board.ReadConfigurationAsync(f.Owner, Ct)).Pipelines.Single().Id;
        Guid secondEmployee = f.EmployeeId("manager2-phase1@test.invalid");
        Assert.AreEqual(2, (await board.ReadBoardAsync(f.Owner, pipeline, null, "Химки", null, Ct)).Total);
        var filtered = await board.ReadBoardAsync(f.Owner, pipeline, secondEmployee, "Химки", null, Ct);
        Assert.AreEqual(second, filtered.Columns.SelectMany(c => c.Cards).Single().Membership.PropertyCaseId);
        var table = await Table(f).ReadPageAsync(f.Owner, new(Text: "Химки", AssigneeId: secondEmployee, PipelineId: pipeline), Ct);
        Assert.AreEqual(second, table.Items.Single().CaseId);
        Assert.AreEqual(first, (await board.ReadBoardAsync(f.Manager, pipeline, null, "Химки", null, Ct))
            .Columns.SelectMany(c => c.Cards).Single().Membership.PropertyCaseId);
        Assert.AreEqual(0, (await board.ReadBoardAsync(f.Manager, pipeline, secondEmployee, "Химки", null, Ct)).Total);
        Assert.AreEqual(0, (await Table(f).ReadPageAsync(f.Manager, new(Text: second.ToString(), PipelineId: pipeline), Ct)).Total);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => board.ReadBoardAsync(f.ForeignOwner, pipeline, null, "Химки", null, Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => Table(f).ReadPageAsync(f.ForeignOwner, new(Text: "Химки", PipelineId: pipeline), Ct));
        await using (var db = f.Sandbox.Context())
        {
            var membership = await db.KanbanMemberships.SingleAsync(x => x.PropertyCaseId == second);
            membership.TransferredAt = DateTimeOffset.UtcNow;
            membership.StageExitedAt = membership.TransferredAt;
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(1, (await board.ReadBoardAsync(f.Owner, pipeline, null, "Химки", null, Ct)).Total);
        Assert.AreEqual(1, (await Table(f).ReadPageAsync(f.Owner, new(Text: "Химки", PipelineId: pipeline), Ct)).Total);
    }

    [TestMethod]
    public async Task SearchRunsBeforeColumnPagingAndTotalsAndBlankRestoresFullSet()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPages(f);
        var board = Board(f);
        Guid pipeline = (await board.ReadConfigurationAsync(f.Owner, Ct)).Pipelines.Single().Id;
        var all = await board.ReadBoardAsync(f.Manager, pipeline, null, Ct);
        Assert.AreEqual(54, all.Total);
        var found = await board.ReadBoardAsync(f.Manager, pipeline, null, "химки", null, Ct);
        var column = found.Columns.Single(c => c.Total > 0);
        Assert.AreEqual(52, found.Total);
        Assert.AreEqual(50, column.Cards.Count);
        Assert.AreEqual(5100m, column.KnownPriceTotals["RUB"]);
        Assert.AreEqual(1, column.WithoutPrice);
        Assert.IsNotNull(column.NextCursor);
        var next = await board.ReadBoardAsync(f.Manager, pipeline, null, "химки",
            new Dictionary<Guid, string> { [column.Stage.Id] = column.NextCursor }, Ct);
        var nextColumn = next.Columns.Single(c => c.Stage.Id == column.Stage.Id);
        Assert.AreEqual(2, nextColumn.Cards.Count);
        Assert.AreEqual(52, column.Cards.Concat(nextColumn.Cards).Select(c => c.Membership.Id).Distinct().Count());
        var table = await Table(f).ReadPageAsync(f.Manager, new(Text: "Химки", PipelineId: pipeline, Size: 100), Ct);
        CollectionAssert.AreEquivalent(table.Items.Select(x => x.CaseId).ToArray(),
            column.Cards.Concat(nextColumn.Cards).Select(c => c.Membership.PropertyCaseId).ToArray());
        // A unique title in a card outside the first unfiltered slice must still be found.
        var last = table.Items.MaxBy(x => x.BusinessNumber)!;
        Assert.IsFalse(all.Columns.SelectMany(c => c.Cards).Any(c => c.Membership.PropertyCaseId == last.CaseId));
        Assert.AreEqual(last.CaseId, (await board.ReadBoardAsync(f.Manager, pipeline, null, last.BusinessNumber, null, Ct))
            .Columns.SelectMany(c => c.Cards).Single().Membership.PropertyCaseId);
        var empty = await board.ReadBoardAsync(f.Manager, pipeline, null, "Несуществующая локация", null, Ct);
        Assert.AreEqual(0, empty.Total);
        Assert.IsTrue(empty.Columns.All(c => c.Total == 0 && c.Cards.Count == 0 && c.NextCursor == null && c.KnownPriceTotals.Count == 0));
        Assert.AreEqual(54, (await board.ReadBoardAsync(f.Manager, pipeline, null, "   ", null, Ct)).Total);
        Assert.AreEqual(54, (await Table(f).ReadPageAsync(f.Manager, new(Text: "   ", PipelineId: pipeline), Ct)).Total);
        await using (var db = f.Sandbox.Context())
        {
            var hiddenStage = await db.KanbanStages.SingleAsync(x => x.PipelineId == pipeline && x.IsRejectionTarget);
            hiddenStage.IsHiddenOnBoard = true;
            (await db.KanbanMemberships.SingleAsync(x => x.PropertyCaseId == last.CaseId)).StageId = hiddenStage.Id;
            await db.SaveChangesAsync();
        }
        var hidden = await board.ReadBoardAsync(f.Manager, pipeline, null, last.BusinessNumber, null, Ct);
        Assert.AreEqual(1, hidden.Total);
        Assert.AreEqual(1, hidden.HiddenCount);
        Assert.AreEqual(0, (await board.ReadBoardAsync(f.Manager, pipeline, null, "Нет совпадений", null, Ct)).HiddenCount);
    }

    [TestMethod]
    public async Task ActualPageDebouncesResetsSlicesAndKeepsQueryBetweenBoardAndTable()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPages(f);
        var board = Board(f);
        Guid pipeline = (await board.ReadConfigurationAsync(f.Owner, Ct)).Pipelines.Single().Id;
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var access = new EmployeeAccessService(f.Factory);
        var activator = new PageActivator();
        var registrations = new ServiceCollection().AddLogging();
        registrations.AddSingleton<IComponentActivator>(activator);
        registrations.AddSingleton<IJSRuntime, NoBrowserJs>();
        registrations.AddSingleton<NavigationManager, TestNavigation>();
        registrations.AddSingleton<AuthenticationStateProvider>(new TestAuthentication(f.Manager));
        registrations.AddSingleton<IProcurementWorkspace>(f.Workspace);
        registrations.AddSingleton<IProcurementQueueV2ReadService>(Table(f));
        registrations.AddSingleton<IKanbanWorkspace>(board);
        registrations.AddSingleton<IGroupMarketService>(new GroupMarketService(f.Factory, access, new IncomingFilterPresetService(source, access)));
        await using var services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            ParameterView Parameters(string view) => ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["PipelineId"] = pipeline, ["View"] = view });
            var rendered = await renderer.RenderComponentAsync<ProcurementQueueV2>(Parameters("kanban"));
            var page = activator.Page!;
            Assert.AreEqual(54, Data<ProcurementQueueV2Page>(page, "queuePage").Total);
            StringAssert.Contains(WebUtility.HtmlDecode(rendered.ToHtmlString()), "Поиск объектов");
            var original = Data<KanbanBoardView>(page, "kanbanBoard").Columns.Single(c => c.Total > 0);
            await Invoke(page, "MoreKanban", original);
            Assert.AreEqual(54, Data<KanbanBoardView>(page, "kanbanBoard").Columns.Sum(c => c.Cards.Count));
            // Start a second input before the first debounce completes.
            Task superseded = Invoke(page, "PipelineSearchInputAsync", new ChangeEventArgs { Value = "Москва" });
            Task latest = Invoke(page, "PipelineSearchInputAsync", new ChangeEventArgs { Value = "  хИмКи  " });
            await Task.WhenAll(superseded, latest);
            var found = Data<KanbanBoardView>(page, "kanbanBoard");
            Assert.AreEqual(52, found.Total);
            Assert.AreEqual(50, found.Columns.Sum(c => c.Cards.Count), "A new search discards loaded slices.");
            Assert.AreEqual(52, Data<ProcurementQueueV2Page>(page, "queuePage").Total);
            await Invoke(page, "MoreKanban", found.Columns.Single(c => c.Total > 0));
            Assert.AreEqual(52, Data<KanbanBoardView>(page, "kanbanBoard").Columns.Sum(c => c.Cards.Count));
            await page.SetParametersAsync(Parameters("table"));
            Assert.AreEqual(52, Data<ProcurementQueueV2Page>(page, "queuePage").Total);
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            StringAssert.Contains(WebUtility.HtmlDecode(rendered.ToHtmlString()), "pipeline-search");
            Task pending = Invoke(page, "PipelineSearchInputAsync", new ChangeEventArgs { Value = "Нет совпадений" });
            await Invoke(page, "SearchKeyDownAsync", new KeyboardEventArgs { Key = "Enter" });
            await pending;
            Assert.AreEqual(0, Data<ProcurementQueueV2Page>(page, "queuePage").Total);
            await Invoke(page, "ClearPipelineSearchAsync");
            Assert.AreEqual(54, Data<ProcurementQueueV2Page>(page, "queuePage").Total);
            await page.SetParametersAsync(Parameters("kanban"));
            Assert.AreEqual(54, Data<KanbanBoardView>(page, "kanbanBoard").Total);
            Task disposed = Invoke(page, "PipelineSearchInputAsync", new ChangeEventArgs { Value = "Москва" });
            page.Dispose();
            await disposed;
            Assert.AreEqual(54, Data<KanbanBoardView>(page, "kanbanBoard").Total);
        });
    }

    private static async Task SeedPages(ProcurementTests.Phase1Fixture f)
    {
        await using var db = f.Sandbox.Context();
        for (int i = 0; i < 54; i++)
        {
            Guid id = Guid.CreateVersion7(), assignment = Guid.CreateVersion7(), task = Guid.CreateVersion7();
            db.WorkAssignments.Add(new() { Id = assignment, OrganizationId = f.OrganizationId,
                ObjectType = "PropertyCase", ObjectId = id, EmployeeId = f.ManagerEmployeeId });
            db.WorkTasks.Add(new() { Id = task, OrganizationId = f.OrganizationId, ObjectType = "PropertyCase",
                ObjectId = id, EmployeeId = f.ManagerEmployeeId, Title = "Первичный анализ", RecordedAt = DateTimeOffset.UtcNow });
            db.PropertyCases.Add(new() { Id = id, OrganizationId = f.OrganizationId,
                BusinessNumber = $"PC-SEARCH-{i:D3}", WorkingTitle = $"Тестовый участок {i:D3}",
                WorkingLocation = i < 52 ? "Химки" : "Москва", WorkingPrice = i == 51 ? null : 100m,
                FactsProvenance = "Search test fixture", DepartmentId = f.DepartmentA,
                ManagerEmployeeId = f.ManagerEmployeeId, AssignmentId = assignment, WorkTaskId = task,
                RecordedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        await KanbanProvisioning.InitializeAsync(db);
    }
    private static T Data<T>(object page, string field) => (T)page.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
    private static async Task Invoke(object page, string method, params object[] args) =>
        await (Task)page.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args)!;
    private sealed class PageActivator : IComponentActivator
    {
        public ProcurementQueueV2? Page { get; private set; }
        public IComponent CreateInstance(Type type)
        {
            var result = (IComponent)Activator.CreateInstance(type)!;
            if (result is ProcurementQueueV2 page) Page = page;
            return result;
        }
    }
    private sealed class TestAuthentication(Subject subject) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject.UserId.ToString()),
                new Claim("amr", subject.MultiFactorAuthenticated ? "mfa" : "pwd")], "test"))));
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://localhost/", "https://localhost/procurement?view=kanban");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Navigation is outside this test.");
    }
    private sealed class NoBrowserJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => throw new JSDisconnectedException("No browser is used.");
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }
}
