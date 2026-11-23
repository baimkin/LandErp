using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using System.Globalization;

namespace LandErp.Server.Components.Procurement;

public sealed class InspectionAnswerDraft
{
    public Guid Id { get; set; }
    public long Version { get; set; }
    public string Title { get; set; } = "";
    public int SortOrder { get; set; }
    public InspectionAnswerType AnswerType { get; set; }
    public string[] Options { get; set; } = [];
    public string Unit { get; set; } = "";
    public bool AllowAttachments { get; set; }
    public bool Required { get; set; }
    public InspectionItemStatus Status { get; set; }
    public string Answer { get; set; } = "";
    public string Note { get; set; } = "";
    public InspectionAnswerDraft() { }
    public InspectionAnswerDraft(InspectionItemView item)
    {
        Id=item.Id; Version=item.Version; Title=item.Title; SortOrder=item.SortOrder;
        AnswerType=item.AnswerType; Options=item.Options; Unit=item.Unit;
        AllowAttachments=item.AllowAttachments; Required=item.Required;
        Status=item.Status; Answer=item.Answer; Note=item.Note;
    }
}

public sealed record InspectionDraft(Guid InspectionId, long Version, string Conclusion,
    string Decision, List<InspectionAnswerDraft> Answers)
{
    // A separate immutable content baseline is essential: a row version alone cannot identify edited fields.
    public InspectionDraft? Baseline { get; init; }
    public string? Revision { get; set; }
    public string[] MergedRevisions { get; init; } = [];
    public static InspectionDraft From(InspectionView view) => new(view.Id,view.Version,
        view.OverallConclusion,view.PreliminaryDecision,view.Items.Select(x=>new InspectionAnswerDraft(x)).ToList());
}

public sealed record InspectionFieldValue(string Text, InspectionItemStatus? Status = null);
public sealed record InspectionFieldConflict(string Key, string Title, InspectionFieldValue[] Choices, bool Legacy);
public sealed record InspectionMergeResult(InspectionDraft Draft, List<InspectionFieldConflict> Conflicts);

public static class InspectionDraftMerge
{
    private static string Normalize(string value) => value.Replace("\r\n","\n",StringComparison.Ordinal).Trim();
    private static bool Same(InspectionFieldValue a, InspectionFieldValue b) =>
        a.Status==b.Status && Normalize(a.Text)==Normalize(b.Text);
    private static string CanonicalAnswer(InspectionAnswerDraft item,InspectionAnswerType type)
    {
        if(item.Status==InspectionItemStatus.NotChecked)return "";
        string value=Normalize(item.Answer);
        if(type is InspectionAnswerType.Number or InspectionAnswerType.Percentage)
        {
            const NumberStyles styles=NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint;
            if(decimal.TryParse(value,styles,CultureInfo.InvariantCulture,out var number)
                ||decimal.TryParse(value,styles,CultureInfo.GetCultureInfo("ru-RU"),out number))return number.ToString("G29",CultureInfo.InvariantCulture);
        }
        return value;
    }
    public static bool Matches(InspectionDraft draft,InspectionView server)=>
        Normalize(draft.Conclusion)==Normalize(server.OverallConclusion)&&Normalize(draft.Decision)==Normalize(server.PreliminaryDecision)
        &&draft.Answers.Count==server.Items.Count&&draft.Answers.All(a=>server.Items.Any(b=>a.Id==b.Id&&a.Status==b.Status
            &&CanonicalAnswer(a,b.AnswerType)==Normalize(b.Answer)&&Normalize(a.Note)==Normalize(b.Note)));

    public static InspectionMergeResult Merge(InspectionView server, IEnumerable<InspectionDraft> drafts)
    {
        var all=drafts.Where(x=>x.InspectionId==server.Id).ToArray();
        var incorporated=all.SelectMany(x=>x.MergedRevisions).ToHashSet(StringComparer.Ordinal);
        var active=all.Where(x=>x.Revision==null||!incorporated.Contains(x.Revision)).ToArray();
        if(active.Any(x=>x.Answers.Any(a=>!server.Items.Any(s=>s.Id==a.Id))))
            throw new ArgumentException("Состав осмотра изменился. Ответы сохранены на устройстве; сначала проверьте назначение.");
        var target=InspectionDraft.From(server) with
        {
            Baseline=InspectionDraft.From(server),
            MergedRevisions=all.SelectMany(x=>x.MergedRevisions.Concat(x.Revision is {} r?[r]:[])).Distinct(StringComparer.Ordinal).ToArray()
        };
        List<InspectionFieldConflict> conflicts=[];
        InspectionFieldValue Field(string key,string title,InspectionFieldValue remote,
            Func<InspectionDraft,InspectionFieldValue?> read,Func<InspectionDraft,bool> compatible)
        {
            List<InspectionFieldValue> candidates=[];
            bool legacy=false;
            foreach(var draft in active)
            {
                var local=read(draft);
                if(local==null||Same(local,remote))continue; // Already saved elsewhere is not another competing edit.
                var original=draft.Baseline is {} baseline?read(baseline):compatible(draft)?remote:null;
                if(original!=null&&Same(local,original))continue;
                candidates.Add(local);
                if(original==null){legacy=true;candidates.Add(remote);}
                else if(!Same(remote,original))candidates.Add(remote);
            }
            var unique=new List<InspectionFieldValue>();
            foreach(var candidate in candidates)if(!unique.Any(x=>Same(x,candidate)))unique.Add(candidate);
            if(unique.Count>1)conflicts.Add(new(key,title,unique.ToArray(),legacy));
            return unique.FirstOrDefault()??remote;
        }
        target=target with
        {
            Conclusion=Field("conclusion","Общий вывод",new(server.OverallConclusion),x=>new(x.Conclusion),x=>x.Version==server.Version).Text,
            Decision=Field("decision","Предварительный вывод",new(server.PreliminaryDecision),x=>new(x.Decision),x=>x.Version==server.Version).Text
        };
        foreach(var item in target.Answers)
        {
            InspectionFieldValue? Answer(InspectionDraft x) => x.Answers.FirstOrDefault(a=>a.Id==item.Id) is {} a?new(CanonicalAnswer(a,item.AnswerType),a.Status):null;
            InspectionFieldValue? Note(InspectionDraft x) => x.Answers.FirstOrDefault(a=>a.Id==item.Id) is {} a?new(a.Note):null;
            bool Compatible(InspectionDraft x) => x.Answers.FirstOrDefault(a=>a.Id==item.Id)?.Version==item.Version;
            var answer=Field($"answer:{item.Id}",item.Title,new(item.Answer,item.Status),Answer,Compatible);
            item.Answer=answer.Text;item.Status=answer.Status!.Value;
            item.Note=Field($"note:{item.Id}",$"{item.Title} — комментарий",new(item.Note),Note,Compatible).Text;
        }
        return new(target,conflicts);
    }

    public static void Choose(InspectionDraft draft,string key,InspectionFieldValue value)
    {
        // Scalar record fields are applied by the page; answer and status always move together.
        var parts=key.Split(':');
        if(parts.Length!=2)return;
        var item=draft.Answers.Single(x=>x.Id==Guid.Parse(parts[1]));
        if(parts[0]=="answer"){item.Answer=value.Text;item.Status=value.Status!.Value;}
        else item.Note=value.Text;
    }
}
