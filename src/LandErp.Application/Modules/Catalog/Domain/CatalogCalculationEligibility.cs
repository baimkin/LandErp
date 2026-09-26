using System.Linq.Expressions;

namespace LandErp.Application.Modules.Catalog.Domain;

public static class CatalogCalculationEligibility
{
    // One expression supplies both SQL eligibility and the human-readable M-01 reason.
    private static readonly Expression<Func<Listing, string?>> ReasonExpression = item =>
        item.Disposition == CatalogDisposition.Duplicate ? "Подтверждённый дубль" :
        item.Disposition == CatalogDisposition.Fake ? "Фейк" :
        item.Disposition == CatalogDisposition.RemovedAtSource ? "Снято у источника" :
        item.Price == null || item.Price <= 0 ? "Нужна положительная цена" :
        item.AreaSquareMeters == null || item.AreaSquareMeters <= 0 ? "Нужна положительная площадь" :
        item.Currency != "RUB" ? "Для расчёта нужна цена в RUB" : null;
    private static readonly Func<Listing, string?> GetReason = ReasonExpression.Compile();
    public static Expression<Func<Listing, bool>> Eligible { get; } = Expression.Lambda<Func<Listing, bool>>(
        Expression.Equal(ReasonExpression.Body, Expression.Constant(null, typeof(string))), ReasonExpression.Parameters);
    public static string? Reason(Listing item) => GetReason(item);
}
