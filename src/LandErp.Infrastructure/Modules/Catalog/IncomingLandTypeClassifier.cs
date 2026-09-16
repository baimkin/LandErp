using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Catalog;

/// <summary>
/// Deterministic source-text classifier for Incoming. Values describe wording in Title/Description only;
/// they are not Rosreestr facts, legal land category or confirmed VRI.
/// </summary>
internal static class IncomingLandTypeClassifier
{
    public static IncomingLandType[] Classify(string? title, string? description)
    {
        string text = $"{title} {description}".ToLowerInvariant().Replace('ё', 'е');
        List<IncomingLandType> result = [];
        if (IsIzhs(text)) result.Add(IncomingLandType.Izhs);
        if (IsSnt(text)) result.Add(IncomingLandType.Snt);
        if (IsDnp(text)) result.Add(IncomingLandType.Dnp);
        if (IsLph(text)) result.Add(IncomingLandType.Lph);
        if (IsGardening(text)) result.Add(IncomingLandType.Gardening);
        if (IsKfh(text)) result.Add(IncomingLandType.Kfh);
        if (IsIndustrial(text)) result.Add(IncomingLandType.Industrial);
        if (IsExplicitOther(text)) result.Add(IncomingLandType.Other);
        return [.. result];
    }

    public static IQueryable<Listing> ApplyFilter(IQueryable<Listing> query, IReadOnlyList<IncomingLandType>? selected)
    {
        if (selected == null || selected.Count == 0) return query;
        IncomingLandType[] types = selected.Distinct().ToArray();
        bool izhs = types.Contains(IncomingLandType.Izhs);
        bool snt = types.Contains(IncomingLandType.Snt);
        bool dnp = types.Contains(IncomingLandType.Dnp);
        bool lph = types.Contains(IncomingLandType.Lph);
        bool gardening = types.Contains(IncomingLandType.Gardening);
        bool kfh = types.Contains(IncomingLandType.Kfh);
        bool industrial = types.Contains(IncomingLandType.Industrial);
        bool other = types.Contains(IncomingLandType.Other);

        return query.Where(item =>
            (izhs && (EF.Functions.ILike(item.Title ?? "", "%ижс%") || EF.Functions.ILike(item.Description ?? "", "%ижс%")
                || ((EF.Functions.ILike(item.Title ?? "", "%индивидуальн%") || EF.Functions.ILike(item.Description ?? "", "%индивидуальн%"))
                    && (EF.Functions.ILike(item.Title ?? "", "%жил%") || EF.Functions.ILike(item.Description ?? "", "%жил%")))))
            || (snt && (EF.Functions.ILike(item.Title ?? "", "%снт%") || EF.Functions.ILike(item.Description ?? "", "%снт%")
                || ((EF.Functions.ILike(item.Title ?? "", "%садов%") || EF.Functions.ILike(item.Description ?? "", "%садов%"))
                    && (EF.Functions.ILike(item.Title ?? "", "%товариществ%") || EF.Functions.ILike(item.Description ?? "", "%товариществ%")))))
            || (dnp && (EF.Functions.ILike(item.Title ?? "", "%днп%") || EF.Functions.ILike(item.Description ?? "", "%днп%")
                || ((EF.Functions.ILike(item.Title ?? "", "%дачн%") || EF.Functions.ILike(item.Description ?? "", "%дачн%"))
                    && (EF.Functions.ILike(item.Title ?? "", "%партнерств%") || EF.Functions.ILike(item.Description ?? "", "%партнерств%")))))
            || (lph && (EF.Functions.ILike(item.Title ?? "", "%лпх%") || EF.Functions.ILike(item.Description ?? "", "%лпх%")
                || ((EF.Functions.ILike(item.Title ?? "", "%личн%") || EF.Functions.ILike(item.Description ?? "", "%личн%"))
                    && (EF.Functions.ILike(item.Title ?? "", "%подсобн%") || EF.Functions.ILike(item.Description ?? "", "%подсобн%"))
                    && (EF.Functions.ILike(item.Title ?? "", "%хозяйств%") || EF.Functions.ILike(item.Description ?? "", "%хозяйств%")))))
            || (gardening && (EF.Functions.ILike(item.Title ?? "", "%садоводств%") || EF.Functions.ILike(item.Description ?? "", "%садоводств%")
                || EF.Functions.ILike(item.Title ?? "", "%садовый участок%") || EF.Functions.ILike(item.Description ?? "", "%садовый участок%")))
            || (kfh && (EF.Functions.ILike(item.Title ?? "", "%кфх%") || EF.Functions.ILike(item.Description ?? "", "%кфх%")
                || ((EF.Functions.ILike(item.Title ?? "", "%фермерск%") || EF.Functions.ILike(item.Description ?? "", "%фермерск%"))
                    && (EF.Functions.ILike(item.Title ?? "", "%хозяйств%") || EF.Functions.ILike(item.Description ?? "", "%хозяйств%")))))
            || (industrial && (EF.Functions.ILike(item.Title ?? "", "%промназнач%") || EF.Functions.ILike(item.Description ?? "", "%промназнач%")
                || EF.Functions.ILike(item.Title ?? "", "%промышленн%") || EF.Functions.ILike(item.Description ?? "", "%промышленн%")
                || EF.Functions.ILike(item.Title ?? "", "%производственн%") || EF.Functions.ILike(item.Description ?? "", "%производственн%")
                || EF.Functions.ILike(item.Title ?? "", "%складск%") || EF.Functions.ILike(item.Description ?? "", "%складск%")))
            || (other && (EF.Functions.ILike(item.Title ?? "", "%сельхозназнач%") || EF.Functions.ILike(item.Description ?? "", "%сельхозназнач%")
                || EF.Functions.ILike(item.Title ?? "", "%сельскохозяйственн%") || EF.Functions.ILike(item.Description ?? "", "%сельскохозяйственн%")
                || EF.Functions.ILike(item.Title ?? "", "%рекреац%") || EF.Functions.ILike(item.Description ?? "", "%рекреац%")
                || EF.Functions.ILike(item.Title ?? "", "%коммерческ%") || EF.Functions.ILike(item.Description ?? "", "%коммерческ%")
                || ((EF.Functions.ILike(item.Title ?? "", "%общественно%") || EF.Functions.ILike(item.Description ?? "", "%общественно%"))
                    && (EF.Functions.ILike(item.Title ?? "", "%делов%") || EF.Functions.ILike(item.Description ?? "", "%делов%"))))));
    }

    private static bool IsIzhs(string text) => text.Contains("ижс", StringComparison.Ordinal)
        || (text.Contains("индивидуальн", StringComparison.Ordinal) && text.Contains("жил", StringComparison.Ordinal));
    private static bool IsSnt(string text) => text.Contains("снт", StringComparison.Ordinal)
        || (text.Contains("садов", StringComparison.Ordinal) && text.Contains("товариществ", StringComparison.Ordinal));
    private static bool IsDnp(string text) => text.Contains("днп", StringComparison.Ordinal)
        || (text.Contains("дачн", StringComparison.Ordinal) && text.Contains("партнерств", StringComparison.Ordinal));
    private static bool IsLph(string text) => text.Contains("лпх", StringComparison.Ordinal)
        || (text.Contains("личн", StringComparison.Ordinal) && text.Contains("подсобн", StringComparison.Ordinal) && text.Contains("хозяйств", StringComparison.Ordinal));
    private static bool IsGardening(string text) => text.Contains("садоводств", StringComparison.Ordinal)
        || text.Contains("садовый участок", StringComparison.Ordinal);
    private static bool IsKfh(string text) => text.Contains("кфх", StringComparison.Ordinal)
        || (text.Contains("фермерск", StringComparison.Ordinal) && text.Contains("хозяйств", StringComparison.Ordinal));
    private static bool IsIndustrial(string text) => text.Contains("промназнач", StringComparison.Ordinal)
        || text.Contains("промышленн", StringComparison.Ordinal) || text.Contains("производственн", StringComparison.Ordinal)
        || text.Contains("складск", StringComparison.Ordinal);
    private static bool IsExplicitOther(string text) => text.Contains("сельхозназнач", StringComparison.Ordinal)
        || text.Contains("сельскохозяйственн", StringComparison.Ordinal) || text.Contains("рекреац", StringComparison.Ordinal)
        || text.Contains("коммерческ", StringComparison.Ordinal)
        || (text.Contains("общественно", StringComparison.Ordinal) && text.Contains("делов", StringComparison.Ordinal));
}
