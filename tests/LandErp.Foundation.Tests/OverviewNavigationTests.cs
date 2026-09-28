using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.Catalog;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Overview;
using LandErp.Infrastructure.Modules.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass, TestCategory("PostgreSQL")]
public sealed class OverviewNavigationTests
{
    internal static IncomingFilterPresetCriteriaV1 Criteria(decimal? max=null) => new(2,null,null,CatalogDisposition.Incoming,CatalogAgeRange.Any,null,max,null,null,null,null,[],false,IncomingCatalogSortField.ChangedAt,IncomingCatalogSortDirection.Descending);

    [TestMethod]
    public async Task MetricsMatchDestinationListsWithOverlapsMultipleTasksSourcesAndPermissions()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(true,true);
        await using var source=NpgsqlDataSource.Create(f.Sandbox.RuntimeConnection);
        var presets=new IncomingFilterPresetService(source,new EmployeeAccessService(f.Factory));
        var incoming=new IncomingCatalogReadService(f.Factory,new EmployeeAccessService(f.Factory),f.Workspace,TimeProvider.System,presets);
        var overview=new OverviewService(f.Factory,TimeProvider.System);
        var queue=new ProcurementQueueV2ReadService(f.Factory,TimeProvider.System);
        var pair=await f.IngestMarketplacePairAsync();
        Assert.AreEqual(0,(await overview.ReadAsync(f.Manager,CancellationToken.None)).NewIncoming.Value,"No saved filters means an empty F-02 working selection.");
        var first=await presets.CreateAsync(f.Manager,new(null,"Все входящие",Criteria()),CancellationToken.None);
        var second=await presets.CreateAsync(f.Manager,new(null,"До трёх миллионов",Criteria(3_000_000m)),CancellationToken.None);
        var initial=await overview.ReadAsync(f.Manager,CancellationToken.None);
        var listings=await incoming.ReadAsync(f.Manager,IncomingOverviewSelection.Filter("new"),CancellationToken.None);
        Assert.AreEqual(2,listings.Total);Assert.AreEqual(listings.Total,initial.NewIncoming.Value);
        Assert.AreEqual(2,listings.Items.Select(x=>x.Id).Distinct().Count());
        Guid id=(await f.Workspace.TakeToWorkAsync(f.Manager,new(pair.AvitoId),"o01",CancellationToken.None)).CaseId;
        await f.Workspace.TakeToWorkAsync(f.Manager,new(pair.CianId,id),"o01",CancellationToken.None);
        for(int i=0;i<2;i++)
        {
            var tasks=await f.Workspace.ReadTasksAsync(f.Manager,id,CancellationToken.None);
            await f.Workspace.ChangeTaskAsync(f.Manager,new(id,tasks.CaseVersion,null,0,CaseTaskAction.Save,"Задача "+i,"",WorkTaskType.General,f.ManagerEmployeeId,DateTimeOffset.UtcNow.AddDays(-1),true,Guid.CreateVersion7()),"o01",CancellationToken.None);
        }
        var card=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        await f.Workspace.DecideAsync(f.Manager,DecisionPresentationTests.Command(card,ProcurementAction.Forward,f.EmployeeId("head-phase1@test.invalid")),"o01",CancellationToken.None);
        await f.IngestChangedAvitoAsync(pair.Agent,pair.Administration,1_750_000m);
        await f.CreateUnlinkedManualAsync();
        foreach(var subject in new[]{f.Manager,f.Head,f.SecondManager,f.ForeignOwner})
        {
            var view=await overview.ReadAsync(subject,CancellationToken.None);
            Assert.AreEqual((await incoming.ReadAsync(subject,IncomingOverviewSelection.Filter("new"),CancellationToken.None)).Total,view.NewIncoming.Value);
            Assert.AreEqual((await incoming.ReadAsync(subject,IncomingOverviewSelection.Filter("attention"),CancellationToken.None)).Total,view.AttentionGroups.Single(x=>x.Label=="Входящие").Count);
            Assert.AreEqual((await queue.ReadPageAsync(subject,new(),CancellationToken.None)).Total,view.ActiveProcurement.Value);
            Assert.AreEqual((await queue.ReadPageAsync(subject,new(Stage:"pending_head"),CancellationToken.None)).Total,view.WaitingDecision.Value);
            Assert.AreEqual((await queue.ReadPageAsync(subject,new(AttentionOnly:true),CancellationToken.None)).Total,view.AttentionGroups.Single(x=>x.Label=="Закупка").Count);
        }
        var actual=await overview.ReadAsync(f.Manager,CancellationToken.None);
        Assert.AreEqual(1,actual.ActiveProcurement.Value);Assert.AreEqual(1,actual.WaitingDecision.Value);
        Assert.AreEqual(1,actual.AttentionGroups.Single(x=>x.Label=="Закупка").Count,"Two tasks and source changes are one case.");
        await using(var db=f.Sandbox.Context())
        {
            var settings=await db.EmployeeAccessSettings.SingleAsync(x=>x.EmployeeId==f.ManagerEmployeeId);
            settings.ProcurementAccess=ProcurementAccessLevel.None;await db.SaveChangesAsync();
        }
        var limited=await overview.ReadAsync(f.Manager,CancellationToken.None);
        Assert.IsFalse(limited.CanViewProcurement);Assert.IsTrue(limited.CanViewIncoming);
        Assert.IsFalse(limited.AttentionGroups.Any(x=>x.Label=="Закупка"));
        Assert.AreEqual(1,limited.NewIncoming.Value);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>queue.ReadPageAsync(f.Manager,new(AttentionOnly:true),CancellationToken.None));
        await using(var db=f.Sandbox.Context())
        {
            var settings=await db.EmployeeAccessSettings.SingleAsync(x=>x.EmployeeId==f.ManagerEmployeeId);
            settings.IncomingAccess=IncomingAccessLevel.None;await db.SaveChangesAsync();
        }
        Assert.IsFalse((await overview.ReadAsync(f.Manager,CancellationToken.None)).CanViewIncoming);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>incoming.ReadAsync(f.Manager,IncomingOverviewSelection.Filter("new"),CancellationToken.None));
        var unchanged=await presets.ReadAsync(f.Owner,CancellationToken.None);
        Assert.AreEqual(first.Version,unchanged.Single(x=>x.Id==first.Id).Version);
        Assert.AreEqual(second.Version,unchanged.Single(x=>x.Id==second.Id).Version);
    }
}
