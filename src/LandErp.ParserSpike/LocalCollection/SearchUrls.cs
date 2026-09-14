namespace LandErp.ParserSpike.LocalCollection;

public sealed record NormalizedSearch(string Url, string Key, SourceSite Source, string[] Warnings);

public static class SearchUrls
{
    private static readonly HashSet<string> Tracking = new(StringComparer.OrdinalIgnoreCase)
    { "context", "source", "source_search", "source_string", "src", "iid", "cd", "from", "ref", "utm_source", "utm_medium", "utm_campaign", "utm_content", "utm_term" };
    private static readonly System.Text.RegularExpressions.Regex Sensitive = new(
        @"(?:token|password|passwd|secret|authorization|cookie|session|csrf|jwt|api[_-]?key|auth[_-]?key)|^(?:auth|sid|key|signature)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static bool IsPublic(Uri uri, SourceSite source) => uri.Scheme == "https" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.Host.Equals(source == SourceSite.Avito ? "avito.ru" : "cian.ru", StringComparison.OrdinalIgnoreCase)
        || uri.Host.EndsWith(source == SourceSite.Avito ? ".avito.ru" : ".cian.ru", StringComparison.OrdinalIgnoreCase));

    public static NormalizedSearch Normalize(string value, SourceSite? manualSource = null)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)) throw new ArgumentException("Введите HTTPS-ссылку выдачи Avito или Cian.");
        SourceSite source = IsPublic(uri, SourceSite.Avito) ? SourceSite.Avito : IsPublic(uri, SourceSite.Cian)
            ? SourceSite.Cian : throw new ArgumentException("Разрешены только публичные HTTPS-ссылки Avito/Cian без пароля и фрагмента.");
        if (manualSource.HasValue && manualSource != source) throw new ArgumentException("Выбранный источник не соответствует ссылке.");
        List<KeyValuePair<string, string>> filters = [];
        List<string> warnings = [];
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            string data = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            if (Tracking.Contains(key) || key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) continue;
            if (Sensitive.IsMatch(key)) throw new ArgumentException("Ссылка содержит параметр авторизации. Используйте публичный адрес без секретов.");
            if (key.Length > 200 || key.Any(char.IsControl)) throw new ArgumentException("Некорректное имя параметра ссылки.");
            if (key == "p") { warnings.Add("Параметр страницы исключён из идентификатора поиска; сбор начинается с первой страницы."); continue; }
            if (data.Length > 8000 || data.Any(char.IsControl)) throw new ArgumentException("FILTER_VALUE_INVALID");
            filters.Add(new(key, data));
        }
        string query = string.Join('&', filters.OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Value, StringComparer.Ordinal)
            .Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        string result = uri.GetLeftPart(UriPartial.Path) + (query.Length == 0 ? "" : "?" + query);
        if (IsDetail(result, source)) warnings.Add("Ссылка сохранена. Парсинг отдельного объявления пока не поддерживается; очередь сообщит об этом явно.");
        return new(result, result, source, warnings.ToArray());
    }

    /// <summary>Checkpoint URLs came from DOM. Retain their page number after applying the same safe filter rules.</summary>
    public static string SafePage(string value, SourceSite source)
    {
        NormalizedSearch search = Normalize(value, source);
        Uri uri = new(value);
        string? page = uri.Query.TrimStart('?').Split('&').FirstOrDefault(x => x.StartsWith("p=", StringComparison.Ordinal));
        if (page is null) return search.Url;
        if (!int.TryParse(page.AsSpan(2), out int number) || number < 1 || number > 10000) throw new ArgumentException("PAGE_NUMBER_INVALID");
        return search.Url + (new Uri(search.Url).Query.Length == 0 ? "?" : "&") + "p=" + number;
    }
    public static int PageNumber(string value)
    {
        string? p = new Uri(value).Query.TrimStart('?').Split('&').FirstOrDefault(x => x.StartsWith("p=", StringComparison.Ordinal));
        return p is not null && int.TryParse(p.AsSpan(2), out int n) ? n : 1;
    }
    public static bool SameSearch(string current, string next, SourceSite source)
    {
        Uri a = new(Normalize(current, source).Url), b = new(Normalize(next, source).Url);
        if (a.AbsolutePath != b.AbsolutePath && !(source == SourceSite.Avito && AvitoCategoryAlias(a.AbsolutePath, b.AbsolutePath))) return false;
        static string Host(Uri uri) => uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;
        if (source == SourceSite.Avito && Host(a) != Host(b)) return false;
        static string[] Terms(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        // An omitted localPriority=0 is the same default, not a change of the search region.
        string[] left = Terms(a).Where(x => x != "localPriority=0").ToArray(), right = Terms(b).Where(x => x != "localPriority=0").ToArray();
        if (source == SourceSite.Cian)
        {
            // Cian's provided DOM omits redundant location[0]=region on pagination links.
            string? region = left.FirstOrDefault(x => x.StartsWith("region=", StringComparison.Ordinal));
            if (region is not null && right.Contains(region, StringComparer.Ordinal))
            {
                string regionValue = region["region=".Length..];
                left = left.Where(x => !(x.StartsWith("location%5B", StringComparison.Ordinal) && x.EndsWith("=" + regionValue, StringComparison.Ordinal))).ToArray();
                right = right.Where(x => !(x.StartsWith("location%5B", StringComparison.Ordinal) && x.EndsWith("=" + regionValue, StringComparison.Ordinal))).ToArray();
            }
        }
        return left.SequenceEqual(right, StringComparer.Ordinal);
    }
    private static bool AvitoCategoryAlias(string first, string second)
    {
        string[] a = first.Trim('/').Split('/'), b = second.Trim('/').Split('/');
        return a.Length == 2 && b.Length == 2 && a[0] == b[0]
            && ((a[1] == "nedvizhimost" && b[1] == "zemelnye_uchastki") || (b[1] == "nedvizhimost" && a[1] == "zemelnye_uchastki"));
    }
    public static bool IsDetail(string url, SourceSite source) => System.Text.RegularExpressions.Regex.IsMatch(new Uri(url).AbsolutePath,
        source == SourceSite.Avito ? @"_\d{5,}/?$" : @"^/(?:sale|rent)/[^/]+/\d+/?$");

    /// <summary>Diagnostics never echo opaque query values: keep useful filter names, show only established public filters.</summary>
    public static string DiagnosticUrl(string? url)
    {
        if (url is null) return "";
        try
        {
            NormalizedSearch safe = Normalize(url); Uri uri = new(SafePage(url, safe.Source));
            string[] visible = ["q","s","p","f","region","deal_type","offer_type","engine_version","localPriority","bbox","center","zoom","maxmcad","land_status","object_type","location","minprice","maxprice"];
            string query = string.Join('&', uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair =>
            {
                string[] parts = pair.Split('=', 2);
                string key = Uri.UnescapeDataString(parts[0]).Split('[')[0];
                return visible.Contains(key, StringComparer.Ordinal) ? pair : parts[0] + "=REDACTED";
            }));
            return uri.GetLeftPart(UriPartial.Path) + (query.Length == 0 ? "" : "?" + query);
        }
        catch (ArgumentException) { return "URL_REDACTED"; }
    }
}
