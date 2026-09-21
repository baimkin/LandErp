using System.Globalization;

namespace LandErp.ParserSpike.LocalCollection;

/// <summary>Source-neutral text inference. Structured source facts remain separate and never overwrite this evidence.</summary>
public static class LandTypeClassifier
{
    public static LandType[] Infer(string? title, string? description)
    {
        string text = $"{title} {description}".ToLowerInvariant().Replace('ё', 'е');
        List<LandType> result = [];
        if (text.Contains("ижс", StringComparison.Ordinal)
            || (text.Contains("индивидуальн", StringComparison.Ordinal) && text.Contains("жил", StringComparison.Ordinal))) result.Add(LandType.Izhs);
        if (text.Contains("снт", StringComparison.Ordinal)
            || (text.Contains("садов", StringComparison.Ordinal) && text.Contains("товариществ", StringComparison.Ordinal))) result.Add(LandType.Snt);
        if (text.Contains("днп", StringComparison.Ordinal)
            || (text.Contains("дачн", StringComparison.Ordinal) && text.Contains("партнерств", StringComparison.Ordinal))) result.Add(LandType.Dnp);
        if (text.Contains("лпх", StringComparison.Ordinal)
            || (text.Contains("личн", StringComparison.Ordinal) && text.Contains("подсобн", StringComparison.Ordinal)
                && text.Contains("хозяйств", StringComparison.Ordinal))) result.Add(LandType.Lph);
        if (text.Contains("садоводств", StringComparison.Ordinal) || text.Contains("садовый участок", StringComparison.Ordinal)) result.Add(LandType.Gardening);
        if (text.Contains("кфх", StringComparison.Ordinal)
            || (text.Contains("фермерск", StringComparison.Ordinal) && text.Contains("хозяйств", StringComparison.Ordinal))) result.Add(LandType.Kfh);
        if (text.Contains("промназнач", StringComparison.Ordinal) || text.Contains("промышленн", StringComparison.Ordinal)
            || text.Contains("производственн", StringComparison.Ordinal) || text.Contains("складск", StringComparison.Ordinal)) result.Add(LandType.Industrial);
        if (text.Contains("сельхозназнач", StringComparison.Ordinal) || text.Contains("сельскохозяйственн", StringComparison.Ordinal)
            || text.Contains("рекреац", StringComparison.Ordinal) || text.Contains("коммерческ", StringComparison.Ordinal)
            || (text.Contains("общественно", StringComparison.Ordinal) && text.Contains("делов", StringComparison.Ordinal))) result.Add(LandType.Other);
        return result.Distinct().ToArray();
    }
}

public static class LandTypeLabels
{
    public static string Label(LandType value) => value switch
    {
        LandType.Izhs => "ИЖС",
        LandType.Snt => "СНТ",
        LandType.Dnp => "ДНП",
        LandType.Lph => "ЛПХ",
        LandType.Gardening => "Садоводство",
        LandType.Kfh => "КФХ",
        LandType.Industrial => "Промназначение",
        _ => "Другое"
    };
    public static string Format(LandType[] values)
    {
        string[] labels = values.Distinct().Select(Label).ToArray();
        return labels.Length == 0 ? "—" : string.Join(", ", labels);
    }
}
