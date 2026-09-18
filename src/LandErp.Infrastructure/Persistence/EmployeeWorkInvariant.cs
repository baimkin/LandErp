using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Persistence;

/// <summary>Serializes changes that can add, remove or transfer active employee work inside one organization.</summary>
internal static class EmployeeWorkInvariant
{
    public static Task LockOrganizationAsync(LandErpDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Employee work invariant requires an active transaction.");
        string key = "EmployeeWork:" + organizationId.ToString("N");
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", cancellationToken);
    }
}
