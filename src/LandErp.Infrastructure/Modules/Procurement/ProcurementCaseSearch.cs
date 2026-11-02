using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

internal static class ProcurementCaseSearch
{
    // Both views intersect this predicate with authorized cases before pagination and totals.
    internal static IQueryable<PropertyCase> Apply(IQueryable<PropertyCase> cases, LandErpDbContext db, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return cases;
        string pattern = $"%{text}%";
        Guid? exactId = Guid.TryParse(text, out Guid parsedId) ? parsedId : null;
        return cases.Where(item => EF.Functions.ILike(item.BusinessNumber, pattern)
            || EF.Functions.ILike(item.WorkingTitle, pattern)
            || (item.WorkingLocation != null && EF.Functions.ILike(item.WorkingLocation, pattern))
            || (item.CadastralNumber != null && EF.Functions.ILike(item.CadastralNumber, pattern))
            || (exactId.HasValue && item.Id == exactId.Value)
            || db.CaseNegotiations.Any(contact => contact.OrganizationId == item.OrganizationId
                && contact.PropertyCaseId == item.Id && EF.Functions.ILike(contact.Contact, pattern))
            || db.PropertyCaseSourceLinks.Any(link => link.OrganizationId == item.OrganizationId
                && link.PropertyCaseId == item.Id && link.Confirmed
                && db.Listings.Any(source => source.OrganizationId == item.OrganizationId && source.Id == link.CatalogItemId
                    && ((source.ExternalId != null && EF.Functions.ILike(source.ExternalId, pattern))
                        || (source.Title != null && EF.Functions.ILike(source.Title, pattern))
                        || (source.SellerName != null && EF.Functions.ILike(source.SellerName, pattern))
                        || (exactId.HasValue && source.Id == exactId.Value)))));
    }
}
