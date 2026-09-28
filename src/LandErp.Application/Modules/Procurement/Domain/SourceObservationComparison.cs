using System.Globalization;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Collector.Contracts.V1;

namespace LandErp.Application.Modules.Procurement.Domain;

public static class SourceObservationComparison
{
    public static IReadOnlyList<SourceChangeObservation> Build(IReadOnlyList<ObservationView> observations, bool completeHistory)
    {
        Dictionary<string, SourceChangeValue> known = new(StringComparer.Ordinal);
        List<SourceChangeObservation> result = [];
        DateTimeOffset? latest = null;
        foreach (var observation in observations.OrderBy(x => x.RecordedAt).ThenBy(x => x.Id))
        {
            // Collector ignores late/equal observations for current listing facts. Never use them as an earlier value.
            if (latest != null && observation.ObservedAt <= latest) continue;
            bool first = latest == null;
            latest = observation.ObservedAt;
            var incoming = Values(observation.Data);
            List<SourceChangeField> fields = [];
            foreach (string name in observation.Changes.Distinct(StringComparer.Ordinal))
            {
                incoming.TryGetValue(name, out var after);
                known.TryGetValue(name, out var before);
                // A gap makes the previous effective value unverifiable, even if an older snapshot exists.
                if (!completeHistory || name == "контакты") before = null;
                if (!first || !completeHistory) fields.Add(new(name, before, after));
            }
            if (fields.Count > 0) result.Add(new(observation.Id, observation.ObservedAt, fields));
            // NotInspected/Absent/Empty do not erase the last known source value; photo [] means not obtained.
            foreach (var field in incoming) known[field.Key] = field.Value;
        }
        return result.OrderByDescending(x => x.ObservedAt).ToArray();
    }

    private static Dictionary<string, SourceChangeValue> Values(ListingData data)
    {
        Dictionary<string, SourceChangeValue> values = new(StringComparer.Ordinal);
        void Text(string key, TextField field) { if (field.Presence == FieldPresence.Present) values[key] = new(Text: field.Raw); }
        Text("название", data.Title); Text("местоположение", data.Location); Text("описание", data.Description);
        Text("продавец", data.SellerName); Text("кадастровый номер", data.CadastralNumber);
        if (data.Price is { Presence: FieldPresence.Present, Parsed: { } price }) values["цена"] = new(Number: decimal.Round(price, 2, MidpointRounding.ToEven));
        if (data.AreaSquareMeters is { Presence: FieldPresence.Present, Parsed: { } area }) values["площадь"] = new(Number: decimal.Round(area, 4, MidpointRounding.ToEven));
        if (data.PhotoUrls.Length > 0) values["фотографии"] = new(Photos: data.PhotoUrls.Distinct(StringComparer.Ordinal).ToArray());
        if (data.SourcePublishedAt != null) values["дата публикации"] = new(Text: data.SourcePublishedAt.Value.ToString("O", CultureInfo.InvariantCulture));
        if (data.Latitude != null && data.Longitude != null) values["координаты"] = new(Text: string.Create(CultureInfo.InvariantCulture, $"{data.Latitude:0.######}, {data.Longitude:0.######}"));
        if (data.DeclaredLandTypes.Length > 0) values["тип участка (заявлено)"] = new(Text: string.Join(", ", data.DeclaredLandTypes.Distinct().Order()));
        // Contacts are additive in ingestion, not a replacement snapshot. Do not claim removed contacts.
        if (data.Contacts.Length > 0) values["контакты"] = new(Text: string.Join("\n", data.Contacts.Select(x => x.Value).Distinct()));
        return values;
    }
}
