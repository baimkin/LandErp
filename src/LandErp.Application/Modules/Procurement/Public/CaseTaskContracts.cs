using LandErp.Application.Modules.Workflow.Domain;

namespace LandErp.Application.Modules.Procurement.Contracts;

public sealed record CaseTaskView(Guid Id, string Title, string Description, WorkTaskType Type,
    Guid EmployeeId, string EmployeeName, DateTimeOffset? DueAt, bool DueHasTime, bool Completed, bool Overdue, long Version);
public sealed record CaseTasksView(long CaseVersion, Guid DefaultEmployeeId, bool CanEdit,
    IReadOnlyList<DecisionTarget> Assignees, IReadOnlyList<CaseTaskView> Tasks);
public enum CaseTaskAction { Save, Complete, Delete }
public sealed record ChangeCaseTask(Guid CaseId, long ExpectedCaseVersion, Guid? TaskId, long ExpectedTaskVersion,
    CaseTaskAction Action, string Title, string Description, WorkTaskType Type,
    Guid EmployeeId, DateTimeOffset? DueAt, bool DueHasTime, Guid CommandId);
