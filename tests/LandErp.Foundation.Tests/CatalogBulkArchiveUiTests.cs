using System.Net;
using System.Reflection;
using System.Security.Claims;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Overview.Contracts;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Overview;
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

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class CatalogBulkArchiveUiTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ActualPageSelectsAllPreviewsCheckboxAndArchivesWithoutBrowser(bool withZone)
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var pair = await f.IngestMarketplacePairAsync();
        Guid? zone = null;
        if (withZone)
        {
            zone = await pair.Administration.CreateGroupAsync(f.Owner, "Зона UI архива", 1, "bulk-ui", CancellationToken.None);
            await using var db = f.Sandbox.Context();
            foreach (var search in await db.SearchConfigurations.ToArrayAsync()) search.SearchGroupId = zone;
            await db.SaveChangesAsync();
        }
        await using (var db = f.Sandbox.Context())
        {
            foreach (var item in await db.Listings.ToArrayAsync()) item.IncludeInCalculation = true;
            await db.SaveChangesAsync();
        }
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = new PageActivator(zone);
        await using var services = PageServices(f, source, activator, withZone ? f.Owner : f.Manager);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!;
            Assert.IsNotNull(Get(page, "catalogPage"));
            await InvokeAsync(page, "SelectAllCalculation");
            await InvokeAsync(page, "OpenArchiveAsync");
            Assert.AreEqual(withZone, Get(page, "archiveExcludeFromMedian"));
            var preview = (CatalogArchivePreview)Get(page, "archivePreview")!;
            Assert.AreEqual(2, preview.Total);
            Assert.AreEqual(withZone ? 2 : 0, preview.WouldExcludeFromMedian);
            Render(page);
            string html = WebUtility.HtmlDecode(rendered.ToHtmlString());
            StringAssert.Contains(html, "Отправить объявления в архив");
            StringAssert.Contains(html, "Также исключить из медианы");
            if (withZone)
            {
                await InvokeAsync(page, "ArchiveMedianChangedAsync", new ChangeEventArgs { Value = false });
                Assert.AreEqual(0, ((CatalogArchivePreview)Get(page, "archivePreview")!).WouldExcludeFromMedian);
                await InvokeAsync(page, "ArchiveMedianChangedAsync", new ChangeEventArgs { Value = true });
                Assert.AreEqual(2, ((CatalogArchivePreview)Get(page, "archivePreview")!).WouldExcludeFromMedian);
            }
            else StringAssert.Contains(html, "Для исключения из медианы выберите зону");
            await InvokeAsync(page, "ApplyArchiveAsync");
            Assert.AreEqual(false, Get(page, "archiveOpen"));
            Assert.AreEqual(0, Get(page, "CalculationCount"));
            Render(page);
            html = WebUtility.HtmlDecode(rendered.ToHtmlString());
            StringAssert.Contains(html, "Отправлено в архив: 2.");
        });
        await using var check = f.Sandbox.Context();
        Assert.AreEqual(2, await check.Listings.CountAsync(item => item.Disposition == CatalogDisposition.Dismissed
            && item.IncludeInCalculation == !withZone));
    }

    [TestMethod]
    public async Task PaginationKeepsFilterSortAndExplicitSelectionAcrossPagesAndSizes()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPaginationAsync(f, 267);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator();
        await using var services = PageServices(f, source, activator, f.Manager);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!;
            var first = PageData(page);
            Assert.AreEqual(267, first.Total);
            Assert.AreEqual(25, first.Items.Count);
            Assert.AreEqual(1000m, first.Items[0].Price);
            await InvokeAsync(page, "SelectCalculationItem", first.Items[0], true);
            await InvokeAsync(page, "GoToPageAsync", 5);
            Assert.AreEqual(1125m, PageData(page).Items[0].Price);
            Assert.AreEqual(1, Get(page, "CalculationCount"));
            var links = (IEnumerable<int?>)page.GetType().GetMethod("PageLinks", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
            CollectionAssert.AreEqual(new int?[] { 0, null, 3, 4, 5, 6, 7, null, 10 }, links.ToArray());
            Render(page);
            string html = WebUtility.HtmlDecode(rendered.ToHtmlString());
            StringAssert.Contains(html, "Страница 6 из 11");
            StringAssert.Contains(html, "126–150 из 267");
            StringAssert.Contains(html, "aria-current=\"page\"");
            await InvokeAsync(page, "SelectCalculationItem", PageData(page).Items[0], true);
            await InvokeAsync(page, "GoToPageAsync", -1);
            await InvokeAsync(page, "GoToPageAsync", 11);
            Assert.AreEqual(5, Get(page, "page"));
            await InvokeAsync(page, "PageSizeChangedAsync", new ChangeEventArgs { Value = "50" });
            Assert.AreEqual(0, Get(page, "page"));
            Assert.AreEqual(50, PageData(page).Items.Count);
            Assert.AreEqual(2, Get(page, "CalculationCount"));
            await InvokeAsync(page, "GoToPageAsync", 2);
            Assert.AreEqual(1100m, PageData(page).Items[0].Price);
            await InvokeAsync(page, "PreviousAsync");
            Assert.AreEqual(1, Get(page, "page"));
            await InvokeAsync(page, "NextAsync");
            Assert.AreEqual(2, Get(page, "page"));
            await InvokeAsync(page, "PageSizeChangedAsync", new ChangeEventArgs { Value = "100" });
            Assert.AreEqual(100, PageData(page).Items.Count);
            Assert.AreEqual(2, Get(page, "CalculationCount"));
            await InvokeAsync(page, "PageSizeChangedAsync", new ChangeEventArgs { Value = "200" });
            Assert.AreEqual(100, Get(page, "pageSize"));
            await InvokeAsync(page, "GoToPageAsync", 2);
            Assert.AreEqual(67, PageData(page).Items.Count);
            Assert.AreEqual(1200m, PageData(page).Items[0].Price);
            await InvokeAsync(page, "NextAsync");
            Assert.AreEqual(2, Get(page, "page"));
            await InvokeAsync(page, "GoToPageAsync", 0);
            await InvokeAsync(page, "PreviousAsync");
            Assert.AreEqual(0, Get(page, "page"));
            Assert.AreEqual(267, PageData(page).Total);
            Assert.AreEqual(IncomingCatalogSortField.Price, Get(page, "sortField"));
        });
    }

    [TestMethod]
    public async Task ArchivingLastPageClampsToExistingPageAndThenHandlesEmptyResults()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPaginationAsync(f, 101);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator();
        await using var services = PageServices(f, source, activator, f.Manager);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!;
            await InvokeAsync(page, "GoToPageAsync", 4);
            Assert.AreEqual(1, PageData(page).Items.Count);
            await InvokeAsync(page, "ToggleCalculationPage");
            await InvokeAsync(page, "OpenArchiveAsync");
            Assert.AreEqual(1, ((CatalogArchivePreview)Get(page, "archivePreview")!).Total);
            await InvokeAsync(page, "ApplyArchiveAsync");
            Assert.AreEqual(3, Get(page, "page"));
            Assert.AreEqual(100, PageData(page).Total);
            Assert.AreEqual(25, PageData(page).Items.Count);
            Render(page);
            string html = WebUtility.HtmlDecode(rendered.ToHtmlString());
            StringAssert.Contains(html, "76–100 из 100");
            StringAssert.Contains(html, "Страница 4 из 4");
            await InvokeAsync(page, "SelectAllCalculation");
            await InvokeAsync(page, "OpenArchiveAsync");
            Assert.AreEqual(100, ((CatalogArchivePreview)Get(page, "archivePreview")!).Total);
            await InvokeAsync(page, "ApplyArchiveAsync");
            Assert.AreEqual(0, Get(page, "page"));
            Assert.AreEqual(0, PageData(page).Total);
            await InvokeAsync(page, "NextAsync");
            Assert.AreEqual(0, Get(page, "page"));
            Render(page);
            StringAssert.Contains(WebUtility.HtmlDecode(rendered.ToHtmlString()), "0 предложений");
        });
        await using var db = f.Sandbox.Context();
        Assert.AreEqual(101, await db.Listings.CountAsync(item => item.Disposition == CatalogDisposition.Dismissed));
        Assert.AreEqual(1, await db.Listings.CountAsync(item => item.Disposition == CatalogDisposition.Incoming));
    }

    [TestMethod]
    public async Task SelectAllWithDeselectionSurvivesPageSizeAndArchivesOnlyMatchingResults()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        await SeedPaginationAsync(f, 67);
        await using var source = NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var activator = PaginationActivator();
        await using var services = PageServices(f, source, activator, f.Manager);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        Guid excluded = Guid.Empty;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<IncomingCatalogV2>();
            var page = activator.Page!;
            var first = PageData(page).Items[0];
            excluded = first.Id;
            await InvokeAsync(page, "SelectAllCalculation");
            await InvokeAsync(page, "SelectCalculationItem", first, false);
            await InvokeAsync(page, "GoToPageAsync", 2);
            Assert.AreEqual(66, Get(page, "CalculationCount"));
            await InvokeAsync(page, "PageSizeChangedAsync", new ChangeEventArgs { Value = "50" });
            Assert.AreEqual(true, Get(page, "allCalculationResults"));
            Assert.AreEqual(66, Get(page, "CalculationCount"));
            await InvokeAsync(page, "PageSizeChangedAsync", new ChangeEventArgs { Value = "100" });
            Assert.AreEqual(66, Get(page, "CalculationCount"));
            await InvokeAsync(page, "OpenArchiveAsync");
            Assert.AreEqual(66, ((CatalogArchivePreview)Get(page, "archivePreview")!).Total);
            await InvokeAsync(page, "ApplyArchiveAsync");
            Assert.AreEqual(1, PageData(page).Total);
            Assert.AreEqual(excluded, PageData(page).Items.Single().Id);
        });
        await using var db = f.Sandbox.Context();
        Assert.AreEqual(66, await db.Listings.CountAsync(item => item.Disposition == CatalogDisposition.Dismissed));
        Assert.AreEqual(2, await db.Listings.CountAsync(item => item.Disposition == CatalogDisposition.Incoming));
    }

    private static IncomingCatalogReadPage PageData(IncomingCatalogV2 page) => (IncomingCatalogReadPage)Get(page, "catalogPage")!;

    private static PageActivator PaginationActivator() => new(null)
    {
        Configure = page =>
        {
            var filter = Get(page, "filter")!;
            filter.GetType().GetProperty("Text")!.SetValue(filter, "Пагинация");
            page.GetType().GetField("sortField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, IncomingCatalogSortField.Price);
            page.GetType().GetField("sortDirection", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, IncomingCatalogSortDirection.Ascending);
        }
    };

    private static async Task SeedPaginationAsync(ProcurementTests.Phase1Fixture f, int count)
    {
        await using var db = f.Sandbox.Context();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 0; i <= count; i++) db.Listings.Add(new()
        {
            Id = Guid.CreateVersion7(), OrganizationId = f.OrganizationId, Source = CatalogSource.Manual,
            IngestionKind = CatalogIngestionKind.Employee, CreatedByEmployeeId = f.ManagerEmployeeId,
            Title = i < count ? $"Пагинация тест {i}" : "Другая выборка", Location = "Москва",
            Price = 1000m + i, AreaSquareMeters = 100m,
            RecordedAt = now, ReceivedAt = now, ChangedAt = now, Provenance = "Тест пагинации"
        });
        await db.SaveChangesAsync();
    }

    private static ServiceProvider PageServices(ProcurementTests.Phase1Fixture f, NpgsqlDataSource source,
        PageActivator activator, Subject subject)
    {
        var access = new EmployeeAccessService(f.Factory);
        var presets = new IncomingFilterPresetService(source, access);
        var registrations = new ServiceCollection().AddLogging();
        registrations.AddSingleton<IComponentActivator>(activator);
        registrations.AddSingleton<IJSRuntime, NoBrowserJs>();
        registrations.AddSingleton<NavigationManager, TestNavigation>();
        registrations.AddSingleton<AuthenticationStateProvider>(new TestAuthentication(subject));
        registrations.AddSingleton<IEmployeeAccessService>(access);
        registrations.AddSingleton<ICatalogWorkspace>(f.Workspace);
        registrations.AddSingleton<IIncomingFilterPresetService>(presets);
        registrations.AddSingleton<IIncomingCatalogReadService>(new IncomingCatalogReadService(f.Factory, access, f.Workspace, TimeProvider.System, presets));
        registrations.AddSingleton<IListingCommentService>(new ListingCommentService(f.Factory, access, TimeProvider.System));
        registrations.AddSingleton<ICatalogCalculationService>(new CatalogCalculationService(f.Factory, access, presets, TimeProvider.System));
        registrations.AddSingleton<ICatalogArchiveService>(new CatalogArchiveService(f.Factory, access, presets, TimeProvider.System));
        registrations.AddSingleton<IGroupMarketService>(new GroupMarketService(f.Factory, access, presets));
        return registrations.BuildServiceProvider();
    }
    private static object? Get(object page, string name) => page.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(page)
        ?? page.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(page);
    private static async Task InvokeAsync(object page, string name, params object[] args)
    {
        object? result = page.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);
        if (result is Task task) await task;
    }
    private static void Render(object page) => typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);

    private sealed class PageActivator(Guid? zone) : IComponentActivator
    {
        public IncomingCatalogV2? Page { get; private set; }
        public Action<IncomingCatalogV2>? Configure { get; init; }
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is IncomingCatalogV2 page)
            {
                Page = page;
                type.GetField("incomingMode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, IncomingCatalogMode.AllListings);
                type.GetField("searchGroupKey", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, zone?.ToString() ?? "");
                Configure?.Invoke(page);
            }
            return component;
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
        public TestNavigation() => Initialize("https://localhost/", "https://localhost/incoming");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Navigation is outside this test.");
    }
    private sealed class NoBrowserJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => throw new InvalidOperationException("No browser or JS is used by this test.");
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }
}
