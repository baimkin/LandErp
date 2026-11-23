using System.Reflection;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Overview.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Server.Components.Pages;
using Microsoft.AspNetCore.Components;
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
    [DataRow(25, false)]
    [DataRow(50, true)]
    [DataRow(100, false)]
    public async Task IncomingFastGroupTransferUpdatesSourcesOnOtherPagesWithoutFullReads(int size, bool selectAll)
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        await SeedPaginationAsync(f, size + 2);
        Guid[] ids;
        await using (var db = f.Sandbox.Context())
        {
            ids = await db.Listings.Where(item => item.Title != null && item.Title.StartsWith("Пагинация")).OrderBy(item => item.Price).Select(item => item.Id).ToArrayAsync();
            Guid group = Guid.CreateVersion7();
            db.CatalogObjectGroups.Add(new() { Id = group, OrganizationId = f.OrganizationId, RecordedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            foreach (var item in await db.Listings.Where(item => item.Id == ids[0] || item.Id == ids[size]).ToArrayAsync()) item.ObjectGroupId = group;
            await db.SaveChangesAsync();
        }
        Guid recipient = f.EmployeeId("manager2-phase1@test.invalid");
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator();
        var probes = new FastProbes(f, source);
        await using var services = PageServices(f, source, activator, f.Manager, probes.Register);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!;
            if (size != 25) await InvokeAsync(page, "PageSizeChangedAsync", new ChangeEventArgs { Value = size.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            bool lastPage = size == 100;
            if (lastPage) await InvokeAsync(page, "GoToPageAsync", 1);
            Guid transferId = lastPage ? ids[size] : ids[0];
            if (selectAll)
            {
                await InvokeAsync(page, "SelectAllCalculation");
                await InvokeAsync(page, "SelectCalculationItem", PageData(page).Items[1], false);
            }
            else
            {
                await InvokeAsync(page, "SelectCalculationItem", PageData(page).Items[0], true);
                await InvokeAsync(page, "SelectCalculationItem", PageData(page).Items[1], true);
            }
            probes.Arm();
            await InvokeAsync(page, "OpenDetailAsync", transferId);
            Assert.AreEqual(transferId, ((CatalogItemDetail)Get(page, "detail")!).Item.Id);
            Assert.IsTrue(PageData(page).Rows[transferId].Reviewed);
            await InvokeAsync(page, "OpenTransferAsync", PageData(page).Items.Single(item => item.Id == transferId));
            SetDismissField(page, "transferTargetKey", recipient.ToString());
            await InvokeAsync(page, "TransferToProcurementAsync");
            Assert.AreEqual(size, PageData(page).Total, "Both group members, including the hidden page, leave Incoming.");
            Assert.AreEqual(size, PageData(page).Items.Count);
            CollectionAssert.AreEqual(ids.Where(id => id != ids[0] && id != ids[size]).ToArray(), PageData(page).Items.Select(item => item.Id).ToArray());
            Assert.AreEqual(false, Get(page, "transferOpen"));
            Assert.IsNotNull(((CatalogItemDetail)Get(page, "detail")!).Item.PropertyCaseId);
            Assert.AreEqual(selectAll ? size - 1 : 1, Get(page, "CalculationCount"));
            Assert.AreEqual(1, probes.Reviews); Assert.AreEqual(1, probes.Transfers);
            Assert.AreEqual(2, probes.ScopeReads);
            Assert.AreEqual(1, probes.Reads.Windows.Count);
            Assert.AreEqual(lastPage ? 0 : size - 1, probes.Reads.Windows[0].Base.Offset);
            Assert.AreEqual(lastPage ? size : 1, probes.Reads.Windows[0].Base.Size);
            Assert.AreEqual(0, Get(page, "page"));
            Assert.AreEqual(0, probes.ForbiddenFullReads);
        });
        await using var verify = f.Sandbox.Context();
        Assert.AreEqual(1, await verify.PropertyCases.CountAsync());
        Assert.AreEqual(2, await verify.PropertyCaseSourceLinks.CountAsync(link => link.Confirmed));
        Assert.AreEqual(recipient, (await f.Workspace.ReadCardAsync(f.SecondManager, (await verify.PropertyCases.SingleAsync()).Id, default)).ManagerEmployeeId);
        var filter = new IncomingCatalogReadFilter(new("Пагинация"), WorkingScope: new(IncomingCatalogMode.AllListings));
        Assert.AreEqual(0, (await probes.Reads.Inner.ReadItemsAsync(f.ForeignOwner, filter, ids.Take(2).ToArray(), default)).Items.Count);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => probes.Reads.Inner.ReadChangeScopeAsync(f.ForeignOwner, filter, ids[0], default));
    }

    [TestMethod]
    public async Task IncomingFastOpenIgnoresLateReadRetriesOnlyReadAndHonorsReadOnlyAccess()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPaginationAsync(f, 5);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator(); var probes = new FastProbes(f, source);
        await using var services = PageServices(f, source, activator, f.Manager, probes.Register);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        Guid[] ids = [];
        Task? opening = null;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<IncomingCatalogDetailRead>(TaskCreationOptions.RunContinuationsAsynchronously);
        IncomingCatalogDetailRead? oldRead = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<IncomingCatalogV2>();
            ids = PageData(activator.Page!).Items.Select(item => item.Id).ToArray();
            oldRead = await probes.Reads.Inner.ReadDetailAsync(f.Manager, ids[0], default);
            probes.Arm();
            probes.Reads.DetailOverride = (subject, id, token) =>
            {
                if (id == ids[0]) { started.TrySetResult(); return late.Task; } // Deliberately ignore cancellation.
                return probes.Reads.Inner.ReadDetailAsync(subject, id, token);
            };
        });
        await renderer.Dispatcher.InvokeAsync(() => { opening = InvokeAsync(activator.Page!, "OpenDetailAsync", ids[0]); });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = activator.Page!;
            Assert.AreEqual(ids[0], Get(page, "detailLoadingId")); Assert.IsNull(Get(page, "detail"));
            await InvokeAsync(page, "CloseDetail");
            await InvokeAsync(page, "OpenDetailAsync", ids[1]);
            late.SetResult(oldRead!);
            await opening!;
            Assert.AreEqual(ids[1], ((CatalogItemDetail)Get(page, "detail")!).Item.Id);
            probes.Reads.DetailOverride = null; probes.Reads.DetailFailures = 1;
            await InvokeAsync(page, "OpenDetailAsync", ids[2]);
            Assert.IsNull(Get(page, "detail")); Assert.IsNotNull(Get(page, "detailLoadError"));
            int reviews = probes.Reviews;
            await InvokeAsync(page, "RetryDetailReadAsync");
            Assert.AreEqual(reviews, probes.Reviews, "A failed read must not repeat review/audit writes.");
            Assert.AreEqual(ids[2], ((CatalogItemDetail)Get(page, "detail")!).Item.Id);
            Assert.AreEqual(0, probes.ForbiddenFullReads);
        });
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with { IncomingAccess = IncomingAccessLevel.Read });
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = activator.Page!; probes.BlockFull = false;
            await InvokeAsync(page, "ReloadAsync"); probes.Arm();
            int reviews = probes.Reviews;
            await InvokeAsync(page, "OpenDetailAsync", ids[3]);
            Assert.AreEqual(reviews, probes.Reviews);
            Assert.IsFalse(PageData(page).Rows[ids[3]].Reviewed);
            Assert.AreEqual(ids[3], ((CatalogItemDetail)Get(page, "detail")!).Item.Id);
            Assert.AreEqual(0, probes.ForbiddenFullReads);
        });
    }

    [TestMethod]
    public async Task IncomingFastTransferKeepsChoiceOnReadRejectUnknownAndNeverResendsAfterCommit()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        await SeedPaginationAsync(f, 27);
        Guid recipient = f.EmployeeId("manager2-phase1@test.invalid");
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator(); var probes = new FastProbes(f, source);
        await using var services = PageServices(f, source, activator, f.Manager, probes.Register);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!; Guid id = PageData(page).Items[0].Id;
            probes.Arm(); await InvokeAsync(page, "OpenDetailAsync", id);
            await InvokeAsync(page, "OpenTransferAsync", PageData(page).Items[0]);
            SetDismissField(page, "transferTargetKey", recipient.ToString());
            probes.Reads.FailScopeAt = probes.Reads.Scopes + 1;
            await InvokeAsync(page, "TransferToProcurementAsync");
            Assert.AreEqual(0, probes.Transfers); Assert.IsNotNull(Get(page, "transferPreparationError"));
            foreach (var failure in new Exception[] { new ArgumentException("Получатель недоступен"), new IOException("Unknown") })
            {
                probes.TransferFailure = failure;
                await InvokeAsync(page, "TransferToProcurementAsync");
                Assert.AreEqual(true, Get(page, "transferOpen"));
                Assert.AreEqual(recipient.ToString(), Get(page, "transferTargetKey"));
                Assert.AreEqual(27, PageData(page).Total);
            }
            probes.TransferFailure = null;
            probes.Reads.FailScopeAt = probes.Reads.Scopes + 2; // Acknowledged write, then failed post-commit read.
            await InvokeAsync(page, "TransferToProcurementAsync");
            Assert.AreEqual(false, Get(page, "transferOpen"));
            Assert.AreEqual(true, Get(page, "RefreshRequired"));
            Assert.IsNotNull(((CatalogItemDetail)Get(page, "detail")!).Item.PropertyCaseId);
            Assert.AreEqual(3, probes.Transfers); // Two rejected/unknown synthetic attempts plus one real write.
            probes.BlockFull = false;
            await InvokeAsync(page, "ReloadAsync");
            Assert.AreEqual(26, PageData(page).Total);
            Assert.AreEqual(3, probes.Transfers);
        });
        await using var verify = f.Sandbox.Context();
        Assert.AreEqual(1, await verify.PropertyCases.CountAsync());
    }

    private sealed class FastProbes
    {
        public FastReads Reads { get; }
        public bool BlockFull { get; set; }
        public int ForbiddenFullReads { get; private set; }
        public int Reviews { get; private set; }
        public int Transfers { get; private set; }
        public int ScopeReads => Reads.Scopes;
        public Exception? TransferFailure { get; set; }
        private readonly ProcurementTests.Phase1Fixture fixture;
        private readonly IGroupMarketService market;
        public FastProbes(ProcurementTests.Phase1Fixture f, NpgsqlDataSource source)
        {
            fixture = f; var access = new EmployeeAccessService(f.Factory);
            var presets = new IncomingFilterPresetService(source, access);
            Reads = new(new IncomingCatalogReadService(f.Factory, access, f.Workspace, TimeProvider.System, presets), () =>
            { if (BlockFull) { ForbiddenFullReads++; throw new InvalidOperationException("Full list forbidden"); } });
            market = new LandErp.Infrastructure.Modules.Overview.GroupMarketService(f.Factory, access, presets);
        }
        public void Arm() { BlockFull = true; ForbiddenFullReads = 0; Reads.Windows.Clear(); }
        public void Register(IServiceCollection services)
        {
            services.AddSingleton<IIncomingCatalogReadService>(Reads);
            services.AddSingleton(DismissProxy<IGroupMarketService>.Wrap(market, (_, _) =>
            { if (BlockFull) { ForbiddenFullReads++; throw new InvalidOperationException("Market read forbidden"); } }));
            services.AddSingleton(DismissProxy<ICatalogWorkspace>.Wrap(fixture.Workspace, (method, _) =>
            {
                if (method == nameof(ICatalogWorkspace.RegisterViewAsync)) Reviews++;
                if (method == nameof(ICatalogWorkspace.TransferToProcurementAsync)) { Transfers++; if (TransferFailure != null) throw TransferFailure; }
            }));
        }
    }

    private sealed class FastReads(IncomingCatalogReadService inner, Action full) : IIncomingCatalogReadService
    {
        public IncomingCatalogReadService Inner => inner;
        public List<IncomingCatalogReadFilter> Windows { get; } = [];
        public int Scopes { get; private set; }
        public int FailScopeAt { get; set; }
        public int DetailFailures { get; set; }
        public Func<Subject, Guid, CancellationToken, Task<IncomingCatalogDetailRead>>? DetailOverride { get; set; }
        public Task<IncomingCatalogReadPage> ReadAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken token)
        { full(); return inner.ReadAsync(subject, filter, token); }
        public Task<IncomingCatalogReadPage> ReadPageAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken token)
        { full(); return inner.ReadPageAsync(subject, filter, token); }
        public Task<IncomingCatalogInformation> ReadInformationAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken token)
        { full(); return inner.ReadInformationAsync(subject, filter, token); }
        public Task<IncomingCatalogReadWindow> ReadWindowAsync(Subject subject, IncomingCatalogReadFilter filter, CancellationToken token)
        { Windows.Add(filter); return inner.ReadWindowAsync(subject, filter, token); }
        public Task<IncomingCatalogReadWindow> ReadItemsAsync(Subject subject, IncomingCatalogReadFilter filter, IReadOnlyCollection<Guid> ids, CancellationToken token) => inner.ReadItemsAsync(subject, filter, ids, token);
        public Task<IncomingCatalogChangeScope> ReadChangeScopeAsync(Subject subject, IncomingCatalogReadFilter filter, Guid id, CancellationToken token)
        { Scopes++; if (Scopes == FailScopeAt) throw new IOException("Scope read failed"); return inner.ReadChangeScopeAsync(subject, filter, id, token); }
        public Task<IncomingCatalogDetailRead> ReadDetailAsync(Subject subject, Guid id, CancellationToken token)
        {
            if (DetailFailures > 0) { DetailFailures--; throw new IOException("Detail read failed"); }
            return DetailOverride?.Invoke(subject, id, token) ?? inner.ReadDetailAsync(subject, id, token);
        }
        public Task<IReadOnlyList<IncomingDuplicateLinkTargetView>> SearchDuplicateTargetsAsync(Subject subject, Guid id, string text, CancellationToken token) => inner.SearchDuplicateTargetsAsync(subject, id, text, token);
    }
}
