using System.Reflection;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class BrowserReliabilityTests
{
    private static T Proxy<T>(Func<string, object?[]?, object?> handler) where T : class
    {
        T value = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)value).Handler = handler;
        return value;
    }
    public class InterfaceProxy : DispatchProxy
    {
        public Func<string, object?[]?, object?> Handler { get; set; } = (_, _) => null;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!.Name, args);
    }
    private static IPlaywright PlaywrightFor(IBrowserContext context, Action? onDispose = null)
    {
        IBrowserType browser = Proxy<IBrowserType>((name, _) => name == "LaunchPersistentContextAsync" ? Task.FromResult(context) : null);
        return Proxy<IPlaywright>((name, _) =>
        {
            if (name == "get_Chromium") return browser;
            if (name == "Dispose") onDispose?.Invoke();
            return null;
        });
    }

    [TestMethod]
    public async Task FailedCloseReleasesRealSessionLeaseAndDisposeIsIdempotent()
    {
        int pageCloses = 0, contextCloses = 0;
        IPage page = Proxy<IPage>((name, _) => name switch
        {
            "get_IsClosed" => false,
            "GotoAsync" => Task.FromException<IResponse?>(new OperationCanceledException()),
            "CloseAsync" => ClosePage(),
            _ => null
        });
        Task ClosePage() { pageCloses++; return Task.FromException(new PlaywrightException("synthetic close error")); }
        IBrowserContext context = Proxy<IBrowserContext>((name, _) =>
        {
            if (name == "get_Pages") return new[] { page };
            if (name == "CloseAsync") { contextCloses++; return Task.CompletedTask; }
            return null;
        });
        await using BrowserSessions sessions = new(Path.Combine(Path.GetTempPath(), "LandErp-BrowserReliability", Guid.NewGuid().ToString("N")),
            playwrightFactory: () => Task.FromResult(PlaywrightFor(context)));
        ISourcePage first = await sessions.CreatePageAsync(SourceSite.Cian, new(), CancellationToken.None);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => first.OpenAsync("https://www.cian.ru/cat.php", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<PlaywrightException>(async () => await first.DisposeAsync());
        await first.DisposeAsync(); Assert.AreEqual(1, pageCloses);
        // A leaked lease rejects the restart; a missing quarantine reuses the unsafe context.
        await using ISourcePage second = await sessions.CreatePageAsync(SourceSite.Cian, new(), CancellationToken.None);
        Assert.AreEqual(1, contextCloses);
    }

    [TestMethod]
    public async Task CancelledNewPageClosesLateResultInsteadOfLeavingAnOrphan()
    {
        TaskCompletionSource<IPage> creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IPage page = Proxy<IPage>((name, _) => { if (name == "CloseAsync") { closed.TrySetResult(); return Task.CompletedTask; } return null; });
        IBrowserContext context = Proxy<IBrowserContext>((name, _) => name switch
        {
            "get_Pages" => Array.Empty<IPage>(),
            "NewPageAsync" => CreatePage(),
            "CloseAsync" => Task.CompletedTask,
            _ => null
        });
        Task<IPage> CreatePage() { entered.TrySetResult(); return creation.Task; }
        await using BrowserSessions sessions = new(Path.Combine(Path.GetTempPath(), "LandErp-BrowserReliability", Guid.NewGuid().ToString("N")),
            playwrightFactory: () => Task.FromResult(PlaywrightFor(context)));
        using CancellationTokenSource stop = new();
        Task<ISourcePage> pending = sessions.CreatePageAsync(SourceSite.Cian, new(), stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); stop.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await pending);
        creation.SetResult(page);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [TestMethod]
    public async Task CancelledDriverCreationDisposesItsLateResult()
    {
        TaskCompletionSource<IPlaywright> creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IPlaywright driver = Proxy<IPlaywright>((name, _) => { if (name == "Dispose") disposed.TrySetResult(); return null; });
        await using BrowserSessions sessions = new(Path.Combine(Path.GetTempPath(), "LandErp-BrowserReliability", Guid.NewGuid().ToString("N")),
            playwrightFactory: () => { entered.TrySetResult(); return creation.Task; });
        using CancellationTokenSource stop = new();
        Task<ISourcePage> pending = sessions.CreatePageAsync(SourceSite.Cian, new(), stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); stop.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await pending);
        creation.SetResult(driver); await disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [TestMethod]
    public async Task CancelledSnapshotClosesPageBeforeReturningItToTheSessionPool()
    {
        TaskCompletionSource<string> reading = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool closed = false, released = false;
        IPage page = Proxy<IPage>((name, _) =>
        {
            if (name == "get_Url") return "https://www.cian.ru/cat.php";
            if (name == "EvaluateAsync") return reading.Task;
            if (name == "CloseAsync") { closed = true; return Task.CompletedTask; }
            return null;
        });
        DomSourcePage adapter = new(page, SourceSite.Cian, () => { released = true; return Task.CompletedTask; });
        using CancellationTokenSource stop = new();
        Task<PageObservation> pending = adapter.ReadAsync(stop.Token); stop.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await pending);
        await adapter.DisposeAsync();
        Assert.IsTrue(closed); Assert.IsTrue(released); reading.TrySetResult("{}");
    }

    [TestMethod]
    public async Task SuccessfulCloseDoesNotQuarantineHealthySiblingTabs()
    {
        bool quarantine = false;
        IPage page = Proxy<IPage>((name, _) => name switch
        {
            "GotoAsync" => Task.FromException<IResponse?>(new OperationCanceledException()),
            "CloseAsync" => Task.CompletedTask,
            _ => null
        });
        DomSourcePage adapter = new(page, SourceSite.Cian, () => Task.CompletedTask, onInterrupted: () => quarantine = true);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => adapter.OpenAsync("https://www.cian.ru/cat.php", CancellationToken.None));
        await adapter.DisposeAsync(); Assert.IsFalse(quarantine);
    }

    [TestMethod]
    public async Task UnresponsivePageCloseHasBoundedWaitAndStillReleasesLease()
    {
        TaskCompletionSource closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool released = false;
        IPage page = Proxy<IPage>((name, _) => name switch
        {
            "GotoAsync" => Task.FromException<IResponse?>(new OperationCanceledException()),
            "CloseAsync" => closing.Task,
            _ => null
        });
        DomSourcePage adapter = new(page, SourceSite.Cian, () => { released = true; return Task.CompletedTask; });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => adapter.OpenAsync("https://www.cian.ru/cat.php", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<TimeoutException>(async () => await adapter.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(7)));
        Assert.IsTrue(released); closing.TrySetException(new PlaywrightException("late failure"));
    }
}
