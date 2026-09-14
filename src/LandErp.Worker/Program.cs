using LandErp.Application.Foundation;
using LandErp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);
builder.Services.AddLandErpPersistence(builder.Configuration);
using IHost host = builder.Build();
IDatabaseStatus database = host.Services.GetRequiredService<IDatabaseStatus>();
ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LandErp.Worker");
bool ready = await database.IsReadyAsync(CancellationToken.None);
Action<ILogger, string, bool, Exception?> logStarted = LoggerMessage.Define<string, bool>(
    LogLevel.Information, new EventId(1, "WORKER_STARTED"),
    "Worker skeleton started; environment {Environment}; database ready {Ready}");
logStarted(logger, builder.Environment.EnvironmentName, ready, null);
// Independent host lifetime, deliberately no collection jobs/scheduler in foundation.
await host.RunAsync();
