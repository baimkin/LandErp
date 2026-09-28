using LandErp.Application.Modules.Procurement.Domain;

namespace LandErp.Application.Modules.Procurement.Contracts;

public sealed record SaveCaseRichNote(Guid CaseId, CaseNoteSection Section, long ExpectedVersion, string DocumentJson, Guid? NoteId = null);
public sealed record CaseRichNoteView(CaseNoteSection Section, string DocumentJson, string SafeHtml,
    long Version, string UpdatedBy, DateTimeOffset UpdatedAt)
{
    public Guid Id { get; init; }
}
public sealed record DeleteCaseRichNote(Guid CaseId, Guid NoteId, long ExpectedVersion);
public sealed record ReassignCaseManager(Guid CaseId, long ExpectedCaseVersion, Guid EmployeeId);
