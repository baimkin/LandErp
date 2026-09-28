using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;

namespace LandErp.Infrastructure.Modules.Procurement;

public static class CaseTimelineAttachment
{
    // Legacy events have no attachment FK. Upload writes both records with the same
    // instant and actor. Never infer a link from a filename alone or choose among ties.
    public static Guid? Find(BusinessTimelineEntry entry, IEnumerable<CaseAttachment> attachments)
    {
        if(entry.Kind!="Attachment" || entry.ObjectType!="PropertyCase")return null;
        var candidates=attachments.Where(a=>a.OrganizationId==entry.OrganizationId && a.PropertyCaseId==entry.ObjectId
            && a.ActorEmployeeId==entry.ActorEmployeeId && a.RecordedAt==entry.RecordedAt && a.Label==entry.Body).Take(2).ToArray();
        return candidates.Length==1?candidates[0].Id:null;
    }
}
