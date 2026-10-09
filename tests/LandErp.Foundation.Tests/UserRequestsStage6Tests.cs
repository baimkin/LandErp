using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class UserRequestsStage6Tests
{
    [TestMethod]
    public void AddressedUiUsesExistingCorrectionAndSafeEmployeeDefaults()
    {
        string root = FoundationTests.RepositoryRoot();
        string propertyCase = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components",
            "Procurement", "CaseWorkspace.razor"));
        string organization = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components",
            "Pages", "OrganizationPage.razor"));
        string login = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components",
            "Account", "Login.razor"));

        Assert.IsTrue(propertyCase.Contains("OpenFactCorrection(CaseFactField.CadastralNumber)", StringComparison.Ordinal));
        Assert.AreEqual(1, Regex.Count(propertyCase, "Workspace\\.CorrectCaseFactAsync"),
            "The dedicated cadastral action must reuse the existing correction command and history.");
        Assert.IsTrue(propertyCase.Contains("Значения исходных источников не переписываются", StringComparison.Ordinal));

        Assert.IsTrue(organization.Contains("x.Name==\"Viewer\"", StringComparison.Ordinal));
        Assert.IsTrue(organization.Contains("ФИО *", StringComparison.Ordinal));
        Assert.IsTrue(organization.Contains("Логин *", StringComparison.Ordinal));
        Assert.IsTrue(organization.Contains("Отдел (необязательно)", StringComparison.Ordinal));
        Assert.IsTrue(organization.Contains("[Required(ErrorMessage=\"Выберите системную роль\")]", StringComparison.Ordinal));

        Assert.IsTrue(login.Contains("if (privileged && !user.TwoFactorEnabled)", StringComparison.Ordinal));
        Assert.IsTrue(login.Contains("if (user.TwoFactorEnabled)", StringComparison.Ordinal));
        Assert.IsTrue(login.Contains("new Claim(\"amr\", mfa ? \"mfa\" : \"pwd\")", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestCategory("PostgreSQL")]
    public async Task EmployeeWithoutOptionalAssignmentsCanSignInAndKeepsExplicitAccess()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context())
            await migrator.Database.MigrateAsync();

        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid ownerUserId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "stage6-owner", "Stage 6 organization");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, ownerUserId);
        await sandbox.GrantRuntimeAsync();

        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IOrganizationWorkspace workspace = scope.ServiceProvider.GetRequiredService<IOrganizationWorkspace>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        IAccessControl access = scope.ServiceProvider.GetRequiredService<IAccessControl>();
        Subject owner = new(ownerUserId, true);

        OrganizationView organization = await workspace.ReadAsync(owner, CancellationToken.None);
        Guid viewerRoleId = organization.Roles.Single(item => item.Name == "Viewer").Id;
        TemporaryCredential credential = await workspace.CreateEmployeeAsync(owner,
            new("Сотрудник без назначений", "stage6.employee", null, null, null, null,
                viewerRoleId, AccessScope.Own, false, EmployeeAccessRules.NoAccess),
            "stage6-optional", CancellationToken.None);

        LandErpUser user = (await users.FindByNameAsync(credential.Login))!;
        Assert.IsTrue(await users.CheckPasswordAsync(user, credential.Password));
        Assert.IsFalse(user.TwoFactorEnabled, "An ordinary employee must not require MFA for password-only login.");
        Assert.IsFalse(user.MustChangePassword);
        _ = await access.ResolveAsync(new Subject(user.Id, false), CancellationToken.None);

        EmployeeView employee = (await workspace.ReadAsync(owner, CancellationToken.None)).Employees
            .Single(item => item.Id == credential.EmployeeId);
        Assert.IsNull(employee.DepartmentId);
        Assert.IsNull(employee.TeamId);
        Assert.IsNull(employee.PositionId);
        Assert.IsNull(employee.ManagerId);
        Assert.AreEqual("Viewer", employee.Role);
        Assert.AreEqual(EmployeeAccessSource.Configured, employee.Access.Source);
        Assert.AreEqual(EmployeeAccessRules.NoAccess, employee.Access.Settings);

        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() =>
            access.RequireAsync(new Subject(ownerUserId, false), Permissions.UsersRead, CancellationToken.None));
        _ = await access.RequireAsync(owner, Permissions.UsersRead, CancellationToken.None);
    }
}
