using Microsoft.EntityFrameworkCore;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Application.Modules.Organization.Domain;
using LandErp.Infrastructure.Modules.IdentityAccess;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Infrastructure.Modules.Collection;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.Procurement;

namespace LandErp.Infrastructure.Persistence;

/// <summary>One migration stream; mappings belong to the module owning each schema.</summary>
public sealed class LandErpDbContext(DbContextOptions<LandErpDbContext> options)
    : IdentityDbContext<LandErpUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeAssignment> EmployeeAssignments => Set<EmployeeAssignment>();
    public DbSet<PermissionDefinition> Permissions => Set<PermissionDefinition>();
    public DbSet<RolePermissionGrant> RolePermissions => Set<RolePermissionGrant>();
    public DbSet<EmployeeInvitation> EmployeeInvitations => Set<EmployeeInvitation>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<CollectorAgent> CollectorAgents => Set<CollectorAgent>();
    public DbSet<SearchConfiguration> SearchConfigurations => Set<SearchConfiguration>();
    public DbSet<SearchGroup> SearchGroups => Set<SearchGroup>();
    public DbSet<ServerCollectionJob> CollectionJobs => Set<ServerCollectionJob>();
    public DbSet<CollectionDelivery> CollectionDeliveries => Set<CollectionDelivery>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<CatalogObservation> ListingObservations => Set<CatalogObservation>();
    public DbSet<CatalogEvent> CatalogEvents => Set<CatalogEvent>();
    public DbSet<PropertyCase> PropertyCases => Set<PropertyCase>();
    public DbSet<PropertyCaseSourceLink> PropertyCaseSourceLinks => Set<PropertyCaseSourceLink>();
    public DbSet<Assignment> WorkAssignments => Set<Assignment>();
    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();
    public DbSet<WorkflowTransition> WorkflowTransitions => Set<WorkflowTransition>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<BusinessTimelineEntry> BusinessTimeline => Set<BusinessTimelineEntry>();
    public DbSet<InternalNotification> Notifications => Set<InternalNotification>();
    public const string FoundationSchema = "foundation";
    public const string HistoryTable = "migration_history";

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres => postgres
            .MigrationsHistoryTable(HistoryTable, FoundationSchema)
            .CommandTimeout(10));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Password + authenticator MFA is the approved first login path; passkeys are not exposed.
        builder.Ignore<IdentityUserPasskey<Guid>>();
        builder.Ignore<IdentityPasskeyData>();
        builder.Entity<LandErpUser>().ToTable("users", "identity");
        builder.Entity<IdentityRole<Guid>>().ToTable("roles", "identity");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles", "identity");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims", "identity");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins", "identity");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens", "identity");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims", "identity");
        builder.Entity<PermissionDefinition>().ToTable("permissions", "identity").HasKey(item => item.Id);
        builder.Entity<RolePermissionGrant>().ToTable("role_permissions", "identity")
            .HasKey(item => new { item.RoleId, item.PermissionId });
        builder.Entity<RolePermissionGrant>().HasOne<IdentityRole<Guid>>().WithMany().HasForeignKey(item => item.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<RolePermissionGrant>().HasOne<PermissionDefinition>().WithMany().HasForeignKey(item => item.PermissionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EmployeeInvitation>().ToTable("employee_invitations", "identity");
        builder.Entity<EmployeeInvitation>().HasOne<Employee>().WithMany().HasForeignKey(item => item.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AuditEvent>().ToTable("audit_events", "foundation");
        builder.Entity<AuditEvent>().Property(item => item.Changes).HasColumnType("jsonb");
        builder.Entity<Organization>().ToTable("organizations", "organization");
        builder.Entity<OrgUnit>().ToTable("org_units", "organization");
        builder.Entity<Position>().ToTable("positions", "organization");
        builder.Entity<Team>().ToTable("teams", "organization");
        builder.Entity<Employee>().ToTable("employees", "organization");
        builder.Entity<EmployeeAssignment>().ToTable("employee_assignments", "organization");
        builder.Entity<EmployeeAssignment>().Property(item => item.Scope).HasConversion<string>();
        builder.Entity<EmployeeAssignment>().HasIndex(item => item.EmployeeId).IsUnique();
        builder.Entity<EmployeeAssignment>().HasOne<Employee>().WithMany().HasForeignKey(item => item.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EmployeeAssignment>().HasOne<Employee>().WithMany().HasForeignKey(item => item.ManagerEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EmployeeAssignment>().HasOne<OrgUnit>().WithMany().HasForeignKey(item => item.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EmployeeAssignment>().HasOne<Position>().WithMany().HasForeignKey(item => item.PositionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EmployeeAssignment>().HasOne<Team>().WithMany().HasForeignKey(item => item.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EmployeeAssignment>().HasOne<IdentityRole<Guid>>().WithMany().HasForeignKey(item => item.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Employee>().HasOne<LandErpUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Employee>().HasIndex(item => item.UserId).IsUnique();
        builder.Entity<OrgUnit>().HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Position>().HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Team>().HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Team>().HasOne<OrgUnit>().WithMany().HasForeignKey(item => item.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Employee>().HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrgUnit>().HasIndex(item => new { item.OrganizationId, item.Name }).IsUnique();
        builder.Entity<Position>().HasIndex(item => new { item.OrganizationId, item.Name }).IsUnique();
        builder.Entity<Team>().HasIndex(item => new { item.OrgUnitId, item.Name }).IsUnique();
        CollectionMappings.Apply(builder);
        ProcurementMappings.Apply(builder);
        ModelConventions.Apply(builder);
        builder.Entity<PropertyCase>().Property(item => item.ManagerEmployeeId)
            .HasComment("Менеджер, ответственный за первичный анализ объекта; получатель возврата руководителя по умолчанию.");
    }

    private void EnforceInvariants()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditEvent or CollectionDelivery or CatalogObservation or CatalogEvent or WorkflowTransition or Approval or BusinessTimelineEntry && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Audit facts are append-only.");
            }

            if (entry.State == EntityState.Modified && entry.Metadata.FindProperty("Version") != null)
            {
                entry.Property("Version").CurrentValue = (long)entry.Property("Version").OriginalValue! + 1;
            }
        }

    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceInvariants();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceInvariants();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
