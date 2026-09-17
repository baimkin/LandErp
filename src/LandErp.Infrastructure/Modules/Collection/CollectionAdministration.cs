using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace LandErp.Infrastructure.Modules.Collection;

public sealed class CollectionAdministration(IDbContextFactory<LandErpDbContext> factory, IAccessControl access, TimeProvider time) : ICollectionAdministration
{
    private async Task<AccessContext> RequireAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.AgentsManage, cancellationToken);
        await access.RequireAsync(subject, Permissions.CollectionManage, cancellationToken);
        if (context.Scope != AccessScope.Organization) throw new AccessDeniedException();
        return context;
    }
    public async Task<CollectionAdminView> ReadAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset onlineSince = time.GetUtcNow().AddMinutes(-3);
        var rawAgents = await db.CollectorAgents.Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Name).ToArrayAsync(cancellationToken);
        var activeJobs = await (from job in db.CollectionJobs join search in db.SearchConfigurations on job.SearchId equals search.Id
            where job.OrganizationId == context.OrganizationId && job.State == CollectionJobState.Leased select new { job.AgentId, search.Label }).ToArrayAsync(cancellationToken);
        var agents = rawAgents.Select(item => { var current = activeJobs.FirstOrDefault(job => job.AgentId == item.Id); bool online = item.Enabled && item.LastHeartbeatAt >= onlineSince;
            return new AgentView(item.Id, item.Name, item.Enabled, online, item.CanManageSearches, item.VersionText, item.Capabilities, item.LastHeartbeatAt, item.Version,
                !item.Enabled ? "Отозван" : current != null ? "Выполняет сбор" : online ? "Готов" : "Нет связи", current?.Label); }).ToArray();
        var groups = await db.SearchGroups.Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(item => new SearchGroupView(item.Id, item.Name, item.SortOrder, item.Active, item.Version,
                db.SearchConfigurations.Count(search => search.SearchGroupId == item.Id))).ToArrayAsync(cancellationToken);
        var rawSearches = await db.SearchConfigurations.Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Label).ToArrayAsync(cancellationToken);
        var lastResults = await db.CollectionJobs.Where(item => item.OrganizationId == context.OrganizationId && item.CompletedAt != null)
            .GroupBy(item => item.SearchId).Select(group => new { SearchId = group.Key, Job = group.OrderByDescending(item => item.CompletedAt).First() }).ToArrayAsync(cancellationToken);
        var searches = rawSearches.Select(item => new SearchView(item.Id, item.Label, item.Source, item.Url, item.MaxPages, item.SearchGroupId,
            groups.FirstOrDefault(group => group.Id == item.SearchGroupId)?.Name ?? "Без группы", CollectionScheduleRules.Display(item), item.ScheduleKind, item.IntervalMinutes,
            System.Text.Json.JsonSerializer.Deserialize<string[]>(item.FixedTimesJson) ?? [], item.Enabled,
            item.NextRunAt, item.Version, JobLabel(lastResults.FirstOrDefault(result => result.SearchId == item.Id)?.Job.State))).ToArray();
        var jobs = await (from job in db.CollectionJobs join search in db.SearchConfigurations on job.SearchId equals search.Id
                          join agentValue in db.CollectorAgents on job.AgentId equals (Guid?)agentValue.Id into agentValues
                          from agent in agentValues.DefaultIfEmpty() where job.OrganizationId == context.OrganizationId
                          orderby job.CreatedAt descending select new CollectionJobView(job.Id, search.Label, agent == null ? null : agent.Name,
                              job.State.ToString(), job.CreatedAt, job.LeaseExpiresAt, job.ResultCode, job.ProcessedCount, job.AcceptedCount,
                              job.NewListingsCount, job.ChangedListingsCount)).Take(100).ToArrayAsync(cancellationToken);
        return new(agents, groups, searches, jobs, searches.Count(item => item.Enabled), jobs.Count(item => item.State == "Pending"),
            jobs.Count(item => item.State is "AwaitingManualAction" or "Failed" or "Interrupted"));
    }
    public async Task<AgentCredential> CreateAgentAsync(Subject subject, string name, bool canManageSearches, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        CollectorAgent agent = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            Name = OrganizationWorkspace.ValidateName(name), CredentialHash = CollectorGateway.Hash(token), CanManageSearches = canManageSearches };
        db.CollectorAgents.Add(agent);
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectorAgentCreated", "CollectorAgent", agent.Id,
            new { agent.Name, agent.CanManageSearches }, correlationId);
        await db.SaveChangesAsync(cancellationToken); return new(agent.Id, token);
    }
    public async Task RevokeAgentAsync(Subject subject, Guid agentId, long expectedVersion, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        CollectorAgent agent = await db.CollectorAgents.SingleOrDefaultAsync(item => item.Id == agentId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (agent.Version != expectedVersion) throw new DbUpdateConcurrencyException();
        agent.Enabled = false;
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectorAgentRevoked", "CollectorAgent", agent.Id, new { agent.Enabled }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task SetAgentSearchManagementAsync(Subject subject, Guid agentId, long expectedVersion, bool allowed,
        string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        CollectorAgent agent = await db.CollectorAgents.SingleOrDefaultAsync(item => item.Id == agentId
            && item.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (agent.Version != expectedVersion) throw new DbUpdateConcurrencyException();
        agent.CanManageSearches = allowed;
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectorSearchManagementChanged", "CollectorAgent", agent.Id,
            new { agent.Name, CanManageSearches = allowed }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task CreateSearchAsync(Subject subject, CreateSearch command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        if (!ContractRules.IsSourceUrl(command.Url, CollectorSource(command.Source)) || command.Url.Length > 2000 || command.MaxPages is < 1 or > 100)
            throw new ArgumentException("Укажите публичную HTTPS-ссылку Avito/Cian и предел 1–100 страниц.");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        string zone = await db.Organizations.Where(item => item.Id == context.OrganizationId).Select(item => item.BusinessTimeZone).SingleAsync(cancellationToken);
        if (command.SearchGroupId != null && !await db.SearchGroups.AnyAsync(item => item.Id == command.SearchGroupId && item.OrganizationId == context.OrganizationId && item.Active, cancellationToken)) throw new AccessDeniedException();
        SearchConfiguration search = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            Label = OrganizationWorkspace.ValidateName(command.Label), Source = command.Source, Url = command.Url, MaxPages = command.MaxPages, SearchGroupId = command.SearchGroupId };
        CollectionScheduleRules.Apply(search, command.Schedule, time.GetUtcNow(), zone);
        db.SearchConfigurations.Add(search);
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectionSearchCreated", "SearchConfiguration", search.Id,
            new { search.Label, search.Source, search.MaxPages }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task UpdateSearchAsync(Subject subject, UpdateSearch command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        if (!ContractRules.IsSourceUrl(command.Url, CollectorSource(command.Source)) || command.Url.Length > 2000 || command.MaxPages is < 1 or > 100) throw new ArgumentException("Параметры поиска некорректны.");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        SearchConfiguration search = await db.SearchConfigurations.SingleOrDefaultAsync(item => item.Id == command.Id && item.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (search.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException();
        if (command.SearchGroupId != null && !await db.SearchGroups.AnyAsync(item => item.Id == command.SearchGroupId && item.OrganizationId == context.OrganizationId && item.Active, cancellationToken)) throw new AccessDeniedException();
        search.Label = OrganizationWorkspace.ValidateName(command.Label); search.Source = command.Source; search.Url = command.Url;
        search.MaxPages = command.MaxPages; search.SearchGroupId = command.SearchGroupId; search.Enabled = command.Enabled;
        string zone = await db.Organizations.Where(item => item.Id == context.OrganizationId).Select(item => item.BusinessTimeZone).SingleAsync(cancellationToken);
        CollectionScheduleRules.Apply(search, command.Schedule, time.GetUtcNow(), zone);
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectionSearchUpdated", "SearchConfiguration", search.Id, new { search.Enabled, search.ScheduleKind }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<Guid> CreateGroupAsync(Subject subject, string name, int sortOrder, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        if (sortOrder is < 0 or > 10000) throw new ArgumentException("Порядок группы должен быть от 0 до 10000.");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        SearchGroup group = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, Name = OrganizationWorkspace.ValidateName(name), SortOrder = sortOrder, RecordedAt = time.GetUtcNow() };
        db.SearchGroups.Add(group);
        db.SearchGroupMarketSettings.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, SearchGroupId = group.Id,
            PeriodDays = 30, AllowedPropertyTypes = Enum.GetNames<IncomingLandType>()
        });
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectionSearchGroupCreated", "SearchGroup", group.Id, new { group.Name, group.SortOrder }, correlationId);
        await db.SaveChangesAsync(cancellationToken); return group.Id;
    }
    public async Task ArchiveGroupAsync(Subject subject, Guid groupId, long expectedVersion, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        SearchGroup group = await db.SearchGroups.SingleOrDefaultAsync(item => item.Id == groupId && item.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (group.Version != expectedVersion) throw new DbUpdateConcurrencyException();
        if (await db.SearchConfigurations.AnyAsync(item => item.SearchGroupId == groupId && item.Enabled, cancellationToken)) throw new ArgumentException("Сначала приостановите активные поиски группы.");
        group.Active = false; OrganizationWorkspace.AddAudit(db, context, subject, "CollectionSearchGroupArchived", "SearchGroup", group.Id, new { group.Name }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<AgentCredential> RotateCredentialAsync(Subject subject, Guid agentId, long expectedVersion, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        CollectorAgent agent = await db.CollectorAgents.SingleOrDefaultAsync(item => item.Id == agentId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (agent.Version != expectedVersion) throw new DbUpdateConcurrencyException();
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        agent.CredentialHash = CollectorGateway.Hash(token); agent.Enabled = true;
        agent.RegisteredAt = null; agent.LastHeartbeatAt = null;
        // A new credential must register its installed version/capabilities before obtaining work.
        agent.VersionText = ""; agent.Capabilities = "";
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectorCredentialRotated", "CollectorAgent", agent.Id,
            new { agent.Name, PreviousRevision = expectedVersion }, correlationId);
        await db.SaveChangesAsync(cancellationToken); return new(agent.Id, token);
    }
    public async Task EnqueueAsync(Subject subject, Guid searchId, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var searches = await db.SearchConfigurations.FromSqlInterpolated($"SELECT * FROM collection.search_configurations WHERE id={searchId} AND organization_id={context.OrganizationId} FOR UPDATE").ToListAsync(cancellationToken);
        SearchConfiguration search = searches.SingleOrDefault() ?? throw new AccessDeniedException();
        if (!search.Enabled) throw new AccessDeniedException();
        if (await db.CollectionJobs.AnyAsync(item => item.SearchId == searchId && (item.State == CollectionJobState.Pending || item.State == CollectionJobState.Leased), cancellationToken))
            throw new ArgumentException("Для поиска уже есть активная работа.");
        ServerCollectionJob job = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            SearchId = search.Id, AgentId = null, CreatedAt = time.GetUtcNow(), State = CollectionJobState.Pending };
        db.CollectionJobs.Add(job);
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectionJobQueued", "CollectionJob", job.Id, new { job.SearchId, job.AgentId }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    private static string JobLabel(CollectionJobState? state) => state switch
    {
        null => "Ещё не запускался", CollectionJobState.Completed => "Завершён", CollectionJobState.LimitReached => "Достигнут предел",
        CollectionJobState.AwaitingManualAction => "Требуется внимание", CollectionJobState.Failed => "Ошибка", CollectionJobState.Interrupted => "Прерван",
        CollectionJobState.Pending => "Ожидает", _ => "Выполняется"
    };

    private static ListingSource CollectorSource(CatalogSource source) => source switch
    {
        CatalogSource.Avito => ListingSource.Avito,
        CatalogSource.Cian => ListingSource.Cian,
        _ => throw new ArgumentException("Collector поддерживает только Avito/Cian.")
    };
}
