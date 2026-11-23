using System.Reflection;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Overview.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Server.Components.Pages;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

public sealed partial class CatalogBulkArchiveUiTests
{
    [TestMethod]
    public async Task QuickDismissRefillsOnlyOneRowAndNextPageDoesNotSkipWhileKeepingOtherSelection()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPaginationAsync(f, 60);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator();
        var probes = new DismissProbes(f, source);
        await using var services = PageServices(f, source, activator, f.Manager, probes.Register);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        Guid dismissed = Guid.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!;
            Guid[] original = PageData(page).Items.Select(item => item.Id).ToArray();
            dismissed = original[24];
            await InvokeAsync(page, "SelectCalculationItem", PageData(page).Items[0], true);
            await InvokeAsync(page, "OpenDetailAsync", dismissed);
            await InvokeAsync(page, "OpenClassification", CatalogDisposition.Dismissed);
            SetDismissField(page, "classificationReason", "Не устраивает подъезд");
            probes.BlockFull = true;
            await InvokeAsync(page, "ClassifyAsync");
            Assert.AreEqual(59, PageData(page).Total);
            Assert.AreEqual(25, PageData(page).Items.Count);
            Assert.IsFalse(PageData(page).Items.Any(item => item.Id == dismissed));
            Assert.AreEqual(1, Get(page, "CalculationCount"));
            CollectionAssert.AreEqual(original[..24], PageData(page).Items.Take(24).Select(item => item.Id).ToArray());
            Assert.AreEqual(PageData(page).Items[24].Id, ((CatalogItemDetail)Get(page, "detail")!).Item.Id);
            Assert.AreEqual(1, probes.Windows.Count);
            Assert.AreEqual(24, probes.Windows[0].Base.Offset);
            Assert.AreEqual(1, probes.Windows[0].Base.Size);
            Assert.AreEqual(false, Get(page, "classificationOpen"));
            var seen = PageData(page).Items.Select(item => item.Id).ToList();
            probes.BlockFull = false;
            await InvokeAsync(page, "GoToPageAsync", 1);
            seen.AddRange(PageData(page).Items.Select(item => item.Id));
            await InvokeAsync(page, "GoToPageAsync", 2);
            seen.AddRange(PageData(page).Items.Select(item => item.Id));
            Assert.AreEqual(59, seen.Distinct().Count());
            Assert.AreEqual(59, seen.Count);
            Assert.AreEqual("51–59 из 59", page.GetType().GetMethod("PageRangeText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null));
        });
        var saved = await f.Workspace.ReadItemAsync(f.Manager, dismissed, default);
        Assert.AreEqual(CatalogDisposition.Dismissed, saved.Item.Disposition);
        await using var db = f.Sandbox.Context();
        Assert.AreEqual(1, await db.CatalogEvents.CountAsync(e => e.CatalogItemId == dismissed && e.Kind == CatalogEventKind.Classified));
    }

    [TestMethod]
    public async Task QuickDismissClampsEmptyLastPageAndKeepsZeroResultRangeValid()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPaginationAsync(f, 26);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator();
        var probes = new DismissProbes(f, source);
        await using var services = PageServices(f, source, activator, f.Manager, probes.Register);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!;
            await InvokeAsync(page, "GoToPageAsync", 1);
            await InvokeAsync(page, "OpenDetailAsync", PageData(page).Items.Single().Id);
            await InvokeAsync(page, "OpenClassification", CatalogDisposition.Dismissed);
            probes.BlockFull = true;
            await InvokeAsync(page, "ClassifyAsync");
            Assert.AreEqual(0, Get(page, "page"));
            Assert.AreEqual(25, PageData(page).Total);
            Assert.AreEqual(25, PageData(page).Items.Count);
            Assert.IsNull(Get(page, "detail"), "No next listing after the end of the dataset.");
            Assert.AreEqual(0, probes.Windows.Single().Base.Offset);
            Assert.AreEqual(25, probes.Windows.Single().Base.Size);
            // Shrink the real remaining selection to one, then verify the final dismissal.
            probes.BlockFull = false;
            var filter = Get(page, "filter")!;
            filter.GetType().GetProperty("Text")!.SetValue(filter, "Пагинация тест 24");
            await InvokeAsync(page, "ApplyFilterAsync");
            await InvokeAsync(page, "OpenDetailAsync", PageData(page).Items.Single().Id);
            await InvokeAsync(page, "OpenClassification", CatalogDisposition.Dismissed);
            probes.Windows.Clear(); probes.BlockFull = true;
            await InvokeAsync(page, "ClassifyAsync");
            Assert.AreEqual(0, PageData(page).Total);
            Assert.AreEqual(0, PageData(page).Items.Count);
            Assert.AreEqual(0, Get(page, "page"));
            Assert.AreEqual(0, probes.Windows.Count);
            Assert.IsNull(Get(page, "detail"));
        });
    }

    [TestMethod]
    public async Task QuickDismissInNewSliceAccountsForReviewOfNextWithoutLosingPagination()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPaginationAsync(f, 30);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator();
        var probes = new DismissProbes(f, source);
        await using var services = PageServices(f, source, activator, f.Manager, probes.Register);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!;
            await InvokeAsync(page, "SelectPresetAsync", IncomingCatalogPreset.New);
            Guid first = PageData(page).Items[0].Id;
            await InvokeAsync(page, "OpenDetailAsync", first);
            Assert.AreEqual(29, PageData(page).Total, "Opening an unreviewed listing removes it from New.");
            probes.Windows.Clear(); // Opening now refills New locally too; measure only the subsequent dismissal.
            Guid next = PageData(page).Items[0].Id;
            await InvokeAsync(page, "OpenClassification", CatalogDisposition.Dismissed);
            probes.BlockFull = true;
            await InvokeAsync(page, "ClassifyAsync");
            Assert.AreEqual(next, ((CatalogItemDetail)Get(page, "detail")!).Item.Id);
            Assert.AreEqual(28, PageData(page).Total);
            Assert.AreEqual(25, PageData(page).Items.Count);
            Assert.IsFalse(PageData(page).Items.Any(item => item.Id == first || item.Id == next));
            Assert.AreEqual(1, probes.Windows.Count);
            Assert.AreEqual(24, probes.Windows[0].Base.Offset);
            probes.BlockFull = false;
            var expected = await probes.Reader.ReadAsync(f.Manager, (IncomingCatalogReadFilter)Get(page, "renderedCalculationFilter")!, default);
            CollectionAssert.AreEqual(expected.Items.Select(item => item.Id).ToArray(), PageData(page).Items.Select(item => item.Id).ToArray());
        });
    }

    [TestMethod]
    public async Task QuickDismissRejectedOrUnknownDoesNotRemoveRowCloseDialogOrLoseReason()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPaginationAsync(f, 27);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator();
        var probes = new DismissProbes(f, source);
        await using var services = PageServices(f, source, activator, f.Manager, probes.Register);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!;
            Guid id = PageData(page).Items[0].Id;
            await InvokeAsync(page, "OpenDetailAsync", id);
            await InvokeAsync(page, "OpenClassification", CatalogDisposition.Dismissed);
            SetDismissField(page, "classificationReason", "Мой подробный комментарий");
            probes.BlockFull = true;
            foreach (Exception failure in new Exception[] { new ArgumentException("Проверьте причину"), new IOException("Acknowledgement unknown") })
            {
                probes.CommandFailure = failure;
                await InvokeAsync(page, "ClassifyAsync");
                Assert.AreEqual(27, PageData(page).Total);
                Assert.IsTrue(PageData(page).Items.Any(item => item.Id == id));
                Assert.AreEqual(true, Get(page, "classificationOpen"));
                Assert.AreEqual("Мой подробный комментарий", Get(page, "classificationReason"));
                Assert.AreEqual(0, probes.Windows.Count);
                Assert.AreEqual(id, ((CatalogItemDetail)Get(page, "detail")!).Item.Id);
            }
        });
    }

    private static void SetDismissField(IncomingCatalogV2 page, string name, object value) =>
        page.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    [TestMethod]
    public async Task QuickDismissCommittedButRefillFailedCanRecoverByReadWithoutRepeatingDecision()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPaginationAsync(f, 27);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator();
        var probes = new DismissProbes(f, source);
        await using var services = PageServices(f, source, activator, f.Manager, probes.Register);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        Guid id = Guid.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!;
            id = PageData(page).Items[0].Id;
            await InvokeAsync(page, "OpenDetailAsync", id);
            await InvokeAsync(page, "OpenClassification", CatalogDisposition.Dismissed);
            probes.BlockFull = true; probes.FailWindow = true;
            await InvokeAsync(page, "ClassifyAsync");
            Assert.AreEqual(false, Get(page, "classificationOpen"));
            Assert.AreEqual(26, PageData(page).Total);
            Assert.AreEqual(24, PageData(page).Items.Count);
            Assert.IsFalse(PageData(page).Items.Any(item => item.Id == id));
            Assert.IsNull(Get(page, "detail"));
            probes.BlockFull = false; probes.FailWindow = false;
            await InvokeAsync(page, "ReloadAsync");
            Assert.AreEqual(25, PageData(page).Items.Count);
            Assert.AreEqual(26, PageData(page).Total);
        });
        await using var db = f.Sandbox.Context();
        Assert.AreEqual(1, await db.CatalogEvents.CountAsync(e => e.CatalogItemId == id && e.Kind == CatalogEventKind.Classified));
    }

    private sealed class DismissProbes
    {
        public bool BlockFull { get; set; }
        public Exception? CommandFailure { get; set; }
        public bool FailWindow { get; set; }
        public List<IncomingCatalogReadFilter> Windows { get; } = [];
        public IncomingCatalogReadService Reader { get; }
        private readonly ProcurementTests.Phase1Fixture fixture;
        private readonly IGroupMarketService market;
        public DismissProbes(ProcurementTests.Phase1Fixture f, NpgsqlDataSource source)
        {
            fixture = f;
            var access = new EmployeeAccessService(f.Factory);
            var presets = new IncomingFilterPresetService(source, access);
            Reader = new IncomingCatalogReadService(f.Factory, access, f.Workspace, TimeProvider.System, presets);
            market = new LandErp.Infrastructure.Modules.Overview.GroupMarketService(f.Factory, access, presets);
        }
        public void Register(IServiceCollection services)
        {
            services.AddSingleton(DismissProxy<IIncomingCatalogReadService>.Wrap(Reader, (method, args) =>
            {
                if (method is nameof(IIncomingCatalogReadService.ReadAsync) or nameof(IIncomingCatalogReadService.ReadPageAsync)
                    or nameof(IIncomingCatalogReadService.ReadInformationAsync) && BlockFull)
                    throw new InvalidOperationException("Full list read forbidden after dismissal");
                if (method == nameof(IIncomingCatalogReadService.ReadWindowAsync))
                {
                    Windows.Add((IncomingCatalogReadFilter)args![1]!);
                    if (FailWindow) throw new IOException("Refill unavailable");
                }
            }));
            services.AddSingleton(DismissProxy<IGroupMarketService>.Wrap(market, (_, _) =>
            { if (BlockFull) throw new InvalidOperationException("Market refresh forbidden after dismissal"); }));
            services.AddSingleton(DismissProxy<ICatalogWorkspace>.Wrap(fixture.Workspace, (method, _) =>
            { if (method == nameof(ICatalogWorkspace.SetDispositionAsync) && CommandFailure != null) throw CommandFailure; }));
        }
    }
    public class DismissProxy<T> : DispatchProxy where T : class
    {
        private T target = default!;
        private Action<string, object?[]?> before = default!;
        internal static T Wrap(T target, Action<string, object?[]?> before)
        {
            T instance = Create<T, DismissProxy<T>>();
            var proxy = (DismissProxy<T>)(object)instance;
            proxy.target = target; proxy.before = before;
            return instance;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            before(targetMethod!.Name, args);
            try { return targetMethod.Invoke(target, args); }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
        }
    }
}
