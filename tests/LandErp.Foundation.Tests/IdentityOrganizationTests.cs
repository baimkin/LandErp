using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class IdentityOrganizationTests
{
    private const string TestPassword = "Synthetic1!PasswordForTests";

    [TestMethod]
    public async Task InvitationsPermissionsScopesConcurrencyAndLastOwner()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using LandErpDbContext db = sandbox.Context();
        await db.Database.MigrateAsync();
        await using ServiceProvider services = Services(sandbox.MigratorConnection);
        Guid owner = await BootstrapAsync(services, "owner@test.invalid", "Test organization");
        Guid secondOwner = await BootstrapAsync(services, "other@test.invalid", "Other organization");
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider runtimeServices = Services(sandbox.RuntimeConnection);
        await using AsyncServiceScope scope = runtimeServices.CreateAsyncScope();
        IOrganizationWorkspace workspace = scope.ServiceProvider.GetRequiredService<IOrganizationWorkspace>();
        IAccessControl access = scope.ServiceProvider.GetRequiredService<IAccessControl>();
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.ReadAsync(new(owner, false), CancellationToken.None));
        await EnableMfaAsync(services, owner);
        await EnableMfaAsync(services, secondOwner);
        Subject actor = new(owner, true);
        await workspace.CreateDepartmentAsync(actor, "Закупка", "test", CancellationToken.None);
        await workspace.CreateDepartmentAsync(actor, "Другое подразделение", "test", CancellationToken.None);
        await workspace.CreatePositionAsync(actor, "Менеджер", "test", CancellationToken.None);
        OrganizationView organization = await workspace.ReadAsync(actor, CancellationToken.None);
        Guid department = organization.Departments.Single(item => item.Name == "Закупка").Id;
        await workspace.CreateTeamAsync(actor, department, "Команда 1", "test", CancellationToken.None);
        organization = await workspace.ReadAsync(actor, CancellationToken.None);
        Guid role = organization.Roles.Single(item => item.Name == "ProcurementManager").Id;
        InvitationResult invitation = await workspace.InviteAsync(actor, new("Менеджер 1", "manager@test.invalid", department,
            organization.Positions[0].Id, organization.Teams[0].Id, organization.Employees[0].Id, role, AccessScope.Department),
            "test", CancellationToken.None);
        await using (AsyncServiceScope activationScope = services.CreateAsyncScope())
        {
            AccountActivation activation = activationScope.ServiceProvider.GetRequiredService<AccountActivation>();
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => activation.ActivateAsync(invitation.InvitationId,
                new string('0', 64), TestPassword, CancellationToken.None));
            await activation.ActivateAsync(invitation.InvitationId, invitation.OneTimeToken, TestPassword, CancellationToken.None);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => activation.ActivateAsync(invitation.InvitationId,
                invitation.OneTimeToken, TestPassword, CancellationToken.None));
        }

        Guid manager = (await db.Users.AsNoTracking().SingleAsync(item => item.Email == "manager@test.invalid")).Id;
        Subject managerActor = new(manager, false);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.CreateDepartmentAsync(managerActor, "Forbidden", "test", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.ReadAuditAsync(managerActor, CancellationToken.None));
        OrganizationView managerView = await workspace.ReadAsync(managerActor, CancellationToken.None);
        Assert.AreEqual(1, managerView.Employees.Count);
        Assert.AreEqual("Менеджер 1", managerView.Employees[0].Name);
        await access.RequireAsync(managerActor, Permissions.ManagerDecide, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => access.RequireAsync(managerActor, Permissions.HeadDecide, CancellationToken.None));
        OrganizationView foreign = await workspace.ReadAsync(new(secondOwner, true), CancellationToken.None);
        Assert.AreEqual(0, foreign.Departments.Count);
        Assert.AreEqual(1, foreign.Employees.Count);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.InviteAsync(new(secondOwner, true),
            new("Injected", "injected@test.invalid", department, null, null, null, role, AccessScope.Department), "test", CancellationToken.None));

        EmployeeView onlyOwner = (await workspace.ReadAsync(actor, CancellationToken.None)).Employees.Single(item => item.Role == "Owner");
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => workspace.ChangeAssignmentAsync(actor,
            new(onlyOwner.Id, department, null, null, null, role, AccessScope.Department, onlyOwner.Version), "test", CancellationToken.None));
        EmployeeView managerRow = (await workspace.ReadAsync(actor, CancellationToken.None)).Employees.Single(item => item.Id == managerView.Employees[0].Id);
        ChangeAssignment change = new(managerRow.Id, department, managerRow.PositionId, managerRow.TeamId,
            managerRow.ManagerId, role, AccessScope.Team, managerRow.Version);
        await workspace.ChangeAssignmentAsync(actor, change, "test", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => workspace.ChangeAssignmentAsync(actor, change, "test", CancellationToken.None));
        Assert.AreEqual(1, (await workspace.ReadAsync(managerActor, CancellationToken.None)).Employees.Count);
        ChangeAssignment own = change with { Scope = AccessScope.Own, ExpectedVersion = change.ExpectedVersion + 1 };
        await workspace.ChangeAssignmentAsync(actor, own, "test", CancellationToken.None);
        Assert.AreEqual(1, (await workspace.ReadAsync(managerActor, CancellationToken.None)).Employees.Count);
        IReadOnlyList<AuditView> audit = await workspace.ReadAuditAsync(actor, CancellationToken.None);
        Assert.IsTrue(audit.Any(item => item.Action == "AssignmentChanged"));
        Assert.IsFalse(audit.Any(item => item.Changes.Contains(invitation.OneTimeToken, StringComparison.Ordinal)
            || item.Changes.Contains(TestPassword, StringComparison.Ordinal)));
        await sandbox.GrantRuntimeAsync();
        await using Npgsql.NpgsqlConnection runtime = new(sandbox.RuntimeConnection);
        await runtime.OpenAsync();
        await using Npgsql.NpgsqlCommand forbiddenDelete = new("DELETE FROM foundation.audit_events", runtime);
        Npgsql.PostgresException denied = await Assert.ThrowsExactlyAsync<Npgsql.PostgresException>(() => forbiddenDelete.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", denied.SqlState);
    }

    internal static ServiceProvider Services(string connection)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddLandErpPersistence(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Database:ConnectionString"] = connection }).Build());
        services.AddLandErpIdentity();
        services.AddScoped<AccountActivation>();
        return services.BuildServiceProvider();
    }

    internal static async Task<Guid> BootstrapAsync(ServiceProvider services, string login, string name)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        return await LocalBootstrap.CreateOwnerAsync(scope.ServiceProvider.GetRequiredService<LandErpDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(), login, TestPassword, name);
    }

    private static async Task EnableMfaAsync(ServiceProvider services, Guid userId)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        LandErpUser user = (await users.FindByIdAsync(userId.ToString()))!;
        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);
    }
}
