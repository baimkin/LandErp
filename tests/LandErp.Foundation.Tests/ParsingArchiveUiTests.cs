using System.Reflection;
using System.Security.Claims;
using LandErp.Application.Foundation;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Server.Components;
using LandErp.Server.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Domain;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class ParsingArchiveUiTests
{
    [TestMethod]
    public async Task ActualPageExcludesArchivedIssuesAndCountsButKeepsArchiveInSearchList()
    {
        Guid archivedGroup = Guid.NewGuid(), activeGroup = Guid.NewGuid();
        var archived = Search() with { Label = "Старые Дубки", GroupId = archivedGroup, Group = "Старые Дубки",
            Enabled = true, LastRun = Run("Failed") };
        var active = Search() with { Label = "Дубки 2", GroupId = activeGroup, Group = "Дубки 2",
            LastRun = Run("Failed") };
        var ungrouped = Search() with { Label = "Без группы", LastRun = Run("Failed") };
        SearchGroupView[] groups = [new(archivedGroup, "Старые Дубки", 0, false, 1, 1), new(activeGroup, "Дубки 2", 1, true, 1, 1)];
        int calls = 0;
        var admin = Stub<ICollectionAdministration>((_, _) => { calls++; return Task.CompletedTask; });
        await using var f = new ComponentFixture(admin, [archived, active, ungrouped], groups, "home");
        await f.RenderAsync();
        await f.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            string html = System.Net.WebUtility.HtmlDecode(f.View.ToHtmlString());
            int start = html.IndexOf("<table class=\"workspace-table issue-table\"", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0);
            string issueTable = html[start..html.IndexOf("</table>", start, StringComparison.Ordinal)];
            Assert.IsFalse(issueTable.Contains(archived.Id.ToString(), StringComparison.Ordinal));
            StringAssert.Contains(issueTable, active.Id.ToString()); StringAssert.Contains(issueTable, ungrouped.Id.ToString());
            int headingStart = html.IndexOf("Требуют внимания ", StringComparison.Ordinal);
            string heading = html[headingStart..html.IndexOf("</h2>", headingStart, StringComparison.Ordinal)];
            Assert.IsTrue(heading.Contains(">2</span>", StringComparison.Ordinal), "Issue heading counts only the active and ungrouped searches.");
            int categoryStart = html.IndexOf(">Все <span", StringComparison.Ordinal);
            string category = html[categoryStart..html.IndexOf("</button>", categoryStart, StringComparison.Ordinal)];
            Assert.IsTrue(category.Contains(">2</span>", StringComparison.Ordinal), "Issue category excludes the archive too.");
            await f.Page!.Enqueue(archived.Id); Assert.AreEqual(0, calls);
        });
        await using var list = new ComponentFixture(admin, [archived, active, ungrouped], groups, includeArchive: true);
        await list.RenderAsync();
        await list.Renderer.Dispatcher.InvokeAsync(() =>
        {
            // Archives stay accessible explicitly; excluding the main issues never deletes their old result.
            string html = System.Net.WebUtility.HtmlDecode(list.View.ToHtmlString());
            int start = html.IndexOf("<table class=\"workspace-table search-table\"", StringComparison.Ordinal);
            string table = html[start..html.IndexOf("</table>", start, StringComparison.Ordinal)];
            StringAssert.Contains(table, archived.Id.ToString());
            StringAssert.Contains(table, "В архиве");
            StringAssert.Contains(table, "Зона в архиве. Новые запуски недоступны.");
            StringAssert.Contains(table, "Ошибка сбора");
        });
    }

    internal static SearchView Search(bool enabled = true, CollectionRunView? run = null) => new(Guid.NewGuid(), "Тест", CatalogSource.Avito,
        "https://www.avito.ru/moskva/zemelnye_uchastki", 10, null, "Без группы", "Вручную", CollectionScheduleKind.Manual,
        null, [], enabled, null, 1, null, run);
    internal static CollectionRunView Run(string state) => new(Guid.NewGuid(), null, state, DateTimeOffset.UtcNow,
        null, null, "", 0, 0, 0, 0, "", [], null, 0, null, false);

    internal static T Stub<T>(Func<MethodInfo, object?[]?, object?> invoke) where T : class
    {
        var value = DispatchProxy.Create<T, InterfaceProxy>(); ((InterfaceProxy)(object)value).Handler = invoke; return value;
    }
    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => throw new InvalidOperationException("Unexpected dependency call.");
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
    public sealed class PageHarness : Collectors
    {
        protected override Task OnInitializedAsync() => Task.CompletedTask;
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
        protected override Task ReadAsync() => Task.FromException(new IOException("synthetic-refresh-failure"));
        public Task Enqueue(Guid id) => (Task)typeof(Collectors).GetMethod("EnqueueAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(this, [id])!;
    }
    internal sealed class ComponentFixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        public HtmlRenderer Renderer { get; }
        public PageHarness? Page { get; private set; }
        public Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent View { get; private set; }
        public ComponentFixture(ICollectionAdministration admin, SearchView[] searches, SearchGroupView[]? groups = null, string tab = "searches", bool includeArchive = false)
        {
            var registrations = new ServiceCollection().AddLogging();
            registrations.AddSingleton(admin);
            registrations.AddSingleton(Stub<IEmployeeAccessService>((_, _) => throw new InvalidOperationException()));
            registrations.AddSingleton(Stub<IDatabaseStatus>((_, _) => throw new InvalidOperationException()));
            registrations.AddSingleton(Stub<IFileStorageHealth>((_, _) => throw new InvalidOperationException()));
            registrations.AddSingleton(Stub<IJSRuntime>((_, _) => throw new InvalidOperationException()));
            registrations.AddSingleton<NavigationManager, TestNavigation>();
            registrations.AddSingleton<AuthenticationStateProvider, TestAuthentication>();
            registrations.AddSingleton<IComponentActivator>(new Activator(type =>
            {
                var component = (IComponent)System.Activator.CreateInstance(type)!;
                if (component is PageHarness page)
                {
                    Page = page;
                    Set(page, "effectiveAccess", new EffectiveEmployeeAccess(Guid.NewGuid(), Guid.NewGuid(), null, null,
                        EmployeeAccessRules.NoAccess with { CollectionAccess = CollectionAccessLevel.Manage }, EmployeeAccessSource.Configured));
                    Set(page, "databaseReady", true); Set(page, "activeTab", tab);
                    Set(page, "groupVisibility", includeArchive ? "all" : "active");
                    Set(page, "view", new CollectionAdminView([], groups ?? [], searches, [], searches.Length, 0, 0, 0, 0, 0,
                        new("Работает", null, null, null, 0, "")));
                    typeof(WorkspaceComponent).GetProperty("Loading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, false);
                }
                return component;
            }));
            services = registrations.BuildServiceProvider(); Renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        }
        public Task RenderAsync() => Renderer.Dispatcher.InvokeAsync(async () => { View = await Renderer.RenderComponentAsync<PageHarness>(); });
        private static void Set(PageHarness page, string name, object value) => typeof(Collectors).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, value);
        public async ValueTask DisposeAsync() { await Renderer.DisposeAsync(); await services.DisposeAsync(); }
    }
    private sealed class Activator(Func<Type, IComponent> create) : IComponentActivator { public IComponent CreateInstance(Type type) => create(type); }
    private sealed class TestNavigation : NavigationManager { public TestNavigation() => Initialize("https://test.invalid/", "https://test.invalid/collectors"); protected override void NavigateToCore(string uri, bool forceLoad) { } }
    private sealed class TestAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"))));
    }
}
