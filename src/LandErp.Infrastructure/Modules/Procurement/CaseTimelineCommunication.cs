using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;

namespace LandErp.Infrastructure.Modules.Procurement;

public static class CaseTimelineCommunication
{
    // Старый timeline не хранит NegotiationId. Только однозначное полное совпадение
    // фактов автора/времени/содержания позволяет дополнить отображение структурой.
    // Никакие события не удаляются; при неоднозначности UI получает исходный Body.
    public static NegotiationView? Find(BusinessTimelineEntry entry, IEnumerable<CaseNegotiation> negotiations, Func<Guid,string> author)
    {
        if (entry.Kind != "Negotiation") return null;
        var matches = negotiations.Where(x => x.OrganizationId == entry.OrganizationId && x.PropertyCaseId == entry.ObjectId
            && x.AuthorEmployeeId == entry.ActorEmployeeId && x.RecordedAt == entry.RecordedAt
            && x.EffectiveAt == entry.EffectiveAt && x.NextStepDueAt == entry.DueAt
            && Body(x) == entry.Body).Take(2).ToArray();
        return matches.Length == 1 ? View(matches[0], author(matches[0].AuthorEmployeeId)) : null;
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
