using System.Text;
using System.Text.Json;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Infrastructure.Modules.Organization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ProcurementReplayAuditViewTests
{
    [TestMethod]
    public async Task ReplayMetadataIsTechnicalOnlyAndBusinessAuditRemainsReadable()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid commandId = Guid.CreateVersion7();
        CreateManualPropertyCase command = new("Объект B1-01", "Химки", null, 4_000_000m, 900m,
            "Проверка представления аудита", commandId);
        await fixture.Workspace.CreateManualCaseAsync(fixture.Manager, command, "audit-view-create", CancellationToken.None);
        AuditReadService service = new(fixture.Factory, fixture.Access);
        AuditQuery query = new(ActorId: fixture.Manager.UserId, PageSize: 100);
        AuditPage page = await service.ReadAsync(fixture.Owner, query, CancellationToken.None);
        AuditEventView row = page.Items.Single(item => item.Id == commandId);
        Assert.IsTrue(row.Changes.Any(item => item.Field == "Название объекта" && item.After == command.Title));
        Assert.IsFalse(row.Changes.Any(item => item.Field.Contains("Replay", StringComparison.OrdinalIgnoreCase)));

        AuditTechnicalDetails technical = await service.ReadTechnicalAsync(fixture.Owner, commandId, CancellationToken.None);
        using JsonDocument payload = JsonDocument.Parse(technical.PayloadJson);
        string hash = payload.RootElement.GetProperty("CommandReplay").GetProperty("PayloadHash").GetString()!;
        Assert.AreEqual(64, hash.Length);
        AuditExport export = await service.ExportCsvAsync(fixture.Owner, query, CancellationToken.None);
        string csv = Encoding.UTF8.GetString(export.Content);
        Assert.IsFalse(csv.Contains("Command Replay", StringComparison.Ordinal));
        Assert.IsFalse(csv.Contains(hash, StringComparison.Ordinal));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadTechnicalAsync(
            fixture.ForeignOwner, commandId, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => service.ReadTechnicalAsync(
            fixture.Manager, commandId, CancellationToken.None));
    }
}
