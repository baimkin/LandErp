using System.Globalization;
using System.Text.Json.Nodes;
using LandErp.ParserSpike.LocalCollection;

namespace LandErp.ParserSpike.Desktop;

public sealed class SettingEntry
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Bounds { get; init; }
    public required string Value { get; set; }
}
/// <summary>Readable editor for numerical settings. JSON remains a storage/export format.</summary>
public static class SettingsEditor
{
    private static readonly (string Key, string Label, string Bounds)[] Fields =
    [
        ("maxPages","Максимум страниц на поиск","1–100"),
        ("freshnessHours","Свежесть результата, часы","0–168"),
        ("resumeHours","Срок продолжения прохода, часы","0–24"),
        ("avitoTabs","Вкладки Avito","1–3"), ("cianTabs","Вкладки Cian","1–3"), ("globalTabs","Общий предел вкладок","1–6"),
        ("wheelDelta","Шаг колеса, пиксели","10–120"), ("wheelIntervalMilliseconds","Интервал колеса, мс","20–250"),
        ("scrollBatchEvents","Событий колеса в серии","1–30"), ("scrollPauseMilliseconds","Пауза между сериями, мс","0–2000"),
        ("loadWaitSeconds","Ожидание загрузки, секунды","1–30"), ("stabilitySeconds","Стабильность выдачи, секунды","1–15"),
        ("maxScrollSteps","Максимум циклов прокрутки","20–2000"),
        ("navigationTimeoutSeconds","Таймаут перехода, секунды","5–120"), ("elementTimeoutSeconds","Таймаут элемента, секунды","2–60"),
        ("pageIntervalSeconds","Интервал страниц на источник, секунды","1–60"),
        ("linkIntervalSeconds","Интервал поисков на источник, секунды","1–120")
    ];
    public static SettingEntry[] Entries(CollectionSettings settings)
    {
        JsonNode json = JsonNode.Parse(LocalJson.Write(settings))!;
        return Fields.Select(x => new SettingEntry { Key = x.Key, Label = x.Label, Bounds = x.Bounds, Value = json[x.Key]!.ToString() }).ToArray();
    }
    public static CollectionSettings Read(IEnumerable<SettingEntry> entries, string browser, ErrorPolicy errorPolicy)
    {
        JsonNode json = JsonNode.Parse(LocalJson.Write(new CollectionSettings()))!;
        foreach (SettingEntry entry in entries)
        {
            string value = entry.Value.Replace(',', '.');
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number)) throw new ArgumentException(entry.Label + ": введите число.");
            json[entry.Key] = JsonValue.Create(number);
        }
        json["browser"] = browser; json["errorPolicy"] = errorPolicy.ToString();
        CollectionSettings settings = LocalJson.Read<CollectionSettings>(json.ToJsonString()); settings.Validate(); return settings;
    }
}
