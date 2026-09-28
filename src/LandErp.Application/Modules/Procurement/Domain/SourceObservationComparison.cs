using System.Globalization;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Collector.Contracts.V1;

namespace LandErp.Application.Modules.Procurement.Domain;

public static class SourceObservationComparison
{
    public static IReadOnlyList<SourceChangeObservation> Build(
        IReadOnlyList<ObservationView> observations,
        bool completeHistory,
        IReadOnlyDictionary<string, long>? photoFingerprints = null,
        int photoHammingDistance = 0)
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
                if (!first || !completeHistory)
                {
                    if (name == "фотографии" && before?.Photos is { } beforePhotos && after?.Photos is { } afterPhotos)
                    {
                        PhotoDiff diff = ComparePhotos(beforePhotos, afterPhotos, photoFingerprints, photoHammingDistance);
                        if (diff.Removed.Length == 0 && diff.Added.Length == 0) continue;
                        fields.Add(new(name, before, after, diff.Removed, diff.Added));
                    }
                    else
                    {
                        fields.Add(new(name, before, after));
                    }
                }
            }
            if (fields.Count > 0) result.Add(new(observation.Id, observation.ObservedAt, fields));
            // NotInspected/Absent/Empty do not erase the last known source value; photo [] means not obtained.
            foreach (var field in incoming) known[field.Key] = field.Value;
        }
        return result.OrderByDescending(x => x.ObservedAt).ToArray();
    }

    private static PhotoDiff ComparePhotos(
        string[] before,
        string[] after,
        IReadOnlyDictionary<string, long>? photoFingerprints,
        int photoHammingDistance)
    {
        bool[] matchedBefore = new bool[before.Length];
        bool[] matchedAfter = new bool[after.Length];

        // Stable URLs are an exact identity shortcut and also make pure reordering a no-op.
        for (int beforeIndex = 0; beforeIndex < before.Length; beforeIndex++)
        {
            for (int afterIndex = 0; afterIndex < after.Length; afterIndex++)
            {
                if (matchedAfter[afterIndex]
                    || !string.Equals(before[beforeIndex], after[afterIndex], StringComparison.Ordinal))
                    continue;
                matchedBefore[beforeIndex] = true;
                matchedAfter[afterIndex] = true;
                break;
            }
        }

        if (photoFingerprints != null)
        {
            List<(int Index, long Hash)> left = [];
            List<(int Index, long Hash)> right = [];
            for (int index = 0; index < before.Length; index++)
                if (!matchedBefore[index] && photoFingerprints.TryGetValue(before[index], out long hash))
                    left.Add((index, hash));
            for (int index = 0; index < after.Length; index++)
                if (!matchedAfter[index] && photoFingerprints.TryGetValue(after[index], out long hash))
                    right.Add((index, hash));

            IReadOnlyList<PhotoFingerprintPair> pairs = PhotoFingerprintMatching.Match(
                left.Select(item => item.Hash).ToArray(),
                right.Select(item => item.Hash).ToArray(),
                photoHammingDistance);
            foreach (PhotoFingerprintPair pair in pairs)
            {
                matchedBefore[left[pair.Left].Index] = true;
                matchedAfter[right[pair.Right].Index] = true;
            }
        }

        return new(
            before.Where((_, index) => !matchedBefore[index]).ToArray(),
            after.Where((_, index) => !matchedAfter[index]).ToArray());
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

    private readonly record struct PhotoDiff(string[] Removed, string[] Added);
}
