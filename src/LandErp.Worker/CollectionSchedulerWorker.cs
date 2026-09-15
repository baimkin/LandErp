using LandErp.Application.Modules.Collection.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LandErp.Worker;

public sealed class CollectionSchedulerWorker(IServiceScopeFactory scopes, ILogger<CollectionSchedulerWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, Exception?> LogQueued = LoggerMessage.Define<int>(LogLevel.Information, new EventId(2, "COLLECTION_QUEUED"), "Collection scheduler queued {Count} due jobs");
    private static readonly Action<ILogger, Exception?> LogFailed = LoggerMessage.Define(LogLevel.Error, new EventId(3, "COLLECTION_SCHEDULER_FAILED"), "Collection scheduler tick failed");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                int created = await scope.ServiceProvider.GetRequiredService<ICollectionScheduler>().RunDueAsync(stoppingToken);
                if (created > 0) LogQueued(logger, created, null);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { LogFailed(logger, exception); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
