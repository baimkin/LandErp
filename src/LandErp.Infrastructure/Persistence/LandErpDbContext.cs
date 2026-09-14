using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Persistence;

/// <summary>One migration stream; mappings belong to the module owning each schema.</summary>
public sealed class LandErpDbContext(DbContextOptions<LandErpDbContext> options) : DbContext(options)
{
    public const string FoundationSchema = "foundation";
    public const string HistoryTable = "migration_history";

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres => postgres
            .MigrationsHistoryTable(HistoryTable, FoundationSchema)
            .CommandTimeout(10));
}
