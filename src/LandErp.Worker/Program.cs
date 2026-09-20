using LandErp.Application.Foundation;
using LandErp.Infrastructure.Persistence;
using LandErp.Infrastructure.Modules.Collection;
using LandErp.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
LoadExternalConfiguration(builder);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
builder.Services.AddLandErpPersistence(builder.Configuration);
builder.Services.AddLandErpCollection();
builder.Services.AddHostedService<CollectionSchedulerWorker>();
builder.Services.AddHostedService<PhotoFingerprintWorker>();
using IHost host = builder.Build();
IDatabaseStatus database = host.Services.GetRequiredService<IDatabaseStatus>();
ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LandErp.Worker");
bool ready = await database.IsReadyAsync(CancellationToken.None);
Action<ILogger, string, bool, Exception?> logStarted = LoggerMessage.Define<string, bool>(
    LogLevel.Information, new EventId(1, "WORKER_STARTED"),
    "Worker started with collection scheduler and photo fingerprinting; environment {Environment}; database ready {Ready}");
logStarted(logger, builder.Environment.EnvironmentName, ready, null);
await host.RunAsync();

static void LoadExternalConfiguration(HostApplicationBuilder builder)
{
    string? configured = Environment.GetEnvironmentVariable("LANDERP_CONFIG_FILE");
    if (string.IsNullOrWhiteSpace(configured))
    {
        if (builder.Environment.IsProduction())
            throw new InvalidOperationException("LANDERP_CONFIG_FILE is required in Production.");
        return;
    }
    string path = Path.GetFullPath(configured);
    if (!File.Exists(path)) throw new InvalidOperationException("External LandErp configuration file was not found.");
    builder.Configuration.AddJsonFile(path, optional: false, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
}
