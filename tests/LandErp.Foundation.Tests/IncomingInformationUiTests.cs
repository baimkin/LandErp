using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Overview.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Server.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

public sealed partial class CatalogBulkArchiveUiTests
{
    [TestMethod]
    public async Task InformationLoadsAfterRowsCachesPresentationAndKeepsRowsOnSecondaryFailure()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPaginationAsync(f, 60);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator();
        var access = new EmployeeAccessService(f.Factory);
        var presets = new IncomingFilterPresetService(source, access);
        var reads = new IncomingCatalogReadService(f.Factory, access, f.Workspace, TimeProvider.System, presets);
        int pageReads = 0, informationReads = 0;
        bool failInformation = false, failMarket = false;
        TaskCompletionSource? hold = null;
        TaskCompletionSource? entered = null;
        var controlled = new InformationReadControl(reads, () => pageReads++, async () =>
        {
            informationReads++;
            entered?.TrySetResult();
            if (hold != null) await hold.Task; // Deliberately ignores cancellation to exercise stale response guards.
            if (failInformation) throw new IOException("Secondary numbers unavailable");
        });
        await using var services = PageServices(f, source, activator, f.Manager, registrations =>
        {
            registrations.AddSingleton<IIncomingCatalogReadService>(controlled);
            var market = new LandErp.Infrastructure.Modules.Overview.GroupMarketService(f.Factory, access, presets);
            registrations.AddSingleton(DismissProxy<IGroupMarketService>.Wrap(market, (_, _) =>
            { if (failMarket) throw new IOException("Secondary market unavailable"); }));
        });
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!;
            Assert.AreEqual(60, PageData(page).Total);
            Assert.AreEqual(1, pageReads);
            Assert.AreEqual(0, informationReads, "Static render never invokes OnAfterRender; the initial read must be page-only.");
            Assert.IsTrue(rendered.ToHtmlString().Contains("Обновляем счётчики", StringComparison.Ordinal));
            await InvokeAsync(page, "LoadInformationAsync");
            Assert.AreEqual(1, informationReads);
            Assert.IsNotNull(Get(page, "information"));
            await InvokeAsync(page, "GoToPageAsync", 1);
            Assert.AreEqual(false, Get(page, "informationPending"));
            Assert.AreEqual(1, informationReads);
            await InvokeAsync(page, "PageSizeChangedAsync", new ChangeEventArgs { Value = "50" });
            Assert.AreEqual(false, Get(page, "informationPending"));
            Assert.AreEqual(50, PageData(page).Items.Count);

            await InvokeAsync(page, "ReloadAsync");
            failInformation = failMarket = true;
            await InvokeAsync(page, "LoadInformationAsync");
            Assert.AreEqual(60, PageData(page).Total);
            Assert.AreEqual(50, PageData(page).Items.Count);
            Assert.IsNotNull(Get(page, "informationError"));
            Assert.IsNotNull(Get(page, "marketError"));
            Assert.IsNull(Get(page, "Error"));
            failInformation = failMarket = false;

            await InvokeAsync(page, "ReloadAsync");
            hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task late = InvokeAsync(page, "LoadInformationAsync");
            await entered.Task;
            await InvokeAsync(page, "GoToPageAsync", 1);
            int beforeCoalescedRead = informationReads;
            await InvokeAsync(page, "LoadInformationAsync");
            Assert.AreEqual(beforeCoalescedRead, informationReads, "Same-scope navigation coalesces the first in-flight miss.");
            Guid[] currentIds = PageData(page).Items.Select(item => item.Id).ToArray();
            hold.SetResult();
            await late;
            CollectionAssert.AreEqual(currentIds, PageData(page).Items.Select(item => item.Id).ToArray());
            Assert.AreEqual(60, PageData(page).Total);

            hold = null; entered = null;
            await InvokeAsync(page, "ReloadAsync");
            hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task wrongScope = InvokeAsync(page, "LoadInformationAsync");
            await entered.Task;
            var edited = Get(page, "filter")!;
            string originalText = (string)edited.GetType().GetProperty("Text")!.GetValue(edited)!;
            edited.GetType().GetProperty("Text")!.SetValue(edited, "нет_такого_объявления");
            await InvokeAsync(page, "ApplyFilterAsync");
            hold.SetResult();
            await wrongScope;
            Assert.AreEqual(0, PageData(page).Total);
            Assert.IsNull(Get(page, "information"));
            edited.GetType().GetProperty("Text")!.SetValue(edited, originalText);
            hold = null; entered = null;
            await InvokeAsync(page, "ReloadAsync");
            await InvokeAsync(page, "LoadInformationAsync");
            await InvokeAsync(page, "ReloadAsync");
            hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task beforeMutation = InvokeAsync(page, "LoadInformationAsync");
            await entered.Task;
            Guid removedId = PageData(page).Items[0].Id;
            await InvokeAsync(page, "RemoveLocalRow", removedId);
            Assert.AreEqual(59, PageData(page).Total);
            hold.SetResult();
            await beforeMutation;
            Assert.AreEqual(59, PageData(page).Total, "Late information cannot undo an acknowledged local update.");
            Assert.AreEqual(DateTimeOffset.MinValue, ((IncomingCatalogInformation)Get(page, "information")!).FreshAt);
            hold = null; entered = null;
            await InvokeAsync(page, "ReloadAsync");
            var cached = (IncomingCatalogInformation)Get(page, "information")!;
            SetDismissField(page, "information", cached with { FreshAt = DateTimeOffset.UtcNow.AddSeconds(-61) });
            await InvokeAsync(page, "GoToPageAsync", (int)Get(page, "page")! == 0 ? 1 : 0);
            Assert.AreEqual(true, Get(page, "informationPending"));
            await InvokeAsync(page, "LoadInformationAsync");
            Assert.AreEqual(false, Get(page, "informationPending"));
        });
    }

    private sealed class InformationReadControl(IIncomingCatalogReadService inner, Action main, Func<Task> information)
        : IIncomingCatalogReadService
    {
        public Task<IncomingCatalogReadPage> ReadAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken token)
            => inner.ReadAsync(subject, filter, token);
        public Task<IncomingCatalogReadPage> ReadPageAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken token)
        { main(); return inner.ReadPageAsync(subject, filter, token); }
        public async Task<IncomingCatalogInformation> ReadInformationAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken token)
        {
            var result = await inner.ReadInformationAsync(subject, filter, token);
            await information();
            return result;
        }
        public Task<IncomingCatalogReadWindow> ReadWindowAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken token)
            => inner.ReadWindowAsync(subject, filter, token);
        public Task<IncomingCatalogReadWindow> ReadItemsAsync(Subject subject, IncomingCatalogReadFilter filter, IReadOnlyCollection<Guid> ids, CancellationToken token)
            => inner.ReadItemsAsync(subject, filter, ids, token);
        public Task<IncomingCatalogChangeScope> ReadChangeScopeAsync(Subject subject, IncomingCatalogReadFilter filter, Guid id, CancellationToken token)
            => inner.ReadChangeScopeAsync(subject, filter, id, token);
        public Task<IncomingCatalogDetailRead> ReadDetailAsync(Subject subject, Guid id, CancellationToken token)
            => inner.ReadDetailAsync(subject, id, token);
        public Task<IReadOnlyList<IncomingDuplicateLinkTargetView>> SearchDuplicateTargetsAsync(Subject subject, Guid id, string text, CancellationToken token)
            => inner.SearchDuplicateTargetsAsync(subject, id, text, token);
    }
}
