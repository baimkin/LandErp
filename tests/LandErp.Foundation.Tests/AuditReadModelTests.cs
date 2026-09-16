using System.Text;
using LandErp.Application.Foundation;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class AuditReadModelTests
{
    [TestMethod]
    public async Task SemanticPagingFiltersFallbackTechnicalDetailsAndTenantIsolation()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context()) await migrator.Database.MigrateAsync();
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid owner = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "audit-owner", "Audit organization");
        Guid foreignOwner = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "audit-foreign", "Foreign organization");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, owner);
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, foreignOwner);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IOrganizationWorkspace organization = scope.ServiceProvider.GetRequiredService<IOrganizationWorkspace>();
        IAuditReadService audit = scope.ServiceProvider.GetRequiredService<IAuditReadService>();
        Subject subject = new(owner, true);

        for (int index = 0; index < 35; index++)
            await organization.SaveDepartmentAsync(subject, new(null, null, $"Аудит отдел {index:D2}", "Проверка semantic audit", null), $"audit-{index}", CancellationToken.None);

        Guid organizationId;
        Guid legacyEventId = DataConventions.NewId();
        await using (LandErpDbContext db = sandbox.Context(runtime: true))
        {
            organizationId = await db.Employees.Where(item => item.UserId == owner).Select(item => item.OrganizationId).SingleAsync();
            db.AuditEvents.Add(new AuditEvent
            {
                Id = legacyEventId, OrganizationId = organizationId, ActorId = owner, Action = "LegacyUnmappedAction",
                ObjectType = "LegacyEntity", ObjectId = DataConventions.NewId(),
                Changes = "{\"DisplayName\":\"Архивный объект из снимка\"}", CorrelationId = "legacy-test",
                RecordedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        AuditPage first = await audit.ReadAsync(subject, new(PageSize: 10), CancellationToken.None);
        Assert.AreEqual(10, first.Items.Count);
        Assert.IsTrue(first.TotalCount >= 37);
        Assert.IsTrue(first.PageCount >= 4);
        Assert.AreEqual("Europe/Moscow", first.BusinessTimeZone);
        Assert.IsTrue(first.Items.All(item => item.ActorDisplayName == "Владелец"));

        AuditPage second = await audit.ReadAsync(subject, new(Page: 2, PageSize: 10), CancellationToken.None);
        Assert.AreEqual(10, second.Items.Count);
        Assert.IsFalse(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)).Any());

        AuditPage searched = await audit.ReadAsync(subject, new(Search: "Аудит отдел 34", PageSize: 10), CancellationToken.None);
        AuditEventView created = searched.Items.Single(item => item.Title == "Создан отдел");
        Assert.AreEqual("Аудит отдел 34", created.ObjectDisplayName);
        Assert.IsTrue(created.Changes.Any(item => item.Field == "Название" && item.After == "Аудит отдел 34"));

        AuditPage dataChanges = await audit.ReadAsync(subject, new(Category: AuditCategory.DataChanges, PageSize: 100), CancellationToken.None);
        Assert.IsTrue(dataChanges.Items.Count >= 35);
        Assert.IsTrue(dataChanges.Items.Any(item => item.Title == "Создан отдел"));
        Assert.IsFalse(dataChanges.Items.Any(item => item.Id == legacyEventId));

        AuditPage legacySearch = await audit.ReadAsync(subject, new(Search: "Архивный объект из снимка"), CancellationToken.None);
        AuditEventView legacy = legacySearch.Items.Single();
        Assert.AreEqual("Зафиксировано системное действие", legacy.Title);
        Assert.AreEqual("Архивный объект из снимка", legacy.ObjectDisplayName);
        Assert.IsTrue(legacy.ObjectArchived);
        AuditTechnicalDetails technical = await audit.ReadTechnicalAsync(subject, legacyEventId, CancellationToken.None);
        Assert.AreEqual("LegacyUnmappedAction", technical.Action);
        Assert.AreEqual("legacy-test", technical.CorrelationId);

        AuditExport export = await audit.ExportCsvAsync(subject, new(Search: "Аудит отдел 34"), CancellationToken.None);
        string csv = Encoding.UTF8.GetString(export.Content);
        StringAssert.Contains(csv, "Аудит отдел 34");
        Assert.IsFalse(csv.Contains("DepartmentCreated", StringComparison.Ordinal));

        AuditPage foreign = await audit.ReadAsync(new(foreignOwner, true), new(PageSize: 100), CancellationToken.None);
        Assert.IsFalse(foreign.Items.Any(item => item.ObjectDisplayName.Contains("Аудит отдел", StringComparison.Ordinal)));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => audit.ReadTechnicalAsync(new(foreignOwner, true), legacyEventId, CancellationToken.None));
    }
}
