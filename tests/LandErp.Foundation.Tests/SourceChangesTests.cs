using System.Text.Json;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Collector.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class SourceChangesTests
{
    [TestMethod]
    public async Task ReadUsesSourceHistoryAndPreservesCaseFacts()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        var pair = await f.IngestMarketplacePairAsync();
        var taken = await f.Workspace.TakeToWorkAsync(f.Manager, new(pair.AvitoId), "u02", CancellationToken.None);
        var before = await f.Workspace.ReadCardAsync(f.Manager, taken.CaseId, CancellationToken.None);
        await f.IngestChangedAvitoAsync(pair.Agent, pair.Administration, 1_700_000.5m);
        var comparison = await f.Workspace.ReadSourceChangesAsync(f.Manager, taken.CaseId, pair.AvitoId, CancellationToken.None);
        Assert.IsTrue(comparison.CompleteHistory);
        var price = comparison.Observations.SelectMany(x => x.Fields).Single(x => x.Name == "цена");
        Assert.AreEqual(2_000_000m, price.Before!.Number);
        Assert.AreEqual(1_700_000.5m, price.After!.Number);
        var after = await f.Workspace.ReadCardAsync(f.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(before.Item.Price, after.Item.Price);
        Assert.AreEqual(before.Item.CaseVersion, after.Item.CaseVersion);
        Assert.AreEqual(before.Description, after.Description);
        Assert.IsTrue(after.Item.Changed);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.ReadSourceChangesAsync(f.ForeignOwner, taken.CaseId, pair.AvitoId, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => f.Workspace.ReadSourceChangesAsync(f.Manager, taken.CaseId, pair.CianId, CancellationToken.None));
        await using var db = f.Sandbox.Context();
        var observation = await db.ListingObservations.Where(x => x.ListingId == pair.AvitoId).OrderByDescending(x => x.ObservedAt).FirstAsync();
        // Deliberate corrupt legacy fixture in the disposable database, bypassing append-only application rules.
        await db.ListingObservations.Where(x => x.Id == observation.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.PayloadJson, "{}"));
        await Assert.ThrowsExactlyAsync<JsonException>(() => f.Workspace.ReadSourceChangesAsync(f.Manager, taken.CaseId, pair.AvitoId, CancellationToken.None));
    }
}
