namespace LandErp.ParserSpike.LocalCollection;

/// <summary>Cancellation of a wait does not cancel Playwright's resource creation.</summary>
internal static class BrowserLifetime
{
    public static async Task<T> AcquireAsync<T>(Task<T> creation, Func<T, Task> cleanup, CancellationToken token)
    {
        try { return await creation.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _ = CleanupLateAsync(creation, cleanup);
            throw;
        }
    }

    private static async Task CleanupLateAsync<T>(Task<T> creation, Func<T, Task> cleanup)
    {
        try { await cleanup(await creation.ConfigureAwait(false)).ConfigureAwait(false); }
        catch (Exception) { /* Observe both creation and cleanup failures after ownership was cancelled. */ }
    }

    public static async Task CloseAsync(Task closing)
    {
        try { await closing.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (TimeoutException) { _ = ObserveAsync(closing); throw; }
    }

    private static async Task ObserveAsync(Task task)
    { try { await task.ConfigureAwait(false); } catch (Exception) { /* Transport disposal is the final fallback. */ } }
}
