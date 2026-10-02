using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Domain;

namespace LandErp.Application.Modules.Procurement.Contracts;

public sealed record KanbanConfiguration(IReadOnlyList<KanbanPipeline> Pipelines, IReadOnlyList<KanbanStage> Stages,
    IReadOnlyList<KanbanTunnel> Tunnels, bool CanConfigureKanban, bool CanManageCards);
public sealed record KanbanStageDraft(Guid Id, string Name, string Description, string ColorKey,
    bool IsInitial, KanbanStageKind Kind, bool IsHiddenOnBoard);
public sealed record KanbanTunnelDraft(Guid SourceStageId, Guid TargetPipelineId, KanbanTunnelMode Mode);
// One draft is committed atomically; closing either editor never writes partial settings.
public sealed record SaveKanbanPipeline(Guid PipelineId, long ExpectedVersion, string Name, int SortOrder,
    bool IsDefault, bool IsActive, IReadOnlyList<KanbanStageDraft> Stages, IReadOnlyList<KanbanTunnelDraft> Tunnels);
public sealed record MoveKanbanCard(Guid CommandId, Guid MembershipId, long ExpectedMembershipVersion,
    Guid TargetStageId, long ExpectedPipelineVersion);
public sealed record AddKanbanCase(Guid CommandId, Guid CaseId, Guid PipelineId, long ExpectedPipelineVersion,
    long? ExpectedMembershipVersion = null);
public sealed record KanbanCommandResult(Guid MembershipId, Guid PipelineId, Guid StageId, string Message);
public sealed record KanbanTask(Guid Id, string Title, DateTimeOffset? DueAt, bool DueHasTime, bool Overdue);
public sealed record KanbanCardView(KanbanMembership Membership, string BusinessNumber, string Title, string? Location,
    decimal? Price, string Currency, decimal? AreaSquareMeters, string Assignee, string BusinessStage,
    KanbanTask? NextTask, bool CanMove);
public sealed record KanbanColumn(KanbanStage Stage, IReadOnlyList<KanbanCardView> Cards, int Total,
    IReadOnlyDictionary<string, decimal> KnownPriceTotals, int WithoutPrice, string? NextCursor);
public sealed record KanbanBoardView(KanbanPipeline Pipeline, IReadOnlyList<KanbanColumn> Columns, int Total,
    int HiddenCount, DateTimeOffset ServerNow);
public sealed record KanbanCandidate(Guid Id, string Number, string Title, long? TransferredMembershipVersion);

public interface IKanbanWorkspace
{
    Task<KanbanConfiguration> ReadConfigurationAsync(Subject subject, CancellationToken ct);
    Task<Guid> SavePipelineAsync(Subject subject, SaveKanbanPipeline c, string correlationId, CancellationToken ct);
    Task<KanbanCommandResult> MoveAsync(Subject subject, MoveKanbanCard c, string correlationId, CancellationToken ct);
    Task<KanbanCommandResult> AddAsync(Subject subject, AddKanbanCase c, string correlationId, CancellationToken ct);
    Task<IReadOnlyList<KanbanCandidate>> CandidatesAsync(Subject subject, Guid pipelineId, string text, CancellationToken ct);
    Task<KanbanBoardView> ReadBoardAsync(Subject subject, Guid pipelineId, IReadOnlyDictionary<Guid, string>? cursors, CancellationToken ct);
    Task<KanbanBoardView> ReadBoardAsync(Subject subject, Guid pipelineId, Guid? assigneeId,
        IReadOnlyDictionary<Guid, string>? cursors, CancellationToken ct) =>
        assigneeId == null
            ? ReadBoardAsync(subject, pipelineId, cursors, ct)
            : throw new NotSupportedException("Фильтр менеджера не поддержан реализацией канбана.");
}

