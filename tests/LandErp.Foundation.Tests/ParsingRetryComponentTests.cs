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

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class ParsingRetryComponentTests
{
    [TestMethod]
    public async Task ActualPageKeepsIndependentRowsLiveAndPreservesConfirmedStateWhenRefreshFails()
    {
        var first = ParsingRetryStateTests.Search(); var second = ParsingRetryStateTests.Search();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new Dictionary<Guid, int>();
        var admin = Stub<ICollectionAdministration>((method, args) =>
        {
            Assert.AreEqual("EnqueueAsync", method.Name);
            var id = (Guid)args![1]!; calls[id] = calls.GetValueOrDefault(id) + 1;
            return id == first.Id ? held.Task : Task.CompletedTask;
        });
        await using var fixture = new ComponentFixture(admin, [first, second]);
        await fixture.RenderAsync();
        Task? firstTask = null;
        await fixture.Renderer.Dispatcher.InvokeAsync(() => { firstTask = fixture.Page!.Enqueue(first.Id); });
        await fixture.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            Assert.AreEqual("Отправляем…", fixture.Page!.Action(first).Label);
            Assert.IsFalse(fixture.Page.Action(second).Disabled);
            Assert.IsFalse(fixture.Page.PageBusy);
            await fixture.Page.Enqueue(first.Id);
            Assert.AreEqual(1, calls[first.Id], "A second call while awaiting the same search is ignored.");
            await fixture.Page.Enqueue(second.Id);
            Assert.AreEqual(1, calls[second.Id]);
            Assert.AreEqual("В очереди", fixture.Page.Action(second).Label);
            StringAssert.Contains(fixture.Page.Message(second.Id)!, "Запуск подтверждён");
            Assert.IsTrue(fixture.Page.Action(second).Disabled, "Failed refresh cannot invite duplicate enqueue.");
        });
        held.SetResult(); await firstTask!;
        await fixture.Renderer.Dispatcher.InvokeAsync(() =>
        {
            Assert.AreEqual("В очереди", fixture.Page!.Action(first).Label);
            Assert.IsFalse(fixture.Page.PageBusy);
            string html = System.Net.WebUtility.HtmlDecode(fixture.View.ToHtmlString());
            StringAssert.Contains(html, "В очереди");
            StringAssert.Contains(html, "Обновить статус");
        });
    }

    [TestMethod]
    public async Task LostReplyRequiresSuccessfulReadAndValidationErrorRemainsVisible()
    {
        var first = ParsingRetryStateTests.Search(); var second = ParsingRetryStateTests.Search(); int calls = 0;
        var admin = Stub<ICollectionAdministration>((_, args) =>
        {
            calls++;
            return Task.FromException((Guid)args![1]! == first.Id
                ? new IOException("synthetic-private-provider-detail")
                : new ArgumentException("Для поиска уже есть активная работа."));
        });
        await using var fixture = new ComponentFixture(admin, [first, second]);
        await fixture.RenderAsync();
        await fixture.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            await fixture.Page!.Enqueue(first.Id);
            Assert.AreEqual("Не подтверждено", fixture.Page.Action(first).Label);
            await fixture.Page.Enqueue(first.Id); Assert.AreEqual(1, calls);
            Assert.IsFalse(fixture.Page.Message(first.Id)!.Contains("synthetic-private", StringComparison.Ordinal));
            await fixture.Page.Enqueue(second.Id);
            StringAssert.Contains(fixture.Page.Message(second.Id)!, "активная работа");
            Assert.IsFalse(fixture.Page.Action(second).Disabled);
            // A stale read cannot clear an unknown result. A fresh authoritative read may confirm no job.
            fixture.Page.Reconcile(0); Assert.IsTrue(fixture.Page.Action(first).Disabled);
            fixture.Page.Reconcile(1); Assert.IsFalse(fixture.Page.Action(first).Disabled);
            StringAssert.Contains(fixture.Page.Message(first.Id)!, "Активного задания не найдено");
        });
    }

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
        public bool PageBusy => Busy;
        public Task Enqueue(Guid id) => (Task)typeof(Collectors).GetMethod("EnqueueAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(this, [id])!;
        public CollectionRunAction Action(SearchView search) => (CollectionRunAction)typeof(Collectors).GetMethod("RunAction", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(this, [search, true])!;
        public string? Message(Guid id) => (string?)typeof(Collectors).GetMethod("QueueMessage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(this, [id]);
        public void Reconcile(int version) => typeof(Collectors).GetMethod("ReconcileEnqueues", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(this, [version]);
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
