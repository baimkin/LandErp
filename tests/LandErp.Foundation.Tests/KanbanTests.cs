using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using LandErp.Infrastructure.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass, TestCategory("PostgreSQL")]
public sealed class KanbanTests
{
    [TestMethod]
    public async Task ProductionUpgradeImportsOnlyUnpositionedActiveCasesAndPreservesRepeatedRuns()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid active = await f.InsertIndependentCaseAsync("До канбана");
        Guid acquired = await f.InsertIndependentCaseAsync("Куплен");
        Guid rejected = await f.InsertIndependentCaseAsync("Отклонён");
        var before = await f.ReadCaseAsync(active);
        int tasks;
        int timeline;
        await using (var db = f.Sandbox.Context())
        {
            (await db.PropertyCases.SingleAsync(x => x.Id == acquired)).StageId = "acquired";
            (await db.PropertyCases.SingleAsync(x => x.Id == rejected)).StageId = "rejected";
            await db.SaveChangesAsync();
            tasks = await db.WorkTasks.CountAsync();
            timeline = await db.BusinessTimeline.CountAsync();
            // Reproduce an existing production database before the Kanban schema.
            await db.Database.GetService<IMigrator>().MigrateAsync("20260928082507_CheckNoteEntries");
        }
        await ProductionDatabaseInitializer.InitializeAsync(f.Sandbox.AdminConnection,
            f.Sandbox.MigratorConnection, f.Sandbox.RuntimeConnection);
        var k = Service(f);
        var cfg = await Config(k, f.Owner);
        var pipeline = cfg.Pipelines.Single();
        var initial = cfg.Stages.Single(x => x.IsInitial);
        Assert.AreEqual("Взят в работу", initial.Name);
        Assert.AreEqual(initial.Id, (await Card(k, f.Manager, pipeline.Id, active)).Membership.StageId);
        var after = await f.ReadCaseAsync(active);
        Assert.AreEqual(before.StageId, after.StageId);
        Assert.AreEqual(before.Version, after.Version);
        Assert.AreEqual(before.AssignmentId, after.AssignmentId);
        Assert.AreEqual(before.WorkTaskId, after.WorkTaskId);
        await using (var db = f.Sandbox.Context())
            Assert.AreEqual(timeline, await db.BusinessTimeline.CountAsync());

        var next = cfg.Stages.First(x => !x.IsInitial);
        await k.MoveAsync(f.Manager, await Move(k, f.Manager, pipeline.Id, active, next.Id), "upgrade-move", Ct);
        var moved = (await Card(k, f.Manager, pipeline.Id, active)).Membership;
        Guid otherPipeline = await SecondPipeline(k, f.Owner);
        Guid otherCase = await f.InsertIndependentCaseAsync("Уже в другой воронке");
        var otherConfig = await Config(k, f.Owner);
        await k.AddAsync(f.Manager, new(Guid.CreateVersion7(), otherCase, otherPipeline,
            otherConfig.Pipelines.Single(x => x.Id == otherPipeline).Version), "other-board", Ct);
        int transitions;
        await using (var db = f.Sandbox.Context())
        {
            transitions = await db.KanbanTransitions.CountAsync();
            timeline = await db.BusinessTimeline.CountAsync();
        }
        for (int attempt = 0; attempt < 2; attempt++)
            await ProductionDatabaseInitializer.InitializeAsync(f.Sandbox.AdminConnection,
                f.Sandbox.MigratorConnection, f.Sandbox.RuntimeConnection);
        var preserved = (await Card(k, f.Manager, pipeline.Id, active)).Membership;
        Assert.AreEqual(moved.StageId, preserved.StageId);
        Assert.AreEqual(moved.StageEnteredAt, preserved.StageEnteredAt);
        Assert.AreEqual(moved.Version, preserved.Version);
        await using (var db = f.Sandbox.Context())
        {
            Assert.AreEqual(transitions, await db.KanbanTransitions.CountAsync());
            Assert.AreEqual(1, await db.KanbanMemberships.CountAsync(x => x.PropertyCaseId == active));
            Assert.AreEqual(1, await db.KanbanMemberships.CountAsync(x => x.PropertyCaseId == otherCase));
            Assert.IsFalse(await db.KanbanMemberships.AnyAsync(x => x.PropertyCaseId == acquired || x.PropertyCaseId == rejected));
            Assert.AreEqual(tasks + 1, await db.WorkTasks.CountAsync());
            Assert.AreEqual(timeline, await db.BusinessTimeline.CountAsync());
            Assert.AreEqual(await db.Organizations.CountAsync(), await db.KanbanPipelines.CountAsync(x => x.IsDefault && x.IsActive));
        }
        Guid created = await Create(f);
        Assert.AreEqual(initial.Id, (await Card(k, f.Manager, pipeline.Id, created)).Membership.StageId);
    }

    private static readonly CancellationToken Ct = CancellationToken.None;
    private static KanbanWorkspace Service(ProcurementTests.Phase1Fixture f) => new(f.Factory,new EmployeeAccessService(f.Factory),TimeProvider.System);
    private static Task<KanbanConfiguration> Config(KanbanWorkspace k, Subject actor)=>k.ReadConfigurationAsync(actor,Ct);
    private static SaveKanbanPipeline Draft(KanbanConfiguration cfg, Guid id)
    {
        var p=cfg.Pipelines.Single(x=>x.Id==id);
        return new(id,p.Version,p.Name,p.SortOrder,p.IsDefault,true,cfg.Stages.Where(s=>s.PipelineId==id).Select(s=>new KanbanStageDraft(s.Id,s.Name,s.Description,s.ColorKey,s.IsInitial,s.Kind,s.IsHiddenOnBoard)).ToArray(),cfg.Tunnels.Where(t=>cfg.Stages.Any(s=>s.Id==t.SourceStageId&&s.PipelineId==id)).Select(t=>new KanbanTunnelDraft(t.SourceStageId,t.TargetPipelineId,t.Mode)).ToArray());
    }
    private static async Task<Guid> Create(ProcurementTests.Phase1Fixture f,decimal? price=1000000m)=>(await f.Workspace.CreateManualCaseAsync(f.Manager,new("Канбан объект","Москва",null,price,900m,"Серверный тест канбана",Guid.CreateVersion7()),"kanban-test",Ct)).CaseId;
    private static async Task<KanbanCardView> Card(KanbanWorkspace k,Subject actor,Guid pipeline,Guid id)=>(await k.ReadBoardAsync(actor,pipeline,null,Ct)).Columns.SelectMany(c=>c.Cards).Single(c=>c.Membership.PropertyCaseId==id);
    private static async Task<MoveKanbanCard> Move(KanbanWorkspace k,Subject actor,Guid pipeline,Guid id,Guid stage)
    {var b=await k.ReadBoardAsync(actor,pipeline,null,Ct);var m=b.Columns.SelectMany(c=>c.Cards).Single(c=>c.Membership.PropertyCaseId==id).Membership;return new(Guid.CreateVersion7(),m.Id,m.Version,stage,b.Pipeline.Version);}
    private static async Task<Guid> SecondPipeline(KanbanWorkspace k,Subject owner)
    {return await k.SavePipelineAsync(owner,new(Guid.Empty,0,"Подготовка участка",1,false,true,[new(Guid.CreateVersion7(),"Межевание","","info",true,KanbanStageKind.Working,false),new(Guid.CreateVersion7(),"Готово","","success",false,KanbanStageKind.PositiveFinal,false)],[]),"new",Ct);}

    [TestMethod]
    public async Task ConfigurationRolesOccupiedStageVisibilityAndIsolation()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);var k=Service(f);var cfg=await Config(k,f.Owner);var p=cfg.Pipelines.Single();
        Assert.AreEqual(12,cfg.Stages.Count);Assert.AreEqual("Взят в работу",cfg.Stages.Single(s=>s.IsInitial).Name);
        Assert.IsFalse((await Config(k,f.Manager)).CanConfigureKanban);Assert.IsTrue((await Config(k,f.Head)).CanConfigureKanban);
        await using(var db=await f.Factory.CreateDbContextAsync()){var a=await db.EmployeeAccessSettings.SingleAsync(x=>x.EmployeeId==f.ManagerEmployeeId);a.CanManageTemplates=true;await db.SaveChangesAsync();}
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>k.SavePipelineAsync(f.Manager,Draft(cfg,p.Id),"denied",Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>k.SavePipelineAsync(f.Owner with{MultiFactorAuthenticated=false},Draft(cfg,p.Id),"mfa",Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>k.ReadBoardAsync(f.ForeignOwner,p.Id,null,Ct));
        Guid id=await Create(f);var final=cfg.Stages.Single(s=>s.Kind==KanbanStageKind.PositiveFinal);
        var command=await Move(k,f.Manager,p.Id,id,final.Id);await k.MoveAsync(f.Manager,command,"final",Ct);
        var before=await Card(k,f.Manager,p.Id,id);var draft=Draft(cfg,p.Id);
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>k.SavePipelineAsync(f.Head,draft with{Stages=draft.Stages.Where(s=>s.Id!=final.Id).ToArray()},"occupied",Ct));
        await k.SavePipelineAsync(f.Head,draft with{Stages=draft.Stages.Reverse().Select(s=>s with{IsHiddenOnBoard=s.Id==final.Id}).ToArray()},"hide-order",Ct);
        var board=await k.ReadBoardAsync(f.Manager,p.Id,null,Ct);Assert.AreEqual(1,board.HiddenCount);Assert.AreEqual(1,board.Total);
        var after=await Card(k,f.Manager,p.Id,id);Assert.AreEqual(before.Membership.StageEnteredAt,after.Membership.StageEnteredAt);Assert.AreEqual(before.Membership.Version,after.Membership.Version);
        var queue=await new ProcurementQueueV2ReadService(f.Factory,TimeProvider.System).ReadPageAsync(f.Manager,new(PipelineId:p.Id),Ct);Assert.AreEqual(id,queue.Items.Single().CaseId);Assert.AreEqual(final.Id,queue.Items.Single().KanbanMembership!.StageId);
        var changed=await Config(k,f.Owner);Assert.AreEqual(draft.Stages[^1].Id,changed.Stages[0].Id);Assert.AreEqual(cfg.Stages.Single(s=>s.IsInitial).Id,changed.Stages.Single(s=>s.IsInitial).Id);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(()=>k.SavePipelineAsync(f.Owner,draft,"stale-config",Ct));
        await using var seed=f.Sandbox.Context();await KanbanProvisioning.InitializeAsync(seed);await KanbanProvisioning.InitializeAsync(seed);
        Assert.AreEqual(1,await seed.KanbanMemberships.CountAsync(m=>m.PropertyCaseId==id));Assert.AreEqual(before.Membership.StageEnteredAt,(await Card(k,f.Manager,p.Id,id)).Membership.StageEnteredAt);
    }

    [TestMethod]
    public async Task MovesNoOpReplayConcurrentCommandsAndBusinessIndependence()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);var k=Service(f);var cfg=await Config(k,f.Owner);var p=cfg.Pipelines.Single();Guid id=await Create(f);var original=await f.ReadCaseAsync(id);var card=await Card(k,f.Manager,p.Id,id);
        var noop=await Move(k,f.Manager,p.Id,id,card.Membership.StageId);await k.MoveAsync(f.Manager,noop,"noop",Ct);Assert.AreEqual(card.Membership.StageEnteredAt,(await Card(k,f.Manager,p.Id,id)).Membership.StageEnteredAt);
        var first=await Move(k,f.Manager,p.Id,id,cfg.Stages[1].Id);var second=first with{CommandId=Guid.CreateVersion7(),TargetStageId=cfg.Stages[2].Id};
        async Task<bool> Try(MoveKanbanCard c){try{await k.MoveAsync(f.Manager,c,"race",Ct);return true;}catch(DbUpdateConcurrencyException){return false;}}
        bool[] results=await Task.WhenAll(Try(first),Try(second));Assert.AreEqual(1,results.Count(x=>x));var winner=results[0]?first:second;
        var result=await k.MoveAsync(f.Manager,winner,"repeat",Ct);
        cfg=await Config(k,f.Owner);await k.SavePipelineAsync(f.Owner,Draft(cfg,p.Id) with{Name="Закупка переименована"},"rename",Ct);
        Assert.AreEqual(result,await k.MoveAsync(f.Manager,winner,"repeat-after-settings",Ct));
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>k.MoveAsync(f.Manager,winner with{TargetStageId=cfg.Stages[3].Id},"reused-id",Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>k.MoveAsync(f.ForeignOwner,winner,"foreign",Ct));
        var unchanged=await f.ReadCaseAsync(id);Assert.AreEqual(original.Version,unchanged.Version);Assert.AreEqual(original.StageId,unchanged.StageId);Assert.AreEqual(original.AssignmentId,unchanged.AssignmentId);Assert.AreEqual(original.WorkTaskId,unchanged.WorkTaskId);
        var before=await Card(k,f.Manager,p.Id,id);var detail=await f.Workspace.ReadCardAsync(f.Manager,id,Ct);
        await f.Workspace.DecideAsync(f.Manager,new(id,detail.Item.CaseVersion,detail.Item.SourceRevision,ProcurementAction.Reject,"Не подходит","",null,null),"reject",Ct);
        var after=await Card(k,f.Manager,p.Id,id);Assert.AreEqual("rejected",after.BusinessStage);Assert.AreEqual(before.Membership.StageId,after.Membership.StageId);Assert.AreEqual(before.Membership.StageEnteredAt,after.Membership.StageEnteredAt);
        Assert.AreEqual(1,(await new ProcurementQueueV2ReadService(f.Factory,TimeProvider.System).ReadPageAsync(f.Manager,new(PipelineId:p.Id),Ct)).Total);
        await using var db=await f.Factory.CreateDbContextAsync();Assert.AreEqual(1,await db.KanbanTransitions.CountAsync(t=>t.PropertyCaseId==id&&t.Kind=="Move"));
        Assert.AreEqual("Закупка",(await db.KanbanTransitions.SingleAsync(t=>t.PropertyCaseId==id&&t.Kind=="Move")).FromPipelineName);
    }

    [TestMethod,DataRow(KanbanTunnelMode.Transfer),DataRow(KanbanTunnelMode.Parallel)]
    public async Task TunnelsAreAtomicReplayableConflictSafeAndAcyclic(KanbanTunnelMode mode)
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);var k=Service(f);var cfg=await Config(k,f.Owner);var p=cfg.Pipelines.Single();Guid b=await SecondPipeline(k,f.Owner);
        cfg=await Config(k,f.Owner);var final=cfg.Stages.First(s=>s.PipelineId==p.Id&&s.Kind==KanbanStageKind.PositiveFinal);
        await k.SavePipelineAsync(f.Owner,Draft(cfg,p.Id) with{Tunnels=[new(final.Id,b,mode)]},"tunnel",Ct);
        Guid id=await Create(f);var command=await Move(k,f.Manager,p.Id,id,final.Id);var result=await k.MoveAsync(f.Manager,command,"tunnel-move",Ct);Assert.AreEqual(result,await k.MoveAsync(f.Manager,command,"replay",Ct));
        Assert.AreEqual(1,(await k.ReadBoardAsync(f.Manager,b,null,Ct)).Total);Assert.AreEqual(mode==KanbanTunnelMode.Transfer?0:1,(await k.ReadBoardAsync(f.Manager,p.Id,null,Ct)).Total);
        await using(var db=await f.Factory.CreateDbContextAsync()){Assert.AreEqual(1,await db.PropertyCases.CountAsync(x=>x.Id==id));Assert.AreEqual(2,await db.KanbanMemberships.CountAsync(x=>x.PropertyCaseId==id));var source=await db.KanbanMemberships.SingleAsync(x=>x.PropertyCaseId==id&&x.PipelineId==p.Id);Assert.AreEqual(mode==KanbanTunnelMode.Transfer,source.TransferredAt!=null);Assert.AreEqual(source.TransferredAt,source.StageExitedAt);}
        // A distinct command with an already occupied destination must roll back both positions and history.
        Guid conflictId=await Create(f);cfg=await Config(k,f.Owner);await k.AddAsync(f.Manager,new(Guid.CreateVersion7(),conflictId,b,cfg.Pipelines.Single(x=>x.Id==b).Version),"explicit-add",Ct);
        var sourceBefore=await Card(k,f.Manager,p.Id,conflictId);var targetBefore=await Card(k,f.Manager,b,conflictId);
        var conflict=await Move(k,f.Manager,p.Id,conflictId,final.Id);
        var ex=await Assert.ThrowsExactlyAsync<ArgumentException>(()=>k.MoveAsync(f.Manager,conflict,"target-exists",Ct));StringAssert.Contains(ex.Message,"Подготовка участка");StringAssert.Contains(ex.Message,"Межевание");
        var sourceAfter=await Card(k,f.Manager,p.Id,conflictId);var targetAfter=await Card(k,f.Manager,b,conflictId);
        Assert.AreEqual(sourceBefore.Membership.StageId,sourceAfter.Membership.StageId);Assert.AreEqual(sourceBefore.Membership.StageEnteredAt,sourceAfter.Membership.StageEnteredAt);Assert.AreEqual(sourceBefore.Membership.Version,sourceAfter.Membership.Version);Assert.AreEqual(targetBefore.Membership.StageEnteredAt,targetAfter.Membership.StageEnteredAt);Assert.AreEqual(targetBefore.Membership.Version,targetAfter.Membership.Version);
        await using(var db=await f.Factory.CreateDbContextAsync()){Assert.AreEqual(0,await db.KanbanTransitions.CountAsync(x=>x.CommandId==conflict.CommandId));Assert.AreEqual(0,await db.AuditEvents.CountAsync(x=>x.Id==conflict.CommandId));}
        var bDraft=Draft(cfg,b);var bFinal=bDraft.Stages.Single(s=>s.Kind==KanbanStageKind.PositiveFinal);
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>k.SavePipelineAsync(f.Owner,bDraft with{Tunnels=[new(bFinal.Id,p.Id,mode)]},"cycle",Ct));
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>k.SavePipelineAsync(f.Owner,bDraft with{Tunnels=[new(bFinal.Id,b,mode)]},"self-cycle",Ct));
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>k.SavePipelineAsync(f.Owner,bDraft with{IsActive=false},"used-target",Ct));
    }

    [TestMethod]
    public async Task BoardManagerFilterKeepsCountsCardsAndPaginationInOneScope()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        var k = Service(f);
        var cfg = await Config(k, f.Owner);
        var pipeline = cfg.Pipelines.Single();

        Guid managerCase = (await f.Workspace.CreateManualCaseAsync(f.Manager,
            new("Менеджер 1", "Москва", null, 1_000_000m, 1000m, "kanban manager filter", Guid.CreateVersion7()),
            "kanban-manager-1", Ct)).CaseId;
        Guid secondCase = (await f.Workspace.CreateManualCaseAsync(f.SecondManager,
            new("Менеджер 2", "Москва", null, 2_000_000m, 1000m, "kanban manager filter", Guid.CreateVersion7()),
            "kanban-manager-2", Ct)).CaseId;

        Guid secondEmployee = f.EmployeeId("manager2-phase1@test.invalid");
        await using (var db = f.Sandbox.Context())
        {
            WorkTask[] managerCaseTasks = await db.WorkTasks
                .Where(task => task.ObjectType == "PropertyCase" && task.ObjectId == managerCase && !task.Completed && !task.Deleted)
                .ToArrayAsync();
            foreach (WorkTask task in managerCaseTasks) task.EmployeeId = secondEmployee;
            await db.SaveChangesAsync();
        }

        var all = await k.ReadBoardAsync(f.Owner, pipeline.Id, null, Ct);
        var managerOnly = await k.ReadBoardAsync(f.Owner, pipeline.Id, f.ManagerEmployeeId, null, Ct);
        var secondOnly = await k.ReadBoardAsync(f.Owner, pipeline.Id, secondEmployee, null, Ct);
        var queue = await new ProcurementQueueV2ReadService(f.Factory, TimeProvider.System)
            .ReadPageAsync(f.Owner, new(PipelineId: pipeline.Id), Ct);

        Assert.AreEqual(2, all.Total);
        Assert.AreEqual(1, managerOnly.Total);
        Assert.AreEqual(1, secondOnly.Total);
        Assert.AreEqual(managerCase, managerOnly.Columns.SelectMany(column => column.Cards).Single().Membership.PropertyCaseId);
        Assert.AreEqual(secondCase, secondOnly.Columns.SelectMany(column => column.Cards).Single().Membership.PropertyCaseId);
        Assert.AreEqual(1, managerOnly.Columns.Sum(column => column.Total));
        Assert.AreEqual(1, secondOnly.Columns.Sum(column => column.Total));
        CollectionAssert.AreEquivalent(new[] { f.ManagerEmployeeId, secondEmployee }, queue.Assignees.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public async Task PaginationTotalsNearestTaskAndOccupiedStageRace()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);var k=Service(f);var cfg=await Config(k,f.Owner);var p=cfg.Pipelines.Single();Guid first=Guid.Empty;
        for(int i=0;i<52;i++){Guid id=await Create(f,i==51?null:100m);if(i==0)first=id;}
        var b=await k.ReadBoardAsync(f.Manager,p.Id,null,Ct);var col=b.Columns.Single(c=>c.Total>0);Assert.AreEqual(52,b.Total);Assert.AreEqual(50,col.Cards.Count);Assert.AreEqual(5100m,col.KnownPriceTotals["RUB"]);Assert.AreEqual(1,col.WithoutPrice);Assert.IsNotNull(col.NextCursor);
        var next=await k.ReadBoardAsync(f.Manager,p.Id,new Dictionary<Guid,string>{{col.Stage.Id,col.NextCursor}},Ct);Assert.AreEqual(2,next.Columns.Single(c=>c.Stage.Id==col.Stage.Id).Cards.Count);Assert.AreEqual(52,col.Cards.Concat(next.Columns.Single(c=>c.Stage.Id==col.Stage.Id).Cards).Select(c=>c.Membership.Id).Distinct().Count());
        var now=DateTimeOffset.UtcNow;Guid expected=Guid.CreateVersion7();
        await using(var db=await f.Factory.CreateDbContextAsync()){db.WorkTasks.AddRange(new WorkTask{Id=Guid.CreateVersion7(),OrganizationId=f.OrganizationId,ObjectId=first,ObjectType="PropertyCase",EmployeeId=f.ManagerEmployeeId,Title="Без даты",RecordedAt=now.AddDays(-4)},new WorkTask{Id=expected,OrganizationId=f.OrganizationId,ObjectId=first,ObjectType="PropertyCase",EmployeeId=f.ManagerEmployeeId,Title="Ранняя",RecordedAt=now,DueAt=now.AddDays(-2)},new WorkTask{Id=Guid.CreateVersion7(),OrganizationId=f.OrganizationId,ObjectId=first,ObjectType="PropertyCase",EmployeeId=f.ManagerEmployeeId,Title="Удалена",RecordedAt=now,DueAt=now.AddDays(-5),Deleted=true});await db.SaveChangesAsync();}
        var card=await Card(k,f.Manager,p.Id,first);Assert.AreEqual(expected,card.NextTask!.Id);Assert.IsTrue(card.NextTask.Overdue);
        var stage=cfg.Stages[1];var move=await Move(k,f.Manager,p.Id,first,stage.Id);var draft=Draft(cfg,p.Id);
        async Task<bool> MoveTry(){try{await k.MoveAsync(f.Manager,move,"race-stage",Ct);return true;}catch(Exception ex)when(ex is ArgumentException or DbUpdateConcurrencyException){return false;}}
        async Task<bool> DeleteTry(){try{await k.SavePipelineAsync(f.Owner,draft with{Stages=draft.Stages.Where(s=>s.Id!=stage.Id).ToArray()},"race-delete",Ct);return true;}catch(ArgumentException){return false;}}
        var results=await Task.WhenAll(MoveTry(),DeleteTry());Assert.AreEqual(1,results.Count(x=>x));
        await using var finalDb=await f.Factory.CreateDbContextAsync();Assert.IsFalse(await finalDb.KanbanMemberships.AnyAsync(m=>m.TransferredAt==null&&finalDb.KanbanStages.Any(s=>s.Id==m.StageId&&!s.IsActive)));
    }
}


