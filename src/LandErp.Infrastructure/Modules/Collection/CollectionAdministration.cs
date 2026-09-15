using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Domain;
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
        if (context.Scope != AccessScope.Organization) throw new AccessDeniedException();
        return context;
    }
    public async Task<CollectionAdminView> ReadAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset onlineSince = time.GetUtcNow().AddMinutes(-3);
        var agents = await db.CollectorAgents.Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Name)
            .Select(item => new AgentView(item.Id, item.Name, item.Enabled, item.Enabled && item.LastHeartbeatAt >= onlineSince,
                item.VersionText, item.Capabilities, item.LastHeartbeatAt, item.Version)).ToArrayAsync(cancellationToken);
        var searches = await db.SearchConfigurations.Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Label)
            .Select(item => new SearchView(item.Id, item.Label, item.Source, item.Url, item.MaxPages)).ToArrayAsync(cancellationToken);
        var jobs = await (from job in db.CollectionJobs join search in db.SearchConfigurations on job.SearchId equals search.Id
                          join agentValue in db.CollectorAgents on job.AgentId equals (Guid?)agentValue.Id into agentValues
                          from agent in agentValues.DefaultIfEmpty() where job.OrganizationId == context.OrganizationId
                          orderby job.CreatedAt descending select new CollectionJobView(job.Id, search.Label, agent == null ? null : agent.Name,
                              job.State.ToString(), job.CreatedAt, job.LeaseExpiresAt, job.ResultCode, job.AcceptedCount)).Take(100).ToArrayAsync(cancellationToken);
        return new(agents, searches, jobs);
    }
    public async Task<AgentCredential> CreateAgentAsync(Subject subject, string name, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        CollectorAgent agent = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            Name = OrganizationWorkspace.ValidateName(name), CredentialHash = CollectorGateway.Hash(token) };
        db.CollectorAgents.Add(agent);
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectorAgentCreated", "CollectorAgent", agent.Id, new { agent.Name }, correlationId);
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
    public async Task CreateSearchAsync(Subject subject, CreateSearch command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireAsync(subject, cancellationToken);
        await access.RequireAsync(subject, Permissions.CollectionManage, cancellationToken);
        if (!ContractRules.IsSourceUrl(command.Url, CollectorSource(command.Source)) || command.Url.Length > 2000 || command.MaxPages is < 1 or > 100)
            throw new ArgumentException("Укажите публичную HTTPS-ссылку Avito/Cian и предел 1–100 страниц.");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        SearchConfiguration search = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            Label = OrganizationWorkspace.ValidateName(command.Label), Source = command.Source, Url = command.Url, MaxPages = command.MaxPages };
        db.SearchConfigurations.Add(search);
        OrganizationWorkspace.AddAudit(db, context, subject, "CollectionSearchCreated", "SearchConfiguration", search.Id,
            new { search.Label, search.Source, search.MaxPages }, correlationId);
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
        await access.RequireAsync(subject, Permissions.CollectionManage, cancellationToken);
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

    private static ListingSource CollectorSource(CatalogSource source) => source switch
    {
        CatalogSource.Avito => ListingSource.Avito,
        CatalogSource.Cian => ListingSource.Cian,
        _ => throw new ArgumentException("Collector поддерживает только Avito/Cian.")
    };
}
