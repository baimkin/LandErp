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
    private async Task<AccessContext> RequireAsync(Subject subject, string permission, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, permission, cancellationToken);
        if (context.Scope != AccessScope.Organization) throw new AccessDeniedException();
        return context;
    }
    public async Task<CollectionAdminView> ReadAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, Permissions.CollectionRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset onlineSince = now.AddMinutes(-3);
        var rawAgents = await db.CollectorAgents.Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Name).ToArrayAsync(cancellationToken);
        var activeJobs = await (from job in db.CollectionJobs join search in db.SearchConfigurations on job.SearchId equals search.Id
            where job.OrganizationId == context.OrganizationId && job.State == CollectionJobState.Leased && job.LeaseExpiresAt > now
            select new { job.AgentId, search.Label }).ToArrayAsync(cancellationToken);
        var agents = rawAgents.Select(item => { var current = activeJobs.FirstOrDefault(job => job.AgentId == item.Id); bool online = item.Enabled && item.LastHeartbeatAt >= onlineSince;
            return new AgentView(item.Id, item.Name, item.Enabled, online, item.CanManageSearches, item.VersionText, item.Capabilities, item.LastHeartbeatAt, item.Version,
                AgentStatus(item, current != null, online), current?.Label, item.RuntimeState, item.AttentionCode,
                item.ProgressProcessed, item.ProgressTotal, item.ProgressCurrentPage, item.ProgressMaxPages, item.LastActivityAt); }).ToArray();
        var groups = await db.SearchGroups.Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(item => new SearchGroupView(item.Id, item.Name, item.SortOrder, item.Active, item.Version,
                db.SearchConfigurations.Count(search => search.SearchGroupId == item.Id))).ToArrayAsync(cancellationToken);
        var rawSearches = await db.SearchConfigurations.Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Label).ToArrayAsync(cancellationToken);
        var lastResults = await (from job in db.CollectionJobs
            join agentValue in db.CollectorAgents on job.AgentId equals (Guid?)agentValue.Id into agentValues
            from agent in agentValues.DefaultIfEmpty()
            where job.OrganizationId == context.OrganizationId && job.CompletedAt != null
            orderby job.CompletedAt descending, job.CreatedAt descending, job.Id descending
            select new { Job = job, Agent = agent == null ? null : agent.Name }).ToArrayAsync(cancellationToken);
        var lastBySearch = lastResults.GroupBy(item => item.Job.SearchId).ToDictionary(group => group.Key, group => group.First());
        var searches = rawSearches.Select(item => new SearchView(item.Id, item.Label, item.Source, item.Url, item.MaxPages, item.SearchGroupId,
            groups.FirstOrDefault(group => group.Id == item.SearchGroupId)?.Name ?? "Без группы", CollectionScheduleRules.Display(item), item.ScheduleKind, item.IntervalMinutes,
            System.Text.Json.JsonSerializer.Deserialize<string[]>(item.FixedTimesJson) ?? [], item.Enabled,
            item.NextRunAt, item.Version, lastBySearch.TryGetValue(item.Id, out var last) ? Run(last.Job, last.Agent) : null)).ToArray();
        var rawJobs = await (from job in db.CollectionJobs join search in db.SearchConfigurations on job.SearchId equals search.Id
                             join agentValue in db.CollectorAgents on job.AgentId equals (Guid?)agentValue.Id into agentValues
                             from agent in agentValues.DefaultIfEmpty() where job.OrganizationId == context.OrganizationId
                             orderby job.CreatedAt descending, job.Id descending select new { Job = job, search.Label, Agent = agent == null ? null : agent.Name })
            .Take(100).ToArrayAsync(cancellationToken);
        CollectionJobView[] jobs = rawJobs.Select(item => Job(item.Job, item.Label, item.Agent)).ToArray();
        int pending = await db.CollectionJobs.CountAsync(item => item.OrganizationId == context.OrganizationId && item.State == CollectionJobState.Pending, cancellationToken);
        int attention = lastBySearch.Values.Count(item => item.Job.RequiresOperatorAttention);
        CollectionSchedulerStatus? scheduler = await db.CollectionSchedulerStatuses.AsNoTracking().SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        CollectionSchedulerHealthView schedulerView = Scheduler(scheduler, now);
        string businessTimeZone = await db.Organizations.Where(item => item.Id == context.OrganizationId)
            .Select(item => item.BusinessTimeZone).SingleAsync(cancellationToken);
        return new(agents, groups, searches, jobs, searches.Count(item => item.Enabled), pending, attention,
            agents.Count(item => item.Online), activeJobs.Length, schedulerView, businessTimeZone);
    }
    public async Task<AgentCredential> CreateAgentAsync(Subject subject, string name, bool canManageSearches, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, Permissions.AgentsManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        CollectorAgent agent = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            Name = OrganizationWorkspace.ValidateName(name), CredentialHash = CollectorGateway.Hash(token), CanManageSearches = canManageSearches };
        db.CollectorAgents.Add(agent);
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectorAgentCreated", "CollectorAgent", agent.Id,
            new { agent.Name, agent.CanManageSearches }, correlationId);
        await db.SaveChangesAsync(cancellationToken); return new(agent.Id, token);
    }
    public async Task<AgentConnectionCode> CreateConnectionCodeAsync(Subject subject, string name, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, Permissions.AgentsManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        DateTimeOffset expiresAt = time.GetUtcNow().AddMinutes(15);
        CollectorAgent agent = new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            Name = OrganizationWorkspace.ValidateName(name), CredentialHash = "",
            ActivationHash = CollectorGateway.Hash(secret), ActivationExpiresAt = expiresAt
        };
        db.CollectorAgents.Add(agent);
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectorConnectionCodeCreated", "CollectorAgent", agent.Id,
            new { agent.Name, ExpiresAt = expiresAt }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        return new(agent.Id, secret, expiresAt);
    }
    public async Task RevokeAgentAsync(Subject subject, Guid agentId, long expectedVersion, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, Permissions.AgentsManage, cancellationToken);
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
        AccessContext context = await RequireAsync(subject, Permissions.AgentsManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        CollectorAgent agent = await db.CollectorAgents.SingleOrDefaultAsync(item => item.Id == agentId
            && item.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (agent.Version != expectedVersion) throw new DbUpdateConcurrencyException();
        agent.CanManageSearches = allowed;
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectorSearchManagementChanged", "CollectorAgent", agent.Id,
            new { agent.Name, CanManageSearches = allowed }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<DateTimeOffset?> PreviewScheduleAsync(Subject subject, CollectionSchedule schedule, Guid? searchId,
        bool enabled, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, Permissions.CollectionManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        string zone = await db.Organizations.Where(x => x.Id == context.OrganizationId)
            .Select(x => x.BusinessTimeZone).SingleAsync(cancellationToken);
        SearchConfiguration search = searchId.HasValue
            ? await db.SearchConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == searchId && x.OrganizationId == context.OrganizationId, cancellationToken)
                ?? throw new AccessDeniedException()
            : new SearchConfiguration();
        bool preserve = searchId.HasValue && search.Enabled == enabled;
        search.Enabled = enabled;
        CollectionScheduleRules.Apply(search, schedule, time.GetUtcNow(), zone, preserve);
        return search.NextRunAt;
    }

    public async Task CreateSearchAsync(Subject subject, CreateSearch command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, Permissions.CollectionManage, cancellationToken);
        if (!ContractRules.IsSourceUrl(command.Url, CollectorSource(command.Source)) || command.Url.Length > 2000 || command.MaxPages is < 1 or > 100)
            throw new ArgumentException("Укажите публичную HTTPS-ссылку Avito/Cian и предел 1–100 страниц.");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        string zone = await db.Organizations.Where(item => item.Id == context.OrganizationId).Select(item => item.BusinessTimeZone).SingleAsync(cancellationToken);
        if (command.SearchGroupId != null && !await db.SearchGroups.AnyAsync(item => item.Id == command.SearchGroupId && item.OrganizationId == context.OrganizationId && item.Active, cancellationToken)) throw new AccessDeniedException();
        SearchConfiguration search = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            Label = OrganizationWorkspace.ValidateName(command.Label), Source = command.Source, Url = command.Url, MaxPages = command.MaxPages, SearchGroupId = command.SearchGroupId };
        CollectionScheduleRules.Apply(search, command.Schedule, time.GetUtcNow(), zone);
        db.SearchConfigurations.Add(search);
        // One SaveChanges transaction persists both the search and its optional first job.
        // A failed validation/save cannot leave an orphan search or enqueue twice.
        if (command.RunImmediately)
        {
            ServerCollectionJob job = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
                SearchId = search.Id, CreatedAt = time.GetUtcNow(), State = CollectionJobState.Pending };
            db.CollectionJobs.Add(job);
            OrganizationWorkspace.AddAudit(db, context, subject, "CollectionJobQueued", "CollectionJob", job.Id,
                new { job.SearchId, job.AgentId }, correlationId);
        }
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectionSearchCreated", "SearchConfiguration", search.Id,
            new { search.Label, search.Source, search.MaxPages }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task UpdateSearchAsync(Subject subject, UpdateSearch command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, Permissions.CollectionManage, cancellationToken);
        if (!ContractRules.IsSourceUrl(command.Url, CollectorSource(command.Source)) || command.Url.Length > 2000 || command.MaxPages is < 1 or > 100) throw new ArgumentException("Параметры поиска некорректны.");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        SearchConfiguration search = await db.SearchConfigurations.SingleOrDefaultAsync(item => item.Id == command.Id && item.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (search.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException();
        if (command.SearchGroupId != null && !await db.SearchGroups.AnyAsync(item => item.Id == command.SearchGroupId &&
            item.OrganizationId == context.OrganizationId && (item.Active || item.Id == search.SearchGroupId), cancellationToken)) throw new AccessDeniedException();
        if (command.Enabled && command.SearchGroupId != null && !await db.SearchGroups.AnyAsync(item =>
            item.Id == command.SearchGroupId && item.OrganizationId == context.OrganizationId && item.Active, cancellationToken))
            throw new ArgumentException("Чтобы включить поиск, перенесите его из архива в действующую группу или выберите «Без группы».");
        bool sameEnabled = search.Enabled == command.Enabled;
        search.Label = OrganizationWorkspace.ValidateName(command.Label); search.Source = command.Source; search.Url = command.Url;
        search.MaxPages = command.MaxPages; search.SearchGroupId = command.SearchGroupId; search.Enabled = command.Enabled;
        string zone = await db.Organizations.Where(item => item.Id == context.OrganizationId).Select(item => item.BusinessTimeZone).SingleAsync(cancellationToken);
        CollectionScheduleRules.Apply(search, command.Schedule, time.GetUtcNow(), zone, preserveNextRun: sameEnabled);
        if (!search.Enabled)
        {
            ServerCollectionJob[] retries = await db.CollectionJobs
                .Where(item => item.SearchId == search.Id && item.RetryAt != null)
                .ToArrayAsync(cancellationToken);
            foreach (ServerCollectionJob retry in retries) retry.RetryAt = null;
        }
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectionSearchUpdated", "SearchConfiguration", search.Id, new { search.Enabled, search.ScheduleKind }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<Guid> CreateGroupAsync(Subject subject, string name, int sortOrder, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, Permissions.CollectionManage, cancellationToken);
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
        AccessContext context = await RequireAsync(subject, Permissions.CollectionManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        SearchGroup group = await db.SearchGroups.SingleOrDefaultAsync(item => item.Id == groupId && item.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (group.Version != expectedVersion) throw new DbUpdateConcurrencyException();
        if (await db.SearchConfigurations.AnyAsync(item => item.SearchGroupId == groupId && item.Enabled, cancellationToken)) throw new ArgumentException("Сначала приостановите активные поиски группы.");
        group.Active = false; OrganizationWorkspace.AddAudit(db, context, subject, "CollectionSearchGroupArchived", "SearchGroup", group.Id, new { group.Name }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<AgentCredential> RotateCredentialAsync(Subject subject, Guid agentId, long expectedVersion, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, Permissions.AgentsManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        CollectorAgent agent = await db.CollectorAgents.SingleOrDefaultAsync(item => item.Id == agentId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (agent.Version != expectedVersion) throw new DbUpdateConcurrencyException();
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        agent.CredentialHash = CollectorGateway.Hash(token); agent.Enabled = true;
        agent.ActivationHash = ""; agent.ActivationExpiresAt = null; agent.ActivationUsedAt = null;
        agent.RegisteredAt = null; agent.LastHeartbeatAt = null;
        // A new credential must register its installed version/capabilities before obtaining work.
        agent.VersionText = ""; agent.Capabilities = "";
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectorCredentialRotated", "CollectorAgent", agent.Id,
            new { agent.Name, PreviousRevision = expectedVersion }, correlationId);
        await db.SaveChangesAsync(cancellationToken); return new(agent.Id, token);
    }
    public async Task EnqueueAsync(Subject subject, Guid searchId, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, Permissions.CollectionManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var searches = await db.SearchConfigurations.FromSqlInterpolated($"SELECT * FROM collection.search_configurations WHERE id={searchId} AND organization_id={context.OrganizationId} FOR UPDATE").ToListAsync(cancellationToken);
        SearchConfiguration search = searches.SingleOrDefault() ?? throw new AccessDeniedException();
        if (!search.Enabled) throw new AccessDeniedException();
        if (await db.CollectionJobs.AnyAsync(item => item.SearchId == searchId && (item.State == CollectionJobState.Pending || item.State == CollectionJobState.Leased), cancellationToken))
            throw new ArgumentException("Для поиска уже есть активная работа.");
        ServerCollectionJob[] retries = await db.CollectionJobs.Where(item => item.SearchId == searchId && item.RetryAt != null).ToArrayAsync(cancellationToken);
        foreach (ServerCollectionJob retry in retries) retry.RetryAt = null;
        ServerCollectionJob job = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            SearchId = search.Id, AgentId = null, CreatedAt = time.GetUtcNow(), State = CollectionJobState.Pending };
        db.CollectionJobs.Add(job);
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectionJobQueued", "CollectionJob", job.Id, new { job.SearchId, job.AgentId }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    private static CollectionRunView Run(ServerCollectionJob job, string? agent) => new(job.Id, agent,
        job.State.ToString(), job.CreatedAt, job.ScheduledFor, job.CompletedAt, job.ResultCode, job.ProcessedCount,
        job.AcceptedCount, job.NewListingsCount, job.ChangedListingsCount, job.ReasonCode, Warnings(job), Coverage(job),
        job.RetryAttempt, job.RetryAt, job.RequiresOperatorAttention);

    private static CollectionJobView Job(ServerCollectionJob job, string label, string? agent) => new(job.Id, job.SearchId,
        label, agent, job.State.ToString(), job.CreatedAt, job.ScheduledFor, job.LeaseExpiresAt, job.CompletedAt,
        job.ResultCode, job.ProcessedCount, job.AcceptedCount, job.NewListingsCount, job.ChangedListingsCount,
        job.ReasonCode, Warnings(job), Coverage(job), job.RetryAttempt, job.RetryAt, job.RequiresOperatorAttention);

    private static string[] Warnings(ServerCollectionJob job) =>
        System.Text.Json.JsonSerializer.Deserialize<string[]>(job.WarningsJson, CollectionJson.Options) ?? [];

    private static CollectionCoverage? Coverage(ServerCollectionJob job) => job.CoverageJson is null ? null
        : System.Text.Json.JsonSerializer.Deserialize<CollectionCoverage>(job.CoverageJson, CollectionJson.Options);

    private static CollectionSchedulerHealthView Scheduler(CollectionSchedulerStatus? status, DateTimeOffset now)
    {
        string state = status switch
        {
            null or { LastStartedAt: null } => "Не запускался",
            { LastFailedAt: not null } value when value.LastSucceededAt == null || value.LastFailedAt > value.LastSucceededAt => "Ошибка",
            { LastSucceededAt: not null } value when value.LastSucceededAt < now.AddMinutes(-2) => "Нет связи",
            _ => "Работает"
        };
        return new(state, status?.LastStartedAt, status?.LastSucceededAt, status?.LastFailedAt,
            status?.LastQueuedCount ?? 0, status?.LastFailureCode ?? "");
    }

    private static string AgentStatus(CollectorAgent agent, bool hasActiveWork, bool online)
    {
        if (!agent.Enabled) return "Отозван";
        if (!string.IsNullOrEmpty(agent.AttentionCode)) return agent.AttentionCode switch
        {
            "Captcha" => "CAPTCHA", "AuthenticationRequired" => "Требуется вход",
            "RateLimited" => "Источник ограничил запросы", _ => "Требуется внимание"
        };
        if (!online) return agent.ActivationUsedAt == null && agent.RegisteredAt == null ? "Ожидает подключения" : "Нет связи";
        return hasActiveWork || agent.RuntimeState is AgentRuntimeState.Parsing or AgentRuntimeState.Delivering
            ? "Выполняет сбор" : "Готов";
    }

    private static ListingSource CollectorSource(CatalogSource source) => source switch
    {
        CatalogSource.Avito => ListingSource.Avito,
        CatalogSource.Cian => ListingSource.Cian,
        _ => throw new ArgumentException("Collector поддерживает только Avito/Cian.")
    };
}
