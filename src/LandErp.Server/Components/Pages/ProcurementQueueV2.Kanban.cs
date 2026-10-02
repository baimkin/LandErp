using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Server.Components.Procurement;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace LandErp.Server.Components.Pages;

public partial class ProcurementQueueV2
{
    [Inject] private IKanbanWorkspace Kanban { get; set; } = default!;
    [Inject] private IJSRuntime KanbanJs { get; set; } = default!;
    [Parameter, SupplyParameterFromQuery(Name="pipeline")] public Guid? PipelineId { get; set; }
    [Parameter, SupplyParameterFromQuery(Name="view")] public string? View { get; set; }
    private (Guid?,string?)? appliedKanbanRoute;
    private bool InPipeline => PipelineId != null || ShowKanban;
    private bool ShowKanban => View == "kanban";
    private Guid activePipeline;
    private bool kanbanSuccess;
    private int kanbanNoticeVersion;
    private string? moveTitle;
    private KanbanConfiguration? kanbanConfig;
    private KanbanBoardView? kanbanBoard;
    private bool kanbanSettingsOpen, kanbanBusy, addExistingOpen;
    private string? kanbanMessage, candidateError;
    private MoveKanbanCard? pendingMove;
    private AddKanbanCase? pendingAdd;
    private string candidateSearch="";
    private IReadOnlyList<KanbanCandidate> candidates=[];
    private CaseNextAction? boardTask;
    private (Guid CaseId,Guid TaskId)? boardTaskTarget;
    private Guid? taskToOpen;
    private IReadOnlyList<KanbanStage> CurrentKanbanStages => kanbanConfig?.Stages.Where(s=>s.PipelineId==activePipeline).ToArray()??[];
    private KanbanMembership? SelectedMembership => kanbanBoard?.Columns.SelectMany(c=>c.Cards).FirstOrDefault(c=>c.Membership.PropertyCaseId==selectedCaseId)?.Membership
        ?? queuePage?.Items.FirstOrDefault(c=>c.CaseId==selectedCaseId)?.KanbanMembership;
    private bool SelectedCanMove => kanbanBoard?.Columns.SelectMany(c=>c.Cards).FirstOrDefault(c=>c.Membership.PropertyCaseId==selectedCaseId)?.CanMove
        ?? queuePage?.Items.FirstOrDefault(c=>c.CaseId==selectedCaseId)?.CanMoveKanban ?? false;
    private Guid? SelectedKanbanAssigneeId => Guid.TryParse(assigneeFilter, out Guid id) ? id : null;

    private async Task ReadKanbanAsync()
    {
        if(appliedKanbanRoute!=(PipelineId,View)){offset=0;appliedKanbanRoute=(PipelineId,View);}
        kanbanConfig=await Kanban.ReadConfigurationAsync(CurrentSubject,CancellationToken.None);
        activePipeline=PipelineId??kanbanConfig.Pipelines.FirstOrDefault(p=>p.IsDefault)?.Id??Guid.Empty;
        kanbanBoard=InPipeline?await Kanban.ReadBoardAsync(CurrentSubject,activePipeline,SelectedKanbanAssigneeId,null,CancellationToken.None):null;
    }
    private async Task ChangePipeline(ChangeEventArgs e)
    {
        if(!Guid.TryParse(e.Value?.ToString(),out var id))return;
        await SelectQueueView(new(id!=Guid.Empty&&ShowKanban?"kanban":"table",id==Guid.Empty?null:id));
    }
    private async Task ChangeView(bool board)
    {
        Guid? target=InPipeline?activePipeline:null;
        if(board && target==null)
        {
            try { var saved=await KanbanJs.InvokeAsync<QueueViewPreference?>("LandErpKanban.loadView",CurrentSubject.UserId.ToString("N"));target=saved?.LastPipelineId; } catch(JSException){}catch(JSDisconnectedException){}
            if(target==null || kanbanConfig?.Pipelines.Any(p=>p.Id==target)!=true)target=kanbanConfig?.Pipelines.FirstOrDefault(p=>p.IsDefault)?.Id??(kanbanConfig?.Pipelines.Count>0?kanbanConfig.Pipelines[0].Id:null);
            if(target==null){kanbanSuccess=false;kanbanNoticeVersion++;kanbanMessage="Сначала создайте воронку в настройках канбана.";return;}
        }
        await SelectQueueView(new(board?"kanban":"table",target));
    }
    private async Task SelectQueueView(QueueViewPreference preference)
    {
        // Presentation only, scoped to the signed-in account; no object data is stored.
        if(preference.PipelineId==null && InPipeline)preference=preference with{LastPipelineId=activePipeline};
        try{await KanbanJs.InvokeVoidAsync("LandErpKanban.saveView",CurrentSubject.UserId.ToString("N"),preference);}catch(JSException){}catch(JSDisconnectedException){}
        Navigation.NavigateTo(QueueViewUrl(preference));
    }
    private static string QueueViewUrl(QueueViewPreference preference)=>$"/procurement?view={preference.View}"+(preference.PipelineId is Guid id?$"&pipeline={id}":"");
    public sealed record QueueViewPreference(string View,Guid? PipelineId,Guid? LastPipelineId=null);
    private string? preferenceRoute;
    private async Task RestoreQueueView()
    {
        if(Loading||Forbidden||Error!=null||kanbanConfig==null||CurrentSubject.UserId==Guid.Empty||preferenceRoute==Navigation.Uri)return;
        preferenceRoute=Navigation.Uri;
        // Explicit links (including overview filters) always take precedence over preferences.
        if(new Uri(preferenceRoute).Query.Length!=0)return;
        try
        {
            var saved=await KanbanJs.InvokeAsync<QueueViewPreference?>("LandErpKanban.loadView",CurrentSubject.UserId.ToString("N"));
            if(saved==null||saved.View is not ("kanban" or "table"))return;
            if(saved.PipelineId is Guid id&&!kanbanConfig.Pipelines.Any(p=>p.Id==id))
                saved=saved with{PipelineId=kanbanConfig.Pipelines.FirstOrDefault(p=>p.IsDefault)?.Id};
            if(saved.View=="kanban"&&saved.PipelineId==null)return;
            Navigation.NavigateTo(QueueViewUrl(saved),replace:true);
        }
        catch(JSException){}catch(JSDisconnectedException){}
    }
    private Task MoveBoardCard((KanbanCardView Card,Guid Stage) request)=>MoveSelectedStage((request.Card.Membership,request.Stage));
    private async Task MoveSelectedStage((KanbanMembership Membership,Guid Stage) request)
    {
        if(kanbanBusy||pendingMove!=null||kanbanBoard==null)return;
        moveTitle=kanbanBoard.Columns.SelectMany(c=>c.Cards).FirstOrDefault(c=>c.Membership.Id==request.Membership.Id)?.Title??queuePage?.Items.FirstOrDefault(c=>c.KanbanMembership?.Id==request.Membership.Id)?.Title??"Объект";
        pendingMove=new(Guid.CreateVersion7(),request.Membership.Id,request.Membership.Version,request.Stage,kanbanBoard.Pipeline.Version);
        await RetryKanbanMove();
    }
    private async Task RetryKanbanMove()
    {
        if(pendingMove==null||kanbanBusy)return;
        kanbanBusy=true;bool refresh=false;
        try{var result=await Kanban.MoveAsync(CurrentSubject,pendingMove,pendingMove.CommandId.ToString(),CancellationToken.None);kanbanSuccess=false;kanbanNoticeVersion++;kanbanMessage=moveTitle+" → "+(kanbanConfig?.Stages.FirstOrDefault(s=>s.Id==result.StageId)?.Name??result.Message);kanbanSuccess=true;pendingMove=null;refresh=true;}
        catch(ArgumentException ex){kanbanSuccess=false;kanbanNoticeVersion++;kanbanMessage=ex.Message;pendingMove=null;refresh=true;}
        catch(DbUpdateConcurrencyException ex){kanbanSuccess=false;kanbanNoticeVersion++;kanbanMessage=ex.Message;pendingMove=null;refresh=true;}
        catch(AccessDeniedException){kanbanSuccess=false;kanbanNoticeVersion++;kanbanMessage="Нет права на перемещение этого объекта.";pendingMove=null;refresh=true;}
        catch(Exception){kanbanSuccess=false;kanbanNoticeVersion++;kanbanMessage="Результат переноса не подтверждён. Нажмите «Проверить результат переноса»: будет повторена та же команда.";}
        finally{kanbanBusy=false;}
        if(refresh){try{await ReadAsync();}catch(Exception){kanbanSuccess=false;kanbanMessage+=" Не удалось обновить экран. Обновите данные.";}}
    }
    private async Task MoreKanban(KanbanColumn column)
    {
        if(column.NextCursor==null||kanbanBusy||kanbanBoard==null)return;
        kanbanBusy=true;
        try{
            var page=await Kanban.ReadBoardAsync(CurrentSubject,activePipeline,SelectedKanbanAssigneeId,new Dictionary<Guid,string>{{column.Stage.Id,column.NextCursor}},CancellationToken.None);
            var next=page.Columns.Single(c=>c.Stage.Id==column.Stage.Id);
            if(page.Pipeline.Version!=kanbanBoard.Pipeline.Version){kanbanSuccess=false;kanbanNoticeVersion++;kanbanMessage="Настройки изменились. Доска обновлена.";await ReadAsync();return;}
            var combined=column.Cards.Concat(next.Cards).DistinctBy(c=>c.Membership.Id).ToArray();
            kanbanBoard=kanbanBoard with { Columns=kanbanBoard.Columns.Select(c=>c.Stage.Id==column.Stage.Id?next with {Cards=combined}:c).ToArray(),Total=page.Total,HiddenCount=page.HiddenCount,ServerNow=page.ServerNow };
        }catch(Exception){kanbanSuccess=false;kanbanNoticeVersion++;kanbanMessage="Не удалось загрузить следующую часть. Повторите запрос.";}finally{kanbanBusy=false;}
    }
    private async Task SettingsSaved(Guid id){kanbanSettingsOpen=false;await ReadAsync();if(!kanbanConfig!.Pipelines.Any(p=>p.Id==activePipeline))Navigation.NavigateTo("/procurement?view=kanban");}
    private async Task OpenAddExisting(){addExistingOpen=true;candidateSearch="";candidateError=null;await FindCandidates();}
    private async Task FindCandidates(){try{candidates=await Kanban.CandidatesAsync(CurrentSubject,activePipeline,candidateSearch,CancellationToken.None);}catch(Exception){candidateError="Не удалось прочитать доступные объекты.";}}
    private async Task AddExisting(KanbanCandidate candidate)
    {
        if(kanbanBusy||pendingAdd!=null||kanbanBoard==null)return;
        pendingAdd=new(Guid.CreateVersion7(),candidate.Id,activePipeline,kanbanBoard.Pipeline.Version,candidate.TransferredMembershipVersion);
        await RetryAddExisting();
    }
    private async Task RetryAddExisting()
    {
        if(pendingAdd==null||kanbanBusy)return;kanbanBusy=true;
        try{var result=await Kanban.AddAsync(CurrentSubject,pendingAdd,pendingAdd.CommandId.ToString(),CancellationToken.None);pendingAdd=null;addExistingOpen=false;kanbanSuccess=false;kanbanNoticeVersion++;kanbanMessage=result.Message;await ReadAsync();}
        catch(ArgumentException ex){candidateError=ex.Message;pendingAdd=null;}
        catch(DbUpdateConcurrencyException){candidateError="Данные изменились. Закройте и заново откройте список.";pendingAdd=null;}
        catch(AccessDeniedException){candidateError="Нет права добавить объект.";pendingAdd=null;}
        catch(Exception){candidateError="Сохранение не подтверждено. Проверьте результат той же команды.";}
        finally{kanbanBusy=false;}
    }
    private async Task OpenKanbanDrawer(Guid id){await KanbanJs.InvokeVoidAsync("LandErpKanban.rememberFocus");await OpenDrawerAsync(id);}
    private async Task OpenKanbanTask((Guid CaseId,Guid TaskId) target){boardTaskTarget=target;taskToOpen=target.TaskId;await Task.CompletedTask;}
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await RestoreQueueView();
        if(restoreKanbanFocus){restoreKanbanFocus=false;try{await KanbanJs.InvokeVoidAsync("LandErpKanban.restoreFocus");}catch(JSDisconnectedException){}}
        if(taskToOpen is Guid id&&boardTask!=null){taskToOpen=null;await boardTask.OpenTaskAsync(id);}
    }
    private bool restoreKanbanFocus;
    private void RestoreKanbanFocus()=>restoreKanbanFocus=true;
    private void KanbanDrawerKey(KeyboardEventArgs e){if(e.Key=="Escape"&&!kanbanSettingsOpen)CloseDrawer();}
}

