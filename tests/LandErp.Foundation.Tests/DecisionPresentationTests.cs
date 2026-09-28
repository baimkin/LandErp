using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Server.Components.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass, TestCategory("PostgreSQL")]
public sealed class DecisionPresentationTests
{
    [TestMethod]
    public async Task ExistingWorkflowRightsAndSourceReviewMatchDecisionPresentation()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(true,true);
        var pair=await f.IngestMarketplacePairAsync();
        Guid id=(await f.Workspace.TakeToWorkAsync(f.Manager,new(pair.AvitoId),"u03",CancellationToken.None)).CaseId;
        Guid head=f.EmployeeId("head-phase1@test.invalid");
        var card=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        Assert.IsTrue(card.CanManagerDecide);
        Assert.IsTrue(CaseDecisionPresentation.Actions(card).Contains(ProcurementAction.Forward));
        Assert.IsNull(CaseDecisionPresentation.Blocked(card,ProcurementAction.Forward));
        Assert.IsFalse(CaseDecisionPresentation.Actions(card).Contains(ProcurementAction.Approve));
        StringAssert.Contains(CaseDecisionPresentation.Responsibility(card with {Item=card.Item with {Assignee=null}}),"Исполнитель не указан");
        Assert.IsNotNull(CaseDecisionPresentation.Blocked(card with {Heads=[]},ProcurementAction.Forward));
        await f.Workspace.DecideAsync(f.Manager,Command(card,ProcurementAction.Forward,head),"u03",CancellationToken.None);
        var waiting=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        Assert.HasCount(0,CaseDecisionPresentation.Actions(waiting));
        StringAssert.Contains(CaseDecisionPresentation.Responsibility(waiting),"назначенному руководителю");
        var headCard=await f.Workspace.ReadCardAsync(f.Head,id,CancellationToken.None);
        Assert.IsTrue(headCard.CanHeadDecide);
        Assert.IsNull(CaseDecisionPresentation.Blocked(headCard,ProcurementAction.Approve));
        await f.IngestChangedAvitoAsync(pair.Agent,pair.Administration,1_850_000m);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(()=>f.Workspace.DecideAsync(f.Head,Command(headCard,ProcurementAction.Approve),"u03",CancellationToken.None));
        headCard=await f.Workspace.ReadCardAsync(f.Head,id,CancellationToken.None);
        var comparison=await f.Workspace.ReadSourceChangesAsync(f.Head,id,pair.AvitoId,CancellationToken.None);
        await f.Workspace.AcknowledgeSourceChangesAsync(f.Head,new(id,comparison.LinkId,pair.AvitoId,comparison.Revision),CancellationToken.None);
        headCard=await f.Workspace.ReadCardAsync(f.Head,id,CancellationToken.None);
        StringAssert.Contains(CaseDecisionPresentation.Blocked(headCard,ProcurementAction.Approve)!,"Источники изменились");
        Assert.IsNull(CaseDecisionPresentation.Blocked(headCard,ProcurementAction.Return));
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>f.Workspace.DecideAsync(f.Head,Command(headCard,ProcurementAction.Approve),"u03",CancellationToken.None));
        await f.Workspace.DecideAsync(f.Head,Command(headCard,ProcurementAction.Return,f.ManagerEmployeeId),"u03",CancellationToken.None);
        card=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        await f.Workspace.DecideAsync(f.Manager,Command(card,ProcurementAction.Forward,head),"u03",CancellationToken.None);
        headCard=await f.Workspace.ReadCardAsync(f.Head,id,CancellationToken.None);
        await f.Workspace.DecideAsync(f.Head,Command(headCard,ProcurementAction.Approve),"u03",CancellationToken.None);
        card=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        Assert.AreEqual("negotiation",card.Item.Stage);
        Assert.IsNull(card.AcquisitionDate);
        StringAssert.Contains(CaseDecisionPresentation.State(card),"Покупка ещё не оформлена");
        await f.Workspace.DecideAsync(f.Manager,Command(card,ProcurementAction.Monitor),"u03",CancellationToken.None);
        card=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        StringAssert.Contains(CaseDecisionPresentation.State(card),"на наблюдении");
        Assert.IsFalse(CaseDecisionPresentation.CanPurchase(card with{CanConfirmPurchase=true}));
        await f.Workspace.DecideAsync(f.Manager,Command(card,ProcurementAction.Reject),"u03",CancellationToken.None);
        card=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        Assert.HasCount(0,CaseDecisionPresentation.Actions(card));
        StringAssert.Contains(CaseDecisionPresentation.State(card),"Объект отклонён");
        Assert.HasCount(0,CaseDecisionPresentation.Actions(card with{Item=card.Item with{Stage="acquired"}}));
        Assert.HasCount(0,CaseDecisionPresentation.Actions(card with{CanManagerDecide=true,Item=card.Item with{Stage="approved",Changed=false}}));
        await using(var db=f.Sandbox.Context())
        {
            var settings=await db.EmployeeAccessSettings.SingleAsync(x=>x.EmployeeId==f.ManagerEmployeeId);
            settings.ProcurementAccess=ProcurementAccessLevel.Read; await db.SaveChangesAsync();
        }
        card=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        Assert.HasCount(0,CaseDecisionPresentation.Actions(card));
        StringAssert.Contains(CaseDecisionPresentation.Responsibility(card),"только для просмотра");
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>f.Workspace.DecideAsync(f.Manager,Command(card,ProcurementAction.Forward,head),"u03",CancellationToken.None));
    }

    [TestMethod]
    public async Task PurchaseRemainsSeparateAndCompletedCaseHasNoDecisionActions()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);
        Guid source=await f.CreateUnlinkedManualAsync();
        Guid id=(await f.Workspace.TakeToWorkAsync(f.Manager,new(source),"u03",CancellationToken.None)).CaseId;
        await using(var db=f.Sandbox.Context())
        {
            var settings=await db.EmployeeAccessSettings.SingleAsync(x=>x.EmployeeId==f.ManagerEmployeeId);
            settings.CanConfirmPurchase=true;await db.SaveChangesAsync();
        }
        var card=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        Assert.IsTrue(CaseDecisionPresentation.CanPurchase(card));
        await f.Workspace.DecideAsync(f.Manager,Command(card,ProcurementAction.Monitor),"u03",CancellationToken.None);
        card=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        Assert.IsFalse(CaseDecisionPresentation.CanPurchase(card));
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>f.Workspace.MarkAcquiredAsync(f.Manager,new(id,card.Item.CaseVersion,1_500_000m,DateOnly.FromDateTime(DateTime.UtcNow),"Покупка"),"u03",CancellationToken.None));
        await f.Workspace.DecideAsync(f.Manager,Command(card,ProcurementAction.Clarify),"u03",CancellationToken.None);
        card=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        await f.Workspace.MarkAcquiredAsync(f.Manager,new(id,card.Item.CaseVersion,1_500_000m,DateOnly.FromDateTime(DateTime.UtcNow),"Покупка"),"u03",CancellationToken.None);
        card=await f.Workspace.ReadCardAsync(f.Manager,id,CancellationToken.None);
        Assert.AreEqual("acquired",card.Item.Stage);
        Assert.HasCount(0,CaseDecisionPresentation.Actions(card));
        StringAssert.Contains(CaseDecisionPresentation.State(card),"Покупка оформлена");
        Assert.IsFalse(CaseDecisionPresentation.CanPurchase(card));
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>f.Workspace.DecideAsync(f.Manager,Command(card,ProcurementAction.Monitor),"u03",CancellationToken.None));
    }
    internal static DecisionCommand Command(CaseCard card,ProcurementAction action,Guid? target=null)=>new(card.Item.CaseId,card.Item.CaseVersion,card.Item.SourceRevision,action,"Проверены данные объекта","Уточнить результаты анализа",target,null);
}
