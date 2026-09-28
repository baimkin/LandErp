using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;

namespace LandErp.Infrastructure.Modules.Procurement;

public static class CaseTimelineCommunication
{
    // Только сохранённый идентификатор связывает событие с общением.
    // Старые события без него сохраняют исходный Body без догадок.
    public static NegotiationView? Find(BusinessTimelineEntry entry, IEnumerable<CaseNegotiation> negotiations, Func<Guid,string> author)
    {
        if (entry.Kind != "Negotiation") return null;
        // Only a stored identifier proves the relationship. Legacy entries retain their body.
        var match = negotiations.FirstOrDefault(x => x.Id == entry.NegotiationId
            && x.OrganizationId == entry.OrganizationId && x.PropertyCaseId == entry.ObjectId);
        return match == null ? null : View(match, author(match.AuthorEmployeeId));
    }

    public static NegotiationView View(CaseNegotiation x, string author) => new(x.Id,x.SellerPrice,x.BuyerOffer,x.AgreedPrice,
        x.Currency,x.Channel,x.Contact,x.Outcome,x.Conditions,x.Comment,x.NextStep,x.NextStepDueAt,author,x.EffectiveAt,x.RecordedAt);

    private static string Body(CaseNegotiation x) => string.Join("\n",new[] {
        x.Channel.Length == 0 ? null : "Канал: " + x.Channel,
        x.Contact.Length == 0 ? null : "Контакт: " + x.Contact,
        x.Outcome.Length == 0 ? null : "Результат: " + x.Outcome,
        x.Comment.Length == 0 ? null : x.Comment,
        x.NextStep.Length == 0 ? null : "Следующий шаг: " + x.NextStep
    }.OfType<string>());
}
