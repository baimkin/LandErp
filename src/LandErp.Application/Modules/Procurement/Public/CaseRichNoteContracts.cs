using LandErp.Application.Modules.Procurement.Domain;

namespace LandErp.Application.Modules.Procurement.Contracts;

public sealed record SaveCaseRichNote(Guid CaseId, CaseNoteSection Section, long ExpectedVersion, string DocumentJson);
public sealed record CaseRichNoteView(CaseNoteSection Section, string DocumentJson, string SafeHtml,
    long Version, string UpdatedBy, DateTimeOffset UpdatedAt);
