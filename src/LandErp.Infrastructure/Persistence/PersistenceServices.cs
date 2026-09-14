using LandErp.Application.Foundation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LandErp.Infrastructure.Persistence;

public static class PersistenceServices
{
    public static IServiceCollection AddLandErpPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration["Database:ConnectionString"] ?? "";
        ValidateConnection(connectionString);
        services.AddDbContextFactory<LandErpDbContext>(options =>
            LandErpDbContext.Configure(options, connectionString));
        services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<IDatabaseStatus, PostgresDatabaseStatus>();
        return services;
    }

    public static void ValidateConnection(string connectionString)
    {
        try
        {
            NpgsqlConnectionStringBuilder connection = new(connectionString);
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)
                || string.IsNullOrWhiteSpace(connection.Username) || connection.IncludeErrorDetail
                || connection.LogParameters)
            {
                throw new ArgumentException("Invalid database configuration.");
            }
        }
        catch (ArgumentException)
        {
            // Never include the input or provider exception: either may contain credentials.
            throw new InvalidOperationException("Database configuration is missing or invalid. Use the approved secret boundary.");
        }
    }

    private sealed class PostgresDatabaseStatus(NpgsqlDataSource dataSource,
        ILogger<PostgresDatabaseStatus> logger) : IDatabaseStatus
    {
        private static readonly Action<ILogger, Exception?> LogUnavailable = LoggerMessage.Define(
            LogLevel.Warning, new EventId(1, "DB_NOT_READY"), "Database readiness unavailable; code DB_NOT_READY");
        public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            try
            {
                await using NpgsqlCommand command = dataSource.CreateCommand(
                    "SELECT current_setting('server_version_num')::integer >= 180000 "
                    + "AND current_setting('server_version_num')::integer < 190000 "
                    + "AND to_regclass('foundation.migration_history') IS NOT NULL");
                command.CommandTimeout = 2;
                return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
            }
            catch (Exception exception) when (exception is NpgsqlException or TimeoutException or OperationCanceledException)
            {
                LogUnavailable(logger, null);
                return false;
            }
        }
    }
}
