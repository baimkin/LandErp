using System.Globalization;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Workflow.Domain;

namespace LandErp.Server.Components.Procurement;

public sealed record CaseFeedRow(Guid Id, DateTimeOffset Date, string DateLabel, string DateHint,
    string Text, IReadOnlyList<string> Details, string Type, string Author, string FullAuthor)
{
    public Guid? CaseId { get; init; }
    public Guid? TaskId { get; init; }
    public string? TaskTitle { get; init; }
    public IReadOnlyList<ProcurementQueueV2Attachment> Attachments { get; init; } = [];
}

public static class CaseFeedFormat
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    public static CaseFeedRow From(NegotiationView value)
    {
        string main = First(value.Outcome,value.Comment,value.Conditions,"Общение");
        List<string> details = [];
        Add(details,main,value.Conditions,"Условия: ");
        if (value.Comment.Trim()!=value.Conditions.Trim()) Add(details,main,value.Comment,"");
        if (!string.IsNullOrWhiteSpace(value.Contact) && value.Contact.Trim() != "Продавец")
            details.Add("Собеседник — " + value.Contact);
        string prices = string.Join(" · ",new[] { Price("Продавец",value.SellerPrice,value.Currency),
            Price("Предложили",value.BuyerOffer,value.Currency),Price("Согласовали",value.AgreedPrice,value.Currency) }.OfType<string>());
        if (prices.Length > 0) details.Add(prices);
        Add(details,main,value.NextStep,"Следующий шаг: ");
        if (value.NextStepDueAt != null) details.Add("Срок следующего шага: " + ProcurementLabels.Time(value.NextStepDueAt));
        return Row(value.Id,value.EffectiveAt,value.RecordedAt,main,details,Channel(value.Channel),value.Author,
            string.IsNullOrWhiteSpace(value.Contact) ? null : "Собеседник: " + value.Contact)
            with { Attachments=value.Attachments, CaseId=value.CaseId, TaskId=value.TaskId, TaskTitle=value.TaskTitle };
    }

    public static CaseFeedRow From(ProcurementNegotiationHistoryItem value) => From(new NegotiationView(value.Id,
        value.SellerPrice,value.BuyerOffer,value.AgreedPrice,value.Currency,value.Channel,value.Contact,value.Outcome,
        value.Conditions,value.Comment,value.NextStep,value.NextStepDueAt,value.Author,value.EffectiveAt,value.RecordedAt))
        with { Attachments = value.Attachments, CaseId=value.CaseId, TaskId=value.TaskId, TaskTitle=value.TaskTitle };

    public static CaseFeedRow From(ProcurementQueueV2Negotiation value) => From(value.Communication ?? new NegotiationView(
        value.Id,value.SellerPrice,value.BuyerOffer,value.AgreedPrice,value.Currency,value.Channel,"",value.Outcome,"",value.Comment,
        "",null,"",value.EffectiveAt,value.EffectiveAt));

    public static CaseFeedRow From(ProcurementTimelineSummary value) => From(new TimelineItem(value.Id,value.Kind,value.Title,
        value.Body,value.Actor,null,value.RecordedAt,value.EffectiveAt,value.DueAt) { Communication=value.Communication, CaseId=value.CaseId, TaskId=value.TaskId, Attachments=value.Attachments });

    public static CaseFeedRow From(TimelineItem value)
    {
        if (value.Communication != null) return From(value.Communication) with { Id=value.Id };
        string type = value.Kind switch {
            "Note" => "Заметка", "Contact" or "Negotiation" => "Общение",
            "CaseTaskSave" or "CaseTaskComplete" or "CaseTaskDelete" or "NextActionChanged" => "Задача",
            "Check" => "Проверка", "Document" or "Attachment" => "Документы",
            "Inspection" or "InspectionAssigned" => "Осмотр",
            "SourceLinked" or "SourceUnlinked" or "SourceRelinked" => "Источник",
            "FactVerified" or "FactCorrected" => "Сведения", _ => "Статус" };
        bool plain = value.Kind is "Note" or "Contact";
        string main = plain ? First(value.Body,value.Title) : First(value.Title,value.Body);
        List<string> details = [];
        if (type == "Задача" && !string.IsNullOrWhiteSpace(value.Body))
        {
            int newline = value.Body.IndexOf('\n');
            string title = newline < 0 ? value.Body : value.Body[..newline];
            main = value.Title.Trim() == title.Trim() ? title : value.Title + ": " + title;
            if (newline >= 0) Add(details,main,value.Body[(newline+1)..],"");
        }
        else if (value.Kind == "Negotiation" && !string.IsNullOrWhiteSpace(value.Body))
        {
            // Без надёжной структуры оставляем исходный Body целиком. Только буквальный
            // повтор заголовка в строке результата не выводим второй раз.
            main = value.Body;
            if (!value.Body.Split('\n').Any(x=>x==value.Title || x=="Результат: "+value.Title))
                Add(details,main,value.Title,"");
        }
        else if (!plain && value.Attachments.Count==0) Add(details,main,value.Body,"");
        if(value.Kind=="Attachment" && value.Attachments.Count==0)details.Add("Ссылка на файл не определена. Проверьте вложения объекта.");
        if (value.Target != null) details.Add("Исполнитель: " + value.Target);
        if (value.DueAt != null) details.Add("Срок: " + ProcurementLabels.Time(value.DueAt));
        return Row(value.Id,value.EffectiveAt??value.RecordedAt,value.RecordedAt,main,details,type,value.Actor) with { CaseId=value.CaseId, TaskId=value.TaskId, TaskTitle="Открыть задачу", Attachments=value.Attachments };
    }

    public static CaseFeedRow From(LandErp.Application.Modules.Catalog.Contracts.CatalogEventView value, string title)
    {
        List<string> details=[];
        if(value.PreviousObservedPrice is decimal before && value.ObservedPrice is decimal after && before!=after)
        {
            string changes="Изменения:\nЦена: "+Primitives.NumberFormat.Money(before)+" ₽ → "+Primitives.NumberFormat.Money(after)+" ₽";
            if(value.PreviousObservedPricePerSotka is decimal oldUnit && value.ObservedPricePerSotka is decimal newUnit && oldUnit!=newUnit)
                changes+="\nЦена за сотку: "+Primitives.NumberFormat.Money(oldUnit)+" ₽ → "+Primitives.NumberFormat.Money(newUnit)+" ₽";
            details.Add(changes);
        }
        Add(details,title,value.Message,"");
        // The catalog event contract has no author; do not infer one from the current viewer.
        return Row(value.Id,value.RecordedAt,value.RecordedAt,title,details,"Объявление","");
    }

    public static string ShortAuthor(string value)
    {
        string[] words = value.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        return words.Length < 2 ? value : string.Join(" ",words[..^1].Select(x=>StringInfo.GetNextTextElement(x)+".")) + " " + words[^1];
    }
    private static CaseFeedRow Row(Guid id, DateTimeOffset date, DateTimeOffset recorded, string text,
        IReadOnlyList<string> details,string type,string author,string? extraHint=null) => new(id,date,
            TimeZoneInfo.ConvertTime(date,WorkTaskDeadline.Moscow).ToString("dd.MM HH:mm",Russian),
            "Действие: " + FullDate(date) + " · Записано: " + FullDate(recorded) + (extraHint==null?"":" · "+extraHint),
            text,details,type,ShortAuthor(author),author);
    private static string FullDate(DateTimeOffset date) => TimeZoneInfo.ConvertTime(date,WorkTaskDeadline.Moscow).ToString("dd.MM.yyyy HH:mm:ss 'МСК'",Russian);
    private static string First(params string[] values) => values.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))??"";
    private static void Add(List<string> target,string main,string value,string prefix)
    {
        // Подавляем только буквальное равенство целого поля, без поиска похожих фраз.
        if (!string.IsNullOrWhiteSpace(value) && value.Trim()!=main.Trim()
            && !target.Any(x=>x==value || x==prefix+value)) target.Add(prefix+value);
    }
    private static string? Price(string label,decimal? value,string currency) => value==null?null
        : label+": "+LandErp.Server.Components.Primitives.NumberFormat.Money(value.Value)+" "+(currency=="RUB"?"₽":currency);
    private static string Channel(string value) => value switch {
        "Телефон" or "Звонок" => "Звонок", "Сообщение" or "Мессенджер" => "Переписка",
        "" => "Общение", _ => value };
}
