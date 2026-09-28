using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Collector.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass, TestCategory("PostgreSQL")]
public sealed class SourceChangesAcknowledgementTests
{
    [TestMethod]
    public async Task ReceiptsAreBoundedMonotonicScopedAndDoNotApproveBusinessChanges()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(true,true);
        var pair=await f.IngestMarketplacePairAsync();
        Guid caseId=(await f.Workspace.TakeToWorkAsync(f.Manager,new(pair.AvitoId),"u02",CancellationToken.None)).CaseId;
        await f.Workspace.TakeToWorkAsync(f.Manager,new(pair.CianId,caseId),"u02",CancellationToken.None);
        var original=await f.Workspace.ReadCardAsync(f.Manager,caseId,CancellationToken.None);
        Assert.HasCount(0,await f.Workspace.ReadSourceChangeNoticesAsync(f.Manager,caseId,CancellationToken.None));
        await using(var db=f.Sandbox.Context())
        {
            Assert.IsTrue(await db.PropertyCaseSourceLinks.Where(x=>x.PropertyCaseId==caseId).AllAsync(x=>x.ViewedDataRevision==0));
        }
        await f.IngestChangedAvitoAsync(pair.Agent,pair.Administration,1_900_000);
        await f.IngestChangedAsync(ListingSource.Cian,pair.Agent,pair.Administration,1_800_000,"u02-cian");
        var old=await f.Workspace.ReadSourceChangesAsync(f.Manager,caseId,pair.AvitoId,CancellationToken.None);
        var cian=await f.Workspace.ReadSourceChangesAsync(f.Manager,caseId,pair.CianId,CancellationToken.None);
        Assert.IsTrue(old.CanAcknowledge);
        Assert.HasCount(2,await f.Workspace.ReadSourceChangeNoticesAsync(f.Manager,caseId,CancellationToken.None));
        // A fresh revision arrives while the old comparison is still open.
        await f.IngestChangedAsync(ListingSource.Avito,pair.Agent,pair.Administration,1_700_000,"u02-avito-new");
        await f.Workspace.AcknowledgeSourceChangesAsync(f.Manager,Command(old),CancellationToken.None);
        Assert.HasCount(2,await f.Workspace.ReadSourceChangeNoticesAsync(f.Manager,caseId,CancellationToken.None));
        var latest=await f.Workspace.ReadSourceChangesAsync(f.Manager,caseId,pair.AvitoId,CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>f.Workspace.AcknowledgeSourceChangesAsync(f.ForeignOwner,Command(latest),CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>f.Workspace.AcknowledgeSourceChangesAsync(f.SecondManager,Command(latest),CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>f.Workspace.AcknowledgeSourceChangesAsync(f.Manager,Command(latest) with{LinkId=cian.LinkId},CancellationToken.None));
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(()=>f.Workspace.AcknowledgeSourceChangesAsync(f.Manager,Command(latest) with{Revision=latest.Revision+1},CancellationToken.None));
        // Viewing requires only permitted read access, not permission to make business decisions.
        await using(var db=f.Sandbox.Context())
        {
            var settings=await db.EmployeeAccessSettings.SingleAsync(x=>x.EmployeeId==f.ManagerEmployeeId);
            settings.ProcurementAccess=ProcurementAccessLevel.Read;
            await db.SaveChangesAsync();
        }
        await Task.WhenAll(f.Workspace.AcknowledgeSourceChangesAsync(f.Manager,Command(latest),CancellationToken.None),
            f.Workspace.AcknowledgeSourceChangesAsync(f.Manager,Command(old),CancellationToken.None));
        await f.Workspace.AcknowledgeSourceChangesAsync(f.Manager,Command(old),CancellationToken.None);
        var notices=await f.Workspace.ReadSourceChangeNoticesAsync(f.Manager,caseId,CancellationToken.None);
        Assert.AreEqual(pair.CianId,notices.Single().CatalogItemId,"Opening Avito must not mark Cian.");
        await f.Workspace.AcknowledgeSourceChangesAsync(f.Manager,Command(cian),CancellationToken.None);
        Assert.HasCount(0,await f.Workspace.ReadSourceChangeNoticesAsync(f.Manager,caseId,CancellationToken.None));
        var unchanged=await f.Workspace.ReadCardAsync(f.Manager,caseId,CancellationToken.None);
        Assert.IsTrue(unchanged.Item.Changed,"Source still requires business review after viewing.");
        Assert.AreEqual(original.Item.Price,unchanged.Item.Price);
        Assert.AreEqual(original.Item.Area,unchanged.Item.Area);
        Assert.AreEqual(original.Description,unchanged.Description);
        Assert.AreEqual(original.Item.CaseVersion,unchanged.Item.CaseVersion);
        await using(var db=f.Sandbox.Context())
        {
            var links=await db.PropertyCaseSourceLinks.Where(x=>x.PropertyCaseId==caseId).ToArrayAsync();
            Assert.IsTrue(links.All(x=>x.ReviewedDataRevision==1));
            Assert.AreEqual(latest.Revision,links.Single(x=>x.CatalogItemId==pair.AvitoId).ViewedDataRevision);
        }
    }
    private static AcknowledgeSourceChanges Command(SourceChangeComparison view)=>new(view.CaseId,view.LinkId,view.CatalogItemId,view.Revision);
}
