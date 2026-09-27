using System.Text.Json;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class CaseCommunicationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [TestMethod]
    public async Task CommunicationWithoutTaskPreservesKnownPricesAndLegacyDetails()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await Create(f);
        var original = (await Command(f,id)).Communication with { SellerPrice=2000000m,BuyerOffer=1800000m,AgreedPrice=1900000m,
            Conditions="Прежние условия",Comment="Прежний комментарий",NextStep="Старая договорённость",Channel="Телефон" };
        Guid oldId = await f.Workspace.AddNegotiationWithIdAsync(f.Manager,original,"legacy",Ct);
        var blankPrices = await Command(f,id);
        // Пустое название задачи означает отсутствие задачи даже при заполненных других полях строки.
        blankPrices = blankPrices with { NextTask=new(" ",DateTimeOffset.UtcNow,true,Guid.Empty) };
        Guid added = await f.Workspace.RecordCommunicationAsync(f.Manager,blankPrices,"new",Ct);
        await f.Workspace.RecordCommunicationAsync(f.Manager,blankPrices,"retry",Ct);
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>f.Workspace.RecordCommunicationAsync(f.Manager,
            blankPrices with { Communication=blankPrices.Communication with { Outcome=" " } },"empty-text",Ct));
        await using var db = await f.Factory.CreateDbContextAsync();
        var legacy = await db.CaseNegotiations.SingleAsync(x=>x.Id==oldId);
        Assert.AreEqual("Прежние условия",legacy.Conditions); Assert.AreEqual("Прежний комментарий",legacy.Comment);
        Assert.AreEqual("Старая договорённость",legacy.NextStep); Assert.AreEqual("Телефон",legacy.Channel);
        var current = await db.CaseNegotiations.SingleAsync(x=>x.Id==added);
        Assert.IsNull(current.SellerPrice); Assert.IsNull(current.BuyerOffer); Assert.IsNull(current.AgreedPrice);
        Assert.AreEqual(0,await db.WorkTasks.CountAsync(x=>x.ObjectId==id&&!x.Completed&&!x.Deleted));
        Assert.AreEqual(2,await db.CaseNegotiations.CountAsync(x=>x.PropertyCaseId==id));
        var detail = await new ProcurementQueueV2ReadService(f.Factory,TimeProvider.System).ReadDetailAsync(f.Manager,id,Ct);
        Assert.AreEqual(2000000m,detail.LatestSellerOffer); Assert.AreEqual(1800000m,detail.LatestBuyerOffer);
        Assert.AreEqual(1900000m,detail.LatestAgreedPrice);
        Assert.AreEqual(oldId,await f.Workspace.AddNegotiationWithIdAsync(f.Manager,original,"legacy-retry",Ct));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CommunicationAndIndependentTaskCommitOnceAndReplayNeverChangesTask(bool exactTime)
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await Create(f);
        var view = await f.Workspace.ReadTasksAsync(f.Manager,id,Ct);
        Guid existing = await f.Workspace.ChangeTaskAsync(f.Manager,new(id,view.CaseVersion,null,0,CaseTaskAction.Save,
            "Уже существующая задача","Подробности",WorkTaskType.Check,f.ManagerEmployeeId,null,false,Guid.CreateVersion7()),"existing",Ct);
        var command = await Command(f,id);
        DateTimeOffset due = WorkTaskDeadline.DayStart(DateTimeOffset.UtcNow).AddDays(1);
        if(exactTime)due=due.AddHours(14).AddMinutes(30);
        command=command with { NextTask=new("Отправить документы",due,exactTime,f.ManagerEmployeeId) };
        var other = new ProcurementWorkspace(f.Factory,TimeProvider.System,f.FileStorage);
        var results=await Task.WhenAll(f.Workspace.RecordCommunicationAsync(f.Manager,command,"first",Ct),
            other.RecordCommunicationAsync(f.Manager,command,"parallel-retry",Ct));
        Assert.AreEqual(results[0],results[1]);
        view=await f.Workspace.ReadTasksAsync(f.Manager,id,Ct);
        Assert.AreEqual(command.Communication.ExpectedCaseVersion+1,view.CaseVersion);
        Assert.AreEqual(2,view.Tasks.Count(x=>!x.Completed));
        Assert.IsFalse(view.Tasks.Single(x=>x.Id==existing).Completed);
        var task=view.Tasks.Single(x=>x.Id!=existing);
        Assert.AreEqual(due,task.DueAt); Assert.AreEqual(exactTime,task.DueHasTime); Assert.AreEqual(f.ManagerEmployeeId,task.EmployeeId);
        await using(var db=await f.Factory.CreateDbContextAsync())
        {
            Assert.AreEqual(1,await db.CaseNegotiations.CountAsync(x=>x.PropertyCaseId==id));
            Assert.AreEqual(1,await db.BusinessTimeline.CountAsync(x=>x.ObjectId==id&&x.Kind=="Negotiation"));
            var audit=await db.AuditEvents.SingleAsync(x=>x.Id==command.Communication.CommandId);
            using var json=JsonDocument.Parse(audit.Changes);
            Assert.AreEqual(task.Id,json.RootElement.GetProperty("Task").GetProperty("Id").GetGuid());
        }
        // Это уже независимая задача T-01: повтор общения не восстанавливает и не меняет её.
        await f.Workspace.ChangeTaskAsync(f.Manager,new(id,view.CaseVersion,task.Id,task.Version,CaseTaskAction.Complete,
            task.Title,task.Description,task.Type,task.EmployeeId,task.DueAt,task.DueHasTime,Guid.CreateVersion7()),"complete",Ct);
        Assert.AreEqual(results[0],await other.RecordCommunicationAsync(f.Manager,command,"later-retry",Ct));
        Assert.IsTrue((await f.Workspace.ReadTasksAsync(f.Manager,id,Ct)).Tasks.Single(x=>x.Id==task.Id).Completed);
    }

    [TestMethod]
    public async Task InvalidTaskVersionsVisibilityAndClosedCaseCannotPartiallyRecordCommunication()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        Guid id=await Create(f);
        var command=await Command(f,id);
        Guid excluded=f.EmployeeId("manager2-phase1@test.invalid");
        command=command with { NextTask=new("Позвонить",null,false,excluded) };
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>f.Workspace.RecordCommunicationAsync(f.Manager,command,"wrong-assignee",Ct));
        command=command with { NextTask=command.NextTask! with { EmployeeId=f.ManagerEmployeeId } };
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>f.Workspace.RecordCommunicationAsync(f.ForeignOwner,command,"foreign",Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>f.Workspace.RecordCommunicationAsync(f.SecondManager,command,"invisible",Ct));
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>f.Workspace.RecordCommunicationAsync(f.Manager,
            command with { NextTask=command.NextTask! with { DueAt=WorkTaskDeadline.DayStart(DateTimeOffset.UtcNow).AddHours(1),DueHasTime=false } },"bad-date",Ct));
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(()=>f.Workspace.RecordCommunicationAsync(f.Manager,
            command with { Communication=command.Communication with { ExpectedCaseVersion=999 } },"stale",Ct));
        await using(var db=await f.Factory.CreateDbContextAsync())
        {
            Assert.AreEqual(0,await db.CaseNegotiations.CountAsync(x=>x.PropertyCaseId==id));
            Assert.AreEqual(0,await db.WorkTasks.CountAsync(x=>x.ObjectId==id&&x.IsUserTask));
            Assert.AreEqual(0,await db.AuditEvents.CountAsync(x=>x.Id==command.Communication.CommandId));
            Assert.AreEqual(0,await db.BusinessTimeline.CountAsync(x=>x.ObjectId==id&&(x.Kind=="Negotiation"||x.Kind=="CaseTaskSave")));
            var item=await db.PropertyCases.SingleAsync(x=>x.Id==id);
            Assert.AreEqual(command.Communication.ExpectedCaseVersion,item.Version);
            item.StageId="acquired";await db.SaveChangesAsync();
        }
        command=command with { Communication=command.Communication with { ExpectedCaseVersion=(await f.ReadCaseAsync(id)).Version } };
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>f.Workspace.RecordCommunicationAsync(f.Manager,command,"closed",Ct));
        await using(var db=await f.Factory.CreateDbContextAsync())
        { var item=await db.PropertyCases.SingleAsync(x=>x.Id==id);item.StageId="analysis";await db.SaveChangesAsync(); }
        command=command with { Communication=command.Communication with { ExpectedCaseVersion=(await f.ReadCaseAsync(id)).Version } };
        await f.Workspace.RecordCommunicationAsync(f.Manager,command,"corrected-same-id",Ct);
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>f.Workspace.RecordCommunicationAsync(f.Manager,
            command with { NextTask=command.NextTask! with { Title="Иной payload" } },"reused-key",Ct));
    }

    private static async Task<Guid> Create(ProcurementTests.Phase1Fixture f)=>
        (await f.Workspace.CreateManualCaseAsync(f.Manager,new("Объект T-02","Москва",null,1000000m,900m,"Тест общения",Guid.CreateVersion7()),"create",Ct)).CaseId;
    private static async Task<RecordCaseCommunication> Command(ProcurementTests.Phase1Fixture f,Guid id)=>
        new(new(id,(await f.ReadCaseAsync(id)).Version,null,null,null,"Звонок","Продавец","Обсудили условия встречи","","","",null,
            DateTimeOffset.UtcNow.AddMinutes(-1),Guid.CreateVersion7()));
}
