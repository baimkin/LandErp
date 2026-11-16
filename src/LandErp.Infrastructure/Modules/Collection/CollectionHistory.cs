using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Collection;

public sealed partial class CollectionAdministration
{
    public async Task<CollectionHistoryPage> ReadHistoryAsync(Subject subject, CollectionHistoryQuery query, CancellationToken cancellationToken)
    {
        var context = await RequireAsync(subject, manage: false, cancellationToken);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Size is < 1 or > 1000 || !Enum.IsDefined(query.Result)
            || query.Source is { } source && !Enum.IsDefined(source) || query.Text is null || query.Text.Length > 200)
            throw new ArgumentException("Параметры истории некорректны.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = from job in db.CollectionJobs.AsNoTracking()
                   join search in db.SearchConfigurations on job.SearchId equals search.Id
                   join agentValue in db.CollectorAgents.Where(a => a.OrganizationId == context.OrganizationId)
                       on job.AgentId equals (Guid?)agentValue.Id into agents
                   from agent in agents.DefaultIfEmpty()
                   join groupValue in db.SearchGroups.Where(g => g.OrganizationId == context.OrganizationId)
                       on search.SearchGroupId equals (Guid?)groupValue.Id into groups
                   from zoneGroup in groups.DefaultIfEmpty()
                   where job.OrganizationId == context.OrganizationId && search.OrganizationId == context.OrganizationId
                   select new { Job = job, search.Label, search.Source, Agent = agent == null ? null : agent.Name, Group = zoneGroup == null ? "" : zoneGroup.Name };
        string text = query.Text.Trim();
        if (text.Length > 0)
        {
            string pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            rows = rows.Where(r => EF.Functions.ILike(r.Label, pattern, "\\") || EF.Functions.ILike(r.Group, pattern, "\\")
                || r.Agent != null && EF.Functions.ILike(r.Agent, pattern, "\\"));
        }
        if (query.Source is { } selectedSource) rows = rows.Where(r => r.Source == selectedSource);
        rows = query.Result switch
        {
            CollectionHistoryResult.Completed => rows.Where(r => r.Job.State == CollectionJobState.Completed || r.Job.State == CollectionJobState.LimitReached),
            CollectionHistoryResult.Running => rows.Where(r => r.Job.State == CollectionJobState.Pending || r.Job.State == CollectionJobState.Leased),
            CollectionHistoryResult.Attention => rows.Where(r => r.Job.State != CollectionJobState.Completed && r.Job.State != CollectionJobState.LimitReached
                && r.Job.State != CollectionJobState.Pending && (r.Job.State != CollectionJobState.Leased
                    || r.Job.ResultCode == "Captcha" || r.Job.ResultCode == "AuthenticationRequired" || r.Job.ResultCode == "RateLimited")),
            _ => rows
        };
        int total = await rows.CountAsync(cancellationToken);
        // Time plus ID is stable: concurrent newer jobs cannot shift an offset or duplicate a page.
        if (query.Before is { } cursor)
            rows = rows.Where(r => r.Job.CreatedAt < cursor.CreatedAt || r.Job.CreatedAt == cursor.CreatedAt && r.Job.Id.CompareTo(cursor.Id) < 0);
        var page = await rows.OrderByDescending(r => r.Job.CreatedAt).ThenByDescending(r => r.Job.Id)
            .Take(query.Size + 1).ToArrayAsync(cancellationToken);
        var items = page.Take(query.Size).Select(r => Job(r.Job, r.Label, r.Agent)).ToArray();
        CollectionHistoryCursor? next = page.Length > query.Size && items.Length > 0 ? new(items[^1].CreatedAt, items[^1].Id) : null;
        return new(items, total, next);
    }
}
