using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
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

    [TestMethod]
    public async Task DirectAccountsOrganizationEditingArchivingAndCredentialLifecycle()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context()) await migrator.Database.MigrateAsync();
        await using ServiceProvider bootstrap = Services(sandbox.MigratorConnection);
        Guid ownerUserId = await BootstrapAsync(bootstrap, "phase6-owner", "Phase 6 organization");
        await EnableMfaAsync(bootstrap, ownerUserId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = Services(sandbox.RuntimeConnection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IOrganizationWorkspace workspace = scope.ServiceProvider.GetRequiredService<IOrganizationWorkspace>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        IAccessControl access = scope.ServiceProvider.GetRequiredService<IAccessControl>();
        Subject owner = new(ownerUserId, true);

        await workspace.SaveDepartmentAsync(owner, new(null, null, "Закупка", "Проверка и покупка участков", null), "phase6", CancellationToken.None);
        await workspace.SavePositionAsync(owner, new(null, null, "Менеджер по закупке", "Работает с объектами"), "phase6", CancellationToken.None);
        OrganizationView view = await workspace.ReadAsync(owner, CancellationToken.None);
        DepartmentView department = view.Departments.Single();
        PositionView position = view.Positions.Single();
        EmployeeView ownerEmployee = view.Employees.Single(item => item.Role == "Owner");
        await workspace.SaveTeamAsync(owner, new(null, null, department.Id, "Команда Север", "Северное направление", ownerEmployee.Id), "phase6", CancellationToken.None);
        await workspace.SaveDepartmentAsync(owner, new(department.Id, department.Version, "Отдел закупки", "Актуальное описание", ownerEmployee.Id), "phase6", CancellationToken.None);

        view = await workspace.ReadAsync(owner, CancellationToken.None);
        department = view.Departments.Single();
        TeamView team = view.Teams.Single();
        Guid managerRole = view.Roles.Single(item => item.Name == "ProcurementManager").Id;
        TemporaryCredential credential = await workspace.CreateEmployeeAsync(owner,
            new("Новый менеджер", "phase6.manager", department.Id, position.Id, team.Id, ownerEmployee.Id,
                managerRole, AccessScope.AssignedObjects), "phase6", CancellationToken.None);
        LandErpUser createdUser = (await users.FindByNameAsync(credential.Login))!;
        Assert.IsTrue(await users.CheckPasswordAsync(createdUser, credential.Password));
        Assert.IsTrue(createdUser.MustChangePassword);

        EmployeeView employee = (await workspace.ReadAsync(owner, CancellationToken.None)).Employees.Single(item => item.Id == credential.EmployeeId);
        Assert.AreEqual(EmployeeState.Active, employee.State);
        Assert.AreEqual("ProcurementManager", employee.Role);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => workspace.SetTeamActiveAsync(owner,
            new(team.Id, team.Version, false), "phase6", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => workspace.SetPositionActiveAsync(owner,
            new(position.Id, position.Version, false), "phase6", CancellationToken.None));

        await workspace.ChangeAssignmentAsync(owner,
            new(employee.Id, null, null, null, ownerEmployee.Id, employee.RoleId, AccessScope.AssignedObjects, employee.Version),
            "phase6", CancellationToken.None);
        view = await workspace.ReadAsync(owner, CancellationToken.None);
        team = view.Teams.Single();
        position = view.Positions.Single();
        await workspace.SetTeamActiveAsync(owner, new(team.Id, team.Version, false), "phase6", CancellationToken.None);
        await workspace.SetPositionActiveAsync(owner, new(position.Id, position.Version, false), "phase6", CancellationToken.None);
        EmployeeView detached = view.Employees.Single(item => item.Id == employee.Id);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.ChangeAssignmentAsync(owner,
            new(detached.Id, null, position.Id, null, ownerEmployee.Id, detached.RoleId, detached.Scope, detached.Version),
            "phase6", CancellationToken.None));

        TemporaryCredential reset = await workspace.ResetTemporaryPasswordAsync(owner, employee.Id, "phase6", CancellationToken.None);
        await using AsyncServiceScope verificationScope = services.CreateAsyncScope();
        UserManager<LandErpUser> freshUsers = verificationScope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        LandErpUser resetUser = (await freshUsers.FindByNameAsync(credential.Login))!;
        Assert.IsFalse(await freshUsers.CheckPasswordAsync(resetUser, credential.Password));
        Assert.IsTrue(await freshUsers.CheckPasswordAsync(resetUser, reset.Password));
        employee = (await workspace.ReadAsync(owner, CancellationToken.None)).Employees.Single(item => item.Id == employee.Id);
        await workspace.SetEmployeeActiveAsync(owner, new(employee.Id, employee.EmployeeVersion, false), "phase6", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => access.RequireAsync(new Subject(resetUser.Id, false), Permissions.UsersRead, CancellationToken.None));
        employee = (await workspace.ReadAsync(owner, CancellationToken.None)).Employees.Single(item => item.Id == employee.Id);
        Assert.AreEqual(EmployeeState.Disabled, employee.State);
        await workspace.SetEmployeeActiveAsync(owner, new(employee.Id, employee.EmployeeVersion, true), "phase6", CancellationToken.None);
        Assert.AreEqual(EmployeeState.Active, (await workspace.ReadAsync(owner, CancellationToken.None)).Employees.Single(item => item.Id == employee.Id).State);

        IReadOnlyList<AuditView> audit = await workspace.ReadAuditAsync(owner, CancellationToken.None);
        Assert.IsTrue(audit.Any(item => item.Action == "DepartmentUpdated"));
        Assert.IsTrue(audit.Any(item => item.Action == "TeamArchived"));
        Assert.IsTrue(audit.Any(item => item.Action == "EmployeeDeactivated"));
        Assert.IsFalse(audit.Any(item => item.Changes.Contains(credential.Password, StringComparison.Ordinal)
            || item.Changes.Contains(reset.Password, StringComparison.Ordinal)));
    }

    internal static ServiceProvider Services(string connection)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
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

    internal static async Task EnableMfaAsync(ServiceProvider services, Guid userId)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        LandErpUser user = (await users.FindByIdAsync(userId.ToString()))!;
        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);
    }
}
