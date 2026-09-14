using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace LandErp.Infrastructure.Persistence;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<LandErpDbContext>
{
    public LandErpDbContext CreateDbContext(string[] args)
    {
        string connection = Environment.GetEnvironmentVariable("LANDERP_MIGRATOR_CONNECTION") ?? "";
        PersistenceServices.ValidateConnection(connection);
        NpgsqlConnectionStringBuilder settings = new(connection);
        // This local tooling cannot accidentally target an arbitrary user/production database.
        if (settings.Database != "landerp_local" && settings.Database?.StartsWith("landerp_test_", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException("Migration tooling accepts only landerp_local or generated landerp_test_ databases.");
        }

        DbContextOptionsBuilder<LandErpDbContext> options = new();
        LandErpDbContext.Configure(options, connection);
        return new(options.Options);
    }
}
