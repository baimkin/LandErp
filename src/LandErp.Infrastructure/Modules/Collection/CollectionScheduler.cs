using LandErp.Application.Foundation;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Collection;

public sealed class CollectionScheduler(IDbContextFactory<LandErpDbContext> factory, TimeProvider time) : ICollectionScheduler
{
    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = time.GetUtcNow();
        await RecordStartedAsync(startedAt, cancellationToken);
        try
        {
            int created = 0;
            while (true)
            {
                await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                DateTimeOffset now = time.GetUtcNow();
                var due = await db.SearchConfigurations.FromSqlInterpolated($"""
                    SELECT * FROM collection.search_configurations
                    WHERE enabled=true AND next_run_at IS NOT NULL AND next_run_at<={now}
                    ORDER BY next_run_at LIMIT 1 FOR UPDATE SKIP LOCKED
                    """).ToListAsync(cancellationToken);
                if (due.Count == 0)
                {
                    await transaction.CommitAsync(cancellationToken);
                    await RecordSucceededAsync(created, cancellationToken);
                    return created;
                }
                SearchConfiguration search = due[0];
                DateTimeOffset scheduledFor = search.NextRunAt!.Value;
                bool active = await db.CollectionJobs.AnyAsync(item => item.SearchId == search.Id &&
                    (item.State == CollectionJobState.Pending || item.State == CollectionJobState.Leased), cancellationToken);
                if (!active && !await db.CollectionJobs.AnyAsync(item => item.SearchId == search.Id && item.ScheduledFor == scheduledFor, cancellationToken))
                {
                    db.CollectionJobs.Add(new ServerCollectionJob { Id = DataConventions.NewId(), OrganizationId = search.OrganizationId,
                        SearchId = search.Id, State = CollectionJobState.Pending, CreatedAt = now, ScheduledFor = scheduledFor });
                    created++;
                }
                string zone = await db.Organizations.Where(item => item.Id == search.OrganizationId).Select(item => item.BusinessTimeZone).SingleAsync(cancellationToken);
                search.NextRunAt = CollectionScheduleRules.Next(search, now, zone);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            await TryRecordFailedAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task RecordStartedAsync(DateTimeOffset instant, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO collection.scheduler_status (id,last_started_at,last_queued_count,last_failure_code)
            VALUES (1,{instant},0,'') ON CONFLICT (id) DO UPDATE SET last_started_at=EXCLUDED.last_started_at
            """, cancellationToken);
    }

    private async Task RecordSucceededAsync(int queued, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE collection.scheduler_status SET last_succeeded_at={now},last_queued_count={queued},last_failure_code='' WHERE id=1
            """, cancellationToken);
    }

    private async Task TryRecordFailedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
            DateTimeOffset now = time.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE collection.scheduler_status SET last_failed_at={now},last_failure_code='COLLECTION_SCHEDULER_FAILED' WHERE id=1
                """, cancellationToken);
        }
        catch { /* The worker log remains the evidence when PostgreSQL itself is unavailable. */ }
    }
}
