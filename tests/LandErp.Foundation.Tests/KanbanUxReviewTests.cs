using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Server.Components.Procurement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class KanbanUxReviewTests
{
    [TestMethod]
    public void NotePreviewRemovesAttachmentNodeButPreservesUserText()
    {
        var json="""
            {"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Автор написал [Файл: договор.pdf]"}]},{"type":"attachment","attrs":{"attachmentId":"11111111-1111-1111-1111-111111111111","name":"договор.pdf"}}]}
            """;
        Assert.AreEqual("Автор написал [Файл: договор.pdf]",CaseNoteDocument.PlainText(json,false));
        Assert.IsTrue(CaseNoteDocument.PlainText(json).EndsWith("[Файл: договор.pdf]",StringComparison.Ordinal));
    }

    [TestMethod]
    public void LegacyFileLinkRequiresSameCaseOrganizationActorInstantAndUniqueMatch()
    {
        var entry=new BusinessTimelineEntry {Id=Guid.NewGuid(),OrganizationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),
            ObjectType="PropertyCase",ActorEmployeeId=Guid.NewGuid(),Kind="Attachment",Body="договор.pdf",RecordedAt=DateTimeOffset.UtcNow};
        CaseAttachment File()=>new(){Id=Guid.NewGuid(),OrganizationId=entry.OrganizationId,PropertyCaseId=entry.ObjectId,
            ActorEmployeeId=entry.ActorEmployeeId,RecordedAt=entry.RecordedAt,Label=entry.Body};
        var file=File();
        Assert.AreEqual(file.Id,CaseTimelineAttachment.Find(entry,[file]));
        Assert.IsNull(CaseTimelineAttachment.Find(entry,[file,File()]));
        file.ActorEmployeeId=Guid.NewGuid();Assert.IsNull(CaseTimelineAttachment.Find(entry,[file]));
        file=File();file.OrganizationId=Guid.NewGuid();Assert.IsNull(CaseTimelineAttachment.Find(entry,[file]));
        file=File();file.PropertyCaseId=Guid.NewGuid();Assert.IsNull(CaseTimelineAttachment.Find(entry,[file]));
        file=File();file.RecordedAt=entry.RecordedAt.AddSeconds(1);Assert.IsNull(CaseTimelineAttachment.Find(entry,[file]));
        file=File();entry.Kind="Note";Assert.IsNull(CaseTimelineAttachment.Find(entry,[file]));
    }

    [TestMethod]
    public void AttachmentSummaryCarriesActualFileAndKeepsUnresolvedHistory()
    {
        var time=DateTimeOffset.UtcNow;
        var file=new ProcurementQueueV2Attachment(Guid.NewGuid(),CaseAttachmentKind.Document,"договор.pdf","","договор.pdf","application/pdf",20,StoredFileStatus.Available,time);
        var entry=new ProcurementTimelineSummary(Guid.NewGuid(),"Attachment","Добавлено вложение",file.Label,"Автор",time,null,null){Attachments=[file]};
        var row=CaseFeedFormat.From(entry);
        Assert.AreEqual(file.Id,row.Attachments.Single().Id);
        Assert.IsFalse(row.Details.Contains(file.Label));
        var unresolved=CaseFeedFormat.From(entry with{Attachments=[]});
        Assert.IsTrue(unresolved.Details.Contains(file.Label));
        Assert.IsTrue(unresolved.Details.Any(x=>x.Contains("не определена",StringComparison.Ordinal)));
    }
}
