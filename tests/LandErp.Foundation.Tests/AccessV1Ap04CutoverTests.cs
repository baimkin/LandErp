using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class AccessV1Ap04CutoverTests
{
    [TestMethod]
    public async Task SystemRoleChangeDoesNotChangeExplicitWorkflowAccess()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        IEmployeeAccessService access = fixture.Scope.ServiceProvider.GetRequiredService<IEmployeeAccessService>();

        EffectiveEmployeeAccess before = await access.ResolveAsync(fixture.Manager, CancellationToken.None);
        Assert.AreEqual(EmployeeAccessSource.Configured, before.Source);
        Assert.AreEqual(ProcurementAccessLevel.Manager, before.Settings.ProcurementAccess);

        OrganizationView organization = await fixture.Organization.ReadAsync(fixture.Owner, CancellationToken.None);
        EmployeeView manager = organization.Employees.Single(item => item.Id == fixture.ManagerEmployeeId);
        Guid inspectorRoleId = organization.Roles.Single(item => item.Name == "Inspector").Id;

        await fixture.Organization.ChangeAssignmentAsync(fixture.Owner,
            new(manager.Id, manager.DepartmentId, manager.PositionId, manager.TeamId, manager.ManagerId,
                inspectorRoleId, manager.Scope, manager.Version),
            "ap04-role-independence", CancellationToken.None);

        EffectiveEmployeeAccess after = await access.ResolveAsync(fixture.Manager, CancellationToken.None);
        Assert.AreEqual(EmployeeAccessSource.Configured, after.Source);
        Assert.AreEqual(before.Settings, after.Settings,
            "Changing the system/identity role must not recalculate Access V1 after cutover.");
    }

    [TestMethod]
    public void RuntimeAndUiContainNoLegacyAccessFallback()
    {
        string root = FoundationTests.RepositoryRoot();
        string resolver = File.ReadAllText(Path.Combine(root, "src", "LandErp.Infrastructure", "Modules",
            "IdentityAccess", "EmployeeAccessService.cs"));
        string organization = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components",
            "Pages", "OrganizationPage.razor"));
        string shell = File.ReadAllText(Path.Combine(root, "src", "LandErp.Server", "Components",
            "Layout", "AppShell.razor"));

        Assert.IsFalse(resolver.Contains("RolePermissions", StringComparison.Ordinal));
        Assert.IsFalse(resolver.Contains("LegacyConfiguration", StringComparison.Ordinal));
        Assert.IsFalse(resolver.Contains("LegacyPermissions", StringComparison.Ordinal));
        Assert.IsFalse(organization.Contains("legacy fallback", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(organization, "accessInput.ToConfiguration()");
        StringAssert.Contains(organization, "Системная роль и административная область");
        StringAssert.Contains(shell, "effectiveAccess.IsSystemOwner");
    }
}
